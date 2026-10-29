# NEON VELOCITY — QA, UX and Visual Audit

**Studio:** RK VANTAGE GAMES  
**Game:** NEON VELOCITY  
**Audit date:** 23–29 October 2026  
**Engine:** Unity 6000.6.7f1 / URP 17.6 / Input System 1.20  
**Test host:** macOS 26.7.1, Intel Core i9-9980HK, 1600 × 900 host display  
**Build under test:** `Builds/macOS/NeonVelocity.app`, version 1.0.0, universal x86_64 + ARM64, ad-hoc signed, rebuilt from Phase 03 source on 29 October 2026 after the final shop label fix (122.5 MiB, 27.3 s build / 39.0 s wall)

This report records the defects found during the audit, the fixes applied, the checks actually executed, and the work that remains unverified. It is not a claim that every possible bug has been found or that every platform has been validated.

## 1. Final verification summary

| Check | Result | Status |
|---|---|---|
| Unity script compilation | 0 errors in runtime, editor-tools and test assemblies | Executed |
| Unity EditMode tests | **43 / 43 passed** | Executed |
| Unity PlayMode tests | **18 / 18 passed**, including a real 30 km streaming run and Phase 03 shop assertions | Executed |
| Headless gameplay-core tests | **66 / 66 passed** | Executed |
| Python backend tests | **79 / 79 passed** (race authority, accounts/auth, premium/payment, operator CLI and package configuration) | Executed |
| Static project integrity | **936 / 936 checks passed** | Executed |
| macOS universal player build | **Succeeded**, x86_64 + ARM64, ad-hoc signed, **122.5 MiB, 27.3 s build / 39.0 s wall**, rebuilt from Phase 03 source after the final label fix | Executed |
| Built player launch / capture run | Completed without runtime exceptions | Executed |
| UI captures at 1600 × 900 | 18 Phase 02 screens inspected (menu, settings, premium-network settings, garage, modes, account tabs, events, world atlas, achievements, characters, five GEM tiers, gameplay ×2, active biome transition, subway/station, pause) | Executed (Phase 02 set) |
| UI captures at 1280 × 800 (16:10) | Fresh 18 Phase 03 screens inspected, including the rebuilt GEM shop and post-fix labels; the Phase 02 1280 set is retained for comparison | Executed (Phase 03 set) |
| 4K render/capture at 3840 × 2160 | Same 18 Phase 02 screens; PNGs confirmed 3840 × 2160 | Executed |
| Premium/payment backend (local HTTP/SQLite) | Catalogue/admin, accounts, receipts, Stripe/Apple/Google verification, refunds and idempotent purchase/revive paths passed 79 tests | Executed |
| Live provider purchase/deployment | No HTTPS host, merchant credentials, store products or real transaction | Unverified |
| Live Stripe/Apple/Google payments | Stripe/Apple/Google paths are stubbed/generated-key tests; no live sale | Unverified |
| Live SMTP/SMS/Google/Apple sign-in | No provider credentials or native Google/Apple sign-in plugin | Unverified |
| Phase 03 shop visual capture | PlayMode text/layout coverage only; captures predate the shop rewrite | Unverified |
| Physical 4K display output | Not available | Unverified |
| Windows / Linux / Android / iOS builds | Platform modules not available/verified | Unverified |
| Real-device touch, controller, audio, suspend/resume | No test devices | Unverified |
| Real multi-client Unity race | No second Unity client/device | Unverified |
| CPU/GPU/memory/thermal profiling | No profiling run | Unverified |
| Full manual playthrough of all 15 modes | Not completed | Unverified |

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

**Test:** PlayMode navigation suite (twelve navigation/context tests, including the modal/quit-title and restart cases). Also verified captured Back controls at 1280 × 800, 1600 × 900 and 3840 × 2160.

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

**Result:** At that pass, 53/53 core, 30/30 EditMode, 16/16 PlayMode; the final suites are 66/66 core, 40/40 EditMode and 18/18 PlayMode.

**Limitation:** None known for these fields.

---

### QA-11 — Medium — 4K/16:10 UI scaling had never been captured

**Root cause:** Earlier verification used a single 1600 × 900 capture. The code claims 4K support but had no proof of a 3840 × 2160 output or a 16:10 layout.

**Files:** `Assets/Game/Scripts/Utilities/VisualCaptureRunner.cs`

**Fix:** Extended the capture harness to ten screens (including the premium-network settings and a mid-biome transition) and added `-nv-capture-size`, explicitly requesting windowed resolution and capturing actual backbuffer screenshots. The harness now re-asserts the requested resolution before each readback because macOS can recreate the player surface at the display's native size. Ran 1600 × 900, 1280 × 800 and 3840 × 2160.

**Test:** Verified PNG dimensions with `sips`; inspected menu, settings, premium-network settings, shop, modes, gameplay, transition, tunnel and pause at all three sizes.

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

**Limitation:** The capture fast-forwards with `Time.timeScale = 6` for up to a 240 s wall-clock window and clears nearby hazards, so it is a visual smoke test of the generated biome, not proof of a normal-speed manual tunnel playthrough.

