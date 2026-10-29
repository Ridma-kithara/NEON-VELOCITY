# NEON VELOCITY — Phase 01 Final Report

**Date:** 28 October 2026  
**Scope:** end-to-end Unity runner audit, 3 km environment transitions, seven-state time of day, connected junction navigation, moving hazards and long-distance verification  
**Engine:** Unity 6000.6.7f1 / URP 17.6 / Input System 1.20  
**Host:** macOS 26.7.1, Intel Core i9-9980HK, 1600 × 900 display  
**Status:** implementation and automated verification complete for the routes below; live services, other platforms and a full manual playthrough remain unverified.

This report is deliberately split into what was executed and what was not. A passing compile, test or 30 km stream is not a claim that a human played every mode, that every visual theme was inspected at 4K, or that payments are live.

---

## 1. Discovered bugs

| # | Severity | Defect | Root cause |
|---|---|---|---|
| 1 | Critical | Several 3 km districts had no landmark | The twenty-theme remap left signature IDs above the 0–19 `BuildSignature` switch; the default branch returned silently |
| 2 | High | Seven purpose-built architecture families were unreachable | Styles 1 and 11–16 still fell through to `BuildCity` |
| 3 | High | Signature hazards could combine with an unread lane block | Event hazards were placed on a free lane without requiring the whole row and its neighbours to be empty |
| 4 | High | A restart test failed on a correct restart | The PlayMode test assumed every profile starts at 0 m, but the local profile has a level-2 head-start upgrade worth 110 m |
| 5 | Medium | T-junction branch deck left a visible hole at the median | The fixed-width curved deck did not reach the central divider |
| 6 | Medium | Pool diagnostics lagged one segment behind the active window | Counters were written during segment construction, before the trim pass |
| 7 | Medium | World contract still described 21 districts / 5 km / five junctions | Code, tests and documentation had drifted after the 3 km / 20-theme / nine-junction design |
| 8 | Medium | Retired district and new ToD settings had no migration path | Save schema needed a v5 migration for retired location IDs and the new `timeOfDay*` fields |
| 9 | Low | Stale 5 km comments in `TrackDirector`, `TrackManager` and the capture harness | Comments were not updated with the zone change |

The first three were functional defects found by tests or source inspection. Item 4 was a test defect, not a gameplay defect; it was fixed without weakening the restart contract.

---

## 2. Fixed bugs

1. **Landmarks:** all twenty districts now use unique wired signatures 0–19. Headless and EditMode tests assert uniqueness and range, so a future out-of-range ID fails loudly instead of deleting a landmark.
2. **Architecture dispatch:** styles 1 and 11–16 now route to `BuildLuxury`, `BuildCryoHarbor`, `BuildAlpineVillage`, `BuildSeoul`, `BuildSkybridge`, `BuildSpaceport` and `BuildRuins`.
3. **Hazard fairness:** `HazardRules` is a pure Core rule. A signature hazard may occupy only a row that is full-width empty with full-width-empty previous and next rows. Geometry and tests share the same function.
4. **Restart test:** the PlayMode test computes the expected upgraded start line from `ProgressionMath`, asserts the restarted distance is below the prior run and inside the upgraded start window.
5. **T-junction deck:** the curved deck now extends from the branch lane to the median with a 0.2 m overlap; the painted lane remains on the route centre. Shortcuts keep their narrower deck.
6. **Pool diagnostics:** `PooledObstacleCount` and `PooledPickupCount` are updated at the end of `TrackManager.Tick`, after build and trim.
7. **World contract:** `LocationCatalog`, `TrackDirector`, `TrackManager`, `SaveData`, HUD, discovery screen, tests and documentation now agree on 20 themes, 3,000 m zones and nine junction shapes.
8. **Save v5:** retired location IDs are removed, time-of-day fields are added and clamped, and legacy XP still migrates once.
9. **Comments:** stale 5 km notes were corrected.

---

## 3. New and rebuilt systems

### 3.1 Twenty data-driven 3 km themes

