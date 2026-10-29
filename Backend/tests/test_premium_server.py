#!/usr/bin/env python3
"""Integration tests for the NEON VELOCITY premium currency service.

Every test boots the real threaded HTTP server on an ephemeral loopback port against a
fresh temporary data directory, then drives it over HTTP exactly as a client would.
"""

from __future__ import annotations

import base64
import dataclasses
import datetime as dt
import hashlib
import hmac
import json
import pathlib
import secrets
import stat
import sys
import tempfile
import time
import unittest
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, Dict, List, Optional, Tuple

TEST_DIR = pathlib.Path(__file__).resolve().parent
BACKEND_DIR = TEST_DIR.parent
if str(BACKEND_DIR) not in sys.path:
    sys.path.insert(0, str(BACKEND_DIR))

from premium_server import PremiumApplication, PremiumConfig, PremiumServer  # noqa: E402
from premium_store import (  # noqa: E402
    ORDER_STATUSES,
    hash_password,
)

TINY_PNG = base64.b64decode(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk"
    "+A8AAQUBAScY42YAAAAASUVORK5CYII="
)
ADMIN_USERNAME = "admin"
ADMIN_PASSWORD = "test-admin-password"
BASE_NOW = dt.datetime(2026, 1, 1, tzinfo=dt.timezone.utc)


@dataclasses.dataclass
class ApiResponse:
    status: int
    headers: Dict[str, str]
    payload: Optional[Dict[str, Any]]
    body: bytes


class PremiumApiTestBase(unittest.TestCase):
    """Fresh process-level server and SQLite database for each test."""

    max_proof_bytes = 4096
    manual_bank_transfer = True

    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory(prefix="nv-premium-test-")
        self.data_dir = pathlib.Path(self.temp_dir.name)
        self.config = PremiumConfig(
            host="127.0.0.1",
            port=0,
            data_dir=self.data_dir,
            admin_username=ADMIN_USERNAME,
            admin_password=ADMIN_PASSWORD,
            max_proof_bytes=self.max_proof_bytes,
            auth_token_ttl_seconds=3600,
            guest_rate_limit=1000,
            admin_login_rate_limit=5,
            rate_window_seconds=60,
            allowed_origin="*",
            auth_dev_mode=True,
            stripe_webhook_secret="whsec_test_secret",
            auth_rate_limit=1000,
            manual_bank_transfer=self.manual_bank_transfer,
        )
        self.server = PremiumServer(self.config)
        self.server.start()
        self.base_url = "http://127.0.0.1:{0}".format(self.server.port)
        self._admin_token: Optional[str] = None

    def tearDown(self) -> None:
        if getattr(self, "server", None) is not None:
            self.server.stop()
        self.temp_dir.cleanup()

    # ---- HTTP transport ---------------------------------------------------------------

    def request(
        self,
        method: str,
        path: str,
        *,
        json_body: Any = None,
        body: Optional[bytes] = None,
        token: Optional[str] = None,
        content_type: Optional[str] = None,
        extra_headers: Optional[Dict[str, str]] = None,
    ) -> ApiResponse:
        headers: Dict[str, str] = {"Accept": "application/json"}
        data: Optional[bytes] = body
        if json_body is not None:
            data = json.dumps(
                json_body, ensure_ascii=False, separators=(",", ":")
            ).encode("utf-8")
            if content_type is None:
                content_type = "application/json"
        if content_type is not None:
            headers["Content-Type"] = content_type
        if token is not None:
            headers["Authorization"] = "Bearer " + token
        if extra_headers:
            headers.update(extra_headers)
        request = urllib.request.Request(
            self.base_url + path,
            data=data,
            headers=headers,
            method=method,
        )
        try:
            with urllib.request.urlopen(request, timeout=10) as response:
                status = response.status
                response_body = response.read()
                response_headers = {
                    key.lower(): value for key, value in response.headers.items()
                }
        except urllib.error.HTTPError as exc:
            status = exc.code
            response_body = exc.read()
            response_headers = {
                key.lower(): value for key, value in exc.headers.items()
            }
        payload = None
        content = response_headers.get("content-type", "")
        if response_body and content.startswith("application/json"):
            payload = json.loads(response_body.decode("utf-8"))
        return ApiResponse(status, response_headers, payload, response_body)

    def get(
        self, path: str, token: Optional[str] = None
    ) -> ApiResponse:
        return self.request("GET", path, token=token)

    def post(
        self,
        path: str,
        payload: Any,
        token: Optional[str] = None,
    ) -> ApiResponse:
        return self.request("POST", path, json_body=payload, token=token)

    def assert_ok(self, response: ApiResponse) -> Dict[str, Any]:
        self.assertEqual(response.status, 200, response.body)
        self.assertIsInstance(response.payload, dict)
        assert response.payload is not None
        self.assertTrue(response.payload.get("ok"))
        return response.payload

    def assert_error(
        self, response: ApiResponse, status: int, code: str
    ) -> Dict[str, Any]:
        self.assertEqual(response.status, status, response.body)
        self.assertIsInstance(response.payload, dict)
        assert response.payload is not None
        self.assertFalse(response.payload.get("ok"))
        self.assertEqual(response.payload.get("code"), code)
        self.assertIn("error", response.payload)
        return response.payload

    # ---- domain helpers ---------------------------------------------------------------

    def guest(
        self, name: str = "RUNNER", device_key: Optional[str] = None
    ) -> Dict[str, Any]:
        device = device_key or ("dev-" + secrets.token_hex(16))
        response = self.post(
            "/api/v1/auth/guest",
            {"deviceKey": device, "playerName": name},
        )
        payload = self.assert_ok(response)
        self.assertGreaterEqual(len(device), 16)
        self.assertEqual(len(payload["token"]) > 20, True)
        return payload

    def admin_token(self) -> str:
        response = self.post(
            "/api/v1/admin/login",
            {"username": ADMIN_USERNAME, "password": ADMIN_PASSWORD},
        )
        payload = self.assert_ok(response)
        token = payload["token"]
        self.assertIsInstance(token, str)
        return token

    def register(self, email: str = "runner@example.com") -> Dict[str, Any]:
        response = self.post(
            "/api/v1/auth/register",
            {"email": email, "password": "GridRunner42", "playerName": "RUNNER-01"},
        )
        payload = self.assert_ok(response)
        self.assertTrue(payload["token"])
        return payload

    def cached_admin_token(self) -> str:
        if self._admin_token is None:
            self._admin_token = self.admin_token()
        return self._admin_token

    def create_order(
        self,
        token: str,
        package_id: str = "gems_80",
        client_request_id: Optional[str] = None,
    ) -> Dict[str, Any]:
        key = client_request_id or ("order-" + secrets.token_hex(8))
        response = self.post(
            "/api/v1/orders",
            {"packageId": package_id, "clientRequestId": key},
            token=token,
        )
        return self.assert_ok(response)

    def order_detail(self, token: str, order_id: str) -> Dict[str, Any]:
        response = self.get("/api/v1/orders/{0}".format(order_id), token=token)
        payload = self.assert_ok(response)
        return payload["order"]

    @staticmethod
    def multipart_proof(
        data: bytes = TINY_PNG,
        filename: str = "payment.png",
        content_type: str = "image/png",
        note: Optional[str] = None,
    ) -> Tuple[bytes, str]:
        boundary = "----NVTest" + secrets.token_hex(12)
        parts: List[bytes] = []

        def add_text(name: str, value: str) -> None:
            parts.append(
                (
                    "--{0}\r\n"
                    'Content-Disposition: form-data; name="{1}"\r\n'
                    "\r\n"
                    "{2}\r\n"
                ).format(boundary, name, value).encode("utf-8")
            )

        add_text("note", note or "")
        parts.append(
            (
                "--{0}\r\n"
                'Content-Disposition: form-data; name="proof"; filename="{1}"\r\n'
                "Content-Type: {2}\r\n"
                "\r\n"
            ).format(boundary, filename, content_type).encode("utf-8")
        )
        parts.append(data)
        parts.append("\r\n".encode("utf-8"))
        parts.append("--{0}--\r\n".format(boundary).encode("utf-8"))
        body = b"".join(parts)
        return body, "multipart/form-data; boundary=" + boundary

    def submit_proof(
        self,
        token: str,
        order_id: str,
        data: bytes = TINY_PNG,
        filename: str = "payment.png",
        content_type: str = "image/png",
        note: Optional[str] = None,
    ) -> ApiResponse:
        body, multipart_type = self.multipart_proof(
            data, filename, content_type, note
        )
        return self.request(
            "POST",
            "/api/v1/orders/{0}/proof".format(order_id),
            body=body,
            content_type=multipart_type,
            token=token,
        )

    def verified_order(
        self, token: str, package_id: str = "gems_80"
    ) -> Dict[str, Any]:
        created = self.create_order(token, package_id)
        order_id = created["order"]["orderId"]
        response = self.submit_proof(token, order_id)
        self.assert_ok(response)
        return self.order_detail(token, order_id)

    def approve_order(
        self, order_id: str, note: str = "Payment verified"
    ) -> Dict[str, Any]:
        token = self.cached_admin_token()
        response = self.post(
            "/api/v1/admin/orders/{0}/decision".format(order_id),
            {"decision": "approve", "note": note},
            token=token,
        )
        return self.assert_ok(response)

    def grant_gems(
        self, token: str, package_id: str = "gems_80"
    ) -> Dict[str, Any]:
        order = self.verified_order(token, package_id)
        return self.approve_order(order["orderId"])


# ---- tests: health, authentication, catalogue, orders -------------------------------