---

### QA-14 — High — Gameplay environment lacked depth and had visible lighting/rain defects

**Root cause:** The procedural city was still dominated by flat wall bands, thin storefront cut-outs and sparse street detail. Earlier rain attempts either used untextured additive streaks that read as laser lines or particles whose texture was too faint. Several warm light cards were placed below the raised sidewalk/forecourt surface, so they were occluded. Transparent glow/haze cards were merged into the shadow-casting decor renderer, producing solid dark rectangles on the road. Tree trunks were thin and dark enough to disappear, leaving canopies looking unsupported.

**Files:** `Assets/Game/Scripts/World/TrackManager.cs`, `EnvironmentController.cs`, `MenuStage.cs`, `Assets/Game/Scripts/Utilities/TextureFactory.cs`, `MaterialLibrary.cs`, `PrimitiveMeshes.cs`, `Assets/Game/Scripts/Player/RunnerCamera.cs`, `Assets/Game/Scripts/Utilities/VisualCaptureRunner.cs`

**Fix:** Rebuilt the city frontage with recessed storefronts, layered facades, podiums, awnings, shutters, shelving/counters/ceiling lights, service pipes, AC units, balconies, rooftop plant and a layered skyline. Added raised sidewalks, gutters, drains, benches, bins, signs, lamps, warm practical light pools and street trees with visible self-lit trunks. Reworked the generated shop/facade/billboard textures, raised the light pools above the pavement, moved additive glow cards into a separate merged renderer with shadows disabled, and rebuilt rain with a generated streak texture, stretch billboards, wind slant, splashes and mist. Added a 2.6-second fog/ambient/sun/rim/fill/camera/rain/sky crossfade.

**Test:** Rebuilt the macOS player and captured ten screens at 1600 × 900, 1280 × 800 and 3840 × 2160. `sips` confirmed the 4K dimensions. Inspected the menu, premium-network settings, garage, game HUD, wet road, storefronts, sidewalk, trees, billboards, lamp pools, textured rain, tunnel and an active mid-transition frame. Final player log contained no runtime exceptions or warnings. Static checks, EditMode, PlayMode, core and backend suites were rerun.

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

### QA-16 — High — HUD readouts shared one ticker, pause toasts overlapped the panel, and compact menu labels clipped

**Root cause:** `NumberTicker.Attach(gameObject, …)` was called three times with the HUD root as owner, so `GetComponent<NumberTicker>()` returned the same component. The credits ticker replaced the score and distance labels, leaving both at their initial text. Separately, toasts render on a top-sorting canvas above the screen layer, so pause panels left floating toasts (`+4 ENERGY`, `MAGNET`) visible over the pause card at shorter aspect ratios. Main-menu grid buttons had no layout preferred/min height, so compact title rects collapsed to 1 px.

**Files:** `Assets/Game/Scripts/UI/HUDScreen.cs`, `Assets/Game/Scripts/GameApp.cs`, `Assets/Game/Scripts/UI/UiFx.cs`, `Assets/Game/Scripts/UI/UIRoot.cs`, `Assets/Game/Scripts/UI/Screens/MainMenuScreen.cs`, `Assets/Game/Tests/PlayMode/NavigationTests.cs`

**Fix:** Each ticker now attaches to its own text GameObject. The telemetry card was enlarged and the distance line moved below the score. The objective label reads `OBJECTIVE · …` to distinguish it from the timer. `ShowPause` hides the HUD and calls `UIRoot.ClearToasts`, which deactivates toast panels while their lifetime coroutines dispose of them safely; `ResumeFromPause` restores the HUD. Grid buttons receive a 64 px preferred/min height.

**Test:** New PlayMode regressions `HudScoreAndDistanceTickersTrackTheRunIndependently`, `PauseHidesHudAndResumeRestoresIt` and `MainMenuShowsEveryDestinationLabel`; the pause test creates a live toast, pauses, and asserts that no toast remains active. Rebuilt player and inspected all ten screens at 1600 × 900, 1280 × 800 and 3840 × 2160.

**Result:** Passed. HUD readouts update independently (`SCORE 161`, `DISTANCE 12 m`), pause shows no HUD or toasts, and all main-menu destination labels are visible.

**Limitation:** Automated layout checks assert rect heights and label text, not pixel-perfect typography at every language/font scale.

---

### QA-17 — Critical — Built player drew the UI over a black 3D world

**Root cause:** `PC_Renderer.asset` still contained a `ScreenSpaceAmbientOcclusion` renderer feature whose stripped resources threw during URP renderer construction. After removing it, URP unused-variant stripping still removed variants required by runtime-created world materials, so the camera drew only its clear colour. `UberPost` was also stripped.

**Files:** `Assets/Settings/PC_Renderer.asset`, `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset`, `Assets/Game/Scripts/Editor/BuildTools.cs`, `Assets/Game/Scripts/Utilities/VisualCaptureRunner.cs`

