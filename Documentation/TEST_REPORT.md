# NEON VELOCITY — Test Report

**Date:** 29 October 2026

**Build under test:** Phase 03 source tree + rebuilt macOS player, version 1.0.0

**Environment:** macOS 26.7.1 (Intel x86_64, Core i9-9980HK), Unity **6000.6.7f1** (`b22767f04f04`) with an active Personal licence, Unity bundled .NET SDK 8.0.318, Python 3.9.6

This report separates checks that were actually executed from work that remains unverified. A successful compile or smoke test is not a claim that every mode, device or network path has been exercised.

## 1. Result summary

| Check | Result | Status |
|---|---|---|
| Headless gameplay-core tests (`Tools/CoreTests`) | **66 / 66 passed** | Executed |
| Python backend integration tests (`Backend/tests`) | **79 / 79 passed** (race authority, accounts/auth, premium wallet, catalogue/admin, Stripe/Apple/Google verification, receipts, operator CLI and package configuration) | Executed |
| Static project integrity (`Tools/validate_project.py`) | **936 / 936 checks passed** | Executed |
| Unity 6000.6.7f1 project import and script compilation | **0 errors** in the final build/capture path | Executed |
| Unity EditMode tests | **43 / 43 passed** | Executed |
| Unity PlayMode tests | **18 / 18 passed**, including a real 30 km streaming run and Phase 03 shop assertions | Executed |
| macOS universal player build | **Succeeded** (x86_64 + ARM64, ad-hoc signed; **122.5 MiB, 27.3 s build / 39.0 s wall**; rebuilt from Phase 03 source after the final label fix) | Executed |
| macOS player launch | **Launched**, booted menu and completed capture run without runtime exceptions | Executed |
| In-app UI/gameplay visual capture | Fresh **18-screen Phase 03 set at 1280×800** captured from the final post-fix build and inspected, including the rebuilt GEM shop; 1600×900 and 3840×2160 remain the Phase 02 sets | Executed (Phase 03 16:10 set) |
| 4K render/capture | **Actual 3840×2160 PNGs** for all 18 Phase 02 screens | Executed |
| WebGL player build | Not built | Unverified |
| Windows/Linux player builds | Not built | Unverified |
| Android APK/AAB | Not built | Unverified |
| iOS Xcode export | Not built | Unverified |
| Live provider purchase (Stripe/Apple/Google) | No real transaction completed | Unverified |
| Full manual playthrough of every mode | Not completed | Unverified |
| Real-device touch/gamepad/audio | Not performed | Unverified |
| Audible mix / loudness / spatial balance | 32 clips loaded without missing-resource errors; not auditioned | Unverified |
| Real-network two/five-client race | Not performed | Unverified |
| Performance/memory profiling, signing/notarisation | Not performed | Unverified |

**Conclusion:** the core logic, authority, Unity runtime bootstrap, expanded EditMode/PlayMode coverage, contextual navigation, settings presets/persistence, garage preview lifecycle, purchase-confirmation guard, infinite progression, twenty-district 3 km zoning, seven-state time-of-day resolution, v5 save migration, exact-position revive, genuine nine-shape junction routing and Phase 03 shop/payment UI are verified for the routes listed in [QA_REPORT.md](../QA_REPORT.md). A PlayMode test drives the real `TrackManager` through 30,030 m and checks the 3/6/9/15/30 km checkpoints, every district announcement, at least 40 junction windows, non-decreasing difficulty, bounded 14-segment/pool budgets and a save round-trip. The premium/account service is verified end-to-end at the HTTP/SQLite layer (79 tests across race authority, accounts/auth, wallet/catalogue, Stripe/Apple/Google verification, receipts, refunds and operator CLI), including idempotent purchases/revives and **no live provider sale is claimed**. Stripe, Apple and Google are exercised with stubs/generated keys; Apple/Google store billing and native sign-in remain unconfigured. The visual pass was repeated on the final Phase 03 build as a fresh 18-screen 1280×800 set, including the post-fix GEM shop; the 1600×900 and actual 3840×2160 sets are the Phase 02 captures. The player-build black-world defect was fixed by removing the broken SSAO renderer feature, disabling URP unused-variant stripping for runtime-built materials and always including `UberPost`. No physical 4K display or 4K performance profile was available. Other platforms, broader manual gameplay coverage, real devices/networks and release packaging remain outstanding.

