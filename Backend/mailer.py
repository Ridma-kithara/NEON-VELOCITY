#!/usr/bin/env python3
"""Minimal SMTP delivery for NEON VELOCITY account and receipt email.

The mailer is deliberately optional. Without ``NV_SMTP_HOST``, ``NV_SMTP_FROM`` and a
usable connection configuration every send returns ``False`` and callers report that no
message was delivered. The game never claims an email was sent without a successful SMTP
handshake, and payment receipts are only queued after a provider webhook has been verified
and GEMS have been credited server-side.
"""

from __future__ import annotations

import json
import logging
import os
import smtplib
import ssl
import urllib.error
import urllib.request
from email.message import EmailMessage
from typing import Optional

logger = logging.getLogger("neon-mailer")


def _env_int(name: str, default: int, minimum: int, maximum: int) -> int:
    raw = os.environ.get(name, "").strip()
    if not raw:
        return default
    try:
        value = int(raw)
    except ValueError:
        return default
    return max(minimum, min(maximum, value))


def _env_flag(name: str, default: bool = False) -> bool:
    raw = os.environ.get(name, "").strip().lower()
    if not raw:
        return default
    return raw in ("1", "true", "yes", "on")


class SmtpMailer:
    def __init__(
        self,
        host: str = "",
        port: int = 587,
        username: str = "",
        password: str = "",
        from_address: str = "",
        use_starttls: bool = True,
        use_ssl: bool = False,
        timeout: float = 12.0,
    ) -> None:
        self.host = host.strip()
        self.port = int(port)
        self.username = username
        self.password = password
        self.from_address = from_address.strip()
        self.use_starttls = bool(use_starttls)
        self.use_ssl = bool(use_ssl)
        self.timeout = float(timeout)

    @classmethod
    def from_env(cls) -> "SmtpMailer":
        return cls(
            host=os.environ.get("NV_SMTP_HOST", "").strip(),
            port=_env_int("NV_SMTP_PORT", 587, 1, 65535),
            username=os.environ.get("NV_SMTP_USERNAME", "").strip(),
            password=os.environ.get("NV_SMTP_PASSWORD", ""),
            from_address=os.environ.get("NV_SMTP_FROM", "").strip(),
            use_starttls=_env_flag("NV_SMTP_STARTTLS", True),
            use_ssl=_env_flag("NV_SMTP_SSL", False),
            timeout=float(_env_int("NV_SMTP_TIMEOUT", 12, 2, 60)),
        )

    @property
    def configured(self) -> bool:
        return bool(self.host and self.from_address)

    def send(self, to_email: str, subject: str, body: str) -> bool:
        if not self.configured:
            return False
        if not isinstance(to_email, str) or "@" not in to_email:
            return False
        message = EmailMessage()
        message["From"] = self.from_address
        message["To"] = to_email
        message["Subject"] = subject[:180]
        message.set_content(body)
        try:
            if self.use_ssl:
                context = ssl.create_default_context()
                with smtplib.SMTP_SSL(
                    self.host, self.port, timeout=self.timeout, context=context
                ) as smtp:
                    if self.username:
                        smtp.login(self.username, self.password)
                    smtp.send_message(message)
            else:
                with smtplib.SMTP(self.host, self.port, timeout=self.timeout) as smtp:
                    smtp.ehlo()
                    if self.use_starttls:
                        smtp.starttls(context=ssl.create_default_context())
                        smtp.ehlo()
                    if self.username:
                        smtp.login(self.username, self.password)
                    smtp.send_message(message)
            return True
        except (OSError, smtplib.SMTPException) as exc:
            # Log the failure class only; recipient addresses and bodies are not secrets but
            # keeping them out of routine logs avoids needless personal-data retention.
            logger.warning("SMTP delivery failed: %s", type(exc).__name__)
            return False

    def send_receipt(
        self,
        to_email: str,
        order_id: str,
        package_name: str,
        gems: int,
        amount_display: str,
        provider: str,
        *,
        game_name: str = "NEON VELOCITY",
        player_id: str = "",
        transaction_id: str = "",
        occurred_utc: str = "",
        status: str = "SUCCEEDED",
    ) -> bool:
        """Send a receipt only after a verified provider payment has been credited."""

        body = (
            "{0} — payment receipt\n\n"
            "Game: {0}\n"
            "Player ID: {1}\n"
            "Transaction ID: {2}\n"
            "Item / package: {3}\n"
            "GEMS credited: {4}\n"
            "Amount: {5}\n"
            "Currency / provider: {6}\n"
            "Date (UTC): {7}\n"
            "Status: {8}\n\n"
            "This receipt was generated after the payment provider confirmed the payment "
            "and the server credited your GEM wallet. If you did not make this purchase, "
            "contact support immediately.\n\n"
            "RK VANTAGE GAMES\n"
        ).format(
            game_name,
            player_id or "not supplied",
            transaction_id or order_id,
            package_name,
            gems,
            amount_display,
            provider,
            occurred_utc or order_id,
            status,
        )
        return self.send(
            to_email, "Your NEON VELOCITY receipt — " + package_name, body
        )


class SmsWebhook:
    """Optional SMS gateway adapter.

    The operator points ``NV_SMS_WEBHOOK`` at an HTTPS endpoint that accepts
    ``{"to": "+...", "body": "..."}`` and returns any 2xx status. The adapter deliberately
    does not embed a provider SDK or credentials: many regional gateways use different
    payloads, so the webhook is the portable boundary. Without configuration every send
    reports ``SMS_NOT_CONFIGURED`` and no message is claimed as delivered.
    """

    def __init__(self, url: str = "", token: str = "", timeout: float = 10.0) -> None:
        self.url = url.strip()
        self.token = token
        self.timeout = float(timeout)

    @classmethod
    def from_env(cls) -> "SmsWebhook":
        return cls(
            url=os.environ.get("NV_SMS_WEBHOOK", "").strip(),
            token=os.environ.get("NV_SMS_WEBHOOK_TOKEN", "").strip(),
            timeout=float(_env_int("NV_SMS_TIMEOUT", 10, 2, 60)),
        )

    @property
    def configured(self) -> bool:
        return bool(self.url)

    def send(self, to_phone: str, body: str) -> bool:
        if not self.configured or not to_phone:
            return False
        payload = json.dumps(
            {"to": to_phone, "body": body[:480]}, separators=(",", ":")
        ).encode("utf-8")
        headers = {"Content-Type": "application/json"}
        if self.token:
            headers["Authorization"] = "Bearer " + self.token
        request = urllib.request.Request(
            self.url, data=payload, headers=headers, method="POST"
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                return 200 <= int(response.status) < 300
        except (OSError, urllib.error.URLError) as exc:
            logger.warning("SMS webhook delivery failed: %s", type(exc).__name__)
            return False
