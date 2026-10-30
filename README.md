# NEON VELOCITY

**RK VANTAGE GAMES** presents a three-lane, neon-noir endless runner built in **Unity 6000.6.7f1**, **Universal Render Pipeline**, **Input System** and **C#**, with an original authoritative multiplayer backend written in dependency-free **Python**.

> **Verification notice (30 October 2026):** Unity 6000.6.7f1 Personal licensing is active. The project imports and compiles in Unity, **43/43 EditMode** and **18/18 PlayMode** tests pass, including a real 30 km `TrackManager` stream across 3/6/9/15/30 km checkpoints. **66/66** headless core tests, **79/79** backend tests (race authority, accounts/auth, premium wallet, catalogue/admin, Stripe/Apple/Google verification, receipts and operator CLI) and **936/936** static checks also pass. Phase 03 connects the existing UI to real account, offline-save, server-wallet, hosted-checkout, platform-IAP, receipt, refund and admin systems. Checkout is never simulated: it stays disabled until the server has provider credentials and verified store products, and **no live provider transaction has been completed in this environment**. The Phase 03 macOS universal player was rebuilt and a fresh 18-screen 1280×800 capture includes the rebuilt GEM shop; the 1600×900 and 4K sets are from Phase 02. An iOS Xcode project was generated at `Builds/iOS` with IL2CPP and Unity IAP 5.4.4 and compiles unsigned for `iphoneos`; it is not signed and has not been run on a device. Google and Apple token verification exist on the server, but the native Google/Apple sign-in plugins, SMTP/SMS delivery and all merchant credentials remain owner-supplied. Drone chases, rideable moving platforms, multi-stage destructible structures, temporary vehicles, explicit Friend Challenge/Limited-Time Event entries and fully drivable perpendicular 90° cross-street turns are **not implemented**; moving trains, traffic, falling debris, sliding obstacle platforms and collapsing-bridge gaps are. A physical 4K display, 4K frame-rate profiling, Windows/Linux/WebGL/Android builds, a signed iOS archive/device run, real-device input/audio and real-network multiplayer remain unverified. See [Phase 03 final report](Documentation/PHASE03_FINAL_REPORT.md), [Development Progress](Documentation/DEVELOPMENT_PROGRESS.md), [QA Report](QA_REPORT.md), [Project Status](PROJECT_STATUS.md), [Test Report](Documentation/TEST_REPORT.md), [Premium Deployment](Backend/PREMIUM_DEPLOYMENT.md) and [Known Limitations](Documentation/KNOWN_LIMITATIONS.md) before making platform claims.

---

## The game

Run a rain-slicked city grid that never ends. Switch three lanes, jump low barriers, slide under overhead lasers, choose a junction branch and thread through solid traffic. Speed rises with distance and time. Twenty districts rotate every 3 km with their own sky, architecture, signature props and hazard mix, while a seven-state time-of-day system moves the sun, moon, shadows, fog, streetlights and neon. Earned CREDITS buy permanent tech and cosmetics; server-authoritative GEMS are a separate premium wallet used for five signature runners, premium upgrades and exact-position revives. Online, five runners share one deterministic circuit and one server-owned countdown.

- **Studio:** RK VANTAGE GAMES
- **Title:** NEON VELOCITY
- **Version:** 1.0.0
- **Bundle ID:** `games.rkvantage.neonvelocity`
- **Engine:** Unity 6000.6.7f1 / URP 17.6 / Input System 1.20
- **Backend:** Python 3.9+ standard library, asyncio TCP

### Modes

| Mode | Players | Rules |
|---|---|---|
| **Classic Run** | 1 | Endless distance, rising speed, one crash ends the run. |
| **Mission Run** | 1 | Reach the authored target distance with a mission-first HUD. |
| **Practice Loop** | 1 | Slower, crash-recovering training with no competitive rewards. |
| **Time Trial** | 1 | Greatest distance in 60 seconds. |
| **Distance Attack** | 1 | Fixed-distance score attack with a completion target. |
| **Daily Signal** | 1 | 600 m target on a UTC-date-seeded track; first clear pays a bonus. |
| **Weekly Protocol** | 1 | 1,500 m target on an ISO-week-seeded track; boosted credit payout. |
| **Coin Rush** | 1 | 90 seconds, triple credit density and magnet-friendly track. |
| **Survival Shift** | 1 | Three lives; collisions consume a life instead of ending immediately. |
| **Monsoon Overdrive** | 1 | Seasonal 1,000 m objective with rain themes and double season XP. |
| **First Contact** | 1 | Guided 300 m tutorial with live prompts and no crash failure. |
| **Boss Chase** | 1 | Outrun the Warden mech and reach the extraction distance. |
| **Route Exploration** | 1 | Junction-focused run that rewards branch discovery and clean merges. |
| **Neon Circuit** | 5 online | Public matchmaking + bots. Shared seed, 5 s countdown, 120 s race, server ranking. |
| **Private Circuit** | 5 online | Same rules in a five-character room code shared with friends. |

