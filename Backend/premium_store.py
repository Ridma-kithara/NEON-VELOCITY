#!/usr/bin/env python3
"""SQLite persistence and business rules for the NEON VELOCITY premium store.

The module is deliberately dependency-free and owns every money or GEM mutation. HTTP
handlers validate transport-level input, then call these methods; all balance changes use
``BEGIN IMMEDIATE`` so concurrent requests cannot spend the same balance twice.

Security boundaries:
  * device keys are keyed-hashed before they reach SQLite;
  * GEM balances and prices never come from the client;
  * order totals are copied from the immutable server catalogue;
  * approval credits an order exactly once inside one transaction;
  * proof files get generated names and restrictive permissions;
  * every query is parameterised.
"""

from __future__ import annotations

import contextlib
import dataclasses
import datetime as dt
import hashlib
import hmac
import json
import os
import pathlib
import secrets
import sqlite3
from typing import Any, Dict, Iterator, List, Optional, Tuple

SERVICE_NAME = "neon-velocity-premium"
SERVER_VERSION = "1.0.0"
CURRENCY = "LKR"
REVIVE_COST_GEMS = 40
ORDER_LIFETIME_SECONDS = 7 * 24 * 60 * 60
DEFAULT_AUTH_TOKEN_TTL_SECONDS = 30 * 24 * 60 * 60
PASSWORD_HASH_ITERATIONS = 260_000
PASSWORD_HASH_PREFIX = "pbkdf2_sha256"

ORDER_STATUSES: Tuple[str, ...] = (
    "PENDING_PAYMENT",
    "PENDING_VERIFICATION",
    "APPROVED",
    "REJECTED",
    "EXPIRED",
)
PENDING_ORDER_STATUSES: Tuple[str, ...] = ("PENDING_PAYMENT", "PENDING_VERIFICATION")
DECIDED_ORDER_STATUSES: Tuple[str, ...] = ("APPROVED", "REJECTED")

ORDER_ID_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"
PROOF_EXTENSIONS: Dict[str, str] = {
    "image/png": "png",
    "image/jpeg": "jpg",
    "image/webp": "webp",
    "application/pdf": "pdf",
}

BANK_DETAILS: Dict[str, str] = {
    "accountNumber": "8022347868",
    "accountName": "G.W.W.M. R.K. Kirinda",
    "bankName": "Commercial Bank PLC",
    "branch": "Athurugiriya Branch",
    "currency": CURRENCY,
}


@dataclasses.dataclass(frozen=True)
class GemPackage:
    """One immutable server-side GEM package definition."""

    id: str
    name: str
    gems: int
    bonus_gems: int
    price_lkr: int
    description: str
    enabled: bool = True
    tier: str = ""

    @property
    def total_gems(self) -> int:
        return self.gems + self.bonus_gems

    def catalog_dict(self) -> Dict[str, Any]:
        return {
            "id": self.id,
            "tier": self.tier,
            "name": self.name,
            "gems": self.gems,
            "bonusGems": self.bonus_gems,
            "totalGems": self.total_gems,
            "priceLkr": self.price_lkr,
            "currency": CURRENCY,
            "description": self.description,
            "enabled": self.enabled,
        }


def _env_int(name: str, default: int, minimum: int = 0) -> int:
    """Read a non-negative integer override, falling back to the shipped default."""

    raw = os.environ.get(name, "").strip()
    if not raw:
        return default
    try:
        value = int(raw)
    except ValueError:
        return default
    return value if value >= minimum else default


def _env_bool(name: str, default: bool) -> bool:
    raw = os.environ.get(name, "").strip().lower()
    if not raw:
        return default
    if raw in ("1", "true", "yes", "on"):
        return True
    if raw in ("0", "false", "no", "off"):
        return False
    return default


def _env_text(name: str, default: str) -> str:
    return os.environ.get(name, "").strip() or default


def _gem_package(
    package_id: str,
    tier: str,
    name: str,
    gems: int,
    bonus_gems: int,
    price_lkr: int,
    description: str,
) -> GemPackage:
    """Build a package from defaults with optional operator environment overrides.

    The server remains the sole price/amount authority. Operators can adjust a tier
    without editing code by setting ``NV_GEM_PACKAGE_<ID>_GEMS``, ``_BONUS_GEMS``,
    ``_PRICE_LKR``, ``_NAME``, ``_DESCRIPTION`` or ``_ENABLED``.
    """

    prefix = "NV_GEM_PACKAGE_" + package_id.upper()
    return GemPackage(
        package_id,
        _env_text(prefix + "_NAME", name),
        _env_int(prefix + "_GEMS", gems, 1),
        _env_int(prefix + "_BONUS_GEMS", bonus_gems),
        _env_int(prefix + "_PRICE_LKR", price_lkr, 1),
        _env_text(prefix + "_DESCRIPTION", description),
        _env_bool(prefix + "_ENABLED", True),
        tier,
    )


@dataclasses.dataclass(frozen=True)
class PremiumProduct:
    """One immutable cosmetic, ability, revival or upgrade definition."""

    id: str
    category: str
    name: str
    description: str
    price_gems: int
    character_id: str = ""
    upgrade_id: str = ""
    max_level: int = 0
    enabled: bool = True

    def catalog_dict(self) -> Dict[str, Any]:
        return {
            "id": self.id,
            "category": self.category,
            "name": self.name,
            "description": self.description,
            "priceGems": self.price_gems,
            "characterId": self.character_id,
            "upgradeId": self.upgrade_id,
            "maxLevel": self.max_level,
            "enabled": self.enabled,
        }


GEM_PACKAGES: Tuple[GemPackage, ...] = (
    _gem_package(
        "gems_80",
        "STARTER",
        "STARTER CACHE",
        80,
        0,
        490,
        "Compact GEM cache for a quick start.",
    ),
    _gem_package(
        "gems_200",
        "BASIC",
        "BASIC CACHE",
        200,
        10,
        1190,
        "Grid-sized GEM cache with a small bonus.",
    ),
    _gem_package(
        "gems_500",
        "PREMIUM",
        "PREMIUM CACHE",
        500,
        50,
        2890,
        "High-capacity GEM cache with a bonus.",
    ),
    _gem_package(
        "gems_1200",
        "MEGA",
        "MEGA CACHE",
        1200,
        180,
        6490,
        "Large GEM cache for committed runners.",
    ),
    _gem_package(
        "gems_2500",
        "ULTIMATE",
        "ULTIMATE CACHE",
        2500,
        500,
        12900,
        "Maximum GEM cache with the largest bonus.",
    ),
)

