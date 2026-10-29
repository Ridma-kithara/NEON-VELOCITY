#!/usr/bin/env python3
"""Operator CLI for the NEON VELOCITY premium service.

Credentials come only from the environment or a getpass prompt. A password is never accepted
as a command-line argument, which keeps it out of shell history and process listings.

Environment:
    NV_PREMIUM_URL       Base URL, for example https://premium.example.com
    NV_ADMIN_USERNAME    Admin account name
    NV_ADMIN_PASSWORD    Optional; otherwise the CLI prompts with getpass
    NV_CA_BUNDLE         Optional CA file for private HTTPS deployments
    NV_ALLOW_INSECURE_HTTP  Set to 1 only for deliberate remote plain-HTTP testing

Examples:
    python3 Backend/premium_admin.py pending
    python3 Backend/premium_admin.py proof NV-20260101-ABCDEFGH --output review/
    python3 Backend/premium_admin.py approve NV-20260101-ABCDEFGH --note "Paid in full"
    python3 Backend/premium_admin.py transactions --status SUCCEEDED --limit 50
    python3 Backend/premium_admin.py accounts --query runner@example.com
    python3 Backend/premium_admin.py catalog
    python3 Backend/premium_admin.py set-package gems_80 --price 490 --enable
    python3 Backend/premium_admin.py set-product skin_phantom_void --disable
    python3 Backend/premium_admin.py refund 42 --confirm --note "provider refund issued"
    python3 Backend/premium_admin.py ledger --limit 50
"""

from __future__ import annotations

import argparse
import email.message
import getpass
import json
import os
import pathlib
import re
import ssl
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, Dict, List, Optional, Tuple

from premium_store import (
    ORDER_STATUSES,
    PASSWORD_HASH_ITERATIONS,
    hash_password,
)

ORDER_ID_RE = re.compile(r"^NV-[0-9]{8}-[A-Z0-9]{8}$")
DEFAULT_BASE_URL = "http://127.0.0.1:8088"


class AdminCliError(Exception):
    """A user-facing CLI failure."""


def _base_url() -> str:
    raw = os.environ.get("NV_PREMIUM_URL", DEFAULT_BASE_URL).strip()
    if not raw:
        raise AdminCliError("NV_PREMIUM_URL is empty")
    parsed = urllib.parse.urlsplit(raw)
    if parsed.scheme not in ("http", "https"):
        raise AdminCliError("NV_PREMIUM_URL must use http or https")
    if not parsed.hostname:
        raise AdminCliError("NV_PREMIUM_URL has no host")
    if parsed.query or parsed.fragment:
        raise AdminCliError("NV_PREMIUM_URL must not contain a query or fragment")
    if parsed.scheme == "http":
        local_hosts = {"127.0.0.1", "::1", "localhost"}
        allow_http = os.environ.get("NV_ALLOW_INSECURE_HTTP", "").strip() == "1"
        if parsed.hostname not in local_hosts and not allow_http:
            raise AdminCliError(
                "Refusing to send admin credentials over remote plain HTTP; "
                "use HTTPS or set NV_ALLOW_INSECURE_HTTP=1 for a deliberate test"
            )
    return raw.rstrip("/")


def _order_id(value: str) -> str:
    candidate = value.strip().upper()
    if not ORDER_ID_RE.match(candidate):
        raise AdminCliError("Invalid order id format")
    return candidate