### Systems

- **Deterministic procedural track** — 20 hand-authored obstacle patterns, validated for navigability and action spacing, composed by a seeded director so every online runner sees the same world without shipping obstacle state.
- **Movement** — three lanes, jump with variable hold height, slide with cooldown, near-miss scoring, hoverboard/shield recovery, lane interpolation.
- **10 power-ups** — magnet, shield, score surge, volt rush, spectre board, phase shell, air jump, time dilation (offline only), credit storm and rare prism dash.
- **Progression** — infinite total-XP levels with exact authored L1–L10 thresholds, milestone rewards, season levels, CREDITS, 5 local runners, 4 trails, permanent magnet/shield/multiplier/boost/head-start upgrades, daily and weekly missions, lifetime achievements, daily supply drops and local bests/leaderboards.
- **Twenty districts** — 3 km zones with exactly the twenty requested themes, unique palettes, skyboxes, weather, time-of-day preferences, signature landmarks, road keys and hazard bias, drawn from seventeen purpose-built architecture families; discovery persists, the layout is deterministic and districts never repeat back-to-back.
- **Seven-state time of day** — Early Morning, Morning, Afternoon, Evening, Sunset, Night and Late Night profiles with smoothly interpolated sun/moon colour and elevation, sky gradients, ambient and directional light, shadow strength, fog, streetlights, neon, stars, clouds, weather intensity, reflections and traffic/train availability. Settings can use per-location authored light, a dynamic 21 km cycle, or a fixed phase, and the choice persists.
- **Connected junctions** — deterministic split, crossroad, narrow-bridge, broken-road, tunnel-branch, T-junction, curved-road, shortcut and collapsing-bridge junctions every fifteen segments, with physically curved branch decks, a 126 m warning gantry, branch chevrons, keyboard/controller lane commitment, connected median/edge collision, merges and authored reward routes. Every branch is physically safe.
- **Two economies** — CREDITS are earned only in local play; GEMS are premium-only, server-authoritative, and credited only after a verified payment. Gameplay never awards GEMS.
- **Premium store** — five GEM packages with a live server catalogue. Android uses Google Play Billing, iOS uses Apple In-App Purchase through Unity IAP, and desktop/web opens Stripe-hosted checkout. The backend verifies store receipts/webhooks before crediting GEMS, sends receipts, prevents duplicate credit and supports refunds. Card data never enters the game. The legacy manual bank-transfer workflow is disabled by default and retained only for historical orders.
- **Accounts and cloud sync** — email/password, phone OTP, guest linking, profile and receipt email, verification/recovery, persistent bearer sessions and monotonic offline-to-online progress merge. Google ID tokens and Apple identity tokens are verified on the server when credentials are configured; this build has no native Google/Apple sign-in plugin, so those buttons explain the boundary instead of faking a session. Offline play never requires an account.
- **Signature runners** — PHANTOM X phase dash, NOVA TITAN nova burst, ECHO, SABLE and HELIOS, each with distinct silhouettes, utility abilities, FX and server-owned entitlements. All are balanced sidegrades.
- **Exact-position revive** — a crashed run is held, rewards previewed, and a server-verified GEM debit resumes the same run at the exact death distance/lane/pose with a short stabilisation window; leaving or restarting finalises rewards once.
- **Online authority** — quick match and private codes, host-controlled start, server-filled bots to five, 10 Hz validated snapshots, results, verified rewards, leaderboard persistence, 45 s reconnect grace and automatic client reconnection.
- **Presentation** — runtime-generated URP world with per-district themes, generated asphalt/concrete/normal maps, a wet multi-layer road, recessed storefronts, layered skyline, street lighting, atmospheric mist/rain and cinematic post-processing. Original synthesized music/ambience/32 clips, speed lines, particles, camera shake, screen fades, toasts and a complete runtime UI. No external art or audio packs.
- **Offline first** — every single-player mode works without the backend; online and premium purchases are never simulated when their services are absent.

