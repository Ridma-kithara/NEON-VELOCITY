#!/usr/bin/env python3
"""Minimal dependency-free RSA helpers for provider service-account auth.

Only what Google Play verification needs is implemented: parse an RSA private key from
PKCS#1 or PKCS#8 PEM and produce an RSASSA-PKCS1-v1_5/SHA-256 signature. No secret ever
leaves the server process, and the implementation refuses malformed or tiny keys.
"""

from __future__ import annotations

import base64
import hashlib
import re
from typing import Tuple

SHA256_DIGEST_INFO = bytes.fromhex("3031300d060960864801650304020105000420")
MIN_MODULUS_BITS = 2048


def _read_der_length(data: bytes, offset: int) -> Tuple[int, int]:
    if offset >= len(data):
        raise ValueError("truncated DER length")
    first = data[offset]
    offset += 1
    if first < 0x80:
        return first, offset
    count = first & 0x7F
    if count == 0 or count > 4 or offset + count > len(data):
        raise ValueError("invalid DER length")
    length = int.from_bytes(data[offset : offset + count], "big")
    return length, offset + count


def _read_der_element(data: bytes, offset: int) -> Tuple[int, bytes, int]:
    if offset >= len(data):
        raise ValueError("truncated DER element")
    tag = data[offset]
    length, position = _read_der_length(data, offset + 1)
    end = position + length
    if end > len(data):
        raise ValueError("truncated DER value")
    return tag, data[position:end], end


def _der_sequence(data: bytes) -> Tuple[bytes, ...]:
    tag, body, end = _read_der_element(data, 0)
    if tag != 0x30 or end != len(data):
        raise ValueError("expected a DER sequence")
    items = []
    offset = 0
    while offset < len(body):
        item_tag, item_body, next_offset = _read_der_element(body, offset)
        if item_tag != 0x02:
            raise ValueError("expected a DER integer")
        items.append(item_body)
        offset = next_offset
    return tuple(items)


def _as_int(value: bytes) -> int:
    return int.from_bytes(value, "big")


def _decode_pem(pem: str) -> Tuple[str, bytes]:
    match = re.search(
        r"-----BEGIN (RSA )?PRIVATE KEY-----(.*?)-----END (RSA )?PRIVATE KEY-----",
        pem,
        re.DOTALL,
    )
    if not match:
        raise ValueError("no RSA private key PEM block found")
    label = "PRIVATE KEY" if match.group(1) else "PKCS8"
    body = re.sub(r"\s+", "", match.group(2) or "")
    return label, base64.b64decode(body)


def load_rsa_private_key(pem: str) -> Tuple[int, int, int]:
    """Return ``(modulus, public_exponent, private_exponent)`` from a PEM key."""

    label, der = _decode_pem(pem)
    if label == "PKCS8":
        # PrivateKeyInfo ::= SEQUENCE { version INTEGER, algorithm, privateKey OCTET STRING }
        tag, body, end = _read_der_element(der, 0)
        if tag != 0x30 or end != len(der):
            raise ValueError("invalid PKCS#8 key")
        _version_tag, _version, position = _read_der_element(body, 0)
        _alg_tag, _alg, position = _read_der_element(body, position)
        octet_tag, octet, _ = _read_der_element(body, position)
        if octet_tag != 0x04:
            raise ValueError("invalid PKCS#8 private key payload")
        der = octet
    items = _der_sequence(der)
    # RSAPrivateKey ::= SEQUENCE { version, n, e, d, p, q, ... }
    if len(items) < 3:
        raise ValueError("invalid RSA private key")
    modulus = _as_int(items[1])
    exponent = _as_int(items[2])
    private_exponent = _as_int(items[3])
    if modulus.bit_length() < MIN_MODULUS_BITS:
        raise ValueError("RSA key is too small")
    if exponent < 3 or exponent % 2 == 0 or private_exponent <= 1:
        raise ValueError("invalid RSA exponents")
    return modulus, exponent, private_exponent


def sign_rs256(pem: str, message: bytes) -> bytes:
    """Sign ``message`` with RSASSA-PKCS1-v1_5/SHA-256, returning raw bytes."""

    modulus, _public, private = load_rsa_private_key(pem)
    size = (modulus.bit_length() + 7) // 8
    digest = hashlib.sha256(message).digest()
    padding = b"\xff" * (size - len(SHA256_DIGEST_INFO) - len(digest) - 3)
    if len(padding) < 8:
        raise ValueError("RSA key is too small for SHA-256")
    encoded = b"\x00\x01" + padding + b"\x00" + SHA256_DIGEST_INFO + digest
    signature = pow(int.from_bytes(encoded, "big"), private, modulus)
    return signature.to_bytes(size, "big")