class PublicApiTests(PremiumApiTestBase):
    def test_health_response_and_headers(self) -> None:
        response = self.get("/api/v1/health")
        payload = self.assert_ok(response)
        self.assertEqual(payload["service"], "neon-velocity-premium")
        self.assertEqual(payload["version"], "1.0.0")
        self.assertTrue(payload["time"].endswith("Z"))
        self.assertEqual(
            response.headers["content-type"],
            "application/json; charset=utf-8",
        )
        self.assertEqual(response.headers["access-control-allow-origin"], "*")
        self.assertEqual(response.headers["x-content-type-options"], "nosniff")

    def test_guest_auth_is_stable_and_wallet_starts_empty(self) -> None:
        device = "device-" + secrets.token_hex(16)
        first = self.guest("  Neon_Runner  ", device)
        self.assertEqual(first["displayName"], "Neon_Runner")
        self.assertEqual(first["gems"], 0)
        self.assertTrue(first["accountId"].startswith("acc_"))

        second_response = self.post(
            "/api/v1/auth/guest",
            {"deviceKey": device, "playerName": "Renamed"},
        )
        second = self.assert_ok(second_response)
        self.assertEqual(second["accountId"], first["accountId"])
        self.assertNotEqual(second["token"], first["token"])

        wallet = self.assert_ok(self.get("/api/v1/wallet", token=second["token"]))
        self.assertEqual(wallet["gems"], 0)
        self.assertEqual(wallet["pendingOrders"], 0)
        self.assertTrue(wallet["serverTime"].endswith("Z"))

    def test_catalog_contains_fixed_packages_and_products(self) -> None:
        payload = self.assert_ok(self.get("/api/v1/store/catalog"))
        self.assertEqual(payload["reviveCostGems"], 40)
        packages = {item["id"]: item for item in payload["packages"]}
        self.assertEqual(
            set(packages),
            {"gems_80", "gems_200", "gems_500", "gems_1200", "gems_2500"},
        )
        self.assertEqual(packages["gems_80"]["priceLkr"], 490)
        self.assertEqual(packages["gems_80"]["tier"], "STARTER")
        self.assertEqual(packages["gems_200"]["totalGems"], 210)
        self.assertEqual(packages["gems_200"]["tier"], "BASIC")
        self.assertEqual(packages["gems_500"]["bonusGems"], 50)
        self.assertEqual(packages["gems_500"]["tier"], "PREMIUM")
        self.assertEqual(packages["gems_1200"]["gems"], 1200)
        self.assertEqual(packages["gems_1200"]["priceLkr"], 6490)
        self.assertEqual(packages["gems_1200"]["tier"], "MEGA")
        self.assertEqual(packages["gems_2500"]["totalGems"], 3000)
        self.assertEqual(packages["gems_2500"]["priceLkr"], 12900)
        self.assertEqual(packages["gems_2500"]["tier"], "ULTIMATE")
        for package in packages.values():
            self.assertEqual(package["currency"], "LKR")
            self.assertEqual(
                package["totalGems"], package["gems"] + package["bonusGems"]
            )
            self.assertTrue(package["enabled"])
            self.assertTrue(package["description"])

        products = {item["id"]: item for item in payload["products"]}
        self.assertEqual(len(products), 18)
        phantom = products["char_phantom_x"]
        self.assertEqual(phantom["category"], "character")
        self.assertEqual(phantom["name"], "PHANTOM X")
        self.assertEqual(phantom["characterId"], "phantom")
        self.assertEqual(phantom["priceGems"], 450)
        self.assertEqual(products["char_nova_titan"]["characterId"], "nova")
        self.assertEqual(products["char_echo"]["characterId"], "echo")
        self.assertEqual(products["char_echo"]["priceGems"], 520)
        self.assertEqual(products["char_sable"]["characterId"], "sable")
        self.assertEqual(products["char_sable"]["priceGems"], 620)
        self.assertEqual(products["char_helios"]["characterId"], "helios")
        self.assertEqual(products["char_helios"]["priceGems"], 760)
        self.assertEqual(products["skin_phantom_void"]["priceGems"], 120)
        self.assertEqual(products["skin_nova_solar"]["priceGems"], 160)
        self.assertEqual(products["armor_spectre"]["category"], "armor")
        self.assertEqual(products["helmet_ghost"]["priceGems"], 90)
        self.assertEqual(products["fx_phase_wake"]["category"], "effect")
        self.assertEqual(
            products["move_quantum"]["category"], "movement_effect"
        )
        self.assertEqual(products["anim_kneel_rise"]["priceGems"], 80)
        self.assertEqual(products["ability_phase_dash"]["priceGems"], 260)
        self.assertEqual(products["ability_nova_burst"]["priceGems"], 320)
        self.assertEqual(products["revive_token"]["priceGems"], 35)
        stabilizer = products["upgrade_revive_stabilizer"]
        self.assertEqual(stabilizer["category"], "upgrade")
        self.assertEqual(stabilizer["upgradeId"], "revive_stabilizer")
        self.assertEqual(stabilizer["maxLevel"], 3)
        self.assertEqual(products["upgrade_phase_effect"]["priceGems"], 90)
        self.assertEqual(products["upgrade_nova_effect"]["priceGems"], 90)
        for product in products.values():
            self.assertTrue(product["enabled"])
            self.assertIn("characterId", product)
            self.assertIn("upgradeId", product)
            self.assertIn("maxLevel", product)

    def test_ownership_starts_empty(self) -> None:
        token = self.guest()["token"]
        payload = self.assert_ok(self.get("/api/v1/ownership", token=token))
        self.assertEqual(payload["items"], [])
        self.assertEqual(payload["characters"], [])

    def test_create_order_uses_exact_server_amount_and_bank_details(self) -> None:
        token = self.guest()["token"]
        created = self.create_order(token, "gems_200")
        order = created["order"]
        self.assertRegex(order["orderId"], r"^NV-[0-9]{8}-[A-Z0-9]{8}$")
        self.assertEqual(order["packageId"], "gems_200")
        self.assertEqual(order["packageName"], "BASIC CACHE")
        self.assertEqual(order["gems"], 200)
        self.assertEqual(order["bonusGems"], 10)
        self.assertEqual(order["amountLkr"], 1190)
        self.assertEqual(order["currency"], "LKR")
        self.assertEqual(order["status"], "PENDING_PAYMENT")
        self.assertEqual(order["reference"], order["orderId"])
        self.assertFalse(order["proofSubmitted"])
        self.assertEqual(order["adminNote"], "")
        created_time = dt.datetime.fromisoformat(
            order["createdUtc"].replace("Z", "+00:00")
        )
        expires_time = dt.datetime.fromisoformat(
            order["expiresUtc"].replace("Z", "+00:00")
        )
        self.assertEqual(expires_time - created_time, dt.timedelta(days=7))
        self.assertEqual(
            created["bank"],
            {
                "accountNumber": "8022347868",
                "accountName": "G.W.W.M. R.K. Kirinda",
                "bankName": "Commercial Bank PLC",
                "branch": "Athurugiriya Branch",
                "currency": "LKR",
            },
        )
        wallet = self.assert_ok(self.get("/api/v1/wallet", token=token))
        self.assertEqual(wallet["pendingOrders"], 1)
        detail = self.order_detail(token, order["orderId"])
        self.assertEqual(detail["orderId"], order["orderId"])

    def test_order_idempotency_is_scoped_to_package(self) -> None:
        token = self.guest()["token"]
        key = "checkout-" + secrets.token_hex(8)
        first = self.create_order(token, "gems_80", key)
        second = self.create_order(token, "gems_80", key)
        self.assertEqual(
            first["order"]["orderId"], second["order"]["orderId"]
        )
        different = self.create_order(token, "gems_500", key)
        self.assertNotEqual(
            first["order"]["orderId"], different["order"]["orderId"]
        )
        listed = self.assert_ok(self.get("/api/v1/orders", token=token))
        ids = [order["orderId"] for order in listed["orders"]]
        self.assertEqual(len(ids), 2)
        self.assertEqual(ids[0], different["order"]["orderId"])

    def test_malformed_json_and_invalid_fields_are_rejected(self) -> None:
        bad = self.request(
            "POST",
            "/api/v1/auth/guest",
            body=b"{not-json",
            content_type="application/json",
        )
        self.assert_error(bad, 400, "INVALID_JSON")
        array = self.request(
            "POST",
            "/api/v1/auth/guest",
            body=b"[]",
            content_type="application/json",
        )
        self.assert_error(array, 400, "INVALID_JSON")
        short_device = self.post(
            "/api/v1/auth/guest",
            {"deviceKey": "short", "playerName": "RUNNER"},
        )
        self.assert_error(short_device, 400, "INVALID_DEVICE_KEY")
        empty_name = self.post(
            "/api/v1/auth/guest",
            {"deviceKey": "device-" + secrets.token_hex(12), "playerName": "!!!"},
        )
        self.assert_error(empty_name, 400, "INVALID_PLAYER_NAME")
        bad_type = self.post(
            "/api/v1/auth/guest",
            {"deviceKey": 1234567890123456, "playerName": "RUNNER"},
        )
        self.assert_error(bad_type, 400, "INVALID_DEVICE_KEY")
        oversized = self.request(
            "POST",
            "/api/v1/auth/guest",
            body=b"{" + b"a" * (70 * 1024) + b"}",
            content_type="application/json",
        )
        self.assert_error(oversized, 413, "PAYLOAD_TOO_LARGE")

    def test_unsupported_method_and_unknown_route(self) -> None:
        method = self.request("PUT", "/api/v1/store/catalog", body=b"")
        self.assert_error(method, 405, "METHOD_NOT_ALLOWED")
        unusual = self.request("PROPFIND", "/api/v1/store/catalog", body=b"")
        self.assert_error(unusual, 405, "METHOD_NOT_ALLOWED")
        unknown = self.get("/api/v1/does-not-exist")
        self.assert_error(unknown, 404, "NOT_FOUND")
        options = self.request("OPTIONS", "/api/v1/orders")
        self.assertEqual(options.status, 204)
        self.assertEqual(
            options.headers["access-control-allow-methods"],
            "GET, POST, OPTIONS",
        )
        self.assertIn(
            "Authorization", options.headers["access-control-allow-headers"]
        )

    def test_non_admin_token_cannot_use_admin_endpoints(self) -> None:
        token = self.guest()["token"]
        self.assert_error(
            self.get("/api/v1/admin/orders", token=token),
            401,
            "ADMIN_UNAUTHORIZED",
        )
        admin = self.cached_admin_token()
        self.assert_error(
            self.get("/api/v1/wallet", token=admin),
            401,
            "UNAUTHORIZED",
        )
        self.assert_error(self.get("/api/v1/wallet"), 401, "UNAUTHORIZED")
        self.assert_error(
            self.get("/api/v1/wallet", token="x" * 40), 401, "UNAUTHORIZED"
        )

    def test_expired_and_unknown_tokens_are_rejected(self) -> None:
        account = self.guest()
        expired = self.server.store.issue_auth_token(account["accountId"], -60)
        self.assert_error(
            self.get("/api/v1/wallet", token=expired),
            401,
            "UNAUTHORIZED",
        )
        self.assert_error(
            self.get("/api/v1/wallet", token="unknown-" + "z" * 40),
            401,
            "UNAUTHORIZED",
        )
        expired_admin, _ = self.server.store.create_admin_token(
            ADMIN_USERNAME, -60
        )
        self.assert_error(
            self.get("/api/v1/admin/stats", token=expired_admin),
            401,
            "ADMIN_UNAUTHORIZED",
        )


