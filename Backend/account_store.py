#!/usr/bin/env python3
"""Account, identity and offline-to-online sync for NEON VELOCITY.

This module is intentionally dependency-free and reuses the premium store's SQLite wallet
and bearer-token authority. It adds:

* email/password registration with verification and password reset;
* phone one-time-password sign-in (the SMS sender is injected and may be unconfigured);
* Apple/Google identity linking through an injected verified-subject callback;
* guest-to-account linking;
* a monotonic offline-to-online progression merge that never trusts client GEMS;
* payment transaction records used by the provider adapters.

The service never stores raw passwords, OTP codes or bearer tokens. It stores PBKDF2
password verifiers, HMAC-protected one-time tokens and transaction records only.
"""

from __future__ import annotations

import datetime as dt
import hashlib
import hmac
import json
import math
import pathlib
import re
import secrets
import sqlite3
import threading
from typing import Any, Callable, Dict, Iterable, List, Optional, Tuple

from premium_store import (
    PremiumStore,
    hash_password,
    iso_utc,
    verify_password,
)

EMAIL_RE = re.compile(r"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+$")
PHONE_RE = re.compile(r"^\+?[0-9][0-9 .()\-]{5,20}$")
CODE_RE = re.compile(r"^[0-9]{6}$")
MAX_EMAIL_LENGTH = 254
MAX_PASSWORD_LENGTH = 128
MIN_PASSWORD_LENGTH = 10
MAX_SNAPSHOT_BYTES = 128 * 1024
MAX_SYNC_ITEMS = 400
MAX_MISSION_ITEMS = 80
MAX_STRING_LENGTH = 96
XP_HARD_CAP = 50_000_000
CREDITS_HARD_CAP = 1_000_000_000
STATE_SCHEMA = 1
EMAIL_TOKEN_TTL_SECONDS = 24 * 60 * 60
RESET_TOKEN_TTL_SECONDS = 60 * 60
OTP_TTL_SECONDS = 10 * 60
OTP_MAX_ATTEMPTS = 5
OTP_RESEND_SECONDS = 30
SUPPORTED_PROVIDERS = ("apple", "google")

EmailSender = Callable[[str, str, str], bool]
SmsSender = Callable[[str, str], bool]
OAuthVerifier = Callable[[str, str], Dict[str, Any]]


class AccountError(Exception):
    """A domain error carrying an HTTP status and stable machine-readable code."""

    def __init__(
        self,
        status: int,
        code: str,
        message: Optional[str] = None,
        extra: Optional[Dict[str, Any]] = None,
    ) -> None:
        super().__init__(message or code)
        self.status = status
        self.code = code
        self.message = message or code
        self.extra = dict(extra or {})


def _now_utc() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc)


SCHEMA_SQL = """
CREATE TABLE IF NOT EXISTS auth_accounts (
    account_id TEXT PRIMARY KEY,
    email TEXT UNIQUE,
    email_verified INTEGER NOT NULL DEFAULT 0,
    phone TEXT UNIQUE,
    phone_verified INTEGER NOT NULL DEFAULT 0,
    password_hash TEXT,
    display_name TEXT NOT NULL,
    provider TEXT NOT NULL DEFAULT '',
    provider_subject TEXT NOT NULL DEFAULT '',
    receipt_email TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS auth_identities (
    provider TEXT NOT NULL,
    subject TEXT NOT NULL,
    account_id TEXT NOT NULL,
    email TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    PRIMARY KEY (provider, subject)
);
CREATE INDEX IF NOT EXISTS idx_auth_identities_account ON auth_identities(account_id);

CREATE TABLE IF NOT EXISTS auth_one_time_tokens (
    token_hash TEXT PRIMARY KEY,
    account_id TEXT NOT NULL,
    kind TEXT NOT NULL,
    email TEXT NOT NULL DEFAULT '',
    expires_utc TEXT NOT NULL,
    used INTEGER NOT NULL DEFAULT 0,
    created_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_auth_tokens_account ON auth_one_time_tokens(account_id);
CREATE INDEX IF NOT EXISTS idx_auth_tokens_kind ON auth_one_time_tokens(kind, expires_utc);

CREATE TABLE IF NOT EXISTS auth_otp (
    challenge_id TEXT PRIMARY KEY,
    phone TEXT NOT NULL,
    code_hash TEXT NOT NULL,
    attempts INTEGER NOT NULL DEFAULT 0,
    expires_utc TEXT NOT NULL,
    used INTEGER NOT NULL DEFAULT 0,
    created_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_auth_otp_phone ON auth_otp(phone, created_utc DESC);

CREATE TABLE IF NOT EXISTS cloud_sync (
    account_id TEXT PRIMARY KEY,
    revision INTEGER NOT NULL,
    snapshot_json TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sync_requests (
    account_id TEXT NOT NULL,
    request_id TEXT NOT NULL,
    revision INTEGER NOT NULL,
    created_utc TEXT NOT NULL,
    PRIMARY KEY (account_id, request_id)
);

CREATE TABLE IF NOT EXISTS payment_transactions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider TEXT NOT NULL,
    event_id TEXT NOT NULL,
    account_id TEXT NOT NULL,
    package_id TEXT NOT NULL,
    gems INTEGER NOT NULL,
    amount_minor INTEGER NOT NULL DEFAULT 0,
    currency TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL,
    receipt_email TEXT NOT NULL DEFAULT '',
    provider_ref TEXT NOT NULL DEFAULT '',
    detail TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    UNIQUE (provider, event_id)
);
CREATE INDEX IF NOT EXISTS idx_payment_account ON payment_transactions(account_id, id DESC);
"""