`LocationCatalog` defines exactly the twenty requested themes on 3,000 m zones:

| # | Theme | District | Structural family |
|---:|---|---|---|
| 0 | Futuristic Japan | SAKURA CIRCUIT | Japanese neon expressway |
| 1 | Futuristic South Korea | SEOUL NEXUS | Holographic river city |
| 2 | California coastal highway | PACIFIC VERGE | Coastal highway |
| 3 | Snowy Canadian mountains | MAPLE FROST | Snowbound highway |
| 4 | Frozen industrial region | CRYO HARBOR | Frozen harbor |
| 5 | Dubai-inspired city | SOLAR ARCOLOGY | Desert arcology |
| 6 | European cyberpunk metropolis | EURODYNE SPIRE | European cyber city |
| 7 | Tropical beach | CORAL EXPANSE | Coastal highway |
| 8 | Desert megacity | DUNE MERIDIAN | Desert arcology |
| 9 | Underground railway | OBSIDIAN LINE | Tunnel / station |
| 10 | Neon Tokyo | NEON TOKYO | Japanese neon expressway |
| 11 | Mountain village | ALPINE HAVEN | Alpine village |
| 12 | Rainy coastal city | MONSOON QUAY | Coastal highway |
| 13 | Forest research zone | VERDANT GRID | Forest research |
| 14 | Spaceport city | KESSLER REACH | Spaceport |
| 15 | Robot district | REACTOR BELT | Reactor / assembly |
| 16 | European mountain highway | AURORA PASS | European highway |
| 17 | Luxury tech district | HALCYON SPIRE | Luxury tech |
| 18 | Abandoned cyber city | NULL CATHEDRAL | Ruins |
| 19 | Giant skybridge megacity | SKYWAY 09 | Skybridge |

Each district has a unique palette, sky gradient, fog colour, ground/building materials, weather code, preferred ToD phase, landmark signature, road key, ambience key and music key. Seventeen structural families cover the twenty themes; the three coastal themes, two desert themes, two European highway themes and two Japanese themes intentionally share a structural family while differing in palette, sky, weather, road dressing and landmark. This is a procedural, stylised pipeline, not twenty authored DCC environment kits.

The first 3,000 m always resolves to zone 0 (SAKURA CIRCUIT), so every seed opens in the same readable district. Zone selection is a pure function of distance: 2,999.9 m is zone 0 and 3,000 m is zone 1. Because segments are 42 m long, the segment that contains the boundary starts at 2,982 m and crosses into the next zone; streaming still preloads the incoming segment before the player reaches it.

### 3.2 Seven-state time of day

`TimeOfDayCatalog` authors Early Morning, Morning, Afternoon, Evening, Sunset, Night and Late Night. Every profile carries:

- sun elevation, azimuth, colour and intensity;
- moon colour and intensity;
- ambient intensity, reflection intensity and shadow strength;
- fog colour and density;
- neon and streetlight intensity;
- star visibility, cloud tint and weather intensity.

`TimeOfDaySystem` blends adjacent profiles continuously and exposes three modes:

- `0` **LOCATION** — each district's authored phase (default);
- `1` **DYNAMIC** — a 21 km default cycle that advances one state per 3 km;
- `2` **FIXED** — a player-selected phase.

Settings exposes mode, fixed phase and cycle length; values are saved in schema v5 and clamped to 3–105 km. `EnvironmentController` binds the resolved state to the sun/moon, ambient trilight, directional light and shadow strength, fog, cached sky dome (zone × phase), stars, clouds, neon/streetlight fills, reflections, rain/weather scale and traffic/train availability. At the default cycle a 30 km run advances from Evening through the full ring to Late Night.

### 3.3 Nine connected junction shapes

The deterministic scheduler places a junction every 15 segments (about 630 m), first at `10 + abs(seed % 15)`. Warning begins three segments (126 m) ahead. The route commits at row 1, uses branch rows 2–4 and merges at row 5.