# ---- tests: proof handling and admin verification -----------------------------------


class ManualTransferDisabledTests(PremiumApiTestBase):
    """Normal purchases must not expose the legacy bank-transfer workflow."""

    manual_bank_transfer = False

    def test_capabilities_report_disabled_and_orders_fail_closed(self) -> None:
        capabilities = self.assert_ok(self.get("/api/v1/store/payments"))
        self.assertFalse(capabilities["payments"]["manualBankTransfer"])
        token = self.guest()["token"]
        created = self.post(
            "/api/v1/orders",
            {"packageId": "gems_80", "clientRequestId": "disabled-order"},
            token=token,
        )
        self.assert_error(created, 403, "MANUAL_TRANSFER_DISABLED")
        listed = self.assert_ok(self.get("/api/v1/orders", token=token))
        self.assertEqual(listed["orders"], [])


class ProofAndAdminTests(PremiumApiTestBase):
    def test_proof_rejects_invalid_magic_empty_and_oversize(self) -> None:
        token = self.guest()["token"]
        order = self.create_order(token, "gems_80")["order"]
        order_id = order["orderId"]

        invalid = self.submit_proof(
            token, order_id, data=b"not-an-image", filename="fake.png"
        )
        self.assert_error(invalid, 415, "UNSUPPORTED_PROOF_TYPE")

        empty = self.submit_proof(
            token, order_id, data=b"", filename="empty.png"
        )
        self.assert_error(empty, 400, "EMPTY_PROOF")

        oversized = self.submit_proof(
            token,
            order_id,
            data=b"\x89PNG\r\n\x1a\n" + b"x" * (self.max_proof_bytes + 100),
            filename="large.png",
        )
        self.assert_error(oversized, 413, "PROOF_TOO_LARGE")

        malformed = self.request(
            "POST",
            "/api/v1/orders/{0}/proof".format(order_id),
            body=b"definitely not multipart",
            content_type="multipart/form-data",
            token=token,
        )
        self.assert_error(malformed, 400, "MALFORMED_MULTIPART")

        unchanged = self.order_detail(token, order_id)
        self.assertEqual(unchanged["status"], "PENDING_PAYMENT")
        self.assertFalse(unchanged["proofSubmitted"])

    def test_valid_proof_admin_listing_and_download(self) -> None:
        account = self.guest("PROOF USER")
        token = account["token"]
        order = self.create_order(token, "gems_200")["order"]
        order_id = order["orderId"]
        accepted = self.submit_proof(
            token, order_id, note="Paid from account ending 1234"
        )
        payload = self.assert_ok(accepted)
        self.assertEqual(payload["order"]["status"], "PENDING_VERIFICATION")
        self.assertTrue(payload["order"]["proofSubmitted"])
        self.assertEqual(payload["order"]["amountLkr"], 1190)

        admin = self.cached_admin_token()
        listing = self.assert_ok(
            self.get(
                "/api/v1/admin/orders?status=PENDING_VERIFICATION",
                token=admin,
            )
        )
        self.assertEqual(len(listing["orders"]), 1)
        admin_order = listing["orders"][0]
        self.assertEqual(admin_order["orderId"], order_id)
        self.assertEqual(admin_order["accountId"], account["accountId"])
        self.assertEqual(admin_order["displayName"], "PROOF USER")
        proof = admin_order["proof"]
        self.assertEqual(proof["originalFilename"], "payment.png")
        self.assertEqual(proof["mime"], "image/png")
        self.assertEqual(proof["sizeBytes"], len(TINY_PNG))
        self.assertEqual(proof["sha256"], hashlib.sha256(TINY_PNG).hexdigest())
        self.assertTrue(proof["submittedUtc"].endswith("Z"))
        self.assertNotIn("proofPath", listing)
        self.assertNotIn("proof_path", listing)
        self.assertNotIn(TINY_PNG, listing_body_bytes(listing))

        user_orders = self.assert_ok(
            self.get("/api/v1/orders", token=token)
        )["orders"]
        self.assertEqual(len(user_orders), 1)
        self.assertEqual(user_orders[0]["orderId"], order_id)
        self.assertTrue(user_orders[0]["proofSubmitted"])

        download = self.get(
            "/api/v1/admin/orders/{0}/proof".format(order_id), token=admin
        )
        self.assertEqual(download.status, 200)
        self.assertEqual(download.body, TINY_PNG)
        self.assertEqual(download.headers["content-type"], "image/png")
        self.assertIn(order_id, download.headers["content-disposition"])
        self.assertEqual(
            download.headers["content-length"], str(len(TINY_PNG))
        )

        self.assert_error(
            self.get(
                "/api/v1/admin/orders/{0}/proof".format(order_id),
                token=token,
            ),
            401,
            "ADMIN_UNAUTHORIZED",
        )
        self.assert_error(
            self.get("/api/v1/admin/orders/{0}/proof".format(order_id)),
            401,
            "UNAUTHORIZED",
        )

        proofs = list((self.data_dir / "proofs").iterdir())
        self.assertEqual(len(proofs), 1)
        self.assertEqual(stat.S_IMODE(proofs[0].stat().st_mode), 0o600)
        self.assertEqual(
            stat.S_IMODE((self.data_dir / "proofs").stat().st_mode), 0o700
        )
        self.assertEqual(
            stat.S_IMODE(self.data_dir.stat().st_mode), 0o700
        )

    def test_proof_can_be_replaced_while_pending(self) -> None:
        token = self.guest()["token"]
        order_id = self.create_order(token)["order"]["orderId"]
        self.assert_ok(self.submit_proof(token, order_id))
        replacement = TINY_PNG + b"\x00"
        response = self.submit_proof(
            token,
            order_id,
            data=replacement,
            filename="replacement.png",
        )
        updated = self.assert_ok(response)["order"]
        self.assertEqual(updated["status"], "PENDING_VERIFICATION")
        admin = self.cached_admin_token()
        listing = self.assert_ok(
            self.get("/api/v1/admin/orders", token=admin)
        )["orders"]
        self.assertEqual(len(listing), 1)
        proof = listing[0]["proof"]
        self.assertEqual(proof["sizeBytes"], len(replacement))
        self.assertEqual(
            proof["sha256"], hashlib.sha256(replacement).hexdigest()
        )
        self.assertEqual(proof["originalFilename"], "replacement.png")

    def test_proof_rejected_after_decision(self) -> None:
        token = self.guest()["token"]
        order = self.verified_order(token)
        self.approve_order(order["orderId"])
        self.assert_error(
            self.submit_proof(token, order["orderId"]),
            409,
            "ORDER_NOT_OPEN_FOR_PROOF",
        )

    def test_admin_login_failure_and_success(self) -> None:
        wrong_password = self.post(
            "/api/v1/admin/login",
            {"username": ADMIN_USERNAME, "password": "wrong-password"},
        )
        self.assert_error(wrong_password, 401, "INVALID_CREDENTIALS")
        wrong_user = self.post(
            "/api/v1/admin/login",
            {"username": "intruder", "password": ADMIN_PASSWORD},
        )
        self.assert_error(wrong_user, 401, "INVALID_CREDENTIALS")
        success = self.post(
            "/api/v1/admin/login",
            {"username": ADMIN_USERNAME, "password": ADMIN_PASSWORD},
        )
        payload = self.assert_ok(success)
        self.assertTrue(payload["token"])
        self.assertTrue(payload["expiresUtc"].endswith("Z"))
        self.assertNotIn("password", json.dumps(payload).lower())

    def test_admin_login_rate_limit_after_failed_attempts(self) -> None:
        for _ in range(5):
            response = self.post(
                "/api/v1/admin/login",
                {"username": ADMIN_USERNAME, "password": "incorrect"},
            )
            self.assert_error(response, 401, "INVALID_CREDENTIALS")
        blocked = self.post(
            "/api/v1/admin/login",
            {"username": ADMIN_USERNAME, "password": ADMIN_PASSWORD},
        )
        self.assert_error(blocked, 429, "RATE_LIMITED")

    def test_admin_password_hash_verifier(self) -> None:
        encoded = hash_password("hashed-admin-password", 260_000)
        self.assertTrue(encoded.startswith("pbkdf2_sha256$"))
        self.assertNotIn("hashed-admin-password", encoded)
        alternate = pathlib.Path(
            tempfile.mkdtemp(prefix="nv-premium-hash-")
        )
        server = PremiumServer(
            PremiumConfig(
                host="127.0.0.1",
                port=0,
                data_dir=alternate,
                admin_username=ADMIN_USERNAME,
                admin_password_hash=encoded,
                max_proof_bytes=4096,
            )
        )
        server.start()
        try:
            url = "http://127.0.0.1:{0}/api/v1/admin/login".format(server.port)
            body = json.dumps(
                {
                    "username": ADMIN_USERNAME,
                    "password": "hashed-admin-password",
                }
            ).encode("utf-8")
            request = urllib.request.Request(
                url,
                data=body,
                headers={"Content-Type": "application/json"},
                method="POST",
            )
            with urllib.request.urlopen(request, timeout=10) as response:
                payload = json.loads(response.read().decode("utf-8"))
            self.assertTrue(payload["ok"])
            self.assertTrue(payload["token"])
        finally:
            server.stop()
            import shutil

            shutil.rmtree(str(alternate), ignore_errors=True)

    def test_approve_credits_exactly_once_and_repeat_is_conflict(self) -> None:
        token = self.guest()["token"]
        order = self.verified_order(token, "gems_200")
        order_id = order["orderId"]
        approved = self.approve_order(order_id, "Bank transfer confirmed")
        self.assertEqual(approved["order"]["status"], "APPROVED")
        self.assertEqual(approved["gems"], 210)
        self.assertEqual(approved["order"]["adminNote"], "Bank transfer confirmed")
        wallet = self.assert_ok(self.get("/api/v1/wallet", token=token))
        self.assertEqual(wallet["gems"], 210)
        self.assertEqual(wallet["pendingOrders"], 0)

        repeated = self.post(
            "/api/v1/admin/orders/{0}/decision".format(order_id),
            {"decision": "approve", "note": "again"},
            token=self.cached_admin_token(),
        )
        self.assert_error(repeated, 409, "ORDER_ALREADY_DECIDED")
        wallet_after = self.assert_ok(
            self.get("/api/v1/wallet", token=token)
        )
        self.assertEqual(wallet_after["gems"], 210)

        credit_rows = [
            entry
            for entry in self.assert_ok(
                self.get("/api/v1/admin/ledger", token=self.cached_admin_token())
            )["entries"]
            if entry["kind"] == "credit"
        ]
        self.assertEqual(len(credit_rows), 1)
        self.assertEqual(credit_rows[0]["deltaGems"], 210)
        self.assertEqual(credit_rows[0]["balanceAfter"], 210)

    def test_reject_flow_does_not_credit_and_blocks_redecision(self) -> None:
        token = self.guest()["token"]
        order = self.verified_order(token)
        admin = self.cached_admin_token()
        rejected = self.post(
            "/api/v1/admin/orders/{0}/decision".format(order["orderId"]),
            {"decision": "reject", "note": "Transfer was not received"},
            token=admin,
        )
        payload = self.assert_ok(rejected)
        self.assertEqual(payload["order"]["status"], "REJECTED")
        self.assertEqual(payload["gems"], 0)
        self.assertEqual(
            payload["order"]["adminNote"], "Transfer was not received"
        )
        wallet = self.assert_ok(self.get("/api/v1/wallet", token=token))
        self.assertEqual(wallet["gems"], 0)
        self.assertEqual(wallet["pendingOrders"], 0)
        repeated = self.post(
            "/api/v1/admin/orders/{0}/decision".format(order["orderId"]),
            {"decision": "reject", "note": "again"},
            token=admin,
        )
        self.assert_error(repeated, 409, "ORDER_ALREADY_DECIDED")
        ledger = self.assert_ok(
            self.get("/api/v1/admin/ledger", token=admin)
        )["entries"]
        self.assertEqual(ledger, [])

    def test_decision_requires_pending_verification(self) -> None:
        token = self.guest()["token"]
        order = self.create_order(token)["order"]
        response = self.post(
            "/api/v1/admin/orders/{0}/decision".format(order["orderId"]),
            {"decision": "approve"},
            token=self.cached_admin_token(),
        )
        self.assert_error(response, 409, "ORDER_NOT_AWAITING_VERIFICATION")

    def test_admin_input_validation_and_missing_proof(self) -> None:
        token = self.guest()["token"]
        order = self.create_order(token)["order"]
        admin = self.cached_admin_token()
        bad_status = self.get(
            "/api/v1/admin/orders?status=MAYBE", token=admin
        )
        self.assert_error(bad_status, 400, "INVALID_STATUS")
        bad_decision = self.post(
            "/api/v1/admin/orders/{0}/decision".format(order["orderId"]),
            {"decision": "maybe"},
            token=admin,
        )
        self.assert_error(bad_decision, 400, "INVALID_DECISION")
        long_note = self.post(
            "/api/v1/admin/orders/{0}/decision".format(order["orderId"]),
            {"decision": "approve", "note": "x" * 501},
            token=admin,
        )
        self.assert_error(long_note, 400, "NOTE_TOO_LONG")
        missing_proof = self.get(
            "/api/v1/admin/orders/{0}/proof".format(order["orderId"]),
            token=admin,
        )
        self.assert_error(missing_proof, 404, "PROOF_NOT_FOUND")
        bad_limit = self.get("/api/v1/admin/ledger?limit=0", token=admin)
        self.assert_error(bad_limit, 400, "INVALID_LIMIT")

    def test_unverified_order_fetch_and_proof_are_account_scoped(self) -> None:
        first = self.guest("FIRST")["token"]
        second = self.guest("SECOND")["token"]
        order = self.create_order(first)["order"]
        order_id = order["orderId"]
        self.assert_ok(self.submit_proof(first, order_id))

        self.assert_error(
            self.get("/api/v1/orders/{0}".format(order_id), token=second),
            404,
            "ORDER_NOT_FOUND",
        )
        self.assert_error(
            self.submit_proof(second, order_id),
            404,
            "ORDER_NOT_FOUND",
        )
        listed_second = self.assert_ok(
            self.get("/api/v1/orders", token=second)
        )
        self.assertEqual(listed_second["orders"], [])
        listed_first = self.assert_ok(
            self.get("/api/v1/orders", token=first)
        )
        self.assertEqual(len(listed_first["orders"]), 1)


