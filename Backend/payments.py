#!/usr/bin/env python3
"""Payment provider adapters for NEON VELOCITY.

Stripe Checkout is implemented end-to-end at the server boundary:

* checkout sessions are created with server-held secret keys;
* prices come from a server-side price map, never from the Unity client;
* webhook signatures are verified with the signed timestamp and HMAC-SHA256;
* GEMS are credited only after a verified ``checkout.session.completed`` event with
  ``payment_status == "paid"``;
* duplicate webhook events are idempotent through the transaction ledger;
* refunds reverse the credited GEMS and update the transaction.

Apple and Google Play adapters are present but deliberately report
``PROVIDER_NOT_CONFIGURED`` until a merchant account, product map and server verification
credentials are supplied. They never accept a client-claimed purchase. No raw card data
ever reaches this service.
"""

from __future__ import annotations

import dataclasses
import base64
import hashlib
import hmac
import json
import logging
import os
import re
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, Callable, Dict, Optional, Tuple

from account_store import AccountError, AccountStore
from mailer import SmtpMailer
from premium_store import PACKAGES_BY_ID, PremiumStore
from rsa_keys import sign_rs256

logger = logging.getLogger("neon-payments")

STRIPE_API = "https://api.stripe.com/v1/checkout/sessions"
APPLE_VERIFY = "https://buy.itunes.apple.com/verifyReceipt"
APPLE_SANDBOX_VERIFY = "https://sandbox.itunes.apple.com/verifyReceipt"
SIGNATURE_TOLERANCE_SECONDS = 300


@dataclasses.dataclass
class StripeConfig:
    secret_key: str = dataclasses.field(default="", repr=False)
    webhook_secret: str = dataclasses.field(default="", repr=False)
    currency: str = "usd"
    price_map: Dict[str, str] = dataclasses.field(default_factory=dict)
    success_url: str = ""
    cancel_url: str = ""

    @classmethod
    def from_env(cls) -> "StripeConfig":
        raw_map = os.environ.get("NV_STRIPE_PRICE_MAP", "").strip()
        price_map: Dict[str, str] = {}
        if raw_map:
            try:
                parsed = json.loads(raw_map)
                if isinstance(parsed, dict):
                    price_map = {
                        str(key): str(value)
                        for key, value in parsed.items()
                        if isinstance(key, str) and isinstance(value, str) and value.startswith("price_")
                    }
            except ValueError:
                price_map = {}
        return cls(
            secret_key=os.environ.get("NV_STRIPE_SECRET_KEY", "").strip(),
            webhook_secret=os.environ.get("NV_STRIPE_WEBHOOK_SECRET", "").strip(),
            currency=os.environ.get("NV_STRIPE_CURRENCY", "usd").strip().lower() or "usd",
            price_map=price_map,
            success_url=os.environ.get("NV_STRIPE_SUCCESS_URL", "").strip(),
            cancel_url=os.environ.get("NV_STRIPE_CANCEL_URL", "").strip(),
        )


@dataclasses.dataclass
class AppleConfig:
    shared_secret: str = dataclasses.field(default="", repr=False)
    product_map: Dict[str, str] = dataclasses.field(default_factory=dict)

    @classmethod
    def from_env(cls) -> "AppleConfig":
        raw_map = os.environ.get("NV_APPLE_PRODUCT_MAP", "").strip()
        product_map: Dict[str, str] = {}
        if raw_map:
            try:
                parsed = json.loads(raw_map)
                if isinstance(parsed, dict):
                    product_map = {
                        str(key): str(value)
                        for key, value in parsed.items()
                        if isinstance(key, str) and isinstance(value, str)
                    }
            except ValueError:
                product_map = {}
        return cls(
            shared_secret=os.environ.get("NV_APPLE_SHARED_SECRET", "").strip(),
            product_map=product_map,
        )


def _validate_redirect(url: str) -> str:
    if not url:
        return ""
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme not in ("https", "http"):
        raise AccountError(400, "INVALID_REDIRECT", "Checkout redirects must use HTTPS.")
    if parsed.scheme == "http" and parsed.hostname not in ("127.0.0.1", "localhost"):
        raise AccountError(400, "INVALID_REDIRECT", "Checkout redirects must use HTTPS.")
    return url


