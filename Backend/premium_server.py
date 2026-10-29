#!/usr/bin/env python3
"""NEON VELOCITY premium currency HTTP service.

A dependency-free Python 3.9+ service that sells GEM packages for manual bank transfers,
stores cosmetic/upgrade entitlements, and debits in-run revives. It owns the catalogue,
prices, balances and approval decisions; clients only name a package or product.

Run locally:

    python3 Backend/premium_server.py --host 127.0.0.1 --port 8088 \
        --data-dir Backend/premium_data

Production must put this service behind an HTTPS reverse proxy. The built-in server speaks
plain HTTP and binds to loopback by default.
"""

from __future__ import annotations

import argparse
import collections
import dataclasses
import email.parser
import email.policy
import ipaddress
import json
import logging
import os
import pathlib
import re
import signal
import sys
import threading
import time
import urllib.request
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Deque, Dict, List, Optional, Tuple
from urllib.parse import parse_qs, urlsplit

from account_store import AccountError, AccountStore
from identity import AppleTokenVerifier, GoogleTokenVerifier, ProviderVerifier
from mailer import SmsWebhook, SmtpMailer
from payments import (
    AppleConfig,
    AppleIap,
    GooglePlayIap,
    GoogleServiceAccountVerifier,
    StripeConfig,
    StripePayments,
)
from premium_store import (
    DEFAULT_AUTH_TOKEN_TTL_SECONDS,
    GEM_PACKAGES,
    ORDER_STATUSES,
    PREMIUM_PRODUCTS,
    REVIVE_COST_GEMS,
    SERVER_VERSION,
    SERVICE_NAME,
    AccountNotFound,
    PremiumError,
    PremiumStore,
    ProofNotFound,
    detect_proof_mime,
    iso_utc,
    password_hash_format_is_valid,
    sanitize_original_filename,
    verify_password,
)

MAX_JSON_BODY_BYTES = 64 * 1024
DEFAULT_MAX_PROOF_BYTES = 8 * 1024 * 1024
MAX_PROOF_OVERHEAD_BYTES = 1024 * 1024
ADMIN_TOKEN_TTL_SECONDS = 30 * 60
DEFAULT_ALLOWED_ORIGIN = "*"
DEFAULT_GUEST_RATE_LIMIT = 60
DEFAULT_ADMIN_LOGIN_RATE_LIMIT = 5
DEFAULT_RATE_WINDOW_SECONDS = 60
MAX_ADMIN_NOTE_LENGTH = 500
MAX_IDEMPOTENCY_KEY_LENGTH = 128
MIN_IDEMPOTENCY_KEY_LENGTH = 1

ORDER_PATH_RE = re.compile(r"^/api/v1/orders/(NV-[0-9]{8}-[A-Z0-9]{8})$")
ORDER_PROOF_PATH_RE = re.compile(
    r"^/api/v1/orders/(NV-[0-9]{8}-[A-Z0-9]{8})/proof$"
)
ADMIN_ORDER_PROOF_PATH_RE = re.compile(
    r"^/api/v1/admin/orders/(NV-[0-9]{8}-[A-Z0-9]{8})/proof$"
)
ADMIN_ORDER_DECISION_PATH_RE = re.compile(
    r"^/api/v1/admin/orders/(NV-[0-9]{8}-[A-Z0-9]{8})/decision$"
)
ADMIN_ACCOUNT_PATH_RE = re.compile(r"^/api/v1/admin/accounts/(acc_[A-Za-z0-9]+)$")
ADMIN_TRANSACTION_REFUND_PATH_RE = re.compile(
    r"^/api/v1/admin/transactions/([0-9]{1,12})/refund$"
)
ADMIN_CATALOG_PATH_RE = re.compile(
    r"^/api/v1/admin/catalog/(packages|products)/([A-Za-z0-9_\-]{1,64})$"
)
IDEMPOTENCY_KEY_RE = re.compile(r"^[A-Za-z0-9._:-]+$")
DEVICE_KEY_RE = re.compile(r"^[\x21-\x7e]+$")
SESSION_ID_RE = re.compile(r"^[A-Za-z0-9._:-]+$")

logger = logging.getLogger("neon-premium")


class ApiError(Exception):
    """An HTTP-visible error with a stable code and optional response fields."""

    def __init__(
        self,
        status: int,
        code: str,
        message: str,
        extra: Optional[Dict[str, Any]] = None,
        headers: Optional[Dict[str, str]] = None,
    ) -> None:
        super().__init__(message)
        self.status = status
        self.code = code
        self.message = message
        self.extra = dict(extra or {})
        self.headers = dict(headers or {})


class SlidingWindowLimiter:
    """Small in-memory limiter. A single process is the deployment model."""

    def __init__(self, limit: int, window_seconds: float) -> None:
        self.limit = max(1, int(limit))
        self.window_seconds = max(1.0, float(window_seconds))
        self._events: Dict[str, Deque[float]] = {}
        self._lock = threading.Lock()

    def _prune(self, key: str, now: float) -> Deque[float]:
        bucket = self._events.get(key)
        if bucket is None:
            bucket = collections.deque()
            self._events[key] = bucket
        cutoff = now - self.window_seconds
        while bucket and bucket[0] <= cutoff:
            bucket.popleft()
        return bucket

    def _compact(self, now: float) -> None:
        if len(self._events) < 2048:
            return
        cutoff = now - self.window_seconds
        stale = [
            key
            for key, bucket in self._events.items()
            if not bucket or bucket[-1] <= cutoff
        ]
        for key in stale:
            self._events.pop(key, None)

    def allow(self, key: str) -> bool:
        now = time.monotonic()
        with self._lock:
            bucket = self._prune(key, now)
            if len(bucket) >= self.limit:
                return False
            bucket.append(now)
            self._compact(now)
            return True

    def is_limited(self, key: str) -> bool:
        now = time.monotonic()
        with self._lock:
            return len(self._prune(key, now)) >= self.limit

    def record_failure(self, key: str) -> None:
        now = time.monotonic()
        with self._lock:
            bucket = self._prune(key, now)
            bucket.append(now)
            self._compact(now)

    def clear(self, key: str) -> None:
        with self._lock:
            self._events.pop(key, None)


def _env_int(
    name: str, default: int, minimum: int, maximum: int
) -> int:
    raw = os.environ.get(name, "").strip()
    if not raw:
        return default
    try:
        value = int(raw)
    except ValueError as exc:
        raise ValueError("{0} must be an integer".format(name)) from exc
    if value < minimum or value > maximum:
        raise ValueError(
            "{0} must be between {1} and {2}".format(name, minimum, maximum)
        )
    return value


def _env_flag(name: str, default: bool = False) -> bool:
    raw = os.environ.get(name, "").strip().lower()
    if not raw:
        return default
    if raw in ("1", "true", "yes", "on"):
        return True
    if raw in ("0", "false", "no", "off"):
        return False
    raise ValueError("{0} must be true or false".format(name))


def _env_string_map(name: str) -> Dict[str, str]:
    """Parse a JSON object of string values, ignoring malformed entries."""

    raw = os.environ.get(name, "").strip()
    if not raw:
        return {}
    try:
        parsed = json.loads(raw)
    except ValueError as exc:
        raise ValueError("{0} must be a JSON object".format(name)) from exc
    if not isinstance(parsed, dict):
        raise ValueError("{0} must be a JSON object".format(name))
    return {
        str(key): str(value)
        for key, value in parsed.items()
        if isinstance(key, str) and isinstance(value, str) and value
    }