# ---- tests: wallet spending, upgrades, revive and reporting -------------------------


class PurchaseAndReviveTests(PremiumApiTestBase):
    def test_purchase_insufficient_then_credit_then_duplicate_idempotent(self) -> None:
        token = self.guest()["token"]
        key = "buy-" + secrets.token_hex(8)
        insufficient = self.post(
            "/api/v1/purchases",
            {"productId": "skin_phantom_void", "clientRequestId": key},
            token=token,
        )
        problem = self.assert_error(
            insufficient, 402, "INSUFFICIENT_GEMS"
        )
        self.assertEqual(problem["error"], "INSUFFICIENT_GEMS")
        self.assertEqual(problem["gems"], 0)

        self.assertEqual(self.grant_gems(token, "gems_200")["gems"], 210)
        purchased = self.post(
            "/api/v1/purchases",
            {"productId": "skin_phantom_void", "clientRequestId": key},
            token=token,
        )
        payload = self.assert_ok(purchased)
        self.assertEqual(payload["gems"], 90)
        self.assertEqual(payload["productId"], "skin_phantom_void")
        self.assertTrue(payload["owned"])
        self.assertEqual(payload["level"], 0)

        repeated = self.post(
            "/api/v1/purchases",
            {"productId": "skin_phantom_void", "clientRequestId": key},
            token=token,
        )
        repeated_payload = self.assert_ok(repeated)
        self.assertEqual(repeated_payload["gems"], 90)
        wallet = self.assert_ok(self.get("/api/v1/wallet", token=token))
        self.assertEqual(wallet["gems"], 90)

        duplicate = self.post(
            "/api/v1/purchases",
            {
                "productId": "skin_phantom_void",
                "clientRequestId": "buy-" + secrets.token_hex(8),
            },
            token=token,
        )
        self.assert_error(duplicate, 409, "ALREADY_OWNED")
        self.assertEqual(
            self.assert_ok(self.get("/api/v1/wallet", token=token))["gems"], 90
        )

        ownership = self.assert_ok(
            self.get("/api/v1/ownership", token=token)
        )
        self.assertEqual(len(ownership["items"]), 1)
        item = ownership["items"][0]
        self.assertEqual(item["productId"], "skin_phantom_void")
        self.assertEqual(item["category"], "skin")
        self.assertEqual(item["level"], 0)
        self.assertTrue(item["acquiredUtc"].endswith("Z"))
        self.assertEqual(ownership["characters"], [])

    def test_character_purchase_populates_character_entitlements(self) -> None:
        token = self.guest()["token"]
        self.grant_gems(token, "gems_500")
        response = self.post(
            "/api/v1/purchases",
            {
                "productId": "char_phantom_x",
                "clientRequestId": "char-" + secrets.token_hex(8),
            },
            token=token,
        )
        payload = self.assert_ok(response)
        self.assertEqual(payload["gems"], 100)
        ownership = self.assert_ok(
            self.get("/api/v1/ownership", token=token)
        )
        self.assertEqual(ownership["characters"], ["phantom"])
        self.assertEqual(ownership["items"][0]["category"], "character")

    def test_upgrade_sequential_levels_and_maximum(self) -> None:
        token = self.guest()["token"]
        self.grant_gems(token, "gems_500")
        first = self.assert_ok(
            self.post(
                "/api/v1/purchases",
                {
                    "productId": "upgrade_revive_stabilizer",
                    "clientRequestId": "up1-" + secrets.token_hex(8),
                },
                token=token,
            )
        )
        self.assertEqual((first["level"], first["gems"]), (1, 430))
        second = self.assert_ok(
            self.post(
                "/api/v1/purchases",
                {
                    "productId": "upgrade_revive_stabilizer",
                    "clientRequestId": "up2-" + secrets.token_hex(8),
                },
                token=token,
            )
        )
        self.assertEqual((second["level"], second["gems"]), (2, 310))
        third_key = "up3-" + secrets.token_hex(8)
        third = self.assert_ok(
            self.post(
                "/api/v1/purchases",
                {
                    "productId": "upgrade_revive_stabilizer",
                    "clientRequestId": third_key,
                },
                token=token,
            )
        )
        self.assertEqual((third["level"], third["gems"]), (3, 190))

        repeated = self.assert_ok(
            self.post(
                "/api/v1/purchases",
                {
                    "productId": "upgrade_revive_stabilizer",
                    "clientRequestId": third_key,
                },
                token=token,
            )
        )
        self.assertEqual((repeated["level"], repeated["gems"]), (3, 190))
        beyond = self.post(
            "/api/v1/purchases",
            {
                "productId": "upgrade_revive_stabilizer",
                "clientRequestId": "up4-" + secrets.token_hex(8),
            },
            token=token,
        )
        maximum = self.assert_error(beyond, 409, "MAX_LEVEL_REACHED")
        self.assertEqual(maximum["level"], 3)
        ownership = self.assert_ok(
            self.get("/api/v1/ownership", token=token)
        )
        self.assertEqual(ownership["items"][0]["level"], 3)
        wallet = self.assert_ok(self.get("/api/v1/wallet", token=token))
        self.assertEqual(wallet["gems"], 190)

    def test_revive_debits_once_and_repeat_is_idempotent(self) -> None:
        token = self.guest()["token"]
        key = "revive-" + secrets.token_hex(8)
        payload = {
            "sessionId": "session-" + secrets.token_hex(8),
            "distance": 123.4,
            "locationIndex": 0,
            "clientRequestId": key,
        }
        no_gems = self.post("/api/v1/revive", payload, token=token)
        self.assert_error(no_gems, 402, "INSUFFICIENT_GEMS")
        self.grant_gems(token, "gems_80")
        first = self.assert_ok(self.post("/api/v1/revive", payload, token=token))
        self.assertEqual(first["gems"], 40)
        self.assertEqual(first["costGems"], 40)
        self.assertTrue(first["reviveToken"].startswith("rv_"))
        second = self.assert_ok(self.post("/api/v1/revive", payload, token=token))
        self.assertEqual(second["gems"], 40)
        self.assertEqual(second["reviveToken"], first["reviveToken"])
        wallet = self.assert_ok(self.get("/api/v1/wallet", token=token))
        self.assertEqual(wallet["gems"], 40)

        with self.server.store.connection() as connection:
            count = connection.execute(
                "SELECT COUNT(*) AS total FROM revive_events"
            ).fetchone()
        self.assertEqual(int(count["total"]), 1)
        ledger = self.assert_ok(
            self.get("/api/v1/admin/ledger", token=self.cached_admin_token())
        )["entries"]
        revive_entries = [
            entry for entry in ledger if entry["kind"] == "revive"
        ]
        self.assertEqual(len(revive_entries), 1)
        self.assertEqual(revive_entries[0]["deltaGems"], -40)
        self.assertEqual(revive_entries[0]["balanceAfter"], 40)

    def test_input_validation_for_spending_endpoints(self) -> None:
        token = self.guest()["token"]
        unknown_product = self.post(
            "/api/v1/purchases",
            {
                "productId": "does_not_exist",
                "clientRequestId": "buy-" + secrets.token_hex(8),
            },
            token=token,
        )
        self.assert_error(unknown_product, 404, "PRODUCT_NOT_FOUND")
        unknown_package = self.post(
            "/api/v1/orders",
            {
                "packageId": "gems_9999",
                "clientRequestId": "order-" + secrets.token_hex(8),
            },
            token=token,
        )
        self.assert_error(unknown_package, 404, "PACKAGE_NOT_FOUND")
        empty_key = self.post(
            "/api/v1/orders",
            {"packageId": "gems_80", "clientRequestId": ""},
            token=token,
        )
        self.assert_error(empty_key, 400, "INVALID_CLIENT_REQUEST_ID")
        long_key = self.post(
            "/api/v1/orders",
            {"packageId": "gems_80", "clientRequestId": "k" * 129},
            token=token,
        )
        self.assert_error(long_key, 400, "INVALID_CLIENT_REQUEST_ID")
        bad_session = self.post(
            "/api/v1/revive",
            {
                "sessionId": "session with spaces",
                "distance": 12.0,
                "locationIndex": 0,
                "clientRequestId": "revive-" + secrets.token_hex(8),
            },
            token=token,
        )
        self.assert_error(bad_session, 400, "INVALID_SESSION")
        boolean_distance = self.post(
            "/api/v1/revive",
            {
                "sessionId": "session-1",
                "distance": True,
                "locationIndex": 0,
                "clientRequestId": "revive-" + secrets.token_hex(8),
            },
            token=token,
        )
        self.assert_error(boolean_distance, 400, "INVALID_DISTANCE")
        negative_distance = self.post(
            "/api/v1/revive",
            {
                "sessionId": "session-1",
                "distance": -1.0,
                "locationIndex": 0,
                "clientRequestId": "revive-" + secrets.token_hex(8),
            },
            token=token,
        )
        self.assert_error(negative_distance, 400, "INVALID_DISTANCE")

    def test_ledger_order_and_stats_reflect_credits_and_debits(self) -> None:
        token = self.guest("LEDGER USER")["token"]
        self.grant_gems(token, "gems_200")
        self.assert_ok(
            self.post(
                "/api/v1/purchases",
                {
                    "productId": "skin_phantom_void",
                    "clientRequestId": "buy-" + secrets.token_hex(8),
                },
                token=token,
            )
        )
        self.assert_ok(
            self.post(
                "/api/v1/revive",
                {
                    "sessionId": "ledger-session",
                    "distance": 42.5,
                    "locationIndex": 2,
                    "clientRequestId": "revive-" + secrets.token_hex(8),
                },
                token=token,
            )
        )
        admin = self.cached_admin_token()
        ledger = self.assert_ok(
            self.get("/api/v1/admin/ledger?limit=100", token=admin)
        )["entries"]
        self.assertEqual([entry["kind"] for entry in ledger], ["revive", "purchase", "credit"])
        self.assertEqual([entry["deltaGems"] for entry in ledger], [-40, -120, 210])
        self.assertEqual([entry["balanceAfter"] for entry in ledger], [50, 90, 210])
        self.assertIsNone(ledger[0]["orderId"])
        self.assertIsNone(ledger[0]["productId"])
        self.assertEqual(ledger[1]["productId"], "skin_phantom_void")
        self.assertFalse(ledger[0]["createdUtc"] > ledger[1]["createdUtc"])
        wallet = self.assert_ok(self.get("/api/v1/wallet", token=token))
        self.assertEqual(wallet["gems"], 50)
        self.assertEqual(wallet["pendingOrders"], 0)

        stats = self.assert_ok(self.get("/api/v1/admin/stats", token=admin))
        self.assertEqual(stats["counts"]["APPROVED"], 1)
        self.assertEqual(stats["counts"]["PENDING_VERIFICATION"], 0)
        self.assertEqual(stats["pendingCount"], 0)
        self.assertEqual(stats["totalCredits"], 210)
        self.assertEqual(stats["creditEntries"], 1)
        self.assertEqual(stats["accountCount"], 1)
        for status in ORDER_STATUSES:
            self.assertIn(status, stats["counts"])

        orders = self.assert_ok(self.get("/api/v1/orders", token=token))["orders"]
        self.assertEqual(len(orders), 1)
        self.assertEqual(orders[0]["status"], "APPROVED")
        filtered = self.assert_ok(
            self.get(
                "/api/v1/admin/orders?status=APPROVED", token=admin
            )
        )["orders"]
        self.assertEqual(len(filtered), 1)