def _post_form(
    url: str,
    form: Dict[str, str],
    headers: Dict[str, str],
    timeout: float = 20.0,
) -> Tuple[int, Dict[str, Any]]:
    data = urllib.parse.urlencode(form).encode("utf-8")
    request = urllib.request.Request(
        url,
        data=data,
        headers={
            "Content-Type": "application/x-www-form-urlencoded",
            **headers,
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            body = response.read(512 * 1024)
            payload = json.loads(body.decode("utf-8")) if body else {}
            return int(response.status), payload if isinstance(payload, dict) else {}
    except urllib.error.HTTPError as exc:
        body = exc.read(512 * 1024)
        try:
            payload = json.loads(body.decode("utf-8")) if body else {}
        except ValueError:
            payload = {}
        return int(exc.code), payload if isinstance(payload, dict) else {}
    except (OSError, ValueError) as exc:
        raise AccountError(502, "PAYMENT_PROVIDER_UNAVAILABLE", "The payment provider is unreachable.") from exc


def _get_json_with_headers(
    url: str, headers: Dict[str, str], timeout: float = 20.0
) -> Tuple[int, Dict[str, Any]]:
    request = urllib.request.Request(
        url,
        headers={"Accept": "application/json", **headers},
        method="GET",
    )
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            body = response.read(512 * 1024)
            payload = json.loads(body.decode("utf-8")) if body else {}
            return int(response.status), payload if isinstance(payload, dict) else {}
    except urllib.error.HTTPError as exc:
        body = exc.read(512 * 1024)
        try:
            payload = json.loads(body.decode("utf-8")) if body else {}
        except ValueError:
            payload = {}
        return int(exc.code), payload if isinstance(payload, dict) else {}
    except (OSError, ValueError) as exc:
        raise AccountError(
            502, "PAYMENT_PROVIDER_UNAVAILABLE", "The payment provider is unreachable."
        ) from exc


def _b64url(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")


def _receipt_delivery(
    mailer: Optional[SmtpMailer],
    accounts: AccountStore,
    account_id: str,
    requested_email: str,
    package_name: str,
    gems: int,
    amount_display: str,
    provider: str,
    transaction: Dict[str, Any],
) -> Tuple[bool, str]:
    """Send a verified-purchase receipt and return ``(delivered, reason)``.

    The signed-in account email is the default destination. A receipt email explicitly
    saved in the profile may override it; no card data is ever included.
    """

    destination = (requested_email or "").strip()
    try:
        profile = accounts.profile(account_id)
        if not destination:
            destination = str(profile.get("receiptEmail") or profile.get("email") or "")
    except Exception:
        return False, "RECEIPT_EMAIL_FAILED"
    if not destination or "@" not in destination:
        return False, "RECEIPT_EMAIL_MISSING"
    if mailer is None:
        return False, "SMTP_NOT_CONFIGURED"
    try:
        delivered = mailer.send_receipt(
            destination,
            str(transaction.get("eventId") or transaction.get("id") or ""),
            package_name,
            gems,
            amount_display,
            provider,
            game_name="NEON VELOCITY",
            player_id=str(transaction.get("accountId") or account_id),
            transaction_id=str(
                transaction.get("providerRef")
                or transaction.get("eventId")
                or transaction.get("id")
                or ""
            ),
            occurred_utc=str(transaction.get("createdUtc") or ""),
            status=str(transaction.get("status") or "SUCCEEDED"),
        )
        return bool(delivered), "" if delivered else "SMTP_DELIVERY_FAILED"
    except Exception:
        return False, "RECEIPT_EMAIL_FAILED"


class StripePayments:
    def __init__(
        self,
        premium: PremiumStore,
        accounts: AccountStore,
        mailer: Optional[SmtpMailer] = None,
        config: Optional[StripeConfig] = None,
        http_post: Optional[Callable[..., Tuple[int, Dict[str, Any]]]] = None,
        clock: Optional[Callable[[], float]] = None,
    ) -> None:
        self.premium = premium
        self.accounts = accounts
        self.mailer = mailer
        self.config = config or StripeConfig()
        self._http_post = http_post or _post_form
        self._clock = clock or time.time

    @property
    def configured(self) -> bool:
        return bool(self.config.secret_key and self.config.webhook_secret and self.config.price_map)

    def create_checkout(
        self,
        account_id: str,
        package_id: str,
        receipt_email: str,
        success_url: str = "",
        cancel_url: str = "",
        client_request_id: str = "",
    ) -> Dict[str, Any]:
        if not self.config.secret_key:
            raise AccountError(
                503,
                "PROVIDER_NOT_CONFIGURED",
                "Card checkout is not configured on this server.",
                {"provider": "stripe"},
            )
        package = self.premium.package_by_id(package_id)
        if package is None:
            raise AccountError(404, "PACKAGE_NOT_FOUND", "This GEM package does not exist.")
        price_id = self.config.price_map.get(package_id, "")
        if not price_id:
            raise AccountError(
                503,
                "PRICE_NOT_CONFIGURED",
                "This package has no server-side card price yet.",
                {"provider": "stripe", "packageId": package_id},
            )
        success = _validate_redirect(success_url or self.config.success_url)
        cancel = _validate_redirect(cancel_url or self.config.cancel_url)
        if not success or not cancel:
            raise AccountError(
                503,
                "REDIRECT_NOT_CONFIGURED",
                "Checkout return URLs are not configured.",
                {"provider": "stripe"},
            )
        account = self.accounts.profile(account_id)
        email = receipt_email or str(account.get("receiptEmail") or account.get("email") or "")
        if not email:
            raise AccountError(
                400,
                "RECEIPT_EMAIL_REQUIRED",
                "Add a receipt email before starting a card payment.",
            )
        metadata = {
            "accountId": account_id,
            "packageId": package_id,
            "receiptEmail": email,
            "gems": str(package.total_gems),
        }
        form: Dict[str, str] = {
            "mode": "payment",
            "success_url": success,
            "cancel_url": cancel,
            "client_reference_id": client_request_id or ("nv-" + account_id),
            "customer_email": email,
            "line_items[0][price]": price_id,
            "line_items[0][quantity]": "1",
            "payment_method_types[0]": "card",
            "allow_promotion_codes": "false",
            "billing_address_collection": "auto",
        }
        for key, value in metadata.items():
            form["metadata[" + key + "]"] = value
        headers = {
            "Authorization": "Bearer " + self.config.secret_key,
            "Stripe-Version": "2024-06-20",
        }
        if client_request_id:
            headers["Idempotency-Key"] = "checkout-" + client_request_id[:100]
        status, payload = self._http_post(STRIPE_API, form, headers)
        if status < 200 or status >= 300:
            message = ""
            error = payload.get("error") if isinstance(payload, dict) else None
            if isinstance(error, dict):
                message = str(error.get("message") or "")
            logger.warning("Stripe checkout creation failed status=%s", status)
            raise AccountError(
                502,
                "PAYMENT_PROVIDER_ERROR",
                message[:240] or "The payment provider rejected the checkout.",
            )
        session_id = str(payload.get("id") or "")
        url = str(payload.get("url") or "")
        if not session_id.startswith("cs_") or not url.startswith("https://"):
            raise AccountError(502, "PAYMENT_PROVIDER_ERROR", "The payment provider returned an invalid session.")
        return {
            "ok": True,
            "provider": "stripe",
            "sessionId": session_id,
            "url": url,
            "packageId": package_id,
            "gems": package.total_gems,
            "receiptEmail": email,
        }

    # ---- webhooks ---------------------------------------------------------------------

    def verify_webhook(self, payload: bytes, signature_header: str) -> Dict[str, Any]:
        if not self.config.webhook_secret:
            raise AccountError(
                503,
                "PROVIDER_NOT_CONFIGURED",
                "Stripe webhooks are not configured on this server.",
                {"provider": "stripe"},
            )
        if not isinstance(signature_header, str) or not signature_header.strip():
            raise AccountError(400, "INVALID_SIGNATURE", "Missing webhook signature.")
        timestamp = 0
        signatures = []
        for part in signature_header.split(","):
            if "=" not in part:
                continue
            key, value = part.split("=", 1)
            if key.strip() == "t":
                try:
                    timestamp = int(value.strip())
                except ValueError:
                    timestamp = 0
            elif key.strip() == "v1":
                signatures.append(value.strip())
        if timestamp <= 0 or not signatures:
            raise AccountError(400, "INVALID_SIGNATURE", "Malformed webhook signature.")
        if abs(self._clock() - timestamp) > SIGNATURE_TOLERANCE_SECONDS:
            raise AccountError(400, "INVALID_SIGNATURE", "The webhook timestamp is outside the tolerance window.")
        signed = str(timestamp).encode("ascii") + b"." + payload
        expected = hmac.new(
            self.config.webhook_secret.encode("utf-8"), signed, hashlib.sha256
        ).hexdigest()
        if not any(hmac.compare_digest(expected, candidate) for candidate in signatures):
            raise AccountError(400, "INVALID_SIGNATURE", "The webhook signature is invalid.")
        try:
            event = json.loads(payload.decode("utf-8"))
        except (UnicodeDecodeError, ValueError) as exc:
            raise AccountError(400, "INVALID_WEBHOOK", "The webhook body is not valid JSON.") from exc
        if not isinstance(event, dict) or not isinstance(event.get("id"), str):
            raise AccountError(400, "INVALID_WEBHOOK", "The webhook event is malformed.")
        return event

    def handle_webhook(self, payload: bytes, signature_header: str) -> Dict[str, Any]:
        event = self.verify_webhook(payload, signature_header)
        event_type = str(event.get("type") or "")
        if event_type in ("checkout.session.completed", "checkout.session.async_payment_succeeded"):
            return self._complete_checkout(event)
        if event_type in ("checkout.session.expired", "checkout.session.async_payment_failed"):
            return self._failed_checkout(event)
        if event_type == "charge.refunded":
            return self._refund(event)
        return {"ok": True, "processed": False, "type": event_type, "reason": "IGNORED_EVENT"}

    def _complete_checkout(self, event: Dict[str, Any]) -> Dict[str, Any]:
        data = event.get("data") if isinstance(event.get("data"), dict) else {}
        session = data.get("object") if isinstance(data.get("object"), dict) else {}
        if str(session.get("payment_status") or "") != "paid":
            return {"ok": True, "processed": False, "reason": "PAYMENT_NOT_PAID"}
        metadata = session.get("metadata") if isinstance(session.get("metadata"), dict) else {}
        account_id = str(metadata.get("accountId") or "")
        package_id = str(metadata.get("packageId") or "")
        package = self.premium.package_by_id(package_id) if account_id else None
        if not account_id or package is None:
            raise AccountError(400, "INVALID_WEBHOOK", "The checkout metadata is incomplete.")
        receipt_email = str(metadata.get("receiptEmail") or session.get("customer_email") or "")
        amount_total = session.get("amount_total")
        if not isinstance(amount_total, int) or isinstance(amount_total, bool) or amount_total < 0:
            amount_total = 0
        currency = str(session.get("currency") or self.config.currency).upper()
        provider_ref = str(session.get("payment_intent") or session.get("id") or "")
        detail = json.dumps(
            {"type": event.get("type"), "sessionId": session.get("id"), "paymentIntent": session.get("payment_intent")},
            separators=(",", ":"),
        )
        created, transaction = self.accounts.record_transaction(
            "stripe",
            str(event["id"]),
            account_id,
            package_id,
            package.total_gems,
            amount_total,
            currency,
            "SUCCEEDED",
            receipt_email,
            detail,
            provider_ref,
        )
        if not created:
            return {
                "ok": True,
                "processed": False,
                "reason": "DUPLICATE_EVENT",
                "transaction": transaction,
            }
        balance = self.premium.credit_gems(
            account_id,
            package.total_gems,
            "provider_purchase",
            "Stripe {0} {1}".format(str(session.get("id") or "")[:80], package.name),
            order_id=str(session.get("id") or "") or None,
            product_id=package_id,
        )
        email_delivered, email_reason = _receipt_delivery(
            self.mailer,
            self.accounts,
            account_id,
            receipt_email,
            package.name,
            package.total_gems,
            "{0:.2f} {1}".format(amount_total / 100.0, currency),
            "stripe",
            transaction,
        )
        self.accounts.mark_transaction_status(
            transaction["id"],
            "SUCCEEDED",
            detail + " email=" + ("sent" if email_delivered else email_reason),
        )
        return {
            "ok": True,
            "processed": True,
            "gems": balance,
            "credited": package.total_gems,
            "transaction": transaction,
            "receiptEmailSent": email_delivered,
            "receiptEmailReason": email_reason,
        }

    def _failed_checkout(self, event: Dict[str, Any]) -> Dict[str, Any]:
        data = event.get("data") if isinstance(event.get("data"), dict) else {}
        session = data.get("object") if isinstance(data.get("object"), dict) else {}
        metadata = session.get("metadata") if isinstance(session.get("metadata"), dict) else {}
        account_id = str(metadata.get("accountId") or "")
        package_id = str(metadata.get("packageId") or "")
        if not account_id or self.premium.package_by_id(package_id) is None:
            return {"ok": True, "processed": False, "reason": "IGNORED_EVENT"}
        created, transaction = self.accounts.record_transaction(
            "stripe",
            str(event["id"]),
            account_id,
            package_id,
            0,
            0,
            self.config.currency.upper(),
            "FAILED",
            str(metadata.get("receiptEmail") or ""),
            str(event.get("type") or ""),
            str(session.get("id") or ""),
        )
        return {"ok": True, "processed": created, "transaction": transaction, "reason": "CHECKOUT_NOT_COMPLETED"}

    def _refund(self, event: Dict[str, Any]) -> Dict[str, Any]:
        data = event.get("data") if isinstance(event.get("data"), dict) else {}
        charge = data.get("object") if isinstance(data.get("object"), dict) else {}
        payment_intent = str(charge.get("payment_intent") or charge.get("id") or "")
        transaction = self.accounts.transaction_by_reference("stripe", payment_intent)
        if transaction is None:
            return {"ok": True, "processed": False, "reason": "TRANSACTION_NOT_FOUND"}
        if transaction["status"] == "REFUNDED":
            return {"ok": True, "processed": False, "reason": "ALREADY_REFUNDED", "transaction": transaction}
        amount_refunded = charge.get("amount_refunded")
        if not isinstance(amount_refunded, int) or amount_refunded < 0:
            amount_refunded = transaction["amountMinor"]
        # Only fully refunded checkouts reverse the whole GEM credit. Partial refunds are
        # recorded but not automatically converted into a GEM deduction; an operator can
        # reconcile them with the admin ledger.
        if amount_refunded < transaction["amountMinor"]:
            updated = self.accounts.mark_transaction_status(
                transaction["id"], "PARTIALLY_REFUNDED", "stripe partial refund"
            )
            return {"ok": True, "processed": False, "reason": "PARTIAL_REFUND_RECORDED", "transaction": updated}
        balance = self.premium.debit_gems_for_refund(
            transaction["accountId"],
            transaction["gems"],
            "Stripe refund " + str(event.get("id") or "")[:80],
            order_id=transaction["eventId"],
        )
        updated = self.accounts.mark_transaction_status(
            transaction["id"], "REFUNDED", "stripe full refund"
        )
        return {"ok": True, "processed": True, "gems": balance, "transaction": updated}


class AppleIap:
    """Apple receipt adapter. Disabled until a shared secret and product map exist."""

    def __init__(
        self,
        premium: PremiumStore,
        accounts: AccountStore,
        mailer: Optional[SmtpMailer] = None,
        config: Optional[AppleConfig] = None,
        http_post: Optional[Callable[..., Tuple[int, Dict[str, Any]]]] = None,
    ) -> None:
        self.premium = premium
        self.accounts = accounts
        self.mailer = mailer
        self.config = config or AppleConfig()
        self._http_post = http_post or _post_form

    @property
    def configured(self) -> bool:
        return bool(self.config.shared_secret and self.config.product_map)

    def verify_and_credit(
        self,
        account_id: str,
        receipt_base64: str,
        product_id: str,
        receipt_email: str = "",
    ) -> Dict[str, Any]:
        if not self.configured:
            raise AccountError(
                503,
                "PROVIDER_NOT_CONFIGURED",
                "Apple In-App Purchase verification is not configured on this server.",
                {"provider": "apple"},
            )
        package_id = self.config.product_map.get(product_id, "")
        if not package_id and self.premium.package_by_id(product_id) is not None:
            package_id = product_id
        package = self.premium.package_by_id(package_id)
        if package is None:
            raise AccountError(400, "INVALID_PRODUCT", "This App Store product is not mapped to a GEM package.")
        if not isinstance(receipt_base64, str) or not receipt_base64 or len(receipt_base64) > 512 * 1024:
            raise AccountError(400, "INVALID_RECEIPT", "The App Store receipt is invalid.")
        status, payload = self._http_post(
            APPLE_VERIFY,
            {"receipt-data": receipt_base64, "password": self.config.shared_secret, "exclude-old-transactions": "true"},
            {},
        )
        if status == 200 and int(payload.get("status", -1)) == 21007:
            status, payload = self._http_post(
                APPLE_SANDBOX_VERIFY,
                {"receipt-data": receipt_base64, "password": self.config.shared_secret, "exclude-old-transactions": "true"},
                {},
            )
        if status != 200 or int(payload.get("status", -1)) != 0:
            raise AccountError(400, "INVALID_RECEIPT", "Apple rejected the receipt.")
        purchases = payload.get("latest_receipt_info") or payload.get("in_app") or []
        if not isinstance(purchases, list):
            raise AccountError(400, "INVALID_RECEIPT", "Apple returned no transactions.")
        selected = None
        for purchase in reversed(purchases):
            if not isinstance(purchase, dict):
                continue
            if str(purchase.get("product_id") or "") != product_id:
                continue
            selected = purchase
            break
        if selected is None:
            raise AccountError(400, "INVALID_RECEIPT", "The App Store receipt does not contain this product.")
        transaction_id = str(selected.get("transaction_id") or "")
        if not transaction_id:
            raise AccountError(400, "INVALID_RECEIPT", "The App Store transaction has no identifier.")
        amount_minor = 0
        currency = "USD"
        raw_price = selected.get("price")
        raw_currency = str(selected.get("currency") or "")
        try:
            if raw_price is not None:
                amount_minor = int(round(float(str(raw_price)) * 100))
            if raw_currency:
                currency = raw_currency.upper()
        except (TypeError, ValueError):
            amount_minor = 0
        created, transaction = self.accounts.record_transaction(
            "apple",
            "apple:" + transaction_id,
            account_id,
            package_id,
            package.total_gems,
            amount_minor,
            currency,
            "SUCCEEDED",
            receipt_email,
            "apple receipt",
            transaction_id,
        )
        if not created:
            return {"ok": True, "processed": False, "reason": "DUPLICATE_TRANSACTION", "transaction": transaction}
        balance = self.premium.credit_gems(
            account_id,
            package.total_gems,
            "provider_purchase",
            "Apple IAP " + transaction_id[:80],
            order_id=transaction_id,
            product_id=package_id,
        )
        if amount_minor > 0:
            amount_display = "{0:.2f} {1}".format(amount_minor / 100.0, currency)
        else:
            amount_display = "See App Store purchase record ({0})".format(currency)
        email_delivered, email_reason = _receipt_delivery(
            self.mailer,
            self.accounts,
            account_id,
            receipt_email,
            package.name,
            package.total_gems,
            amount_display,
            "apple",
            transaction,
        )
        return {
            "ok": True,
            "processed": True,
            "gems": balance,
            "credited": package.total_gems,
            "transaction": transaction,
            "receiptEmailSent": email_delivered,
            "receiptEmailReason": email_reason,
        }


class GoogleServiceAccountVerifier:
    """Verify Google Play product purchases with a server-held service account.

    The JSON key is supplied through ``NV_GOOGLE_PLAY_SERVICE_ACCOUNT`` (never the client).
    A signed JWT assertion is exchanged for a short-lived OAuth token, then the purchase is
    read from the Android Publisher API. Only ``purchaseState == 0`` (purchased) credits.
    """

    TOKEN_URL = "https://oauth2.googleapis.com/token"
    SCOPE = "https://www.googleapis.com/auth/androidpublisher"
    API_ROOT = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications"

    def __init__(
        self,
        service_account_json: str,
        package_name: str,
        product_map: Optional[Dict[str, str]] = None,
        http_post: Optional[Callable[..., Tuple[int, Dict[str, Any]]]] = None,
        http_get: Optional[Callable[..., Tuple[int, Dict[str, Any]]]] = None,
        clock: Optional[Callable[[], float]] = None,
    ) -> None:
        self.service_account_json = service_account_json or ""
        self.package_name = (package_name or "").strip()
        self.product_map = dict(product_map or {})
        self._http_post = http_post or _post_form
        self._http_get = http_get or _get_json_with_headers
        self._clock = clock or time.time
        self._token = ""
        self._token_expires = 0.0

    @property
    def configured(self) -> bool:
        return bool(self.service_account_json and self.package_name)

    def _credentials(self) -> Dict[str, Any]:
        try:
            payload = json.loads(self.service_account_json)
        except ValueError as exc:
            raise AccountError(
                503,
                "PROVIDER_NOT_CONFIGURED",
                "NV_GOOGLE_PLAY_SERVICE_ACCOUNT is not valid JSON.",
            ) from exc
        if not isinstance(payload, dict):
            raise AccountError(
                503,
                "PROVIDER_NOT_CONFIGURED",
                "NV_GOOGLE_PLAY_SERVICE_ACCOUNT is not a JSON object.",
            )
        for field in ("client_email", "private_key"):
            if not isinstance(payload.get(field), str) or not payload.get(field):
                raise AccountError(
                    503,
                    "PROVIDER_NOT_CONFIGURED",
                    "The Google Play service account is incomplete.",
                )
        return payload

    def _access_token(self) -> str:
        now = int(self._clock())
        if self._token and now < self._token_expires - 60:
            return self._token
        credentials = self._credentials()
        token_uri = str(credentials.get("token_uri") or self.TOKEN_URL)
        header = {"alg": "RS256", "typ": "JWT"}
        claims = {
            "iss": str(credentials["client_email"]),
            "scope": self.SCOPE,
            "aud": token_uri,
            "iat": now,
            "exp": now + 3600,
        }
        signing_input = (
            _b64url(json.dumps(header, separators=(",", ":")).encode("utf-8"))
            + "."
            + _b64url(json.dumps(claims, separators=(",", ":")).encode("utf-8"))
        )
        try:
            assertion = (
                signing_input
                + "."
                + _b64url(sign_rs256(str(credentials["private_key"]), signing_input.encode("ascii")))
            )
        except (ValueError, TypeError) as exc:
            raise AccountError(
                503,
                "PROVIDER_NOT_CONFIGURED",
                "The Google Play service-account private key is invalid.",
            ) from exc
        status, payload = self._http_post(
            token_uri,
            {
                "grant_type": "urn:ietf:params:oauth:grant-type:jwt-bearer",
                "assertion": assertion,
            },
            {},
        )
        token = str(payload.get("access_token") or "") if isinstance(payload, dict) else ""
        if status < 200 or status >= 300 or not token:
            raise AccountError(
                502, "PAYMENT_PROVIDER_ERROR", "Google rejected the service-account token."
            )
        try:
            expires_in = int(payload.get("expires_in") or 3600)
        except (TypeError, ValueError):
            expires_in = 3600
        self._token = token
        self._token_expires = self._clock() + max(60, expires_in)
        return token

    def __call__(self, product_id: str, purchase_token: str) -> Dict[str, Any]:
        if not self.configured:
            raise AccountError(
                503,
                "PROVIDER_NOT_CONFIGURED",
                "Google Play verification is not configured on this server.",
                {"provider": "google_play"},
            )
        token = self._access_token()
        url = (
            self.API_ROOT
            + "/"
            + urllib.parse.quote(self.package_name, safe="")
            + "/purchases/products/"
            + urllib.parse.quote(product_id, safe="")
            + "/tokens/"
            + urllib.parse.quote(purchase_token, safe="")
        )
        status, payload = self._http_get(
            url, {"Authorization": "Bearer " + token}
        )
        if status == 401:
            self._token = ""
            self._token_expires = 0.0
            raise AccountError(502, "PAYMENT_PROVIDER_ERROR", "Google Play authentication expired.")
        if status == 404:
            raise AccountError(400, "INVALID_RECEIPT", "Google Play does not know this purchase.")
        if status < 200 or status >= 300 or not isinstance(payload, dict):
            raise AccountError(502, "PAYMENT_PROVIDER_ERROR", "Google Play verification failed.")
        purchase_state = payload.get("purchaseState")
        if purchase_state != 0:
            raise AccountError(400, "INVALID_RECEIPT", "The Google Play purchase is not completed.")
        order_id = str(payload.get("orderId") or "")
        if not order_id:
            raise AccountError(400, "INVALID_RECEIPT", "The Google Play purchase has no order id.")
        amount_minor = 0
        try:
            micros = int(payload.get("priceAmountMicros") or 0)
            if micros > 0:
                amount_minor = int(round(micros / 10_000))
        except (TypeError, ValueError):
            amount_minor = 0
        return {
            "packageId": self.product_map.get(product_id, product_id),
            "transactionId": order_id,
            "amountMinor": amount_minor,
            "currency": str(payload.get("priceCurrencyCode") or "USD").upper(),
            "purchaseState": purchase_state,
        }


class GooglePlayIap:
    """Google Play adapter boundary.

    Live purchase-token validation requires a Play developer account, a service-account
    key and an OAuth2 token exchange. Those credentials are deliberately not accepted from
    the game client or embedded in this repository. Until ``NV_GOOGLE_PLAY_VERIFY`` supplies
    a callable verifier, the endpoint reports ``PROVIDER_NOT_CONFIGURED``.
    """

    def __init__(
        self,
        premium: PremiumStore,
        accounts: AccountStore,
        verifier: Optional[Callable[[str, str], Dict[str, Any]]] = None,
        mailer: Optional[SmtpMailer] = None,
    ) -> None:
        self.premium = premium
        self.accounts = accounts
        self.verifier = verifier
        self.mailer = mailer

    @property
    def configured(self) -> bool:
        return self.verifier is not None

    def verify_and_credit(
        self,
        account_id: str,
        purchase_token: str,
        product_id: str,
        receipt_email: str = "",
    ) -> Dict[str, Any]:
        if self.verifier is None:
            raise AccountError(
                503,
                "PROVIDER_NOT_CONFIGURED",
                "Google Play verification is not configured on this server.",
                {"provider": "google_play"},
            )
        try:
            result = self.verifier(product_id, purchase_token)
        except Exception as exc:
            raise AccountError(400, "INVALID_RECEIPT", "Google Play rejected the purchase.") from exc
        if not isinstance(result, dict):
            raise AccountError(400, "INVALID_RECEIPT", "Google Play rejected the purchase.")
        package_id = str(result.get("packageId") or "")
        package = self.premium.package_by_id(package_id)
        transaction_id = str(result.get("transactionId") or "")
        if package is None or not transaction_id:
            raise AccountError(400, "INVALID_RECEIPT", "The Google Play purchase is incomplete.")
        amount_minor = result.get("amountMinor")
        if not isinstance(amount_minor, int) or isinstance(amount_minor, bool) or amount_minor < 0:
            amount_minor = 0
        currency = str(result.get("currency") or "USD").upper()
        created, transaction = self.accounts.record_transaction(
            "google_play",
            "google:" + transaction_id,
            account_id,
            package_id,
            package.total_gems,
            amount_minor,
            currency,
            "SUCCEEDED",
            receipt_email,
            "google play purchase",
            transaction_id,
        )
        if not created:
            return {"ok": True, "processed": False, "reason": "DUPLICATE_TRANSACTION", "transaction": transaction}
        balance = self.premium.credit_gems(
            account_id,
            package.total_gems,
            "provider_purchase",
            "Google Play " + transaction_id[:80],
            order_id=transaction_id,
            product_id=package_id,
        )
        amount_display = (
            "{0:.2f} {1}".format(amount_minor / 100.0, currency)
            if amount_minor > 0
            else "See Google Play purchase record ({0})".format(currency)
        )
        email_delivered, email_reason = _receipt_delivery(
            self.mailer,
            self.accounts,
            account_id,
            receipt_email,
            package.name,
            package.total_gems,
            amount_display,
            "google_play",
            transaction,
        )
        return {
            "ok": True,
            "processed": True,
            "gems": balance,
            "credited": package.total_gems,
            "transaction": transaction,
            "receiptEmailSent": email_delivered,
            "receiptEmailReason": email_reason,
        }
