# NEON VELOCITY — QA, UX and Visual Audit

**Studio:** RK VANTAGE GAMES  
**Game:** NEON VELOCITY  
**Audit date:** 23–24 October 2026  
**Engine:** Unity 6000.6.7f1 / URP 17.6 / Input System 1.20  
**Test host:** macOS 26.7.1, Intel Core i9-9980HK, 1600 × 900 host display  
**Build under test:** `Builds/macOS/NeonVelocity.app`, version 1.0.0, universal x86_64 + ARM64, ad-hoc signed

This report records the defects found during the audit, the fixes applied, the checks actually executed, and the work that remains unverified. It is not a claim that every possible bug has been found or that every platform has been validated.

## 1. Final verification summary

| Check | Result | Status |
|---|---|---|
| Unity script compilation | 0 errors in runtime, editor-tools and test assemblies | Executed |
| Unity EditMode tests | **24 / 24 passed** | Executed |
| Unity PlayMode tests | **9 / 9 passed** | Executed |
| Headless gameplay-core tests | **50 / 50 passed** | Executed |
| Python multiplayer authority tests | **9 / 9 passed** (10.386 s) | Executed |
| Static project integrity | **710 / 710 checks passed** | Executed |
| macOS universal player build | **Succeeded**, 117.0 MiB, x86_64 + ARM64 | Executed |
| Built player launch / capture run | Completed without runtime exceptions | Executed |
| UI captures at 1600 × 900 | menu, settings, shop/garage, mode select, gameplay ×2, active biome transition, subway/station and pause inspected | Executed |
| UI captures at 1280 × 800 (16:10) | Same nine screens inspected | Executed |
| 4K render/capture at 3840 × 2160 | Same nine screens, including the transition and subway; PNGs confirmed 3840 × 2160 | Executed |
| Physical 4K display output | Not available | Unverified |
| Windows / Linux / Android / iOS builds | Platform modules not available/verified | Unverified |
| Real-device touch, controller, audio, suspend/resume | No test devices | Unverified |
| Real multi-client Unity race | No second Unity client/device | Unverified |
| CPU/GPU/memory/thermal profiling | No profiling run | Unverified |
| Full manual playthrough of all 11 modes | Not completed | Unverified |

The 4K result is an actual 3840 × 2160 backbuffer/screenshot produced by the built player. The host display is 1600 × 900, so physical 4K-display behaviour, cable/HDMI mode negotiation and 4K frame-rate performance remain unverified. The final build log contains only non-blocking Unity platform/licensing notices; it has no script compilation warnings or errors.

## 2. Issues found and fixed

### QA-01 — Critical — Back returned to a screen while navigation state stayed stale

**Root cause:** `GameApp.NavigateBack()` popped the history entry and rebuilt the previous screen, but did not assign `_currentScreen` to that previous screen. The visible UI could show the previous screen while `_currentScreen` still identified the screen being left. This broke subsequent contextual Back actions and the Settings-from-Pause flow.

**Files:** `Assets/Game/Scripts/GameApp.cs`

**Fix:** Assign `_currentScreen = previous` before `BuildScreen(previous)`. Also clear `_settingsReturnToRun` when a paused run resumes.

**Test:** PlayMode `BackReturnsThroughPushedScreensToMainMenu`, `SettingsFromPauseReturnsToPauseThenResumesRun` and `ResultsBackReturnsToMainMenuRoot`.

**Result:** Passed. Settings now returns to Pause, then Escape/Back resumes the run; Shop returns to Modes, then Main Menu.

**Limitation:** Automated tests cover the major routes, not every button in all 11 modes.

---

### QA-02 — High — No consistent contextual Back model across secondary screens

**Root cause:** Navigation was screen-specific; several screens lacked a visible Back action or returned blindly to the main menu. There was no shared history/stateful-screen contract, and the title menu was not treated as the only root.

