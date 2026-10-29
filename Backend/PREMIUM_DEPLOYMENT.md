# NEON VELOCITY Premium Backend Deployment Guide

## Deployment status

The premium backend is implemented and covered by local integration tests. **It has not been
deployed to a public host and no real card, App Store or Play Store transaction has been
completed.** Checkout opening is not treated as success: GEMS are credited only after the
server verifies a provider webhook or store receipt.

The Unity client is implemented. It signs in, syncs progression, opens the official Apple or
Google store sheet on mobile, opens Stripe-hosted checkout on desktop/web, displays wallet and
transaction history, buys premium products, requests exact-position revives and fails closed
with **STORE OFFLINE** when the service is absent. Live HTTPS traffic from a shipped client has
not been exercised.

The service is dependency-free Python 3.9+ and uses only the standard library. The normal
purchase paths are:

| Platform | Checkout | Server verification |
| --- | --- | --- |
| iOS | Apple In-App Purchase through Unity IAP | App Store receipt verification with the shared secret |
| Android | Google Play Billing through Unity IAP | Android Publisher API purchase lookup with a service account |
| Desktop / web | Stripe-hosted Checkout in the system browser | Signed Stripe webhook |
| Legacy operator opt-in | Manual bank transfer (off by default) | Human review of bank settlement |

No client ever supplies a price, balance, currency or "paid" flag. The manual bank-transfer
flow is retained only for historical orders and is gated by
`NV_ENABLE_MANUAL_BANK_TRANSFER=1`; it is not the recommended purchase path.

## Files

| File | Purpose |
| --- | --- |
| `Backend/premium_server.py` | Threaded HTTP API, CORS, validation, rate limiting, admin auth, account and payment routes |
| `Backend/premium_store.py` | Catalogue, five GEM tiers, SQLite schema, wallet/ownership/ledger transactions and catalogue overrides |
| `Backend/account_store.py` | Email/password, phone OTP, verification, recovery, guest linking, profile, cloud sync, transactions and admin account review |
| `Backend/payments.py` | Stripe Checkout/webhook/refund, Apple receipt adapter, Google Play verifier, receipts |
| `Backend/identity.py` | Google ID-token verification and Apple JWKS/RS256 identity-token verification |
| `Backend/rsa_keys.py` | Pure-Python RSA key parsing and RS256 signing helpers |
| `Backend/mailer.py` | Optional SMTP verification/receipt delivery and SMS webhook adapter |
| `Backend/premium_admin.py` | Secure operator CLI for transactions, accounts, catalogue, refunds and legacy orders |
| `Backend/tests/test_premium_server.py` | End-to-end premium/account/payment/admin integration tests |
| `Backend/tests/test_phase03_identity.py` | Apple JWKS token and Google service-account verifier unit tests |
| `Backend/tests/test_premium_admin.py` | Operator CLI integration tests |
| `Backend/run_premium_local.sh` | Loopback-only local development launcher |
| `Backend/.gitignore` | Ignores `premium_data/`, `__pycache__/` and bytecode |

The race authority in `Backend/server.py` is a separate service and is unchanged.

## Data files

The data directory contains:

| File | Contents |
| --- | --- |
| `premium.sqlite3` | Wallets, catalogue overrides, ownership, ledger, payment transactions, legacy orders and admin audit |
| `accounts.sqlite3` | Auth identities, email/phone verification state, profiles and cloud-sync snapshots |
| `premium_secret.key` | HMAC pepper for stable guest device lookup |
| `proofs/` | Legacy bank-transfer proof files, if that flow is deliberately enabled |

Both SQLite files live in the same directory because the admin account view attaches the
wallet database to join identities with balances. Keep them together on one filesystem.

## Catalogue

These values live on the server. Clients cannot supply prices, balances or package contents.

| Field | Value |
| --- | --- |
| Default revive cost | `40 GEMS` |
| Default display currency | `LKR` for the reference catalogue; Stripe currency is configurable |

GEM packages:

| ID | Tier | Name | Gems | Bonus | Total | Reference LKR |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| `gems_80` | STARTER | STARTER CACHE | 80 | 0 | 80 | 490 |
| `gems_200` | BASIC | BASIC CACHE | 200 | 10 | 210 | 1190 |
| `gems_500` | PREMIUM | PREMIUM CACHE | 500 | 50 | 550 | 2890 |
| `gems_1200` | MEGA | MEGA CACHE | 1200 | 180 | 1380 | 6490 |
| `gems_2500` | ULTIMATE | ULTIMATE CACHE | 2500 | 500 | 3000 | 12900 |

Package amounts and defaults can be set at process start with:

```text
NV_GEM_PACKAGE_<ID>_GEMS
NV_GEM_PACKAGE_<ID>_BONUS_GEMS
NV_GEM_PACKAGE_<ID>_PRICE_LKR
NV_GEM_PACKAGE_<ID>_NAME
NV_GEM_PACKAGE_<ID>_DESCRIPTION
NV_GEM_PACKAGE_<ID>_ENABLED
```

