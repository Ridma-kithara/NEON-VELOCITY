# NEON VELOCITY — Project Status

**Studio:** RK VANTAGE GAMES  
**Last updated:** 24 October 2026  
**Engine:** Unity 6000.6.7f1 / URP 17.6 / Input System 1.20  
**Verified build:** `Builds/macOS/NeonVelocity.app` — 117.0 MiB, universal x86_64 + ARM64, ad-hoc signed  
**Repository:** `Ridma-kithara/NEON-VELOCITY` (`main`)

## 1. Current state

The game is playable offline, the menu/garage/settings/shop flows are implemented, and the macOS player has been rebuilt and exercised after the latest QA, character, audio and world-polish pass. The current worktree contains the completed navigation/UI/settings/garage work plus layered procedural armour, generated armour materials, new synthesized spatial audio, wet-reflection street dressing, a subway-station pass and authoritative biome reporting. It is no longer just the baseline commit; see the Git history for the exact revision.

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
| Visual upgrade | Beveled layered runner armour, generated materials, wet reflections, tiered city, subway station, rain/mist, neon signage, sky train, air traffic and post-processing |
| PC 4K support | Actual 3840 × 2160 render/capture verified on the host; physical 4K display and 4K performance not verified |
| Mobile quality profile | Implemented with independent mobile settings path; no device build/validation |
| Builds | macOS universal verified; Windows/Linux/Android/iOS not built here |

## 2. Final verification results

| Check | Result |
|---|---|
| Unity EditMode | **24 / 24 passed** |
| Unity PlayMode | **8 / 8 passed** |
| Headless core | **50 / 50 passed** |
| Python backend | **9 / 9 passed** |
| Static integrity | **709 / 709 passed** |
| Compilation | **0 errors** in the tested assemblies |
| macOS build | **Succeeded**, 117.0 MiB, x86_64 + arm64 |
| Captures | 8 screens at **1600 × 900**, **1280 × 800** and actual **3840 × 2160** PNGs, including a real subway/station frame |
| Runtime capture log | Completed without runtime exceptions |

The PlayMode suite includes real bootstrap, Practice start, quick-tap jump clearance, Back-stack routing, title/quit modal priority, garage preview cleanup, Results root return, settings-from-pause return, and purchase-confirmation safety.

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

### UI, shop and garage
- Runner garage with RenderTexture layer isolation, front-facing turntable, stage lighting, drag rotation and pose controls.
- Purchase confirmation for runners, trails and upgrades; modal and in-flight guards.
- Removed duplicate currency from the shop tab bar; fixed runner-name clipping.
- Fixed mode-card text/stat overlap and main-menu season badge wrapping.
- Solo HUD hides empty lives/online panels; pause glyph remains visible; in-run toasts sit below the timer.

### Tests and tooling
- EditMode: 24 tests, including settings clamps/presets, city-first opening, and save round-trip of new display/render fields.
- PlayMode: 8 tests, including navigation and purchase-confirmation flows.
- Core test expectation updated for Custom preset (4).
- VisualCaptureRunner captures eight screens with selectable resolution, including a time-accelerated subway/station frame.

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
- Menu, settings, shop/garage, mode select, two gameplay frames and pause render correctly at 16:9, 16:10 and 4K.
- Core logic, authority, settings clamps/persistence, navigation and purchase-confirmation logic pass automated checks.
- Offline Practice launch, jump regression, navigation and purchase routes pass in PlayMode.
- Player captures reached a real subway segment after fast-forwarding; biome environment and geometry agree. No missing audio clips, runtime exceptions or stripped shaders were reported. Audio was not auditioned.

### Not verified
- Physical 4K display output, HDR, cable/mode negotiation and 60 FPS at 4K.
- Windows, Linux, WebGL, Android and iOS builds; real Android system Back behaviour.
- Real touch, gamepad focus order, audio balance, suspend/resume and safe areas on devices.
- Two/five real Unity clients, latency/loss, reconnect through the UI, NAT/TLS.
- CPU/GPU/memory/thermal profiling and long-session memory growth.
- Full manual playthrough of all modes, every power-up/obstacle/pickup/result path.
- Audible audio balance, loudness and device output; no listening test was performed.
- macOS Developer ID signing, hardened runtime, notarisation and store packaging.
- Full confirmed shop purchase on a disposable profile (modal/no-charge guard is tested; the final debit path was not exercised end-to-end in PlayMode to avoid mutating the real save).

## 6. Known risks / next work

1. Run a full manual QA pass on macOS, then produce and test Windows and Android builds on real hardware.
2. Add a disposable-profile PlayMode test that confirms a real purchase and asserts exactly one debit.
3. Profile 4K and low-end mobile; measure frame time, memory and thermal behaviour.
4. Test two/five-client Unity races against the Python authority, including disconnect/reconnect.
5. Verify Android system Back, touch safe areas and controller navigation on devices.
6. Add production art/rigging/cosmetics pipeline if photoreal AAA quality is required beyond procedural assets.
7. Add code signing, notarisation, store metadata, privacy policy and release automation.
8. Extend automated coverage to every mode, every button, loading/error/empty states and online failure paths.

## 7. Document map

- [QA_REPORT.md](QA_REPORT.md) — defects, root causes, fixes, tests and remaining gaps.
- [Documentation/TEST_REPORT.md](Documentation/TEST_REPORT.md) — executed test details and platform status.
- [Documentation/KNOWN_LIMITATIONS.md](Documentation/KNOWN_LIMITATIONS.md) — operational, multiplayer, content and release constraints.
- [README.md](README.md) — controls, setup, build commands and verification notice.