**Files:** `Assets/Game/Scripts/GameApp.cs`, `Assets/Game/Scripts/UI/IScreenBackHandler.cs` (new), `Assets/Game/Scripts/UI/Screens/ScreenKit.cs`, `SettingsScreen.cs`, `MultiplayerScreen.cs`, `ResultsScreen.cs`, `MissionsScreen.cs`, `LeaderboardScreen.cs`

**Fix:** Central `AppScreen` history stack, one `HandleBack()` entry point for Escape/gamepad/UI, `IScreenBackHandler` for stateful screens, `NavigateBack()` fallback rules, modal-first dismissal, and title/Results root resets. The header Back control is now a visible icon button with consistent placement. Escape on the title screen opens the existing quit confirmation instead of silently rebuilding the menu.

**Test:** PlayMode navigation suite (seven navigation/context tests, including the modal/quit-title and restart cases). Also verified captured Back controls at 1280 × 800, 1600 × 900 and 3840 × 2160.

**Result:** Passed for tested routes.

**Limitation:** Android system Back with the Input System and gamepad UI focus order are not verified on physical devices.

---

### QA-03 — High — Settings had no real display/quality control set

**Root cause:** The previous settings screen exposed only a limited quality preset and did not provide resolution, display mode, texture/shadow/reflection/AA/effects/render-scale/view-distance configuration. The Custom preset did not exist. 4K could not be exercised from the UI.

**Files:** `Assets/Game/Scripts/Core/SaveData.cs`, `GameApp.cs`, `Assets/Game/Scripts/UI/Screens/SettingsScreen.cs`, `Player/RunnerCamera.cs`, `GameSession.cs`, `World/EnvironmentController.cs`

**Fix:** Added persisted display fields, Low/Medium/High/Ultra/Custom presets, resolution/mode cycling, frame-rate, render scale, texture mip limit, shadows, reflections, MSAA, effects, view distance and VSync. Settings apply live and save on leaving the screen. Mobile hides desktop resolution/mode controls and keeps device-managed rendering. Added clamps for hostile/corrupt values and safe display fallbacks.

**Test:** EditMode settings clamp and preset tests; SaveService round-trip of new settings; PlayMode settings-from-pause flow; built-player 3840 × 2160 and 1280 × 800 captures.

**Result:** Passed. The 4K capture produced actual 3840 × 2160 PNGs and the 16:10 capture produced 1280 × 800 PNGs with intact UI scaling.

**Limitation:** No physical 4K display or mobile device; no performance measurement at 4K. The UI exposes exclusive fullscreen only on Windows/Linux; macOS offers windowed/borderless because that is the supported desktop model.

---

### QA-04 — High — Shop purchases had no confirmation and upgrade clicks could repeat

**Root cause:** Unlock/upgrade buttons executed purchases immediately. Upgrade purchases are level-based; rapid repeated activation could buy multiple levels and deduct currency repeatedly. There was no shared modal guard.

**Files:** `Assets/Game/Scripts/UI/Screens/ShopScreen.cs`, `Assets/Game/Scripts/UI/UIRoot.cs`

**Fix:** Added purchase-confirmation modals for runners, trails and upgrades. `ConfirmPurchase` checks eligibility/credits, blocks opening while any modal is active, and wraps the actual purchase with `_purchaseInFlight`; the Progression service keeps ownership/credit checks. Added `HasModal` use to prevent stacked dialogs.

**Test:** PlayMode `ShopPurchaseRequiresConfirmationBeforeCharging` opens the Volt unlock dialog twice, verifies the dialog does not stack, verifies no unlock or debit occurs before confirmation, and restores the in-memory profile. Progression ownership/credit checks remain covered by core tests.

**Result:** Passed for modal/no-early-charge/double-open protection.

**Limitation:** The final confirmed purchase was not executed in the automated test because it writes the developer's real profile. A disposable-profile end-to-end purchase test remains manual/future work.

---

### QA-05 — High — Shop/garage presentation was incomplete

**Root cause:** No rendered 3D turntable, the preview was dark/misoriented, and the runner name was clipped in its container. The garage/loadout screen also repeated currency in the tab bar and mixed crowded controls.

