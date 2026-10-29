# NEON VELOCITY — Phase 03 Final Report

**Project:** NEON VELOCITY  
**Studio:** RK VANTAGE GAMES  
**Engine:** Unity 6000.6.7f1 / URP 17.6 / Input System 1.20 / Unity IAP 4.15.1  
**Backend:** dependency-free Python 3.9+ HTTP + SQLite services  
**Date:** 29 October 2026  
**Verdict:** **Implemented and locally verified. Not production-live.** No real card, App Store or Google Play transaction has been completed in this environment.

---

## 1. Executive summary

Phase 03 replaced the previous placeholder/manual payment path with a real account, offline-save, server-wallet, official-store, receipt, refund and admin architecture. Existing Phase 01 gameplay and Phase 02 visuals were preserved; the shop and account UI were connected to the new services rather than redesigned from scratch.

| Area | State | Evidence |
| --- | --- | --- |
| Authentication | Implemented for email/password, phone OTP, guest, linking, verification, reset and logout. Google/Apple server verification implemented; native sign-in plugins absent. | Backend HTTP/account tests, Unity account screens |
| Offline play | Implemented and mandatory account-free. Endless, locations, COINS, XP, missions and local save/restart work without a session. | Headless core, PlayMode, manual boot |
| Save/sync | Local save plus conflict-safe monotonic cloud merge. GEMS and entitlements excluded from sync. | Account-store tests, EditMode save tests |
| Economy | COINS earned in gameplay; GEMS real-money only; server wallet/catalogue/ownership/ledger and atomic debits. | Premium HTTP tests, PlayMode revive |
| Provider integration | Stripe hosted checkout + signed webhook; Apple IAP and Google Play Billing through Unity IAP; server verification and idempotency. | Stub/generated-key tests, compile |
| Receipts | Generated after verified success; account/receipt email default; item, amount, transaction, provider, date and status. | Receipt construction tests |
| Shop | Server catalogue wired into BUY GEMS / PREMIUM STORE / TRANSACTION HISTORY. Retired bank-transfer UI removed. | PlayMode assertions, fresh 1280×800 capture |
| Revive | Exact death-state restore, one atomic GEM debit, invulnerability, NOT ENOUGH GEMS recovery actions. | PlayMode exact-revive test |
| Admin | Secure operator CLI and HTTP routes for transactions, accounts, catalogue, refunds and audit. | Admin/CLI tests |
| Live payments | **Not verified.** No credentials, store products, HTTPS host or real transaction were supplied. | See sections 10–11 |

The honest boundary is simple: the implementation and local verification are substantial, but **checkout opening is not a completed purchase** and no live sale is claimed.

---

## 2. Authentication

### Implemented player methods

- **Email and password** registration, sign-in, sign-out, session restore, email verification and password reset.
- **Phone OTP** start/verify through a configurable SMS webhook; codes are single-use, time-limited and attempt-capped.
- **Guest wallet** creation/restore from a local device key; guest progress can be linked to an email/password account without discarding local progression or GEMS.
- **Google and Apple identity verification** on the server: Google ID tokens against Google's token endpoint with audience/issuer/expiry checks; Apple identity tokens against Apple JWKS using pure-Python RS256.
- **Profile** display name, account email change request and a separate receipt email.
- **Logout** revokes the current bearer token. Password reset revokes existing sessions.
- Persistent bearer sessions with configurable lifetime.

### What is not in the client

The Unity build does **not** contain native Google Sign-In or Sign in with Apple plugins. The account screen says so explicitly and does not fabricate a provider session. The server endpoints and token verifiers are implemented and tested with generated keys; a native plugin and OAuth client/service configuration are still required for provider sign-in on device.

### Security properties

- Passwords are stored as PBKDF2-SHA256 verifiers, never plaintext.
- One-time codes are stored as SHA-256 hashes with expiry and single-use enforcement.
- Rate limiters cover authentication, OTP, admin login and guest creation.
- Account routes never silently fall back to a new guest identity.
- Development mode (`NV_AUTH_DEV_MODE`) returns codes for local testing only and must remain off in production.
- SMTP and SMS delivery are not faked. Without credentials the API reports the delivery boundary and the UI states that delivery is unavailable.

