# NEON VELOCITY — Development Progress

**Studio:** RK VANTAGE GAMES  
**Last updated:** 24 October 2026  
**Engine:** Unity 6000.6.7f1 / URP 17.6 / Input System 1.20  
**Repository:** `Ridma-kithara/NEON-VELOCITY`, branch `main`  
**Current scope:** Phase 1 plus the gameplay-environment/atmosphere vertical slice. This is not the complete AAA-scale transformation.

This is the working production record. It separates implemented work, verified evidence, known defects, asset/tooling gaps and the next tasks. The QA evidence lives in [TEST_REPORT.md](TEST_REPORT.md), [QA_REPORT.md](../QA_REPORT.md) and [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md).

---

## 1. Roadmap and status

| Phase | Scope | Status | Notes |
|---|---|---|---|
| **Phase 1 — Core product** | Original runner, modes, progression, saves, UI, shop/garage, multiplayer authority, settings, accessibility | **Substantially complete** | Existing verified systems preserved; full manual all-mode playthrough still outstanding. |
| **Environment / atmosphere vertical slice** | Real gameplay environment, city geometry, roads/sidewalks, street furniture, foliage, distant skyline, PBR-style materials, weather, rain, wet surfaces, lighting and biome transitions | **Implemented and visually verified** | This pass. Procedural/original assets only; no authored DCC assets. |
| **Presentation polish** | Character, armour, audio, HUD, UI, post-processing, 4K layout | **Substantially complete** | Automated tests and multi-resolution captures pass; live-device/4K performance not measured. |
| **Performance and platform hardening** | Profiling, LOD/occlusion, Windows build, release packaging, signing/notarisation | **In progress / blocked** | No Windows module; no profiler run; no physical 4K display. |
| **Authored AAA content** | Production meshes, textures, rigs, animation, cinematic audio | **Blocked** | Requires DCC assets and an art/audio production pipeline, which this code-only project does not have. |

### Current status in one line

The verified macOS build now renders a materially richer, original neon city with recessed storefronts, layered facades, street trees, furniture, signage, wet-road lighting, textured rain and a working 2.6-second biome crossfade. It remains a stylised procedural game, not a photoreal AAA production.

---

## 2. Environment vertical slice — implemented

### Architecture and streaming

- `TrackManager` continues to stream deterministic 42 m segments and pool segments, hazards and pickups.
- The city remains code-generated; no imported meshes, textures or animation clips are required.
- Buildings are recessed behind the playable canyon, with deterministic frontage blocks, alley gaps, podiums and world-scaled facade UVs.
- Street furniture, trees, planters, awnings, pipes, air-conditioning units, rooftop plant and signs are merged into the segment decor mesh to keep draw calls bounded.
- Additive light cards are now built into a second merged renderer with shadow casting disabled. This preserves material batching and fixes dark rectangle shadows that appeared when glow cards fell into the directional-light shadow map.

### Roads, sidewalks and drainage

- Asphalt with generated albedo/normal detail, a thicker structural deck, lane paint, reflective studs, wet gutter strips and puddle patches.
- Clean continuous lane field; previous transverse neon strips that read as unintended glowing crosswalks were removed.
- Raised sidewalks, curbs, drains, service aprons and a far setback plane eliminate visible void gaps.
- Safety rail with posts, caps, reflective markers and drain grates.

### Buildings and urban detail

- Three procedural facade families with world-unit window UVs, mipmaps, interior silhouettes, blinds and light spill.
- Varied heights, setbacks, podiums, roof plant, balcony/ledge bands, service boxes and facade pipes.
- Recessed street-level shops with glass, mullions, doors, shutters, product shelving, counters and ceiling lights.
- Warm under-awning strips, shop signage, projecting blade signs and abstract billboard art.
- Layered midground/background towers and a distant skyline instead of empty horizon space.
- The menu stage shares the improved procedural skyline and billboard language.

### Vegetation and street furniture

- Street trees with planter boxes, metal guards, thicker self-lit trunks, layered low-poly canopies and neon planter-edge strips.
- Benches, bins, street-name signs, lamp posts and warm sidewalk/road light pools.
- Trees and furniture sit outside the playable lane boundaries, behind the safety rail, so collision and multiplayer determinism are unchanged.

### Lighting, weather and atmosphere