**Files:** `Assets/Game/Scripts/UI/GaragePreview.cs` (new), `ShopScreen.cs`, `UIRoot.cs`, `ScreenKit.cs`

**Fix:** Added a dedicated layer-30 RenderTexture garage stage with key/cyan/magenta lights, front-facing turntable, drag rotation, pose preview (Idle/Run/Jump), larger runner-name typography, active-loadout presentation, and guaranteed cleanup with Shop. Removed duplicated currency from the tab bar.

**Test:** PlayMode `GaragePreviewIsDestroyedWhenShopCloses`; captured shop screen at three resolutions; inspected the rendered model, name, tabs and card layout.

**Result:** Passed. Preview camera, texture and stage are destroyed when leaving Shop; name is fully readable.

**Limitation:** The preview is still an original primitive-based model, not a production character asset; equipment is visual-only and uses one procedural rig.

---

### QA-06 — High — Runner and neon scene materials were overexposed or off-colour

**Root cause:** Local rim/fill lights were too strong for the pearl armour, washing out the runner in gameplay. The title stage's magenta fill dominated the same white armour, making the menu hero pink/maroon. Window facades and additive pickups also competed with the character.

**Files:** `Assets/Game/Scripts/Player/RunnerVisuals.cs`, `Assets/Game/Scripts/World/MenuStage.cs`, `World/TrackManager.cs`, `World/EnvironmentController.cs`, `Utilities/TextureFactory.cs`, `Player/RunnerCamera.cs`

**Fix:** Rebalanced metallic/smoothness and emissive visor values, reduced gameplay local-light intensity, replaced the menu's dominant magenta fill with cool cyan/blue + neutral-white key lighting, tightened bloom threshold and effects scaling, reduced facade window density, and added original block-glyph neon sign generation.

**Test:** Rebuilt the player and inspected menu/gameplay/shop captures at 1600 × 900, 1280 × 800 and 3840 × 2160.

**Result:** Menu hero now reads as pearl/cyan instead of pink; gameplay runner retains readable silhouette; signs show neon glyph shapes.

**Limitation:** This is a stylised procedural look, not photoreal AAA/Cyberpunk 2077 parity. 2K/4K authored PBR texture libraries, character scanning, DCC assets and baked GI are not part of this code-only asset pipeline.

---

### QA-07 — High — HUD showed empty panels and an unreadable pause glyph

**Root cause:** Solo mode hid the lives text but left its panel visible; the pause glyph used a font size almost equal to its 54px button height, so Unity text truncation could remove it; in-run pickup toasts sat over the centre timer.

**Files:** `Assets/Game/Scripts/UI/HUDScreen.cs`, `Assets/Game/Scripts/UI/UIRoot.cs`

**Fix:** Hide the lives panel with its content in solo runs, keep the multiplayer panel hidden offline, reduce the pause glyph to a safe size while keeping the 54px hit target, and offset in-run toasts below the timer panel while keeping menu toasts at the top.

**Test:** Captured gameplay and pause screens at three sizes; inspected HUD for empty panels, icon clipping and toast/timer overlap.

**Result:** Passed visually at tested resolutions.

**Limitation:** Touch hit-target and controller focus behaviour were not tested on devices.

---

### QA-08 — High — Mode select and title layouts had overlapping/cramped content

**Root cause:** Fixed-height text containers and an over-wide season badge caused title/tag/stats overlap in mode cards and badge wrapping on the title screen. Header Back placement also crowded the eyebrow/title.

**Files:** `Assets/Game/Scripts/UI/Screens/ModeSelectScreen.cs`, `MainMenuScreen.cs`, `ScreenKit.cs`, `LeaderboardScreen.cs`, `MissionsScreen.cs`, `ResultsScreen.cs`

**Fix:** Responsive card spacing, separated stats/title zones, fixed badge width/typography, shifted header text when Back exists, and gave Results a consistent Back/root action. Solo HUD and capture layouts were adjusted accordingly.