@dataclasses.dataclass
class PremiumConfig:
    """Runtime configuration. Secrets are supplied through the environment or tests."""

    host: str = "127.0.0.1"
    port: int = 8088
    data_dir: pathlib.Path = dataclasses.field(
        default_factory=lambda: pathlib.Path(__file__).resolve().parent / "premium_data"
    )
    allowed_origin: str = DEFAULT_ALLOWED_ORIGIN
    max_proof_bytes: int = DEFAULT_MAX_PROOF_BYTES
    max_json_body_bytes: int = MAX_JSON_BODY_BYTES
    admin_username: str = ""
    admin_password: str = dataclasses.field(default="", repr=False)
    admin_password_hash: str = dataclasses.field(default="", repr=False)
    admin_token_ttl_seconds: int = ADMIN_TOKEN_TTL_SECONDS
    auth_token_ttl_seconds: int = DEFAULT_AUTH_TOKEN_TTL_SECONDS
    notify_webhook: str = dataclasses.field(default="", repr=False)
    guest_rate_limit: int = DEFAULT_GUEST_RATE_LIMIT
    admin_login_rate_limit: int = DEFAULT_ADMIN_LOGIN_RATE_LIMIT
    auth_rate_limit: int = 30
    rate_window_seconds: int = DEFAULT_RATE_WINDOW_SECONDS
    trust_proxy_headers: bool = False
    auth_dev_mode: bool = False
    stripe_secret_key: str = dataclasses.field(default="", repr=False)
    stripe_webhook_secret: str = dataclasses.field(default="", repr=False)
    stripe_currency: str = "usd"
    stripe_price_map: Dict[str, str] = dataclasses.field(default_factory=dict)
    stripe_success_url: str = ""
    stripe_cancel_url: str = ""
    apple_shared_secret: str = dataclasses.field(default="", repr=False)
    apple_product_map: Dict[str, str] = dataclasses.field(default_factory=dict)
    google_play_product_map: Dict[str, str] = dataclasses.field(default_factory=dict)
    google_play_service_account: str = dataclasses.field(default="", repr=False)
    google_play_package_name: str = ""
    google_client_id: str = ""
    apple_client_id: str = ""
    manual_bank_transfer: bool = False

    @classmethod
    def from_env(
        cls,
        host: Optional[str] = None,
        port: Optional[int] = None,
        data_dir: Optional[pathlib.Path] = None,
    ) -> "PremiumConfig":
        env_host = os.environ.get("NV_PREMIUM_HOST", "").strip()
        env_port = _env_int("NV_PREMIUM_PORT", 8088, 1, 65535)
        env_data = os.environ.get("NV_PREMIUM_DATA_DIR", "").strip()
        resolved_data = pathlib.Path(data_dir) if data_dir is not None else (
            pathlib.Path(env_data).expanduser()
            if env_data
            else pathlib.Path(__file__).resolve().parent / "premium_data"
        )
        allowed_origin = os.environ.get(
            "NV_ALLOWED_ORIGIN", DEFAULT_ALLOWED_ORIGIN
        ).strip() or DEFAULT_ALLOWED_ORIGIN
        admin_hash = os.environ.get("NV_ADMIN_PASSWORD_HASH", "").strip()
        if admin_hash and not password_hash_format_is_valid(admin_hash):
            raise ValueError(
                "NV_ADMIN_PASSWORD_HASH is not a valid pbkdf2_sha256 verifier"
            )
        return cls(
            host=host if host is not None else (env_host or "127.0.0.1"),
            port=port if port is not None else env_port,
            data_dir=resolved_data,
            allowed_origin=allowed_origin,
            max_proof_bytes=_env_int(
                "NV_MAX_PROOF_BYTES",
                DEFAULT_MAX_PROOF_BYTES,
                1024,
                64 * 1024 * 1024,
            ),
            admin_username=os.environ.get("NV_ADMIN_USERNAME", "").strip(),
            admin_password=os.environ.get("NV_ADMIN_PASSWORD", ""),
            admin_password_hash=admin_hash,
            admin_token_ttl_seconds=_env_int(
                "NV_ADMIN_TOKEN_TTL_SECONDS",
                ADMIN_TOKEN_TTL_SECONDS,
                60,
                86400,
            ),
            auth_token_ttl_seconds=_env_int(
                "NV_AUTH_TOKEN_TTL_SECONDS",
                DEFAULT_AUTH_TOKEN_TTL_SECONDS,
                60,
                365 * 24 * 60 * 60,
            ),
            notify_webhook=os.environ.get("NV_NOTIFY_WEBHOOK", "").strip(),
            guest_rate_limit=_env_int("NV_GUEST_RATE_LIMIT", 60, 1, 10000),
            admin_login_rate_limit=_env_int(
                "NV_ADMIN_LOGIN_RATE_LIMIT", 5, 1, 1000
            ),
            rate_window_seconds=_env_int("NV_RATE_WINDOW_SECONDS", 60, 1, 3600),
            trust_proxy_headers=_env_flag("NV_TRUST_PROXY_HEADERS", False),
            auth_dev_mode=_env_flag("NV_AUTH_DEV_MODE", False),
            auth_rate_limit=_env_int("NV_AUTH_RATE_LIMIT", 30, 1, 10000),
            stripe_secret_key=os.environ.get("NV_STRIPE_SECRET_KEY", "").strip(),
            stripe_webhook_secret=os.environ.get(
                "NV_STRIPE_WEBHOOK_SECRET", ""
            ).strip(),
            stripe_currency=(
                os.environ.get("NV_STRIPE_CURRENCY", "usd").strip().lower() or "usd"
            ),
            stripe_price_map=_env_string_map("NV_STRIPE_PRICE_MAP"),
            stripe_success_url=os.environ.get("NV_STRIPE_SUCCESS_URL", "").strip(),
            stripe_cancel_url=os.environ.get("NV_STRIPE_CANCEL_URL", "").strip(),
            apple_shared_secret=os.environ.get("NV_APPLE_SHARED_SECRET", "").strip(),
            apple_product_map=_env_string_map("NV_APPLE_PRODUCT_MAP"),
            google_play_product_map=_env_string_map("NV_GOOGLE_PLAY_PRODUCT_MAP"),
            google_play_service_account=os.environ.get(
                "NV_GOOGLE_PLAY_SERVICE_ACCOUNT", ""
            ).strip(),
            google_play_package_name=os.environ.get(
                "NV_GOOGLE_PLAY_PACKAGE_NAME", ""
            ).strip(),
            google_client_id=os.environ.get("NV_GOOGLE_CLIENT_ID", "").strip(),
            apple_client_id=os.environ.get("NV_APPLE_CLIENT_ID", "").strip(),
            manual_bank_transfer=_env_flag("NV_ENABLE_MANUAL_BANK_TRANSFER", False),
        )