**Fix:** Removed the SSAO feature and restored the PC renderer to native render pass plus Forward+. Set `m_StripUnusedVariants` to `0` in all three global-settings locations because every world material is created at runtime. Added `Hidden/Universal Render Pipeline/UberPost` to Always Included Shaders.

**Test:** An offscreen camera readback returned only the clear colour before the fix and scene content after it. Rebuilt the player and captured all 18 screens at 1600 × 900, 1280 × 800 and 3840 × 2160; the city, runners, pickups, rain and post-processing render, with no shader-stripping warnings in the player log.

**Result:** Passed for the captured macOS player.

**Limitation:** URP unused-variant stripping must remain disabled until world materials are authored assets rather than runtime-created; this increases build size and build time. Other platforms are unverified.

### QA-18 — High — Long header section labels overlapped the subtitle

**Root cause:** `ScreenKit.Header` placed the section label and subtitle on the same baseline band. Sections with long labels (ACCOUNT, PREMIUM SUPPLY, ROUTE EXPLORATION) collided with the description.

**Fix:** Section, title and subtitle now occupy three stacked rows inside the header band, with the title and subtitle shortened to fit above the bottom rule.

**Test:** 1600 × 900 account/events/atlas captures and PlayMode screen-construction tests.

**Result:** Passed; no overlapping text in the inspected captures.

### QA-19 — Medium — In-run toasts drew over the timer card and district banner

**Root cause:** The in-run toast offset was 150 px, while the district banner occupied roughly 148–196 px and the centre timer card extended below it.

**Fix:** In-run toasts now start at 212 px; menu toasts remain at 24 px. Pausing still deactivates in-flight toasts.

**Test:** PlayMode pause/toast suppression test and gameplay captures.

**Result:** Passed.

### QA-20 — Medium — The centre HUD timer was captioned as a distance objective

**Root cause:** The timer digits sat above an `OBJECTIVE · DISTANCE` label, so elapsed time could be mistaken for a distance target.

**Fix:** Added a `RUN TIME` / `TIME LEFT` caption above the digits, widened the objective label and kept distance in the telemetry card.

**Test:** HUD ticker PlayMode test and 1600/1280/4K gameplay captures.

**Result:** Passed.

### QA-21 — Medium — No master volume control

**Root cause:** Settings exposed music, effects and ambience only, so there was no single mute/overall level control.

**Fix:** Added `SettingsData.masterVolume` (default 1, clamped 0–1), multiplied it into music, ambience, SFX, spatial SFX and pause attenuation, exposed `AudioManager.SetMasterVolume`, and added the MASTER slider above the existing buses. The audio panel was raised from 300 to 358 px.

**Test:** EditMode save round-trip assertion for master volume, settings capture, and existing audio path checks.

**Result:** Passed.

### QA-22 — Medium — Local capture server served the old four-tier GEM catalogue

**Root cause:** The capture clone's premium server process was still running the pre-five-tier module after the backend was updated.

**Fix:** Restarted the clone server from the updated source. `/api/v1/store/catalog` now returns STARTER, BASIC, PREMIUM, MEGA and ULTIMATE with the configured amounts/prices.

**Test:** Catalogue curl check and the final GEM-shop capture.

**Result:** Passed.

> **Phase 03 note:** QA-23 to QA-25 describe the retired manual bank-transfer card. That UI was removed when the shop was rebuilt around official store checkout and transaction history, so those specific layout paths no longer exist. The overflow-safety lesson was retained centrally in `UITheme.Button` (QA-26). The Phase 03 findings are recorded in section 6.

### QA-23 — High — GEM shop account number overlapped BRANCH and vanished at 1280×800

**Root cause:** The account number was drawn as a 28 pt label anchored from `-354` to `-318`, while the BRANCH row occupied `-330` to `-308`. The 12-unit overlap pushed the digits into the BRANCH caption; the 1600 × 900 capture showed the collision directly. After converting the field to a 20 pt labelled row, a second resolution-specific failure appeared: at 1280 × 800 the value was dropped entirely because the 20 pt line height slightly exceeded the 22-unit row at the smaller canvas scale, and `verticalOverflow = Truncate` discarded the line.

**Fix:** Replaced the oversized standalone number with a labelled bank row: `ACCOUNT NUMBER` caption at 12 pt on the left and the number at 20 pt bold on the right. The row is 24 units tall (`-334` to `-358`), leaving a 4-unit gap below BRANCH and a 4-unit gap above COPY ACCOUNT NUMBER. The value sets `verticalOverflow = Overflow` so the line is never discarded; horizontal overflow is also enabled so a font-metric change cannot wrap it. No downstream controls moved.

**Files:** `Assets/Game/Scripts/UI/Screens/GemShopScreen.cs`

**Test:** Full-resolution crops of the final 1600 × 900, 1280 × 800 and 3840 × 2160 GEM shop captures. The number is visible, right-aligned and visually separated at every size; the 4K capture shows the full row.

**Result:** Passed at all three resolutions.

### QA-24 — Medium — Order-detail warning overlapped the reference row

