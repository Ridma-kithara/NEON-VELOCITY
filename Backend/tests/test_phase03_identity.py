#!/usr/bin/env python3
"""Unit tests for Apple JWKS verification and Google Play service-account auth.

The RSA key below is a throwaway test key generated for this suite. It is not used by
the game server and is safe to keep in the repository.
"""

from __future__ import annotations

import base64
import json
import pathlib
import sys
import time
import unittest
from typing import Any, Dict, List, Optional, Tuple

TEST_DIR = pathlib.Path(__file__).resolve().parent
BACKEND_DIR = TEST_DIR.parent
if str(BACKEND_DIR) not in sys.path:
    sys.path.insert(0, str(BACKEND_DIR))

from account_store import AccountError  # noqa: E402
from identity import AppleTokenVerifier  # noqa: E402
from payments import GoogleServiceAccountVerifier  # noqa: E402
from rsa_keys import load_rsa_private_key, sign_rs256  # noqa: E402

TEST_RSA_PRIVATE_KEY = """\
-----BEGIN PRIVATE KEY-----
MIIEvwIBADANBgkqhkiG9w0BAQEFAASCBKkwggSlAgEAAoIBAQDDlfwJXhGUzDg5
JNLBHjjELE3zBWwzFL6Z4Qz6VmAtJGSrKZCn6PbdC5/lByzhtEabgsHJswW/7iyc
WHjOFsBQkJtxHWVcNhz/LtoTQMgxdsOu3VKCKWfNP19pia25QGudKuy47o/Dyvtv
fRF8QfBHZyzv2yAcGcntSTbhYPzy120S9/3zOwpzmA58kExDyQrfb8OEEAAYRoLG
fMZCzOeceK8xfyvXIo/E4KkWdpvePbcbzlPde7VzDHQMAQkrkBZxl0g2SQ+MSQ24
7XjUkWuv+d6cI21ilJVI3KfftYfNCcN5pcYN5xv3d0Ol0Rkvm86q2QFGDhPLQYuT
sg09HvPDAgMBAAECggEBALQFv0BYB1KwTie6zwnV6WYXaYAAQ8EYnP9B+BILouaq
EsbAsVb7xQ1cf8XsvtDYgAzvY7NlRiuM7B7UCA2ZP1ZoY8rPxdYEMNhopW0HObzd
levD5wfLWLHAXO/lO7PBqcu2EMF/Q3BxytCpi4iVoLX4L5JuXLXAvu3bb9ouzWQu
avp1trZAmAAVAkyzpbLVBR43STuQe3m1rry13L+YA85FBYOlEBi8NXGeAFKduGK1
+nlz87VsWcvNKerU8fBiZXwUTQO39WW6FP/oiW95QATHD6A+JHc0x50Pwfbmabw8
BTepJWuY+o5jDFbFQWQsQ7EyP0juEQzKqpxr8Fljm3ECgYEA+RYuzM2Z6+TwEvHs
UjaRREPsHhR/iHqbmhTuaRDVS5hzVv3QsB4G9mRPpITeF819fAOekUkkn/crqMJ1
buOCknMa2OGbkmSTSBKWPWTsBGTA48iaHo0iMCPD8rbNBLnH0ymm4fo2SZnX5wU1
4VIgVeQMMoz/hV78fzgH4HRYAfcCgYEAyQOqo7TsTZXOngqYFWw1yAljCdwQmlat
65Pn3riWtPxNTgwz09O9vY+1wNM7SlI7i3e7UdsaQVtKGD77nNbQ3MqPCRQQhzBj
A1sh6ipiBWmhNUJYaCWo25NbAFmrQMQTeprzIXlwx+CkP2nG53Z8CoFt/QtQkN9M
HwJ0sK3E6ZUCgYEAn2PbhMCYG0Q/f9ABKkayxF02bbUuOtZCVeHJAcB8zMf8HpGC
hgBg8jAUarF/MPoUr1PdlxiX8CkJbg8peFDrj+1KI7iShaYIPRlvvE+lW9ZhPCF0
xdIIK4fnbpwdK0X6oZxIptgm/wgMCU/jP5D/EhdA2uQw6lZy+rGPEMlCO9cCgYEA
wwI+mgDcOkx6HRJUBnxrFHwey6LlrIUoxuE/Ue+rrTsTWAM4iGjjqzHu9nk2DN0I
DT6DyF6R8xO4Jxt7B+XSzE/qQbloa26Cr+/KUBz76RpXnY6Wq7rVPOeQoh9bp0JR
T2FhO3lP82i4jxh0MXrdzWdRJ+rBJzqp42+Dex+pZ50CgYB0V3NoSAga+YbJcSiD
Q+kWPjKuL+l2v/SOdCGwR227fuiZLFTeEDFVeEaXyAgje0BXdYCP0MqYOVsI0sXo
Q2G6uVsNP0VX/3KZ8En/CtWYSJNc9077ASFvp7azBvm+ZkTf80msf4q7ovIGOg5b
NewKNwpMUHmYV3Y7u4ax3YVEDQ==
-----END PRIVATE KEY-----
"""