`<ID>` is the upper-case package id, for example `NV_GEM_PACKAGE_GEMS_2500_PRICE_LKR=13900`.
Invalid values fall back to the built-in default. Restart the service after changing them.
The admin catalogue can change name, description, reference price and enabled state, but
**GEM counts are intentionally immutable through the admin route** so a price edit cannot
silently change what a purchase grants.

Premium products include five runners (`char_phantom_x`, `char_nova_titan`, `char_echo`,
`char_sable`, `char_helios`), skins, armour, helmets, trail/ability effects, animation sets,
revive tokens and permanent upgrades. The effective catalogue is `GET /api/v1/store/catalog`;
operator changes are returned by `GET /api/v1/admin/catalog`.

## Requirements

* Python 3.9 or newer. No `pip install` step is required.
* A dedicated unprivileged service account.
* A persistent local filesystem for SQLite, WAL files and the secret.
* An HTTPS reverse proxy such as Caddy or nginx. The Python process speaks plain HTTP and
  must bind to loopback in production.
* A single service process per data directory. SQLite is single-host storage and the
  in-memory rate limiters are not shared between processes.
* A backup target outside the host and an encrypted backup policy.
* A Stripe account, Apple developer account or Google Play developer account as applicable.

## Server command line

```text
python3 Backend/premium_server.py \
  --host 127.0.0.1 \
  --port 8088 \
  --data-dir /var/lib/neon-premium \
  [--init-only] \
  [--log-level INFO]
```

`--init-only` creates the directories and database schema, then exits without binding a port.
Defaults are `127.0.0.1:8088` and `Backend/premium_data`. Do not bind to `0.0.0.0` on a host
with a public interface; let the reverse proxy reach loopback.

## Environment variables

### Server process

| Variable | Default | Meaning |
| --- | --- | --- |
| `NV_PREMIUM_HOST` | `127.0.0.1` | Bind address unless `--host` is supplied |
| `NV_PREMIUM_PORT` | `8088` | Port unless `--port` is supplied |
| `NV_PREMIUM_DATA_DIR` | `Backend/premium_data` | SQLite, secret and legacy proof root |
| `NV_ADMIN_USERNAME` | empty | Admin login name; admin API is disabled until set with a secret |
| `NV_ADMIN_PASSWORD` | empty | Plaintext admin password fallback |
| `NV_ADMIN_PASSWORD_HASH` | empty | Preferred PBKDF2 verifier; takes precedence over the plaintext password |
| `NV_ADMIN_TOKEN_TTL_SECONDS` | `1800` | Admin bearer lifetime in seconds |
| `NV_AUTH_TOKEN_TTL_SECONDS` | `2592000` | Account/guest bearer lifetime in seconds |
| `NV_MAX_PROOF_BYTES` | `8388608` | Maximum decoded legacy proof size (8 MiB) |
| `NV_ALLOWED_ORIGIN` | `*` | CORS origin, or comma-separated origins; tighten for browser builds |
| `NV_NOTIFY_WEBHOOK` | empty | Optional JSON webhook for legacy proof submissions |
| `NV_GUEST_RATE_LIMIT` | `60` | Guest logins per IP per window |
| `NV_ADMIN_LOGIN_RATE_LIMIT` | `5` | Failed admin logins per IP per window |
| `NV_AUTH_RATE_LIMIT` | `30` | Account auth/OTP attempts per IP per window |
| `NV_RATE_WINDOW_SECONDS` | `60` | Rate-limit window |
| `NV_TRUST_PROXY_HEADERS` | `false` | Trust the first `X-Forwarded-For` address |
| `NV_AUTH_DEV_MODE` | `false` | Return verification/reset/OTP codes in API responses; local development only |
| `NV_LOG_LEVEL` | `INFO` | `DEBUG`, `INFO`, `WARNING` or `ERROR` |

### Identity and delivery

| Variable | Default | Meaning |
| --- | --- | --- |
| `NV_GOOGLE_CLIENT_ID` | empty | Google OAuth client id used to verify Google ID tokens |
| `NV_APPLE_CLIENT_ID` | empty | Apple service id/audience used to verify Apple identity tokens |
| `NV_SMTP_HOST` | empty | SMTP host; email delivery reports `EMAIL_NOT_CONFIGURED` until set with a sender |
| `NV_SMTP_PORT` | `587` | SMTP port |
| `NV_SMTP_USERNAME` | empty | Optional SMTP username |
| `NV_SMTP_PASSWORD` | empty | Optional SMTP password |
| `NV_SMTP_FROM` | empty | Verified sender shown on verification and receipt mail |
| `NV_SMTP_STARTTLS` | `true` | Upgrade a plain SMTP connection with STARTTLS |
| `NV_SMTP_SSL` | `false` | Use implicit TLS (normally with port 465) |
| `NV_SMTP_TIMEOUT` | `12` | SMTP socket timeout in seconds |
| `NV_SMS_WEBHOOK` | empty | HTTPS gateway accepting `{"to", "body"}`; OTP reports `SMS_NOT_CONFIGURED` until set |
| `NV_SMS_WEBHOOK_TOKEN` | empty | Optional bearer token for the SMS gateway |
| `NV_SMS_TIMEOUT` | `10` | SMS gateway timeout in seconds |