**Root cause:** The transfer-instructions card placed the six bank/amount rows 32 units apart, ending the REFERENCE row at `-292`, but started the three-line warning at `-270`. The warning's first line therefore drew inside the reference row.

**Fix:** Tightened the detail rows to 30-unit spacing (`-100` to `-250`), moved the warning to `-286…-342`, and shifted the copy row, proof heading and proof path to `-352…-476`. The bottom-anchored upload control now clears the path field by 2 units.

**Files:** `Assets/Game/Scripts/UI/Screens/GemShopScreen.cs`

**Test:** Layout arithmetic across the 560-unit card; the orders tab is covered by PlayMode GEM-shop construction but is not part of the 18-screen capture set, so this specific card has not been visually captured.

**Result:** Fixed geometrically; visual capture still outstanding.

### QA-25 — Medium — GEM wallet disclaimer dropped its final clause

**Root cause:** The “Gameplay awards CREDITS only…” note relied on a 48-unit rect and natural wrapping. At 14 pt it needed four lines in the wallet width, and `verticalOverflow = Truncate` dropped the fourth, so the visible sentence ended at “they are” and the word “premium-only.” never appeared. The order-detail warning had the same class of risk: a 56-unit rect for three 17 pt lines.

**Fix:** Rewrote the wallet note as three deliberate lines whose measured widths are 211, 248 and 276 px at 14 pt, all inside the 368 px content width. The rect is now 58 units (`-592` to `-650`), leaving a 4-unit gap above RECEIPT EMAIL. Extended the order-detail warning from 56 to 62 units (`-286` to `-348`), keeping a 4-unit gap above the copy row at `-352`.

**Files:** `Assets/Game/Scripts/UI/Screens/GemShopScreen.cs`

**Test:** Crops from the final rebuilt-player GEM shop at 1600 × 900, 1280 × 800 and 3840 × 2160 show all three lines and the complete sentence. The order card remains outside the 18-screen capture set; its wording measures to three lines inside the new 62-unit rect.

**Result:** Passed at all three resolutions; the order warning is fixed geometrically but remains visually uncaptured.

### QA-26 — High — Long button labels wrapped and were clipped at 4K

**Root cause:** `UITheme.Button` created its label with the default `HorizontalWrapMode.Wrap` and `VerticalWrapMode.Truncate`. At a 4K canvas scale the “COPY ACCOUNT NUMBER” label wrapped before “NUMBER”, and the second line was discarded by the button's 30-unit label height. The button visibly read “COPY ACCOUNT” even though it copied the full number.

**Fix:** Button labels are now single-line by contract: `horizontalOverflow = Overflow` and `verticalOverflow = Overflow` are applied centrally in `UITheme.Button`. A long label can no longer wrap to a hidden second line or be dropped at any canvas scale.

**Files:** `Assets/Game/Scripts/UI/UITheme.cs`

**Test:** Final rebuilt-player captures at 1600 × 900, 1280 × 800 and 3840 × 2160. The 4K GEM shop now reads “COPY ACCOUNT NUMBER” and “PREMIUM STORE” in full; the same labels were clean at 1600 and 1280. The current PlayMode suite passes 18/18.

**Result:** Passed at all three resolutions.

---

### QA-27 — Critical — Landmarks silently disappeared from several remapped 3 km districts

**Root cause:** The twenty-theme remap left several `LocationCatalog` signature IDs above 19 while `TrackManager.BuildSignature` only handles 0–19. Those districts therefore built a normal road with no landmark at all. No exception was raised because the default switch branch simply returned.

**Fix:** Remapped all twenty districts to unique signature IDs 0–19 and added uniqueness/range assertions in the headless and EditMode location contracts so a future duplicate or out-of-range signature fails a test instead of silently deleting a landmark.

**Files:** `Assets/Game/Scripts/Core/LocationCatalog.cs`, `Assets/Game/Tests/EditMode/CoreGameplayTests.cs`, `Tools/CoreTests/Program.cs`

**Test:** Headless core `locations carry valid weather, time of day and structural style` and `districts are unique and never repeat back-to-back`; EditMode `LocationCatalog_HasTwentyUniqueThemesOnThreeKilometreZones`.

**Result:** Passed. All twenty signatures are unique and inside the wired range.

---

### QA-28 — High — Seven architecture builders existed but were unreachable

**Root cause:** Luxury, cryo harbor, alpine village, Seoul, skybridge, spaceport and ruins builders had been added, but `TrackManager.BuildDecor` still routed their styles to the generic city builder. The districts looked like palette swaps despite having dedicated code.

**Fix:** Wired styles 1 and 11–16 to the seven builders. Each theme now uses its intended structural family; related themes retain their own palette, sky, weather, road key and landmark.

**Files:** `Assets/Game/Scripts/World/TrackManager.cs`

**Test:** Unity script compilation and the full EditMode/PlayMode suites.

**Result:** Passed. The twenty-theme architecture dispatch is now reachable at runtime.

---

### QA-29 — Medium — T-junction branch deck left a visible hole at the median