### Environment vertical slice

- **City fabric** — recessed facade blocks with deterministic alley gaps, podiums, balconies, AC units, pipes, rooftop plant, storefront shelving/counters, awnings, shutters, blade signs and abstract billboards.
- **Street level** — clean lane field, curbs, gutters, drains, raised sidewalks, service aprons, safety rails, benches, bins, street-name signs, lamps and warm practical pools.
- **Vegetation** — street trees with visible trunks, layered low-poly canopies, planters, metal guards and neon planter edges; all outside the playable lane boundaries.
- **Weather and light** — dynamic seven-state time of day, generated wet asphalt/normal detail, puddle/reflection cards, textured additive rain, wind slant, splashes, mist, fog, ACES/bloom and a **2.6-second** biome lighting/sky/rain crossfade.
- **Performance structure** — segmented and pooled world data, material-batched merged decor and a separate no-shadow additive-card renderer. No LOD/occlusion or per-tree wind animation was added in this slice.

### Controls

| Action | Keyboard | Gamepad | Touch |
|---|---|---|---|
| Change lane | `A` / `D`, `←` / `→` | Left stick left/right | Swipe left/right |
| Jump | `Space`, `W`, `↑` (hold for height) | South button | Swipe up |
| Slide | `S`, `↓` | East button | Swipe down |
| Premium ability | `Q` / `E` | Left/right shoulder | HUD ability button |
| Pause | `Esc`, `P` | Start | On-screen pause |

UI is mouse/touch driven with gamepad/keyboard navigation through Unity's Input System UI module.

---

## Quick start

### 1. Play offline in the editor

1. Install **Unity Hub** and **Unity 6000.6.7f1** with Mac/Windows build support (or another target).
2. Sign in to a Unity account with an active Personal/Pro entitlement. Without a licence the editor exits before import.
3. In Unity Hub choose **Add project from disk** and select this folder.
4. Open `Assets/Game/Scenes/Main.unity` and press **Play**.

The scene is intentionally almost empty. `GameApp` bootstraps after scene load via `[RuntimeInitializeOnLoadMethod]`, builds the menu stage, UI, audio, save data and all gameplay objects at runtime. Do not add scene objects manually.

The save file is written to `Application.persistentDataPath/neon_velocity_save.json` with an atomic `.bak` fallback. Starting credits are 500.

### 2. Play the verified macOS build

The locally verified build is a universal binary for Intel and Apple silicon:

```bash
open "Builds/macOS/NeonVelocity.app"
```

`Builds/` is intentionally gitignored, so a fresh clone must create it with **NEON VELOCITY → Build macOS** or **Build iOS Xcode Project** (or the batch commands in [BUILD.md](Documentation/BUILD.md)). The macOS app is ad-hoc signed, not notarised, and has no store packaging; the iOS export must be signed in Xcode with your Apple team.

### 3. Run the multiplayer authority

```bash
# From the project root
python3 Backend/server.py --host 0.0.0.0 --port 7777
```

Defaults: port `7777`, 5-second countdown, 120-second race, 45-second reconnect grace. Data is stored under `Backend/data/leaderboard.json`. Use `--data` to choose another directory. No Python packages are required.

In game, open **MULTIPLAYER**, confirm the host in **SETTINGS → NETWORK** (default `127.0.0.1:7777`), then **QUICK MATCH** or create/join a private code. All human runners press **READY**; the host presses **START CIRCUIT**. Empty slots are filled with server bots so a race always has five runners.

For a LAN session, start the server with `--host 0.0.0.0`, allow TCP 7777 through the firewall, and enter the host machine's LAN IP in each client's Network settings. The protocol is plain TCP and is intended for trusted networks; see the limitations.

### 4. Run the premium GEM service (local)

```bash
# From the project root. Choose your own admin credentials.
export NV_ADMIN_USERNAME=operator
export NV_ADMIN_PASSWORD='replace-with-a-long-local-password'
Backend/run_premium_local.sh
```

The service binds to `127.0.0.1:8088` and stores SQLite data and its device-key secret under `Backend/premium_data/` (gitignored). The game defaults to that URL in the save settings. Real-money purchases use the platform store sheet on Android/iOS and hosted Stripe Checkout on desktop/web. GEMS are credited only after server-side verification; a receipt is emailed after success when SMTP is configured. **No provider is live until you supply credentials and products**:

```bash
# Card checkout (desktop/web)
export NV_STRIPE_SECRET_KEY=sk_live_...
export NV_STRIPE_WEBHOOK_SECRET=whsec_...
export NV_STRIPE_PRICE_MAP='{"gems_80":"price_...","gems_200":"price_..."}'
export NV_STRIPE_SUCCESS_URL='https://your.game/success'
export NV_STRIPE_CANCEL_URL='https://your.game/cancel'

# Apple In-App Purchase (iOS)
# Store products use package ids (gems_80, ...) by default.
export NV_APPLE_SHARED_SECRET=...
export NV_APPLE_PRODUCT_MAP='{"gems_80":"gems_80"}'

# Google Play Billing (Android)
export NV_GOOGLE_PLAY_SERVICE_ACCOUNT=/secure/play-service-account.json
export NV_GOOGLE_PLAY_PACKAGE_NAME=games.rkvantage.neonvelocity
export NV_GOOGLE_PLAY_PRODUCT_MAP='{"gems_200":"gems_200"}'

# Receipts and phone OTP (optional until configured)
export NV_SMTP_HOST=smtp.example.com NV_SMTP_FROM=receipts@example.com
export NV_SMS_WEBHOOK=https://your.sms.gateway/send
```

Billing is gated on the server's reported capability, so a missing provider can never charge a player. The legacy manual bank-transfer workflow is off unless `NV_ENABLE_MANUAL_BANK_TRANSFER=1`. Admin review, catalogue control and refunds use `Backend/premium_admin.py` (`transactions`, `accounts`, `catalog`, `set-package`, `set-product`, `refund`). For production, follow [PREMIUM_DEPLOYMENT.md](Backend/PREMIUM_DEPLOYMENT.md) — HTTPS, admin credentials, provider verification, backups, receipts and refund reconciliation are required.

### 5. Backend smoke test

```bash
python3 -m unittest discover -s Backend/tests -v
```

Expected: **79 tests pass** covering race authority, premium catalogue/admin, idempotent wallet credit/debit, Stripe/Apple/Google verification, receipts, refunds, admin CLI, account registration/login/verification/OTP, conflict-safe cloud sync and malformed/unauthorised input.

---

## Repository map

```
Assets/Game/
  Scenes/Main.unity              Boot scene (runtime-generated game)
  Scripts/Core/                  Engine-independent deterministic core (NeonVelocity.Core)
  Scripts/                       Runtime game, UI, audio, world, online client
  Scripts/Editor/BuildTools.cs   Project configuration + platform build entry points
  Scripts/Editor/NeonVelocity.EditorTools.asmdef  Editor-only assembly boundary
  Tests/EditMode/                NUnit core + save tests
  Tests/PlayMode/                Boot/menu/practice, jump, navigation and restart tests
  Resources/Audio/               32 original WAV assets (music, ambience, SFX)
  Resources/Materials/           Pinned URP shader templates used by the runtime
Backend/
  server.py                      Authoritative asyncio TCP server
  premium_server.py              Premium/account/auth/payment HTTP service
  premium_store.py               Wallet, catalogue, ownership and ledger
  account_store.py               Auth identities, cloud sync and transactions
  payments.py                    Stripe, Apple IAP and Google Play verification
  identity.py                    Google/Apple token verification
  premium_admin.py               Secure operator CLI
  tests/                         79 backend tests (race, account, premium, payments, CLI)
Tools/
  CoreTests/                     Headless .NET test runner for the gameplay core
  generate_audio.py              Reproducible original audio synthesis
  generate_meta.py               Stable Unity .meta generation
Documentation/
  SETUP.md                       Editor, controls, server and LAN setup
  BUILD.md                       Module installation and build commands
  TEST_REPORT.md                 Actually executed tests and unverified areas
  PHASE01_FINAL_REPORT.md        Phase 01 bugs, fixes, systems and long-run results
  PHASE03_FINAL_REPORT.md        Phase 03 auth, offline, economy, payments and test status
  DEVELOPMENT_PROGRESS.md        Roadmap, phase status, defects, gaps and next tasks
  KNOWN_LIMITATIONS.md           Honest gaps and operational constraints
  SERVER_PROTOCOL.md             nv1 TCP wire protocol and authority rules
  ASSET_LICENSING.md             Ownership and third-party licence notes
QA_REPORT.md                     Defects, fixes, test evidence and remaining gaps
PROJECT_STATUS.md                Current implementation/test/build state and next work
```