### Payments

| Variable | Default | Meaning |
| --- | --- | --- |
| `NV_STRIPE_SECRET_KEY` | empty | Stripe server secret key; never placed in Unity |
| `NV_STRIPE_WEBHOOK_SECRET` | empty | Stripe signing secret for the raw webhook body |
| `NV_STRIPE_CURRENCY` | `usd` | Stripe Checkout currency |
| `NV_STRIPE_PRICE_MAP` | empty | JSON object mapping package ids to Stripe `price_...` ids |
| `NV_STRIPE_SUCCESS_URL` | empty | HTTPS post-payment return page |
| `NV_STRIPE_CANCEL_URL` | empty | HTTPS cancelled-payment return page |
| `NV_APPLE_SHARED_SECRET` | empty | App Store shared secret for receipt verification |
| `NV_APPLE_PRODUCT_MAP` | empty | Optional JSON alias map: store product id to package id |
| `NV_GOOGLE_PLAY_SERVICE_ACCOUNT` | empty | Path to the Play service-account JSON, or the JSON itself |
| `NV_GOOGLE_PLAY_PACKAGE_NAME` | empty | Android application id, for example `games.rkvantage.neonvelocity` |
| `NV_GOOGLE_PLAY_PRODUCT_MAP` | empty | Optional JSON alias map: store product id to package id |
| `NV_ENABLE_MANUAL_BANK_TRANSFER` | `false` | Enable the legacy manual bank-transfer routes and UI metadata |
| `NV_APPLE_CLIENT_ID` | empty | Apple identity-token audience (sign-in, not IAP) |
| `NV_AUTH_DEV_MODE` | `false` | Must remain false in production |

The JSON request body limit is fixed at 64 KiB. Proxy body limits must allow normal API
traffic and, only if the legacy proof flow is enabled, the configured proof size plus 1 MiB.

### Admin CLI

| Variable | Meaning |
| --- | --- |
| `NV_PREMIUM_URL` | Base URL, for example `https://premium.example.com` |
| `NV_ADMIN_USERNAME` | Required admin name |
| `NV_ADMIN_PASSWORD` | Optional; if absent the CLI prompts with `getpass` |
| `NV_CA_BUNDLE` | Optional CA file for a private TLS chain |
| `NV_ALLOW_INSECURE_HTTP` | Must be `1` to allow a remote plain-HTTP URL; localhost is always allowed |

The CLI never accepts a password as an argument, so passwords do not appear in shell history
or process listings.

## Secrets

Prefer a PBKDF2 verifier. Generate one interactively:

```bash
python3 Backend/premium_admin.py hash-password
```

The command prompts twice, prints a line shaped like
`pbkdf2_sha256$260000$<salt>$<digest>`, and tells you to store it as
`NV_ADMIN_PASSWORD_HASH`. It never prints the password.

Production secret handling:

1. Create `/etc/neon-premium.env`, owned by root and readable only by the service group.
2. Put `NV_ADMIN_USERNAME` and `NV_ADMIN_PASSWORD_HASH` in that file. Remove
   `NV_ADMIN_PASSWORD` once the hash works.
3. Inject provider secrets from a secret manager where available.
4. Do not put secrets in git, the Unity project, command-line arguments, screenshots or
   support tickets.
5. Rotate the admin password by generating a new verifier and restarting. Existing admin
   tokens remain valid until they expire (default 30 minutes). To revoke immediately, stop
   the service and run `DELETE FROM admin_tokens;` before restarting.
6. Back up `premium_secret.key`. Losing it breaks stable guest lookup for existing devices.

The admin password, bearer tokens and payment receipts are not written to application logs.
Admin and guest tokens are stored as SHA-256 hashes. Device keys are stored only as an
HMAC-SHA256 value keyed by `premium_secret.key`.

## File permissions

The service creates and tightens:

* data directory: `0700`
* legacy `proofs/` directory: `0700`
* SQLite databases and `premium_secret.key`: `0600`
* each legacy proof file: `0600`

Verify after first start:

```bash
sudo stat -c '%a %U:%G %n' /var/lib/neon-premium \
  /var/lib/neon-premium/premium.sqlite3 \
  /var/lib/neon-premium/accounts.sqlite3 \
  /var/lib/neon-premium/premium_secret.key
# macOS: use  stat -f '%Lp %Su:%Sg %N'  instead of stat -c
```