PREMIUM_PRODUCTS: Tuple[PremiumProduct, ...] = (
    PremiumProduct(
        "char_phantom_x",
        "character",
        "PHANTOM X",
        "stealth phase-dash runner",
        450,
        character_id="phantom",
    ),
    PremiumProduct(
        "char_nova_titan",
        "character",
        "NOVA TITAN",
        "heavy energy-burst runner",
        700,
        character_id="nova",
    ),
    PremiumProduct(
        "char_echo",
        "character",
        "ECHO",
        "sonar survey runner",
        520,
        character_id="echo",
    ),
    PremiumProduct(
        "char_sable",
        "character",
        "SABLE",
        "night-veil stealth runner",
        620,
        character_id="sable",
    ),
    PremiumProduct(
        "char_helios",
        "character",
        "HELIOS",
        "solar-shield bulwark runner",
        760,
        character_id="helios",
    ),
    PremiumProduct(
        "skin_phantom_void",
        "skin",
        "VOID RUNNER SKIN",
        "Phantom X void-phase cosmetic skin.",
        120,
    ),
    PremiumProduct(
        "skin_nova_solar",
        "skin",
        "SOLAR FLARE SKIN",
        "Nova Titan solar-charged cosmetic skin.",
        160,
    ),
    PremiumProduct(
        "armor_spectre",
        "armor",
        "SPECTRE ARMOR",
        "Spectral armor plating for the runner.",
        180,
    ),
    PremiumProduct(
        "helmet_ghost",
        "helmet",
        "GHOST HELMET",
        "Phase-shifted helmet with a cool neon visor.",
        90,
    ),
    PremiumProduct(
        "fx_phase_wake",
        "effect",
        "PHASE WAKE FX",
        "Violet phase-wake trail effect.",
        110,
    ),
    PremiumProduct(
        "move_quantum",
        "movement_effect",
        "QUANTUM STRIDE",
        "Quantum movement trail for every run.",
        130,
    ),
    PremiumProduct(
        "anim_kneel_rise",
        "animation",
        "KNEEL AND RISE",
        "Victory kneel-and-rise animation.",
        80,
    ),
    PremiumProduct(
        "ability_phase_dash",
        "ability",
        "PHASE DASH",
        "Short stealth phase-dash ability.",
        260,
    ),
    PremiumProduct(
        "ability_nova_burst",
        "ability",
        "NOVA BURST",
        "Heavy energy-burst ability.",
        320,
    ),
    PremiumProduct(
        "revive_token",
        "revival",
        "REVIVE TOKEN",
        "Single-run revival entitlement.",
        35,
    ),
    PremiumProduct(
        "upgrade_revive_stabilizer",
        "upgrade",
        "REVIVE STABILIZER",
        "Extends the revival stabilisation window.",
        120,
        upgrade_id="revive_stabilizer",
        max_level=3,
    ),
    PremiumProduct(
        "upgrade_phase_effect",
        "upgrade",
        "PHASE EFFECT",
        "Refines the Phantom phase visual effect.",
        90,
        upgrade_id="phase_effect",
        max_level=3,
    ),
    PremiumProduct(
        "upgrade_nova_effect",
        "upgrade",
        "NOVA EFFECT",
        "Refines the Nova energy visual effect.",
        90,
        upgrade_id="nova_effect",
        max_level=3,
    ),
)

PACKAGES_BY_ID: Dict[str, GemPackage] = {item.id: item for item in GEM_PACKAGES}
PRODUCTS_BY_ID: Dict[str, PremiumProduct] = {item.id: item for item in PREMIUM_PRODUCTS}


class PremiumError(Exception):
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


class PackageNotFound(PremiumError):
    def __init__(self) -> None:
        super().__init__(404, "PACKAGE_NOT_FOUND", "PACKAGE_NOT_FOUND")


class PackageDisabled(PremiumError):
    def __init__(self) -> None:
        super().__init__(409, "PACKAGE_DISABLED", "PACKAGE_DISABLED")


class ProductNotFound(PremiumError):
    def __init__(self) -> None:
        super().__init__(404, "PRODUCT_NOT_FOUND", "PRODUCT_NOT_FOUND")


class ProductDisabled(PremiumError):
    def __init__(self) -> None:
        super().__init__(409, "PRODUCT_DISABLED", "PRODUCT_DISABLED")


class InsufficientGems(PremiumError):
    def __init__(self, gems: int) -> None:
        super().__init__(
            402,
            "INSUFFICIENT_GEMS",
            "INSUFFICIENT_GEMS",
            {"gems": int(gems)},
        )
        self.gems = int(gems)


class ProductAlreadyOwned(PremiumError):
    def __init__(self) -> None:
        super().__init__(409, "ALREADY_OWNED", "ALREADY_OWNED")


class MaximumLevelReached(PremiumError):
    def __init__(self, level: int) -> None:
        super().__init__(
            409,
            "MAX_LEVEL_REACHED",
            "MAX_LEVEL_REACHED",
            {"level": int(level)},
        )


class OrderNotFound(PremiumError):
    def __init__(self) -> None:
        super().__init__(404, "ORDER_NOT_FOUND", "ORDER_NOT_FOUND")


class OrderNotOpenForProof(PremiumError):
    def __init__(self) -> None:
        super().__init__(
            409,
            "ORDER_NOT_OPEN_FOR_PROOF",
            "ORDER_NOT_OPEN_FOR_PROOF",
        )


class OrderAlreadyDecided(PremiumError):
    def __init__(self) -> None:
        super().__init__(
            409,
            "ORDER_ALREADY_DECIDED",
            "ORDER_ALREADY_DECIDED",
        )


class OrderNotAwaitingVerification(PremiumError):
    def __init__(self) -> None:
        super().__init__(
            409,
            "ORDER_NOT_AWAITING_VERIFICATION",
            "ORDER_NOT_AWAITING_VERIFICATION",
        )


class ProofNotFound(PremiumError):
    def __init__(self) -> None:
        super().__init__(404, "PROOF_NOT_FOUND", "PROOF_NOT_FOUND")


class AccountNotFound(PremiumError):
    def __init__(self) -> None:
        super().__init__(401, "UNAUTHORIZED", "UNAUTHORIZED")


def utc_now() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc)


def iso_utc(moment: Optional[dt.datetime] = None, offset_seconds: int = 0) -> str:
    """Return an ISO-8601 UTC timestamp with the ``Z`` suffix."""

    value = moment if moment is not None else utc_now()
    value = value.astimezone(dt.timezone.utc) + dt.timedelta(seconds=offset_seconds)
    return value.replace(microsecond=0).isoformat().replace("+00:00", "Z")


def compact_json(payload: Any) -> str:
    return json.dumps(payload, ensure_ascii=False, separators=(",", ":"))


