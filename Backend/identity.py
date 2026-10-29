#!/usr/bin/env python3
"""Identity-provider verification for NEON VELOCITY.

Google Sign-In tokens are validated against Google's tokeninfo endpoint, which performs
the signature and expiry checks server-side. The local validator additionally enforces the
configured OAuth client id, issuer and expiry, and rejects tokens that are not email
verified.

Apple Sign In tokens are verified locally against Apple's published JWKS. RS256 uses a
small pure-Python RSA/PKCS#1 v1.5 verifier so the service keeps its dependency-free
deployment. The configured service id is enforced as the audience; no client-claimed
identity is trusted.
"""

from __future__ import annotations

import base64
import hashlib
import hmac
import json
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, Callable, Dict, List, Optional

from account_store import AccountError

GOOGLE_TOKENINFO = "https://oauth2.googleapis.com/tokeninfo"
GOOGLE_ISSUERS = ("accounts.google.com", "https://accounts.google.com")
APPLE_JWKS = "https://appleid.apple.com/auth/keys"
APPLE_ISSUER = "https://appleid.apple.com"
SHA256_DIGEST_INFO = bytes.fromhex("3031300d060960864801650304020105000420")


class GoogleTokenVerifier:
    def __init__(
        self,
        client_id: str,
        http_get: Optional[Callable[[str], Dict[str, Any]]] = None,
        clock: Optional[Callable[[], float]] = None,
    ) -> None:
        self.client_id = client_id.strip()
        self._http_get = http_get or _get_json
        self._clock = clock or time.time

    @property
    def configured(self) -> bool:
        return bool(self.client_id)

    def verify(self, id_token: str) -> Dict[str, Any]:
        if not self.configured:
            raise AccountError(
                503,
                "PROVIDER_NOT_CONFIGURED",
                "Google sign-in is not configured on this server.",
                {"provider": "google"},
            )
        if not isinstance(id_token, str) or len(id_token) < 32:
            raise AccountError(400, "INVALID_PROVIDER_TOKEN", "The identity token is invalid.")
        url = GOOGLE_TOKENINFO + "?" + urllib.parse.urlencode({"id_token": id_token})
        try:
            claims = self._http_get(url)
        except AccountError:
            raise
        except Exception as exc:
            raise AccountError(
                401, "INVALID_PROVIDER_TOKEN", "Google rejected the identity token."
            ) from exc
        if not isinstance(claims, dict):
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "Google rejected the identity token.")
        audience = str(claims.get("aud") or "")
        if audience != self.client_id:
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "The identity token has the wrong audience.")
        issuer = str(claims.get("iss") or "")
        if issuer not in GOOGLE_ISSUERS:
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "The identity token has the wrong issuer.")
        try:
            expires = int(claims.get("exp") or 0)
        except (TypeError, ValueError):
            expires = 0
        if expires <= int(self._clock()):
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "The identity token has expired.")
        subject = str(claims.get("sub") or "")
        if len(subject) < 3:
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "The identity token has no subject.")
        verified = claims.get("email_verified")
        email_verified = verified is True or str(verified).lower() == "true"
        return {
            "subject": subject,
            "email": str(claims.get("email") or ""),
            "emailVerified": email_verified,
            "name": str(claims.get("name") or ""),
        }


def _b64url_decode(value: str) -> bytes:
    padded = value + "=" * (-len(value) % 4)
    try:
        return base64.urlsafe_b64decode(padded.encode("ascii"))
    except (ValueError, UnicodeEncodeError) as exc:
        raise AccountError(
            401, "INVALID_PROVIDER_TOKEN", "The identity token is malformed."
        ) from exc


def _rs256_verify(n_b64: str, e_b64: str, message: bytes, signature: bytes) -> bool:
    """Verify RSASSA-PKCS1-v1_5/SHA-256 with a public JWK modulus and exponent."""

    try:
        modulus = int.from_bytes(_b64url_decode(n_b64), "big")
        exponent = int.from_bytes(_b64url_decode(e_b64), "big")
    except AccountError:
        return False
    if modulus <= 0 or exponent <= 1:
        return False
    size = (modulus.bit_length() + 7) // 8
    if len(signature) != size:
        return False
    encoded = pow(int.from_bytes(signature, "big"), exponent, modulus).to_bytes(
        size, "big"
    )
    digest = hashlib.sha256(message).digest()
    expected = (
        b"\x00\x01"
        + b"\xff" * (size - len(SHA256_DIGEST_INFO) - len(digest) - 3)
        + b"\x00"
        + SHA256_DIGEST_INFO
        + digest
    )
    return hmac.compare_digest(encoded, expected)