---

## 3. Offline play and save/sync

### Offline-first behaviour

- The game boots and plays without an account or network connection.
- Endless play, locations, COINS, XP, local missions, achievements, discoveries, settings and restart work offline.
- A local save persists XP, level, best and total distance, COINS, character/unlocks/items/upgrades, missions, achievements, discoveries and settings.
- Losing the premium service does not block offline play. It only disables server-owned GEMS, purchases and premium ownership.

### Cloud sync

- `GET/POST /api/v1/account/sync` exchanges a versioned progression snapshot with `baseRevision` and a client request id.
- Merge is monotonic for progression: higher scalar values win, keyed collections merge, reward ids are unioned, and a conflict summary is returned.
- The snapshot deliberately **excludes GEMS, premium ownership, payment transactions, bearer tokens and the server URL**. A tampered client cannot add GEMS through sync.
- Oversized, unsupported-schema or revision-ahead snapshots are rejected. Implausible movement/distance values are clamped server-side.
- Retries are bounded; duplicate submissions return the recorded result rather than granting twice.
- Changing the premium endpoint clears cached wallet, transaction history, token and entitlements before the next sync.

### Limitation

The local save is not cryptographically signed. Server authority protects GEMS, premium ownership and payments, but offline progression is not competitive anti-cheat evidence.

---

## 4. COIN/GEM economy

### Currency separation

- **COINS** (`profile.credits`) are the earned soft currency. They come from running, missions and normal progression and remain part of the local/synced profile.
- **GEMS** are the premium currency. They are **only** obtained by a verified real-money purchase. Gameplay, missions, XP, random rewards, offline play and cloud sync never mint GEMS.
- Premium runner purchases, premium products and revives debit GEMS through the server wallet. COIN-priced local cosmetics/upgrades continue to use earned credits.

### Server authority

- The server owns wallets, catalogue, product ownership, ledger, payment transactions and admin audit.
- The Unity cache (`app.Profile.premium.gems`) is display-only; `PremiumService` is the only client path that accepts a server balance.
- Prices, package contents, product prices and enabled state come from the server catalogue. The client cannot submit an amount, price or currency for credit.
- Purchases and revives require a client-generated idempotency key. Replaying the key returns the original result without a second debit/credit.
- Wallet updates and ownership grants run in SQLite transactions. Duplicate provider events/receipts are acknowledged without a second credit.
- Catalogue edits can change name, description, reference price and enabled state, but **GEM counts are immutable through the admin route**.

### Catalogue

Five GEM packages are served:

| ID | Name | GEMS | Bonus | Total | Reference LKR |
| --- | --- | ---: | ---: | ---: | ---: |
| `gems_80` | STARTER CACHE | 80 | 0 | 80 | 490 |
| `gems_200` | BASIC CACHE | 200 | 10 | 210 | 1,190 |
| `gems_500` | PREMIUM CACHE | 500 | 50 | 550 | 2,890 |
| `gems_1200` | MEGA CACHE | 1,200 | 180 | 1,380 | 6,490 |
| `gems_2500` | ULTIMATE CACHE | 2,500 | 500 | 3,000 | 12,900 |

The premium product catalogue covers characters, skins, armour, helmets, trail/ability effects, movement effects, animations, abilities, revival and permanent upgrades. Normal shop cosmetics/upgrades use COINS; premium catalogue items use GEMS; real money buys GEMS. This gives the intended mixed COIN/GEM/real-money shop without letting GEMS leak into gameplay rewards.

Reference LKR prices are display/reference values. Stripe uses server price ids and the provider's checkout currency; Apple/Google use their store price tiers. The final provider price is shown by the store/browser before payment.

---

## 5. Payment providers

### Architecture

| Platform | Checkout surface | Server verification |
| --- | --- | --- |
| iOS | Apple In-App Purchase sheet through Unity IAP 4.15.1 | App Store receipt verification with shared secret; sandbox fallback |
| Android | Google Play Billing sheet through Unity IAP 4.15.1 | Android Publisher API purchase lookup with a service account |
| Desktop / web | Stripe-hosted Checkout in the system browser | Signed Stripe webhook |
| Legacy operator opt-in | Manual bank transfer, disabled by default | Human reconciliation only |