## 2. Headless core tests

**Command**

```bash
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet" \
  run --project Tools/CoreTests/CoreTests.csproj -c Release
```

**Result**

```text
Passed: 66  Failed: 0
CORE TESTS: PASS
```

The runner compiles the same `NeonVelocity.Core` sources used by Unity with no engine references. Coverage by area:

| Area | Cases | What is checked |
|---|---:|---|
| Lane math | 5 | lane positions, edge clamping, invalid input, smooth clamped interpolation |
| Speed model | 4 | monotonic/capped speed, boost, hostile config, distance envelope, difficulty stages |
| Track patterns and generation | 10 | parsing errors, full library navigability, blocked paths, action spacing, deterministic seeds, seed/salt variation, safe opening, generated-sequence validation, stable run seeds |
| Junctions | 10 | deterministic 15-segment scheduling, all nine kinds without immediate repeat, curved route offsets/slopes on the deck, centre-lane safe commitment, connected/navigable branch rows, valid generated junction tracks, 126 m warning/committed branch phases, lane-lock fairness, narrow-bridge centre commitment, broken-road neighbour suppression |
| Save schema and migration | 5 | null repair, corrupt value clamping, fresh-save stability, sparse upgrades, idempotent reward redemption |
| Progression and daily rewards | 8 | exact L1–L10 total-XP thresholds, post-10 growth/cap, cumulative levels, multi-level gains, no-loss accounting, score/XP/credit formulas, upgrade curves, season cap, streak/calendar rules |
| Locations | 5 | exact 3 km zone mapping, 20 unique districts, no back-to-back repeats, all 20 required themes present and distinct, valid weather/time-of-day/style/signature, discovery memory |
| Time of day | 1 | seven authored states, 21 km cycle ring, progressive blending and authored/dynamic/fixed resolution |
| Deterministic RNG and hash | 4 | exact repeatability, zero-seed replacement, range bounds, stable non-negative hash |
| Missions and achievements | 4 | daily/weekly determinism, three unique missions, date keys, lifetime-stat mapping |
| Race rules | 6 | snapshot/finish plausibility, NaN and teleport rejection, ranking tiebreaks, placement assignment, rewards, name sanitisation |
| Mode definitions | 4 | 15 unique modes, online caps/durations, lookup fallback, practice/tutorial rules |

Earlier defect found and fixed by this work: `MissionFactory` compared a shortened key when de-duplicating missions, so two daily missions could share a final ID. It now compares the complete scoped ID and is covered across 120 dates.

## 3. Backend integration tests

**Command**

```bash
python3 -m unittest discover -s Backend/tests -v
```

**Result:** `Ran 79 tests — OK`

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

The race suite uses real asyncio TCP connections against an in-process `RaceServer` and temporary leaderboard storage. No Unity process is involved.

### Premium, payment, account and admin HTTP suite

`Backend/tests/test_premium_server.py` starts the real dependency-free HTTP service against a temporary SQLite database and exercises:

- health/security headers, guest authentication and token expiry/rejection;
- the five fixed GEM tiers (`STARTER` 80, `BASIC` 210, `PREMIUM` 550, `MEGA` 1380, `ULTIMATE` 3000), immutable GEM counts under admin edits and validated package overrides;
- admin authentication/rate limiting, catalogue overrides for names, descriptions, reference prices and enabled state;
- admin transaction/account review, cross-database account detail and GEM refund reversal after provider confirmation;
- insufficient then credited purchase, duplicate request idempotency, character entitlement projection and duplicate-purchase balance preservation;
- sequential upgrade levels/max handling and revive debit-once/idempotent replay;
- email registration/verification/login/profile/logout, password reset token rotation and phone OTP;
- Google/Apple token-verification configuration and provider-disabled responses;
- cloud-sync monotonicity, unsupported-schema rejection and refusal to trust client-supplied GEMS;
- Stripe capability reporting, unconfigured checkout, signed-webhook one-time credit and refund reversal, invalid-signature rejection, and hosted-checkout use of server-side price IDs;
- Apple receipt adapter and Google Play service-account verification with injected/stubbed provider responses;
- receipt construction (game name, player/account id, transaction id, item, amount, currency, provider, date, status) and duplicate handling.