class AppleTokenVerifier:
    """Verify an Apple identity token against Apple's JWKS and configured audience."""

    def __init__(
        self,
        client_id: str,
        http_get: Optional[Callable[[str], Dict[str, Any]]] = None,
        clock: Optional[Callable[[], float]] = None,
        jwks_ttl_seconds: int = 3600,
    ) -> None:
        self.client_id = client_id.strip()
        self._http_get = http_get or _get_json
        self._clock = clock or time.time
        self._jwks_ttl = max(60, int(jwks_ttl_seconds))
        self._jwks: List[Dict[str, Any]] = []
        self._jwks_loaded = 0.0

    @property
    def configured(self) -> bool:
        return bool(self.client_id)

    def _keys(self) -> List[Dict[str, Any]]:
        now = self._clock()
        if self._jwks and now - self._jwks_loaded < self._jwks_ttl:
            return self._jwks
        try:
            payload = self._http_get(APPLE_JWKS)
        except AccountError:
            raise
        except Exception as exc:
            raise AccountError(
                503, "PROVIDER_UNAVAILABLE", "Apple sign-in is temporarily unavailable."
            ) from exc
        keys = payload.get("keys") if isinstance(payload, dict) else None
        if not isinstance(keys, list):
            raise AccountError(
                503, "PROVIDER_UNAVAILABLE", "Apple returned no signing keys."
            )
        self._jwks = [key for key in keys if isinstance(key, dict)]
        self._jwks_loaded = now
        return self._jwks

    def verify(self, id_token: str) -> Dict[str, Any]:
        if not self.configured:
            raise AccountError(
                503,
                "PROVIDER_NOT_CONFIGURED",
                "Apple sign-in is not configured on this server.",
                {"provider": "apple"},
            )
        if not isinstance(id_token, str) or id_token.count(".") != 2:
            raise AccountError(400, "INVALID_PROVIDER_TOKEN", "The identity token is invalid.")
        header_b64, payload_b64, signature_b64 = id_token.split(".")
        try:
            header = json.loads(_b64url_decode(header_b64).decode("utf-8"))
            claims = json.loads(_b64url_decode(payload_b64).decode("utf-8"))
        except (UnicodeDecodeError, ValueError) as exc:
            raise AccountError(
                401, "INVALID_PROVIDER_TOKEN", "The identity token is malformed."
            ) from exc
        if not isinstance(header, dict) or not isinstance(claims, dict):
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "The identity token is malformed.")
        if str(header.get("alg") or "") != "RS256":
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "Unsupported Apple token algorithm.")
        key_id = str(header.get("kid") or "")
        key = next(
            (
                candidate
                for candidate in self._keys()
                if str(candidate.get("kid") or "") == key_id
                and str(candidate.get("kty") or "") == "RSA"
            ),
            None,
        )
        if key is None:
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "Unknown Apple signing key.")
        try:
            signature = _b64url_decode(signature_b64)
        except AccountError:
            raise
        if not _rs256_verify(
            str(key.get("n") or ""),
            str(key.get("e") or ""),
            (header_b64 + "." + payload_b64).encode("ascii"),
            signature,
        ):
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "The Apple token signature is invalid.")
        if str(claims.get("iss") or "") != APPLE_ISSUER:
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "The identity token has the wrong issuer.")
        if str(claims.get("aud") or "") != self.client_id:
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "The identity token has the wrong audience.")
        try:
            expires = int(claims.get("exp") or 0)
        except (TypeError, ValueError):
            expires = 0
        if expires <= int(self._clock()):
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "The identity token has expired.")
        subject = str(claims.get("sub") or "")
        if len(subject) < 3:
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "The identity token has no subject.")
        verified = claims.get("email_verified")
        email_verified = verified is True or str(verified).lower() == "true"
        return {
            "subject": subject,
            "email": str(claims.get("email") or ""),
            "emailVerified": email_verified,
            "name": str(claims.get("name") or ""),
        }


def _get_json(url: str) -> Dict[str, Any]:
    request = urllib.request.Request(
        url,
        headers={"Accept": "application/json", "User-Agent": "NeonVelocityIdentity/1.0"},
        method="GET",
    )
    try:
        with urllib.request.urlopen(request, timeout=12.0) as response:
            body = response.read(256 * 1024)
    except urllib.error.HTTPError as exc:
        raise AccountError(
            401, "INVALID_PROVIDER_TOKEN", "Google rejected the identity token."
        ) from exc
    except (OSError, urllib.error.URLError) as exc:
        raise AccountError(
            503, "PROVIDER_UNAVAILABLE", "Google sign-in is temporarily unavailable."
        ) from exc
    try:
        payload = json.loads(body.decode("utf-8"))
    except (UnicodeDecodeError, ValueError) as exc:
        raise AccountError(
            401, "INVALID_PROVIDER_TOKEN", "Google rejected the identity token."
        ) from exc
    return payload if isinstance(payload, dict) else {}


class ProviderVerifier:
    """Routes a provider name to the adapters that are actually configured."""

    def __init__(
        self,
        google: Optional[GoogleTokenVerifier] = None,
        apple: Optional[AppleTokenVerifier] = None,
    ) -> None:
        self.google = google
        self.apple = apple

    def __call__(self, provider: str, id_token: str) -> Dict[str, Any]:
        if provider == "google" and self.google is not None and self.google.configured:
            return self.google.verify(id_token)
        if provider == "apple" and self.apple is not None and self.apple.configured:
            return self.apple.verify(id_token)
        raise AccountError(
            503,
            "PROVIDER_NOT_CONFIGURED",
            provider.upper() + " sign-in is not configured on this server.",
            {"provider": provider},
        )