class PremiumApplication:
    """Wires configuration, persistence, limiters and admin authentication."""

    def __init__(
        self,
        config: PremiumConfig,
        *,
        mailer: Optional[Any] = None,
        sms_sender: Optional[Any] = None,
        oauth_verifier: Optional[Any] = None,
        stripe_http_post: Optional[Any] = None,
        apple_http_post: Optional[Any] = None,
        google_verifier: Optional[Any] = None,
    ) -> None:
        self.config = config
        self.config.data_dir = pathlib.Path(self.config.data_dir)
        self.store = PremiumStore(self.config.data_dir)
        self.guest_limiter = SlidingWindowLimiter(
            self.config.guest_rate_limit, self.config.rate_window_seconds
        )
        self.admin_failure_limiter = SlidingWindowLimiter(
            self.config.admin_login_rate_limit,
            self.config.rate_window_seconds,
        )
        self.auth_limiter = SlidingWindowLimiter(
            self.config.auth_rate_limit, self.config.rate_window_seconds
        )
        if mailer is None:
            smtp = SmtpMailer.from_env()
            self.mailer = smtp if smtp.configured else None
        else:
            self.mailer = mailer
        if sms_sender is None:
            sms = SmsWebhook.from_env()
            sms_sender = sms.send if sms.configured else None
        if oauth_verifier is None:
            google = GoogleTokenVerifier(self.config.google_client_id)
            apple = AppleTokenVerifier(self.config.apple_client_id)
            oauth_verifier = ProviderVerifier(
                google if google.configured else None,
                apple if apple.configured else None,
            )
        self.accounts = AccountStore(
            self.config.data_dir,
            self.store,
            email_sender=self.mailer.send if self.mailer is not None else None,
            sms_sender=sms_sender,
            oauth_verifier=oauth_verifier,
            dev_mode=self.config.auth_dev_mode,
        )
        self.stripe = StripePayments(
            self.store,
            self.accounts,
            mailer=self.mailer,
            config=StripeConfig(
                secret_key=self.config.stripe_secret_key,
                webhook_secret=self.config.stripe_webhook_secret,
                currency=self.config.stripe_currency,
                price_map=dict(self.config.stripe_price_map),
                success_url=self.config.stripe_success_url,
                cancel_url=self.config.stripe_cancel_url,
            ),
            http_post=stripe_http_post,
        )
        self.apple = AppleIap(
            self.store,
            self.accounts,
            mailer=self.mailer,
            config=AppleConfig(
                shared_secret=self.config.apple_shared_secret,
                product_map=dict(self.config.apple_product_map),
            ),
            http_post=apple_http_post,
        )
        if google_verifier is None and self.config.google_play_service_account:
            google_verifier = GoogleServiceAccountVerifier(
                self.config.google_play_service_account,
                self.config.google_play_package_name,
                product_map=dict(self.config.google_play_product_map),
            )
        self.google_play = GooglePlayIap(
            self.store, self.accounts, verifier=google_verifier, mailer=self.mailer
        )

    def payment_capabilities(self) -> Dict[str, Any]:
        return {
            "stripe": self.stripe.configured,
            "apple": self.apple.configured,
            "googlePlay": self.google_play.configured,
            "manualBankTransfer": bool(self.config.manual_bank_transfer),
            "appleProductMap": dict(self.config.apple_product_map),
            "googlePlayProductMap": dict(
                self.config.google_play_product_map
                or {item.id: item.id for item in GEM_PACKAGES}
            ),
            "stripeCurrency": self.config.stripe_currency.upper(),
        }

    def require_manual_transfer(self) -> None:
        if not self.config.manual_bank_transfer:
            raise ApiError(
                403,
                "MANUAL_TRANSFER_DISABLED",
                "Manual bank transfers are disabled. Use the in-app store checkout.",
            )

    @property
    def admin_configured(self) -> bool:
        return bool(
            self.config.admin_username
            and (self.config.admin_password or self.config.admin_password_hash)
        )

    def check_admin_credentials(self, username: str, password: str) -> bool:
        configured_user = self.config.admin_username
        username_ok = hmac_compare(username, configured_user)
        if self.config.admin_password_hash:
            password_ok = verify_password(password, self.config.admin_password_hash)
        else:
            password_ok = hmac_compare(password, self.config.admin_password)
        return bool(configured_user) and username_ok and password_ok

    def notify_proof_submitted(self, order: Dict[str, Any]) -> None:
        order_id = str(order.get("orderId") or "")
        logger.info(
            "Payment proof submitted for review: order=%s package=%s amountLkr=%s",
            order_id,
            order.get("packageId"),
            order.get("amountLkr"),
        )
        webhook = self.config.notify_webhook
        if not webhook:
            return
        payload = {
            "type": "premium_proof_submitted",
            "orderId": order_id,
            "packageId": order.get("packageId"),
            "amountLkr": order.get("amountLkr"),
            "currency": order.get("currency"),
            "status": order.get("status"),
            "updatedUtc": order.get("updatedUtc"),
        }
        thread = threading.Thread(
            target=self._post_webhook,
            args=(webhook, payload),
            name="premium-webhook",
            daemon=True,
        )
        thread.start()

    @staticmethod
    def _post_webhook(webhook: str, payload: Dict[str, Any]) -> None:
        body = json.dumps(payload, separators=(",", ":")).encode("utf-8")
        request = urllib.request.Request(
            webhook,
            data=body,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=5.0) as response:
                response.read(1024)
        except Exception as exc:  # network failures must never affect the API response
            logger.warning("Proof notification webhook failed: %s", exc)


def hmac_compare(left: str, right: str) -> bool:
    """Constant-time string compare that also tolerates an empty configured value."""

    import hmac

    return hmac.compare_digest(left.encode("utf-8"), right.encode("utf-8"))


def sanitize_display_name(value: Any) -> str:
    """Sanitise a player name to 1..20 safe characters."""

    if not isinstance(value, str):
        raise ApiError(400, "INVALID_PLAYER_NAME", "INVALID_PLAYER_NAME")
    cleaned = "".join(
        character
        for character in value.strip()
        if character.isalnum() or character in " _-"
    )
    cleaned = " ".join(cleaned.split())
    if len(cleaned) > 20:
        cleaned = cleaned[:20].rstrip()
    if not cleaned:
        raise ApiError(400, "INVALID_PLAYER_NAME", "INVALID_PLAYER_NAME")
    return cleaned


def require_text(
    payload: Dict[str, Any],
    field: str,
    minimum: int,
    maximum: int,
    code: str = "INVALID_FIELD",
) -> str:
    value = payload.get(field)
    if not isinstance(value, str) or isinstance(value, bool):
        raise ApiError(400, code, code)
    if len(value) < minimum or len(value) > maximum:
        raise ApiError(400, code, code)
    return value


def require_idempotency_key(payload: Dict[str, Any]) -> str:
    value = require_text(
        payload,
        "clientRequestId",
        MIN_IDEMPOTENCY_KEY_LENGTH,
        MAX_IDEMPOTENCY_KEY_LENGTH,
        "INVALID_CLIENT_REQUEST_ID",
    )
    if not IDEMPOTENCY_KEY_RE.match(value):
        raise ApiError(
            400, "INVALID_CLIENT_REQUEST_ID", "INVALID_CLIENT_REQUEST_ID"
        )
    return value


def require_device_key(payload: Dict[str, Any]) -> str:
    value = require_text(payload, "deviceKey", 16, 128, "INVALID_DEVICE_KEY")
    if not DEVICE_KEY_RE.match(value):
        raise ApiError(400, "INVALID_DEVICE_KEY", "INVALID_DEVICE_KEY")
    return value


def optional_note(payload: Dict[str, Any], field: str) -> str:
    value = payload.get(field, "")
    if value is None:
        return ""
    if not isinstance(value, str) or isinstance(value, bool):
        raise ApiError(400, "INVALID_NOTE", "INVALID_NOTE")
    if len(value) > MAX_ADMIN_NOTE_LENGTH:
        raise ApiError(400, "NOTE_TOO_LONG", "NOTE_TOO_LONG")
    return value.strip()


def parse_multipart_proof(
    content_type: str, body: bytes, max_proof_bytes: int
) -> Tuple[bytes, str, str, str]:
    """Extract and validate the proof file and optional note from multipart bytes."""

    if len(content_type) > 1024:
        raise ApiError(400, "MALFORMED_MULTIPART", "MALFORMED_MULTIPART")
    envelope = (
        "Content-Type: {0}\r\nMIME-Version: 1.0\r\n\r\n".format(content_type)
    ).encode("utf-8", "surrogateescape")
    try:
        message = email.parser.BytesParser(
            policy=email.policy.default
        ).parsebytes(envelope + body)
    except Exception as exc:
        raise ApiError(
            400, "MALFORMED_MULTIPART", "MALFORMED_MULTIPART"
        ) from exc
    if not message.is_multipart():
        raise ApiError(400, "MALFORMED_MULTIPART", "MALFORMED_MULTIPART")
    parts = list(message.iter_parts())
    if not parts or len(parts) > 16:
        raise ApiError(400, "MALFORMED_MULTIPART", "MALFORMED_MULTIPART")

    proof_data: Optional[bytes] = None
    proof_mime = ""
    proof_name = ""
    note = ""
    note_seen = False
    for part in parts:
        if part.get_content_disposition() != "form-data":
            continue
        field_name = part.get_param("name", header="content-disposition")
        if field_name == "proof":
            if proof_data is not None:
                raise ApiError(400, "DUPLICATE_PROOF", "DUPLICATE_PROOF")
            filename = part.get_filename()
            if not filename:
                raise ApiError(400, "PROOF_FILE_REQUIRED", "PROOF_FILE_REQUIRED")
            payload = part.get_payload(decode=True)
            if not isinstance(payload, bytes):
                raise ApiError(400, "MALFORMED_MULTIPART", "MALFORMED_MULTIPART")
            if not payload:
                raise ApiError(400, "EMPTY_PROOF", "EMPTY_PROOF")
            if len(payload) > max_proof_bytes:
                raise ApiError(413, "PROOF_TOO_LARGE", "PROOF_TOO_LARGE")
            declared_header = part.get("Content-Type")
            declared = part.get_content_type().lower() if declared_header else ""
            detected = detect_proof_mime(payload)
            if detected is None:
                raise ApiError(
                    415, "UNSUPPORTED_PROOF_TYPE", "UNSUPPORTED_PROOF_TYPE"
                )
            if declared and declared not in (
                "image/png",
                "image/jpeg",
                "image/webp",
                "application/pdf",
                "application/octet-stream",
                "binary/octet-stream",
            ):
                raise ApiError(
                    415, "UNSUPPORTED_PROOF_TYPE", "UNSUPPORTED_PROOF_TYPE"
                )
            if declared not in (
                "",
                "application/octet-stream",
                "binary/octet-stream",
            ):
                if declared != detected:
                    raise ApiError(
                        415, "UNSUPPORTED_PROOF_TYPE", "UNSUPPORTED_PROOF_TYPE"
                    )
            extension = {
                "image/png": "png",
                "image/jpeg": "jpg",
                "image/webp": "webp",
                "application/pdf": "pdf",
            }[detected]
            proof_data = payload
            proof_mime = detected
            proof_name = sanitize_original_filename(
                filename, "proof.{0}".format(extension)
            )
        elif field_name == "note":
            if note_seen:
                raise ApiError(400, "DUPLICATE_NOTE", "DUPLICATE_NOTE")
            note_seen = True
            raw_note = part.get_payload(decode=True)
            if not isinstance(raw_note, bytes):
                raise ApiError(400, "INVALID_NOTE", "INVALID_NOTE")
            charset = part.get_content_charset() or "utf-8"
            try:
                note = raw_note.decode(charset)
            except (LookupError, UnicodeDecodeError) as exc:
                raise ApiError(400, "INVALID_NOTE", "INVALID_NOTE") from exc
            if len(note) > MAX_ADMIN_NOTE_LENGTH:
                raise ApiError(400, "NOTE_TOO_LONG", "NOTE_TOO_LONG")
            note = note.strip()

    if proof_data is None:
        raise ApiError(400, "PROOF_FILE_REQUIRED", "PROOF_FILE_REQUIRED")
    return proof_data, proof_mime, proof_name, note


