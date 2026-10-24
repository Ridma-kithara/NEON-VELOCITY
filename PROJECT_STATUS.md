# NEON VELOCITY — Project Status

**Studio:** RK VANTAGE GAMES  
**Last updated:** 24 October 2026  
**Engine:** Unity 6000.6.7f1 / URP 17.6 / Input System 1.20  
**Verified build:** `Builds/macOS/NeonVelocity.app` — 117.0 MiB, universal x86_64 + ARM64, ad-hoc signed  
**Repository:** `Ridma-kithara/NEON-VELOCITY` (`main`)

## 1. Current state

The game is playable offline, the menu/garage/settings/shop flows are implemented, and the macOS player has been rebuilt and exercised after the latest QA, character, audio and environment/atmosphere pass. The current worktree contains the completed navigation/UI/settings/garage work plus layered procedural armour, generated armour materials, new synthesized spatial audio, a recessed storefront city with layered facades and street furniture, textured rain, wet-road practical lighting, a subway-station pass, an additive no-shadow glow pass, authoritative biome reporting and a verified 2.6-second biome crossfade. It remains an original stylised procedural game, not a photoreal AAA production.

| Area | State |
|---|---|
| Core gameplay | Implemented; deterministic three-lane runner, obstacles, pickups, power-ups, modes and progression |
| Offline play | Implemented and exercised through Practice/PlayMode; full manual all-mode playthrough still pending |
| Multiplayer authority | Implemented; Python loopback suite passes; real multi-client Unity race not run |
| Main menu / branding | Implemented, polished and captured at three resolutions |
| Settings | Implemented with display resolution/mode, presets, render scale, textures, shadows, reflections, AA, effects, view distance, VSync, input and accessibility |
| Garage / customization | Implemented with a dedicated RenderTexture turntable, pose preview, drag rotation, owned/equipped states and purchase confirmation |
| Shop / currency | Implemented with confirmation and duplicate-dialog guard; final debits remain protected by Progression checks |
| Navigation / Back | Centralized history and contextual Back; tested for Shop/Modes/Settings-from-Pause/Results/modal routes |
| HUD / pause | Solo-only panels, readable pause glyph, toast placement and pause/settings flow fixed |
| Visual upgrade | Beveled layered runner armour, generated materials, recessed storefront city, layered facades, street trees/furniture, wet practical light pools, textured rain/mist, abstract signage, subway station and post-processing |
| Environment / atmosphere slice | Implemented and visually verified: roads, sidewalks, drainage, podiums, storefronts, trees, skyline, wet surfaces, rain/splashes, lamp/shop light pools, biome lighting/sky crossfade |
| PC 4K support | Actual 3840 × 2160 render/capture verified on the host; physical 4K display and 4K performance not verified |
| Mobile quality profile | Implemented with independent mobile settings path; no device build/validation |
| Builds | macOS universal verified; Windows/Linux/Android/iOS not built here |

## 2. Final verification results

| Check | Result |
|---|---|
| Unity EditMode | **24 / 24 passed** |
| Unity PlayMode | **9 / 9 passed** |
| Headless core | **50 / 50 passed** |
| Python backend | **9 / 9 passed** |
| Static integrity | **710 / 710 passed** |
| Compilation | **0 errors** in the tested assemblies |
| macOS build | **Succeeded**, 117.0 MiB, x86_64 + arm64 |
| Captures | 9 screens at **1600 × 900**, **1280 × 800** and actual **3840 × 2160** PNGs, including the active biome crossfade and a real subway/station frame |
| Runtime capture log | Completed without runtime exceptions or warnings |

The PlayMode suite includes real bootstrap, Practice start, quick-tap jump clearance, Back-stack routing, title/quit modal priority, restart into a fresh run, garage preview cleanup, Results root return, settings-from-pause return, and purchase-confirmation safety.

## 3. Implemented in this pass

