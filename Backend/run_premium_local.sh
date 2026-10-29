#!/usr/bin/env bash
# Local development launcher for the NEON VELOCITY premium service.
#
# Admin credentials are read from the environment. The service binds to loopback and stores
# its SQLite database, device-key secret and proof files under Backend/premium_data.
set -euo pipefail
umask 077

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

if [[ -z "${NV_ADMIN_USERNAME:-}" ]]; then
    echo "NV_ADMIN_USERNAME must be set." >&2
    echo "Example: export NV_ADMIN_USERNAME=operator" >&2
    exit 1
fi

if [[ -z "${NV_ADMIN_PASSWORD:-}" && -z "${NV_ADMIN_PASSWORD_HASH:-}" ]]; then
    echo "Set NV_ADMIN_PASSWORD or NV_ADMIN_PASSWORD_HASH before starting." >&2
    echo "Generate a verifier with: python3 premium_admin.py hash-password" >&2
    exit 1
fi

PORT="${NV_PREMIUM_PORT:-8088}"
DATA_DIR="${NV_PREMIUM_DATA_DIR:-$SCRIPT_DIR/premium_data}"

exec python3 "$SCRIPT_DIR/premium_server.py" \
    --host 127.0.0.1 \
    --port "$PORT" \
    --data-dir "$DATA_DIR"