Never run the service as root. Do not place the data directory inside a web-server document
root or a synced consumer folder.

## Local development

```bash
cd Backend
export NV_ADMIN_USERNAME=localoperator
export NV_ADMIN_PASSWORD_HASH='pbkdf2_sha256$260000$...'
./run_premium_local.sh
```

Or start directly:

```bash
NV_ADMIN_USERNAME=localoperator \
NV_ADMIN_PASSWORD='local-only-password' \
python3 Backend/premium_server.py --host 127.0.0.1 --port 8088 \
  --data-dir Backend/premium_data
```

The launcher requires admin credentials, sets `umask 077`, binds only to `127.0.0.1`, and
uses `Backend/premium_data` unless `NV_PREMIUM_DATA_DIR` is set. Plaintext
`NV_ADMIN_PASSWORD` is acceptable only for a throwaway local database.

Health check:

```bash
curl -sS http://127.0.0.1:8088/api/v1/health
```

## Unity client configuration

- Default endpoint: `http://127.0.0.1:8088` (local development only).
- Production endpoint: set **Settings → Network → Premium Store URL** to the deployed
  **https://** base URL. The field validates absolute `http://`/`https://` URLs.
- The endpoint is stored in the local save. It is never used to trust prices, balances,
  ownership or receipt email; those come from server responses.
- Changing the endpoint clears the previous server's cached GEM balance, bearer token,
  catalogue, transaction cache and ownership before syncing.
- The client keeps no admin credential and exposes no approval, ledger or price-editing
  route. `NV_ADMIN_*` values stay on the server.
- Unity IAP is installed (`com.unity.purchasing` 4.15.1). The store products must use the
  backend package ids by default: `gems_80`, `gems_200`, `gems_500`, `gems_1200`, `gems_2500`.
  If store console rules require different ids, add an alias in `NV_APPLE_PRODUCT_MAP` or
  `NV_GOOGLE_PLAY_PRODUCT_MAP` **and** register that same store id in the client build.
- The shop reads `GET /api/v1/store/payments` and refuses to start a platform charge when the
  matching provider is not configured. It never fakes a successful purchase.
- To test locally without a store, leave the URL at loopback and confirm **STORE ONLINE**.
  Gameplay, CREDITS, local saves and secret-free GEM products still work; real-money purchase
  buttons should report the provider is not configured.
- A shipped build that leaves the loopback URL shows STORE OFFLINE on players' machines. Set
  the production URL before building.

## Admin CLI

```bash
export NV_PREMIUM_URL='https://premium.example.com'
export NV_ADMIN_USERNAME='operator'
# NV_ADMIN_PASSWORD unset: the CLI prompts with getpass.

# Phase 03 review tools
python3 Backend/premium_admin.py transactions --status SUCCEEDED --limit 50
python3 Backend/premium_admin.py transactions --provider stripe
python3 Backend/premium_admin.py accounts --query player@example.com
python3 Backend/premium_admin.py account acc_0123456789abcdef
python3 Backend/premium_admin.py catalog
python3 Backend/premium_admin.py set-package gems_80 --price 490 --enable
python3 Backend/premium_admin.py set-product skin_phantom_void --price 120 --disable
python3 Backend/premium_admin.py refund 42 --confirm --note 'provider refund issued'

# Legacy manual-transfer tools (only when explicitly enabled)
python3 Backend/premium_admin.py pending
python3 Backend/premium_admin.py orders --status PENDING_VERIFICATION
python3 Backend/premium_admin.py proof NV-20260101-ABCDEFGH --output ./review/
python3 Backend/premium_admin.py approve NV-20260101-ABCDEFGH --note 'Verified against bank statement'
python3 Backend/premium_admin.py reject NV-20260101-ABCDEFGH --note 'Transfer not found'
python3 Backend/premium_admin.py ledger --limit 100
python3 Backend/premium_admin.py stats
python3 Backend/premium_admin.py hash-password
```

`refund` requires `--confirm` and reverses GEMS only after the operator has issued the refund
at the provider. The game does not call a provider refund API. The response includes
`providerRefundRequired` so the operator knows the provider-side action is still needed.

## Admin HTTP routes

All admin routes except login require `Authorization: Bearer <admin token>`. The server
dispatches GET, POST and OPTIONS only; mutations use POST.

| Route | Purpose |
| --- | --- |
| `POST /api/v1/admin/login` | Exchange admin credentials for a short-lived token |
| `GET /api/v1/admin/transactions?provider=&status=&limit=` | Review payment transactions |
| `GET /api/v1/admin/accounts?q=&limit=` | Search account id, player name, email or phone |
| `GET /api/v1/admin/accounts/{id}` | Identities, wallet and recent transactions |
| `GET /api/v1/admin/catalog` | Effective packages and products |
| `POST /api/v1/admin/catalog/packages/{id}` | Change name, description, reference price or enabled state |
| `POST /api/v1/admin/catalog/products/{id}` | Change product name, description, price or enabled state |
| `POST /api/v1/admin/transactions/{id}/refund` | Reverse a verified transaction after provider refund |
| `GET /api/v1/admin/orders`, `stats`, `ledger` | Legacy order and ledger review |