class AdminClient:
    """Minimal JSON and raw-byte client for the admin API."""

    def __init__(
        self,
        base_url: str,
        username: str,
        password: str,
        timeout: float = 20.0,
    ) -> None:
        self.base_url = base_url.rstrip("/")
        self.username = username
        self.password = password
        self.timeout = timeout
        self.token = ""
        try:
            self._ssl_context = self._build_ssl_context()
        except (OSError, ssl.SSLError) as exc:
            raise AdminCliError(
                "Could not load the NV_CA_BUNDLE CA file: {0}".format(exc)
            ) from exc

    @staticmethod
    def _build_ssl_context() -> ssl.SSLContext:
        bundle = os.environ.get("NV_CA_BUNDLE", "").strip()
        if bundle:
            return ssl.create_default_context(cafile=bundle)
        return ssl.create_default_context()

    def login(self) -> None:
        payload = {
            "username": self.username,
            "password": self.password,
        }
        response = self._json_request("POST", "/api/v1/admin/login", payload)
        token = response.get("token")
        if not isinstance(token, str) or not token:
            raise AdminCliError("Login response did not contain a token")
        self.token = token

    def _headers(self) -> Dict[str, str]:
        headers = {
            "Accept": "application/json",
            "User-Agent": "neon-premium-admin/1.0",
        }
        if self.token:
            headers["Authorization"] = "Bearer " + self.token
        return headers

    def _request(
        self,
        method: str,
        path: str,
        body: Optional[bytes] = None,
        content_type: Optional[str] = None,
    ) -> Tuple[int, Dict[str, str], bytes]:
        headers = self._headers()
        if body is not None and content_type:
            headers["Content-Type"] = content_type
        request = urllib.request.Request(
            self.base_url + path,
            data=body,
            headers=headers,
            method=method,
        )
        try:
            with urllib.request.urlopen(
                request,
                timeout=self.timeout,
                context=self._ssl_context,
            ) as response:
                headers_out = {key: value for key, value in response.headers.items()}
                return response.status, headers_out, response.read()
        except urllib.error.HTTPError as exc:
            raw = exc.read(64 * 1024)
            raise AdminCliError(self._describe_http_error(exc.code, raw)) from exc
        except urllib.error.URLError as exc:
            raise AdminCliError(
                "Cannot reach {0}: {1}".format(self.base_url, exc.reason)
            ) from exc
        except TimeoutError as exc:
            raise AdminCliError("Request to the premium API timed out") from exc

    @staticmethod
    def _describe_http_error(status: int, raw: bytes) -> str:
        try:
            payload = json.loads(raw.decode("utf-8"))
        except (UnicodeDecodeError, ValueError):
            payload = None
        if isinstance(payload, dict):
            error = str(payload.get("error") or "request failed")
            code = str(payload.get("code") or "")
            if code:
                return "HTTP {0}: {1} ({2})".format(status, error, code)
            return "HTTP {0}: {1}".format(status, error)
        return "HTTP {0}: request failed".format(status)

    def _json_request(
        self,
        method: str,
        path: str,
        payload: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        body = None
        content_type = None
        if payload is not None:
            body = json.dumps(payload, separators=(",", ":")).encode("utf-8")
            content_type = "application/json"
        _status, _headers, raw = self._request(method, path, body, content_type)
        try:
            decoded = json.loads(raw.decode("utf-8"))
        except (UnicodeDecodeError, ValueError) as exc:
            raise AdminCliError("Server returned invalid JSON") from exc
        if not isinstance(decoded, dict):
            raise AdminCliError("Server returned an unexpected JSON value")
        if decoded.get("ok") is not True:
            raise AdminCliError(
                str(decoded.get("error") or "Server rejected the request")
            )
        return decoded

    def fetch_orders(self, status: Optional[str]) -> List[Dict[str, Any]]:
        path = "/api/v1/admin/orders"
        if status:
            path += "?" + urllib.parse.urlencode({"status": status})
        response = self._json_request("GET", path)
        orders = response.get("orders")
        if not isinstance(orders, list):
            raise AdminCliError("Server response is missing the order list")
        return [order for order in orders if isinstance(order, dict)]

    def fetch_ledger(self, limit: int) -> List[Dict[str, Any]]:
        path = "/api/v1/admin/ledger?" + urllib.parse.urlencode({"limit": limit})
        response = self._json_request("GET", path)
        entries = response.get("entries")
        if not isinstance(entries, list):
            raise AdminCliError("Server response is missing ledger entries")
        return [entry for entry in entries if isinstance(entry, dict)]

    def fetch_stats(self) -> Dict[str, Any]:
        return self._json_request("GET", "/api/v1/admin/stats")

    def fetch_transactions(
        self, limit: int, provider: str, status: str
    ) -> List[Dict[str, Any]]:
        query: Dict[str, Any] = {"limit": limit}
        if provider:
            query["provider"] = provider
        if status:
            query["status"] = status
        response = self._json_request(
            "GET", "/api/v1/admin/transactions?" + urllib.parse.urlencode(query)
        )
        transactions = response.get("transactions")
        if not isinstance(transactions, list):
            raise AdminCliError("Server response is missing the transaction list")
        return [item for item in transactions if isinstance(item, dict)]

    def fetch_accounts(self, query_text: str, limit: int) -> List[Dict[str, Any]]:
        query: Dict[str, Any] = {"limit": limit}
        if query_text:
            query["q"] = query_text
        response = self._json_request(
            "GET", "/api/v1/admin/accounts?" + urllib.parse.urlencode(query)
        )
        accounts = response.get("accounts")
        if not isinstance(accounts, list):
            raise AdminCliError("Server response is missing the account list")
        return [item for item in accounts if isinstance(item, dict)]

    def fetch_account(self, account_id: str) -> Dict[str, Any]:
        response = self._json_request(
            "GET",
            "/api/v1/admin/accounts/{0}".format(
                urllib.parse.quote(account_id, safe="")
            ),
        )
        account = response.get("account")
        if not isinstance(account, dict):
            raise AdminCliError("Server response is missing the account detail")
        return account

    def fetch_catalog(self) -> Dict[str, Any]:
        return self._json_request("GET", "/api/v1/admin/catalog")

    def update_catalog(
        self, kind: str, item_id: str, fields: Dict[str, Any]
    ) -> Dict[str, Any]:
        if kind not in ("packages", "products"):
            raise AdminCliError("Unknown catalogue kind: {0}".format(kind))
        if not fields:
            raise AdminCliError("No catalogue fields were supplied")
        response = self._json_request(
            "POST",
            "/api/v1/admin/catalog/{0}/{1}".format(
                kind, urllib.parse.quote(item_id, safe="")
            ),
            fields,
        )
        item = response.get("item")
        if not isinstance(item, dict):
            raise AdminCliError("Server response is missing the updated item")
        return item

    def refund(self, transaction_id: int, note: str) -> Dict[str, Any]:
        return self._json_request(
            "POST",
            "/api/v1/admin/transactions/{0}/refund".format(transaction_id),
            {"confirmed": True, "note": note},
        )

    def decide(
        self, order_id: str, decision: str, note: str
    ) -> Dict[str, Any]:
        payload = {"decision": decision, "note": note}
        return self._json_request(
            "POST",
            "/api/v1/admin/orders/{0}/decision".format(order_id),
            payload,
        )

    def download_proof(
        self, order_id: str
    ) -> Tuple[bytes, Dict[str, str]]:
        _status, headers, raw = self._request(
            "GET",
            "/api/v1/admin/orders/{0}/proof".format(order_id),
        )
        return raw, headers


def _admin_credentials() -> Tuple[str, str]:
    username = os.environ.get("NV_ADMIN_USERNAME", "").strip()
    if not username:
        raise AdminCliError("NV_ADMIN_USERNAME is required")
    password = os.environ.get("NV_ADMIN_PASSWORD", "")
    if not password:
        password = getpass.getpass("Admin password for {0}: ".format(username))
    if not password:
        raise AdminCliError("No admin password supplied")
    return username, password


def _connected_client() -> AdminClient:
    username, password = _admin_credentials()
    client = AdminClient(_base_url(), username, password)
    try:
        client.login()
    except AdminCliError as exc:
        raise AdminCliError("Admin login failed: {0}".format(exc)) from exc
    client.password = ""
    return client


def _proof_filename(headers: Dict[str, str], order_id: str) -> str:
    disposition = ""
    for key, value in headers.items():
        if key.lower() == "content-disposition":
            disposition = value
            break
    if disposition:
        message = email.message.Message()
        message["Content-Disposition"] = disposition
        candidate = message.get_param(
            "filename", header="content-disposition"
        )
        if isinstance(candidate, str) and candidate:
            safe = pathlib.Path(candidate.replace("\\", "/")).name
            if safe and safe not in (".", ".."):
                return safe
    content_type = ""
    for key, value in headers.items():
        if key.lower() == "content-type":
            content_type = value.split(";")[0].strip().lower()
            break
    extension = {
        "image/png": "png",
        "image/jpeg": "jpg",
        "image/webp": "webp",
        "application/pdf": "pdf",
    }.get(content_type, "bin")
    return "{0}.{1}".format(order_id, extension)


def _write_private_file(path: pathlib.Path, data: bytes, force: bool) -> None:
    if path.exists() and not force:
        raise AdminCliError(
            "Output file already exists: {0} (use --force to replace)".format(path)
        )
    parent = path.parent
    if not parent.is_dir():
        raise AdminCliError("Output directory does not exist: {0}".format(parent))
    descriptor, temporary = tempfile.mkstemp(
        prefix=".premium-proof-", dir=str(parent)
    )
    try:
        os.chmod(temporary, 0o600)
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(data)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, str(path))
        os.chmod(str(path), 0o600)
    except BaseException:
        try:
            os.unlink(temporary)
        except OSError:
            pass
        raise