### Assembly layout

- **NeonVelocity.Core** — no engine references; pure deterministic rules, track generation, save schema, race DTO/ranking and progression math. Runs in Unity and plain .NET.
- **NeonVelocity.Runtime** — Unity-facing assembly referencing URP, Input System and uGUI.
- **NeonVelocity.EditorTools** — editor-only assembly for project configuration, shader templates and builds.
- **NeonVelocity.Tests.EditMode** / **PlayMode** — test assemblies gated by `UNITY_INCLUDE_TESTS`.

---

## Testing and builds

```bash
# Headless core tests (66 test cases)
dotnet run --project Tools/CoreTests/CoreTests.csproj -c Release

# Backend integration tests (79: race authority + account + premium + payments + CLI)
python3 -m unittest discover -s Backend/tests -v

# Static project integrity (936 checks)
python3 Tools/validate_project.py
```

Unity tests run through the Test Framework and pass in this environment:

```text
EditMode: 43 / 43 passed
PlayMode:  18 / 18 passed (boot, practice, jump, navigation/Back, settings-from-pause,
           results root, restart, garage cleanup, purchase confirmation, GEM SHOP tabs,
           phase-03 shop labels and retired bank-transfer removal, premium-screen
           navigation, account tabs, events, world atlas, achievements,
           ability/cooldown, exact-distance revive, endpoint cache reset, menu-label
           visibility, independent HUD tickers, pause HUD visibility and a real 30 km
           TrackManager streaming run across 3/6/9/15/30 km checkpoints)
```

Build the verified macOS target:

```bash
UNITY="/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity"
"$UNITY" -batchmode -quit -projectPath "$PWD" \
  -executeMethod NeonVelocity.EditorTools.BuildTools.BuildMacOS \
  -logFile "/tmp/neon-velocity-build.log"

open "Builds/macOS/NeonVelocity.app"

"$UNITY" -batchmode -quit -projectPath "$PWD" \
  -executeMethod NeonVelocity.EditorTools.BuildTools.BuildIOS \
  -logFile "/tmp/neon-velocity-ios-build.log"

open "Builds/iOS/Unity-iPhone.xcodeproj"
```

The build tool configures a universal **x86_64 + ARM64** app, creates the pinned URP material templates, keeps URP general variant stripping disabled for the PC/Deferred quality level (every world material is built at runtime), and lists the post-processing shaders as always-included. The iOS build temporarily enables general stripping for the forward-only Mobile level and restores the setting afterward. The verified output is ad-hoc signed and launched successfully on this Intel Mac. A developer-only capture flag (`-nv-capture <directory> -nv-capture-size 3840x2160`) saves 18 screens — menu, settings, premium-network settings, garage, modes, account sign-in/recovery/profile, events, world atlas, achievements, characters, GEM packages, two Practice frames, a mid-biome transition, the seeded tunnel and pause — from the built player; it produces actual 3840×2160 PNGs for UI and environment inspection. The transition frame is held 0.65 s into the crossfade.

**Build status:** macOS universal player built and launched; the iOS Xcode project is generated, parses in Xcode and compiles unsigned for `iphoneos`. Editor EditMode/PlayMode tests pass. Windows, Linux, WebGL and Android builds, a signed iOS archive/device run, and real-device, performance and multi-machine network validation remain unverified. Android tooling is not available here.

## Documentation

- [Setup guide](Documentation/SETUP.md)
- [Build guide](Documentation/BUILD.md)
- [Test report](Documentation/TEST_REPORT.md)
- [Phase 01 final report](Documentation/PHASE01_FINAL_REPORT.md)
- [Phase 03 final report](Documentation/PHASE03_FINAL_REPORT.md)
- [Development progress](Documentation/DEVELOPMENT_PROGRESS.md)
- [Premium deployment](Backend/PREMIUM_DEPLOYMENT.md)
- [Known limitations](Documentation/KNOWN_LIMITATIONS.md)
- [Server protocol](Documentation/SERVER_PROTOCOL.md)
- [Asset licensing](Documentation/ASSET_LICENSING.md)

## Ownership

Original game code, design, generated audio, procedural art and documentation were produced for **RK VANTAGE GAMES**. No marketplace assets, third-party audio samples or external art are included. Unity engine/package components remain under Unity's licence terms. See [ASSET_LICENSING.md](Documentation/ASSET_LICENSING.md).