**Root cause:** The curved branch deck was a fixed 3.05 m-wide strip around the branch lane. At a T-junction the lane curves away from the central divider, so the strip stopped short and exposed a void between the branch and the median.

**Fix:** T-junction decks now extend from the branch lane to the median with an extra 0.2 m overlap while the painted lane follows the actual route centre. Shortcuts keep the narrower marked deck.

**Files:** `Assets/Game/Scripts/World/TrackManager.cs`

**Test:** `JunctionRoutes_StayOnTheDeckAndReturnToCentre`, junction connectivity tests, and visual inspection of the generated junction geometry.

**Result:** Passed. Geometry and collision both reach the median.

---

### QA-30 — Medium — Pooled hazard/pickup diagnostics lagged the active window

**Root cause:** Diagnostic counters were written inside segment construction, before the tick's trim pass. The reported pool size could be one segment larger than the active window on the first frame after a new segment was built.

**Fix:** Moved `PooledObstacleCount` and `PooledPickupCount` to the end of `TrackManager.Tick`, after build and trim. The 30 km test now verifies proportionality every step instead of allowing a one-frame skew.

**Files:** `Assets/Game/Scripts/World/TrackManager.cs`, `Assets/Game/Tests/PlayMode/LongRunTests.cs`

**Test:** `LongRunTests.TrackStreamsThirtyKilometresWithinBoundedPoolsAndZones`.

**Result:** Passed at 30,030 m.

---

### QA-31 — Medium — PlayMode restart test assumed every profile starts at zero

**Root cause:** The restart test asserted `Distance < 1 m`. The local profile has a level-2 `head_start` upgrade worth 110 m, so a correct restart begins at 110 m and the test failed. The test, not the restart path, was wrong.

**Fix:** The test now derives the expected upgraded start line from `ProgressionMath` and asserts the restarted run is below the prior run's distance and within that upgraded start window.

**Files:** `Assets/Game/Tests/PlayMode/NavigationTests.cs`

**Test:** `RestartSessionRebuildsTheRunAndResetsDistance`.

**Result:** Passed with the existing upgraded profile.

---

### QA-32 — High — Signature hazards could sit beside an unavoidable lane block

**Root cause:** Event hazards (train, traffic, falling debris, sliding platform) were placed on any row whose own lane was free, so a sweep could arrive beside a solid barrier in a neighbouring row. At high speed this could become unreadable or unfair.

**Fix:** Extracted `HazardRules` as pure Core logic: a signature hazard is allowed only on a row that is full-width empty and whose previous and next rows are also full-width empty. The runtime and tests consume the same rule.

**Files:** `Assets/Game/Scripts/Core/HazardRules.cs` (new), `Assets/Game/Scripts/World/TrackManager.cs`, `Assets/Game/Tests/EditMode/CoreGameplayTests.cs`

**Test:** `HazardRules_OnlyUseFullWidthEmptyRows` over a 30 km/715-segment run, plus the junction/event safety suites.

**Result:** Passed. At least twenty fair signature-hazard slots remain in a 30 km run.

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
- Infinite total-XP progression with exact authored thresholds, post-10 growth, milestone rewards and v5 save schema/v4 XP migration; path: `LevelProgression.cs`, `ProgressionMath.cs`, `SaveData.cs`.
- Twenty 3 km districts with per-zone palettes, weather, road keys, landmark signatures, hazard bias and persistent discovery; paths: `LocationCatalog.cs`, `TrackManager.cs`, `EnvironmentController.cs`.
- Seven-state time-of-day profiles with smooth sun/moon, sky, ambient/directional light, shadows, fog, streetlights, neon, stars, reflections and weather blending, plus authored/dynamic/fixed modes; paths: `TimeOfDaySystem.cs`, `EnvironmentController.cs`, `SettingsScreen.cs`, `SaveData.cs`.
- Nine connected junction kinds (including T-junction, curved road, shortcut and collapsing bridge) with deterministic scheduling, real curved decks, 126 m warning, safe centre-lane commitment, connected collision, merge and authored reward routes; paths: `Junctions.cs`, `TrackDirector.cs`, `TrackManager.cs`, `RunnerController.cs`, `HUDScreen.cs`.
- Fair signature-hazard placement limited to full-width-empty rows with empty neighbours; path: `HazardRules.cs`, `TrackManager.cs`.
- Fifteen modes including Boss Chase and Route Exploration, with the Warden pursuit mech and the local event calendar; paths: `GameModes.cs`, `GameSession.cs`, `EnvironmentController.cs`, `EventsScreen.cs`.
- Server-authoritative COIN/GEM economy with five GEM tiers, catalogue/admin overrides, Unity IAP Apple/Google purchases, Stripe-hosted checkout, server verification, receipts, refunds and idempotent purchase/revive debits; paths: `Backend/premium_server.py`, `premium_store.py`, `payments.py`, `identity.py`, `premium_admin.py`, `Assets/Game/Scripts/Premium/PremiumService.cs`, `PlatformStore.cs`, `UI/Screens/GemShopScreen.cs`.
- Accounts, email/password, phone OTP, guest linking, verification/recovery, conflict-safe cloud sync, receipt-email handling, Google/Apple server token verification and transaction history; paths: `Backend/account_store.py`, `mailer.py`, `identity.py`, `Assets/Game/Scripts/Account/*`, `UI/Screens/AccountScreen.cs`.
- PHANTOM X, NOVA TITAN, ECHO, SABLE and HELIOS abilities, premium upgrades and exact-position revive with **NOT ENOUGH GEMS!** recovery actions; paths: `GameConfig.cs`, `RunnerController.cs`, `GameSession.cs`, `ResultsScreen.cs`.
- Editable premium endpoint with cache reset in **Settings → Network**; paths: `SettingsScreen.cs`, `PremiumService.cs`, `SaveData.cs`.
- Master volume plus stacked headers, lower in-run toasts and a captioned run-time HUD; paths: `AudioManager.cs`, `SettingsScreen.cs`, `ScreenKit.cs`, `UIRoot.cs`, `HUDScreen.cs`, `GameSession.cs`.