class PremiumRequestHandler(BaseHTTPRequestHandler):
    """One request per connection; routes are thin wrappers around PremiumStore."""

    protocol_version = "HTTP/1.1"
    server_version = "NeonVelocityPremium/1.0"
    sys_version = ""
    timeout = 30

    @property
    def app(self) -> PremiumApplication:
        return self.server.app  # type: ignore[attr-defined]

    def version_string(self) -> str:
        return self.server_version

    def __getattr__(self, name: str) -> Any:
        if name.startswith("do_") and len(name) > 3:
            def unsupported_method(*_args: Any, **_kwargs: Any) -> None:
                self._handle(name[3:])

            return unsupported_method
        raise AttributeError(name)

    def send_error(
        self,
        code: int,
        message: Optional[str] = None,
        explain: Optional[str] = None,
    ) -> None:
        if getattr(self, "_response_started", False):
            self.close_connection = True
            return
        try:
            code_name = HTTPStatus(code).phrase.upper().replace(" ", "_").replace(
                "-", "_"
            )
        except ValueError:
            code_name = "HTTP_ERROR"
        try:
            self._send_api_error(code, code_name, code_name)
        except Exception:
            self.close_connection = True

    def log_message(self, format: str, *args: Any) -> None:
        logger.info(
            "%s %s %s",
            self.client_address[0],
            self.command,
            urlsplit(self.path).path,
        )

    def log_error(self, format: str, *args: Any) -> None:
        logger.warning("HTTP handler error from %s: %s", self.client_address[0], format % args)

    # ---- response primitives ----------------------------------------------------------

    def _allowed_origin(self) -> str:
        allowed = [
            item.strip()
            for item in self.app.config.allowed_origin.split(",")
            if item.strip()
        ]
        if not allowed or "*" in allowed:
            return "*"
        request_origin = self.headers.get("Origin", "")
        if request_origin and request_origin in allowed:
            return request_origin
        return allowed[0]

    def _response_headers(self) -> Dict[str, str]:
        return {
            "Access-Control-Allow-Origin": self._allowed_origin(),
            "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
            "Access-Control-Allow-Headers": "Authorization, Content-Type",
            "Access-Control-Max-Age": "600",
            "Vary": "Origin",
            "X-Content-Type-Options": "nosniff",
            "Cache-Control": "no-store",
            "Connection": "close",
        }

    def _send_json(
        self,
        status: int,
        payload: Dict[str, Any],
        extra_headers: Optional[Dict[str, str]] = None,
    ) -> None:
        data = json.dumps(
            payload, ensure_ascii=False, separators=(",", ":")
        ).encode("utf-8")
        self.send_response(status)
        for key, value in self._response_headers().items():
            self.send_header(key, value)
        if extra_headers:
            for key, value in extra_headers.items():
                self.send_header(key, value)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.close_connection = True
        self._response_started = True
        self.end_headers()
        self.wfile.write(data)

    def _send_empty(
        self, status: int, extra_headers: Optional[Dict[str, str]] = None
    ) -> None:
        self.send_response(status)
        for key, value in self._response_headers().items():
            self.send_header(key, value)
        if extra_headers:
            for key, value in extra_headers.items():
                self.send_header(key, value)
        self.send_header("Content-Length", "0")
        self.close_connection = True
        self._response_started = True
        self.end_headers()

    def _send_bytes(
        self,
        status: int,
        data: bytes,
        content_type: str,
        filename: str,
        extra_headers: Optional[Dict[str, str]] = None,
    ) -> None:
        safe_filename = filename.replace('"', "").replace("\r", "").replace("\n", "")
        self.send_response(status)
        for key, value in self._response_headers().items():
            self.send_header(key, value)
        if extra_headers:
            for key, value in extra_headers.items():
                self.send_header(key, value)
        self.send_header("Content-Type", content_type)
        self.send_header(
            "Content-Disposition",
            'attachment; filename="{0}"'.format(safe_filename),
        )
        self.send_header("Content-Length", str(len(data)))
        self.close_connection = True
        self._response_started = True
        self.end_headers()
        self.wfile.write(data)

    def _send_api_error(
        self,
        status: int,
        code: str,
        message: str,
        extra: Optional[Dict[str, Any]] = None,
        headers: Optional[Dict[str, str]] = None,
    ) -> None:
        payload: Dict[str, Any] = {
            "ok": False,
            "error": message,
            "code": code,
        }
        if extra:
            payload.update(extra)
        self._send_json(status, payload, headers)

    # ---- request helpers --------------------------------------------------------------

    def _client_ip(self) -> str:
        peer = self.client_address[0]
        if self.app.config.trust_proxy_headers:
            forwarded = self.headers.get("X-Forwarded-For", "")
            candidate = forwarded.split(",")[0].strip()
            if candidate:
                try:
                    ipaddress.ip_address(candidate)
                    return candidate
                except ValueError:
                    pass
        return peer

    def _read_body(self, maximum: int) -> bytes:
        if self.headers.get("Transfer-Encoding"):
            self.close_connection = True
            raise ApiError(
                400,
                "UNSUPPORTED_TRANSFER_ENCODING",
                "UNSUPPORTED_TRANSFER_ENCODING",
            )
        length_headers = self.headers.get_all("Content-Length") or []
        if len(length_headers) != 1:
            raise ApiError(411, "LENGTH_REQUIRED", "LENGTH_REQUIRED")
        raw_length = length_headers[0].strip()
        if not raw_length or not raw_length.isdigit():
            raise ApiError(411, "LENGTH_REQUIRED", "LENGTH_REQUIRED")
        length = int(raw_length)
        if length > maximum:
            self.close_connection = True
            raise ApiError(413, "PAYLOAD_TOO_LARGE", "PAYLOAD_TOO_LARGE")
        body = self.rfile.read(length)
        if len(body) != length:
            raise ApiError(400, "INCOMPLETE_BODY", "INCOMPLETE_BODY")
        return body

    def _read_json(self) -> Dict[str, Any]:
        content_type = self.headers.get("Content-Type", "").split(";")[0].strip().lower()
        if content_type and content_type != "application/json":
            raise ApiError(
                415, "UNSUPPORTED_MEDIA_TYPE", "UNSUPPORTED_MEDIA_TYPE"
            )
        body = self._read_body(self.app.config.max_json_body_bytes)
        if not body:
            raise ApiError(400, "INVALID_JSON", "INVALID_JSON")
        try:
            parsed = json.loads(body.decode("utf-8"))
        except (UnicodeDecodeError, ValueError) as exc:
            raise ApiError(400, "INVALID_JSON", "INVALID_JSON") from exc
        if not isinstance(parsed, dict):
            raise ApiError(400, "INVALID_JSON", "INVALID_JSON")
        return parsed

    def _bearer_token(self) -> str:
        values = self.headers.get_all("Authorization") or []
        if len(values) != 1:
            raise ApiError(401, "UNAUTHORIZED", "UNAUTHORIZED")
        parts = values[0].strip().split()
        if len(parts) != 2 or parts[0].lower() != "bearer":
            raise ApiError(401, "UNAUTHORIZED", "UNAUTHORIZED")
        token = parts[1]
        if len(token) < 16 or len(token) > 512:
            raise ApiError(401, "UNAUTHORIZED", "UNAUTHORIZED")
        return token

    def _require_account(self) -> Dict[str, Any]:
        token = self._bearer_token()
        try:
            return self.app.store.account_for_token(token)
        except AccountNotFound as exc:
            raise ApiError(401, "UNAUTHORIZED", "UNAUTHORIZED") from exc

    def _require_admin(self) -> str:
        token = self._bearer_token()
        try:
            return self.app.store.admin_for_token(token)
        except PremiumError as exc:
            raise ApiError(401, "ADMIN_UNAUTHORIZED", "ADMIN_UNAUTHORIZED") from exc

    # ---- HTTP method and routing ------------------------------------------------------

    def do_GET(self) -> None:
        self._handle("GET")

    def do_POST(self) -> None:
        self._handle("POST")

    def do_OPTIONS(self) -> None:
        self._handle("OPTIONS")

    def do_HEAD(self) -> None:
        self._handle("HEAD")

    def do_PUT(self) -> None:
        self._handle("PUT")

    def do_PATCH(self) -> None:
        self._handle("PATCH")

    def do_DELETE(self) -> None:
        self._handle("DELETE")

    def do_TRACE(self) -> None:
        self._handle("TRACE")

    def do_CONNECT(self) -> None:
        self._handle("CONNECT")

    def _handle(self, method: str) -> None:
        self._response_started = False
        try:
            parsed = urlsplit(self.path)
            path = parsed.path
            if len(path) > 1:
                path = path.rstrip("/")
            if method == "OPTIONS":
                self._send_empty(204)
                return
            if method not in ("GET", "POST"):
                raise ApiError(
                    405,
                    "METHOD_NOT_ALLOWED",
                    "METHOD_NOT_ALLOWED",
                    headers={"Allow": "GET, POST, OPTIONS"},
                )
            query = parse_qs(parsed.query, keep_blank_values=True)
            self._route(method, path, query)
        except ApiError as exc:
            if self._response_started:
                self.close_connection = True
                return
            self._send_api_error(
                exc.status, exc.code, exc.message, exc.extra, exc.headers
            )
        except PremiumError as exc:
            if self._response_started:
                self.close_connection = True
                return
            self._send_api_error(exc.status, exc.code, exc.message, exc.extra)
        except AccountError as exc:
            if self._response_started:
                self.close_connection = True
                return
            self._send_api_error(exc.status, exc.code, exc.message, exc.extra)
        except (BrokenPipeError, ConnectionResetError):
            self.close_connection = True
        except Exception:
            logger.exception(
                "Unhandled request failure method=%s path=%s",
                method,
                urlsplit(self.path).path,
            )
            if not self._response_started:
                self._send_api_error(
                    500, "INTERNAL_ERROR", "INTERNAL_ERROR"
                )

    def _route(
        self, method: str, path: str, query: Dict[str, List[str]]
    ) -> None:
        if method == "GET":
            if path == "/api/v1/health":
                self._health()
                return
            if path == "/api/v1/store/catalog":
                self._catalog()
                return
            if path == "/api/v1/wallet":
                self._wallet()
                return
            if path == "/api/v1/ownership":
                self._ownership()
                return
            if path == "/api/v1/store/payments":
                self._payment_capabilities()
                return
            if path == "/api/v1/account/profile":
                self._account_profile()
                return
            if path == "/api/v1/account/identities":
                self._account_identities()
                return
            if path == "/api/v1/account/sync":
                self._sync_get()
                return
            if path == "/api/v1/account/transactions":
                self._account_transactions(query)
                return
            if path == "/api/v1/orders":
                self._orders_list()
                return
            match = ORDER_PATH_RE.match(path)
            if match:
                self._order_detail(match.group(1))
                return
            if path == "/api/v1/admin/orders":
                self._admin_orders(query)
                return
            match = ADMIN_ORDER_PROOF_PATH_RE.match(path)
            if match:
                self._admin_proof(match.group(1))
                return
            if path == "/api/v1/admin/ledger":
                self._admin_ledger(query)
                return
            if path == "/api/v1/admin/stats":
                self._admin_stats()
                return
            if path == "/api/v1/admin/transactions":
                self._admin_transactions(query)
                return
            if path == "/api/v1/admin/accounts":
                self._admin_accounts(query)
                return
            match = ADMIN_ACCOUNT_PATH_RE.match(path)
            if match:
                self._admin_account_detail(match.group(1))
                return
            if path == "/api/v1/admin/catalog":
                self._admin_catalog()
                return
            raise ApiError(404, "NOT_FOUND", "NOT_FOUND")

        if path == "/api/v1/auth/guest":
            self._guest_login()
            return
        if path == "/api/v1/auth/register":
            self._account_register()
            return
        if path == "/api/v1/auth/login":
            self._account_login()
            return
        if path == "/api/v1/auth/logout":
            self._account_logout()
            return
        if path == "/api/v1/auth/link":
            self._account_link()
            return
        if path == "/api/v1/auth/email/verify":
            self._account_verify_email()
            return
        if path == "/api/v1/auth/password/request":
            self._account_password_request()
            return
        if path == "/api/v1/auth/password/reset":
            self._account_password_reset()
            return
        if path == "/api/v1/auth/phone/start":
            self._account_phone_start()
            return
        if path == "/api/v1/auth/phone/verify":
            self._account_phone_verify()
            return
        if path == "/api/v1/auth/oauth":
            self._account_oauth()
            return
        if path == "/api/v1/account/profile":
            self._account_profile_update()
            return
        if path == "/api/v1/account/email":
            self._account_email_change()
            return
        if path == "/api/v1/account/sync":
            self._sync_merge()
            return
        if path == "/api/v1/payments/checkout":
            self._payment_checkout()
            return
        if path == "/api/v1/payments/stripe/webhook":
            self._payment_stripe_webhook()
            return
        if path == "/api/v1/payments/apple":
            self._payment_apple()
            return
        if path == "/api/v1/payments/google":
            self._payment_google()
            return
        if path == "/api/v1/orders":
            self._order_create()
            return
        match = ORDER_PROOF_PATH_RE.match(path)
        if match:
            self._order_proof(match.group(1))
            return
        if path == "/api/v1/purchases":
            self._purchase()
            return
        if path == "/api/v1/revive":
            self._revive()
            return
        if path == "/api/v1/admin/login":
            self._admin_login()
            return
        match = ADMIN_ORDER_DECISION_PATH_RE.match(path)
        if match:
            self._admin_decision(match.group(1))
            return
        match = ADMIN_TRANSACTION_REFUND_PATH_RE.match(path)
        if match:
            self._admin_refund_transaction(int(match.group(1)))
            return
        match = ADMIN_CATALOG_PATH_RE.match(path)
        if match:
            self._admin_catalog_update(
                "package" if match.group(1) == "packages" else "product",
                match.group(2),
            )
            return
        raise ApiError(404, "NOT_FOUND", "NOT_FOUND")

    # ---- public and player routes -----------------------------------------------------

    def _health(self) -> None:
        self._send_json(
            200,
            {
                "ok": True,
                "service": SERVICE_NAME,
                "version": SERVER_VERSION,
                "time": iso_utc(),
            },
        )

    def _catalog(self) -> None:
        snapshot = self.app.store.catalog_snapshot()
        self._send_json(
            200,
            {
                "ok": True,
                "packages": snapshot["packages"],
                "products": snapshot["products"],
                "reviveCostGems": snapshot["reviveCostGems"],
            },
        )

    def _guest_login(self) -> None:
        ip = self._client_ip()
        if not self.app.guest_limiter.allow(ip):
            raise ApiError(
                429,
                "RATE_LIMITED",
                "RATE_LIMITED",
                headers={
                    "Retry-After": str(self.app.config.rate_window_seconds)
                },
            )
        payload = self._read_json()
        device_key = require_device_key(payload)
        display_name = sanitize_display_name(payload.get("playerName"))
        result = self.app.store.authenticate_guest(
            device_key,
            display_name,
            self.app.config.auth_token_ttl_seconds,
        )
        self._send_json(
            200,
            {
                "ok": True,
                "token": result["token"],
                "accountId": result["accountId"],
                "displayName": result["displayName"],
                "gems": int(result["gems"]),
            },
        )

    def _wallet(self) -> None:
        account = self._require_account()
        wallet = self.app.store.wallet(str(account["accountId"]))
        self._send_json(
            200,
            {
                "ok": True,
                "gems": wallet["gems"],
                "pendingOrders": wallet["pendingOrders"],
                "serverTime": iso_utc(),
            },
        )

    # ---- account and identity routes --------------------------------------------------

    def _auth_rate_guard(self) -> None:
        if not self.app.auth_limiter.allow(self._client_ip()):
            raise ApiError(
                429,
                "RATE_LIMITED",
                "RATE_LIMITED",
                headers={"Retry-After": str(self.app.config.rate_window_seconds)},
            )

    def _account_register(self) -> None:
        self._auth_rate_guard()
        payload = self._read_json()
        session = self.app.accounts.register(
            payload.get("email"), payload.get("password"), payload.get("playerName")
        )
        self._send_json(200, session)

    def _account_login(self) -> None:
        self._auth_rate_guard()
        payload = self._read_json()
        session = self.app.accounts.login(
            payload.get("email"), payload.get("password")
        )
        self._send_json(200, session)

    def _account_logout(self) -> None:
        account = self._require_account()
        token = self._bearer_token()
        result = self.app.accounts.logout(token)
        result["accountId"] = str(account["accountId"])
        self._send_json(200, result)

    def _account_link(self) -> None:
        self._auth_rate_guard()
        account = self._require_account()
        payload = self._read_json()
        session = self.app.accounts.link_credentials(
            str(account["accountId"]),
            payload.get("email"),
            payload.get("password"),
            payload.get("playerName"),
        )
        self._send_json(200, session)

    def _account_verify_email(self) -> None:
        self._auth_rate_guard()
        payload = self._read_json()
        profile = self.app.accounts.verify_email(payload.get("token"))
        self._send_json(200, profile)

    def _account_password_request(self) -> None:
        self._auth_rate_guard()
        payload = self._read_json()
        result = self.app.accounts.request_password_reset(payload.get("email"))
        self._send_json(200, result)

    def _account_password_reset(self) -> None:
        self._auth_rate_guard()
        payload = self._read_json()
        result = self.app.accounts.reset_password(
            payload.get("token"), payload.get("password")
        )
        self._send_json(200, result)

    def _account_phone_start(self) -> None:
        self._auth_rate_guard()
        payload = self._read_json()
        result = self.app.accounts.request_phone_code(payload.get("phone"))
        self._send_json(200, result)

    def _account_phone_verify(self) -> None:
        self._auth_rate_guard()
        payload = self._read_json()
        session = self.app.accounts.verify_phone_code(
            payload.get("challengeId"),
            payload.get("code"),
            payload.get("playerName"),
        )
        self._send_json(200, session)

    def _account_oauth(self) -> None:
        self._auth_rate_guard()
        payload = self._read_json()
        session = self.app.accounts.oauth_login(
            payload.get("provider"),
            payload.get("idToken"),
            payload.get("playerName"),
        )
        self._send_json(200, session)

    def _account_profile(self) -> None:
        account = self._require_account()
        self._send_json(
            200, self.app.accounts.profile(str(account["accountId"]))
        )

    def _account_profile_update(self) -> None:
        account = self._require_account()
        payload = self._read_json()
        display_name = payload.get("displayName", account["displayName"])
        profile = self.app.accounts.update_profile(
            str(account["accountId"]), display_name, payload.get("receiptEmail")
        )
        self._send_json(200, profile)

    def _account_email_change(self) -> None:
        self._auth_rate_guard()
        account = self._require_account()
        payload = self._read_json()
        profile = self.app.accounts.change_email(
            str(account["accountId"]), payload.get("email")
        )
        self._send_json(200, profile)

    def _account_identities(self) -> None:
        account = self._require_account()
        self._send_json(
            200,
            {
                "ok": True,
                "identities": self.app.accounts.identities(str(account["accountId"])),
            },
        )

    def _sync_get(self) -> None:
        account = self._require_account()
        self._send_json(
            200, self.app.accounts.get_sync(str(account["accountId"]))
        )

    def _sync_merge(self) -> None:
        account = self._require_account()
        payload = self._read_json()
        result = self.app.accounts.merge_sync(
            str(account["accountId"]),
            payload.get("snapshot"),
            payload.get("baseRevision", 0),
            payload.get("clientRequestId"),
        )
        self._send_json(200, result)

    def _account_transactions(self, query: Dict[str, List[str]]) -> None:
        account = self._require_account()
        raw_limit = (query.get("limit") or ["100"])[0].strip()
        if not raw_limit.isdigit():
            raise ApiError(400, "INVALID_LIMIT", "INVALID_LIMIT")
        limit = int(raw_limit)
        if limit < 1 or limit > 200:
            raise ApiError(400, "INVALID_LIMIT", "INVALID_LIMIT")
        account_id = str(account["accountId"])
        self._send_json(
            200,
            {
                "ok": True,
                "transactions": self.app.accounts.transactions(account_id, limit),
                "orders": self.app.store.list_orders(account_id),
            },
        )

    def _payment_capabilities(self) -> None:
        self._send_json(
            200,
            {
                "ok": True,
                "payments": self.app.payment_capabilities(),
                "reviveCostGems": REVIVE_COST_GEMS,
            },
        )

    def _payment_checkout(self) -> None:
        account = self._require_account()
        payload = self._read_json()
        package_id = require_text(
            payload, "packageId", 1, 64, "INVALID_PACKAGE"
        )
        client_request_id = require_idempotency_key(payload)
        receipt_email = payload.get("receiptEmail", "")
        if receipt_email is None:
            receipt_email = ""
        if not isinstance(receipt_email, str):
            raise ApiError(400, "INVALID_EMAIL", "INVALID_EMAIL")
        if receipt_email.strip():
            receipt_email = self.app.accounts.normalize_email(receipt_email)
        success_url = payload.get("successUrl", "")
        cancel_url = payload.get("cancelUrl", "")
        for value in (success_url, cancel_url):
            if not isinstance(value, str) or len(value) > 2048:
                raise ApiError(400, "INVALID_REDIRECT", "INVALID_REDIRECT")
        result = self.app.stripe.create_checkout(
            str(account["accountId"]),
            package_id,
            receipt_email,
            success_url,
            cancel_url,
            client_request_id,
        )
        self._send_json(200, result)

    def _payment_stripe_webhook(self) -> None:
        body = self._read_body(self.app.config.max_json_body_bytes)
        signature = self.headers.get("Stripe-Signature", "")
        result = self.app.stripe.handle_webhook(body, signature)
        self._send_json(200, result)

    def _payment_apple(self) -> None:
        account = self._require_account()
        payload = self._read_json()
        receipt = require_text(
            payload, "receiptBase64", 1, 700_000, "INVALID_RECEIPT"
        )
        product_id = require_text(
            payload, "productId", 1, 200, "INVALID_PRODUCT"
        )
        receipt_email = self._optional_receipt_email(payload)
        result = self.app.apple.verify_and_credit(
            str(account["accountId"]), receipt, product_id, receipt_email
        )
        self._send_json(200, result)

    def _payment_google(self) -> None:
        account = self._require_account()
        payload = self._read_json()
        purchase_token = require_text(
            payload, "purchaseToken", 1, 4096, "INVALID_RECEIPT"
        )
        product_id = require_text(
            payload, "productId", 1, 200, "INVALID_PRODUCT"
        )
        receipt_email = self._optional_receipt_email(payload)
        result = self.app.google_play.verify_and_credit(
            str(account["accountId"]), purchase_token, product_id, receipt_email
        )
        self._send_json(200, result)

    def _optional_receipt_email(self, payload: Dict[str, Any]) -> str:
        value = payload.get("receiptEmail", "")
        if value is None:
            return ""
        if not isinstance(value, str):
            raise ApiError(400, "INVALID_EMAIL", "INVALID_EMAIL")
        return self.app.accounts.normalize_email(value) if value.strip() else ""

    def _ownership(self) -> None:
        account = self._require_account()
        ownership = self.app.store.ownership(str(account["accountId"]))
        self._send_json(
            200,
            {
                "ok": True,
                "items": ownership["items"],
                "characters": ownership["characters"],
            },
        )

    def _order_create(self) -> None:
        self.app.require_manual_transfer()
        account = self._require_account()
        payload = self._read_json()
        package_id = require_text(payload, "packageId", 1, 64, "INVALID_PACKAGE")
        client_request_id = require_idempotency_key(payload)
        order, bank = self.app.store.create_order(
            str(account["accountId"]), package_id, client_request_id
        )
        self._send_json(200, {"ok": True, "order": order, "bank": bank})

    def _orders_list(self) -> None:
        account = self._require_account()
        orders = self.app.store.list_orders(str(account["accountId"]))
        self._send_json(200, {"ok": True, "orders": orders})

    def _order_detail(self, order_id: str) -> None:
        account = self._require_account()
        order = self.app.store.get_order(str(account["accountId"]), order_id)
        self._send_json(200, {"ok": True, "order": order})

    def _order_proof(self, order_id: str) -> None:
        self.app.require_manual_transfer()
        account = self._require_account()
        content_type = self.headers.get("Content-Type", "")
        if not content_type.lower().startswith("multipart/form-data"):
            raise ApiError(
                415, "UNSUPPORTED_MEDIA_TYPE", "UNSUPPORTED_MEDIA_TYPE"
            )
        maximum = (
            self.app.config.max_proof_bytes + MAX_PROOF_OVERHEAD_BYTES
        )
        body = self._read_body(maximum)
        proof, mime, original_name, note = parse_multipart_proof(
            content_type, body, self.app.config.max_proof_bytes
        )
        order, _old_file = self.app.store.submit_proof(
            str(account["accountId"]),
            order_id,
            proof,
            mime,
            original_name,
            note,
        )
        self.app.notify_proof_submitted(order)
        self._send_json(200, {"ok": True, "order": order})

    def _purchase(self) -> None:
        account = self._require_account()
        payload = self._read_json()
        product_id = require_text(payload, "productId", 1, 64, "INVALID_PRODUCT")
        client_request_id = require_idempotency_key(payload)
        result = self.app.store.purchase(
            str(account["accountId"]), product_id, client_request_id
        )
        self._send_json(200, result)

    def _revive(self) -> None:
        account = self._require_account()
        payload = self._read_json()
        session_id = require_text(payload, "sessionId", 1, 128, "INVALID_SESSION")
        if not SESSION_ID_RE.match(session_id):
            raise ApiError(400, "INVALID_SESSION", "INVALID_SESSION")
        distance_value = payload.get("distance")
        if (
            isinstance(distance_value, bool)
            or not isinstance(distance_value, (int, float))
        ):
            raise ApiError(400, "INVALID_DISTANCE", "INVALID_DISTANCE")
        distance = float(distance_value)
        if not (distance == distance) or distance in (
            float("inf"),
            float("-inf"),
        ):
            raise ApiError(400, "INVALID_DISTANCE", "INVALID_DISTANCE")
        if distance < 0.0 or distance > 1_000_000_000.0:
            raise ApiError(400, "INVALID_DISTANCE", "INVALID_DISTANCE")
        location_value = payload.get("locationIndex")
        if isinstance(location_value, bool) or not isinstance(location_value, int):
            raise ApiError(400, "INVALID_LOCATION", "INVALID_LOCATION")
        if location_value < 0 or location_value > 1_000_000:
            raise ApiError(400, "INVALID_LOCATION", "INVALID_LOCATION")
        client_request_id = require_idempotency_key(payload)
        result = self.app.store.revive(
            str(account["accountId"]),
            session_id,
            distance,
            location_value,
            client_request_id,
        )
        self._send_json(200, result)

    # ---- admin routes -----------------------------------------------------------------

    def _admin_login(self) -> None:
        if not self.app.admin_configured:
            raise ApiError(
                503, "ADMIN_NOT_CONFIGURED", "ADMIN_NOT_CONFIGURED"
            )
        ip = self._client_ip()
        if self.app.admin_failure_limiter.is_limited(ip):
            raise ApiError(
                429,
                "RATE_LIMITED",
                "RATE_LIMITED",
                headers={
                    "Retry-After": str(self.app.config.rate_window_seconds)
                },
            )
        payload = self._read_json()
        username = require_text(
            payload, "username", 1, 128, "INVALID_CREDENTIALS"
        )
        password = require_text(
            payload, "password", 1, 256, "INVALID_CREDENTIALS"
        )
        if not self.app.check_admin_credentials(username, password):
            self.app.admin_failure_limiter.record_failure(ip)
            try:
                self.app.store.record_admin_audit(
                    username,
                    "login_failed",
                    None,
                    "Rejected admin login",
                    ip,
                )
            except Exception:
                logger.exception("Could not write failed admin login audit")
            raise ApiError(
                401, "INVALID_CREDENTIALS", "INVALID_CREDENTIALS"
            )

        self.app.admin_failure_limiter.clear(ip)
        token, expires = self.app.store.create_admin_token(
            username, self.app.config.admin_token_ttl_seconds
        )
        try:
            self.app.store.record_admin_audit(
                username, "login_success", None, "Admin session started", ip
            )
        except Exception:
            logger.exception("Could not write admin login audit")
        self._send_json(
            200, {"ok": True, "token": token, "expiresUtc": expires}
        )

    def _admin_orders(self, query: Dict[str, List[str]]) -> None:
        self._require_admin()
        values = query.get("status", [])
        status: Optional[str] = None
        if values:
            candidate = values[0].strip().upper()
            if candidate:
                if candidate not in ORDER_STATUSES:
                    raise ApiError(400, "INVALID_STATUS", "INVALID_STATUS")
                status = candidate
        orders = self.app.store.admin_list_orders(status)
        self._send_json(200, {"ok": True, "orders": orders})

    def _admin_proof(self, order_id: str) -> None:
        self._require_admin()
        path, mime, download_name = self.app.store.admin_proof(order_id)
        try:
            data = path.read_bytes()
        except OSError as exc:
            raise ProofNotFound() from exc
        self._send_bytes(200, data, mime, download_name)

    def _admin_decision(self, order_id: str) -> None:
        admin_username = self._require_admin()
        payload = self._read_json()
        decision = payload.get("decision")
        if not isinstance(decision, str) or decision.strip().lower() not in (
            "approve",
            "reject",
        ):
            raise ApiError(400, "INVALID_DECISION", "INVALID_DECISION")
        note = optional_note(payload, "note")
        approve = decision.strip().lower() == "approve"
        order, gems = self.app.store.decide_order(
            order_id,
            approve,
            note,
            admin_username,
            self._client_ip(),
        )
        self._send_json(200, {"ok": True, "order": order, "gems": gems})

    def _admin_ledger(self, query: Dict[str, List[str]]) -> None:
        self._require_admin()
        values = query.get("limit", [])
        raw_limit = values[0].strip() if values else ""
        if not raw_limit:
            limit = 100
        else:
            if not raw_limit.isdigit():
                raise ApiError(400, "INVALID_LIMIT", "INVALID_LIMIT")
            limit = int(raw_limit)
            if limit < 1 or limit > 500:
                raise ApiError(400, "INVALID_LIMIT", "INVALID_LIMIT")
        entries = self.app.store.ledger(limit)
        self._send_json(200, {"ok": True, "entries": entries})

    def _admin_stats(self) -> None:
        self._require_admin()
        stats = self.app.store.stats()
        payload = {"ok": True}
        payload.update(stats)
        self._send_json(200, payload)

    @staticmethod
    def _query_limit(
        query: Dict[str, List[str]], default: int = 100, maximum: int = 500
    ) -> int:
        raw = (query.get("limit") or [str(default)])[0].strip()
        if not raw.isdigit():
            raise ApiError(400, "INVALID_LIMIT", "INVALID_LIMIT")
        limit = int(raw)
        if limit < 1 or limit > maximum:
            raise ApiError(400, "INVALID_LIMIT", "INVALID_LIMIT")
        return limit

    def _admin_transactions(self, query: Dict[str, List[str]]) -> None:
        self._require_admin()
        limit = self._query_limit(query)
        provider = (query.get("provider") or [""])[0].strip().lower()
        status = (query.get("status") or [""])[0].strip().upper()
        if provider and not re.match(r"^[a-z_]{1,32}$", provider):
            raise ApiError(400, "INVALID_PROVIDER", "INVALID_PROVIDER")
        if status and not re.match(r"^[A-Z_]{1,32}$", status):
            raise ApiError(400, "INVALID_STATUS", "INVALID_STATUS")
        transactions = self.app.accounts.admin_transactions(limit, provider, status)
        self._send_json(
            200,
            {
                "ok": True,
                "transactions": transactions,
                "count": len(transactions),
            },
        )

    def _admin_accounts(self, query: Dict[str, List[str]]) -> None:
        self._require_admin()
        limit = self._query_limit(query)
        search = (query.get("q") or [""])[0].strip()[:120]
        accounts = self.app.accounts.admin_accounts(search, limit)
        self._send_json(
            200, {"ok": True, "accounts": accounts, "count": len(accounts)}
        )

    def _admin_account_detail(self, account_id: str) -> None:
        self._require_admin()
        try:
            detail = self.app.accounts.admin_account_detail(account_id)
        except AccountError as exc:
            raise ApiError(exc.status, exc.code, exc.message, exc.extra) from exc
        self._send_json(200, {"ok": True, "account": detail})

    def _admin_catalog(self) -> None:
        self._require_admin()
        snapshot = self.app.store.catalog_snapshot()
        snapshot["manualBankTransfer"] = bool(self.app.config.manual_bank_transfer)
        snapshot["ok"] = True
        self._send_json(200, snapshot)

    def _admin_catalog_update(self, kind: str, item_id: str) -> None:
        admin_username = self._require_admin()
        payload = self._read_json()
        fields: Dict[str, Any] = {}
        for key in ("name", "description", "price", "enabled"):
            if key in payload:
                fields[key] = payload[key]
        try:
            updated = self.app.store.set_catalog_override(
                kind, item_id, **fields
            )
        except PremiumError as exc:
            raise ApiError(exc.status, exc.code, exc.message, exc.extra) from exc
        detail = "catalog {0} {1}: {2}".format(
            kind,
            item_id,
            json.dumps(fields, separators=(",", ":")) if fields else "{}",
        )
        self.app.store.record_admin_audit(
            admin_username, "catalog_update", None, detail, self._client_ip()
        )
        self._send_json(
            200,
            {
                "ok": True,
                "kind": kind,
                "item": updated,
                "catalog": self.app.store.catalog_snapshot(),
            },
        )

    def _admin_refund_transaction(self, transaction_id: int) -> None:
        admin_username = self._require_admin()
        payload = self._read_json()
        if payload.get("confirmed") is not True:
            raise ApiError(
                400,
                "REFUND_NOT_CONFIRMED",
                "Refund the payment at the provider first, then confirm the wallet reversal.",
            )
        note = optional_note(payload, "note") or "operator refund"
        transaction = self.app.accounts.transaction_by_id(transaction_id)
        if transaction is None:
            raise ApiError(404, "TRANSACTION_NOT_FOUND", "TRANSACTION_NOT_FOUND")
        if transaction["status"] not in ("SUCCEEDED", "PARTIALLY_REFUNDED"):
            raise ApiError(
                409,
                "TRANSACTION_NOT_REFUNDABLE",
                "Only a successful or partially refunded transaction can be reversed.",
            )
        if int(transaction["gems"]) <= 0:
            raise ApiError(
                409,
                "TRANSACTION_NOT_REFUNDABLE",
                "This transaction did not credit GEMS.",
            )
        balance = self.app.store.debit_gems_for_refund(
            str(transaction["accountId"]),
            int(transaction["gems"]),
            "Admin refund #{0} {1}".format(transaction_id, note)[:480],
            order_id=str(transaction.get("eventId") or "")[:120] or None,
        )
        updated = self.app.accounts.mark_transaction_status(
            transaction_id,
            "REFUNDED",
            "admin wallet reversal: " + note,
        )
        self.app.store.record_admin_audit(
            admin_username,
            "transaction_refund",
            None,
            "transaction #{0} {1} provider={2} gems={3}".format(
                transaction_id,
                note,
                transaction.get("provider"),
                transaction.get("gems"),
            ),
            self._client_ip(),
        )
        self._send_json(
            200,
            {
                "ok": True,
                "gems": balance,
                "transaction": updated,
                "providerRefundRequired": str(transaction.get("provider"))
                in ("stripe", "apple", "google_play"),
            },
        )