def sha256_hex(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def hash_password(password: str, iterations: int = PASSWORD_HASH_ITERATIONS) -> str:
    """Create an encoded PBKDF2-SHA256 verifier suitable for NV_ADMIN_PASSWORD_HASH."""

    if not isinstance(password, str) or not password:
        raise ValueError("password must be a non-empty string")
    if iterations < 50_000 or iterations > 5_000_000:
        raise ValueError("iteration count is outside the supported range")
    salt = secrets.token_bytes(16)
    derived = hashlib.pbkdf2_hmac(
        "sha256", password.encode("utf-8"), salt, iterations
    )
    return "{0}${1}${2}${3}".format(
        PASSWORD_HASH_PREFIX, iterations, salt.hex(), derived.hex()
    )


def verify_password(password: str, encoded: str) -> bool:
    """Constant-time verification of a ``pbkdf2_sha256$...`` verifier."""

    if not isinstance(password, str) or not isinstance(encoded, str):
        return False
    parts = encoded.split("$")
    if len(parts) != 4 or parts[0] != PASSWORD_HASH_PREFIX:
        return False
    try:
        iterations = int(parts[1])
        salt = bytes.fromhex(parts[2])
        expected = bytes.fromhex(parts[3])
    except (TypeError, ValueError):
        return False
    if iterations < 50_000 or iterations > 5_000_000:
        return False
    if not salt or not expected:
        return False
    derived = hashlib.pbkdf2_hmac(
        "sha256", password.encode("utf-8"), salt, iterations
    )
    return hmac.compare_digest(derived, expected)


def password_hash_format_is_valid(encoded: str) -> bool:
    """Check the verifier format without deriving a key."""

    if not isinstance(encoded, str):
        return False
    parts = encoded.split("$")
    if len(parts) != 4 or parts[0] != PASSWORD_HASH_PREFIX:
        return False
    try:
        iterations = int(parts[1])
        salt = bytes.fromhex(parts[2])
        expected = bytes.fromhex(parts[3])
    except (TypeError, ValueError):
        return False
    return (
        50_000 <= iterations <= 5_000_000
        and len(salt) >= 8
        and len(expected) == 32
    )


def generate_account_id() -> str:
    return "acc_" + secrets.token_hex(12)


def generate_order_id(moment: Optional[dt.datetime] = None) -> str:
    value = moment if moment is not None else utc_now()
    date_part = value.astimezone(dt.timezone.utc).strftime("%Y%m%d")
    random_part = "".join(secrets.choice(ORDER_ID_ALPHABET) for _ in range(8))
    return "NV-{0}-{1}".format(date_part, random_part)


def generate_token() -> str:
    return secrets.token_urlsafe(32)


def sanitize_original_filename(value: str, fallback: str) -> str:
    """Keep harmless display metadata while removing directories and controls."""

    candidate = str(value or "").replace("\\", "/").split("/")[-1]
    cleaned = "".join(
        character for character in candidate if 32 <= ord(character) <= 126
    ).strip()
    cleaned = cleaned.replace('"', "").replace("'", "")
    if not cleaned or cleaned in (".", ".."):
        cleaned = fallback
    return cleaned[:120]


def detect_proof_mime(data: bytes) -> Optional[str]:
    """Detect an allowed proof type from magic bytes only."""

    if data.startswith(b"\x89PNG\r\n\x1a\n"):
        return "image/png"
    if data.startswith(b"\xff\xd8\xff"):
        return "image/jpeg"
    if len(data) >= 12 and data.startswith(b"RIFF") and data[8:12] == b"WEBP":
        return "image/webp"
    if data.startswith(b"%PDF-"):
        return "application/pdf"
    return None


SCHEMA_SQL = """
CREATE TABLE IF NOT EXISTS accounts (
    id TEXT PRIMARY KEY,
    device_key_hash TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    gems INTEGER NOT NULL DEFAULT 0 CHECK (gems >= 0),
    created_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS auth_tokens (
    token_hash TEXT PRIMARY KEY,
    account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    expires_utc TEXT NOT NULL,
    created_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_auth_tokens_account ON auth_tokens(account_id);

CREATE TABLE IF NOT EXISTS admin_tokens (
    token_hash TEXT PRIMARY KEY,
    username TEXT NOT NULL,
    expires_utc TEXT NOT NULL,
    created_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS orders (
    id TEXT PRIMARY KEY,
    account_id TEXT NOT NULL REFERENCES accounts(id),
    package_id TEXT NOT NULL,
    gems INTEGER NOT NULL CHECK (gems >= 0),
    bonus_gems INTEGER NOT NULL CHECK (bonus_gems >= 0),
    amount_lkr INTEGER NOT NULL CHECK (amount_lkr >= 0),
    currency TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN (
        'PENDING_PAYMENT', 'PENDING_VERIFICATION', 'APPROVED', 'REJECTED', 'EXPIRED'
    )),
    reference TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    expires_utc TEXT NOT NULL,
    proof_path TEXT,
    proof_mime TEXT,
    proof_sha256 TEXT,
    proof_size INTEGER,
    proof_original_name TEXT,
    proof_submitted_utc TEXT,
    admin_note TEXT,
    decided_utc TEXT
);
CREATE INDEX IF NOT EXISTS idx_orders_account_created
    ON orders(account_id, created_utc DESC);
CREATE INDEX IF NOT EXISTS idx_orders_status_created
    ON orders(status, created_utc DESC);

CREATE TABLE IF NOT EXISTS order_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    order_id TEXT NOT NULL REFERENCES orders(id),
    event TEXT NOT NULL,
    detail TEXT NOT NULL,
    created_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_order_events_order ON order_events(order_id, id);

CREATE TABLE IF NOT EXISTS ledger (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    account_id TEXT NOT NULL REFERENCES accounts(id),
    order_id TEXT,
    product_id TEXT,
    delta_gems INTEGER NOT NULL,
    balance_after INTEGER NOT NULL,
    kind TEXT NOT NULL,
    note TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_ledger_created ON ledger(id DESC);
CREATE INDEX IF NOT EXISTS idx_ledger_account ON ledger(account_id, id DESC);

CREATE TABLE IF NOT EXISTS ownership (
    account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    product_id TEXT NOT NULL,
    category TEXT NOT NULL,
    level INTEGER NOT NULL DEFAULT 0,
    acquired_utc TEXT NOT NULL,
    PRIMARY KEY (account_id, product_id)
);

CREATE TABLE IF NOT EXISTS idempotency (
    account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    key TEXT NOT NULL,
    scope TEXT NOT NULL,
    response_json TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    UNIQUE (account_id, key, scope)
);

CREATE TABLE IF NOT EXISTS revive_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    account_id TEXT NOT NULL REFERENCES accounts(id),
    session_id TEXT NOT NULL,
    distance REAL NOT NULL,
    location_index INTEGER NOT NULL,
    cost_gems INTEGER NOT NULL,
    token TEXT NOT NULL,
    created_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_revive_events_account
    ON revive_events(account_id, id DESC);

CREATE TABLE IF NOT EXISTS admin_audit (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    admin_username TEXT NOT NULL,
    action TEXT NOT NULL,
    order_id TEXT,
    detail TEXT NOT NULL DEFAULT '',
    ip_address TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_admin_audit_created ON admin_audit(id DESC);

CREATE TABLE IF NOT EXISTS catalog_overrides (
    kind TEXT NOT NULL CHECK (kind IN ('package', 'product')),
    item_id TEXT NOT NULL,
    name TEXT,
    description TEXT,
    price INTEGER,
    enabled INTEGER,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (kind, item_id)
);
"""


class PremiumStore:
    """Thread-safe-by-connection SQLite store. Every public method opens its own connection."""

    def __init__(self, data_dir: pathlib.Path) -> None:
        self.data_dir = pathlib.Path(data_dir).expanduser().resolve()
        self.db_path = self.data_dir / "premium.sqlite3"
        self.proofs_dir = self.data_dir / "proofs"
        self.secret_path = self.data_dir / "premium_secret.key"
        self._ensure_directories()
        self._secret = self._load_or_create_secret()
        self._initialize_database()

    # ---- filesystem and connection plumbing -------------------------------------------

    @staticmethod
    def _chmod_private(path: pathlib.Path, mode: int) -> None:
        os.chmod(str(path), mode)

    def _ensure_directories(self) -> None:
        self.data_dir.mkdir(parents=True, exist_ok=True, mode=0o700)
        self._chmod_private(self.data_dir, 0o700)
        self.proofs_dir.mkdir(parents=True, exist_ok=True, mode=0o700)
        self._chmod_private(self.proofs_dir, 0o700)

    def _load_or_create_secret(self) -> bytes:
        if self.secret_path.exists():
            self._chmod_private(self.secret_path, 0o600)
            raw = self.secret_path.read_text(encoding="ascii").strip()
            try:
                secret = bytes.fromhex(raw)
            except ValueError as exc:
                raise RuntimeError(
                    "Premium secret file is corrupt; restore it from backup"
                ) from exc
            if len(secret) < 32:
                raise RuntimeError(
                    "Premium secret file is too short; restore it from backup"
                )
            return secret

        secret = secrets.token_bytes(32)
        flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
        try:
            descriptor = os.open(str(self.secret_path), flags, 0o600)
        except FileExistsError:
            return self._load_or_create_secret()
        try:
            with os.fdopen(descriptor, "w", encoding="ascii") as handle:
                handle.write(secret.hex())
                handle.flush()
                os.fsync(handle.fileno())
        finally:
            self._chmod_private(self.secret_path, 0o600)
        return secret

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(
            str(self.db_path), timeout=10.0, isolation_level=None
        )
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        connection.execute("PRAGMA busy_timeout = 10000")
        connection.execute("PRAGMA synchronous = FULL")
        return connection

    def _initialize_database(self) -> None:
        connection = self._connect()
        try:
            connection.execute("PRAGMA journal_mode = WAL")
            connection.executescript(SCHEMA_SQL)
            connection.execute("PRAGMA user_version = 1")
        finally:
            connection.close()
        self._chmod_private(self.db_path, 0o600)
        for suffix in ("-wal", "-shm"):
            candidate = pathlib.Path(str(self.db_path) + suffix)
            if candidate.exists():
                self._chmod_private(candidate, 0o600)

    @contextlib.contextmanager
    def connection(self) -> Iterator[sqlite3.Connection]:
        connection = self._connect()
        try:
            yield connection
        finally:
            connection.close()

    @contextlib.contextmanager
    def transaction(self) -> Iterator[sqlite3.Connection]:
        connection = self._connect()
        try:
            connection.execute("BEGIN IMMEDIATE")
            try:
                yield connection
            except BaseException:
                connection.execute("ROLLBACK")
                raise
            else:
                connection.execute("COMMIT")
        finally:
            connection.close()

    # ---- catalogue overrides -----------------------------------------------------------

    @staticmethod
    def _override_key(kind: str, item_id: str) -> Tuple[str, str]:
        if kind not in ("package", "product"):
            raise PremiumError(400, "INVALID_CATALOG_KIND", "INVALID_CATALOG_KIND")
        if not isinstance(item_id, str) or not item_id:
            raise PremiumError(400, "INVALID_CATALOG_ITEM", "INVALID_CATALOG_ITEM")
        return kind, item_id

    def _catalog_overrides(self, kind: str) -> Dict[str, Dict[str, Any]]:
        with self.connection() as connection:
            rows = connection.execute(
                "SELECT item_id, name, description, price, enabled "
                "FROM catalog_overrides WHERE kind = ?",
                (kind,),
            ).fetchall()
        return {str(row["item_id"]): dict(row) for row in rows}

    def catalog_packages(self) -> List[Dict[str, Any]]:
        overrides = self._catalog_overrides("package")
        payload: List[Dict[str, Any]] = []
        for package in GEM_PACKAGES:
            item = package.catalog_dict()
            override = overrides.get(package.id)
            if override:
                if override["name"] is not None:
                    item["name"] = str(override["name"])
                if override["description"] is not None:
                    item["description"] = str(override["description"])
                if override["price"] is not None:
                    item["priceLkr"] = int(override["price"])
                if override["enabled"] is not None:
                    item["enabled"] = bool(override["enabled"])
            payload.append(item)
        return payload

    def catalog_products(self) -> List[Dict[str, Any]]:
        overrides = self._catalog_overrides("product")
        payload: List[Dict[str, Any]] = []
        for product in PREMIUM_PRODUCTS:
            item = product.catalog_dict()
            override = overrides.get(product.id)
            if override:
                if override["name"] is not None:
                    item["name"] = str(override["name"])
                if override["description"] is not None:
                    item["description"] = str(override["description"])
                if override["price"] is not None:
                    item["priceGems"] = int(override["price"])
                if override["enabled"] is not None:
                    item["enabled"] = bool(override["enabled"])
            payload.append(item)
        return payload

    def catalog_snapshot(self) -> Dict[str, Any]:
        return {
            "packages": self.catalog_packages(),
            "products": self.catalog_products(),
            "reviveCostGems": REVIVE_COST_GEMS,
        }

    def package_by_id(self, package_id: str) -> Optional[GemPackage]:
        package = PACKAGES_BY_ID.get(str(package_id))
        if package is None:
            return None
        override = self._catalog_overrides("package").get(package.id)
        if not override:
            return package
        return dataclasses.replace(
            package,
            name=str(override["name"]) if override["name"] is not None else package.name,
            description=(
                str(override["description"])
                if override["description"] is not None
                else package.description
            ),
            price_lkr=(
                int(override["price"])
                if override["price"] is not None
                else package.price_lkr
            ),
            enabled=(
                bool(override["enabled"])
                if override["enabled"] is not None
                else package.enabled
            ),
        )

    def product_by_id(self, product_id: str) -> Optional[PremiumProduct]:
        product = PRODUCTS_BY_ID.get(str(product_id))
        if product is None:
            return None
        override = self._catalog_overrides("product").get(product.id)
        if not override:
            return product
        return dataclasses.replace(
            product,
            name=str(override["name"]) if override["name"] is not None else product.name,
            description=(
                str(override["description"])
                if override["description"] is not None
                else product.description
            ),
            price_gems=(
                int(override["price"])
                if override["price"] is not None
                else product.price_gems
            ),
            enabled=(
                bool(override["enabled"])
                if override["enabled"] is not None
                else product.enabled
            ),
        )

    def set_catalog_override(
        self,
        kind: str,
        item_id: str,
        *,
        name: Optional[Any] = None,
        description: Optional[Any] = None,
        price: Optional[Any] = None,
        enabled: Optional[Any] = None,
    ) -> Dict[str, Any]:
        kind, item_id = self._override_key(kind, item_id)
        known = PACKAGES_BY_ID if kind == "package" else PRODUCTS_BY_ID
        if item_id not in known:
            raise PremiumError(404, "CATALOG_ITEM_NOT_FOUND", "CATALOG_ITEM_NOT_FOUND")
        clean_name: Optional[str] = None
        if name is not None:
            if not isinstance(name, str) or not name.strip() or len(name.strip()) > 80:
                raise PremiumError(400, "INVALID_NAME", "INVALID_NAME")
            clean_name = name.strip()
        clean_description: Optional[str] = None
        if description is not None:
            if not isinstance(description, str) or len(description.strip()) > 240:
                raise PremiumError(400, "INVALID_DESCRIPTION", "INVALID_DESCRIPTION")
            clean_description = description.strip()
        clean_price: Optional[int] = None
        if price is not None:
            if isinstance(price, bool) or not isinstance(price, int):
                raise PremiumError(400, "INVALID_PRICE", "INVALID_PRICE")
            if price < 1 or price > 10_000_000:
                raise PremiumError(400, "INVALID_PRICE", "INVALID_PRICE")
            clean_price = price
        clean_enabled: Optional[int] = None
        if enabled is not None:
            if not isinstance(enabled, bool):
                raise PremiumError(400, "INVALID_ENABLED", "INVALID_ENABLED")
            clean_enabled = 1 if enabled else 0
        if all(value is None for value in (clean_name, clean_description, clean_price, clean_enabled)):
            raise PremiumError(400, "EMPTY_CATALOG_UPDATE", "EMPTY_CATALOG_UPDATE")
        with self.transaction() as connection:
            connection.execute(
                "INSERT INTO catalog_overrides(kind, item_id, name, description, price, enabled, updated_utc) "
                "VALUES (?, ?, ?, ?, ?, ?, ?) "
                "ON CONFLICT(kind, item_id) DO UPDATE SET "
                "name = COALESCE(excluded.name, catalog_overrides.name), "
                "description = COALESCE(excluded.description, catalog_overrides.description), "
                "price = COALESCE(excluded.price, catalog_overrides.price), "
                "enabled = COALESCE(excluded.enabled, catalog_overrides.enabled), "
                "updated_utc = excluded.updated_utc",
                (
                    kind,
                    item_id,
                    clean_name,
                    clean_description,
                    clean_price,
                    clean_enabled,
                    iso_utc(),
                ),
            )
        if kind == "package":
            package = self.package_by_id(item_id)
            assert package is not None
            return package.catalog_dict()
        product = self.product_by_id(item_id)
        assert product is not None
        return product.catalog_dict()

    # ---- account and token helpers -----------------------------------------------------

    def _device_hash(self, device_key: str) -> str:
        return hmac.new(
            self._secret, device_key.encode("utf-8"), hashlib.sha256
        ).hexdigest()

    def derive_secret(self, purpose: str) -> bytes:
        """Domain-separated key material for sibling services (accounts, OTP, sync)."""

        return hmac.new(
            self._secret, purpose.encode("utf-8"), hashlib.sha256
        ).digest()

    def create_account(self, display_name: str) -> str:
        """Create a server wallet with no device binding. Used by account sign-up.

        A synthetic device hash keeps the legacy unique column satisfied while making it
        impossible to authenticate as the account through the guest endpoint.
        """

        account_id = generate_account_id()
        created = iso_utc()
        with self.transaction() as connection:
            connection.execute(
                "INSERT INTO accounts(id, device_key_hash, display_name, gems, created_utc) "
                "VALUES (?, ?, ?, 0, ?)",
                (
                    account_id,
                    self._device_hash("account:" + account_id),
                    display_name,
                    created,
                ),
            )
        return account_id

    def rename_account(self, account_id: str, display_name: str) -> None:
        with self.transaction() as connection:
            cursor = connection.execute(
                "UPDATE accounts SET display_name = ? WHERE id = ?",
                (display_name, account_id),
            )
            if cursor.rowcount == 0:
                raise AccountNotFound()

    def account_name(self, account_id: str) -> str:
        with self.connection() as connection:
            row = connection.execute(
                "SELECT display_name FROM accounts WHERE id = ?", (account_id,)
            ).fetchone()
        if row is None:
            raise AccountNotFound()
        return str(row["display_name"])

    def revoke_auth_token(self, token: str) -> None:
        with self.transaction() as connection:
            connection.execute(
                "DELETE FROM auth_tokens WHERE token_hash = ?",
                (sha256_hex(token),),
            )

    def revoke_all_auth_tokens(self, account_id: str) -> None:
        with self.transaction() as connection:
            connection.execute(
                "DELETE FROM auth_tokens WHERE account_id = ?", (account_id,)
            )

    def _issue_auth_token(
        self, connection: sqlite3.Connection, account_id: str, ttl_seconds: int
    ) -> Tuple[str, str]:
        token = generate_token()
        expires = iso_utc(offset_seconds=int(ttl_seconds))
        connection.execute(
            "INSERT INTO auth_tokens(token_hash, account_id, expires_utc, created_utc) "
            "VALUES (?, ?, ?, ?)",
            (sha256_hex(token), account_id, expires, iso_utc()),
        )
        return token, expires

    def issue_auth_token(self, account_id: str, ttl_seconds: int) -> str:
        """Issue a token for an existing account. Primarily useful for diagnostics/tests."""

        with self.transaction() as connection:
            row = connection.execute(
                "SELECT id FROM accounts WHERE id = ?", (account_id,)
            ).fetchone()
            if row is None:
                raise AccountNotFound()
            token, _ = self._issue_auth_token(connection, account_id, ttl_seconds)
        return token

    def account_for_token(self, token: str) -> Dict[str, Any]:
        token_hash = sha256_hex(token)
        with self.connection() as connection:
            row = connection.execute(
                "SELECT a.id, a.display_name, a.gems FROM auth_tokens t "
                "JOIN accounts a ON a.id = t.account_id "
                "WHERE t.token_hash = ? AND t.expires_utc > ?",
                (token_hash, iso_utc()),
            ).fetchone()
        if row is None:
            raise AccountNotFound()
        return {
            "accountId": str(row["id"]),
            "displayName": str(row["display_name"]),
            "gems": int(row["gems"]),
        }

    def authenticate_guest(
        self, device_key: str, display_name: str, ttl_seconds: int
    ) -> Dict[str, Any]:
        """Return a stable account for a device, issuing a fresh bearer token."""

        device_hash = self._device_hash(device_key)
        created = iso_utc()
        with self.transaction() as connection:
            connection.execute(
                "DELETE FROM auth_tokens WHERE expires_utc <= ?", (created,)
            )
            row = connection.execute(
                "SELECT id, display_name, gems FROM accounts WHERE device_key_hash = ?",
                (device_hash,),
            ).fetchone()
            if row is None:
                account_id = generate_account_id()
                connection.execute(
                    "INSERT INTO accounts(id, device_key_hash, display_name, gems, created_utc) "
                    "VALUES (?, ?, ?, 0, ?)",
                    (account_id, device_hash, display_name, created),
                )
                gems = 0
                stored_name = display_name
            else:
                account_id = str(row["id"])
                gems = int(row["gems"])
                stored_name = display_name
                connection.execute(
                    "UPDATE accounts SET display_name = ? WHERE id = ?",
                    (display_name, account_id),
                )
            token, _ = self._issue_auth_token(connection, account_id, ttl_seconds)
        return {
            "token": token,
            "accountId": account_id,
            "displayName": stored_name,
            "gems": gems,
        }

    # ---- wallet and ownership ---------------------------------------------------------

    def wallet(self, account_id: str) -> Dict[str, int]:
        placeholders = ",".join("?" for _ in PENDING_ORDER_STATUSES)
        with self.connection() as connection:
            row = connection.execute(
                "SELECT a.gems AS gems, "
                "(SELECT COUNT(*) FROM orders o WHERE o.account_id = a.id "
                "AND o.status IN (" + placeholders + ")) AS pending "
                "FROM accounts a WHERE a.id = ?",
                (*PENDING_ORDER_STATUSES, account_id),
            ).fetchone()
        if row is None:
            raise AccountNotFound()
        return {"gems": int(row["gems"]), "pendingOrders": int(row["pending"])}

    def ownership(self, account_id: str) -> Dict[str, Any]:
        with self.connection() as connection:
            rows = connection.execute(
                "SELECT product_id, category, level, acquired_utc FROM ownership "
                "WHERE account_id = ? ORDER BY acquired_utc DESC, product_id ASC",
                (account_id,),
            ).fetchall()
        items: List[Dict[str, Any]] = []
        owned_ids = set()
        for row in rows:
            product_id = str(row["product_id"])
            owned_ids.add(product_id)
            items.append(
                {
                    "productId": product_id,
                    "category": str(row["category"]),
                    "level": int(row["level"]),
                    "acquiredUtc": str(row["acquired_utc"]),
                }
            )
        characters = [
            product.character_id
            for product in PREMIUM_PRODUCTS
            if product.category == "character"
            and product.character_id
            and product.id in owned_ids
        ]
        return {"items": items, "characters": characters}

    # ---- order serialisation ----------------------------------------------------------

    @staticmethod
    def order_public(row: sqlite3.Row) -> Dict[str, Any]:
        package = PACKAGES_BY_ID.get(str(row["package_id"]))
        return {
            "orderId": str(row["id"]),
            "packageId": str(row["package_id"]),
            "packageName": package.name if package is not None else str(row["package_id"]),
            "gems": int(row["gems"]),
            "bonusGems": int(row["bonus_gems"]),
            "amountLkr": int(row["amount_lkr"]),
            "currency": str(row["currency"]),
            "status": str(row["status"]),
            "reference": str(row["reference"]),
            "createdUtc": str(row["created_utc"]),
            "expiresUtc": str(row["expires_utc"]),
            "updatedUtc": str(row["updated_utc"]),
            "proofSubmitted": bool(row["proof_submitted_utc"] or row["proof_path"]),
            "adminNote": str(row["admin_note"] or ""),
        }

    @staticmethod
    def proof_metadata(row: sqlite3.Row) -> Optional[Dict[str, Any]]:
        if not row["proof_path"]:
            return None
        return {
            "originalFilename": str(row["proof_original_name"] or ""),
            "mime": str(row["proof_mime"] or ""),
            "sizeBytes": int(row["proof_size"] or 0),
            "sha256": str(row["proof_sha256"] or ""),
            "submittedUtc": str(row["proof_submitted_utc"] or ""),
        }

    def _admin_order_dict(self, row: sqlite3.Row) -> Dict[str, Any]:
        payload = self.order_public(row)
        payload["accountId"] = str(row["account_id"])
        payload["displayName"] = str(row["display_name"])
        payload["proof"] = self.proof_metadata(row)
        return payload

    @staticmethod
    def _idempotent_response(
        connection: sqlite3.Connection, account_id: str, key: str, scope: str
    ) -> Optional[Dict[str, Any]]:
        row = connection.execute(
            "SELECT response_json FROM idempotency "
            "WHERE account_id = ? AND key = ? AND scope = ?",
            (account_id, key, scope),
        ).fetchone()
        if row is None:
            return None
        try:
            decoded = json.loads(str(row["response_json"]))
        except (TypeError, ValueError):
            return None
        return decoded if isinstance(decoded, dict) else None

    @staticmethod
    def _save_idempotency(
        connection: sqlite3.Connection,
        account_id: str,
        key: str,
        scope: str,
        payload: Dict[str, Any],
    ) -> None:
        connection.execute(
            "INSERT INTO idempotency("
            "account_id, key, scope, response_json, created_utc) "
            "VALUES (?, ?, ?, ?, ?)",
            (account_id, key, scope, compact_json(payload), iso_utc()),
        )

    def _order_row(
        self, connection: sqlite3.Connection, order_id: str
    ) -> Optional[sqlite3.Row]:
        return connection.execute(
            "SELECT o.*, a.display_name AS display_name FROM orders o "
            "JOIN accounts a ON a.id = o.account_id WHERE o.id = ?",
            (order_id,),
        ).fetchone()

    def _own_order_row(
        self, connection: sqlite3.Connection, account_id: str, order_id: str
    ) -> Optional[sqlite3.Row]:
        return connection.execute(
            "SELECT o.*, a.display_name AS display_name FROM orders o "
            "JOIN accounts a ON a.id = o.account_id "
            "WHERE o.id = ? AND o.account_id = ?",
            (order_id, account_id),
        ).fetchone()

    # ---- order creation and retrieval -------------------------------------------------

    def create_order(
        self, account_id: str, package_id: str, client_request_id: str
    ) -> Tuple[Dict[str, Any], Dict[str, str]]:
        package = self.package_by_id(package_id)
        if package is None:
            raise PackageNotFound()
        if not package.enabled:
            raise PackageDisabled()
        scope = "order:" + package.id
        with self.transaction() as connection:
            existing = self._idempotent_response(
                connection, account_id, client_request_id, scope
            )
            if existing is not None:
                existing_order = existing.get("order")
                stored_id = ""
                if isinstance(existing_order, dict):
                    stored_id = str(existing_order.get("orderId") or "")
                if stored_id:
                    row = self._own_order_row(connection, account_id, stored_id)
                    if row is not None:
                        return self.order_public(row), dict(BANK_DETAILS)
                inner = existing.get("order")
                if isinstance(inner, dict):
                    return dict(inner), dict(BANK_DETAILS)

            created = iso_utc()
            expires = iso_utc(offset_seconds=ORDER_LIFETIME_SECONDS)
            order_id = ""
            for _ in range(8):
                candidate = generate_order_id()
                present = connection.execute(
                    "SELECT 1 FROM orders WHERE id = ?", (candidate,)
                ).fetchone()
                if present is None:
                    order_id = candidate
                    break
            if not order_id:
                raise PremiumError(
                    500, "ORDER_ID_EXHAUSTED", "ORDER_ID_EXHAUSTED"
                )
            connection.execute(
                "INSERT INTO orders("
                "id, account_id, package_id, gems, bonus_gems, amount_lkr, currency, "
                "status, reference, created_utc, updated_utc, expires_utc) "
                "VALUES (?, ?, ?, ?, ?, ?, ?, 'PENDING_PAYMENT', ?, ?, ?, ?)",
                (
                    order_id,
                    account_id,
                    package.id,
                    package.gems,
                    package.bonus_gems,
                    package.price_lkr,
                    CURRENCY,
                    order_id,
                    created,
                    created,
                    expires,
                ),
            )
            connection.execute(
                "INSERT INTO order_events(order_id, event, detail, created_utc) "
                "VALUES (?, 'created', ?, ?)",
                (
                    order_id,
                    compact_json(
                        {
                            "packageId": package.id,
                            "amountLkr": package.price_lkr,
                            "totalGems": package.total_gems,
                        }
                    ),
                    created,
                ),
            )
            row = self._order_row(connection, order_id)
            if row is None:  # pragma: no cover - insert just succeeded
                raise PremiumError(500, "ORDER_WRITE_FAILED", "ORDER_WRITE_FAILED")
            order = self.order_public(row)
            response = {"ok": True, "order": order, "bank": dict(BANK_DETAILS)}
            self._save_idempotency(
                connection, account_id, client_request_id, scope, response
            )
        return order, dict(BANK_DETAILS)

    def list_orders(self, account_id: str) -> List[Dict[str, Any]]:
        with self.connection() as connection:
            rows = connection.execute(
                "SELECT o.*, a.display_name AS display_name FROM orders o "
                "JOIN accounts a ON a.id = o.account_id "
                "WHERE o.account_id = ? ORDER BY o.rowid DESC",
                (account_id,),
            ).fetchall()
        return [self.order_public(row) for row in rows]

    def get_order(self, account_id: str, order_id: str) -> Dict[str, Any]:
        with self.connection() as connection:
            row = self._own_order_row(connection, account_id, order_id)
        if row is None:
            raise OrderNotFound()
        return self.order_public(row)

    # ---- proof submission and retrieval ----------------------------------------------

    def submit_proof(
        self,
        account_id: str,
        order_id: str,
        data: bytes,
        declared_mime: str,
        original_filename: str,
        note: str,
    ) -> Tuple[Dict[str, Any], Optional[pathlib.Path]]:
        detected = detect_proof_mime(data)
        if detected is None:
            raise PremiumError(
                415, "UNSUPPORTED_PROOF_TYPE", "UNSUPPORTED_PROOF_TYPE"
            )
        if declared_mime and declared_mime != detected:
            raise PremiumError(
                415, "UNSUPPORTED_PROOF_TYPE", "UNSUPPORTED_PROOF_TYPE"
            )
        extension = PROOF_EXTENSIONS[detected]
        filename = "{0}-{1}.{2}".format(
            order_id, secrets.token_hex(8), extension
        )
        path = self.proofs_dir / filename
        descriptor = os.open(
            str(path), os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600
        )
        try:
            with os.fdopen(descriptor, "wb") as handle:
                handle.write(data)
                handle.flush()
                os.fsync(handle.fileno())
            self._chmod_private(path, 0o600)
        except BaseException:
            with contextlib.suppress(OSError):
                path.unlink()
            raise

        sha256 = hashlib.sha256(data).hexdigest()
        submitted = iso_utc()
        old_path: Optional[pathlib.Path] = None
        try:
            with self.transaction() as connection:
                row = self._own_order_row(connection, account_id, order_id)
                if row is None:
                    raise OrderNotFound()
                status = str(row["status"])
                if status not in PENDING_ORDER_STATUSES:
                    raise OrderNotOpenForProof()
                if row["proof_path"]:
                    old_path = self.proofs_dir / pathlib.Path(
                        str(row["proof_path"])
                    ).name
                connection.execute(
                    "UPDATE orders SET status = 'PENDING_VERIFICATION', "
                    "proof_path = ?, proof_mime = ?, proof_sha256 = ?, "
                    "proof_size = ?, proof_original_name = ?, "
                    "proof_submitted_utc = ?, updated_utc = ? WHERE id = ?",
                    (
                        filename,
                        detected,
                        sha256,
                        len(data),
                        original_filename,
                        submitted,
                        submitted,
                        order_id,
                    ),
                )
                connection.execute(
                    "INSERT INTO order_events(order_id, event, detail, created_utc) "
                    "VALUES (?, 'proof_submitted', ?, ?)",
                    (
                        order_id,
                        compact_json(
                            {
                                "mime": detected,
                                "sizeBytes": len(data),
                                "sha256": sha256,
                                "replaced": old_path is not None,
                                "note": note[:500],
                            }
                        ),
                        submitted,
                    ),
                )
                updated = self._own_order_row(connection, account_id, order_id)
                if updated is None:  # pragma: no cover - row just updated
                    raise OrderNotFound()
                order = self.order_public(updated)
        except BaseException:
            with contextlib.suppress(OSError):
                path.unlink()
            raise
        return order, old_path

    def admin_proof(
        self, order_id: str
    ) -> Tuple[pathlib.Path, str, str]:
        with self.connection() as connection:
            row = connection.execute(
                "SELECT proof_path, proof_mime FROM orders WHERE id = ?",
                (order_id,),
            ).fetchone()
        if row is None:
            raise OrderNotFound()
        if not row["proof_path"]:
            raise ProofNotFound()
        filename = pathlib.Path(str(row["proof_path"])).name
        path = self.proofs_dir / filename
        try:
            resolved = path.resolve()
            resolved.relative_to(self.proofs_dir.resolve())
        except (OSError, ValueError) as exc:
            raise ProofNotFound() from exc
        if not resolved.is_file():
            raise ProofNotFound()
        mime = str(row["proof_mime"] or "")
        if mime not in PROOF_EXTENSIONS:
            raise ProofNotFound()
        extension = PROOF_EXTENSIONS[mime]
        return resolved, mime, "{0}.{1}".format(order_id, extension)

    # ---- admin order views ------------------------------------------------------------

    def admin_list_orders(self, status: Optional[str]) -> List[Dict[str, Any]]:
        query = (
            "SELECT o.*, a.display_name AS display_name FROM orders o "
            "JOIN accounts a ON a.id = o.account_id"
        )
        parameters: Tuple[Any, ...] = ()
        if status:
            query += " WHERE o.status = ?"
            parameters = (status,)
        query += " ORDER BY o.rowid DESC"
        with self.connection() as connection:
            rows = connection.execute(query, parameters).fetchall()
        return [self._admin_order_dict(row) for row in rows]

    # ---- purchases --------------------------------------------------------------------

    def purchase(
        self, account_id: str, product_id: str, client_request_id: str
    ) -> Dict[str, Any]:
        product = self.product_by_id(product_id)
        if product is None:
            raise ProductNotFound()
        if not product.enabled:
            raise ProductDisabled()
        if product.category == "upgrade" and product.max_level <= 0:
            raise PremiumError(
                500, "INVALID_PRODUCT_CONFIGURATION", "INVALID_PRODUCT_CONFIGURATION"
            )
        scope = "purchase:" + product.id
        with self.transaction() as connection:
            existing = self._idempotent_response(
                connection, account_id, client_request_id, scope
            )
            if existing is not None:
                return existing

            account = connection.execute(
                "SELECT gems FROM accounts WHERE id = ?", (account_id,)
            ).fetchone()
            if account is None:
                raise AccountNotFound()
            gems = int(account["gems"])
            owned = connection.execute(
                "SELECT level FROM ownership WHERE account_id = ? AND product_id = ?",
                (account_id, product.id),
            ).fetchone()

            if product.category == "upgrade":
                if owned is not None and int(owned["level"]) >= product.max_level:
                    raise MaximumLevelReached(int(owned["level"]))
                level = int(owned["level"]) + 1 if owned is not None else 1
            else:
                if owned is not None:
                    raise ProductAlreadyOwned()
                level = 0

            price = product.price_gems
            if gems < price:
                raise InsufficientGems(gems)

            balance = gems - price
            connection.execute(
                "UPDATE accounts SET gems = ? WHERE id = ?", (balance, account_id)
            )
            acquired = iso_utc()
            if owned is None:
                connection.execute(
                    "INSERT INTO ownership("
                    "account_id, product_id, category, level, acquired_utc) "
                    "VALUES (?, ?, ?, ?, ?)",
                    (account_id, product.id, product.category, level, acquired),
                )
            else:
                connection.execute(
                    "UPDATE ownership SET level = ? "
                    "WHERE account_id = ? AND product_id = ?",
                    (level, account_id, product.id),
                )
            note = product.name if product.category != "upgrade" else "{0} level {1}".format(
                product.name, level
            )
            connection.execute(
                "INSERT INTO ledger("
                "account_id, order_id, product_id, delta_gems, balance_after, "
                "kind, note, created_utc) VALUES (?, NULL, ?, ?, ?, 'purchase', ?, ?)",
                (account_id, product.id, -price, balance, note, acquired),
            )
            response = {
                "ok": True,
                "gems": balance,
                "productId": product.id,
                "owned": True,
                "level": level,
            }
            self._save_idempotency(
                connection, account_id, client_request_id, scope, response
            )
        return response

    # ---- provider credits, entitlements and refunds ----------------------------------

    def credit_gems(
        self,
        account_id: str,
        gems: int,
        kind: str,
        note: str,
        order_id: Optional[str] = None,
        product_id: Optional[str] = None,
    ) -> int:
        """Credit a verified provider payment. Callers must enforce idempotency first."""

        if not isinstance(gems, int) or isinstance(gems, bool) or gems <= 0:
            raise ValueError("gems must be a positive integer")
        created = iso_utc()
        with self.transaction() as connection:
            account = connection.execute(
                "SELECT gems FROM accounts WHERE id = ?", (account_id,)
            ).fetchone()
            if account is None:
                raise AccountNotFound()
            balance = int(account["gems"]) + int(gems)
            connection.execute(
                "UPDATE accounts SET gems = ? WHERE id = ?", (balance, account_id)
            )
            connection.execute(
                "INSERT INTO ledger("
                "account_id, order_id, product_id, delta_gems, balance_after, "
                "kind, note, created_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                (
                    account_id,
                    order_id,
                    product_id,
                    int(gems),
                    balance,
                    str(kind)[:32],
                    str(note)[:500],
                    created,
                ),
            )
        return balance

    def debit_gems_for_refund(
        self,
        account_id: str,
        gems: int,
        note: str,
        order_id: Optional[str] = None,
    ) -> int:
        """Reverse GEMS for a refunded provider payment, flooring the wallet at zero.

        A negative balance is never stored. Any shortfall is written to the ledger note so
        an operator can reconcile a partially spent refund manually.
        """

        if not isinstance(gems, int) or isinstance(gems, bool) or gems <= 0:
            raise ValueError("gems must be a positive integer")
        created = iso_utc()
        with self.transaction() as connection:
            account = connection.execute(
                "SELECT gems FROM accounts WHERE id = ?", (account_id,)
            ).fetchone()
            if account is None:
                raise AccountNotFound()
            current = int(account["gems"])
            applied = min(current, int(gems))
            balance = current - applied
            connection.execute(
                "UPDATE accounts SET gems = ? WHERE id = ?", (balance, account_id)
            )
            suffix = "" if applied == int(gems) else " (shortfall {0})".format(int(gems) - applied)
            connection.execute(
                "INSERT INTO ledger("
                "account_id, order_id, product_id, delta_gems, balance_after, "
                "kind, note, created_utc) VALUES (?, ?, NULL, ?, ?, 'refund', ?, ?)",
                (
                    account_id,
                    order_id,
                    -applied,
                    balance,
                    (str(note)[:480] + suffix),
                    created,
                ),
            )
        return balance

    # ---- in-run revival ---------------------------------------------------------------

    def revive(
        self,
        account_id: str,
        session_id: str,
        distance: float,
        location_index: int,
        client_request_id: str,
    ) -> Dict[str, Any]:
        scope = "revive"
        with self.transaction() as connection:
            existing = self._idempotent_response(
                connection, account_id, client_request_id, scope
            )
            if existing is not None:
                return existing

            account = connection.execute(
                "SELECT gems FROM accounts WHERE id = ?", (account_id,)
            ).fetchone()
            if account is None:
                raise AccountNotFound()
            gems = int(account["gems"])
            if gems < REVIVE_COST_GEMS:
                raise InsufficientGems(gems)

            balance = gems - REVIVE_COST_GEMS
            token = "rv_" + secrets.token_urlsafe(18)
            created = iso_utc()
            connection.execute(
                "UPDATE accounts SET gems = ? WHERE id = ?", (balance, account_id)
            )
            connection.execute(
                "INSERT INTO revive_events("
                "account_id, session_id, distance, location_index, cost_gems, "
                "token, created_utc) VALUES (?, ?, ?, ?, ?, ?, ?)",
                (
                    account_id,
                    session_id,
                    float(distance),
                    int(location_index),
                    REVIVE_COST_GEMS,
                    token,
                    created,
                ),
            )
            note = "session {0} distance {1:.1f} index {2}".format(
                session_id[:64], float(distance), int(location_index)
            )
            connection.execute(
                "INSERT INTO ledger("
                "account_id, order_id, product_id, delta_gems, balance_after, "
                "kind, note, created_utc) VALUES (?, NULL, NULL, ?, ?, 'revive', ?, ?)",
                (
                    account_id,
                    -REVIVE_COST_GEMS,
                    balance,
                    note[:500],
                    created,
                ),
            )
            response = {
                "ok": True,
                "gems": balance,
                "reviveToken": token,
                "costGems": REVIVE_COST_GEMS,
            }
            self._save_idempotency(
                connection, account_id, client_request_id, scope, response
            )
        return response

    # ---- admin decisions and reporting ------------------------------------------------

    def decide_order(
        self,
        order_id: str,
        approve: bool,
        note: str,
        admin_username: str,
        ip_address: str,
    ) -> Tuple[Dict[str, Any], int]:
        created = iso_utc()
        action = "approve_order" if approve else "reject_order"
        with self.transaction() as connection:
            row = self._order_row(connection, order_id)
            if row is None:
                raise OrderNotFound()
            status = str(row["status"])
            if status in DECIDED_ORDER_STATUSES:
                raise OrderAlreadyDecided()
            if status != "PENDING_VERIFICATION":
                raise OrderNotAwaitingVerification()

            new_status = "APPROVED" if approve else "REJECTED"
            connection.execute(
                "UPDATE orders SET status = ?, admin_note = ?, decided_utc = ?, "
                "updated_utc = ? WHERE id = ?",
                (new_status, note[:500], created, created, order_id),
            )
            if approve:
                total_gems = int(row["gems"]) + int(row["bonus_gems"])
                connection.execute(
                    "UPDATE accounts SET gems = gems + ? WHERE id = ?",
                    (total_gems, str(row["account_id"])),
                )
                account = connection.execute(
                    "SELECT gems FROM accounts WHERE id = ?",
                    (str(row["account_id"]),),
                ).fetchone()
                if account is None:  # pragma: no cover - foreign key guarantees this
                    raise AccountNotFound()
                balance = int(account["gems"])
                ledger_note = note[:500] or "Order {0} approved".format(order_id)
                connection.execute(
                    "INSERT INTO ledger("
                    "account_id, order_id, product_id, delta_gems, balance_after, "
                    "kind, note, created_utc) "
                    "VALUES (?, ?, NULL, ?, ?, 'credit', ?, ?)",
                    (
                        str(row["account_id"]),
                        order_id,
                        total_gems,
                        balance,
                        ledger_note,
                        created,
                    ),
                )
            else:
                account = connection.execute(
                    "SELECT gems FROM accounts WHERE id = ?",
                    (str(row["account_id"]),),
                ).fetchone()
                if account is None:  # pragma: no cover - foreign key guarantees this
                    raise AccountNotFound()
                balance = int(account["gems"])

            connection.execute(
                "INSERT INTO order_events(order_id, event, detail, created_utc) "
                "VALUES (?, ?, ?, ?)",
                (
                    order_id,
                    new_status.lower(),
                    compact_json(
                        {
                            "decision": new_status,
                            "note": note[:500],
                            "admin": admin_username,
                            "creditedGems": (
                                int(row["gems"]) + int(row["bonus_gems"])
                                if approve
                                else 0
                            ),
                        }
                    ),
                    created,
                ),
            )
            connection.execute(
                "INSERT INTO admin_audit("
                "admin_username, action, order_id, detail, ip_address, created_utc) "
                "VALUES (?, ?, ?, ?, ?, ?)",
                (
                    admin_username,
                    action,
                    order_id,
                    note[:500],
                    ip_address[:64],
                    created,
                ),
            )
            updated = self._order_row(connection, order_id)
            if updated is None:  # pragma: no cover - row just updated
                raise OrderNotFound()
            order = self.order_public(updated)
        return order, balance

    def ledger(self, limit: int) -> List[Dict[str, Any]]:
        with self.connection() as connection:
            rows = connection.execute(
                "SELECT id, account_id, order_id, product_id, delta_gems, "
                "balance_after, kind, note, created_utc FROM ledger "
                "ORDER BY id DESC LIMIT ?",
                (int(limit),),
            ).fetchall()
        return [
            {
                "id": int(row["id"]),
                "accountId": str(row["account_id"]),
                "orderId": row["order_id"],
                "productId": row["product_id"],
                "deltaGems": int(row["delta_gems"]),
                "balanceAfter": int(row["balance_after"]),
                "kind": str(row["kind"]),
                "note": str(row["note"]),
                "createdUtc": str(row["created_utc"]),
            }
            for row in rows
        ]

    def stats(self) -> Dict[str, Any]:
        counts = {status: 0 for status in ORDER_STATUSES}
        with self.connection() as connection:
            for row in connection.execute(
                "SELECT status, COUNT(*) AS total FROM orders GROUP BY status"
            ).fetchall():
                if str(row["status"]) in counts:
                    counts[str(row["status"])] = int(row["total"])
            credit = connection.execute(
                "SELECT COALESCE(SUM(delta_gems), 0) AS gems, COUNT(*) AS entries "
                "FROM ledger WHERE kind = 'credit'"
            ).fetchone()
            pending = connection.execute(
                "SELECT COUNT(*) AS total FROM orders "
                "WHERE status IN ('PENDING_PAYMENT', 'PENDING_VERIFICATION')"
            ).fetchone()
            accounts = connection.execute(
                "SELECT COUNT(*) AS total FROM accounts"
            ).fetchone()
        return {
            "counts": counts,
            "pendingCount": int(pending["total"]) if pending is not None else 0,
            "totalCredits": int(credit["gems"]) if credit is not None else 0,
            "creditEntries": int(credit["entries"]) if credit is not None else 0,
            "accountCount": int(accounts["total"]) if accounts is not None else 0,
        }

    # ---- admin tokens and audit -------------------------------------------------------

    def create_admin_token(
        self, username: str, ttl_seconds: int
    ) -> Tuple[str, str]:
        token = generate_token()
        expires = iso_utc(offset_seconds=int(ttl_seconds))
        created = iso_utc()
        with self.transaction() as connection:
            connection.execute(
                "DELETE FROM admin_tokens WHERE expires_utc <= ?", (created,)
            )
            connection.execute(
                "INSERT INTO admin_tokens("
                "token_hash, username, expires_utc, created_utc) VALUES (?, ?, ?, ?)",
                (sha256_hex(token), username, expires, created),
            )
        return token, expires

    def admin_for_token(self, token: str) -> str:
        token_hash = sha256_hex(token)
        with self.connection() as connection:
            row = connection.execute(
                "SELECT username FROM admin_tokens "
                "WHERE token_hash = ? AND expires_utc > ?",
                (token_hash, iso_utc()),
            ).fetchone()
        if row is None:
            raise PremiumError(401, "ADMIN_UNAUTHORIZED", "ADMIN_UNAUTHORIZED")
        return str(row["username"])

    def record_admin_audit(
        self,
        username: str,
        action: str,
        order_id: Optional[str],
        detail: str,
        ip_address: str,
    ) -> None:
        with self.transaction() as connection:
            connection.execute(
                "INSERT INTO admin_audit("
                "admin_username, action, order_id, detail, ip_address, created_utc) "
                "VALUES (?, ?, ?, ?, ?, ?)",
                (
                    username[:80],
                    action[:80],
                    order_id,
                    detail[:500],
                    ip_address[:64],
                    iso_utc(),
                ),
            )