## 4. Remaining limitations and unverified work

These are not reported as fixed:

1. **Platform coverage:** only macOS was built and launched. Windows, Linux, WebGL, Android and iOS builds were not produced or run.
2. **Physical 4K:** the player produced 4K PNGs in a 1600 × 900 host session, but no physical 4K display, HDR display, HDMI/DisplayPort negotiation or 4K performance measurement was available.
3. **Mobile:** no Android/iOS device. Touch gestures, safe areas, notches, thermal throttling and Android system Back remain unverified. Quality settings are coded to stay independent of desktop.
4. **Controller and accessibility:** keyboard/controller code paths exist and some are covered indirectly, but focus order, remapping, vibration and real-device gamepad navigation were not tested. No screen-reader/color-blind palette support.
5. **Multiplayer:** the Python authority passes 9/9 loopback integration tests. A real two- or five-client Unity race, reconnect through the UI, latency/loss conditions and NAT/TLS deployment were not tested.
6. **Performance:** no CPU/GPU/memory/thermal profiling. 60 FPS at 4K is a goal, not a measured result.
7. **Content:** art/audio are original procedural primitives/synthesis. The armour pass adds generated textures and layered geometry, but it is not equivalent to authored AAA character/environment assets; modular cosmetics still share one procedural rig and are visual-only.
8. **Testing depth:** the automated suite covers the menu/practice bootstrap, jump clearance, navigation, modal priority, garage lifecycle, purchase confirmation, premium screens/tabs, ability cooldown, exact revive, endpoint cache reset and core/backend rules. It does not exercise every power-up, obstacle, mode, button or failure path.
9. **Signing/release:** the macOS app is ad-hoc signed and not notarised. No store packaging, privacy metadata or release pipeline exists.
10. **Save safety:** the local profile is not cryptographically tamper-proof and is not authoritative for GEMS or online rewards. Changing the premium endpoint clears cached wallet/entitlements. Cloud sync merges progression only; GEMS, payments and premium ownership stay server-owned.
11. **Audio quality:** all 32 synthesized clips load in the player and the new spatial one-shots produced no missing-resource errors, but no listening test, loudness measurement or device-output validation was performed.
12. **Screenshot capture:** one automated macOS Metal run produced a tiled/duplicated-UI readback frame. Clean reruns and the final 1280/1600/4K sets were inspected, but the cause was not isolated. Live physical-display capture and 4K frame-rate profiling remain unverified. A fresh 18-screen Phase 03 1280×800 set was captured from the final post-fix build and inspected; the GEM shop labels are no longer clipped.
13. **Environment depth:** dynamic tree/foliage wind, explicit LOD/occlusion and profiler-driven performance tuning were not implemented. Rain/light pools are deliberately subtle at night, and storefront interiors still repeat within a theme.
14. **Premium operations:** no HTTPS host, admin credential, provider credentials or store products are deployed. No Stripe/Apple/Google transaction has been completed. The backend paths are tested locally with stubs/generated keys; receipts require SMTP, and Apple/Google refunds require operator action after the store console refund.
15. **Requested mechanics not implemented:** drone chases, rideable moving platforms, multi-stage destructible structures, temporary vehicles, explicit Friend Challenge and Limited-Time Event mode entries, and a fully drivable perpendicular 90° cross-street network. Moving trains, traffic vehicles, falling debris, sliding obstacle platforms and collapsing-bridge gaps are implemented as hazards. These remaining items are product gaps, not untested code.
16. **Auth/provider boundaries:** server-side Google ID-token and Apple identity-token verification exist, but the Unity build has no native Google/Apple sign-in plugin. The provider buttons explain the boundary instead of fabricating a session.

## 5. Progression and premium-economy continuation (25–26 October)

The audit was extended from the visual/gameplay pass to the infinite-progression and premium-economy work. This continuation was implemented and tested locally; it is **not** a live payment deployment.