### What the client sends

Only a package/product id, the provider receipt or purchase token, an optional receipt email and (for Stripe) optional return URLs. The client never sends a price, amount, currency, credited balance or "paid" flag.

### Capability gate

`GET /api/v1/store/payments` reports which providers are fully configured. The shop will not start a charge when the matching provider capability is false. This prevents charging a player when the server could not verify or credit the purchase.

### Stripe

- Server-held secret key, webhook secret, price map and HTTPS success/cancel URLs.
- Checkout Session is created against server-side price ids; card data stays in Stripe's hosted page.
- The raw signed webhook is verified with timestamp tolerance; `payment_status=paid` is required.
- Account/package metadata comes from the signed event, not the client.
- Credits are idempotent by Stripe event id. Duplicate webhooks do not double-credit.
- A full `charge.refunded` reverses the credited GEMS; partial refunds are recorded without a proportional GEM reversal.

### Apple In-App Purchase

- Unity IAP opens the App Store sheet and forwards the base64 receipt.
- The server calls Apple's verify endpoint, retries the sandbox endpoint for status `21007`, checks the product id and records the Apple transaction id idempotently.
- Store products must use the backend package ids (`gems_80`, `gems_200`, ...) unless matching alias maps are configured.
- A pending/failed verification leaves the store transaction replayable; it is never converted into a local GEM balance.

### Google Play Billing

- Unity IAP opens Google Play Billing and forwards the purchase token, product id and package name.
- The server obtains an OAuth access token from the supplied service account, calls the Android Publisher API, checks the purchase state/product/order id and credits once.
- `purchaseState` must be purchased; pending or cancelled purchases are rejected.
- Products must be created with the backend package ids unless alias maps are configured.

### Failure, cancel and duplicate handling

- Unconfigured provider: no charge is started; the UI says the provider is not configured.
- Failed verification: no GEMS are credited; the store transaction remains pending/replayable.
- Cancelled checkout: no credit; the wallet is unchanged.
- Duplicate webhook/receipt/purchase: acknowledged once, no second credit.
- Server unavailable: **STORE OFFLINE**; no fabricated balance or purchase.
- Provider-side refunds: Stripe full refunds auto-reverse; Apple/Google refunds must be issued in the store console and then reversed with the admin CLI.

### Critical status

**No live provider transaction was completed.** Stripe, Apple and Google tests use stubs/generated keys and local HTTP. No merchant account, store product, sandbox tester, device build or HTTPS host was supplied. The integration is prepared, not proven live.

---

## 6. Receipts

- A receipt is generated only after a verified, credited purchase.
- The destination defaults to the account's receipt email, which itself defaults to the account email. The player can change it; the account UI warns when it differs from the verified account email.
- Receipt contents: game name, player/account id, transaction id and provider reference, item/package and GEMS, amount and currency, provider, date and status.
- No card number, CVV, provider token or secret is included.
- Delivery requires SMTP (`NV_SMTP_HOST`, `NV_SMTP_FROM`, credentials as needed). Without SMTP the transaction still records the verified payment, and the response reports that the receipt could not be emailed. Nothing is emailed before settlement.
- Duplicate webhook/receipt handling does not send a second credit; receipt delivery is best-effort and recorded.

**Unverified:** live SMTP delivery, deliverability, bounce handling and receipt rendering in a real mail client. No sending domain was supplied.

---

## 7. Shop and revive

### Shop

The Phase 03 GEM SHOP has three tabs:

1. **BUY GEMS** — server packages, provider selection, price guidance, receipt email and capability state.
2. **PREMIUM STORE** — server catalogue of premium characters, skins, armour, helmets, effects, animations, abilities, revival and upgrades, priced in GEMS.
3. **TRANSACTION HISTORY** — verified purchase/refund records with status, provider, amount, reference and receipt email.

The old bank account number, exact-amount transfer instructions, proof upload and order-status UI were removed. The legacy routes are gated by `NV_ENABLE_MANUAL_BANK_TRANSFER=1` and default to `403 MANUAL_TRANSFER_DISABLED`.

