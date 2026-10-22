# NEON VELOCITY — Test Report

**Date:** 22 October 2026  
**Build under test:** source tree, version 1.0.0  
**Environment:** macOS 26.7.1 (x86_64), Unity 6000.6.7f1 installed, Unity bundled .NET SDK 8.0.318, Python 3.9.6  
**Unity licence:** none active (`Unity Hub` reported no user entitlements; editor startup exited 198)

This report distinguishes **executed and passing** checks from **written but unexecuted** tests. It is not a claim that the game has been played in Unity on this machine.

## 1. Result summary

| Check | Result | Status |
|---|---|---|
| Headless gameplay-core tests (`Tools/CoreTests`) | **50 / 50 passed** | Executed |
| Python authority integration tests (`Backend/tests`) | **9 / 9 passed** (10.4 s) | Executed |
| Runtime C# compile against Unity 6000.6.7f1 assemblies | **0 errors, 0 warnings** | Executed |
| Editor build-script compile (`BuildTools.cs`) against UnityEditor assemblies | **0 errors, 0 warnings** | Executed |
| Unity test-source compile (EditMode + PlayMode, NUnit/TestRunner) | **0 errors, 0 warnings** | Executed (compile only) |
| Static project integrity (`Tools/validate_project.py`) | **606 checks passed** | Executed |
| Unity EditMode tests (19 cases) | Not run | **Blocked by licence** |
| Unity PlayMode smoke test (1 case) | Not run | **Blocked by licence** |
| Scene import / Play Mode manual playthrough | Not run | **Blocked by licence** |
| macOS player build | Not built | **Blocked by licence** |
| WebGL player build | Not built | **Blocked by licence** |
| Windows/Linux builds | Not built | Modules not installed + licence |
| Android APK/AAB build | Not built | Android module/SDK absent + licence |
| iOS Xcode export | Not built | iOS module absent + licence |
| Real-device input/audio/network validation | Not performed | No builds available |

**Conclusion:** the deterministic gameplay logic, authority and build tooling compile and pass their automated suites. The Unity-facing runtime and platform builds remain unverified at runtime. See [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md).

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
| Track patterns and generation | 10 | pattern parsing errors, full library navigability, blocked paths, action spacing, deterministic seeds, variation by seed/salt, safe opening, generated-sequence validation, stable run seeds |
| Save schema and migration | 5 | null repair, corrupt value clamping, fresh-save stability, sparse upgrades, bounded idempotent reward redemption |
| Progression and daily rewards | 8 | XP thresholds, cumulative levels, multi-level gains, cap at 100, score/XP/credit formulas, upgrade curves, season cap, streak/calendar rules |
| Deterministic RNG and hash | 4 | exact repeatability, zero-seed replacement, range bounds, stable non-negative hash |
| Missions and achievements | 4 | daily/weekly determinism, three unique missions, date keys, lifetime-stat mapping |
| Race rules | 6 | snapshot/finish plausibility, NaN and teleport rejection, ranking tiebreaks, placement assignment, placement rewards, name sanitisation |
| Mode definitions | 4 | 11 unique modes, online caps/durations, lookup fallback, practice/tutorial rules |

**Real defect found and fixed by this suite:** `MissionFactory` compared a shortened mission key when de-duplicating, so two daily missions could share the same final ID. It now compares the complete scoped ID and was exercised across 120 consecutive dates in Unity tests plus deterministic runs here.

## 3. Backend integration tests

**Command**

```bash
python3 -m unittest discover -s Backend/tests -v
```

**Result:** `Ran 9 tests in 10.375s — OK`

| Test | Coverage |
|---|---|
| `test_two_humans_bots_results_and_leaderboard_persistence` | auth, quick match, readiness, 3 bots, countdown, results, verified rewards, leaderboard survives server restart |
| `test_five_human_room_rejects_a_sixth_and_races_without_bots` | full five-human room, no bots, sixth human rejected, ranking |
| `test_private_room_code_readiness_and_bot_fill` | private code creation/join, room full rules, bot fill on host start |
| `test_early_finish_is_rejected_and_snapshot_attack_is_disqualified` | early finish denied, out-of-envelope snapshots clamped, disqualification after repeated violations |
| `test_dead_finish_receives_no_competitive_rewards` | dead runner ranking without competitive rewards |
| `test_missing_finish_packet_still_uses_validated_snapshots` | finalisation uses last validated snapshots when finish is lost |
| `test_reconnect_token_restores_the_live_race` | disconnect, token reconnect, room/race state restoration |
| `test_sanitisation_unknown_messages_and_second_hello` | name/code sanitisation, unknown type, repeat hello |
| `test_unauthenticated_request_is_rejected` | pre-auth commands rejected |

The suite uses real asyncio TCP connections against a locally started `RaceServer` and temporary leaderboard storage. No Unity process is involved.

Earlier backend defects fixed during development and covered above include: a room factory/handler name collision that broke private-room creation, duplicate room creation paths, early finish acceptance, scoring/coin caps relative to distance, dead-run rewards, client-trusted results, leaked finished-room state as rewards, and missing-finish finalisation.

## 4. Compile checks

Three temporary MSBuild projects referenced the installed Unity 6000.6.7f1 managed assemblies, URP, Input System, uGUI, TestRunner and NUnit:

| Harness | Sources | Result |
|---|---|---|
| runtime | all `Assets/Game/Scripts` except `Editor/` | Build succeeded, 0 warnings, 0 errors |
| editor | `Assets/Game/Scripts/Editor/BuildTools.cs` + UnityEditor assemblies | Build succeeded, 0 warnings, 0 errors |
| tests | runtime + all `Assets/Game/Tests` + TestRunner/NUnit | Build succeeded, 0 warnings, 0 errors |

The test harness compile confirms that the NUnit/Unity Test Framework APIs, save-service temp-directory constructor and PlayMode smoke code type-check. It **does not execute** the tests.

Two unused fields and an unused font cache were removed after the first compile check, taking the runtime harness from three warnings to zero.

Unity's own import/compilation was not run. Compiling against Unity's managed assemblies is strong syntax/API evidence, but it cannot validate asmdef resolution, serialization, importer behavior, shaders or scene bootstrap in the editor.

## 5. Static project integrity

**Command**

```bash
python3 Tools/validate_project.py
```

**Result (after all documentation is present):**

```text
[meta] 96 assets, 96 unique GUIDs
[scene] build GUID b8e4b52cd50888fb8d01b634add84552
[branding] RK VANTAGE GAMES / NEON VELOCITY / games.rkvantage.neonvelocity
[audio] 21 mono PCM clips, 64.4s total
[sources] 45 runtime/editor scripts, 3 Unity test scripts
[docs] required documentation present
PASS: 606 checks
```

It checks:

- every `Assets/Game` file/folder has exactly one `.meta`, no orphan metas, unique 32-hex GUIDs;
- `EditorBuildSettings.asset` points at `Assets/Game/Scenes/Main.unity` with the matching meta GUID and only one build scene;
- the boot scene is intentionally empty (`m_Roots: []`);
- company/product/version/bundle ID, landscape orientation, Input System, linear colour, Android ARM64/IL2CPP/min SDK 26/internet permission;
- 21 WAV files are mono, 16-bit PCM, 22.05 kHz and non-empty;
- no `TODO`, `FIXME`, `NotImplementedException` markers in runtime, test or backend source;
- the core/test asmdefs and all required documents exist.

Static validation cannot replace Unity's importer. Manually generated `.meta` files in particular should be confirmed by the first licensed editor import.

## 6. Written but unexecuted Unity tests

### EditMode — 19 cases

`Assets/Game/Tests/EditMode/CoreGameplayTests.cs` (15 NUnit cases):

- lane clamping/interpolation;
- authored pattern library navigability;
- blocked rows and full-width spacing rejection;
- generated tracks safe/valid for 4 seeds;
- deterministic and seed/salt-varied track descriptions;
- 120 consecutive days of unique daily missions;
- hostile save migration;
- XP curve and level gains;
- ranking and plausibility rules;
- name sanitisation.

`Assets/Game/Tests/EditMode/SaveServiceTests.cs` (4 cases):

- save/reload round-trip of credits, XP, character, settings;
- corrupt primary file recovery from atomic backup;
- reset-to-default profile;
- JSON export/cloud-import path in an isolated temp folder.

### PlayMode — 1 case

`Assets/Game/Tests/PlayMode/BootSmokeTests.cs`:

- waits for `GameApp` runtime bootstrap;
- verifies UI/save/profile/menu/menu-stage and product branding;
- starts a Practice session, verifies runner/track/input/HUD and generated segments;
- confirms the session shuts down cleanly.

All three test files and asmdefs compile against NUnit/Unity TestRunner. They have **not** been executed because the editor cannot start without a licence.

### Manual checklist still required

- First import resolves all packages, asmdefs, metas and shaders without console errors.
- Main menu renders at 16:9 and ultrawide; UI is readable and navigable with mouse, keyboard and gamepad.
- Every mode starts, reaches its end condition and shows results.
- Crashes, near misses, slides, jumps, all power-ups, upgrades and pickups behave correctly.
- Save/progression persists across play sessions and survives corrupt-file injection.
- Online two-client race, five-client race, private room, bots, disconnect/reconnect, results and leaderboard on real running clients.
- Audio balance, music transitions and browser/mobile autoplay behavior.
- Touch and controller input on target hardware.
- Platform build launch and signing.

## 7. What was not tested and why

| Gap | Reason |
|---|---|
| Unity editor import, console, Play Mode | No active Unity entitlement; editor exits 198 before import |
| Unity EditMode/PlayMode execution | Same |
| macOS/WebGL builds | Same (modules are installed, licence is not) |
| Windows/Linux builds | Modules not installed |
| Android/iOS builds | Modules absent; no Android SDK/adb; no device testing |
| Frame rate/performance/memory profiling | No runnable player/editor |
| Real network conditions, latency, loss, NAT/firewall | Only loopback TCP integration tests executed |
| Store signing, notarisation, provisioning | Requires platform accounts/certificates |
| Screenshots/trailers | No editor to render the game |

## 8. Reproducing the executed checks

```bash
# 1. 50 core tests
dotnet run --project Tools/CoreTests/CoreTests.csproj -c Release

# 2. 9 authority integration tests
python3 -m unittest discover -s Backend/tests -v

# 3. Static project integrity
python3 Tools/validate_project.py
```

If `dotnet` is absent, use the SDK bundled with Unity on macOS:

```bash
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet" \
  run --project Tools/CoreTests/CoreTests.csproj -c Release
```

Unity tests require opening the project with a licensed editor and using **Window → General → Test Runner**.