Admin credentials are never present in the Unity client. Put `/api/v1/admin/` behind a VPN,
IP allowlist or identity-aware proxy in addition to the bearer token.

## HTTPS reverse proxy

Terminate TLS at Caddy or nginx. Bind the Python process to `127.0.0.1`. Example Caddy site:

```caddyfile
premium.example.com {
    encode zstd gzip
    reverse_proxy 127.0.0.1:8088
}
```

Equivalent nginx fragment:

```nginx
server {
    listen 443 ssl http2;
    server_name premium.example.com;

    ssl_certificate     /etc/letsencrypt/live/premium.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/premium.example.com/privkey.pem;

    client_max_body_size 1m;
    proxy_read_timeout   60s;

    add_header Strict-Transport-Security "max-age=31536000" always;

    location / {
        proxy_pass http://127.0.0.1:8088;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        # The service reads the first address. Overwrite client input instead of appending it.
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

Set `NV_TRUST_PROXY_HEADERS=1` only when the reverse proxy is the sole way to reach the port
and overwrites the incoming `X-Forwarded-For` header with the real client address. With the
setting off, all requests appear to come from the proxy and share one rate-limit bucket.

Network controls:

* Firewall the public interface; allow only 443 (and 80 for ACME redirects if used).
* Deny inbound 8088 from everywhere except loopback or the proxy host.
* Use modern TLS; do not enable obsolete protocol versions.
* Put `/api/v1/admin/` behind a VPN, IP allowlist or identity-aware proxy.
* Do not enable `NV_ALLOW_INSECURE_HTTP` in production.

CORS note: the default `NV_ALLOWED_ORIGIN=*` is permissive because a native Unity client and
`file://` builds are expected. For a browser-hosted build, set exact origins instead. Native
clients do not enforce CORS, but browsers do.

## systemd example

```ini
[Unit]
Description=NEON VELOCITY premium GEM service
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=neonpremium
Group=neonpremium
EnvironmentFile=/etc/neon-premium.env
ExecStart=/usr/bin/python3 /opt/neon-velocity/Backend/premium_server.py \
  --host 127.0.0.1 --port 8088 --data-dir /var/lib/neon-premium
Restart=on-failure
RestartSec=3
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/var/lib/neon-premium
UMask=0077

[Install]
WantedBy=multi-user.target
```

Run `systemctl daemon-reload` after changes. Use `journalctl -u neon-premium` for logs and
confirm the log does not contain tokens, passwords or receipt contents.

## Backups

Back up both databases, the secret and the environment/secret-manager export. A safe online
SQLite backup uses the SQLite backup API, not a raw `cp` of a live database. Stop the service
for a fully consistent pair if that is operationally simpler.

```bash
install -d -m 0700 /var/backups/neon-premium
sudo -u neonpremium python3 - <<'PY'
import datetime
import pathlib
import sqlite3

stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ')
for name in ('premium', 'accounts'):
    source = pathlib.Path('/var/lib/neon-premium', name + '.sqlite3')
    target = pathlib.Path('/var/backups/neon-premium', name + '-' + stamp + '.sqlite3')
    with sqlite3.connect(str(source)) as src, sqlite3.connect(str(target)) as dst:
        src.backup(dst)
    target.chmod(0o600)
    print(target)
PY

sudo tar -C /var/lib/neon-premium -czf \
  /var/backups/neon-premium/secret.tar.gz premium_secret.key
sudo chmod 0600 /var/backups/neon-premium/secret.tar.gz
```

Include the reverse-proxy configuration and TLS automation inventory. Encrypt backups at
rest, copy them off-host, restrict access and test a restore on a separate host. The ledger is
the accounting trail; do not edit it by hand.

Restore procedure:

1. Stop the service.
2. Restore both database snapshots and the secret file.
3. `chown -R neonpremium:neonpremium /var/lib/neon-premium`.
4. Set directories to `0700` and files to `0600`.
5. Start the service and check `/api/v1/health`.
6. Compare admin transactions and the ledger with provider records before enabling purchases.

## Payment providers

### Capability gating

`GET /api/v1/store/payments` returns:

```json
{
  "ok": true,
  "payments": {
    "stripe": false,
    "apple": false,
    "googlePlay": false,
    "manualBankTransfer": false,
    "appleProductMap": {},
    "googlePlayProductMap": {"gems_80": "gems_80"},
    "stripeCurrency": "USD"
  }
}
```

The client uses this to decide whether a real-money button is available. A provider is
configured only when all of its server verification inputs exist. Store products must still be
created in the provider console before the store sheet can open.

### Stripe Checkout (card)