### Character, audio and world polish
- Cached 26-facet chamfered armour geometry, articulated palms/fingers and layered boots in `PrimitiveMeshes.cs` / `RunnerVisuals.cs`.
- Generated armour albedo, normal and height textures in `TextureFactory.cs`; transparent contact shadow and emissive trim.
- Landing compression, lane banking, jump/slide/fall/victory poses and optional vertical-velocity/ground-height animation parameters.
- Footsteps with wet/metal variants, lane whoosh, thunder and train pass-by clips; pooled spatial one-shots in `AudioManager.cs`.
- Wet-road reflection cards, puddle/curb light pools, storefront mullions/doorways, tiered facades and expanded subway station geometry in `TrackManager.cs`.
- Landing camera dip/FOV punch in `RunnerCamera.cs`, respecting reduced motion.
- Authoritative biome reporting from `TrackDirector` plans so lighting/audio match the generated segment.

### Navigation and flow
- Central `AppScreen` history managed by `GameApp`.
- `IScreenBackHandler` for stateful screens; modal dismissal takes priority.
- Contextual Back from Settings to Pause when opened in a run, then Back resumes.
- Results returns to Main Menu and clears stale history.
- Title-screen Back/Escape opens the quit confirmation instead of rebuilding the menu.
- Header Back icon and consistent header text offsets.

### Graphics and presentation
- Scaled PC/mobile settings: Low, Medium, High, Ultra, Custom.
- Resolution, windowed/borderless (and Windows/Linux exclusive fullscreen offset), 30/60/120/144 FPS, render scale, textures, shadows, reflections, AA, effects and view distance.
- True 4K render/capture path validated; UI inspected at 3840 × 2160.
- Original layered runner with beveled armour, articulated hands/feet, chest core, backpack thrusters, helmet and emissive visor.
- Wet road, denser neon facade windows, procedural sign glyphs, maglev sky train, air traffic, rain, mist and post-processing.
- City-first opening biome for all seeds; deterministic multiplayer rotation.

### Environment and atmosphere vertical slice
- Rebuilt the gameplay city around recessed frontages, deterministic alley gaps, layered facade families, podiums, balconies, service pipes, AC units, rooftop plant and a layered skyline.
- Added street trees with thicker self-lit trunks, layered canopies, planters and neon edges; benches, bins, street-name signs, lamps and a safety rail with drains.
- Added storefront interiors with shelving, counters, ceiling lights, mullions, awnings, shutters, doorways, blade signs and abstract billboard art.
- Reworked wet-road dressing, practical lamp/shop light pools and pavement/forecourt depth; fixed light cards that had been sunk below the raised sidewalk.
- Rebuilt rain with a generated streak texture, stretch billboards, wind slant, splashes and mist; tuned density by effects quality.
- Split additive glow cards into a separate merged renderer with shadow casting disabled, removing dark card-shaped shadows while preserving opaque architecture shadows.
- Added a 2.6-second fog/ambient/sun/rim/fill/camera/rain/sky crossfade and verified it with a captured mid-transition frame.

### UI, shop and garage
- Runner garage with RenderTexture layer isolation, front-facing turntable, stage lighting, drag rotation and pose controls.
- Purchase confirmation for runners, trails and upgrades; modal and in-flight guards.
- Removed duplicate currency from the shop tab bar; fixed runner-name clipping.
- Fixed mode-card text/stat overlap and main-menu season badge wrapping.
- Solo HUD hides empty lives/online panels; pause glyph remains visible; in-run toasts sit below the timer.

### Tests and tooling
- EditMode: 24 tests, including settings clamps/presets, city-first opening, and save round-trip of new display/render fields.
- PlayMode: 9 tests, including navigation, restart rebuilding a fresh run and purchase-confirmation flows.
- Core test expectation updated for Custom preset (4).
- VisualCaptureRunner captures nine screens with selectable resolution, including a mid-biome transition, a time-accelerated subway/station frame and pause.

## 4. Generated / authored assets