def listing_body_bytes(payload: Dict[str, Any]) -> bytes:
    """Serialise the admin listing exactly as it came over HTTP for leak checks."""

    return json.dumps(payload, separators=(",", ":")).encode("utf-8")


if __name__ == "__main__":
    unittest.main()


class AccountHttpTests(PremiumApiTestBase):
    """End-to-end account, OTP, sync and transaction routes over real HTTP."""

    def register(self, email: str = "runner@example.com") -> Dict[str, Any]:
        response = self.post(
            "/api/v1/auth/register",
            {"email": email, "password": "GridRunner42", "playerName": "RUNNER-01"},
        )
        payload = self.assert_ok(response)
        self.assertTrue(payload["token"])
        return payload

    def test_register_verify_login_profile_and_logout(self) -> None:
        session = self.register()
        account_id = session["accountId"]
        token = session["token"]
        self.assertFalse(session["emailVerified"])
        self.assertEqual(session["email"], "runner@example.com")
        self.assertEqual(session["delivery"]["reason"], "SMTP_NOT_CONFIGURED")
        self.assertTrue(session["delivery"]["devVerificationToken"])

        profile = self.assert_ok(
            self.get("/api/v1/account/profile", token=token)
        )
        self.assertEqual(profile["accountId"], account_id)
        self.assertEqual(profile["receiptEmail"], "runner@example.com")
        self.assertEqual(profile["syncRevision"], 0)

        verified = self.assert_ok(
            self.post(
                "/api/v1/auth/email/verify",
                {"token": session["delivery"]["devVerificationToken"]},
            )
        )
        self.assertTrue(verified["emailVerified"])

        login = self.assert_ok(
            self.post(
                "/api/v1/auth/login",
                {"email": "RUNNER@example.com", "password": "GridRunner42"},
            )
        )
        self.assertEqual(login["accountId"], account_id)
        self.assertTrue(login["emailVerified"])

        self.assert_error(
            self.post(
                "/api/v1/auth/login",
                {"email": "runner@example.com", "password": "WrongPass99"},
            ),
            401,
            "INVALID_CREDENTIALS",
        )
        self.assert_error(
            self.post(
                "/api/v1/auth/register",
                {"email": "runner@example.com", "password": "GridRunner42"},
            ),
            409,
            "EMAIL_IN_USE",
        )

        logout_token = login["token"]
        self.assert_ok(self.post("/api/v1/auth/logout", {}, token=logout_token))
        self.assert_error(
            self.get("/api/v1/account/profile", token=logout_token),
            401,
            "UNAUTHORIZED",
        )
        self.assert_ok(self.get("/api/v1/account/profile", token=token))

    def test_password_reset_rotation_and_phone_otp(self) -> None:
        session = self.register("reset@example.com")
        request = self.assert_ok(
            self.post(
                "/api/v1/auth/password/request", {"email": "reset@example.com"}
            )
        )
        self.assertEqual(request["delivery"]["channel"], "email")
        reset_token = request["delivery"]["devResetToken"]
        self.assert_ok(
            self.post(
                "/api/v1/auth/password/reset",
                {"token": reset_token, "password": "NewGridPass99"},
            )
        )
        self.assert_error(
            self.get("/api/v1/account/profile", token=session["token"]),
            401,
            "UNAUTHORIZED",
        )
        self.assert_ok(
            self.post(
                "/api/v1/auth/login",
                {"email": "reset@example.com", "password": "NewGridPass99"},
            )
        )
        missing = self.assert_ok(
            self.post(
                "/api/v1/auth/password/request", {"email": "nobody@example.com"}
            )
        )
        self.assertEqual(missing["delivery"]["reason"], "ACCOUNT_NOT_FOUND")

        start = self.assert_ok(
            self.post("/api/v1/auth/phone/start", {"phone": "+94 77 123 4567"})
        )
        self.assertEqual(start["phone"], "+94771234567")
        self.assertTrue(start["delivery"]["devCode"])
        phone_session = self.assert_ok(
            self.post(
                "/api/v1/auth/phone/verify",
                {
                    "challengeId": start["challengeId"],
                    "code": start["delivery"]["devCode"],
                    "playerName": "PHONE RUNNER",
                },
            )
        )
        self.assertEqual(phone_session["phone"], "+94771234567")
        self.assertTrue(phone_session["phoneVerified"])
        self.assert_error(
            self.post(
                "/api/v1/auth/phone/verify",
                {"challengeId": start["challengeId"], "code": start["delivery"]["devCode"]},
            ),
            400,
            "INVALID_OTP",
        )

    def test_oauth_is_an_explicit_boundary(self) -> None:
        self.assert_error(
            self.post(
                "/api/v1/auth/oauth",
                {"provider": "apple", "idToken": "x" * 64},
            ),
            503,
            "PROVIDER_NOT_CONFIGURED",
        )
        self.assert_error(
            self.post(
                "/api/v1/auth/oauth",
                {"provider": "google", "idToken": "y" * 64},
            ),
            503,
            "PROVIDER_NOT_CONFIGURED",
        )

    def test_sync_ignores_gems_and_is_idempotent(self) -> None:
        session = self.register("sync@example.com")
        token = session["token"]
        snapshot = {
            "schema": 1,
            "totalXp": 1800,
            "credits": 900,
            "bestDistance": 5400,
            "premium": {"gems": 999999, "token": "stolen"},
            "discoveredLocations": ["skyway"],
            "missions": [{"id": "m1", "progress": 2, "rewardCredits": 100}],
        }
        merged = self.assert_ok(
            self.post(
                "/api/v1/account/sync",
                {"snapshot": snapshot, "baseRevision": 0, "clientRequestId": "sync-1"},
                token=token,
            )
        )
        self.assertEqual(merged["revision"], 1)
        self.assertEqual(merged["gems"], 0)
        self.assertNotIn("premium", merged["snapshot"])
        self.assertEqual(merged["snapshot"]["totalXp"], 1800)

        replay = self.assert_ok(
            self.post(
                "/api/v1/account/sync",
                {"snapshot": snapshot, "baseRevision": 0, "clientRequestId": "sync-1"},
                token=token,
            )
        )
        self.assertEqual(replay["revision"], 1)

        self.assert_error(
            self.post(
                "/api/v1/account/sync",
                {"snapshot": {"schema": 1}, "baseRevision": 9, "clientRequestId": "sync-2"},
                token=token,
            ),
            409,
            "SYNC_REVISION_AHEAD",
        )
        fetched = self.assert_ok(self.get("/api/v1/account/sync", token=token))
        self.assertEqual(fetched["revision"], 1)

    def test_payment_capabilities_and_unconfigured_checkout(self) -> None:
        capabilities = self.assert_ok(self.get("/api/v1/store/payments"))
        payments = capabilities["payments"]
        self.assertFalse(payments["stripe"])
        self.assertFalse(payments["apple"])
        self.assertFalse(payments["googlePlay"])
        self.assertTrue(payments["manualBankTransfer"])
        self.assertEqual(payments["appleProductMap"], {})
        self.assertEqual(payments["googlePlayProductMap"]["gems_80"], "gems_80")
        self.assertEqual(payments["stripeCurrency"], "USD")
        session = self.register("checkout@example.com")
        self.assert_error(
            self.post(
                "/api/v1/payments/checkout",
                {
                    "packageId": "gems_80",
                    "clientRequestId": "checkout-1",
                    "receiptEmail": "checkout@example.com",
                },
                token=session["token"],
            ),
            503,
            "PROVIDER_NOT_CONFIGURED",
        )

    def test_stripe_webhook_credits_once_and_refund_reverses(self) -> None:
        session = self.register("buyer@example.com")
        token = session["token"]
        account_id = session["accountId"]
        self.assert_ok(
            self.post(
                "/api/v1/auth/email/verify",
                {"token": session["delivery"]["devVerificationToken"]},
            )
        )

        event = {
            "id": "evt_test_001",
            "type": "checkout.session.completed",
            "data": {
                "object": {
                    "id": "cs_test_001",
                    "payment_intent": "pi_test_001",
                    "payment_status": "paid",
                    "amount_total": 490,
                    "currency": "lkr",
                    "customer_email": "buyer@example.com",
                    "metadata": {
                        "accountId": account_id,
                        "packageId": "gems_80",
                        "receiptEmail": "buyer@example.com",
                        "gems": "80",
                    },
                }
            },
        }
        result = self.assert_ok(self.stripe_event(event))
        self.assertTrue(result["processed"])
        self.assertEqual(result["credited"], 80)
        self.assertEqual(result["gems"], 80)
        self.assertFalse(result["receiptEmailSent"])
        self.assertEqual(result["receiptEmailReason"], "SMTP_NOT_CONFIGURED")

        wallet = self.assert_ok(self.get("/api/v1/wallet", token=token))
        self.assertEqual(wallet["gems"], 80)

        duplicate = self.assert_ok(self.stripe_event(event))
        self.assertFalse(duplicate["processed"])
        self.assertEqual(duplicate["reason"], "DUPLICATE_EVENT")
        wallet = self.assert_ok(self.get("/api/v1/wallet", token=token))
        self.assertEqual(wallet["gems"], 80)

        history = self.assert_ok(
            self.get("/api/v1/account/transactions", token=token)
        )
        self.assertEqual(len(history["transactions"]), 1)
        self.assertEqual(history["transactions"][0]["status"], "SUCCEEDED")
        self.assertEqual(history["transactions"][0]["provider"], "stripe")
        self.assertEqual(history["transactions"][0]["providerRef"], "pi_test_001")

        refund = {
            "id": "evt_refund_001",
            "type": "charge.refunded",
            "data": {
                "object": {
                    "id": "ch_test_001",
                    "payment_intent": "pi_test_001",
                    "amount_refunded": 490,
                }
            },
        }
        reversed_result = self.assert_ok(self.stripe_event(refund))
        self.assertTrue(reversed_result["processed"])
        wallet = self.assert_ok(self.get("/api/v1/wallet", token=token))
        self.assertEqual(wallet["gems"], 0)
        history = self.assert_ok(
            self.get("/api/v1/account/transactions", token=token)
        )
        self.assertEqual(history["transactions"][0]["status"], "REFUNDED")

    def test_invalid_webhook_signature_is_rejected(self) -> None:
        body = json.dumps({"id": "evt_bad", "type": "checkout.session.completed"}).encode("utf-8")
        response = self.request(
            "POST",
            "/api/v1/payments/stripe/webhook",
            body=body,
            content_type="application/json",
            extra_headers={"Stripe-Signature": "t={0},v1=deadbeef".format(int(time.time()))},
        )
        self.assert_error(response, 400, "INVALID_SIGNATURE")

    def stripe_event(self, event: Dict[str, Any]) -> ApiResponse:
        body = json.dumps(event, separators=(",", ":")).encode("utf-8")
        timestamp = int(time.time())
        signed = str(timestamp).encode("ascii") + b"." + body
        signature = hmac.new(
            b"whsec_test_secret", signed, hashlib.sha256
        ).hexdigest()
        return self.request(
            "POST",
            "/api/v1/payments/stripe/webhook",
            body=body,
            content_type="application/json",
            extra_headers={
                "Stripe-Signature": "t={0},v1={1}".format(timestamp, signature)
            },
        )