def _print_table(rows: List[List[str]], headings: List[str]) -> None:
    if not rows:
        print("No matching records.")
        return
    widths = [len(item) for item in headings]
    for row in rows:
        for index, cell in enumerate(row):
            widths[index] = max(widths[index], len(cell))
    format_string = "  ".join(
        "{{{0}:<{1}}}".format(index, width)
        for index, width in enumerate(widths)
    )
    print(format_string.format(*headings))
    print(format_string.format(*["-" * width for width in widths]))
    for row in rows:
        print(format_string.format(*row))


def _cmd_pending(client: AdminClient, _args: argparse.Namespace) -> int:
    orders = client.fetch_orders("PENDING_VERIFICATION")
    rows = []
    for order in orders:
        proof = order.get("proof")
        proof_text = "no proof"
        if isinstance(proof, dict):
            proof_text = "{0} {1} bytes {2}".format(
                proof.get("mime", "?"),
                proof.get("sizeBytes", "?"),
                str(proof.get("sha256", ""))[:12],
            )
        rows.append(
            [
                str(order.get("orderId", "")),
                str(order.get("amountLkr", "")),
                str(order.get("packageName", "")),
                str(order.get("displayName", "")),
                proof_text,
            ]
        )
    _print_table(
        rows,
        ["ORDER", "LKR", "PACKAGE", "PLAYER", "PROOF"],
    )
    print("\n{0} order(s) awaiting verification.".format(len(orders)))
    return 0