All runtime assets remain original and procedural:
- geometry from Unity primitives;
- textures generated by `TextureFactory`;
- UI built at runtime;
- 27 audio clips synthesized by `Tools/generate_audio.py` (ambience, music and SFX);
- no copyrighted characters, logos, third-party art packs or proprietary assets.

This is intentionally a code-only/original pipeline. It is not a production DCC asset pipeline, and the result is stylised rather than photoreal.

## 5. Tested vs. unverified

### Verified
- macOS build compiles, launches and completes the capture flow without runtime exceptions.
- All nine screens render at 16:9, 16:10 and 4K; gameplay and transition captures show textured rain, wet roads, raised sidewalks, recessed storefronts, street trees/furniture, signage and the active crossfade.
- Core logic, authority, settings clamps/persistence, navigation and purchase-confirmation logic pass automated checks.
- Offline Practice launch, jump regression, navigation and purchase routes pass in PlayMode.
- Player captures reached a real subway segment after fast-forwarding; biome environment and geometry agree. No missing audio clips, runtime exceptions or stripped shaders were reported. Audio was not auditioned.

### Not verified
- Physical 4K display output, HDR, cable/mode negotiation and 60 FPS at 4K.
- Live confirmation that an intermittent macOS Metal screenshot-readback artifact (one tiled/duplicated-UI frame, clean on reruns) cannot appear on a physical display.
- LOD, occlusion culling, profiler-driven frame-time validation and dynamic tree/foliage wind; none were implemented in the environment slice.
- Windows, Linux, WebGL, Android and iOS builds; real Android system Back behaviour.
- Real touch, gamepad focus order, audio balance, suspend/resume and safe areas on devices.
- Two/five real Unity clients, latency/loss, reconnect through the UI, NAT/TLS.
- CPU/GPU/memory/thermal profiling and long-session memory growth.
- Full manual playthrough of all modes, every power-up/obstacle/pickup/result path.
- Audible audio balance, loudness and device output; no listening test was performed.
- macOS Developer ID signing, hardened runtime, notarisation and store packaging.
- Full confirmed shop purchase on a disposable profile (modal/no-charge guard is tested; the final debit path was not exercised end-to-end in PlayMode to avoid mutating the real save).

## 6. Known risks / next work

1. Run a full manual QA pass on macOS (menus, Back, saves/settings, restart, collision/death, results, shop debit), then produce and test Windows and Android builds on real hardware.
2. Capture a live/OS-composited verification set to close the intermittent automated screenshot-readback limitation; keep the synchronous harness as a fallback.
3. Add a disposable-profile PlayMode test that confirms a real purchase and asserts exactly one debit.
4. Profile 4K and low-end mobile; measure frame time, memory and thermal behaviour, then add LOD/occlusion or instancing where justified.
5. Add per-theme storefront/facade variants and dynamic foliage wind to reduce repetition and add environmental motion.
6. Test two/five-client Unity races against the Python authority, including disconnect/reconnect.
7. Verify Android system Back, touch safe areas and controller navigation on devices.
8. Add production art/rigging/cosmetics pipeline if photoreal AAA quality is required beyond procedural assets.
9. Add code signing, notarisation, store metadata, privacy policy and release automation.
10. Extend automated coverage to every mode, every button, loading/error/empty states and online failure paths.

## 7. Document map

- [QA_REPORT.md](QA_REPORT.md) — defects, root causes, fixes, tests and remaining gaps.
- [Documentation/TEST_REPORT.md](Documentation/TEST_REPORT.md) — executed test details and platform status.
- [Documentation/DEVELOPMENT_PROGRESS.md](Documentation/DEVELOPMENT_PROGRESS.md) — roadmap, phase status, defects, gaps, performance observations and next tasks.
- [Documentation/KNOWN_LIMITATIONS.md](Documentation/KNOWN_LIMITATIONS.md) — operational, multiplayer, content and release constraints.
- [README.md](README.md) — controls, setup, build commands and verification notice.
