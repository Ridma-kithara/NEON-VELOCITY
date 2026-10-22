# NEON VELOCITY — Test Report

**Date:** 22 October 2026

**Build under test:** source tree + macOS player, version 1.0.0

**Environment:** macOS 26.7.1 (Intel x86_64, Core i9-9980HK), Unity **6000.6.7f1** (`b22767f04f04`) with an active Personal licence, Unity bundled .NET SDK 8.0.318, Python 3.9.6

This report separates checks that were actually executed from work that remains unverified. A successful compile or smoke test is not a claim that every mode, device or network path has been exercised.

## 1. Result summary

| Check | Result | Status |
|---|---|---|
| Headless gameplay-core tests (`Tools/CoreTests`) | **50 / 50 passed** | Executed |
| Python authority integration tests (`Backend/tests`) | **9 / 9 passed** in 10.4 s | Executed |
| Static project integrity (`Tools/validate_project.py`) | **630 / 630 checks passed** | Executed |
| Unity 6000.6.7f1 project import and script compilation | **0 errors**; warnings cleaned | Executed |
| Unity EditMode tests | **19 / 19 passed** | Executed |
| Unity PlayMode smoke test | **1 / 1 passed** | Executed |
| macOS universal player build | **Succeeded** (116.8 MiB, x86_64 + ARM64) | Executed |
| macOS player launch | **Launched**, booted menu without runtime exceptions | Executed |
| WebGL player build | Not built | Unverified |
| Windows/Linux player builds | Not built | Unverified |
| Android APK/AAB | Not built | Unverified |
| iOS Xcode export | Not built | Unverified |
| Full manual playthrough of every mode | Not completed | Unverified |
| Real-device touch/gamepad/audio | Not performed | Unverified |
| Real-network two/five-client race | Not performed | Unverified |
| Performance/memory profiling, signing/notarisation | Not performed | Unverified |

**Conclusion:** the core logic, authority, Unity runtime bootstrap, EditMode coverage, PlayMode menu/practice smoke flow and a universal macOS player are now verified. The macOS build launches and runs without the earlier shader and input exceptions. Other platforms, broader gameplay coverage, real devices/networks and release packaging remain outstanding.

## 2. Headless core tests

**Command**

```bash
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet" \
  run --project Tools/CoreTests/CoreTests.csproj -c Release
```

**Result**

```text
Passed: 50  Failed: 0
CORE TESTS: PASS
```

The runner compiles the same `NeonVelocity.Core` sources used by Unity with no engine references. Coverage by area:

| Area | Cases | What is checked |
|---|---:|---|
| Lane math | 5 | lane positions, edge clamping, invalid input, smooth clamped interpolation |
| Speed model | 4 | monotonic/capped speed, boost, hostile config, distance envelope, difficulty stages |
| Track patterns and generation | 10 | parsing errors, full library navigability, blocked paths, action spacing, deterministic seeds, seed/salt variation, safe opening, generated-sequence validation, stable run seeds |
| Save schema and migration | 5 | null repair, corrupt value clamping, fresh-save stability, sparse upgrades, idempotent reward redemption |
| Progression and daily rewards | 8 | XP thresholds, cumulative levels, multi-level gains, level cap, score/XP/credit formulas, upgrade curves, season cap, streak/calendar rules |
| Deterministic RNG and hash | 4 | exact repeatability, zero-seed replacement, range bounds, stable non-negative hash |
| Missions and achievements | 4 | daily/weekly determinism, three unique missions, date keys, lifetime-stat mapping |
| Race rules | 6 | snapshot/finish plausibility, NaN and teleport rejection, ranking tiebreaks, placement assignment, rewards, name sanitisation |
| Mode definitions | 4 | 11 unique modes, online caps/durations, lookup fallback, practice/tutorial rules |

Earlier defect found and fixed by this work: `MissionFactory` compared a shortened key when de-duplicating missions, so two daily missions could share a final ID. It now compares the complete scoped ID and is covered across 120 dates.