- **Progression:** exact L1–L10 cumulative thresholds (0, 450, 900, 1,800, 3,000, 4,500, 6,500, 9,000, 12,000, 16,000), then 1.12× growth to a 1,000,000 XP requirement cap. Level is always derived from one `long totalXp`.
- **Districts:** 20 authored 3 km districts (exactly the twenty requested themes) with unique palettes, sky, weather, road keys, landmark signatures and hazard bias, deterministic wrap, no back-to-back repeats and persistent first-discovery records.
- **Premium authority (Phase 02 baseline, superseded in Phase 03):** the service originally used exact-LKR bank orders and proof upload. Phase 03 removed that player flow. GEMS are still never awarded by gameplay.
- **Unity client (Phase 03):** GEM SHOP (BUY GEMS / PREMIUM STORE / TRANSACTION HISTORY), provider selection, receipt email, transaction history, five premium runners, premium upgrades and exact-position revive. CREDITS were separated from GEMS in every label.
- **Signature runners:** PHANTOM X phase dash and NOVA TITAN nova burst, plus ECHO, SABLE and HELIOS, are balanced sidegrades with server ownership, HUD cooldown and Q/E or gamepad-shoulder activation.
- **Junctions and modes:** nine connected junction kinds with physically curved branch decks, committed branches, safe centre-lane auto-commit and merge; 15 modes with Boss Chase and Route Exploration; a visible non-colliding Warden mech; and a deterministic local event calendar.
- **Exact revive:** captures death distance/lane/pose/speed, previews results, performs one server-verified debit and resumes the same run with a stabilisation window. Leaving or restarting finalises the pending run once.

**Defects found and fixed during the continuation**

| Defect | Fix | Verification |
|---|---|---|
| `LevelProgression.MaxTotalXp` called `TotalXpForLevel`, which called `MaxTotalXp`, overflowing the stack when XP was granted | Cache a closed-form maximum and split cumulative computation from the clamped accessor | Exact-threshold EditMode/headless tests and a real PlayMode run |
| Legacy `xp` could remain written alongside authoritative `totalXp` | Migrate once, then clear the legacy field and derive level from `totalXp` | Save-v4 migration test and JSON round-trip |
| Credits were still labelled “GEMS” in several places, conflating earned and premium currency | Corrected all non-store labels to CREDITS; GEMS remain premium-only | Label audit and UI smoke tests |
| Premium endpoint existed in the save but was not editable and did not refresh | Added **Settings → Network → Premium Store URL**, URL validation and cache/token clearing on host change | PlayMode endpoint-cache test and compilation |
| Revive originally restarted partway through the run | Capture and restore the exact death state; finalise the pending run once | PlayMode exact-distance revive test |
| Long header labels overlapped subtitles | Stacked section/title/subtitle rows | Captures and PlayMode construction |
| In-run toasts overlapped the timer/district banner | Raised the toast offset to 212 px | Pause/toast PlayMode test and captures |
| Timer looked like a distance objective | Added `RUN TIME` / `TIME LEFT` caption | HUD test and captures |
| No master volume control | Persisted master volume multiplied into every bus | EditMode save assertion and capture |
| Capture server served four GEM tiers | Restarted it from the five-tier backend | Catalogue curl and final capture |
| Store UI still showed the retired bank-transfer layout | Rebuilt the screen as BUY GEMS / PREMIUM STORE / TRANSACTION HISTORY and removed account/proof/order copy | PlayMode tab/label assertions and retired-copy scan |
| Duplicate platform purchase zeroed the cached GEM display | Preserved the cached balance on an already-processed purchase response | Backend duplicate-purchase tests |
| Google Play verification omitted the package name | Included the package name and product id in the verification request | Backend Google verifier tests |
| Long button labels wrapped and vanished at 4K | Made every button label single-line with overflow enabled | 1600/1280/4K captures and final PlayMode pass |

**Result (Phase 02 baseline):** 40/40 EditMode, 18/18 PlayMode, 66/66 headless core, 59/59 backend and 910/910 static checks passed at that time. Those counts are superseded by section 6. The macOS build was rebuilt and the 18 screens from the previous verified build were captured at 1600 × 900, 1280 × 800 and 3840 × 2160.

## 6. Phase 03 account, offline, economy and payment pass (29 October 2026)

Phase 03 connected the existing UI to real account, offline-save, server-wallet, checkout, receipt, refund and admin systems without rewriting gameplay. It also removed the manual bank-transfer player flow.

### What was implemented