Card payments require a signed-in account. The Unity client never sees the Stripe secret key,
card data or CVV; it calls `POST /api/v1/payments/checkout` with `packageId`,
`clientRequestId`, optional `receiptEmail` and optional return URLs. The server creates the
Checkout Session against server-held price ids and returns the hosted
`https://checkout.stripe.com/...` URL for the system browser.

Required server configuration:

```bash
NV_STRIPE_SECRET_KEY=sk_live_...
NV_STRIPE_WEBHOOK_SECRET=whsec_...
NV_STRIPE_CURRENCY=usd
NV_STRIPE_PRICE_MAP='{"gems_80":"price_...","gems_200":"price_..."}'
NV_STRIPE_SUCCESS_URL=https://game.example.com/purchase/success
NV_STRIPE_CANCEL_URL=https://game.example.com/purchase/cancel
```

The webhook endpoint is `POST /api/v1/payments/stripe/webhook` and must receive the **raw**
request body. Configure the Stripe endpoint to send at least
`checkout.session.completed`, `checkout.session.async_payment_succeeded`,
`checkout.session.async_payment_failed`, `checkout.session.expired` and `charge.refunded`.
The server verifies the timestamped `Stripe-Signature` HMAC, rejects events outside the
tolerance window, requires `payment_status=paid`, copies account/package from signed metadata,
and credits GEMS exactly once keyed by the Stripe event id. Duplicate webhooks are
acknowledged without a second credit. A full `charge.refunded` reverses the credited GEMS;
partial refunds are recorded without reversing the whole credit.

### Apple In-App Purchase

The Unity IAP adapter opens the App Store sheet and forwards the base64 receipt to
`POST /api/v1/payments/apple`. The server calls Apple's verify endpoint, falls back to the
sandbox endpoint on status `21007`, checks the product id, records the Apple transaction id
idempotently and credits once. Required server configuration:

```bash
NV_APPLE_SHARED_SECRET=...
# Optional alias map. With no map, the store product id must equal the package id.
NV_APPLE_PRODUCT_MAP='{"gems_80":"gems_80"}'
```

Create App Store products using the package ids by default (`gems_80`, `gems_200`, ...), or
register the alias ids in both the store and the client. Use a sandbox tester account and a
device build. The server adapter alone cannot process a purchase.

### Google Play Billing

`POST /api/v1/payments/google` verifies the purchase token with the Android Publisher API. The
server obtains and caches an OAuth access token from the supplied service account, checks the
purchase state, product id and order id, and credits once. Required configuration:

```bash
NV_GOOGLE_PLAY_SERVICE_ACCOUNT=/secure/play-service-account.json
NV_GOOGLE_PLAY_PACKAGE_NAME=games.rkvantage.neonvelocity
# Optional alias map. With no map, the store product id must equal the package id.
NV_GOOGLE_PLAY_PRODUCT_MAP='{"gems_200":"gems_200"}'
```

Grant the service account access to the Play Console app, create the products with the package
ids, and test with a licence-testing account on a device build. `purchaseState` must be
purchased (`0`); pending or cancelled purchases are rejected.

### Provider truth and client claims

Only a signed webhook or a server-verified store receipt can credit GEMS. The client cannot
submit a balance, a "successful" flag, a price or a currency. A verified duplicate is
acknowledged without a second credit. A failed verification leaves the platform purchase
pending so Unity IAP can replay it on the next launch; it is never converted into a local GEM
balance.

## Receipts

A receipt is sent only after a verified purchase has been credited. The client defaults the
destination to the signed-in account's verified email; the player can save a different
receipt email in the account screen and the choice is remembered. The account UI warns when
the saved address does not match the verified account email.

Receipt contents:

* game name (`NEON VELOCITY`);
* player/account id;
* transaction id and provider reference;
* item/package name and GEMS credited;
* amount and currency;
* provider;
* date and status.

No card number, CVV, token or store secret is included. Delivery requires
`NV_SMTP_HOST` and `NV_SMTP_FROM`. If SMTP is absent, the transaction still records the
verified payment and the response reports that the receipt could not be emailed. Nothing is
emailed before settlement.

## Refunds

Refund policy and execution belong to the merchant. The backend provides wallet reversal after
the provider-side refund is confirmed:

* Stripe full refunds arrive as `charge.refunded` and reverse the credited GEMS through the
  ledger.
* Apple/Google do not send a wallet-reversal webhook to this service. The operator must issue
  the refund in the store console, then run
  `premium_admin.py refund <transactionId> --confirm --note '...'`.
* A refund fails if the transaction is not `SUCCEEDED`/`PARTIALLY_REFUNDED`, if it credited no
  GEMS, or if the wallet cannot absorb the reversal.
* The response sets `providerRefundRequired` when the operator still needs to act at the
  provider. The game never executes the provider refund itself.

Record the provider refund id in the operator note and reconcile the ledger against the
provider settlement report.

## Accounts, identity and cloud sync

