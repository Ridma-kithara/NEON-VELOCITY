# NEON VELOCITY

**RK VANTAGE GAMES** presents a three-lane, neon-noir endless runner built in **Unity 6000.6.7f1**, **Universal Render Pipeline**, **Input System** and **C#**, with an original authoritative multiplayer backend written in dependency-free **Python**.

> **Verification notice (22 October 2026):** the engine-independent gameplay core (50 tests), the Python race authority (9 integration tests) and both C# compile checks pass. The Unity editor could not be licensed in this environment, so scene import, Unity EditMode/PlayMode tests and platform builds are **implemented but not runtime-verified here**. See [Test Report](Documentation/TEST_REPORT.md) and [Known Limitations](Documentation/KNOWN_LIMITATIONS.md) before making platform claims.

---

## The game

Run a rain-slicked city grid that never ends. Switch three lanes, jump low barriers, slide under overhead lasers and thread through solid traffic. Speed rises with distance and time. Energy credits buy permanent upgrades and cosmetics. Online, five runners share one deterministic circuit and one server-owned countdown.

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
| **Practice Loop** | 1 | Slower, crash-recovering training with no competitive rewards. |
| **Time Trial** | 1 | Greatest distance in 60 seconds. |
| **Daily Signal** | 1 | 600 m target on a UTC-date-seeded track; first clear pays a bonus. |
| **Weekly Protocol** | 1 | 1,500 m target on an ISO-week-seeded track; boosted credit payout. |
| **Coin Rush** | 1 | 90 seconds, triple credit density and magnet-friendly track. |
| **Survival Shift** | 1 | Three lives; collisions consume a life instead of ending immediately. |
| **Monsoon Overdrive** | 1 | Seasonal 1,000 m objective with rain themes and double season XP. |
| **First Contact** | 1 | Guided 300 m tutorial with live prompts and no crash failure. |
| **Neon Circuit** | 5 online | Public matchmaking + bots. Shared seed, 5 s countdown, 120 s race, server ranking. |
| **Private Circuit** | 5 online | Same rules in a five-character room code shared with friends. |

### Systems

- **Deterministic procedural track** — 20 hand-authored obstacle patterns, validated for navigability and action spacing, composed by a seeded director so every online runner sees the same world without shipping obstacle state.
- **Movement** — three lanes, jump with variable hold height, slide with cooldown, near-miss scoring, hoverboard/shield recovery, lane interpolation.
- **10 power-ups** — magnet, shield, score surge, volt rush, spectre board, phase shell, air jump, time dilation (offline only), credit storm and rare prism dash.
- **Progression** — credits, XP/levels (1–100), season levels, 5 runners, 4 trails, permanent magnet/shield/multiplier/boost/head-start upgrades, daily and weekly missions, lifetime achievements, daily supply drops and local bests/leaderboards.
- **Online authority** — quick match and private codes, host-controlled start, server-filled bots to five, 10 Hz validated snapshots, results, verified rewards, leaderboard persistence, 45 s reconnect grace and automatic client reconnection.
- **Presentation** — runtime-generated URP world (four neon themes), original synthesized music/ambience/14 SFX, speed lines, particles, camera shake, screen fades, toasts and a complete runtime UI. No external art or audio packs.
- **Offline first** — every single-player mode works without the backend; online is never simulated when the server is absent.

### Controls

| Action | Keyboard | Gamepad | Touch |
|---|---|---|---|
| Change lane | `A` / `D`, `←` / `→` | Left stick left/right | Swipe left/right |
| Jump | `Space`, `W`, `↑` (hold for height) | South button | Swipe up |
| Slide | `S`, `↓` | East button | Swipe down |
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

### 2. Run the multiplayer authority

```bash
# From the project root
python3 Backend/server.py --host 0.0.0.0 --port 7777
```

Defaults: port `7777`, 5-second countdown, 120-second race, 45-second reconnect grace. Data is stored under `Backend/data/leaderboard.json`. Use `--data` to choose another directory. No Python packages are required.