class AccountStore:
    """Persistent account identities and cloud-sync state.

    Parameters
    ----------
    data_dir:
        Directory shared with the premium store. The account database is a sibling file so
        a wallet backup can include both without schema coupling.
    premium_store:
        Authority for wallet balances and bearer tokens.
    email_sender / sms_sender:
        Optional delivery adapters. When absent, responses explicitly report
        ``SMTP_NOT_CONFIGURED`` or ``SMS_NOT_CONFIGURED`` and no message is claimed as sent.
    oauth_verifier:
        Optional callback ``(provider, id_token) -> {subject, email, emailVerified}``. When
        absent, Apple/Google routes return ``PROVIDER_NOT_CONFIGURED``.
    dev_mode:
        Test/development only. Returns one-time tokens and OTP codes in API responses so a
        local integration can complete without email/SMS. Must stay off in production.
    now:
        Injectable clock used by tests.
    """

    def __init__(
        self,
        data_dir: pathlib.Path,
        premium_store: PremiumStore,
        email_sender: Optional[EmailSender] = None,
        sms_sender: Optional[SmsSender] = None,
        oauth_verifier: Optional[OAuthVerifier] = None,
        dev_mode: bool = False,
        now: Optional[Callable[[], dt.datetime]] = None,
    ) -> None:
        self.data_dir = pathlib.Path(data_dir)
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.db_path = self.data_dir / "accounts.sqlite3"
        self.premium = premium_store
        self.email_sender = email_sender
        self.sms_sender = sms_sender
        self.oauth_verifier = oauth_verifier
        self.dev_mode = bool(dev_mode)
        self._now = now or _now_utc
        self._secret = premium_store.derive_secret("neon-velocity-account-v1")
        self._lock = threading.RLock()
        self._initialize()
        self._chmod_private(self.db_path)

    # ---- storage ----------------------------------------------------------------------

    @staticmethod
    def _chmod_private(path: pathlib.Path) -> None:
        try:
            path.chmod(0o600)
        except OSError:
            pass

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(str(self.db_path), timeout=15.0)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        connection.execute("PRAGMA busy_timeout = 15000")
        return connection

    def _initialize(self) -> None:
        with self._connect() as connection:
            connection.execute("PRAGMA journal_mode = WAL")
            connection.executescript(SCHEMA_SQL)
            columns = {
                str(row["name"])
                for row in connection.execute("PRAGMA table_info(payment_transactions)")
            }
            if "provider_ref" not in columns:
                connection.execute(
                    "ALTER TABLE payment_transactions ADD COLUMN provider_ref "
                    "TEXT NOT NULL DEFAULT ''"
                )
        for suffix in ("-wal", "-shm"):
            candidate = pathlib.Path(str(self.db_path) + suffix)
            if candidate.exists():
                self._chmod_private(candidate)

    def close(self) -> None:
        # SQLite connections are short-lived; this method exists for symmetry and future
        # connection pooling. It intentionally performs no destructive work.
        return None

    # ---- crypto helpers ---------------------------------------------------------------

    def _token_hash(self, token: str) -> str:
        return hmac.new(
            self._secret, token.encode("utf-8"), hashlib.sha256
        ).hexdigest()

    def _code_hash(self, challenge_id: str, code: str) -> str:
        payload = (challenge_id + ":" + code).encode("utf-8")
        return hmac.new(self._secret, payload, hashlib.sha256).hexdigest()

    def _new_token(self) -> str:
        return secrets.token_urlsafe(32)

    @staticmethod
    def _iso(moment: dt.datetime) -> str:
        if moment.tzinfo is None:
            moment = moment.replace(tzinfo=dt.timezone.utc)
        return moment.astimezone(dt.timezone.utc).isoformat(timespec="seconds").replace(
            "+00:00", "Z"
        )

    def _expiry(self, seconds: int) -> str:
        return self._iso(self._now() + dt.timedelta(seconds=int(seconds)))

    # ---- validation -------------------------------------------------------------------

    @staticmethod
    def normalize_email(value: Any) -> str:
        if not isinstance(value, str):
            raise AccountError(400, "INVALID_EMAIL", "A valid email address is required.")
        email = value.strip()
        if len(email) < 5 or len(email) > MAX_EMAIL_LENGTH or not EMAIL_RE.match(email):
            raise AccountError(400, "INVALID_EMAIL", "A valid email address is required.")
        return email.lower()

    @staticmethod
    def normalize_phone(value: Any) -> str:
        if not isinstance(value, str):
            raise AccountError(400, "INVALID_PHONE", "A valid phone number is required.")
        raw = value.strip()
        if not PHONE_RE.match(raw):
            raise AccountError(400, "INVALID_PHONE", "A valid phone number is required.")
        digits = "".join(character for character in raw if character.isdigit())
        if len(digits) < 7 or len(digits) > 15:
            raise AccountError(400, "INVALID_PHONE", "A valid phone number is required.")
        return "+" + digits

    @staticmethod
    def normalize_name(value: Any) -> str:
        if value is None or (isinstance(value, str) and not value.strip()):
            return "RUNNER-" + secrets.token_hex(2).upper()
        if not isinstance(value, str):
            raise AccountError(400, "INVALID_PLAYER_NAME", "A valid player name is required.")
        cleaned = "".join(
            character
            for character in value.strip()
            if character.isalnum() or character in " _-"
        )
        cleaned = " ".join(cleaned.split())[:20].rstrip()
        if not cleaned:
            raise AccountError(400, "INVALID_PLAYER_NAME", "A valid player name is required.")
        return cleaned

    @staticmethod
    def validate_password(value: Any) -> str:
        if not isinstance(value, str):
            raise AccountError(400, "INVALID_PASSWORD", "Password must be 10-128 characters.")
        if len(value) < MIN_PASSWORD_LENGTH or len(value) > MAX_PASSWORD_LENGTH:
            raise AccountError(400, "INVALID_PASSWORD", "Password must be 10-128 characters.")
        if not any(character.isalpha() for character in value):
            raise AccountError(400, "INVALID_PASSWORD", "Password must include a letter.")
        if not any(character.isdigit() for character in value):
            raise AccountError(400, "INVALID_PASSWORD", "Password must include a number.")
        return value

    # ---- delivery ---------------------------------------------------------------------

    def _deliver_email(self, to_email: str, subject: str, body: str) -> Dict[str, Any]:
        if self.email_sender is None:
            return {"channel": "email", "delivered": False, "reason": "SMTP_NOT_CONFIGURED"}
        try:
            delivered = bool(self.email_sender(to_email, subject, body))
        except Exception:
            return {"channel": "email", "delivered": False, "reason": "SMTP_DELIVERY_FAILED"}
        return {
            "channel": "email",
            "delivered": delivered,
            "reason": "" if delivered else "SMTP_DELIVERY_FAILED",
        }

    def _deliver_sms(self, phone: str, body: str) -> Dict[str, Any]:
        if self.sms_sender is None:
            return {"channel": "sms", "delivered": False, "reason": "SMS_NOT_CONFIGURED"}
        try:
            delivered = bool(self.sms_sender(phone, body))
        except Exception:
            return {"channel": "sms", "delivered": False, "reason": "SMS_DELIVERY_FAILED"}
        return {
            "channel": "sms",
            "delivered": delivered,
            "reason": "" if delivered else "SMS_DELIVERY_FAILED",
        }

    # ---- account lookup ---------------------------------------------------------------

    def _account_row(self, account_id: str) -> sqlite3.Row:
        with self._connect() as connection:
            row = connection.execute(
                "SELECT * FROM auth_accounts WHERE account_id = ?", (account_id,)
            ).fetchone()
        if row is None:
            raise AccountError(404, "ACCOUNT_NOT_FOUND", "Account not found.")
        return row

    def _email_row(self, email: str) -> Optional[sqlite3.Row]:
        with self._connect() as connection:
            return connection.execute(
                "SELECT * FROM auth_accounts WHERE email = ?", (email,)
            ).fetchone()

    def _phone_row(self, phone: str) -> Optional[sqlite3.Row]:
        with self._connect() as connection:
            return connection.execute(
                "SELECT * FROM auth_accounts WHERE phone = ?", (phone,)
            ).fetchone()

    def _session(self, account_id: str, delivery: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
        row = self._account_row(account_id)
        token = self.premium.issue_auth_token(account_id, self._auth_ttl())
        wallet = self.premium.wallet(account_id)
        payload: Dict[str, Any] = {
            "ok": True,
            "token": token,
            "accountId": account_id,
            "displayName": str(row["display_name"]),
            "gems": int(wallet["gems"]),
            "email": str(row["email"] or ""),
            "emailVerified": bool(row["email_verified"]),
            "phone": str(row["phone"] or ""),
            "phoneVerified": bool(row["phone_verified"]),
            "provider": str(row["provider"] or ""),
            "receiptEmail": str(row["receipt_email"] or row["email"] or ""),
            "syncRevision": self._sync_revision(account_id),
        }
        if delivery is not None:
            payload["delivery"] = delivery
        return payload

    def _auth_ttl(self) -> int:
        # Keep in sync with the premium HTTP default. The premium store issues the bearer
        # token; this value only controls the account-facing session description.
        return 30 * 24 * 60 * 60

    def _ensure_profile_row(self, account_id: str) -> None:
        """Create a blank profile row for a guest wallet on first account-facing call."""

        with self._connect() as connection:
            row = connection.execute(
                "SELECT account_id FROM auth_accounts WHERE account_id = ?", (account_id,)
            ).fetchone()
            if row is not None:
                return
        name = self.premium.account_name(account_id)
        now = self._iso(self._now())
        with self._connect() as connection:
            connection.execute(
                "INSERT OR IGNORE INTO auth_accounts(account_id, display_name, created_utc, "
                "updated_utc) VALUES (?, ?, ?, ?)",
                (account_id, name, now, now),
            )

    def _profile(self, account_id: str) -> Dict[str, Any]:
        try:
            row = self._account_row(account_id)
        except AccountError as exc:
            if exc.code != "ACCOUNT_NOT_FOUND":
                raise
            self._ensure_profile_row(account_id)
            row = self._account_row(account_id)
        wallet = self.premium.wallet(account_id)
        return {
            "ok": True,
            "accountId": account_id,
            "displayName": str(row["display_name"]),
            "email": str(row["email"] or ""),
            "emailVerified": bool(row["email_verified"]),
            "phone": str(row["phone"] or ""),
            "phoneVerified": bool(row["phone_verified"]),
            "provider": str(row["provider"] or ""),
            "receiptEmail": str(row["receipt_email"] or row["email"] or ""),
            "gems": int(wallet["gems"]),
            "pendingOrders": int(wallet["pendingOrders"]),
            "syncRevision": self._sync_revision(account_id),
            "createdUtc": str(row["created_utc"]),
            "updatedUtc": str(row["updated_utc"]),
        }

    # ---- email/password ----------------------------------------------------------------

    def register(
        self,
        email: Any,
        password: Any,
        display_name: Any,
    ) -> Dict[str, Any]:
        email_value = self.normalize_email(email)
        password_value = self.validate_password(password)
        name = self.normalize_name(display_name)
        if self._email_row(email_value) is not None:
            raise AccountError(409, "EMAIL_IN_USE", "An account already uses this email.")
        with self._lock:
            account_id = self.premium.create_account(name)
            now = self._iso(self._now())
            try:
                with self._connect() as connection:
                    connection.execute(
                        "INSERT INTO auth_accounts(account_id, email, email_verified, "
                        "password_hash, display_name, receipt_email, created_utc, updated_utc) "
                        "VALUES (?, ?, 0, ?, ?, ?, ?, ?)",
                        (
                            account_id,
                            email_value,
                            hash_password(password_value),
                            name,
                            email_value,
                            now,
                            now,
                        ),
                    )
            except sqlite3.IntegrityError as exc:
                raise AccountError(409, "EMAIL_IN_USE", "An account already uses this email.") from exc

        delivery = self._send_email_verification(account_id, email_value)
        session = self._session(account_id, delivery)
        session["verificationRequired"] = True
        return session

    def login(self, email: Any, password: Any) -> Dict[str, Any]:
        email_value = self.normalize_email(email)
        if not isinstance(password, str) or not password:
            raise AccountError(400, "INVALID_PASSWORD", "Password is required.")
        row = self._email_row(email_value)
        # Always run a verifier to keep timing broadly similar for unknown accounts.
        stored = str(row["password_hash"]) if row is not None and row["password_hash"] else (
            "pbkdf2_sha256$120000$00000000000000000000000000000000$"
            + "0" * 64
        )
        valid = verify_password(password, stored)
        if row is None or not valid:
            raise AccountError(401, "INVALID_CREDENTIALS", "Email or password is incorrect.")
        return self._session(str(row["account_id"]))

    def link_credentials(
        self, account_id: str, email: Any, password: Any, display_name: Any = None
    ) -> Dict[str, Any]:
        """Attach email/password to an existing guest or provider account."""

        email_value = self.normalize_email(email)
        password_value = self.validate_password(password)
        existing = self._email_row(email_value)
        if existing is not None and str(existing["account_id"]) != account_id:
            raise AccountError(409, "EMAIL_IN_USE", "An account already uses this email.")
        name = self.normalize_name(display_name) if display_name is not None else None
        with self._lock:
            with self._connect() as connection:
                row = connection.execute(
                    "SELECT account_id FROM auth_accounts WHERE account_id = ?", (account_id,)
                ).fetchone()
                if row is None:
                    connection.execute(
                        "INSERT INTO auth_accounts(account_id, email, email_verified, "
                        "password_hash, display_name, receipt_email, created_utc, updated_utc) "
                        "VALUES (?, ?, 0, ?, ?, ?, ?, ?)",
                        (
                            account_id,
                            email_value,
                            hash_password(password_value),
                            name or self.premium.account_name(account_id),
                            email_value,
                            self._iso(self._now()),
                            self._iso(self._now()),
                        ),
                    )
                else:
                    connection.execute(
                        "UPDATE auth_accounts SET email = ?, email_verified = 0, password_hash = ?, "
                        "receipt_email = ?, updated_utc = ? WHERE account_id = ?",
                        (
                            email_value,
                            hash_password(password_value),
                            email_value,
                            self._iso(self._now()),
                            account_id,
                        ),
                    )
                if name:
                    connection.execute(
                        "UPDATE auth_accounts SET display_name = ? WHERE account_id = ?",
                        (name, account_id),
                    )
            if name:
                self.premium.rename_account(account_id, name)
        delivery = self._send_email_verification(account_id, email_value)
        session = self._session(account_id, delivery)
        session["verificationRequired"] = True
        return session

    def logout(self, token: str) -> Dict[str, Any]:
        if not isinstance(token, str) or len(token) < 16:
            raise AccountError(401, "UNAUTHORIZED", "UNAUTHORIZED")
        self.premium.revoke_auth_token(token)
        return {"ok": True, "signedOut": True}

    def profile(self, account_id: str) -> Dict[str, Any]:
        return self._profile(account_id)

    def update_profile(self, account_id: str, display_name: Any, receipt_email: Any = None) -> Dict[str, Any]:
        self._ensure_profile_row(account_id)
        name = self.normalize_name(display_name)
        row = self._account_row(account_id)
        email = str(row["email"] or "")
        destination = str(row["receipt_email"] or email)
        if receipt_email is not None:
            destination = (
                self.normalize_email(receipt_email) if str(receipt_email).strip() else email
            )
        # Unverified receipt destinations are saved and shown in the profile. The checkout
        # UI warns before charging and the receipt mailer re-checks verification after the
        # signed payment webhook has been processed.
        with self._connect() as connection:
            connection.execute(
                "UPDATE auth_accounts SET display_name = ?, receipt_email = ?, updated_utc = ? "
                "WHERE account_id = ?",
                (name, destination, self._iso(self._now()), account_id),
            )
        self.premium.rename_account(account_id, name)
        return self._profile(account_id)

    def _send_email_verification(self, account_id: str, email: str) -> Dict[str, Any]:
        token = self._new_token()
        with self._connect() as connection:
            connection.execute(
                "INSERT INTO auth_one_time_tokens(token_hash, account_id, kind, email, "
                "expires_utc, used, created_utc) VALUES (?, ?, 'verify', ?, ?, 0, ?)",
                (
                    self._token_hash(token),
                    account_id,
                    email,
                    self._expiry(EMAIL_TOKEN_TTL_SECONDS),
                    self._iso(self._now()),
                ),
            )
        body = (
            "Verify your NEON VELOCITY email address.\n\n"
            "Verification code: " + token + "\n\n"
            "This code expires in 24 hours. If you did not create this account, ignore this email.\n"
        )
        delivery = self._deliver_email(email, "Verify your NEON VELOCITY account", body)
        if self.dev_mode:
            delivery["devVerificationToken"] = token
        return delivery

    def verify_email(self, token: Any) -> Dict[str, Any]:
        if not isinstance(token, str) or len(token) < 16:
            raise AccountError(400, "INVALID_TOKEN", "This verification code is invalid.")
        digest = self._token_hash(token)
        now = self._iso(self._now())
        with self._lock, self._connect() as connection:
            row = connection.execute(
                "SELECT * FROM auth_one_time_tokens WHERE token_hash = ? AND kind = 'verify'",
                (digest,),
            ).fetchone()
            if row is None or int(row["used"]) != 0 or str(row["expires_utc"]) <= now:
                raise AccountError(400, "INVALID_TOKEN", "This verification code is invalid or expired.")
            connection.execute(
                "UPDATE auth_one_time_tokens SET used = 1 WHERE token_hash = ?", (digest,)
            )
            connection.execute(
                "UPDATE auth_accounts SET email_verified = 1, updated_utc = ? WHERE account_id = ?",
                (now, str(row["account_id"])),
            )
        return self._profile(str(row["account_id"]))

    def request_password_reset(self, email: Any) -> Dict[str, Any]:
        email_value = self.normalize_email(email)
        row = self._email_row(email_value)
        delivery: Dict[str, Any] = {
            "channel": "email",
            "delivered": False,
            "reason": "ACCOUNT_NOT_FOUND",
        }
        if row is not None:
            account_id = str(row["account_id"])
            token = self._new_token()
            with self._connect() as connection:
                connection.execute(
                    "INSERT INTO auth_one_time_tokens(token_hash, account_id, kind, email, "
                    "expires_utc, used, created_utc) VALUES (?, ?, 'reset', ?, ?, 0, ?)",
                    (
                        self._token_hash(token),
                        account_id,
                        email_value,
                        self._expiry(RESET_TOKEN_TTL_SECONDS),
                        self._iso(self._now()),
                    ),
                )
            body = (
                "Reset your NEON VELOCITY password.\n\n"
                "Reset code: " + token + "\n\n"
                "This code expires in one hour. If you did not request a reset, ignore this email.\n"
            )
            delivery = self._deliver_email(email_value, "Reset your NEON VELOCITY password", body)
            if self.dev_mode:
                delivery["devResetToken"] = token
        # The API returns a generic success even for unknown addresses; delivery.reason tells
        # a local operator whether a message was actually sent.
        return {
            "ok": True,
            "message": "If that email has an account, a reset code is on its way.",
            "delivery": delivery,
        }

    def reset_password(self, token: Any, password: Any) -> Dict[str, Any]:
        new_password = self.validate_password(password)
        if not isinstance(token, str) or len(token) < 16:
            raise AccountError(400, "INVALID_TOKEN", "This reset code is invalid.")
        digest = self._token_hash(token)
        now = self._iso(self._now())
        with self._lock, self._connect() as connection:
            row = connection.execute(
                "SELECT * FROM auth_one_time_tokens WHERE token_hash = ? AND kind = 'reset'",
                (digest,),
            ).fetchone()
            if row is None or int(row["used"]) != 0 or str(row["expires_utc"]) <= now:
                raise AccountError(400, "INVALID_TOKEN", "This reset code is invalid or expired.")
            connection.execute(
                "UPDATE auth_one_time_tokens SET used = 1 WHERE token_hash = ?", (digest,)
            )
            connection.execute(
                "UPDATE auth_accounts SET password_hash = ?, updated_utc = ? WHERE account_id = ?",
                (hash_password(new_password), now, str(row["account_id"])),
            )
        self.premium.revoke_all_auth_tokens(str(row["account_id"]))
        return {"ok": True, "passwordReset": True}

    def change_email(self, account_id: str, email: Any) -> Dict[str, Any]:
        email_value = self.normalize_email(email)
        existing = self._email_row(email_value)
        if existing is not None and str(existing["account_id"]) != account_id:
            raise AccountError(409, "EMAIL_IN_USE", "An account already uses this email.")
        self._ensure_profile_row(account_id)
        with self._connect() as connection:
            connection.execute(
                "UPDATE auth_accounts SET email = ?, email_verified = 0, receipt_email = ?, "
                "updated_utc = ? WHERE account_id = ?",
                (email_value, email_value, self._iso(self._now()), account_id),
            )
        delivery = self._send_email_verification(account_id, email_value)
        payload = self._profile(account_id)
        payload["delivery"] = delivery
        return payload

    # ---- phone OTP ---------------------------------------------------------------------

    def request_phone_code(self, phone: Any) -> Dict[str, Any]:
        phone_value = self.normalize_phone(phone)
        now = self._now()
        with self._connect() as connection:
            recent = connection.execute(
                "SELECT created_utc FROM auth_otp WHERE phone = ? ORDER BY created_utc DESC LIMIT 1",
                (phone_value,),
            ).fetchone()
        if recent is not None:
            try:
                created = dt.datetime.fromisoformat(str(recent["created_utc"]).replace("Z", "+00:00"))
                if (now - created).total_seconds() < OTP_RESEND_SECONDS:
                    raise AccountError(
                        429, "RATE_LIMITED", "Wait before requesting another code.",
                        {"retryAfter": OTP_RESEND_SECONDS},
                    )
            except ValueError:
                pass
        challenge_id = "otp_" + secrets.token_hex(12)
        code = "{0:06d}".format(secrets.randbelow(1_000_000))
        with self._connect() as connection:
            connection.execute(
                "INSERT INTO auth_otp(challenge_id, phone, code_hash, attempts, expires_utc, "
                "used, created_utc) VALUES (?, ?, ?, 0, ?, 0, ?)",
                (
                    challenge_id,
                    phone_value,
                    self._code_hash(challenge_id, code),
                    self._expiry(OTP_TTL_SECONDS),
                    self._iso(now),
                ),
            )
        body = "NEON VELOCITY sign-in code: " + code + ". Expires in 10 minutes."
        delivery = self._deliver_sms(phone_value, body)
        if self.dev_mode:
            delivery["devCode"] = code
        return {
            "ok": True,
            "challengeId": challenge_id,
            "phone": phone_value,
            "expiresInSeconds": OTP_TTL_SECONDS,
            "delivery": delivery,
        }

    def verify_phone_code(
        self, challenge_id: Any, code: Any, display_name: Any = None
    ) -> Dict[str, Any]:
        if not isinstance(challenge_id, str) or not challenge_id.startswith("otp_"):
            raise AccountError(400, "INVALID_CHALLENGE", "This sign-in code is invalid.")
        if not isinstance(code, str) or not CODE_RE.match(code):
            raise AccountError(400, "INVALID_OTP", "Enter the six-digit code.")
        now = self._iso(self._now())
        invalid_code = False
        with self._lock, self._connect() as connection:
            row = connection.execute(
                "SELECT * FROM auth_otp WHERE challenge_id = ?", (challenge_id,)
            ).fetchone()
            if row is None or int(row["used"]) != 0 or str(row["expires_utc"]) <= now:
                raise AccountError(400, "INVALID_OTP", "This code is invalid or expired.")
            if int(row["attempts"]) >= OTP_MAX_ATTEMPTS:
                raise AccountError(429, "OTP_ATTEMPTS_EXCEEDED", "Request a new code.")
            if not hmac.compare_digest(
                str(row["code_hash"]), self._code_hash(challenge_id, code)
            ):
                connection.execute(
                    "UPDATE auth_otp SET attempts = attempts + 1 WHERE challenge_id = ?",
                    (challenge_id,),
                )
                invalid_code = True
            else:
                connection.execute(
                    "UPDATE auth_otp SET used = 1 WHERE challenge_id = ?", (challenge_id,)
                )
        if invalid_code:
            # Raised after the connection commits so the attempt counter cannot be reset by
            # disconnecting or retrying with a new HTTP request.
            raise AccountError(400, "INVALID_OTP", "This code is incorrect.")
        phone_value = str(row["phone"])
        existing = self._phone_row(phone_value)
        if existing is not None:
            return self._session(str(existing["account_id"]))
        name = self.normalize_name(display_name)
        account_id = self.premium.create_account(name)
        now_iso = self._iso(self._now())
        with self._connect() as connection:
            connection.execute(
                "INSERT INTO auth_accounts(account_id, phone, phone_verified, display_name, "
                "created_utc, updated_utc) VALUES (?, ?, 1, ?, ?, ?)",
                (account_id, phone_value, name, now_iso, now_iso),
            )
        return self._session(account_id)

    # ---- OAuth ---------------------------------------------------------------------------

    def oauth_login(
        self,
        provider: Any,
        id_token: Any,
        display_name: Any = None,
    ) -> Dict[str, Any]:
        if not isinstance(provider, str) or provider.lower() not in SUPPORTED_PROVIDERS:
            raise AccountError(400, "INVALID_PROVIDER", "Unsupported identity provider.")
        provider_value = provider.lower()
        if self.oauth_verifier is None:
            raise AccountError(
                503,
                "PROVIDER_NOT_CONFIGURED",
                provider_value.upper() + " sign-in is not configured on this server.",
            )
        if not isinstance(id_token, str) or len(id_token) < 32 or len(id_token) > 8192:
            raise AccountError(400, "INVALID_PROVIDER_TOKEN", "The identity token is invalid.")
        try:
            claims = self.oauth_verifier(provider_value, id_token)
        except AccountError:
            raise
        except Exception as exc:
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "The identity token was rejected.") from exc
        if not isinstance(claims, dict):
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "The identity token was rejected.")
        subject = claims.get("subject")
        if not isinstance(subject, str) or len(subject) < 3 or len(subject) > 255:
            raise AccountError(401, "INVALID_PROVIDER_TOKEN", "The identity token has no subject.")
        email = ""
        raw_email = claims.get("email")
        if isinstance(raw_email, str) and raw_email.strip():
            try:
                email = self.normalize_email(raw_email)
            except AccountError:
                email = ""
        with self._connect() as connection:
            identity = connection.execute(
                "SELECT account_id FROM auth_identities WHERE provider = ? AND subject = ?",
                (provider_value, subject),
            ).fetchone()
        if identity is not None:
            return self._session(str(identity["account_id"]))
        if email:
            email_row = self._email_row(email)
            if email_row is not None and bool(claims.get("emailVerified", False)):
                account_id = str(email_row["account_id"])
                with self._connect() as connection:
                    connection.execute(
                        "INSERT OR REPLACE INTO auth_identities(provider, subject, account_id, "
                        "email, created_utc) VALUES (?, ?, ?, ?, ?)",
                        (provider_value, subject, account_id, email, self._iso(self._now())),
                    )
                return self._session(account_id)
        name = self.normalize_name(display_name or claims.get("name"))
        account_id = self.premium.create_account(name)
        now = self._iso(self._now())
        with self._connect() as connection:
            connection.execute(
                "INSERT INTO auth_accounts(account_id, email, email_verified, display_name, "
                "provider, provider_subject, receipt_email, created_utc, updated_utc) "
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                (
                    account_id,
                    email or None,
                    1 if email and claims.get("emailVerified", False) else 0,
                    name,
                    provider_value,
                    subject,
                    email,
                    now,
                    now,
                ),
            )
            connection.execute(
                "INSERT INTO auth_identities(provider, subject, account_id, email, created_utc) "
                "VALUES (?, ?, ?, ?, ?)",
                (provider_value, subject, account_id, email, now),
            )
        return self._session(account_id)

    def identities(self, account_id: str) -> List[Dict[str, Any]]:
        with self._connect() as connection:
            rows = connection.execute(
                "SELECT provider, email, created_utc FROM auth_identities WHERE account_id = ? "
                "ORDER BY created_utc",
                (account_id,),
            ).fetchall()
        return [
            {
                "provider": str(row["provider"]),
                "email": str(row["email"]),
                "createdUtc": str(row["created_utc"]),
            }
            for row in rows
        ]

    # ---- offline-to-online sync --------------------------------------------------------

    def _sync_revision(self, account_id: str) -> int:
        with self._connect() as connection:
            row = connection.execute(
                "SELECT revision FROM cloud_sync WHERE account_id = ?", (account_id,)
            ).fetchone()
        return int(row["revision"]) if row is not None else 0

    def get_sync(self, account_id: str) -> Dict[str, Any]:
        with self._connect() as connection:
            row = connection.execute(
                "SELECT revision, snapshot_json, updated_utc FROM cloud_sync WHERE account_id = ?",
                (account_id,),
            ).fetchone()
        if row is None:
            return {
                "ok": True,
                "revision": 0,
                "snapshot": None,
                "updatedUtc": "",
                "gems": int(self.premium.wallet(account_id)["gems"]),
            }
        try:
            snapshot = json.loads(str(row["snapshot_json"]))
        except ValueError:
            snapshot = None
        return {
            "ok": True,
            "revision": int(row["revision"]),
            "snapshot": snapshot,
            "updatedUtc": str(row["updated_utc"]),
            "gems": int(self.premium.wallet(account_id)["gems"]),
        }

    def merge_sync(
        self,
        account_id: str,
        snapshot: Any,
        base_revision: Any,
        request_id: Any,
    ) -> Dict[str, Any]:
        if not isinstance(snapshot, dict):
            raise AccountError(400, "INVALID_SYNC", "A progress snapshot is required.")
        if not isinstance(base_revision, int) or isinstance(base_revision, bool) or base_revision < 0:
            raise AccountError(400, "INVALID_SYNC_REVISION", "A non-negative base revision is required.")
        if not isinstance(request_id, str) or not (1 <= len(request_id) <= 128):
            raise AccountError(400, "INVALID_CLIENT_REQUEST_ID", "A request id is required.")
        if not re.match(r"^[A-Za-z0-9._:-]+$", request_id):
            raise AccountError(400, "INVALID_CLIENT_REQUEST_ID", "A request id is required.")
        try:
            encoded = json.dumps(snapshot, ensure_ascii=False, separators=(",", ":"))
        except (TypeError, ValueError) as exc:
            raise AccountError(400, "INVALID_SYNC", "The progress snapshot is not serialisable.") from exc
        if len(encoded.encode("utf-8")) > MAX_SNAPSHOT_BYTES:
            raise AccountError(413, "SYNC_TOO_LARGE", "The progress snapshot is too large.")
        schema = snapshot.get("schema", STATE_SCHEMA)
        if not isinstance(schema, int) or schema < 0 or schema > STATE_SCHEMA:
            raise AccountError(400, "UNSUPPORTED_SYNC_SCHEMA", "This save schema is not supported.")

        with self._lock:
            current = self.get_sync(account_id)
            current_revision = int(current["revision"])
            with self._connect() as connection:
                replay = connection.execute(
                    "SELECT revision FROM sync_requests WHERE account_id = ? AND request_id = ?",
                    (account_id, request_id),
                ).fetchone()
            if replay is not None:
                return self.get_sync(account_id)
            if base_revision > current_revision:
                raise AccountError(
                    409,
                    "SYNC_REVISION_AHEAD",
                    "The local save is from a newer cloud revision.",
                    {"serverRevision": current_revision},
                )
            server_snapshot = current["snapshot"] if isinstance(current["snapshot"], dict) else {}
            merged, conflicts = self._merge_snapshots(server_snapshot, snapshot)
            revision = current_revision + 1
            now = self._iso(self._now())
            with self._connect() as connection:
                connection.execute(
                    "INSERT INTO cloud_sync(account_id, revision, snapshot_json, updated_utc) "
                    "VALUES (?, ?, ?, ?) ON CONFLICT(account_id) DO UPDATE SET "
                    "revision = excluded.revision, snapshot_json = excluded.snapshot_json, "
                    "updated_utc = excluded.updated_utc",
                    (account_id, revision, json.dumps(merged, ensure_ascii=False, separators=(",", ":")), now),
                )
                connection.execute(
                    "INSERT INTO sync_requests(account_id, request_id, revision, created_utc) "
                    "VALUES (?, ?, ?, ?)",
                    (account_id, request_id, revision, now),
                )
        return {
            "ok": True,
            "revision": revision,
            "snapshot": merged,
            "conflicts": conflicts,
            "gems": int(self.premium.wallet(account_id)["gems"]),
            "updatedUtc": now,
        }

    @staticmethod
    def _number(value: Any, cap: float, default: float = 0.0) -> float:
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            return default
        if not math.isfinite(float(value)):
            return default
        return max(0.0, min(float(value), cap))

    @classmethod
    def _merge_snapshots(
        cls, server: Dict[str, Any], client: Dict[str, Any]
    ) -> Tuple[Dict[str, Any], List[str]]:
        conflicts: List[str] = []
        merged: Dict[str, Any] = {"schema": STATE_SCHEMA}

        # Monotonic scalar progression. GEMS, tokens and entitlements are never accepted
        # from a save snapshot; they live in the server wallet.
        for field, cap in (
            ("totalXp", XP_HARD_CAP),
            ("credits", CREDITS_HARD_CAP),
            ("seasonXp", 10_000_000),
        ):
            server_value = cls._number(server.get(field), cap)
            client_value = cls._number(client.get(field), cap)
            if abs(server_value - client_value) > 1e-6:
                conflicts.append(field)
            merged[field] = int(max(server_value, client_value))

        for field, cap in (
            ("bestDistance", 10_000_000),
            ("bestScore", 1_000_000_000),
            ("seasonLevel", 10_000),
            ("furthestLocationIndex", 10_000),
        ):
            server_value = cls._number(server.get(field), cap)
            client_value = cls._number(client.get(field), cap)
            if abs(server_value - client_value) > 1e-6:
                conflicts.append(field)
            merged[field] = max(server_value, client_value)

        server_lifetime = server.get("lifetime") if isinstance(server.get("lifetime"), dict) else {}
        client_lifetime = client.get("lifetime") if isinstance(client.get("lifetime"), dict) else {}
        lifetime: Dict[str, float] = {}
        for key in (
            "totalRuns", "totalDistance", "totalCoins", "totalJumps", "totalSlides",
            "totalPowerUps", "totalDeaths", "racesCompleted", "racesWon", "bestRaceRank",
            "missionsClaimed", "dailyRewardsClaimed", "nearMisses", "levelsGained",
            "locationsDiscovered", "premiumPurchases",
        ):
            value = max(
                cls._number(server_lifetime.get(key), 1_000_000_000),
                cls._number(client_lifetime.get(key), 1_000_000_000),
            )
            lifetime[key] = int(value)
        merged["lifetime"] = lifetime

        for field in (
            "discoveredLocations", "unlockedCharacters", "unlockedTrails",
            "redeemedRewardIds", "claimedAchievements",
        ):
            merged[field] = cls._union_strings(server.get(field), client.get(field), field, conflicts)

        merged["missions"] = cls._merge_missions(server.get("missions"), client.get("missions"))
        merged["modeBests"] = cls._merge_keyed(
            server.get("modeBests"), client.get("modeBests"), "modeId", ("distance", "score", "coins")
        )
        merged["locationRecords"] = cls._merge_keyed(
            server.get("locationRecords"), client.get("locationRecords"), "locationId",
            ("bestDistance", "visits")
        )
        merged["tutorialCompleted"] = bool(server.get("tutorialCompleted")) or bool(
            client.get("tutorialCompleted")
        )
        merged["settings"] = cls._sanitize_settings(client.get("settings", server.get("settings")))
        merged["lastModeId"] = cls._safe_string(client.get("lastModeId", server.get("lastModeId", "")))
        merged["selectedCharacter"] = cls._safe_string(
            client.get("selectedCharacter", server.get("selectedCharacter", "kira"))
        )
        merged["selectedTrail"] = cls._safe_string(
            client.get("selectedTrail", server.get("selectedTrail", "spark"))
        )
        merged["lastPlayedUtc"] = cls._safe_string(
            client.get("lastPlayedUtc", server.get("lastPlayedUtc", ""))
        )
        return merged, sorted(set(conflicts))

    @classmethod
    def _union_strings(
        cls, server: Any, client: Any, field: str, conflicts: List[str]
    ) -> List[str]:
        output: List[str] = []
        seen = set()
        for source in (server, client):
            if not isinstance(source, list):
                continue
            for item in source[:MAX_SYNC_ITEMS]:
                if not isinstance(item, str):
                    continue
                value = item.strip()[:MAX_STRING_LENGTH]
                if not value or value in seen:
                    continue
                seen.add(value)
                output.append(value)
                if len(output) >= MAX_SYNC_ITEMS:
                    break
        if isinstance(server, list) and isinstance(client, list) and set(
            item for item in server if isinstance(item, str)
        ) != set(item for item in client if isinstance(item, str)):
            conflicts.append(field)
        return output

    @classmethod
    def _merge_missions(cls, server: Any, client: Any) -> List[Dict[str, Any]]:
        by_id: Dict[str, Dict[str, Any]] = {}
        for source in (server, client):
            if not isinstance(source, list):
                continue
            for item in source[:MAX_MISSION_ITEMS]:
                if not isinstance(item, dict):
                    continue
                mission_id = cls._safe_string(item.get("id"))
                if not mission_id:
                    continue
                current = by_id.get(mission_id)
                candidate = {
                    "id": mission_id,
                    "title": cls._safe_string(item.get("title")),
                    "description": cls._safe_string(item.get("description")),
                    "scope": cls._safe_string(item.get("scope")) or "daily",
                    "metric": int(cls._number(item.get("metric"), 1000)),
                    "target": int(cls._number(item.get("target"), 10_000_000)),
                    "progress": int(cls._number(item.get("progress"), 10_000_000)),
                    "rewardCredits": int(cls._number(item.get("rewardCredits"), 1_000_000)),
                    "rewardXp": int(cls._number(item.get("rewardXp"), 1_000_000)),
                    "claimed": bool(item.get("claimed")),
                    "completed": bool(item.get("completed")),
                    "expiresDateKey": cls._safe_string(item.get("expiresDateKey")),
                }
                if current is None:
                    by_id[mission_id] = candidate
                else:
                    for key in ("progress", "target", "rewardCredits", "rewardXp", "metric"):
                        current[key] = max(int(current.get(key, 0)), int(candidate.get(key, 0)))
                    current["claimed"] = bool(current.get("claimed")) or candidate["claimed"]
                    current["completed"] = bool(current.get("completed")) or candidate["completed"]
        return list(by_id.values())

    @classmethod
    def _merge_keyed(
        cls, server: Any, client: Any, key: str, numeric_fields: Iterable[str]
    ) -> List[Dict[str, Any]]:
        by_key: Dict[str, Dict[str, Any]] = {}
        for source in (server, client):
            if not isinstance(source, list):
                continue
            for item in source[:MAX_SYNC_ITEMS]:
                if not isinstance(item, dict):
                    continue
                item_key = cls._safe_string(item.get(key))
                if not item_key:
                    continue
                current = by_key.get(item_key)
                candidate = {key: item_key}
                for numeric in numeric_fields:
                    candidate[numeric] = cls._number(item.get(numeric), 10_000_000_000)
                for text in ("achievedUtc", "firstSeenUtc", "lastSeenUtc", "modeId", "locationId"):
                    if text in item:
                        candidate[text] = cls._safe_string(item.get(text))
                if current is None:
                    by_key[item_key] = candidate
                else:
                    for numeric in numeric_fields:
                        current[numeric] = max(
                            float(current.get(numeric, 0)), float(candidate.get(numeric, 0))
                        )
                    for text in ("achievedUtc", "lastSeenUtc"):
                        if candidate.get(text):
                            current[text] = candidate[text]
        return list(by_key.values())

    @classmethod
    def _sanitize_settings(cls, settings: Any) -> Dict[str, Any]:
        if not isinstance(settings, dict):
            return {}
        output: Dict[str, Any] = {}
        for key, value in list(settings.items())[:48]:
            if not isinstance(key, str) or len(key) > 64:
                continue
            if isinstance(value, bool):
                output[key] = value
            elif isinstance(value, (int, float)) and not isinstance(value, bool):
                if math.isfinite(float(value)):
                    output[key] = value
            elif isinstance(value, str):
                output[key] = value[:256]
        return output

    @staticmethod
    def _safe_string(value: Any) -> str:
        if not isinstance(value, str):
            return ""
        return value.strip()[:MAX_STRING_LENGTH]

    # ---- payment transaction records ---------------------------------------------------

    def record_transaction(
        self,
        provider: str,
        event_id: str,
        account_id: str,
        package_id: str,
        gems: int,
        amount_minor: int,
        currency: str,
        status: str,
        receipt_email: str = "",
        detail: str = "",
        provider_ref: str = "",
    ) -> Tuple[bool, Dict[str, Any]]:
        """Insert a provider transaction exactly once.

        Returns ``(created, row)``. A duplicate ``(provider, event_id)`` returns the existing
        row so webhook retries are idempotent.
        """

        now = self._iso(self._now())
        with self._lock, self._connect() as connection:
            existing = connection.execute(
                "SELECT * FROM payment_transactions WHERE provider = ? AND event_id = ?",
                (provider, event_id),
            ).fetchone()
            if existing is not None:
                return False, self._transaction_dict(existing)
            connection.execute(
                "INSERT INTO payment_transactions(provider, event_id, account_id, package_id, gems, "
                "amount_minor, currency, status, receipt_email, provider_ref, detail, created_utc) "
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                (
                    provider, event_id, account_id, package_id, int(gems), int(amount_minor),
                    currency, status, receipt_email, provider_ref[:255], detail[:500], now,
                ),
            )
            row = connection.execute(
                "SELECT * FROM payment_transactions WHERE provider = ? AND event_id = ?",
                (provider, event_id),
            ).fetchone()
        return True, self._transaction_dict(row)

    @staticmethod
    def _transaction_dict(row: sqlite3.Row) -> Dict[str, Any]:
        return {
            "id": int(row["id"]),
            "provider": str(row["provider"]),
            "eventId": str(row["event_id"]),
            "accountId": str(row["account_id"]),
            "packageId": str(row["package_id"]),
            "gems": int(row["gems"]),
            "amountMinor": int(row["amount_minor"]),
            "currency": str(row["currency"]),
            "status": str(row["status"]),
            "receiptEmail": str(row["receipt_email"]),
            "providerRef": str(row["provider_ref"]),
            "detail": str(row["detail"]),
            "createdUtc": str(row["created_utc"]),
        }

    def transactions(self, account_id: str, limit: int = 100) -> List[Dict[str, Any]]:
        limit = max(1, min(int(limit), 200))
        with self._connect() as connection:
            rows = connection.execute(
                "SELECT * FROM payment_transactions WHERE account_id = ? ORDER BY id DESC LIMIT ?",
                (account_id, limit),
            ).fetchall()
        return [self._transaction_dict(row) for row in rows]

    def transaction_by_reference(
        self, provider: str, provider_ref: str
    ) -> Optional[Dict[str, Any]]:
        if not provider_ref:
            return None
        with self._connect() as connection:
            row = connection.execute(
                "SELECT * FROM payment_transactions WHERE provider = ? AND provider_ref = ? "
                "ORDER BY id DESC LIMIT 1",
                (provider, provider_ref),
            ).fetchone()
        return self._transaction_dict(row) if row is not None else None

    def transaction_by_id(self, transaction_id: int) -> Optional[Dict[str, Any]]:
        with self._connect() as connection:
            row = connection.execute(
                "SELECT * FROM payment_transactions WHERE id = ?",
                (int(transaction_id),),
            ).fetchone()
        return self._transaction_dict(row) if row is not None else None

    def mark_transaction_status(
        self, transaction_id: int, status: str, detail: str = ""
    ) -> Dict[str, Any]:
        with self._connect() as connection:
            connection.execute(
                "UPDATE payment_transactions SET status = ?, detail = ? WHERE id = ?",
                (status[:32], detail[:500], int(transaction_id)),
            )
            row = connection.execute(
                "SELECT * FROM payment_transactions WHERE id = ?", (int(transaction_id),)
            ).fetchone()
        if row is None:
            raise AccountError(404, "TRANSACTION_NOT_FOUND", "Transaction not found.")
        return self._transaction_dict(row)

    # ---- operator views ---------------------------------------------------------------

    def admin_transactions(
        self,
        limit: int = 100,
        provider: str = "",
        status: str = "",
    ) -> List[Dict[str, Any]]:
        limit = max(1, min(int(limit), 500))
        clauses: List[str] = []
        parameters: List[Any] = []
        if provider:
            clauses.append("t.provider = ?")
            parameters.append(provider)
        if status:
            clauses.append("t.status = ?")
            parameters.append(status)
        query = (
            "SELECT t.*, a.display_name AS display_name, a.email AS email, "
            "a.receipt_email AS receipt_email "
            "FROM payment_transactions t "
            "LEFT JOIN auth_accounts a ON a.account_id = t.account_id"
        )
        if clauses:
            query += " WHERE " + " AND ".join(clauses)
        query += " ORDER BY t.id DESC LIMIT ?"
        parameters.append(limit)
        with self._connect() as connection:
            rows = connection.execute(query, tuple(parameters)).fetchall()
        payload: List[Dict[str, Any]] = []
        for row in rows:
            item = self._transaction_dict(row)
            item["displayName"] = str(row["display_name"] or "")
            item["accountEmail"] = str(row["email"] or "")
            item["accountReceiptEmail"] = str(row["receipt_email"] or "")
            payload.append(item)
        return payload

    def admin_accounts(self, query_text: str = "", limit: int = 100) -> List[Dict[str, Any]]:
        limit = max(1, min(int(limit), 500))
        term = "%" + str(query_text or "").strip() + "%"
        # Wallet accounts and auth identities live in two separate SQLite files. ATTACH the
        # wallet database read-only so the operator view can search and page across both
        # without copying balances into the account database.
        with self._connect() as connection:
            connection.execute(
                "ATTACH DATABASE ? AS wallet", (str(self.premium.db_path),)
            )
            rows = connection.execute(
                "SELECT a.id, a.display_name, a.gems, a.created_utc, "
                "u.email, u.email_verified, u.phone, u.phone_verified, "
                "u.provider, u.receipt_email, u.updated_utc, "
                "(SELECT COUNT(*) FROM payment_transactions t WHERE t.account_id = a.id) "
                "AS transaction_count "
                "FROM wallet.accounts a LEFT JOIN auth_accounts u ON u.account_id = a.id "
                "WHERE a.id LIKE ? OR a.display_name LIKE ? OR COALESCE(u.email, '') LIKE ? "
                "OR COALESCE(u.phone, '') LIKE ? "
                "ORDER BY a.created_utc DESC LIMIT ?",
                (term, term, term, term, limit),
            ).fetchall()
        return [
            {
                "accountId": str(row["id"]),
                "displayName": str(row["display_name"]),
                "gems": int(row["gems"]),
                "createdUtc": str(row["created_utc"]),
                "email": str(row["email"] or ""),
                "emailVerified": bool(row["email_verified"]),
                "phone": str(row["phone"] or ""),
                "phoneVerified": bool(row["phone_verified"]),
                "provider": str(row["provider"] or ""),
                "receiptEmail": str(row["receipt_email"] or ""),
                "updatedUtc": str(row["updated_utc"] or ""),
                "transactionCount": int(row["transaction_count"]),
            }
            for row in rows
        ]

    def admin_account_detail(self, account_id: str) -> Dict[str, Any]:
        if not isinstance(account_id, str) or not account_id.startswith("acc_"):
            raise AccountError(400, "INVALID_ACCOUNT", "INVALID_ACCOUNT")
        matches = [item for item in self.admin_accounts(account_id, 10) if item["accountId"] == account_id]
        if not matches:
            raise AccountError(404, "ACCOUNT_NOT_FOUND", "Account not found.")
        detail = matches[0]
        detail["identities"] = self.identities(account_id)
        detail["transactions"] = self.transactions(account_id, 50)
        return detail

