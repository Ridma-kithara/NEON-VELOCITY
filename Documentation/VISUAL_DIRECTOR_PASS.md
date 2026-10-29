# NEON VELOCITY — Visual Director Pass (UI/UX + Presentation)

> **Historical pass record.** Counts and build sizes below describe the 25 October visual-director milestone. Current verification is **40/40 EditMode, 18/18 PlayMode, 66/66 headless, 59/59 backend and 910/910 static checks**, with the macOS build rebuilt on 28 October 2026; see [TEST_REPORT.md](TEST_REPORT.md) and [PROJECT_STATUS.md](../PROJECT_STATUS.md).

**Studio:** RK VANTAGE GAMES
**Pass date:** 25 October 2026
**Scope:** main character, environment/city, cinematic graphics, main menu, complete UI/UX — implemented directly in code.
**Constraint honoured:** the project is 100 % procedural (URP 17.6, Unity 6000.6.7f1). Every mesh, material, texture, sprite and sound in this game is generated in code; no external art was introduced and none was faked. Public gameplay APIs (RunnerVisuals Build/Tick/SetPose, GameApp.Show*, HUDScreen public methods) are unchanged.

---

## 1. Main character (priority 1) — `Assets/Game/Scripts/Player/RunnerVisuals.cs`

**Paint scheme (premium finish):**
- Glossy obsidian main armour (near-black with character secondary tinting), titanium-silver plating, machined graphite internals, polished chrome dress parts, cyan energy accents, magenta trim. Wet-spec look via low-smoothness contrast between armour and joints.

**Mechanical detail added (all procedural meshes):**
- Knee actuator rings, calf housings, bicep pistons, chest intake/vent arrays, back cable harnesses, heel exhaust jets, backpack exhaust bells (additive flares), fastener studs across plates, layered chest carapace with a live reactor core, twin-shell pauldrons, articulated gauntlets with fingers.

**Energy FX (no material clones):**
- `MaterialPropertyBlock` + `_EmissionColor` pulses on core/visor renderers; thruster jet scale pulses; sprint wake quad behind the runner. Fixed a Unity rule violation from the previous pass: `MaterialPropertyBlock` is now created lazily (never in a MonoBehaviour constructor), which had silently blocked boot.

**Animation states:**
- Sprint blend above 0.62 speed; double-jump somersault (`NotifyDoubleJump()` now wired from the RunnerController air-jump branch); new `RunnerPose.Revive` kneel-and-rise; improved Slide brace and Death collapse; Landing compression retained; pose set fully exposed: Idle / Run / Jump / Landing / Slide / Dead / Victory / Revive.

**Runtime fix validated by tests:** boot failed with the old constructor-initialised FX buffer; PlayMode suite now passes 9/9 after the lazy-buffer fix.

## 2. Environment / city

- **Menu stage** (`Assets/Game/Scripts/World/MenuStage.cs`): rebuilt skyline in three layered towers (families A/B/C with window facades, mast arrays, beacons), skybridges, an elevated airway with lantern pylons, drifting shuttles with lanterns, blade signs; camera reframed so the hero stands right-of-centre (LookAt x −2.1, DOF focus 7.4 / aperture 5.6).
- **Gameplay city** (`Assets/Game/Scripts/World/TrackManager.cs` BuildCity/BuildDecor): midground 4→6 towers up to 78 m with antenna arrays and beacons; far silhouettes up to 104 m with billboard quads; per-side sky bridges (36 %); cross-street arch (50 %); overhead media board (62 %). Adds depth so the $-axis corridor reads like a street canyon inside a city, not a toy-road.
- Biomes unchanged (NEON SPRAWL / SKYWAY 09 / REACTOR BELT / GHOST TUNNEL) and all hooks intact.

## 3. Cinematic graphics (`RunnerCamera.cs`)

Added to the existing ACES/bloom/vignette/CA/grade/grain/motion-blur pipeline:
- **WhiteBalance** (−9 temperature, +1.5 tint) — keys the night city cool so signage accents pop.
- **LensDistortion** (~0.08, scaled to 0.05 on low FX / 0 reduced-motion).
- **PaniniProjection** (0.14 default, 0.18 on high FX) — keeps tower verticals straight at high speed.
- All three respect `effectsQuality` and `reducedMotion` via `ApplyVideoSettings`.

## 4. UI/UX system (shared plumbing)

- `TextureFactory.PanelBorder` — new 9-sliced technical-frame sprite (hairline + corner bracket accents + top sheen interior-transparent).
- `Assets/Game/Scripts/UI/UiFx.cs` — new shared FX kit:
  - `HoverScale` (crit-damped hover grow with optional tint, mirrors keyboard/gamepad focus),
  - `PressScale` (press sink),
  - `NumberTicker` (0.65 s eased rolling numbers),
  - `PulseBar` (eased fill + sheen sweep on upward jumps).