- URP ACES tone mapping, controlled bloom, vignette, chromatic aberration at menu, film grain and motion blur where enabled.
- Fog, ambient trilight, sun, rim/fill lights and sky crossfade over **2.6 seconds** when the biome changes.
- Textured additive rain (`NV_RainStreak`) with stretch-billboard particles, wind slant, splash particles and mist. Untextured additive rain previously read as laser lines; particle-textured rain was too faint.
- Wet-road reflection dressing and warm practical pools beneath lamps/shop fronts.
- Tunnel, reactor and city themes retain distinct lighting and geometry. The transition capture holds the active crossfade so the fog/lighting/rain blend can be inspected without a full-screen veil.

### Render-quality controls

- Low/Medium/High/Ultra/Custom presets remain intact.
- MSAA, render scale, texture mip limits, shadow distance, reflections, effects density, view distance and VSync continue to be applied from saved settings.
- Additive rain density and particle caps scale with the effects quality setting.

### Verification and tooling

- `VisualCaptureRunner` now captures 9 screens: menu, settings, shop/garage, mode select, gameplay ×2, mid-biome transition, tunnel and pause.
- Captures use synchronous `CaptureScreenshotAsTexture()` readback and PNG encoding to avoid the earlier asynchronous duplicated-frame path.
- `sips` confirmed actual 1280×800, 1600×900 and 3840×2160 PNG dimensions.
- The transition capture is taken 0.65 s into a real biome change while the crossfade is active.

---

## 3. Defects found and fixed in this pass

| Defect | Fix | Verification |
|---|---|---|
| Untextured additive rain looked like bright laser lines; alternative particle texture was too faint | Rebuilt rain with `TextureFactory.RainStreak`, additive texture material, stretch billboards, wind slant and splash/mist tuning | Rain visible in 1600/1280/4K gameplay and transition captures; no runtime shader/material errors |
| Rain density too low to read at speed | Increased quality-scaled emission/max particles, narrowed the emitter volume and increased streak length | Final 4K frame shows rain against sky and street lights |
| Additive glow/haze cards cast solid dark rectangles across the road | Split transparent decor into a second merged renderer with `ShadowCastingMode.Off`; opaque architecture still casts shadows | Clean late-biome 4K capture; no large black wedge |
| Warm light pools were placed below the raised sidewalk/forecourt surface | Raised shop/lamp pools to the pavement surface and added a dim road-edge spill card | Pools now render above the surface; no z-fighting observed |
| Storefront ceiling strips/counter were vertically inverted in the generated texture | Moved ceiling lights to the top of the texture and the counter to pavement level | Shop interiors now read with ceiling light, glazing, shelving and counter |
| Facades were too dark and windows read as isolated cut-outs | Raised wall values, added light spill around lit window bays and brightened storefront exposure | Building walls, window frames and shop interiors readable at 1280/1600/4K |
| Tree trunks were too thin/dark and canopies appeared to float | Thicker trunks, larger branches, warmer self-lit bark, brighter foliage-highlight material and planter neon | Trunks visible in gameplay/transition captures |
| Ads and wall signs were too dark | Raised billboard/unlit sign tints while keeping the abstract ring/stroke artwork | Billboard and blade-sign art readable at night |
| Retail signage looked like boxed UI glyphs | Replaced with procedural shop interiors, abstract billboard art and thinner transit-board strokes | Visual inspection; no UI-like boxed signs remain in the storefront pass |

### Automated capture limitation observed

On this macOS Metal host, one automated capture run produced a tiled/duplicated-UI readback frame. Subsequent clean runs produced normal gameplay, transition and tunnel frames, and the final 1280, 1600 and 4K sets were inspected. The harness now uses the synchronous texture path; the intermittent readback behaviour is documented as a QA-tooling limitation, not claimed as fixed. No matching rendering error appears in the player log, and PlayMode tests do not reproduce a UI duplication. Live 4K/monitor capture remains unverified.

---

## 4. Verification evidence

### Automated checks (24 October 2026)

| Check | Result |
|---|---|
| Static project validation | **710 / 710 checks passed** after adding this document to required-doc checks |
| Unity EditMode | **24 / 24 passed** |
| Unity PlayMode | **9 / 9 passed** |
| Headless core | **50 / 50 passed** |
| Python authority | **9 / 9 passed** (10.386 s) |
| Build compilation | **Succeeded**; no script compilation errors; only Unity licensing/Intel-deprecation notices |
| macOS build | `Builds/macOS/NeonVelocity.app`, **117 MiB**, universal x86_64 + ARM64, ad-hoc signed |
| Runtime capture log | No runtime exceptions or warnings in the final player log |

PlayMode coverage remains: real bootstrap, title stage, Practice start, clean shutdown, quick-tap jump clearance, centralized navigation/Back routes, Settings-from-Pause, Results return, restart rebuilding a fresh run with reset distance, garage cleanup and purchase-confirmation safety.