class AdminOperationsTests(PremiumApiTestBase):
    """Operator review, catalogue control and refund reversal."""

    def stripe_event(self, event: Dict[str, Any]) -> ApiResponse:
        body = json.dumps(event, separators=(",", ":")).encode("utf-8")
        timestamp = int(time.time())
        signed = str(timestamp).encode("ascii") + b"." + body
        signature = hmac.new(
            b"whsec_test_secret", signed, hashlib.sha256
        ).hexdigest()
        return self.request(
            "POST",
            "/api/v1/payments/stripe/webhook",
            body=body,
            content_type="application/json",
            extra_headers={
                "Stripe-Signature": "t={0},v1={1}".format(timestamp, signature)
            },
        )

    def test_admin_transactions_accounts_catalog_and_refund(self) -> None:
        session = self.register("admin-review@example.com")
        token = session["token"]
        account_id = session["accountId"]
        self.assert_ok(
            self.post(
                "/api/v1/auth/email/verify",
                {"token": session["delivery"]["devVerificationToken"]},
            )
        )
        event = {
            "id": "evt_admin_review_001",
            "type": "checkout.session.completed",
            "data": {
                "object": {
                    "id": "cs_admin_review_001",
                    "payment_intent": "pi_admin_review_001",
                    "payment_status": "paid",
                    "amount_total": 490,
                    "currency": "lkr",
                    "metadata": {
                        "accountId": account_id,
                        "packageId": "gems_80",
                        "receiptEmail": "admin-review@example.com",
                    },
                }
            },
        }
        self.assertTrue(self.assert_ok(self.stripe_event(event))["processed"])

        self.assert_error(
            self.get("/api/v1/admin/transactions"), 401, "UNAUTHORIZED"
        )
        admin = self.cached_admin_token()

        listing = self.assert_ok(
            self.get("/api/v1/admin/transactions", token=admin)
        )
        self.assertEqual(listing["count"], 1)
        transaction = listing["transactions"][0]
        self.assertEqual(transaction["provider"], "stripe")
        self.assertEqual(transaction["status"], "SUCCEEDED")
        self.assertEqual(transaction["accountEmail"], "admin-review@example.com")
        self.assertEqual(transaction["gems"], 80)

        filtered = self.assert_ok(
            self.get(
                "/api/v1/admin/transactions?provider=stripe&status=SUCCEEDED",
                token=admin,
            )
        )
        self.assertEqual(filtered["count"], 1)
        self.assert_error(
            self.get("/api/v1/admin/transactions?limit=0", token=admin),
            400,
            "INVALID_LIMIT",
        )

        accounts = self.assert_ok(
            self.get(
                "/api/v1/admin/accounts?q="
                + urllib.parse.quote("admin-review@example.com"),
                token=admin,
            )
        )
        self.assertEqual(accounts["count"], 1)
        self.assertEqual(accounts["accounts"][0]["accountId"], account_id)
        detail = self.assert_ok(
            self.get(
                "/api/v1/admin/accounts/" + account_id, token=admin
            )
        )
        self.assertEqual(detail["account"]["accountId"], account_id)
        self.assertEqual(len(detail["account"]["transactions"]), 1)
        self.assertTrue(detail["account"]["emailVerified"])

        catalog = self.assert_ok(self.get("/api/v1/admin/catalog", token=admin))
        starter = next(
            item for item in catalog["packages"] if item["id"] == "gems_80"
        )
        self.assertEqual(starter["name"], "STARTER CACHE")

        updated = self.assert_ok(
            self.post(
                "/api/v1/admin/catalog/packages/gems_80",
                {"name": "ADMIN STARTER", "price": 777, "enabled": True},
                token=admin,
            )
        )
        self.assertEqual(updated["item"]["name"], "ADMIN STARTER")
        self.assertEqual(updated["item"]["priceLkr"], 777)
        public_catalog = self.assert_ok(self.get("/api/v1/store/catalog"))
        public_starter = next(
            item for item in public_catalog["packages"] if item["id"] == "gems_80"
        )
        self.assertEqual(public_starter["name"], "ADMIN STARTER")
        self.assertEqual(public_starter["priceLkr"], 777)
        self.assertEqual(public_starter["gems"], 80)

        disabled = self.assert_ok(
            self.post(
                "/api/v1/admin/catalog/packages/gems_80",
                {"enabled": False},
                token=admin,
            )
        )
        self.assertFalse(disabled["item"]["enabled"])
        guest = self.guest("DISABLED PACKAGE")
        self.assert_error(
            self.post(
                "/api/v1/orders",
                {"packageId": "gems_80", "clientRequestId": "disabled-1"},
                token=guest["token"],
            ),
            409,
            "PACKAGE_DISABLED",
        )
        self.assert_ok(
            self.post(
                "/api/v1/admin/catalog/packages/gems_80",
                {"enabled": True},
                token=admin,
            )
        )

        product_update = self.assert_ok(
            self.post(
                "/api/v1/admin/catalog/products/skin_phantom_void",
                {"price": 999, "enabled": False},
                token=admin,
            )
        )
        self.assertEqual(product_update["item"]["priceGems"], 999)
        self.assertFalse(product_update["item"]["enabled"])
        self.assert_error(
            self.post(
                "/api/v1/admin/catalog/products/skin_phantom_void",
                {"price": True},
                token=admin,
            ),
            400,
            "INVALID_PRICE",
        )
        self.assert_error(
            self.post(
                "/api/v1/admin/catalog/products/missing_product",
                {"price": 10},
                token=admin,
            ),
            404,
            "CATALOG_ITEM_NOT_FOUND",
        )

        self.assert_error(
            self.post(
                "/api/v1/admin/transactions/{0}/refund".format(transaction["id"]),
                {"confirmed": False},
                token=admin,
            ),
            400,
            "REFUND_NOT_CONFIRMED",
        )
        refunded = self.assert_ok(
            self.post(
                "/api/v1/admin/transactions/{0}/refund".format(transaction["id"]),
                {"confirmed": True, "note": "provider refund verified"},
                token=admin,
            )
        )
        self.assertEqual(refunded["gems"], 0)
        self.assertEqual(refunded["transaction"]["status"], "REFUNDED")
        self.assertTrue(refunded["providerRefundRequired"])
        wallet = self.assert_ok(self.get("/api/v1/wallet", token=token))
        self.assertEqual(wallet["gems"], 0)
        history = self.assert_ok(
            self.get("/api/v1/account/transactions", token=token)
        )
        self.assertEqual(history["transactions"][0]["status"], "REFUNDED")
        self.assert_error(
            self.post(
                "/api/v1/admin/transactions/{0}/refund".format(transaction["id"]),
                {"confirmed": True},
                token=admin,
            ),
            409,
            "TRANSACTION_NOT_REFUNDABLE",
        )