- `UITheme.GlassCard` — signature panel (rounded dark fill + inset `PanelBorder`), used by every new surface.
- `ScreenKit.MenuButton` — now ships right chevron, hover underline sweep (MenuButtonUnderline), hover grow/press sink.
- `ScreenKit.RefreshCurrency` — affectionate GEMS rebrand: `LVL n   ◆ N GEMS`. `credits` field name unchanged for save compatibility.

## 5. Main menu — `Assets/Game/Scripts/UI/Screens/MainMenuScreen.cs`

- Full rebuild: hero-visible layout (left rail + two narrow right glass cards).
- Menu: **PLAY** (full width), **CONTINUE** (guarded by new `SaveData.lastModeId`; shows mode title + BEST, disabled when empty/online/tutorial), 2-up grid rows for **CHARACTER / GEM SHOP**, **UPGRADES / MISSIONS**, **MULTIPLAYER / RANKINGS**, then **SETTINGS / EXIT** row.
- Fields kept fresh on `ProfileChanged`: name/callsign, level + XP bar, best run, races; season card with `PulseBar` progress; daily claim card ("GEMS ON CLAIM"); animated ◆ GEMS ticker top-right (NumberTicker).
- Brand block adds a subtle title echo + rail glow; footer shows version/product facts.

## 6. New screens (all `GameApp`-wired)

- **CharacterScreen** — big garage turntable (GaragePreview reuse) with a 7-button pose stage (IDLE/SPRINT/JUMP/SOMERSAULT/SLIDE/VICTORY/REVIVE), name plate, trail/armour specs; roster column with equip/unlock via gems (Level gates preserved).
- **GemShopScreen** — "every gem is earned": left panel balances + earn guide + daily claim; right column prices locked runners/trails; deep-link to full shop.
- **UpgradeScreen** — dedicated tech bay: 5 upgrade lines with pip ladders, current value, next cost, one-touch purchases.
- **LoadingCard** (in `UIRoot.ShowLoading/HideLoading`) — full-screen cinematic overlay (brand eyebrow, mode title, objective, sweeping progress bar) shown during StartSession; also fills the 0.85 s build window.
- **PurchaseKit** — shared spend-confirmation (balance validation, cost/balance modal, double-tap guard) used by the new screens.
- `GameApp`: extended private AppScreen enum {Character, GemShop, Upgrades}, Show methods, BuildScreen cases, `ReviveRun`, `GameApp.ReviveCost = 400`, `StartSession(..., float reviveHeadStart = -1)`.

## 7. HUD / results / pause

**HUDScreen** (public API preserved: `Bind`, `SetOnlinePlayerList`, `SetPrompt`, `SetConnectionWarning`, `SetSpectating`, `SetRunOver`, `Flash`, `PulseCredits`, `Toast`, `ShowBanner`):
- Glass telemetry cards with cyan/amber borders; SCORE reads through a rolling NumberTicker; distance tape rolls in metres, auto-formats to km.
- Objective panel restyled; GEMS panel titled "GEMS" with ticker + pickup pulse.
- Velocity gauge: sheen-swept PulseBar + numeric "N M/S" readout; sheen fires on band jumps.
- New pinned **district callout** — "— ENTERING  NEON SPRAWL —" fades in/out on `Track.BiomeChanged` (`HUDScreen.ShowLocation`), plus initial callout at HUD attach, so runs open with a sense of place.
- Pause: glass panel restyle + RESUME hover FX; "ENERGY" → "GEMS".

**ResultsScreen:** rewards text now "+N GEMS +N XP"; **LEVEL UP badge** ("LEVEL UP ▸ RUNNER LV N", lime) next to NEW PERSONAL BEST (laying out side-by-side when both); **REVIVE PROTOCOL panel** for crashed offline runs with primary "REVIVE ◆ 400" button → `GameApp.ReviveRun`.

**Revive behaviour (additive, offline-only):** spends 400 GEMS via `ProgressionService.SpendCredits`, restarts the run with a head start at ~60 % of the death distance (`RunnerController.GrantHeadStart`: track retick, hazard clear, 3.6 s shield covering the countdown), same mode/spec as before. Blocked for online/ranked/survival and very short runs (<450 m) by design.

## 8. QA executed

| Check | Result |
|---|---|
| `Tools/validate_project.py` static integrity | **745 / 745 PASS** (after adding .meta for 5 new scripts) |
| Unity batch compile (6000.6.7f1) | **0 errors** |
| EditMode suite | **24 / 24 PASS** |
| PlayMode suite | **9 / 9 PASS** (bootstrap, practice start, jump clearance, navigation/back stack, restart, garage cleanup, results return, purchase guards) |
| macOS player build this pass | **Succeeded** — `Builds/macOS/NeonVelocity.app` rebuilt 25 Oct 2026 (115.7 MiB, 56.7 s, universal x86_64 + arm64) including every screen and system from this pass |