### Account store suite

`Backend/tests/test_account_store.py` covers the persistence layer directly: registration, login, verification, duplicate-email and password-policy rejection, reset-token revocation, generic unknown-reset responses, phone OTP sign-in and replay limits, OTP attempt caps, provider-disabled login, guest credential linking, monotonic sync that never trusts GEMS, unsupported-schema rejection and idempotent transaction records.

### Provider identity suite

`Backend/tests/test_phase03_identity.py` validates Apple JWKS/RS256 identity-token verification and Google service-account token handling with generated keys and injected HTTP responses. No real Google or Apple account is contacted.

### Operator CLI suite

`Backend/tests/test_premium_admin.py` drives `premium_admin.py` against a live ephemeral server: catalogue listing/editing, account search/detail, transaction listing and the `--confirm`/range guards on refund and catalogue mutations.

### GEM package configuration suite

`Backend/tests/test_package_config.py` verifies that all five required tiers exist, that `NV_GEM_PACKAGE_<ID>_*` environment overrides change amounts/prices/names/descriptions/availability, and that invalid override values fall back to safe defaults.

Stripe, Apple and Google paths are exercised against stubs/generated keys, not live merchant accounts. **No real card, App Store or Play Store transaction was completed.**

## 4. Unity import and compilation

The licensed Unity editor imported the project and compiled all assemblies:

- **NeonVelocity.Core** — engine-independent gameplay rules.
- **NeonVelocity.Runtime** — game, UI, world, audio, online client.
- **NeonVelocity.EditorTools** — new editor-only assembly (`includePlatforms: Editor`) so `BuildTools.cs` cannot leak into runtime player compilation.
- **EditMode/PlayMode test assemblies**.

The original import exposed a real problem: `BuildTools.cs` was being compiled into the runtime assembly, causing `UnityEditor` types to fail during the player build. The new editor-only asmdef fixed it. Two obsolete API warnings (`FindObjectsSortMode`) and one unused field were also removed. The final Unity compile reports **0 errors**.

## 5. Unity EditMode tests

**Result: 43 / 43 passed.**

Coverage includes lane math, authored/generated track validation, deterministic seeds, the city-first opening biome, junction scheduling and fairness including all nine junction kinds, curved-route offsets, centre-lane safe commitment and a 30 km junction/location contract, 120 days of unique daily missions, hostile save migration, exact L1–L10 XP thresholds and post-10 scaling, 20-district 3 km zoning/discovery, seven-state time-of-day resolution, signature-hazard row fairness, ranking/plausibility rules and name sanitisation, save round-trip including display/render/time-of-day settings and master volume, v4 legacy-XP migration, premium-cache normalisation, corrupt-primary recovery, reset and JSON import/export. Added Phase 03 tests cover transaction status/amount/provider display labels. Existing settings tests cover hostile display/quality clamps, Low/Ultra preset expansion and Custom preservation; the save test round-trips resolution, fullscreen mode, render scale, textures, shadows, reflections, AA, effects, view distance, master volume and time-of-day mode/phase/cycle.

One headless-core assertion was found and corrected during this pass: it expected `graphicsPreset` to clamp to 3 (the old maximum) after Custom (4) was introduced. The assertion now expects 4. A self-recursive `LevelProgression.MaxTotalXp` path was also found by the new exact-threshold tests; it was replaced with a cached closed-form calculation so granting XP can no longer overflow the stack.