class PremiumHTTPServer(ThreadingHTTPServer):
    """Threaded HTTP server carrying the application state."""

    daemon_threads = True
    allow_reuse_address = True
    request_queue_size = 32

    def __init__(
        self,
        server_address: Tuple[str, int],
        app: PremiumApplication,
    ) -> None:
        self.app = app
        super().__init__(server_address, PremiumRequestHandler)


class PremiumServer:
    """Lifecycle wrapper used by the CLI and integration tests."""

    def __init__(
        self, config: PremiumConfig, app: Optional[PremiumApplication] = None
    ) -> None:
        self.config = config
        self.app = app if app is not None else PremiumApplication(config)
        self._httpd = PremiumHTTPServer(
            (config.host, int(config.port)), self.app
        )
        self._thread: Optional[threading.Thread] = None
        self._start_lock = threading.Lock()

    @property
    def address(self) -> Tuple[str, int]:
        host, port = self._httpd.server_address[:2]
        return str(host), int(port)

    @property
    def port(self) -> int:
        return self.address[1]

    @property
    def store(self) -> PremiumStore:
        return self.app.store

    def start(self) -> None:
        with self._start_lock:
            if self._thread is not None and self._thread.is_alive():
                return
            self._thread = threading.Thread(
                target=self._httpd.serve_forever,
                kwargs={"poll_interval": 0.1},
                name="premium-http",
                daemon=True,
            )
            self._thread.start()

    def serve_forever(self) -> None:
        self._httpd.serve_forever(poll_interval=0.2)

    def stop(self) -> None:
        with self._start_lock:
            thread = self._thread
            self._thread = None
        if thread is not None and thread.is_alive():
            self._httpd.shutdown()
            thread.join(timeout=10.0)
        self._httpd.server_close()

    def __enter__(self) -> "PremiumServer":
        self.start()
        return self

    def __exit__(self, exc_type: Any, exc: Any, traceback: Any) -> None:
        self.stop()