class RecordingMailer:
    """Receipt sink that records exactly what the payment adapters pass."""

    def __init__(self) -> None:
        self.receipts: List[Dict[str, Any]] = []
        self.messages: List[Dict[str, str]] = []

    def send(self, to_email: str, subject: str, body: str) -> bool:
        self.messages.append(
            {"toEmail": to_email, "subject": subject, "body": body}
        )
        return True

    def send_receipt(
        self,
        to_email: str,
        order_id: str,
        package_name: str,
        gems: int,
        amount_display: str,
        provider: str,
        **kwargs: Any,
    ) -> bool:
        receipt = {
            "toEmail": to_email,
            "orderId": order_id,
            "packageName": package_name,
            "gems": gems,
            "amountDisplay": amount_display,
            "provider": provider,
        }
        receipt.update(kwargs)
        self.receipts.append(receipt)
        return True


class ReceiptAndPlatformPaymentTests(unittest.TestCase):
    """Verified provider purchases credit once and send a complete receipt."""

    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory(prefix="nv-platform-test-")
        self.data_dir = pathlib.Path(self.temp_dir.name)
        self.mailer = RecordingMailer()
        self.stripe_calls: List[Dict[str, Any]] = []
        self.apple_calls: List[Dict[str, Any]] = []

        def fake_stripe(url, form, headers, timeout=20.0):
            self.stripe_calls.append({"url": url, "form": form, "headers": headers})
            return 200, {
                "id": "cs_receipt_checkout",
                "url": "https://checkout.stripe.com/c/pay/cs_receipt_checkout",
            }

        def fake_apple(url, form, headers, timeout=20.0):
            self.apple_calls.append({"url": url, "form": form, "headers": headers})
            return 200, {
                "status": 0,
                "receipt": {"bundle_id": "com.neonvelocity.game"},
                "latest_receipt_info": [
                    {
                        "product_id": "com.neonvelocity.gems80",
                        "transaction_id": "apple_tx_001",
                        "purchase_date_ms": "1720000000000",
                        "price": "4.99",
                        "currency": "USD",
                    }
                ],
            }

        self.config = PremiumConfig(
            host="127.0.0.1",
            port=0,
            data_dir=self.data_dir,
            guest_rate_limit=1000,
            auth_rate_limit=1000,
            auth_dev_mode=True,
            allowed_origin="*",
            stripe_secret_key="sk_test_server_only",
            stripe_webhook_secret="whsec_test_secret",
            stripe_currency="usd",
            stripe_price_map={"gems_80": "price_test_80"},
            apple_shared_secret="apple-shared-secret",
            apple_product_map={"com.neonvelocity.gems80": "gems_80"},
            google_play_product_map={"com.neonvelocity.gems200": "gems_200"},
        )
        self.app = PremiumApplication(
            self.config,
            mailer=self.mailer,
            stripe_http_post=fake_stripe,
            apple_http_post=fake_apple,
            google_verifier=self.fake_google,
        )
        self.server = PremiumServer(self.config, self.app)
        self.server.start()
        self.base_url = "http://127.0.0.1:{0}".format(self.server.port)

    def tearDown(self) -> None:
        self.server.stop()
        self.temp_dir.cleanup()

    def fake_google(self, product_id: str, purchase_token: str) -> Dict[str, Any]:
        if purchase_token == "rejected":
            raise ValueError("rejected purchase")
        return {
            "packageId": "gems_200",
            "transactionId": "gpa_tx_001",
            "amountMinor": 990,
            "currency": "LKR",
            "purchaseState": 0,
        }

    def request(
        self,
        method: str,
        path: str,
        payload: Any = None,
        token: Optional[str] = None,
    ) -> Tuple[int, Dict[str, Any]]:
        headers = {"Accept": "application/json"}
        data = None
        if payload is not None:
            data = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"
        if token:
            headers["Authorization"] = "Bearer " + token
        request = urllib.request.Request(
            self.base_url + path, data=data, headers=headers, method=method
        )
        try:
            with urllib.request.urlopen(request, timeout=10) as response:
                return response.status, json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            return exc.code, json.loads(exc.read().decode("utf-8"))

    def register(self, email: str = "platform@example.com") -> Dict[str, Any]:
        status, payload = self.request(
            "POST",
            "/api/v1/auth/register",
            {
                "email": email,
                "password": "GridRunner42",
                "playerName": "PLATFORM RUNNER",
            },
        )
        self.assertEqual(status, 200, payload)
        return payload

    def stripe_event(self, event: Dict[str, Any]) -> Tuple[int, Dict[str, Any]]:
        body = json.dumps(event, separators=(",", ":")).encode("utf-8")
        timestamp = int(time.time())
        signed = str(timestamp).encode("ascii") + b"." + body
        signature = hmac.new(
            b"whsec_test_secret", signed, hashlib.sha256
        ).hexdigest()
        request = urllib.request.Request(
            self.base_url + "/api/v1/payments/stripe/webhook",
            data=body,
            headers={
                "Content-Type": "application/json",
                "Stripe-Signature": "t={0},v1={1}".format(timestamp, signature),
            },
            method="POST",
        )
        with urllib.request.urlopen(request, timeout=10) as response:
            return response.status, json.loads(response.read().decode("utf-8"))

    def test_stripe_receipt_contains_required_fields(self) -> None:
        session = self.register("receipt@example.com")
        status, result = self.stripe_event(
            {
                "id": "evt_receipt_001",
                "type": "checkout.session.completed",
                "data": {
                    "object": {
                        "id": "cs_receipt_001",
                        "payment_intent": "pi_receipt_001",
                        "payment_status": "paid",
                        "amount_total": 499,
                        "currency": "usd",
                        "metadata": {
                            "accountId": session["accountId"],
                            "packageId": "gems_80",
                            "receiptEmail": "billing@example.com",
                        },
                    }
                },
            }
        )
        self.assertEqual(status, 200, result)
        self.assertTrue(result["processed"])
        self.assertTrue(result["receiptEmailSent"])
        self.assertEqual(result["receiptEmailReason"], "")
        self.assertEqual(len(self.mailer.receipts), 1)
        receipt = self.mailer.receipts[0]
        self.assertEqual(receipt["toEmail"], "billing@example.com")
        self.assertEqual(receipt["packageName"], "STARTER CACHE")
        self.assertEqual(receipt["gems"], 80)
        self.assertEqual(receipt["amountDisplay"], "4.99 USD")
        self.assertEqual(receipt["provider"], "stripe")
        self.assertEqual(receipt["game_name"], "NEON VELOCITY")
        self.assertEqual(receipt["player_id"], session["accountId"])
        self.assertEqual(receipt["transaction_id"], "pi_receipt_001")
        self.assertEqual(receipt["status"], "SUCCEEDED")
        self.assertTrue(receipt["occurred_utc"])

    def test_apple_verified_purchase_credits_once_and_sends_receipt(self) -> None:
        session = self.register("apple@example.com")
        token = session["token"]
        payload = {
            "receiptBase64": base64.b64encode(b"apple-receipt").decode("ascii"),
            "productId": "com.neonvelocity.gems80",
            "receiptEmail": "apple-receipt@example.com",
        }
        status, result = self.request(
            "POST", "/api/v1/payments/apple", payload, token=token
        )
        self.assertEqual(status, 200, result)
        self.assertTrue(result["processed"])
        self.assertEqual(result["credited"], 80)
        self.assertEqual(result["gems"], 80)
        self.assertTrue(result["receiptEmailSent"])
        self.assertEqual(self.apple_calls[0]["form"]["password"], "apple-shared-secret")
        receipt = self.mailer.receipts[0]
        self.assertEqual(receipt["toEmail"], "apple-receipt@example.com")
        self.assertEqual(receipt["provider"], "apple")
        self.assertEqual(receipt["amountDisplay"], "4.99 USD")
        self.assertEqual(receipt["transaction_id"], "apple_tx_001")

        status, duplicate = self.request(
            "POST", "/api/v1/payments/apple", payload, token=token
        )
        self.assertEqual(status, 200, duplicate)
        self.assertFalse(duplicate["processed"])
        self.assertEqual(duplicate["reason"], "DUPLICATE_TRANSACTION")
        self.assertEqual(len(self.mailer.receipts), 1)
        status, wallet = self.request(
            "GET", "/api/v1/wallet", token=token
        )
        self.assertEqual(status, 200)
        self.assertEqual(wallet["gems"], 80)

        status, rejected = self.request(
            "POST",
            "/api/v1/payments/apple",
            {
                "receiptBase64": base64.b64encode(b"receipt").decode("ascii"),
                "productId": "com.neonvelocity.unknown",
            },
            token=token,
        )
        self.assertEqual(status, 400, rejected)
        self.assertEqual(rejected["code"], "INVALID_PRODUCT")

    def test_google_verified_purchase_credits_once_and_rejects_bad_token(self) -> None:
        session = self.register("google@example.com")
        token = session["token"]
        payload = {
            "purchaseToken": "google-token-001",
            "productId": "com.neonvelocity.gems200",
            "receiptEmail": "google-receipt@example.com",
        }
        status, result = self.request(
            "POST", "/api/v1/payments/google", payload, token=token
        )
        self.assertEqual(status, 200, result)
        self.assertTrue(result["processed"])
        self.assertEqual(result["credited"], 210)
        self.assertEqual(result["gems"], 210)
        self.assertEqual(result["transaction"]["amountMinor"], 990)
        self.assertEqual(result["transaction"]["currency"], "LKR")
        receipt = self.mailer.receipts[0]
        self.assertEqual(receipt["provider"], "google_play")
        self.assertEqual(receipt["amountDisplay"], "9.90 LKR")
        self.assertEqual(receipt["transaction_id"], "gpa_tx_001")

        status, duplicate = self.request(
            "POST", "/api/v1/payments/google", payload, token=token
        )
        self.assertEqual(status, 200, duplicate)
        self.assertFalse(duplicate["processed"])
        self.assertEqual(len(self.mailer.receipts), 1)

        status, rejected = self.request(
            "POST",
            "/api/v1/payments/google",
            {"purchaseToken": "rejected", "productId": "com.neonvelocity.gems200"},
            token=token,
        )
        self.assertEqual(status, 400, rejected)
        self.assertEqual(rejected["code"], "INVALID_RECEIPT")

    def test_platform_purchase_requires_account(self) -> None:
        status, result = self.request(
            "POST",
            "/api/v1/payments/google",
            {"purchaseToken": "anonymous", "productId": "com.neonvelocity.gems200"},
        )
        self.assertEqual(status, 401, result)
        self.assertEqual(result["code"], "UNAUTHORIZED")