## 6. Unity PlayMode tests

**Result: 18 / 18 passed.**

`LongRunTests.TrackStreamsThirtyKilometresWithinBoundedPoolsAndZones` starts a real Practice session, pauses the runner and drives the live `TrackManager` across 30,030 m in 42 m steps. It asserts the 3/6/9/15/30 km district checkpoints and every `LocationChanged` announcement, at least 40 distinct junction windows, non-decreasing difficulty to tier 8, a non-growing 6–14-segment window, hazard/pickup pools proportional to that window, and reloads the save to verify the time-of-day settings survive. It is a streaming/world contract test; it does not claim a human played 30 km at normal speed.

`BootSmokeTests.AppBootsMainMenuAndStartsPractice` executes the real runtime bootstrap and checks the menu, save, profile, title stage, Practice run, HUD and clean shutdown. `QuickTapJumpClearsLowBarrierHeight` drives a real short jump through the input/runtime path and verifies a low-barrier clearance. `PremiumRunnerAbility_ActivatesWithCooldown` equips PHANTOM X, starts a real run, checks PHASE DASH activation and prevents repeated presses from refreshing it. `Revive_RestoresExactDistanceWithoutResettingTheRun` captures the crash state, kills the runner, restores it and asserts the same distance/lane/score without a world reset.

`NavigationTests` adds thirteen end-to-end UI tests:

- Back from Shop returns to Modes and Back from Modes returns to Main Menu without stale history;
- Back dismisses a quit confirmation before leaving the title screen, then title Back opens the confirmation;
- Settings opened from Pause returns to Pause, then Back resumes the run;
- Results Back returns to Main Menu with no stale history;
- Restart rebuilds the Practice session, preserves the mode, starts at the start line and leaves the new run unpaused;
- the garage preview camera/texture/stage are destroyed when Shop closes;
- a locked-runner purchase opens confirmation without unlocking or charging, and repeated clicks do not stack a second dialog;
- the GEM SHOP builds offline, every one of its three tabs can be selected, the tabs read BUY GEMS / PREMIUM STORE / TRANSACTION HISTORY, retired bank-transfer/upload/proof/order-status copy is absent, the offline history tab explains that the store is unavailable, and Character/Upgrades/Back navigation remains intact;
- changing the premium endpoint normalises the URL, clears the cached GEM balance/orders/token and leaves earned CREDITS untouched;
- Account, Events, World Atlas and Achievements build, show their content and return to the main menu;
- every main-menu destination label has a visible, non-clipped title rect;
- HUD score and distance use independent tickers and both advance during a run;
- pausing hides the HUD and dismisses in-flight toasts and resuming restores the HUD, so neither can overlap the pause panel.

The earlier PlayMode run also caught a real critical navigation defect: `NavigateBack()` rebuilt the previous screen without updating `_currentScreen`, so subsequent Back/Settings-from-Pause logic operated on stale state. The fix and regression tests are described in [QA_REPORT.md](../QA_REPORT.md).

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
[BUILD] Succeeded: Builds/macOS/NeonVelocity.app (122.5 MiB, 27.3s)
Build Finished, Result: Success.
```

The batch invocation measured **39.0 s wall** including Unity startup.

**Binary**

```text
Mach-O universal binary with 2 architectures:
  x86_64
  arm64