A fresh 1280×800 capture of the rebuilt shop was produced from the final post-fix Phase 03 build and inspected (`Captures/phase03/1280x800/gem-shop-packages.png`). It confirms the new tabs, capability message, server wallet, package cards, receipt section and the shortened provider/receipt status lines; the label clipping found in the earlier capture is no longer present.

### Revive

- The session captures exact distance, lane, height, vertical velocity, slide state and speed at death.
- The revive button previews the pending result and cost.
- The server validates the run/session and debits the configured GEM cost exactly once using an idempotency key.
- On success the same run resumes at the captured state with a short invulnerability window; it does not rebuild the world or restart the run.
- Insufficient balance shows **NOT ENOUGH GEMS!** with **BUY GEMS / RESTART / CLOSE**. BUY GEMS opens the real shop. No local GEM mint or free revive exists.
- Leaving, restarting or quitting finalises the pending run exactly once.

---

## 8. Admin and security

### Operator CLI

`Backend/premium_admin.py` supports:

```text
transactions --provider --status --limit
accounts --query --limit
account <accountId>
catalog
set-package <id> --name/--description/--price/--enable/--disable
set-product <id> --name/--description/--price/--enable/--disable
refund <transactionId> --confirm --note
ledger --limit
stats
```

Credentials come from the environment or a `getpass` prompt; a password is never accepted as a command-line argument. Destructive/refund actions require `--confirm` and range/format validation. The CLI covers transaction review, account search/detail, effective catalogue inspection, catalogue overrides and GEM reversal after a provider refund.

### Admin HTTP routes

All admin routes require a short-lived bearer token from `POST /api/v1/admin/login`. The server dispatches GET/POST/OPTIONS only; mutations use POST.

| Route | Purpose |
| --- | --- |
| `/api/v1/admin/transactions` | Filter by provider/status, review payment records |
| `/api/v1/admin/accounts` | Search account id, player name, email or phone |
| `/api/v1/admin/accounts/{id}` | Identities, wallet and recent transactions |
| `/api/v1/admin/catalog` | Effective packages and products |
| `/api/v1/admin/catalog/packages/{id}` | Name/description/reference price/enabled override |
| `/api/v1/admin/catalog/products/{id}` | Name/description/price/enabled override |
| `/api/v1/admin/transactions/{id}/refund` | Reverse GEMS after provider confirmation |
| `/api/v1/admin/orders`, `/stats`, `/ledger` | Legacy order and ledger review |

### Security boundary

- No card number, CVV or provider secret is accepted by the Unity client or written to local saves.
- Stripe uses hosted checkout; Apple/Google use official store sheets through Unity IAP.
- GEMS are credited only by verified webhook/receipt. No local mint, client-supplied balance or gameplay award exists.
- Admin credentials are never in the client. Admin routes should additionally sit behind a VPN, IP allow-list or identity-aware proxy.
- Admin tokens are short-lived and stored hashed. Admin login is rate-limited.
- The service uses SQLite transactions, foreign-key relationships, ledger records, idempotent event keys and file permissions.
- The local save is not tamper-proof; premium state remains server-authoritative.

### Admin limitations

- Single shared admin credential; no MFA, role separation, bulk actions, CSV export or dispute portal.
- SQLite is single-host; run one process per data directory.
- Rate limits are in-memory and reset on restart.
- No tax, accounting, payout or chargeback-dispute automation.
- Apple/Google provider refunds are not executed by the game; the operator must issue them in the store console and then run the admin reversal.
- Stripe partial refunds are recorded but do not reverse a proportional GEM amount.

---

## 9. Tests performed

All counts below are from the Phase 03 source tree on 29 October 2026.

| Check | Result | Notes |
| --- | --- | --- |
| Unity compile | **0 errors** | Runtime, editor tools and test assemblies |
| Unity EditMode | **43 / 43 passed** | Save migration, progression, locations, junctions, transaction display labels |
| Unity PlayMode | **18 / 18 passed** | Boot, navigation, settings, restart, revive, shop tabs, offline history, account/events/atlas/achievements, 30 km stream |
| Headless core | **66 / 66 passed** | Deterministic gameplay core |
| Backend | **79 / 79 passed** | 9 race + 42 premium/payment HTTP + 11 account store + 11 identity + 3 admin CLI + 3 package config |
| Static integrity | **936 / 936 passed** | Meta/GUID, scene, branding, audio, sources, required docs |
| macOS build | **Succeeded** | Universal x86_64 + ARM64, ad-hoc signed, **122.5 MiB, 27.3 s build / 39.0 s wall**, rebuilt from Phase 03 source after the final label fix |
| macOS launch | **Launched** | Booted, completed the capture run and exited cleanly; no runtime exceptions in the player log |
| Fresh capture | **18 screens at 1280×800** | Final post-fix build; `Captures/phase03/1280x800`, including the rebuilt GEM shop |