class StripeCheckoutHttpTests(unittest.TestCase):
    """Checkout uses server-side prices and never trusts client amounts."""

    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory(prefix="nv-stripe-test-")
        self.data_dir = pathlib.Path(self.temp_dir.name)
        self.calls: List[Dict[str, Any]] = []
        self.config = PremiumConfig(
            host="127.0.0.1",
            port=0,
            data_dir=self.data_dir,
            guest_rate_limit=1000,
            auth_rate_limit=1000,
            auth_dev_mode=True,
            allowed_origin="*",
            stripe_secret_key="sk_test_server_only",
            stripe_webhook_secret="whsec_test_secret",
            stripe_currency="lkr",
            stripe_price_map={"gems_80": "price_test_80"},
            stripe_success_url="https://example.com/success",
            stripe_cancel_url="https://example.com/cancel",
        )

        def fake_stripe(url, form, headers, timeout=20.0):
            self.calls.append({"url": url, "form": form, "headers": headers})
            return 200, {
                "id": "cs_test_checkout",
                "url": "https://checkout.stripe.com/c/pay/cs_test_checkout",
            }

        self.app = PremiumApplication(
            self.config, stripe_http_post=fake_stripe, oauth_verifier=None
        )
        self.server = PremiumServer(self.config, self.app)
        self.server.start()
        self.base_url = "http://127.0.0.1:{0}".format(self.server.port)

    def tearDown(self) -> None:
        self.server.stop()
        self.temp_dir.cleanup()

    def request(self, method, path, payload=None, token=None):
        headers = {"Accept": "application/json"}
        data = None
        if payload is not None:
            data = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"
        if token:
            headers["Authorization"] = "Bearer " + token
        request = urllib.request.Request(
            self.base_url + path, data=data, headers=headers, method=method
        )
        with urllib.request.urlopen(request, timeout=10) as response:
            return json.loads(response.read().decode("utf-8"))

    def test_checkout_uses_server_price_and_returns_hosted_url(self) -> None:
        session = self.request(
            "POST",
            "/api/v1/auth/register",
            {
                "email": "card@example.com",
                "password": "GridRunner42",
                "playerName": "CARD RUNNER",
            },
        )
        token = session["token"]
        result = self.request(
            "POST",
            "/api/v1/payments/checkout",
            {
                "packageId": "gems_80",
                "clientRequestId": "checkout-http-1",
                "receiptEmail": "card@example.com",
                "successUrl": "https://example.com/success",
                "cancelUrl": "https://example.com/cancel",
            },
            token,
        )
        self.assertTrue(result["ok"])
        self.assertEqual(result["sessionId"], "cs_test_checkout")
        self.assertEqual(result["gems"], 80)
        self.assertEqual(len(self.calls), 1)
        call = self.calls[0]
        self.assertEqual(call["form"]["line_items[0][price]"], "price_test_80")
        self.assertEqual(call["form"]["metadata[accountId]"], session["accountId"])
        self.assertEqual(call["form"]["metadata[gems]"], "80")
        self.assertNotIn("amount", call["form"])
        capabilities = self.request("GET", "/api/v1/store/payments")
        self.assertTrue(capabilities["payments"]["stripe"])