```

The app is ad-hoc signed (`Signature=adhoc`) and has no Developer ID or notarisation. It launched on the Intel host, completed the full 18-screen capture run and exited cleanly. The final Player.log contains no `ArgumentNullException`, no URP "shader stripped" messages and no Input System exceptions.

### Runtime fixes required for the player

1. **Editor-only build tooling** — split into `NeonVelocity.EditorTools` so runtime compilation does not reference `UnityEditor`.
2. **Universal macOS architecture** — `BuildMacOS` sets `OSXStandalone.UserBuildSettings.architecture = x64ARM64`; an earlier build was ARM64-only and could not open on the Intel host.
3. **Shader pinning** — runtime-created materials now instantiate six URP material templates under `Assets/Game/Resources/Materials/` (lit opaque/transparent, unlit opaque/transparent/additive, particles additive). This keeps URP/Lit and URP/Unlit out of player shader stripping.
4. **Post-processing shaders** — URP's unused-post-processing-resource stripping removed Bloom, DoF, motion blur, Panini and UberPost shaders even though the game builds its volume profile at runtime. The project now retains those resources and lists the hidden shaders as always-included.
5. **Input System** — replaced the last legacy `UnityEngine.Input.GetKeyDown` pause check with Input System keyboard/gamepad reads, eliminating per-frame `InvalidOperationException`s.
6. **Track mesh in the player** — the two-stage `Mesh.CombineMeshes` path produced zeroed vertex positions in the macOS player, so the generated road and city were frustum-culled. `DecorMeshBuilder` now copies and transforms vertex/triangle/normal/UV streams directly, rebuilds tangents and bounds, and the track renders in the verified player.
7. **SSAO renderer feature** — `ScreenSpaceAmbientOcclusion` remained in `PC_Renderer.asset`, but its stripped resources threw inside `TryPrepareResources` while URP constructed the renderer. The feature was removed; the PC renderer now uses Forward+ with native render pass and no renderer features.
8. **Runtime material variant stripping** — even after the SSAO removal, URP unused-variant stripping removed variants that runtime-created world materials require, so the player drew only the camera clear colour. `m_StripUnusedVariants` is now `0` in all three global-settings locations, and `Hidden/Universal Render Pipeline/UberPost` is in Always Included Shaders. An offscreen camera probe returned only the clear colour before the fix and scene content after it.

### Visual capture pass

The built player was run with the developer-only `-nv-capture <directory> [-nv-capture-size WxH]` switch. The harness captured 18 screens through a synchronous `ScreenCapture.CaptureScreenshotAsTexture()` readback: main menu, settings, premium-network settings, shop/garage, mode select, account sign-in, account recovery, account profile, events, world atlas, achievements, characters, five-tier GEM packages, two live Practice frames, a mid-biome transition, a subway/station frame and pause. The transition frame is held 0.65 s into a real 2.6 s crossfade. The subway frame is reached by temporarily accelerating game time with `Time.timeScale = 6` for up to a 240 s wall-clock window and clearing nearby hazards; it is a visual smoke test of the seeded biome, not a normal-speed manual playthrough. The harness re-asserts the requested resolution before every readback because macOS can recreate the player surface at the display's native backbuffer size.

The pass was repeated at **1600×900**, **1280×800 (16:10)** and **3840×2160**. `sips` confirmed the PNG dimensions; the 4K files are genuinely 3840×2160 for all 18 screens. The menu, runner card, premium-network settings, garage turntable, mode cards, account tabs, event calendar, world atlas, achievements, character roster, five-tier GEM packages, beveled runner, rain-slicked road, raised sidewalks, curb/gutter detail, recessed stone podiums, tiered storefronts with shelving/counters, awnings and mullions, street trees with visible trunks, lamp pools, billboard/blade signage, layered skyline, subway ceiling/walls/platform edge/columns/signage, textured rain, live HUD and pause overlay were inspected at each size. The 1600×900 and 4K sets show the **pre-Phase 03** shop layout. On the final Phase 03 build the pass was repeated at **1280×800**, producing a fresh 18-screen set that includes the rebuilt GEM shop, provider/capability copy and receipt section; the post-capture label-clipping fix is visible in that set. The HUD shows independent score and distance values, and pausing hides the HUD and dismisses in-flight toasts so neither draws over the pause panel. The transition frame showed normal tunnel geometry with blended purple/blue lighting and no scene-wide dark overlay. The final player log contained no runtime exceptions or warnings, and the build log contained only Unity licensing and Intel-editor deprecation notices.

One automated run produced a tiled/duplicated-UI readback frame; the synchronous harness was already in use, and immediate clean reruns produced normal frames. The final Phase 03 1280×800 set and the Phase 02 1600×900/4K sets used for this report were visually inspected. Evidence is stored in `Captures/phase03/1280x800`, `Captures/final/1600x900`, `Captures/final/1280x800` and `Captures/final/3840x2160`; the final build log and EditMode/PlayMode XML are in `Logs/`. The cause was not isolated, so this remains a QA capture-readback limitation; live physical-display behaviour has not been independently captured. This is an automated visual/UI smoke check, not a full manual playthrough or listening test; physical 4K-display behaviour, 4K frame rate and audio balance remain unverified.

## 8. Static project integrity

**Command**

```bash
python3 Tools/validate_project.py
```

**Result**

```text
[meta] 143 assets, 143 unique GUIDs
[scene] build GUID b8e4b52cd50888fb8d01b634add84552
[branding] RK VANTAGE GAMES / NEON VELOCITY / games.rkvantage.neonvelocity
[audio] 32 mono PCM clips, 112.5s total
[sources] 65 runtime/editor scripts, 6 Unity test scripts
[docs] required documentation present
PASS: 936 checks
```

The validator checks meta/GUID integrity, build-scene configuration, empty boot scene, branding, orientation, Input System, colour space, Android settings, audio format, source markers and required documentation, including the development-progress record.

## 9. Not yet verified

| Gap | Reason |
|---|---|
| Full manual playthrough of all 15 modes | Not completed; PlayMode covers Practice plus jump, ability, revive and UI routes |
| Audible audio mix and spatial balance | 32 clips load without missing-resource errors; trigger path not audibly validated and no loudness measurement was performed |
| Every power-up, pickup, junction branch, upgrade, collision and result path | Not individually exercised at runtime |
| Live provider purchase (Stripe/Apple/Google) | No HTTPS deployment, merchant credentials, store products, test account or real transaction was supplied. Checkout opening is not success; GEMS require server verification |
| Real iOS/Android Unity IAP purchase | Unity IAP is integrated and compiles, but no store product, merchant account or device build was available |
| Live email/SMS/Apple/Google identity | No SMTP/SMS credentials or native Google/Apple sign-in plugin. Server token verifiers are tested with generated keys |
| Unity HTTP purchase/revive against a deployed premium server | Client protocol compiles and offline/insufficient paths are tested; live HTTP integration remains a deployment QA step |
| Drone chases, moving platforms, destructible sections, temporary vehicles, explicit Friend Challenge/Limited-Time Event entries, full 90° T-turn/curve junctions | Not implemented; see Known Limitations |
| Physical 4K display, HDR and 4K frame rate | 3840×2160 backbuffer capture succeeded, but only a 1600×900 host display was available |
| Live confirmation that the intermittent macOS screenshot-readback artifact cannot appear on a physical display | One tiled/duplicated-UI readback frame occurred; clean reruns and final captures passed, but live compositor capture was unavailable |
| LOD, occlusion and dynamic foliage wind | Not implemented in this environment slice; merged static geometry was chosen for draw-call control |
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
# 1. 66 headless core tests
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet" \
  run --project Tools/CoreTests/CoreTests.csproj -c Release

# 2. 79 backend tests (race authority + accounts/auth + premium/payment + operator CLI)
python3 -m unittest discover -s Backend/tests -v

# 3. 936 static checks
python3 Tools/validate_project.py

# 4. Unity EditMode tests
UNITY="/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity"
"$UNITY" -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode \
  -testResults /tmp/nv-editmode.xml -logFile /tmp/nv-editmode.log

# 5. Unity PlayMode tests
"$UNITY" -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode \
  -testResults /tmp/nv-playmode.xml -logFile /tmp/nv-playmode.log

# 6. Build and launch the universal macOS player
"$UNITY" -batchmode -quit -projectPath "$PWD" \
  -executeMethod NeonVelocity.EditorTools.BuildTools.BuildMacOS \
  -logFile /tmp/nv-build-macos.log
open "Builds/macOS/NeonVelocity.app"
```