## 3. Backend integration tests

**Command**

```bash
python3 -m unittest discover -s Backend/tests -v
```

**Result:** `Ran 9 tests in 10.356s — OK`

| Test | Coverage |
|---|---|
| `test_two_humans_bots_results_and_leaderboard_persistence` | auth, quick match, readiness, 3 bots, countdown, results, verified rewards, leaderboard restart persistence |
| `test_five_human_room_rejects_a_sixth_and_races_without_bots` | full five-human room, no bots, sixth player rejected, ranking |
| `test_private_room_code_readiness_and_bot_fill` | private code creation/join, room-full rules, bot fill on host start |
| `test_early_finish_is_rejected_and_snapshot_attack_is_disqualified` | early finish denied, out-of-envelope snapshots clamped, disqualification |
| `test_dead_finish_receives_no_competitive_rewards` | dead runner ranking without rewards |
| `test_missing_finish_packet_still_uses_validated_snapshots` | finalisation from last validated snapshots |
| `test_reconnect_token_restores_the_live_race` | disconnect, token reconnect, room/race restoration |
| `test_sanitisation_unknown_messages_and_second_hello` | sanitisation, unknown message type, repeated hello |
| `test_unauthenticated_request_is_rejected` | pre-auth commands rejected |

The suite uses real asyncio TCP connections against an in-process `RaceServer` and temporary leaderboard storage. No Unity process is involved.

## 4. Unity import and compilation

The licensed Unity editor imported the project and compiled all assemblies:

- **NeonVelocity.Core** — engine-independent gameplay rules.
- **NeonVelocity.Runtime** — game, UI, world, audio, online client.
- **NeonVelocity.EditorTools** — new editor-only assembly (`includePlatforms: Editor`) so `BuildTools.cs` cannot leak into runtime player compilation.
- **EditMode/PlayMode test assemblies**.

The original import exposed a real problem: `BuildTools.cs` was being compiled into the runtime assembly, causing `UnityEditor` types to fail during the player build. The new editor-only asmdef fixed it. Two obsolete API warnings (`FindObjectsSortMode`) and one unused field were also removed. The final Unity compile reports **0 errors**.

## 5. Unity EditMode tests

**Result: 19 / 19 passed.**

Coverage includes lane math, authored/generated track validation, deterministic seeds, 120 days of unique daily missions, hostile save migration, XP/level math, ranking/plausibility rules and name sanitisation, plus save round-trip, corrupt-primary recovery, reset and JSON import/export.

One stale assertion was found and corrected: the test expected level 2 at `TotalXpForLevel(3)`, but the one-based curve correctly returns level 3. The test now checks the actual level boundaries.

## 6. Unity PlayMode smoke test

**Result: 1 / 1 passed.**

`BootSmokeTests.AppBootsMainMenuAndStartsPractice` executes the real runtime bootstrap and checks:

- `GameApp` appears without scene wiring;
- save, profile, UI, main menu and title stage exist;
- product branding is `NEON VELOCITY`;
- Practice starts with a runner, generated track, input router, HUD and active segments;
- after the countdown, the real update loop advances the runner for 3.5 s;
- the run is not accidentally finished during the smoke window;
- the session shuts down cleanly.

The first PlayMode run found an unexpected Unity log assertion from setting `ParticleSystem.main.duration` while a newly created system was already playing. `VfxService` now stops each system before configuring it; the suite passes with no unexpected runtime logs.

## 7. macOS player build and launch

**Build command**

```bash
UNITY="/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity"
"$UNITY" -batchmode -quit -projectPath "/Users/ridma/RK VANTAGE GAMES/NEON VELOCITY" \
  -executeMethod NeonVelocity.EditorTools.BuildTools.BuildMacOS \
  -logFile "/tmp/neon-velocity-build.log"
```

**Build result**

```text
[BUILD] Succeeded: Builds/macOS/NeonVelocity.app (116.8 MiB)
Build Finished, Result: Success.
```

**Binary**