**Test:** Captured mode select, menu, settings, shop and results-flow test at three resolutions; inspected headings, badges, buttons and record rows.

**Result:** Passed at 1280 × 800, 1600 × 900 and 3840 × 2160.

**Limitation:** Mobile safe areas/notches were not tested on hardware; the game is landscape-oriented.

---

### QA-09 — High — Opening biome was random, weakening the identity and first-run readability

**Root cause:** Biome selection included the seed before the first segment, so a new run could open in tunnel/industrial/other biomes with different lighting and no guaranteed neon-city establishing shot.

**Files:** `Assets/Game/Scripts/Core/TrackDirector.cs`, `Assets/Game/Tests/EditMode/CoreGameplayTests.cs`

**Fix:** First eight segments always use Neon Sprawl (biome 0), then deterministic biome rotation. All clients derive the same plan from seed/salt.

**Test:** EditMode `GeneratedTrack_OpensInNeonSprawlForEverySeed` with four seeds; core determinism tests still pass.

**Result:** Passed. Multiplayer determinism is preserved because the rule depends only on index and seed.

**Limitation:** Visual transitions are still procedural theme swaps, not authored cinematic biome gates.

---

### QA-10 — Medium — Save migration and test drift around the new Custom preset

**Root cause:** The headless core test still expected `graphicsPreset` to clamp to 3 (Ultra) after the new Custom value 4 was introduced. This produced a 49/50 core run during QA.

**Files:** `Tools/CoreTests/Program.cs`, `Assets/Game/Tests/EditMode/CoreGameplayTests.cs`, `SaveServiceTests.cs`

**Fix:** Updated the expected clamp to Custom (4) and added settings clamp, preset, and persistence coverage for display/render fields.

**Test:** Headless core suite and EditMode suite.

**Result:** 50/50 core, 24/24 EditMode.

**Limitation:** None known for these fields.

---

### QA-11 — Medium — 4K/16:10 UI scaling had never been captured

**Root cause:** Earlier verification used a single 1600 × 900 capture. The code claims 4K support but had no proof of a 3840 × 2160 output or a 16:10 layout.

**Files:** `Assets/Game/Scripts/Utilities/VisualCaptureRunner.cs`

**Fix:** Extended the capture harness to nine screens (including a mid-biome transition) and added `-nv-capture-size`, explicitly requesting windowed resolution and capturing actual backbuffer screenshots. Ran 1600 × 900, 1280 × 800 and 3840 × 2160.

**Test:** Verified PNG dimensions with `sips`; inspected menu, settings, shop, modes, gameplay, transition, tunnel and pause at all three sizes.

**Result:** Passed. 4K PNGs are genuinely 3840 × 2160 and the UI remains anchored and readable.

**Limitation:** No physical 4K monitor; no 4K frame-time profile; no mobile portrait/tablet captures.

---

### QA-12 — Low — Menu Escape did nothing useful at the navigation root

**Root cause:** With no history/session, `NavigateBack()` rebuilt the main menu. Escape appeared to work but had no meaningful result.

**Files:** `Assets/Game/Scripts/GameApp.cs`, `PlayMode/NavigationTests.cs`

**Fix:** At the title root, Back/Escape/menu Back opens the existing quit confirmation instead of rebuilding the menu.

**Test:** PlayMode modal-priority test now invokes Back on the title and verifies a confirmation appears, then dismisses without closing the app.

**Result:** Passed.

**Limitation:** Actual Android system Back mapping is unverified.

### QA-13 — High — Environment biome could disagree with the generated track

**Root cause:** `TrackManager` reported `currentIndex / 4 % 4` as the active biome, while `TrackDirector` keeps the first eight segments in the city and then applies a seed-dependent rotation offset. With a non-zero offset, lighting, rain, ambience and the capture harness could switch to a different biome than the geometry the runner was actually on. The earlier tunnel capture exposed this: it showed a reactor-style scene while the UI reported a tunnel biome.