def _cmd_orders(client: AdminClient, args: argparse.Namespace) -> int:
    orders = client.fetch_orders(args.status)
    rows = [
        [
            str(order.get("orderId", "")),
            str(order.get("status", "")),
            str(order.get("amountLkr", "")),
            str(order.get("packageId", "")),
            str(order.get("displayName", "")),
            "yes" if order.get("proofSubmitted") else "no",
        ]
        for order in orders
    ]
    _print_table(
        rows,
        ["ORDER", "STATUS", "LKR", "PACKAGE", "PLAYER", "PROOF"],
    )
    return 0


def _cmd_proof(client: AdminClient, args: argparse.Namespace) -> int:
    order_id = _order_id(args.order_id)
    data, headers = client.download_proof(order_id)
    filename = _proof_filename(headers, order_id)
    output = pathlib.Path(args.output).expanduser() if args.output else pathlib.Path(filename)
    if output.is_dir():
        output = output / filename
    _write_private_file(output, data, args.force)
    print("Saved {0} bytes to {1}".format(len(data), output))
    return 0


def _cmd_decision(
    client: AdminClient, args: argparse.Namespace, decision: str
) -> int:
    order_id = _order_id(args.order_id)
    result = client.decide(order_id, decision, args.note or "")
    order = result.get("order") if isinstance(result.get("order"), dict) else {}
    print(
        "{0}: {1}; gems={2}".format(
            order_id,
            order.get("status", "UNKNOWN"),
            result.get("gems", "?"),
        )
    )
    return 0


def _cmd_approve(client: AdminClient, args: argparse.Namespace) -> int:
    return _cmd_decision(client, args, "approve")


def _cmd_reject(client: AdminClient, args: argparse.Namespace) -> int:
    return _cmd_decision(client, args, "reject")