```text
Mach-O universal binary with 2 architectures:
  x86_64
  arm64
```

The app is ad-hoc signed (`Signature=adhoc`) and has no Developer ID or notarisation. It launched on the Intel host and remained running. The final Player.log contains no `ArgumentNullException`, no URP "shader stripped" messages and no Input System exceptions.

### Runtime fixes required for the player

1. **Editor-only build tooling** — split into `NeonVelocity.EditorTools` so runtime compilation does not reference `UnityEditor`.
2. **Universal macOS architecture** — `BuildMacOS` sets `OSXStandalone.UserBuildSettings.architecture = x64ARM64`; an earlier build was ARM64-only and could not open on the Intel host.
3. **Shader pinning** — runtime-created materials now instantiate six URP material templates under `Assets/Game/Resources/Materials/` (lit opaque/transparent, unlit opaque/transparent/additive, particles additive). This keeps URP/Lit and URP/Unlit out of player shader stripping.
4. **Post-processing shaders** — URP's unused-post-processing-resource stripping removed Bloom, DoF, motion blur and Panini shaders even though the game builds its volume profile at runtime. The project now retains those resources and also lists the hidden shaders as always-included.
5. **Input System** — replaced the last legacy `UnityEngine.Input.GetKeyDown` pause check with Input System keyboard/gamepad reads, eliminating per-frame `InvalidOperationException`s.

## 8. Static project integrity

**Command**

```bash
python3 Tools/validate_project.py
```

**Result**

```text
[meta] 104 assets, 104 unique GUIDs
[scene] build GUID b8e4b52cd50888fb8d01b634add84552
[branding] RK VANTAGE GAMES / NEON VELOCITY / games.rkvantage.neonvelocity
[audio] 21 mono PCM clips, 64.4s total
[sources] 45 runtime/editor scripts, 3 Unity test scripts
[docs] required documentation present
PASS: 630 checks
```

The validator checks meta/GUID integrity, build-scene configuration, empty boot scene, branding, orientation, Input System, colour space, Android settings, audio format, source markers and required documentation.

## 9. Not yet verified

| Gap | Reason |
|---|---|
| Full manual playthrough of all 11 modes | Not completed; PlayMode covers only Practice |
| Every power-up, pickup, upgrade, collision and result path | Not individually exercised at runtime |
| Two- and five-client races with real Unity clients | Backend loopback tests only; no multi-client Unity run |
| Reconnect/disconnect on real clients | Server logic tested; client UI path not exercised |
| Windows/Linux/WebGL/Android/iOS builds | Not built here; platform modules/tooling not all available |
| Touch, gamepad, audio balance, suspend/resume on devices | No real-device testing |
| Frame time, memory, battery, thermals | No profiling pass |
| macOS signing, notarisation, hardened runtime | No Developer ID/certificate configured |
| Store packaging, privacy metadata, release channels | Not implemented |
| Public-internet multiplayer/TLS | Plain TCP by design; no relay/TLS/auth deployment |

## 10. Reproduce the verified checks

```bash
# 1. 50 headless core tests
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet" \
  run --project Tools/CoreTests/CoreTests.csproj -c Release

# 2. 9 authority integration tests
python3 -m unittest discover -s Backend/tests -v

# 3. 630 static checks
python3 Tools/validate_project.py

# 4. Unity EditMode tests
UNITY="/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity"
"$UNITY" -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode \
  -testResults /tmp/nv-editmode.xml -logFile /tmp/nv-editmode.log

# 5. Unity PlayMode smoke test
"$UNITY" -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode \
  -testResults /tmp/nv-playmode.xml -logFile /tmp/nv-playmode.log

# 6. Build and launch the universal macOS player
"$UNITY" -batchmode -quit -projectPath "$PWD" \
  -executeMethod NeonVelocity.EditorTools.BuildTools.BuildMacOS \
  -logFile /tmp/nv-build-macos.log
open "Builds/macOS/NeonVelocity.app"
```