- **Authentication:** email/password register/login/logout, email verification and resend, password recovery/reset, phone OTP start/verify, guest wallets and guest-to-account linking, persistent bearer sessions, profile edits and receipt email. The backend verifies Google ID tokens and Apple identity tokens when configured.
- **Offline:** the game boots and plays without an account. XP, level, best/total distance, COINS, character/unlocks/items/upgrades, missions, achievements, discoveries and settings persist locally.
- **Save/sync:** cloud merge uses `baseRevision`, an idempotency key, max scalar progression, unioned reward IDs and conflict summaries. GEMS, premiums, transactions and provider tokens are excluded from sync and remain server-authoritative.
- **Economy:** COINS are earned in gameplay; GEMS are real-money only. The server owns the wallet, catalogue, ownership, ledger, purchase validation, duplicate prevention and atomic revive debits.
- **Payments:** Apple IAP and Google Play Billing through Unity IAP 4.15.1, Stripe-hosted Checkout for desktop/web, server-side verification, signed webhooks, duplicate-charge/credit prevention, cancel/failure handling, transaction records and refund reversal. No card data or provider secret enters Unity.
- **Receipts:** sent after verified success when SMTP is configured; the destination defaults to the account's verified/receipt email and can be changed. Includes game name, account/player id, transaction id and provider reference, item/package, GEMS, amount/currency, provider, date and status.
- **Shop:** the UI is backed by the server catalogue, covering packages and premium characters/skins/helmets/armour/trails/animations/effects/power-ups/boosters/revives with mixed COIN/GEM/real-money pricing. Prices are operator-configurable.
- **Revive:** captures the exact death state, validates and debits GEMS once, then restores the run with brief invulnerability. Insufficient balance shows **NOT ENOUGH GEMS!** with BUY GEMS / RESTART / CLOSE; BUY GEMS opens the real shop.
- **Admin:** authenticated transaction/account review, catalogue and package/product overrides, refund reversal after provider confirmation and currency audit. Admin credentials are server-side only.

### Phase 03 defects found and fixed

| Defect | Fix | Verification |
|---|---|---|
| Shop still displayed bank account, exact LKR transfer, proof upload and order status | Replaced with official-provider purchase flow and transaction history; legacy routes return `403 MANUAL_TRANSFER_DISABLED` by default | PlayMode tab/label assertions, backend route tests |
| Duplicate platform-purchase response cleared the cached GEM display | Kept the cached balance when the server reported the purchase already processed | Backend duplicate tests |
| Google Play verification lacked the package name | Sent package name and product id with the token | Google verifier tests |
| Admin account detail could not join the wallet database | Attached the premium SQLite database before the cross-database query | Admin account tests |
| Platform purchase could start before the server had a verifier | Gated platform checkout on `/api/v1/store/payments` capability flags | Compile and capability-path review |
| Receipt address could silently differ from the verified account email | Defaulted to the account email and added a UI warning when a different receipt address is saved | UI review and backend receipt tests |

### What was actually executed

- **EditMode 43/43**, **PlayMode 18/18**, **headless core 66/66**, **backend 79/79**, **static 936/936**, **0 compile errors**.
- Backend tests boot the real HTTP service and temporary SQLite databases. They cover catalogue overrides, admin transactions/accounts/refunds, Stripe signed-webhook credit/refund and duplicate events, Apple receipt verification, Google service-account verification, receipt construction, account lifecycle, cloud sync, idempotent purchase/revive and operator CLI guards.
- PlayMode asserts the Phase 03 tab labels, absence of retired bank-transfer copy and the offline transaction-history state.

### What was not executed

- No live Stripe, Apple or Google transaction. No merchant account, store product, sandbox tester or HTTPS host was supplied. **Checkout opening is not treated as completion.**
- No real iOS/Android Unity IAP purchase on a device.
- No native Google/Apple **sign-in**; server token verification is tested with generated keys.
- No live SMTP receipt/verification delivery or SMS OTP delivery.
- No manual playthrough of the shop's success/cancel/duplicate/refund flows on hardware. The Phase 03 package screen is captured at 1280×800; transaction-history and receipt-success states are not.

## 7. How the final checks were run

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

## 8. Honest conclusion

The tested macOS build is substantially more stable and more polished than the baseline: contextual navigation works on the covered routes, the 4K UI is legible, the shop has a working garage preview and purchase confirmation, headers/toasts/timer captions no longer overlap or mislabel, the HUD shows a real run-time readout, master volume works, nine junction shapes are connected and safe on both branches with real curved decks, 20 districts and 15 modes are wired, the seven-state time-of-day system moves sun/moon/light/fog/neon/weather and can be authored, dynamic or fixed, the runner has layered procedural armour with generated materials, footsteps and spatial event audio load, wet-road reflections and the subway station render, and biome lighting follows the authoritative seeded track plan. The account, events, atlas, achievements, character and five-tier GEM screens all build and return correctly. Phase 03 adds real account/offline/sync/economy plumbing and official-provider payment boundaries; the shop no longer uses the retired bank-transfer workflow.

**No live payment has been proven.** No Stripe, Apple App Store or Google Play transaction was completed, no merchant credentials/store products were supplied, and no native Google/Apple sign-in plugin exists in the Unity build. Checkout opening, a compiled adapter or a stubbed verifier is not evidence of a live sale. The game builds and runs without runtime exceptions in the capture flow. The visual target is closer to the supplied neon-cyberpunk reference, but it remains an original procedural interpretation rather than a AAA photoreal production.

Do not treat this report as proof that "all bugs are fixed." It records what was implemented and what was actually executed; the platform, device, network, performance and unimplemented-mechanic gaps in section 4 remain open.