def _cmd_ledger(client: AdminClient, args: argparse.Namespace) -> int:
    entries = client.fetch_ledger(args.limit)
    rows = [
        [
            str(entry.get("id", "")),
            str(entry.get("createdUtc", "")),
            str(entry.get("kind", "")),
            str(entry.get("deltaGems", "")),
            str(entry.get("balanceAfter", "")),
            str(entry.get("orderId") or ""),
            str(entry.get("productId") or ""),
            str(entry.get("accountId", "")),
        ]
        for entry in entries
    ]
    _print_table(
        rows,
        [
            "ID",
            "CREATED",
            "KIND",
            "DELTA",
            "BALANCE",
            "ORDER",
            "PRODUCT",
            "ACCOUNT",
        ],
    )
    return 0


def _cmd_stats(client: AdminClient, _args: argparse.Namespace) -> int:
    stats = client.fetch_stats()
    print(json.dumps(stats, indent=2, sort_keys=True))
    return 0


def _cmd_transactions(client: AdminClient, args: argparse.Namespace) -> int:
    transactions = client.fetch_transactions(
        args.limit, args.provider or "", args.status or ""
    )
    rows = [
        [
            str(item.get("id", "")),
            str(item.get("createdUtc", "")),
            str(item.get("provider", "")),
            str(item.get("status", "")),
            str(item.get("packageId", "")),
            str(item.get("gems", "")),
            "{0} {1}".format(item.get("amountMinor", ""), item.get("currency", "")).strip(),
            str(item.get("accountId", "")),
            str(item.get("accountEmail") or ""),
        ]
        for item in transactions
    ]
    _print_table(
        rows,
        [
            "ID",
            "CREATED",
            "PROVIDER",
            "STATUS",
            "PACKAGE",
            "GEMS",
            "AMOUNT",
            "ACCOUNT",
            "EMAIL",
        ],
    )
    print("\n{0} transaction(s).".format(len(transactions)))
    return 0


def _cmd_accounts(client: AdminClient, args: argparse.Namespace) -> int:
    accounts = client.fetch_accounts(args.query or "", args.limit)
    rows = [
        [
            str(item.get("accountId", "")),
            str(item.get("displayName", "")),
            str(item.get("email") or ""),
            str(item.get("phone") or ""),
            str(item.get("gems", "")),
            "yes" if item.get("emailVerified") else "no",
            str(item.get("createdUtc", "")),
        ]
        for item in accounts
    ]
    _print_table(
        rows,
        ["ACCOUNT", "PLAYER", "EMAIL", "PHONE", "GEMS", "VERIFIED", "CREATED"],
    )
    print("\n{0} account(s).".format(len(accounts)))
    return 0


def _cmd_account(client: AdminClient, args: argparse.Namespace) -> int:
    account = client.fetch_account(args.account_id)
    print(
        "{0}  {1}  gems={2}  email={3}  verified={4}".format(
            account.get("accountId", ""),
            account.get("displayName", ""),
            account.get("gems", "?"),
            account.get("email") or "-",
            "yes" if account.get("emailVerified") else "no",
        )
    )
    identities = account.get("identities")
    if isinstance(identities, list):
        print("\nIDENTITIES")
        for identity in identities:
            if not isinstance(identity, dict):
                continue
            print(
                "  {0}  {1}  verified={2}".format(
                    identity.get("provider", "?"),
                    identity.get("providerSubject", "") or identity.get("subject", ""),
                    "yes" if identity.get("verified") else "no",
                )
            )
    transactions = account.get("transactions")
    rows = []
    if isinstance(transactions, list):
        for item in transactions:
            if not isinstance(item, dict):
                continue
            rows.append(
                [
                    str(item.get("id", "")),
                    str(item.get("createdUtc", "")),
                    str(item.get("provider", "")),
                    str(item.get("status", "")),
                    str(item.get("gems", "")),
                    str(item.get("receiptEmail") or ""),
                ]
            )
    print("\nTRANSACTIONS")
    _print_table(rows, ["ID", "CREATED", "PROVIDER", "STATUS", "GEMS", "RECEIPT"])
    return 0