| Shape | Route behaviour |
|---|---|
| Split | Median fork with left/right branches and reward route |
| Crossroads | Median fork with a cross-street deck |
| NarrowBridge | Centre-lane commitment over a narrow deck |
| BrokenRoad | Full-width road gap resolved by a jump |
| TunnelBranch | Median branch into a tunnel mouth |
| TJunction | Curved left/right branch decks that reach the median |
| CurvedRoad | Whole road sweeps laterally and returns to centre |
| Shortcut | Outer curved reward lane with a guard rail |
| CollapsingBridge | Broken-road gap with rubble and warning geometry |

`JunctionRules.RouteOffsetAt` is the single pure source for geometry, pickups and runner collision. `RunnerController` follows `RouteOffsetX`/`RouteX`; a centre-lane runner auto-commits to a safe branch at the commit line, so a median junction cannot become an unavoidable death. Directional input commits a branch, holds the lane to the merge, and drives lean, camera roll and the HUD warning panel.

### 3.4 Moving hazards and environmental events

`Obstacle` now supports deterministic motion for maglev trains, traffic vehicles, falling debris and sliding platforms, plus collapsing-bridge geometry. Event-only signature hazards are placed by `HazardRules` on full-width-empty rows with empty neighbours. Collision remains swept against the obstacle bounds; motion is simulation-time based, so it is deterministic across clients. The sliding platform is a hazard, not a rideable platform.

---

## 4. 3 km transition status

- **Implemented:** 20 zones, exact 3,000 m boundaries, unique theme data, 17 architecture families, 20 landmarks, preloaded incoming segments, zone-change events and a 2.6 s environment crossfade.
- **Verified:** headless core zone mapping and boundary contract; EditMode 3/6/9/15/30 km location/junction contract; PlayMode live `TrackManager` stream across 3/6/9/15/30 km with district announcements.
- **Not verified:** human visual inspection of every one of the twenty transitions at 4K. Existing 18-screen captures include one active transition and a tunnel from the previous build; the new themes have not all been captured. No memory profiler run was made.

The old 5 km / 21-district layout is gone. The 21st district (Helion Ring) is retired and removed by save v5 migration.

---

## 5. Time-of-day status

- **Implemented:** seven complete profiles, smooth blending, authored/dynamic/fixed modes, persisted settings, sky/light/fog/shadow/neon/star/cloud/weather/reflection binding, traffic and train availability.
- **Verified:** headless seven-state ring and blend test; dynamic 30 km phase progression; EditMode ToD contract; PlayMode save round-trip of mode/phase/cycle.
- **Not verified:** a full visual inspection of all seven phases in all twenty districts; shadow/acne, reflection and exposure tuning at every phase; HDR/4K output.

---

## 6. Junction and navigation status

- **Implemented:** nine shapes, real curved decks and matching collision, T branches to the median, shortcut reward routes, broken-road/collapsing-bridge gaps, 126 m warnings, auto-commit, lane lock, lean/camera roll and HUD prompts.
- **Verified:** EditMode nine-shape cycle, route offsets/slopes, deck containment, centre-lane commitment and 30 km junction/location contract; PlayMode at least 40 junction windows in 30,030 m and every row passable; headless junction scheduling, connectivity, phase, narrow-bridge and broken-road fairness.
- **Not verified:** physical controller/touch play through every branch; visual inspection of every shape at speed; a fully drivable perpendicular 90° cross-street network (not implemented).

---

## 7. Long-distance results

`LongRunTests.TrackStreamsThirtyKilometresWithinBoundedPoolsAndZones` drove the real `TrackManager` through 715 segment ticks (30,030 m) in a real Practice session:

- 3,000 / 6,000 / 9,000 / 15,000 / 30,000 m checkpoints all matched the expected 3 km zone.
- At least ten `LocationChanged` announcements were observed and the final zone was 10.
- At least 40 distinct junction windows were observed.
- Active segments stayed between 6 and 14; hazard and pickup diagnostics stayed proportional (≤ 14 × 6 and ≤ 14 × 24).
- Difficulty never regressed and reached tier 8.
- The save round-trip preserved time-of-day mode, fixed phase and cycle length.
- No exception, compile error or unbounded growth was observed in the test run.