The same server owns identity and the premium wallet. Account sessions use the same
bearer-token authority as guest wallets, but account routes never silently fall back to a new
guest identity.

| Route | Purpose |
| --- | --- |
| `POST /api/v1/auth/register` | Email/password registration with display name |
| `POST /api/v1/auth/login` | Email/password sign-in |
| `POST /api/v1/auth/logout` | Revoke the current bearer token |
| `POST /api/v1/auth/link` | Attach email/password to a guest wallet without losing progression or GEMS |
| `POST /api/v1/auth/email/verify` | Consume an email verification code |
| `POST /api/v1/auth/password/request` | Issue a one-time password-reset code |
| `POST /api/v1/auth/password/reset` | Consume the reset code and revoke all sessions |
| `POST /api/v1/auth/phone/start` | Start phone OTP through the configured SMS gateway |
| `POST /api/v1/auth/phone/verify` | Verify OTP and create/restore a phone account |
| `POST /api/v1/auth/oauth` | Google/Apple token sign-in when the matching verifier is configured |
| `GET/POST /api/v1/account/profile` | Read or update display name and receipt email |
| `POST /api/v1/account/email` | Request an account email change and verification |
| `GET/POST /api/v1/account/sync` | Fetch or merge the cloud progression snapshot |
| `GET /api/v1/account/transactions` | Verified purchase transaction history |
| `GET /api/v1/account/identities` | Linked identity providers |

Passwords are stored as PBKDF2-SHA256 verifiers, not plaintext. One-time codes are stored as
SHA-256 hashes with expiry and single-use enforcement. Email verification, reset codes and
phone OTP are never returned by the API unless `NV_AUTH_DEV_MODE=true`; that flag is for a
local database only and must be off in production. Registration, login, OTP, verification and
reset routes share the `NV_AUTH_RATE_LIMIT` limiter.

Google ID tokens are verified against Google's tokeninfo endpoint for the configured client
id. Apple identity tokens are verified against Apple's JWKS with pure-Python RS256 and the
configured audience. The **Unity client does not include native Google or Apple sign-in
plugins** in this build: those buttons explain the boundary and do not fabricate a session.
Email, phone OTP and guest modes are usable now.

Cloud sync deliberately omits GEM balances, bearer tokens, payment transactions and premium
ownership; those remain premium-server state. The merge is monotonic for progression (higher
values win, keyed collections merge, reward ids union), uses `baseRevision` and an idempotency
key, rejects oversized snapshots and returns a conflict summary. The local save is not
cryptographically signed, so offline progression should not be treated as competitive
anti-cheat evidence. The server clamps implausible movement and distance values before
accepting a snapshot.

Offline play never requires an account. The account screen and GEM buy path explain when an
account is needed for purchases, receipts or cloud sync, and a guest can link credentials
later without discarding local progression.

## Legacy bank workflow

This flow is **off by default** and is not part of the normal shop. If
`NV_ENABLE_MANUAL_BANK_TRANSFER=1` is set, the historical order/proof routes remain available
for reconciliation:

1. The player selects a package and the server creates an exact-amount order.
2. The player transfers the amount and uploads a PNG/JPEG/WebP/PDF proof.
3. The operator verifies settlement against the bank statement and approves or rejects the
   order in `premium_admin.py`.
4. Approval credits GEMS exactly once and writes an audit/ledger record.

Proof upload is evidence, not settlement. No bank API, automatic reconciliation or payout
automation exists. The player-facing GEM SHOP no longer displays account numbers, order
references or proof upload. Do not re-enable this flow for a public build unless store policy
and the operator's banking process explicitly permit it.

## Monitoring and operations

* Poll `/api/v1/health` from an internal monitor.
* Alert on repeated 5xx, admin login rate limits, provider verification failures, database/WAL
  disk use, receipt delivery failures and backup failure.
* Review the admin transaction list and ledger daily; reconcile GEMS credits with provider
  settlements.
* Watch for duplicate provider references, repeated verification failures from one account,
  unusual refund volume and mismatched amounts.
* Rotate admin credentials and provider keys on staff changes. Remove environment-file and
  proxy-allowlist access at the same time.
* Keep Python, SQLite, OpenSSL, Unity IAP and the reverse proxy patched.
* The optional notify webhook is fire-and-forget with a short timeout; it is informational and
  not an approval channel.

## Running the tests

From the repository root:

```bash
python3 -m unittest discover -s Backend/tests -p 'test_*.py' -v
```

The backend suite boots the real threaded server on an ephemeral loopback port with a
temporary data directory for each test. It covers health, guest stability, catalogue
integrity, package env overrides, admin authentication/rate limiting, catalogue overrides,
admin transactions/accounts/refunds, idempotent wallet credit/debit, purchase/revive,
insufficient balances, ledger/stats, cross-account isolation, expired tokens, malformed JSON,
CORS, account registration/login/verification/reset/OTP/linking, profile and receipt email,
conflict-safe cloud sync, Stripe signed webhooks and duplicate events, Apple receipt
verification, Google service-account verification, receipt construction, duplicate purchases,
admin CLI commands and provider-disabled responses. No test contacts a real Stripe, Apple,
Google, SMTP or SMS provider.