def _cmd_catalog(client: AdminClient, _args: argparse.Namespace) -> int:
    catalog = client.fetch_catalog()
    packages = catalog.get("packages")
    package_rows = []
    if isinstance(packages, list):
        for item in packages:
            if not isinstance(item, dict):
                continue
            package_rows.append(
                [
                    str(item.get("id", "")),
                    str(item.get("name", "")),
                    str(item.get("gems", "")),
                    str(item.get("bonusGems", "")),
                    str(item.get("priceLkr", "")),
                    "yes" if item.get("enabled") else "no",
                ]
            )
    print("GEM PACKAGES")
    _print_table(package_rows, ["ID", "NAME", "GEMS", "BONUS", "LKR", "ENABLED"])
    products = catalog.get("products")
    product_rows = []
    if isinstance(products, list):
        for item in products:
            if not isinstance(item, dict):
                continue
            product_rows.append(
                [
                    str(item.get("id", "")),
                    str(item.get("category", "")),
                    str(item.get("name", "")),
                    str(item.get("priceGems", "")),
                    "yes" if item.get("enabled") else "no",
                ]
            )
    print("\nPREMIUM PRODUCTS")
    _print_table(product_rows, ["ID", "CATEGORY", "NAME", "GEMS", "ENABLED"])
    return 0


def _catalog_fields(args: argparse.Namespace) -> Dict[str, Any]:
    fields: Dict[str, Any] = {}
    if args.name is not None:
        fields["name"] = args.name
    if args.description is not None:
        fields["description"] = args.description
    if args.price is not None:
        fields["price"] = args.price
    if args.enable:
        fields["enabled"] = True
    if args.disable:
        fields["enabled"] = False
    if not fields:
        raise AdminCliError(
            "Supply at least one of --name, --description, --price, --enable or --disable"
        )
    return fields


def _print_catalog_item(item: Dict[str, Any]) -> None:
    print(
        "{0}  {1}  price={2}  enabled={3}".format(
            item.get("id", ""),
            item.get("name", ""),
            item.get("priceLkr", item.get("priceGems", "?")),
            "yes" if item.get("enabled") else "no",
        )
    )


def _cmd_set_package(client: AdminClient, args: argparse.Namespace) -> int:
    item = client.update_catalog("packages", args.package_id, _catalog_fields(args))
    _print_catalog_item(item)
    return 0


def _cmd_set_product(client: AdminClient, args: argparse.Namespace) -> int:
    item = client.update_catalog("products", args.product_id, _catalog_fields(args))
    _print_catalog_item(item)
    return 0


def _cmd_refund(client: AdminClient, args: argparse.Namespace) -> int:
    result = client.refund(args.transaction_id, args.note or "")
    transaction = (
        result.get("transaction") if isinstance(result.get("transaction"), dict) else {}
    )
    print(
        "Transaction {0}: {1}; gems={2}; provider refund required={3}".format(
            args.transaction_id,
            transaction.get("status", "UNKNOWN"),
            result.get("gems", "?"),
            "yes" if result.get("providerRefundRequired") else "no",
        )
    )
    return 0