**Files:** `Assets/Game/Scripts/World/TrackManager.cs`

**Fix:** `TrackManager.Tick` now reads `BiomeIndex` from the director's authoritative `SegmentPlan` for the current index. The environment therefore follows the same seeded rotation as the track, including the city-only opening.

**Test:** Rebuilt the player and ran the capture harness with time acceleration. The harness reached a real tunnel at 351 m, 519 m and 697 m in different runs (the first tunnel index depends on the run seed) and captured visible ceiling ribs, wall signage, platform edge/columns and overhead station boards. No runtime exceptions were logged.

**Result:** Passed for the captured runs.

**Limitation:** The capture fast-forwards with `Time.timeScale = 4` and clears nearby hazards, so it is a visual smoke test of the generated biome, not proof of a normal-speed manual tunnel playthrough.

---

### QA-14 — High — Gameplay environment lacked depth and had visible lighting/rain defects

**Root cause:** The procedural city was still dominated by flat wall bands, thin storefront cut-outs and sparse street detail. Earlier rain attempts either used untextured additive streaks that read as laser lines or particles whose texture was too faint. Several warm light cards were placed below the raised sidewalk/forecourt surface, so they were occluded. Transparent glow/haze cards were merged into the shadow-casting decor renderer, producing solid dark rectangles on the road. Tree trunks were thin and dark enough to disappear, leaving canopies looking unsupported.

**Files:** `Assets/Game/Scripts/World/TrackManager.cs`, `EnvironmentController.cs`, `MenuStage.cs`, `Assets/Game/Scripts/Utilities/TextureFactory.cs`, `MaterialLibrary.cs`, `PrimitiveMeshes.cs`, `Assets/Game/Scripts/Player/RunnerCamera.cs`, `Assets/Game/Scripts/Utilities/VisualCaptureRunner.cs`

**Fix:** Rebuilt the city frontage with recessed storefronts, layered facades, podiums, awnings, shutters, shelving/counters/ceiling lights, service pipes, AC units, balconies, rooftop plant and a layered skyline. Added raised sidewalks, gutters, drains, benches, bins, signs, lamps, warm practical light pools and street trees with visible self-lit trunks. Reworked the generated shop/facade/billboard textures, raised the light pools above the pavement, moved additive glow cards into a separate merged renderer with shadows disabled, and rebuilt rain with a generated streak texture, stretch billboards, wind slant, splashes and mist. Added a 2.6-second fog/ambient/sun/rim/fill/camera/rain/sky crossfade.

**Test:** Rebuilt the macOS player and captured nine screens at 1600 × 900, 1280 × 800 and 3840 × 2160. `sips` confirmed the 4K dimensions. Inspected the menu, garage, game HUD, wet road, storefronts, sidewalk, trees, billboards, lamp pools, textured rain, tunnel and an active mid-transition frame. Final player log contained no runtime exceptions or warnings. Static checks, EditMode, PlayMode, core and backend suites were rerun.

**Result:** Passed for the inspected captures. The transition frame showed normal tunnel geometry with the incoming purple/blue lighting blend and no scene-wide dark veil. The earlier additive glow-card shadow rectangles were no longer present in the clean late-biome 4K frame.

**Limitation:** All art remains procedural/original; there are no authored meshes, textures, PBR libraries, rigs or animation clips. Trees are static merged geometry, with no dynamic foliage wind. No LOD/occlusion pass or 4K frame-time profile was added. The pavement light pools and rain remain intentionally subtle at night.

---

### QA-15 — Medium — Intermittent duplicated-UI/tiled screenshot readback on macOS Metal

**Root cause:** Not isolated. One automated capture run produced a frame with tiled UI/frame content while normal reruns of the same build produced clean gameplay, transition and tunnel images. The harness already used synchronous `ScreenCapture.CaptureScreenshotAsTexture()` and `EncodeToPNG()`. No matching exception, shader error or UI-duplication symptom appeared in the player log or PlayMode tests.