Expected total: **79 tests** (plus the separate race-authority suite).

## Known limitations

* No live provider transaction has been completed. Stripe, Apple and Google verification are
  exercised with stubs/unit keys in this environment.
* Apple and Google IAP require real store products, merchant accounts, a device build and
  sandbox/licence-testing accounts. They are integrated through Unity IAP but not device-tested.
* Native Google/Apple **sign-in** plugins are absent; server token verification alone cannot
  mint a provider session in the Unity client.
* Receipts require an SMTP provider and verified sending domain. No live delivery or
  deliverability test was run.
* Phone OTP requires an SMS webhook. No live SMS delivery test was run.
* Apple/Google refunds are not auto-reversed by a webhook; an operator must run the admin
  refund after the store console shows the refund.
* Stripe partial refunds are recorded but do not reverse a proportional GEM amount. Full
  refunds reverse the full credit.
* No tax, accounting, payout, chargeback-dispute or regulatory compliance automation.
* SQLite is single-host storage. Run one process per data directory; do not scale horizontally
  behind a load balancer.
* Rate limits are in-memory and reset on restart.
* Guest identity is a device key, not hardware attestation. Protect devices and use short
  bearer lifetimes where appropriate.
* Admin auth is a single shared username/password with no MFA or role separation. Put the admin
  namespace behind a VPN/allowlist and use a strong unique password.
* The local save is not cryptographically signed; premium balances and entitlements remain
  server-authoritative, but offline progression is not competitive anti-cheat evidence.
* Legacy proof type checking uses magic bytes only. Files are not antivirus-scanned, encrypted
  at rest or rendered in a hardened document service.
* CORS defaults to `*`. Tighten `NV_ALLOWED_ORIGIN` for browser-hosted clients.
* The built-in HTTP server has no TLS. HTTPS, HSTS, certificate renewal and request-size
  limits are the reverse proxy's responsibility.
* No admin web UI, MFA, bulk actions, CSV export, dispute portal or account-deletion tooling.
* No automatic device-account merge or recovery if `premium_secret.key` is lost.
* The service is not a tax, accounting or regulatory compliance system.

## Required external steps before production use

1. Provision a host, domain and DNS record; create a dedicated service account.
2. Obtain and automate TLS certificates; deploy the Caddy/nginx reverse proxy.
3. Create `/etc/neon-premium.env` with a strong admin username and PBKDF2 verifier; restrict
   its permissions.
4. Copy this `Backend/` directory to the host, initialize `/var/lib/neon-premium`, and confirm
   directory/file modes.
5. Start the service under systemd, verify loopback binding, health, firewall rules and
   external HTTPS access.
6. Configure backups for both SQLite databases, the device-key secret and the environment
   secret, then perform a restore drill.
7. Configure monitoring, log retention, alerting and the optional webhook.
8. For card payments, create a Stripe account, price ids for all five packages, a webhook
   endpoint with the signing secret, and HTTPS success/cancel pages. Set `NV_STRIPE_*` values
   only on the server. Run a test-mode purchase, verify the signed webhook credit, receipt,
   duplicate delivery and a full refund before going live.
9. For App Store purchases, create the products with the package ids, generate the shared
   secret, configure `NV_APPLE_SHARED_SECRET`/`NV_APPLE_PRODUCT_MAP`, build an iOS development
   client with Unity IAP, sign in with a sandbox tester and verify one purchase and duplicate.
10. For Google Play purchases, create the products, create a service account with Play Console
    access, set `NV_GOOGLE_PLAY_SERVICE_ACCOUNT`/`NV_GOOGLE_PLAY_PACKAGE_NAME`/product map,
    build an Android development client, use a licence-testing account and verify one purchase
    and duplicate.
11. For email, configure `NV_SMTP_*` with a verified sending domain and test verification,
    password reset and receipt delivery. For phone OTP, deploy an SMS gateway
    (`NV_SMS_WEBHOOK`) and test delivery, expiry and rate limits.
12. For Google/Apple sign-in, add native provider plugins, create the OAuth client/service id,
    set `NV_GOOGLE_CLIENT_ID`/`NV_APPLE_CLIENT_ID` and test token verification end to end.
13. Set the deployed HTTPS base URL in **Settings → Network → Premium Store URL**, then
    exercise STORE OFFLINE, catalogue, one sandbox purchase per provider, duplicate replay,
    receipt delivery, a refund, transaction history, ownership and an exact-position revive
    from the Unity client. A live end-to-end client test has not been run here.
14. Only after all of the above, enable purchases for all players and reconcile the first
    settled transactions.