def build_argument_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="NEON VELOCITY premium GEM and entitlement service"
    )
    parser.add_argument(
        "--host",
        default=None,
        help="Bind address (default: NV_PREMIUM_HOST or 127.0.0.1)",
    )
    parser.add_argument(
        "--port",
        type=int,
        default=None,
        help="TCP port (default: NV_PREMIUM_PORT or 8088)",
    )
    parser.add_argument(
        "--data-dir",
        default=None,
        help="SQLite and proof directory (default: NV_PREMIUM_DATA_DIR or Backend/premium_data)",
    )
    parser.add_argument(
        "--init-only",
        action="store_true",
        help="Create the database schema and directories, then exit",
    )
    parser.add_argument(
        "--log-level",
        default=os.environ.get("NV_LOG_LEVEL", "INFO").upper(),
        choices=("DEBUG", "INFO", "WARNING", "ERROR"),
        help="Logging verbosity (default: INFO)",
    )
    return parser


def main(argv: Optional[List[str]] = None) -> int:
    args = build_argument_parser().parse_args(argv)
    logging.basicConfig(
        level=getattr(logging, args.log_level),
        format="%(asctime)s %(levelname)s %(name)s %(message)s",
    )
    try:
        config = PremiumConfig.from_env(
            host=args.host,
            port=args.port,
            data_dir=(
                pathlib.Path(args.data_dir).expanduser()
                if args.data_dir
                else None
            ),
        )
    except ValueError as exc:
        logger.error("Invalid configuration: %s", exc)
        return 2

    if args.init_only:
        try:
            store = PremiumStore(config.data_dir)
        except (OSError, RuntimeError) as exc:
            logger.error("Could not initialise the premium store: %s", exc)
            return 1
        logger.info("Premium database initialised at %s", store.db_path)
        return 0

    try:
        server = PremiumServer(config)
    except OSError as exc:
        logger.error("Could not bind %s:%s: %s", config.host, config.port, exc)
        return 1
    except RuntimeError as exc:
        logger.error("Could not initialise the premium store: %s", exc)
        return 1
    if not server.app.admin_configured:
        logger.warning(
            "Admin API is disabled: set NV_ADMIN_USERNAME and "
            "NV_ADMIN_PASSWORD or NV_ADMIN_PASSWORD_HASH"
        )
    server.start()
    host, port = server.address
    logger.info(
        "Premium API listening on http://%s:%s (data directory %s)",
        host,
        port,
        config.data_dir,
    )
    stop_event = threading.Event()

    def request_stop(signum: int, frame: Any) -> None:
        logger.info("Shutdown requested by signal %s", signum)
        stop_event.set()

    previous_handlers: Dict[int, Any] = {}
    for sig in (signal.SIGINT, signal.SIGTERM):
        previous_handlers[sig] = signal.getsignal(sig)
        signal.signal(sig, request_stop)
    try:
        stop_event.wait()
    except KeyboardInterrupt:
        stop_event.set()
    finally:
        server.stop()
        for sig, previous in previous_handlers.items():
            signal.signal(sig, previous)
    logger.info("Premium API stopped")
    return 0


if __name__ == "__main__":
    sys.exit(main())