This is a scripted streaming test, not a human 30 km playthrough. It exercises segment release, pooling, zone/junction scheduling and save serialization together. A memory profiler and thermal/frame-time measurement were not run.

---

## 8. Verification summary

| Check | Result |
|---|---|
| Unity EditMode | **40 / 40 passed** |
| Unity PlayMode | **18 / 18 passed**, including the 30 km stream |
| Headless core | **66 / 66 passed** |
| Python backend | **59 / 59 passed** |
| Static integrity | **910 / 910 passed** |
| Unity compilation | **0 errors** |
| macOS build | Rebuilt 28 October 2026; see `PROJECT_STATUS.md` for final size/architecture |
| Visual captures | Existing 18-screen sets at 1600×900, 1280×800 and 3840×2160 inspected; not recaptured for every new theme |

---

## 9. Remaining bugs, gaps and unverified work

1. **Architecture reuse:** four groups of related themes share a structural family. Palettes, sky, weather, road keys and landmarks differ, but the building grammar is not unique per theme.
2. **Road prefab data:** `roadStyle`/`roadPrefab` are authored keys; the runtime still builds road geometry procedurally rather than loading a unique prefab per key. The road finish is themed through materials and geometry, not twenty authored prefab meshes.
3. **Requested mechanics not implemented:** drone chases, rideable moving platforms, multi-stage destructible structures, temporary vehicles, explicit Friend Challenge/Limited-Time Event mode entries and a fully drivable perpendicular 90° cross-street network. Moving trains, traffic, falling debris, sliding hazard platforms and collapsing-bridge gaps are implemented.
4. **Visual coverage:** no new full 4K capture set for all twenty themes, all seven ToD phases or all nine junctions. The existing capture set predates the Phase 01 themes.
5. **Performance:** no CPU/GPU/memory/thermal profiling; 60 FPS at 4K is a goal, not a measured result. Segment/pool bounds are asserted, not profiled.
6. **Platforms:** only macOS was built and launched. Windows, Linux, WebGL, Android and iOS were not built or run. Physical 4K/HDR, real-device touch/controller/audio and suspend/resume are unverified.
7. **Multiplayer:** the Python authority passes its loopback suite; a real two/five-client Unity race, reconnect through the UI and NAT/TLS deployment were not tested.
8. **Live services:** no HTTPS deployment, verified bank settlement, live Stripe/Apple/Google credentials, SMTP/SMS delivery or admin/proof storage. The premium service is tested locally/sandboxed only.
9. **Save integrity:** the local save is editable and not cryptographically tamper-proof or authoritative for GEMS/online rewards.
10. **Content quality:** procedural primitives, generated textures and synthesized audio remain stylised, not authored AAA assets. Some storefront interiors repeat within a theme; foliage is static merged geometry; there is no LOD/occlusion pass.
11. **Manual QA:** no full manual playthrough of all 15 modes, every obstacle, power-up, branch, death/revive and persistence path.
12. **Capture tooling:** the prior intermittent macOS Metal duplicated-UI readback was not reproduced, but it is not explained or proven fixed.

---

## 10. How to reproduce the checks

```bash
# Headless core (66 tests)
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet" \
  run --project Tools/CoreTests/CoreTests.csproj -c Release

# Backend (59 tests)
python3 -m unittest discover -s Backend/tests -v

# Static integrity (910 checks)
python3 Tools/validate_project.py

# Unity EditMode / PlayMode
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode \
  -testResults /tmp/nv-editmode.xml -logFile /tmp/nv-editmode.log
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode \
  -testResults /tmp/nv-playmode.xml -logFile /tmp/nv-playmode.log

# macOS build
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit -projectPath "$PWD" \
  -executeMethod NeonVelocity.EditorTools.BuildTools.BuildMacOS \
  -logFile /tmp/nv-build.log
```
