# NEON VELOCITY — Setup Guide

This guide covers opening the project, playing offline, running the authoritative server and joining a LAN match.

## 1. Requirements

| Component | Requirement |
|---|---|
| Unity Editor | 6000.6.7f1 (project's `ProjectVersion.txt`) |
| Unity licence | Active Personal or Pro entitlement signed into Unity Hub |
| Render pipeline | URP 17.6.0 (already in `Packages/manifest.json`) |
| Input | Input System 1.20.0 (active input handling is set to Input System) |
| UI | uGUI 2.6.0 |
| Python | 3.9 or newer, standard library only, for multiplayer and the premium store |
| OS (editor) | macOS/Windows/Linux supported by the Unity version |
| Build modules | Only needed for player builds; see [BUILD.md](BUILD.md) |

No third-party Unity Asset Store packages, Python packages or network services are required.

> A Unity editor without an active licence exits with code **198** before importing the project. That is an account/licence problem, not a project error.

## 2. First open

1. Launch **Unity Hub → Add → Add project from disk**.
2. Select the `NEON VELOCITY` folder (the folder containing `Assets`, `Packages` and `ProjectSettings`).
3. Open with **6000.6.7f1**. Allow the first import and shader compilation to finish.
4. Open **Assets/Game/Scenes/Main.unity**.
5. Press **Play**.

The scene contains no authored gameplay objects. On entering Play Mode, `GameApp` is created automatically by `[RuntimeInitializeOnLoadMethod]` and generates:

- the neon title stage and animated camera;
- HUD, menus, modal dialogs and the Input System event system;
- audio sources and the original 32-clip soundtrack;
- the save profile, progression, missions, shop state and leaderboard;
- the run world, track, runner, obstacles, pickups, VFX and results when a mode starts.

This design removes prefab/scene-reference fragility and makes the build work from a single boot scene.

## 3. First-run profile

- Save path: `Application.persistentDataPath/neon_velocity_save.json`
  - macOS: `~/Library/Application Support/RK VANTAGE GAMES/NEON VELOCITY/`
  - Windows: `%USERPROFILE%\AppData\LocalLow\RK VANTAGE GAMES\NEON VELOCITY\`
  - Android: app-internal files directory
  - iOS: app sandbox `Documents`/persistent data folder
- A `.bak` copy is maintained beside the save. Corrupt primary saves are quarantined and the backup is loaded.
- Defaults: 500 credits, KIRA-9 runner, ION SPARK trail, starter upgrades at level 0.
- The welcome flag is stored on first menu load; the daily supply button appears on the main menu and must be claimed manually.
- "Reset profile" is available in **Settings**; it creates a fresh profile.

## 4. Controls and UI

| Action | Keyboard | Gamepad | Touch |
|---|---|---|---|
| Lane left/right | `A`/`D` or arrows | Left stick | Swipe left/right |
| Jump | `Space`, `W`, `↑` | South button | Swipe up |
| Slide | `S`, `↓` | East button | Swipe down |
| Premium ability | `Q` / `E` | Left/right shoulder | Ability HUD button |
| Pause | `Esc`, `P` | Start | Pause button |

- Hold jump for extra height; release early for a short hop.
- Menus are mouse/touch driven and keyboard/gamepad navigable through Unity UI navigation.
- **Settings** exposes master/music/effects/ambience volume, swipe sensitivity, graphics preset, frame-rate cap, display options, camera shake, reduced motion, vibration, high contrast, the multiplayer host/port and the premium store URL.
- The HUD shows score, distance, credits, speed/lives, a captioned run-time readout, objective progress, active power-ups, connection state and the five-runner race ladder.

## 5. Offline play

From the main menu:

1. Choose **PLAY** or **GAME MODES**.
2. Select any single-player mode.
3. Wait for the 3-2-1 countdown, then run.
4. Pause with `Esc` for resume/restart/settings/quit.
5. Results show distance, score, credits, XP, missions/achievements and new bests.

Practice and First Contact deliberately do not grant competitive progression. Daily/Weekly/Seasonal clears pay once per period. Every other offline mode applies credits, XP, missions, achievements and leaderboard updates.

## 6. Run the race authority

From the project root:

```bash
python3 Backend/server.py
```

Default bind: `0.0.0.0:7777`; countdown 5 s; race 120 s; reconnect grace 45 s.

Options (environment variables in parentheses):

```text
--host 0.0.0.0          (NV_HOST)
--port 7777             (NV_PORT)
--data Backend/data     (NV_DATA)
--countdown 5           (NV_COUNTDOWN)
--duration 120          (NV_DURATION)
--log-level INFO        (NV_LOG_LEVEL)
```

The leaderboard is persisted atomically to `<data>/leaderboard.json`. Live rooms and sessions are in memory and do not survive a server restart. The `nv1` message contract, validation rules and limits are documented in [SERVER_PROTOCOL.md](SERVER_PROTOCOL.md).

Verify the server process is listening:

```bash
lsof -nP -iTCP:7777 -sTCP:LISTEN    # macOS/Linux
python3 -m unittest discover -s Backend/tests -v   # integration tests
```

## 7. Run the premium GEM store locally

The premium store is a separate HTTP + SQLite service used for real-money GEM packages, ownership and paid revives. It is not the race authority.

```bash
export NV_ADMIN_USERNAME=operator
export NV_ADMIN_PASSWORD='choose-a-long-local-password'
Backend/run_premium_local.sh
```

- It binds to `127.0.0.1:8088` and stores SQLite data and the device-key secret in gitignored `Backend/premium_data/`.
- The game defaults to that endpoint. If needed, set it at **Settings → Network → Premium Store URL**; production must use **https://**.
- GEMS are never minted locally or awarded by gameplay. Mobile purchases use the official Apple/Google store sheet through Unity IAP; desktop/web opens Stripe-hosted checkout. The backend verifies the receipt/webhook, prevents duplicate credit and then updates the wallet. If a provider is not configured, the shop says so and will not start a charge.
- Receipts are emailed after a verified purchase when SMTP is configured. The receipt address defaults to the verified account email and can be changed from the account screen.
- The legacy manual bank-transfer workflow is disabled by default. Only set `NV_ENABLE_MANUAL_BANK_TRANSFER=1` if an operator deliberately needs it for historical reconciliation; it is not the normal purchase path.
- Review accounts, transactions, catalogue and refunds with `python3 Backend/premium_admin.py` (`transactions`, `accounts`, `account`, `catalog`, `set-package`, `set-product`, `refund`). See [PREMIUM_DEPLOYMENT.md](../Backend/PREMIUM_DEPLOYMENT.md) for provider configuration, HTTPS deployment, account/cloud-sync routes, receipts, refunds, security and backup requirements.
- GEMS are never awarded by gameplay. If the store is unreachable the UI shows **STORE OFFLINE** and will not fabricate a balance, purchase or revive.

## 8. Play online

### Local machine

1. Start the server.
2. Keep **Settings → Network → Host** as `127.0.0.1`, port `7777`.
3. Open **MULTIPLAYER**.
4. **QUICK MATCH** for a public room, or **CREATE ROOM** for a five-character private code.
5. Press **READY**. Bots fill remaining slots only when the host starts.
6. The host presses **START CIRCUIT** after every human is ready.
7. All clients receive the same seed and server start time, show the countdown, race for 120 seconds, then see server-ranked results.

### LAN / remote server

1. On the server machine, start with `--host 0.0.0.0 --port 7777`.
2. Allow inbound TCP 7777 in the firewall.
3. In each client, open **Settings → Network**, enter the server's LAN/public IP and port, and save.
4. Connect through **MULTIPLAYER**.

All clients must use the same protocol version (`nv1`); the shipped client and server are 1.0.0.

### Reconnection

The client keeps the server-issued player ID and token for the session. If the socket drops, it retries automatically for up to three attempts and the server holds the runner's slot for 45 seconds. The runner is marked disconnected, their last validated distance is retained and the race continues. A finished room replays its stored `race_results` packet to a reconnecting client.

Closing the game/process loses the race token, so relaunching requires a new room join. The separate premium/account service now supports persistent email/password, phone and provider-linked identities plus cloud progression sync; the race authority itself still uses ephemeral session tokens.

## 9. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Editor exits 198 with no window | No active Unity licence. Sign in to Hub and confirm an entitlement; this project cannot bypass licensing. |
| "SERVER UNAVAILABLE" | Server not running, wrong host/port, firewall, or TCP blocked. Verify with `lsof`/`nc`. |
| WebGL build cannot play online | Raw TCP sockets are unavailable in browsers; online is deliberately blocked on WebGL. Use a native build. |
| Android/iOS cannot reach server | Use the host's LAN IP, not `127.0.0.1`; both devices must be on the same network and port 7777 must be reachable. |
| Six-player join rejected | Rooms are capped at five total runners; the backend rejects a sixth human. |
| Race will not start | Every human in the room must press READY and only the host can start. Bots do not need readiness. |
| Online result says "NO REWARDS GRANTED" | Server connection ended or the result timed out before the verified packet arrived. Only verified results pay competitive rewards. |
| Corrupt save on startup | The primary file is quarantined and `.bak` is restored automatically. |
| No audio on WebGL | Browsers may block autoplay until the first click; interact with the menu. Unverified in this environment. |
| GEM SHOP says "STORE OFFLINE" | Premium service not running, wrong URL, HTTP/HTTPS mismatch, or unreachable host. Start `Backend/run_premium_local.sh` or set the deployed URL in Settings. |
| Paid purchase stays pending | The provider has not yet confirmed the payment, or server verification failed. Check transaction history and the operator `transactions` view; never credit manually without matching the provider record. |
| Premium URL rejected | It must be an absolute `http://` or `https://` URL; production should use HTTPS. |
| "STORE NOT CONFIGURED" on a package | The server has no credentials/store products for the current platform. Configure Stripe, Apple IAP or Google Play on the backend; the game will not fake a purchase. |
| Cached GEMS reset after changing URL | Expected: cached wallet, token and entitlements from the previous server are cleared before syncing with the new authority. |

## 10. Current environment status

The project was authored and verified on macOS 26.7.1 (Intel) with Unity **6000.6.7f1**, Python 3.9.6 and Unity's bundled .NET SDK. A Personal licence is active. Unity imports/compiles the project, **43/43 EditMode** and **18/18 PlayMode** tests pass (including navigation/Back, settings-from-pause, restart, purchase confirmation, Phase 03 shop tabs/transaction history, account/events/atlas/achievements, ability/cooldown, exact revive, endpoint cache reset, HUD/toast pause behaviour and a real 30 km streaming run), **66/66** headless core tests, **79/79** backend tests and **936** static checks pass. A universal **Intel + Apple silicon** macOS player was built from the Phase 03 source, and an unsigned **iOS Xcode project** was generated with IL2CPP and Unity IAP 5.4.4 and compiled for `iphoneos`. A fresh **18-screen 1280×800** capture from that build includes the rebuilt GEM shop; the 1600×900 and 3840×2160 sets are from Phase 02, and the host has no physical 4K display. The premium/payment/account service is tested locally with stubbed Stripe/Apple/Google responses but is **not deployed**; live card/store sales need HTTPS, admin credentials, merchant/provider credentials, store products, SMTP delivery and a real sandbox transaction. The armour/audio/environment pass has no audible, live-display or frame-rate validation yet. See [PHASE03_FINAL_REPORT.md](PHASE03_FINAL_REPORT.md) and [DEVELOPMENT_PROGRESS.md](DEVELOPMENT_PROGRESS.md) for the current roadmaps and evidence.

That launch is not a full manual playthrough. Every mode, power-up, result path, real-device input/audio and multi-machine online race still need the checklist in [TEST_REPORT.md](TEST_REPORT.md). Windows, Linux, WebGL and Android builds and a signed iOS archive/device run are unverified; the iOS module and Xcode are installed, but Android tooling is absent. See [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md) before making platform claims.