**Files:** `Assets/Game/Scripts/Utilities/VisualCaptureRunner.cs` (capture tooling only)

**Fix/mitigation:** Retained the synchronous capture path, reran the full capture set, and inspected the final 1280 × 800, 1600 × 900 and 3840 × 2160 images used for this report. Documented the artifact rather than presenting it as fixed.

**Test:** Multiple automated capture runs; clean final sets inspected at all three sizes. No live physical-display capture was available to confirm compositor behaviour.

**Result:** Mitigated for the QA evidence set; root cause unresolved. This is recorded as a QA capture-readback limitation, not a confirmed gameplay rendering bug.

**Limitation:** Live/OS-composited capture and physical 4K display verification remain outstanding. If the artifact ever appears outside the capture harness it must be escalated and re-tested as a product defect.

---

## 3. Additional implementation work completed during the pass

These are product/quality improvements rather than regressions:

- Central navigation stack and contextual Back handlers; path: `GameApp.cs`, `UI/IScreenBackHandler.cs`, `ScreenKit.cs`.
- Full display/quality settings, clamps, presets and live application; path: `SettingsScreen.cs`, `SaveData.cs`, `GameApp.cs`, `RunnerCamera.cs`.
- Contextual garage with RenderTexture preview and cleanup; path: `GaragePreview.cs`, `ShopScreen.cs`.
- Runner material/lighting rebuild; path: `RunnerVisuals.cs`, `MenuStage.cs`.
- Wet multi-layer road, denser city facades, neon blade signs, maglev sky train, air traffic and rain/mist; paths: `TrackManager.cs`, `EnvironmentController.cs`, `TextureFactory.cs`, `MenuStage.cs`.
- Deterministic city-first opening for all seeds; path: `TrackDirector.cs`.
- Purchase confirmation and duplicate-activation guards; path: `ShopScreen.cs`, `UIRoot.cs`.
- HUD/pause/toast polish; path: `HUDScreen.cs`, `UIRoot.cs`.
- Expanded automated coverage; paths: `Assets/Game/Tests/EditMode/CoreGameplayTests.cs`, `SaveServiceTests.cs`, `Assets/Game/Tests/PlayMode/NavigationTests.cs`.
- Repeatable multi-screen/4K capture harness; path: `VisualCaptureRunner.cs`.
- Layered beveled armour, articulated hands/feet, generated armour albedo/normal/height textures, contact shadow and landing/jump/slide poses; paths: `RunnerVisuals.cs`, `PrimitiveMeshes.cs`, `TextureFactory.cs`, `MaterialLibrary.cs`.
- Footstep, lane-whoosh, thunder and train pass-by synthesis plus a pooled spatial one-shot path; paths: `AudioManager.cs`, `RunnerController.cs`, `EnvironmentController.cs`, `Tools/generate_audio.py`, `Assets/Game/Resources/Audio`.
- Wet asphalt reflection cards, storefront mullions/doorways/light pools, tiered facades and a subway-station pass with platforms, columns, benches, route maps and suspended signage; path: `TrackManager.cs`.
- Landing weight through a short camera dip and FOV punch, gated by reduced motion; paths: `RunnerCamera.cs`, `RunnerController.cs`.
- Authoritative biome reporting so environment and audio match the seeded track plan; path: `TrackManager.cs`.
- Recessed storefront city with layered facade families, podiums, street trees/furniture, skyline depth, warm practical pools, textured rain, a no-shadow additive-card renderer and a 2.6-second biome crossfade; paths: `TrackManager.cs`, `EnvironmentController.cs`, `TextureFactory.cs`, `MaterialLibrary.cs`, `PrimitiveMeshes.cs`, `MenuStage.cs`.

## 4. Remaining limitations and unverified work

These are not reported as fixed:

1. **Platform coverage:** only macOS was built and launched. Windows, Linux, WebGL, Android and iOS builds were not produced or run.
2. **Physical 4K:** the player produced 4K PNGs in a 1600 × 900 host session, but no physical 4K display, HDR display, HDMI/DisplayPort negotiation or 4K performance measurement was available.
3. **Mobile:** no Android/iOS device. Touch gestures, safe areas, notches, thermal throttling and Android system Back remain unverified. Quality settings are coded to stay independent of desktop.
4. **Controller and accessibility:** keyboard/controller code paths exist and some are covered indirectly, but focus order, remapping, vibration and real-device gamepad navigation were not tested. No screen-reader/color-blind palette support.
5. **Multiplayer:** the Python authority passes 9/9 loopback integration tests. A real two- or five-client Unity race, reconnect through the UI, latency/loss conditions and NAT/TLS deployment were not tested.
6. **Performance:** no CPU/GPU/memory/thermal profiling. 60 FPS at 4K is a goal, not a measured result.
7. **Content:** art/audio are original procedural primitives/synthesis. The armour pass adds generated textures and layered geometry, but it is not equivalent to authored AAA character/environment assets; modular cosmetics still share one procedural rig and are visual-only.
8. **Testing depth:** the automated suite covers the menu/practice bootstrap, jump clearance, navigation, modal priority, garage lifecycle, purchase confirmation, settings clamps/presets and core/backend rules. It does not exercise every power-up, obstacle, mode, button or failure path.
9. **Signing/release:** the macOS app is ad-hoc signed and not notarised. No store packaging, privacy metadata or release pipeline exists.
10. **Save safety:** purchase tests avoid writing the real profile; a full disposable-profile purchase/restore test remains to be added.
11. **Audio quality:** all 27 synthesized clips load in the player and the new spatial one-shots produced no missing-resource errors, but no listening test, loudness measurement or device-output validation was performed.
12. **Screenshot capture:** one automated macOS Metal run produced a tiled/duplicated-UI readback frame. Clean reruns and the final 1280/1600/4K sets were inspected, but the cause was not isolated. Live physical-display capture and 4K frame-rate profiling remain unverified.
13. **Environment depth:** dynamic tree/foliage wind, explicit LOD/occlusion and profiler-driven performance tuning were not implemented. Rain/light pools are deliberately subtle at night, and storefront interiors still repeat within a theme.

## 5. How the final checks were run

```bash
# EditMode
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -runTests -projectPath "$PWD" -testPlatform EditMode \
  -testResults /tmp/nv-editmode.xml -logFile /tmp/nv-editmode.log

# PlayMode
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -runTests -projectPath "$PWD" -testPlatform PlayMode \
  -testResults /tmp/nv-playmode.xml -logFile /tmp/nv-playmode.log

# Headless gameplay core
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet" \
  run --project Tools/CoreTests/CoreTests.csproj -c Release

# Authority
python3 -m unittest discover -s Backend/tests -v

# Static validation
python3 Tools/validate_project.py

# macOS build
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit -projectPath "$PWD" \
  -executeMethod NeonVelocity.EditorTools.BuildTools.BuildMacOS \
  -logFile /tmp/nv-build-macos.log

# Capture (built player)
"Builds/macOS/NeonVelocity.app/Contents/MacOS/NEON VELOCITY" \
  -nv-capture /tmp/nv-captures -nv-capture-size 3840x2160
```

## 6. Honest conclusion

The tested macOS build is substantially more stable and more polished than the baseline: contextual navigation works on the covered routes, the 4K UI is legible, the shop has a working garage preview and purchase confirmation, the HUD no longer shows empty/clipped panels, the runner has layered procedural armour with generated materials, footsteps and spatial event audio load, wet-road reflections and the subway station render, and biome lighting now follows the authoritative seeded track plan. The game builds and runs without runtime exceptions in the capture flow. The visual target is closer to the supplied neon-cyberpunk reference, but it remains an original procedural interpretation rather than a AAA photoreal production.

Do not treat this report as proof that "all bugs are fixed." It records what was implemented and what was actually executed; the platform, device, network and performance gaps in section 4 remain open.