def b64url(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")


def public_jwk() -> Dict[str, str]:
    modulus, exponent, _private = load_rsa_private_key(TEST_RSA_PRIVATE_KEY)
    return {
        "kty": "RSA",
        "kid": "test-key-1",
        "alg": "RS256",
        "use": "sig",
        "n": b64url(modulus.to_bytes((modulus.bit_length() + 7) // 8, "big")),
        "e": b64url(exponent.to_bytes((exponent.bit_length() + 7) // 8, "big")),
    }


def apple_token(
    *,
    audience: str = "com.neonvelocity.web",
    expires_in: int = 600,
    kid: str = "test-key-1",
    email_verified: Any = True,
) -> str:
    header = {"alg": "RS256", "kid": kid, "typ": "JWT"}
    claims = {
        "iss": "https://appleid.apple.com",
        "aud": audience,
        "exp": int(time.time()) + expires_in,
        "iat": int(time.time()),
        "sub": "001234.abcdef0123456789",
        "email": "apple@example.com",
        "email_verified": email_verified,
    }
    signing_input = (
        b64url(json.dumps(header, separators=(",", ":")).encode("utf-8"))
        + "."
        + b64url(json.dumps(claims, separators=(",", ":")).encode("utf-8"))
    )
    signature = sign_rs256(TEST_RSA_PRIVATE_KEY, signing_input.encode("ascii"))
    return signing_input + "." + b64url(signature)


class AppleTokenVerifierTests(unittest.TestCase):
    def verifier(self, client_id: str = "com.neonvelocity.web") -> AppleTokenVerifier:
        return AppleTokenVerifier(
            client_id,
            http_get=lambda _url: {"keys": [public_jwk()]},
            clock=lambda: float(int(time.time())),
        )

    def test_valid_token_is_verified_against_jwks(self) -> None:
        result = self.verifier().verify(apple_token())
        self.assertEqual(result["subject"], "001234.abcdef0123456789")
        self.assertEqual(result["email"], "apple@example.com")
        self.assertTrue(result["emailVerified"])

    def test_wrong_audience_is_rejected(self) -> None:
        with self.assertRaises(AccountError) as caught:
            self.verifier().verify(apple_token(audience="com.other.app"))
        self.assertEqual(caught.exception.code, "INVALID_PROVIDER_TOKEN")

    def test_expired_token_is_rejected(self) -> None:
        with self.assertRaises(AccountError) as caught:
            self.verifier().verify(apple_token(expires_in=-30))
        self.assertEqual(caught.exception.code, "INVALID_PROVIDER_TOKEN")

    def test_unknown_signing_key_is_rejected(self) -> None:
        with self.assertRaises(AccountError) as caught:
            self.verifier().verify(apple_token(kid="missing-key"))
        self.assertEqual(caught.exception.code, "INVALID_PROVIDER_TOKEN")

    def test_tampered_signature_is_rejected(self) -> None:
        token = apple_token()
        header, claims, signature = token.split(".")
        tampered = signature[:-4] + ("AAAA" if signature[-4:] != "AAAA" else "BBBB")
        with self.assertRaises(AccountError) as caught:
            self.verifier().verify(header + "." + claims + "." + tampered)
        self.assertEqual(caught.exception.code, "INVALID_PROVIDER_TOKEN")

    def test_unconfigured_verifier_refuses(self) -> None:
        with self.assertRaises(AccountError) as caught:
            AppleTokenVerifier("").verify(apple_token())
        self.assertEqual(caught.exception.status, 503)
        self.assertEqual(caught.exception.code, "PROVIDER_NOT_CONFIGURED")


class GoogleServiceAccountVerifierTests(unittest.TestCase):
    def setUp(self) -> None:
        self.token_posts: List[Dict[str, Any]] = []
        self.gets: List[Dict[str, Any]] = []
        self.purchase_state = 0
        self.api_status = 200
        self.service_account = json.dumps(
            {
                "type": "service_account",
                "client_email": "play-verifier@example.iam.gserviceaccount.com",
                "private_key": TEST_RSA_PRIVATE_KEY,
                "token_uri": "https://oauth2.googleapis.com/token",
            }
        )

    def http_post(self, url: str, form: Dict[str, str], headers: Dict[str, str], timeout: float = 20.0):
        self.token_posts.append({"url": url, "form": form, "headers": headers})
        return 200, {"access_token": "ya29.test-token", "expires_in": 3600}

    def http_get(self, url: str, headers: Dict[str, str], timeout: float = 20.0):
        self.gets.append({"url": url, "headers": headers})
        if self.api_status != 200:
            return self.api_status, {}
        return 200, {
            "purchaseState": self.purchase_state,
            "orderId": "GPA.1234-5678-9012-34567",
            "priceAmountMicros": "4990000",
            "priceCurrencyCode": "USD",
        }

    def verifier(self) -> GoogleServiceAccountVerifier:
        return GoogleServiceAccountVerifier(
            self.service_account,
            "com.neonvelocity.game",
            product_map={"com.neonvelocity.gems80": "gems_80"},
            http_post=self.http_post,
            http_get=self.http_get,
        )

    def test_service_account_token_and_purchase_verification(self) -> None:
        result = self.verifier()("com.neonvelocity.gems80", "purchase-token")
        self.assertEqual(result["packageId"], "gems_80")
        self.assertEqual(result["transactionId"], "GPA.1234-5678-9012-34567")
        self.assertEqual(result["amountMinor"], 499)
        self.assertEqual(result["currency"], "USD")
        self.assertEqual(len(self.token_posts), 1)
        self.assertEqual(
            self.token_posts[0]["form"]["grant_type"],
            "urn:ietf:params:oauth:grant-type:jwt-bearer",
        )
        assertion = self.token_posts[0]["form"]["assertion"]
        self.assertEqual(len(assertion.split(".")), 3)
        self.assertIn("com.neonvelocity.game", self.gets[0]["url"])
        self.assertIn("com.neonvelocity.gems80", self.gets[0]["url"])
        self.assertEqual(self.gets[0]["headers"]["Authorization"], "Bearer ya29.test-token")

    def test_token_is_cached_between_purchases(self) -> None:
        verifier = self.verifier()
        verifier("com.neonvelocity.gems80", "purchase-one")
        verifier("com.neonvelocity.gems80", "purchase-two")
        self.assertEqual(len(self.token_posts), 1)
        self.assertEqual(len(self.gets), 2)

    def test_unpurchased_state_is_rejected(self) -> None:
        self.purchase_state = 1
        with self.assertRaises(AccountError) as caught:
            self.verifier()("com.neonvelocity.gems80", "pending-token")
        self.assertEqual(caught.exception.code, "INVALID_RECEIPT")

    def test_missing_api_purchase_is_rejected(self) -> None:
        self.api_status = 404
        with self.assertRaises(AccountError) as caught:
            self.verifier()("com.neonvelocity.gems80", "missing-token")
        self.assertEqual(caught.exception.code, "INVALID_RECEIPT")

    def test_unconfigured_service_account_refuses(self) -> None:
        verifier = GoogleServiceAccountVerifier("", "com.neonvelocity.game")
        with self.assertRaises(AccountError) as caught:
            verifier("com.neonvelocity.gems80", "token")
        self.assertEqual(caught.exception.status, 503)
        self.assertEqual(caught.exception.code, "PROVIDER_NOT_CONFIGURED")


if __name__ == "__main__":
    unittest.main()
