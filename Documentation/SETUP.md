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
| Python | 3.9 or newer, standard library only, for multiplayer |
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
- audio sources and the original 21-clip soundtrack;
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
| Pause | `Esc`, `P` | Start | Pause button |

- Hold jump for extra height; release early for a short hop.
- Menus are mouse/touch driven and keyboard/gamepad navigable through Unity UI navigation.
- **Settings** exposes music/SFX/ambience volume, swipe sensitivity, graphics preset, frame-rate cap (30/60/144), camera shake, reduced motion, vibration, high contrast and the multiplayer host/port.
- The HUD shows distance, score, credits, speed/lives, timer/objective, active power-ups, connection state and the five-runner race ladder.

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

## 7. Play online

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

Closing the game/process loses the token, so relaunching requires a new room join. There are no persistent accounts in this version.

## 8. Troubleshooting

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

## 9. Current environment status

The project was authored on macOS 26.7.1 with Unity 6000.6.7f1 and Python 3.9.6. At hand-off, Unity Hub reports no active entitlement, so the editor cannot import or enter Play Mode here. Only `MacStandaloneSupport` and `WebGLSupport` modules are installed; Android/iOS modules and the Android SDK are absent. See [TEST_REPORT.md](TEST_REPORT.md) for exactly what was and was not executed.