### Backend coverage highlights

- Guest stability, token expiry/rejection and CORS/security headers.
- Catalogue integrity, package overrides, immutable GEM counts and admin catalogue edits.
- Admin login/rate limiting, transaction/account review and cross-database account detail.
- GEM refund reversal after provider confirmation.
- Purchase and revive debit-once/idempotent replay, insufficient balances and duplicate purchase handling.
- Email registration/verification/login/profile/logout, password reset and phone OTP.
- Account linking and monotonically merged cloud sync that rejects client-supplied GEMS.
- Stripe capability, unconfigured checkout, signed webhook credit, invalid signature rejection and full refund reversal.
- Apple receipt verification and Google service-account purchase verification with injected provider responses.
- Receipt construction, duplicate event handling and provider-disabled responses.
- Operator CLI catalogue/account/transaction commands and destructive-action guards.

### What the tests do **not** prove

- Any live Stripe, Apple or Google purchase.
- Any real iOS/Android store sheet or device behaviour.
- Live Google/Apple sign-in; the native plugins are absent.
- Live SMTP receipt or SMS OTP delivery.
- A fresh visual capture of transaction-history and receipt-success states (the default BUY GEMS package screen is captured; the other tabs and receipt success are asserted in code/PlayMode only).
- A full manual shop success/cancel/failure/refund playthrough on hardware.

---

## 10. Missing credentials and services

The following must be supplied before any live sale can be claimed:

### Hosting and operations

- A production HTTPS host and DNS name for `Backend/premium_server.py`.
- Reverse proxy/TLS configuration, firewall rules and backups.
- `NV_ADMIN_USERNAME` and a strong `NV_ADMIN_PASSWORD_HASH`; admin namespace behind VPN/allow-list.
- Monitoring/alerting and a restore drill for `premium.sqlite3`, `accounts.sqlite3` and `premium_secret.key`.

### Stripe (card checkout)

- `NV_STRIPE_SECRET_KEY` and `NV_STRIPE_WEBHOOK_SECRET`.
- `NV_STRIPE_PRICE_MAP` with a `price_...` id for each package.
- `NV_STRIPE_SUCCESS_URL` and `NV_STRIPE_CANCEL_URL` on HTTPS.
- A Stripe test-mode purchase, duplicate webhook, receipt and full refund test; then the same in live mode.

### Apple (iOS)

- Apple Developer/merchant account and App Store Connect products using `gems_80`, `gems_200`, etc.
- `NV_APPLE_SHARED_SECRET` and, if aliases are used, `NV_APPLE_PRODUCT_MAP`.
- A sandbox tester and an iOS device build; test purchase, duplicate replay, receipt and refund.

### Google (Android)

- Google Play Console app and products using the package ids.
- A service account with Play Console access and `NV_GOOGLE_PLAY_SERVICE_ACCOUNT`.
- `NV_GOOGLE_PLAY_PACKAGE_NAME` (`games.rkvantage.neonvelocity`) and optional product map.
- A licence-testing account and Android device build; test purchase, duplicate, receipt and refund.

### Identity and delivery

- Native Google Sign-In and Sign in with Apple plugins with OAuth client/service ids.
- `NV_GOOGLE_CLIENT_ID`, `NV_APPLE_CLIENT_ID`.
- `NV_SMTP_*` with a verified sending domain for verification/reset/receipt mail.
- `NV_SMS_WEBHOOK` and token for OTP delivery.

### Platform builds and policy

- iOS/Android modules, signing, provisioning, store metadata and real-device test accounts.
- Terms of sale, privacy policy, refund policy, tax and age-consent review.
- Confirmation that each distribution channel permits the chosen payment surface.