In game, open **MULTIPLAYER**, confirm the host in **SETTINGS → NETWORK** (default `127.0.0.1:7777`), then **QUICK MATCH** or create/join a private code. All human runners press **READY**; the host presses **START CIRCUIT**. Empty slots are filled with server bots so a race always has five runners.

For a LAN session, start the server with `--host 0.0.0.0`, allow TCP 7777 through the firewall, and enter the host machine's LAN IP in each client's Network settings. The protocol is plain TCP and is intended for trusted networks; see the limitations.

### 3. Backend smoke test

```bash
python3 -m unittest discover -s Backend/tests -v
```

Expected: **9 tests pass**, covering two humans + bots, five humans, sixth-player rejection, private rooms, readiness/host rules, ranking/rewards, leaderboard persistence, integrity rejection, early-finish rejection, dead-run rewards, missing-finish fallback, reconnect, and malformed/unauthorised input.

---

## Repository map

```
Assets/Game/
  Scenes/Main.unity              Boot scene (runtime-generated game)
  Scripts/Core/                  Engine-independent deterministic core (NeonVelocity.Core)
  Scripts/                       Runtime game, UI, audio, world, online client
  Scripts/Editor/BuildTools.cs   Project configuration + platform build entry points
  Scripts/Editor/...             Editor-only build tooling
  Tests/EditMode/                NUnit core + save tests
  Tests/PlayMode/                Boot/menu/practice smoke test
  Resources/Audio/               21 original WAV assets (music, ambience, SFX)
Backend/
  server.py                      Authoritative asyncio TCP server
  tests/                         9 end-to-end backend tests
Tools/
  CoreTests/                     Headless .NET test runner for the gameplay core
  generate_audio.py              Reproducible original audio synthesis
  generate_meta.py               Stable Unity .meta generation
Documentation/
  SETUP.md                       Editor, controls, server and LAN setup
  BUILD.md                       Module installation and build commands
  TEST_REPORT.md                 Actually executed tests and unverified areas
  KNOWN_LIMITATIONS.md           Honest gaps and operational constraints
  SERVER_PROTOCOL.md             nv1 TCP wire protocol and authority rules
  ASSET_LICENSING.md             Ownership and third-party licence notes
```

### Assembly layout

- **NeonVelocity.Core** — no engine references; pure deterministic rules, track generation, save schema, race DTO/ranking and progression math. Runs in Unity and plain .NET.
- **NeonVelocity.Runtime** — Unity-facing assembly referencing URP, Input System and uGUI.
- **NeonVelocity.Tests.EditMode** / **PlayMode** — test assemblies gated by `UNITY_INCLUDE_TESTS`.

---

## Testing and builds

```bash
# Headless core tests (50 test cases)
dotnet run --project Tools/CoreTests/CoreTests.csproj -c Release

# Backend integration tests
python3 -m unittest discover -s Backend/tests -v
```

On macOS, if `dotnet` is not on PATH, use Unity's bundled SDK:

```bash
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet" \
  run --project Tools/CoreTests/CoreTests.csproj -c Release
```

Unity tests live in **Window → General → Test Runner**. Builds use **NEON VELOCITY → Configure Project and Build Settings**, then any **Build …** menu item. Batch commands and module requirements are in [BUILD.md](Documentation/BUILD.md).

**Build status:** no player builds were produced on this machine because the Unity editor lacks an active licence, and only macOS/WebGL support modules are installed. Android/iOS modules and the Android SDK are absent. The build scripts are present and compile, but platform behaviour remains unverified.

## Documentation

- [Setup guide](Documentation/SETUP.md)
- [Build guide](Documentation/BUILD.md)
- [Test report](Documentation/TEST_REPORT.md)
- [Known limitations](Documentation/KNOWN_LIMITATIONS.md)
- [Server protocol](Documentation/SERVER_PROTOCOL.md)
- [Asset licensing](Documentation/ASSET_LICENSING.md)

## Ownership

Original game code, design, generated audio, procedural art and documentation were produced for **RK VANTAGE GAMES**. No marketplace assets, third-party audio samples or external art are included. Unity engine/package components remain under Unity's licence terms. See [ASSET_LICENSING.md](Documentation/ASSET_LICENSING.md).