Regressions found & fixed during QA:
1. `MaterialPropertyBlock` constructed in a field initializer → **boot-blocking** UnityException + follow-on NRE. Fixed with lazy buffer (Fx accessor).
2. RunnerVisuals compile errors from the previous session (cable mesh argument order, jet variance redeclaration) — fixed.
3. Panini `cropSizeMode` does not exist in URP 17.6 — removed, distance-only.
4. MainMenuScreen `parsed` definite-assignment + AddMenuButton arity — fixed.

## 9. What still needs external assets (honest gap list)

Nothing visual in this pass is a placeholder, but to push beyond "premium stylised-procedural" into modern AAA fidelity these would need authored external assets or services — none are currently implemented, and nothing above pretends otherwise:

1. **Fonts** — a display font (e.g. a digital/techno face) would replace `LegacyRuntime.ttf`; today the brand reads through layout/colour/case rather than typography.
2. **Audio for new beats** — revive sting, purchase "clink", menu roll-up sounds are synthesised placeholders from the existing pool; dedicated SFX design would sell the moments harder.
3. **VFX textures** — decals for tire marks, metal sparks, volumetric window bloom sheets; currently geometric primitives.
4. **Skeletal animation/operator motion** — the run is procedural; a mocap library (start/land/revive cycles) would raise believability, especially for the Revive kneel-rise.
5. **Environment textures** — real photographic-based normal sets for asphalt/facade variety at close-ups.
6. **Store/monetisation** — real money gem packs, receipt validation, platform store SDKs (deliberately not faked; the in-game shop spends "earned" gems only).
7. **Display/graphics QA** — the macOS bundle has been rebuilt with this pass (115.7 MiB universal binary, 25 Oct 2026); physical-device validation (4K performance, touch/controller safe areas on hardware) still remains from the verification matrix.

## 10. Files touched in this pass (summary)

- `Assets/Game/Scripts/Player/RunnerVisuals.cs` — character overhaul (previous step) + boot fix.
- `Assets/Game/Scripts/Player/RunnerController.cs` — NotifyDoubleJump wiring, GrantHeadStart (shield 3.6 s).
- `Assets/Game/Scripts/Player/RunnerCamera.cs` — WhiteBalance / LensDistortion / PaniniProjection + settings hooks.
- `Assets/Game/Scripts/World/MenuStage.cs`, `Assets/Game/Scripts/World/TrackManager.cs` — city/menustage rebuild (previous step).
- `Assets/Game/Scripts/Utilities/TextureFactory.cs` — PanelBorder sprite.
- `Assets/Game/Scripts/UI/UITheme.cs` — GlassCard, Border alias.
- `Assets/Game/Scripts/UI/UiFx.cs` — new FX kit (+ .meta).
- `Assets/Game/Scripts/UI/UITheme.cs`, `ScreenKit.cs` — GEMS rebrand, MenuButton FX/underline.
- `Assets/Game/Scripts/UI/Screens/MainMenuScreen.cs` — full redesign.
- `Assets/Game/Scripts/UI/Screens/CharacterScreen.cs`, `UpgradeScreen.cs`, `GemShopScreen.cs`, `PurchaseKit.cs` — new screens (+ .meta each).
- `Assets/Game/Scripts/UI/UIRoot.cs` — ShowLoading/HideLoading + LoadingProgress.
- `Assets/Game/Scripts/UI/HUDScreen.cs` — full HUD rebuild (public API intact) + ShowLocation.
- `Assets/Game/Scripts/UI/Screens/ResultsScreen.cs` — revive panel + LEVEL UP badge + GEMS.
- `Assets/Game/Scripts/UI/Screens/PauseScreen.cs` — glass restyle + GEMS label.
- `Assets/Game/Scripts/UI/Screens/ModeSelectScreen.cs`, `LeaderboardScreen.cs`, `ShopScreen.cs` — GEMS rebrand sweeps.
- `Assets/Game/Scripts/GameApp.cs` — enum/Show/BuildScreen cases, ReviveRun, ReviveCost, StartSession revive + loading card + lastModeId.
- `Assets/Game/Scripts/GameSession.cs` — RunResult revive/level fields, DelayedResults logic, biome Hud callout, Begin head-start param, AttachHud opening callout.
- `Assets/Game/Scripts/Core/SaveData.cs` — `lastModeId`.
- `Assets/Game/Scripts/Progression/ProgressionService.cs` — SpendCredits; GEMS messages.