def _cmd_hash_password(_args: argparse.Namespace) -> int:
    first = getpass.getpass("New admin password: ")
    if len(first) < 12:
        raise AdminCliError("Use at least 12 characters for the admin password")
    second = getpass.getpass("Confirm admin password: ")
    if first != second:
        raise AdminCliError("Passwords do not match")
    encoded = hash_password(first, PASSWORD_HASH_ITERATIONS)
    print(encoded)
    print(
        "Store the line above as NV_ADMIN_PASSWORD_HASH and remove any plaintext password.",
        file=sys.stderr,
    )
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="NEON VELOCITY premium operator CLI"
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    pending = subparsers.add_parser(
        "pending", help="List orders awaiting payment verification"
    )
    pending.set_defaults(handler=_cmd_pending, needs_client=True)

    orders = subparsers.add_parser("orders", help="List orders with an optional status")
    orders.add_argument(
        "--status",
        choices=ORDER_STATUSES,
        default=None,
        help="Filter by order status",
    )
    orders.set_defaults(handler=_cmd_orders, needs_client=True)

    proof = subparsers.add_parser("proof", help="Download one proof file")
    proof.add_argument("order_id")
    proof.add_argument(
        "--output",
        default=None,
        help="Destination file or existing directory",
    )
    proof.add_argument(
        "--force",
        action="store_true",
        help="Replace an existing output file",
    )
    proof.set_defaults(handler=_cmd_proof, needs_client=True)

    approve = subparsers.add_parser("approve", help="Approve a verified order")
    approve.add_argument("order_id")
    approve.add_argument("--note", default="", help="Operator note (maximum 500 characters)")
    approve.set_defaults(handler=_cmd_approve, needs_client=True)

    reject = subparsers.add_parser("reject", help="Reject a payment proof")
    reject.add_argument("order_id")
    reject.add_argument("--note", default="", help="Reason shown to the player")
    reject.set_defaults(handler=_cmd_reject, needs_client=True)

    ledger = subparsers.add_parser("ledger", help="Show the GEM ledger")
    ledger.add_argument("--limit", type=int, default=100)
    ledger.set_defaults(handler=_cmd_ledger, needs_client=True)

    stats = subparsers.add_parser("stats", help="Show order and credit totals")
    stats.set_defaults(handler=_cmd_stats, needs_client=True)

    transactions = subparsers.add_parser(
        "transactions", help="Review card and store transactions"
    )
    transactions.add_argument("--provider", default="", help="Filter by provider")
    transactions.add_argument("--status", default="", help="Filter by status")
    transactions.add_argument("--limit", type=int, default=100)
    transactions.set_defaults(handler=_cmd_transactions, needs_client=True)

    accounts = subparsers.add_parser(
        "accounts", help="Search player accounts and wallets"
    )
    accounts.add_argument("--query", default="", help="Account id, name, email or phone")
    accounts.add_argument("--limit", type=int, default=100)
    accounts.set_defaults(handler=_cmd_accounts, needs_client=True)

    account = subparsers.add_parser(
        "account", help="Review one account, its identities and transactions"
    )
    account.add_argument("account_id")
    account.set_defaults(handler=_cmd_account, needs_client=True)

    catalog = subparsers.add_parser(
        "catalog", help="Show the effective GEM package and product catalogue"
    )
    catalog.set_defaults(handler=_cmd_catalog, needs_client=True)

    for kind, handler, identifier in (
        ("package", _cmd_set_package, "package_id"),
        ("product", _cmd_set_product, "product_id"),
    ):
        command = subparsers.add_parser(
            "set-" + kind,
            help="Change the {0} name, description, price or enabled state".format(kind),
        )
        command.add_argument(identifier)
        command.add_argument("--name", default=None)
        command.add_argument("--description", default=None)
        command.add_argument("--price", type=int, default=None)
        state = command.add_mutually_exclusive_group()
        state.add_argument(
            "--enable", action="store_true", help="Make the {0} purchasable".format(kind)
        )
        state.add_argument(
            "--disable", action="store_true", help="Hide the {0} from players".format(kind)
        )
        command.set_defaults(handler=handler, needs_client=True)

    refund = subparsers.add_parser(
        "refund",
        help="Reverse GEMS after the payment provider has confirmed a refund",
    )
    refund.add_argument("transaction_id", type=int)
    refund.add_argument("--confirm", action="store_true")
    refund.add_argument("--note", default="")
    refund.set_defaults(handler=_cmd_refund, needs_client=True)

    hash_password_parser = subparsers.add_parser(
        "hash-password",
        help="Prompt for a password and print an NV_ADMIN_PASSWORD_HASH value",
    )
    hash_password_parser.set_defaults(
        handler=_cmd_hash_password, needs_client=False
    )
    return parser


def main(argv: Optional[List[str]] = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    if getattr(args, "needs_client", False) and args.command in (
        "ledger",
        "transactions",
        "accounts",
    ):
        if args.limit < 1 or args.limit > 500:
            parser.error("--limit must be between 1 and 500")
    if args.command in ("set-package", "set-product"):
        if args.price is not None and (args.price < 1 or args.price > 10_000_000):
            parser.error("--price must be between 1 and 10000000")
    if args.command == "refund":
        if not args.confirm:
            parser.error(
                "--confirm is required after the provider has issued the refund"
            )
        if len(args.note or "") > 500:
            parser.error("--note must be at most 500 characters")
    if args.command in ("approve", "reject") and len(args.note or "") > 500:
        parser.error("--note must be at most 500 characters")
    try:
        if getattr(args, "needs_client", False):
            client = _connected_client()
        else:
            client = None
        if client is not None:
            return int(args.handler(client, args))
        return int(args.handler(args))
    except AdminCliError as exc:
        print("ERROR: {0}".format(exc), file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("Cancelled.", file=sys.stderr)
        return 130


if __name__ == "__main__":
    sys.exit(main())