---

## 11. Remaining bugs and limitations

1. **No live payment evidence.** This is the largest gap. The code path exists; a real provider transaction does not.
2. **No native Google/Apple sign-in.** Server token verification is tested, but the Unity client cannot mint a provider session.
3. **No live SMTP/SMS.** Receipt and OTP delivery are unverified against real providers.
4. **No device IAP test.** Unity IAP compiles and is integrated, but store products/test accounts were unavailable.
5. **Apple/Google refunds require manual reconciliation.** Stripe full refunds auto-reverse; the other stores do not notify this service.
6. **Stripe partial refunds are recorded, not proportionally reversed.**
7. **Admin is single-credential.** No MFA, roles or bulk tooling.
8. **No admin web UI.** The CLI/routes are the operator surface.
9. **Local saves are not signed.** Premium state is server-authoritative; offline progression is not anti-cheat evidence.
10. **SQLite is single-host.** No clustering or horizontal scale-out.
11. **Receipt email is not gated on verification.** It defaults to the account email and warns when a different address is chosen.
12. **No tax/accounting/chargeback automation.**
13. **No fresh visual capture of transaction history, success, cancel or failure states.** The default BUY GEMS package screen is captured at 1280×800; PlayMode covers layout/text for the other tabs.
14. **Phase 03 UI label clipping was found in an earlier fresh capture and fixed** (provider status and receipt sign-in prompts shortened). The final build was recompiled, all suites rerun and the 18-screen 1280×800 capture regenerated; the fixed labels are visible in `Captures/phase03/1280x800/gem-shop-packages.png`.
15. **The macOS app is ad-hoc signed**, not notarised; Windows/Linux/Android/iOS builds were not produced here.
16. **Performance/device coverage remains outstanding:** no physical 4K display, real-device audio/input, thermal testing or 4K frame-rate profile.

---

## 12. Final verdict

Phase 03 delivers a real account/offline/economy/payment architecture and connects it to the existing game. The server is authoritative for GEMS, payments, transactions, premium ownership and account security. The normal shop no longer uses manual bank-transfer proof upload; it presents official platform checkout and verified transaction history. COINS remain the gameplay currency; GEMS are real-money only; revive and premium purchases debit the server wallet atomically.

The implementation is **not** a live payment deployment. No merchant credentials, store products, HTTPS host, native Google/Apple sign-in plugin or real provider transaction exists in this environment. The correct production statement is:

> **Phase 03 is implemented and locally verified; live payments and live provider sign-in remain unverified and require the credentials/services listed in section 10.**

Do not promote this build to a paid store release, and do not claim a live GEM sale, until at least one real sandbox purchase per provider has been completed end to end and reconciled in the admin transaction view and ledger.

---

## Appendix A — Reproduce the local checks

```bash
# Backend (79)
python3 -m unittest discover -s Backend/tests -v

# Static integrity (936)
python3 Tools/validate_project.py

# Headless core (66)
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet" \
  run --project Tools/CoreTests/CoreTests.csproj -c Release

# Unity EditMode (43)
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode \
  -testResults /tmp/nv-editmode.xml -logFile /tmp/nv-editmode.log

# Unity PlayMode (18)
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode \
  -testResults /tmp/nv-playmode.xml -logFile /tmp/nv-playmode.log

# macOS build
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit -projectPath "$PWD" \
  -executeMethod NeonVelocity.EditorTools.BuildTools.BuildMacOS \
  -logFile /tmp/nv-build.log
```

## Appendix B — Related documents

- [PREMIUM_DEPLOYMENT.md](../Backend/PREMIUM_DEPLOYMENT.md) — provider setup, receipts, refunds, admin, HTTPS, backup and operator checklist.
- [SERVER_PROTOCOL.md](SERVER_PROTOCOL.md) — HTTP contract for account, premium, payment and admin routes.
- [TEST_REPORT.md](TEST_REPORT.md) — full executed test record.
- [QA_REPORT.md](../QA_REPORT.md) — defects, fixes and unverified work.
- [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md) — operational, content and release constraints.
- [PROJECT_STATUS.md](../PROJECT_STATUS.md) — current status summary.