### Visual captures

- **1600×900:** 9 screens, including mid-transition and tunnel.
- **1280×800 (16:10):** 9 screens; HUD and menu remain anchored and legible.
- **3840×2160:** 9 screens; all PNGs confirmed genuinely 3840×2160. The host display is 1600×900, so this is an actual large backbuffer capture, not a physical 4K-monitor test.
- Rain, wet road, recessed storefronts, layered facades, trees, street furniture, billboards, lamp pools, tunnel geometry and the active biome crossfade were inspected.

### Not verified

- Physical 4K/HDR display, cable negotiation and 4K frame rate.
- Windows/Linux/Android/iOS builds and real-device input/audio.
- CPU/GPU/memory/thermal profiling and long-session memory growth.
- Full manual playthrough of all 11 modes; every obstacle, power-up, collision, restart and persistence path.
- Real two/five-client Unity races and reconnect through the UI.
- Audible audio balancing and loudness.
- Live manual confirmation that the intermittent screenshot-readback artifact never appears on a physical display.
- Confirmed end-to-end shop debit on a disposable profile.

---

## 5. Performance observations

- The world remains segmented and pooled: segments, hazards and pickups are reused rather than instantiated without bound.
- Static decor is merged by material. The glow-card split adds one extra renderer per active segment, but avoids opaque shadow artifacts from additive cards.
- Rain emission scales with effects quality (220/400/650 particles per second; the particle cap scales up to roughly 2,100, or 300 in reduced-motion mode), with splashes capped separately.
- Facade and shop textures use generated mipmaps and world-scaled UVs to reduce shimmer.
- No LOD, occlusion culling, per-tree wind animation or GPU instancing pass was added in this slice.
- No profiler numbers are claimed. The 117 MiB build and 4K screenshot run prove output and completion, not a frame-time target.
- A real 4K/GPU profile and low-end device profile are the next performance tasks.

---

## 6. Asset and tooling gaps

- No authored environment meshes, PBR texture libraries, character rigs, animation clips or DCC source. The result is intentionally a strong stylised procedural interpretation, not photoreal AAA.
- No downloaded music, voice, licensed art packs or third-party characters/logos. All runtime art and audio remain original/procedural or synthesized.
- Cinemachine, Shader Graph and VFX Graph are not installed; camera and effects are hand-coded.
- Windows build support is not installed, so the Windows target could not be built or run.
- No physical 4K/HDR display, capture card or real-device test hardware.
- Dynamic tree/foliage wind is not implemented; tree geometry is merged into static segment meshes for draw-call efficiency.

---

## 7. Next tasks

1. Run a full manual macOS QA pass: all menus, Back routes, save/settings persistence, restart, collision/death, results and shop debit on a disposable profile.
2. Capture a live/OS-composited verification set to close the intermittent automated screenshot-readback limitation; keep the synchronous capture harness as a fallback.
3. Profile CPU/GPU/frame time and memory at 1600×900 and 4K; add LOD/occlusion or instancing where measurements justify it.
4. Add per-theme shop-interior variants and additional facade/storefront variation to reduce visible repetition.
5. Implement foliage wind and small environmental animation through a custom shader or a supported graph tool.
6. Add stronger wet-surface/reflection work, SSAO/GI alternatives, controlled lightning and more weather-state transitions.
7. Build and test Windows on hardware; then validate Android/iOS where modules and devices are available.
8. Exercise real two/five-client multiplayer, reconnect and network-failure UI paths.
9. Keep the progress, test, QA and limitations documents updated with every verified pass; never promote an unexecuted check to "passed".

---

## 8. Command reference

```bash
# Static validation
python3 Tools/validate_project.py

# Headless gameplay core
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet" \
  run --project Tools/CoreTests/CoreTests.csproj -c Release

# Backend authority
python3 -m unittest discover -s Backend/tests -v

# Unity EditMode
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode \
  -testResults /tmp/nv-editmode.xml -logFile /tmp/nv-editmode.log

# Unity PlayMode
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode \
  -testResults /tmp/nv-playmode.xml -logFile /tmp/nv-playmode.log

# macOS build
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit -projectPath "$PWD" \
  -executeMethod NeonVelocity.EditorTools.BuildTools.BuildMacOS \
  -logFile /tmp/nv-build.log

# Visual capture (built player)
"Builds/macOS/NeonVelocity.app/Contents/MacOS/NEON VELOCITY" \
  -nv-capture /tmp/nv-captures -nv-capture-size 3840x2160 \
  -screen-fullscreen 0 -screen-width 3840 -screen-height 2160
```
