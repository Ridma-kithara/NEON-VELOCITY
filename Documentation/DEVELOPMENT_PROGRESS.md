# NEON VELOCITY — Development Progress

**Studio:** RK VANTAGE GAMES  
**Last updated:** 29 October 2026  
**Engine:** Unity 6000.6.7f1 / URP 17.6 / Input System 1.20  
**Repository:** `Ridma-kithara/NEON-VELOCITY`, branch `main`  
**Current scope:** Phase 01–03. Phase 03 delivers real accounts/auth plumbing, offline-first save/sync, a server-authoritative COIN/GEM economy, official store payment integration, receipts, refunds, admin tools and exact-position revive. Provider credentials, native Google/Apple sign-in plugins and production hosting remain owner-supplied; no live provider transaction has been completed.

This is the working production record. It separates implemented work, verified evidence, known defects, asset/tooling gaps and the next tasks. The QA evidence lives in [TEST_REPORT.md](TEST_REPORT.md), [QA_REPORT.md](../QA_REPORT.md) and [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md).

---

## 1. Roadmap and status

| Phase | Scope | Status | Notes |
|---|---|---|---|
| **Phase 1 — Core product** | Original runner, modes, progression, saves, UI, shop/garage, multiplayer authority, settings, accessibility | **Substantially complete** | Existing verified systems preserved; full manual all-mode playthrough still outstanding. |
| **Environment / atmosphere vertical slice** | Real gameplay environment, city geometry, roads/sidewalks, street furniture, foliage, distant skyline, PBR-style materials, weather, rain, wet surfaces, lighting and biome transitions | **Implemented and visually verified** | Procedural/original assets only; no authored DCC assets. |
| **Progression, districts and premium economy** | Infinite XP, 20 districts on 3 km zones, server-authoritative COINS/GEMS, five GEM package tiers, five premium runners/upgrades, exact-position revive | **Implemented and tested locally** | COINS are earned in gameplay; GEMS are real-money only. Live providers, merchant credentials and production HTTPS remain owner actions. |
| **Time of day and environment** | Seven configurable lighting states, smooth sun/moon/sky/fog/shadow/neon/weather blending, authored/dynamic/fixed modes, 20 original 3 km themes with purpose-built architecture and landmarks | **Implemented and tested** | Procedural geometry, not authored DCC assets; some related themes share an architecture family. |
| **Junctions and mode expansion** | Nine connected junction kinds, physically curved branch decks, lane commitment, 15 modes including Boss Chase and Route Exploration | **Implemented and tested** | True 90° cross-street turns are represented by curved lane sweeps, not a fully drivable perpendicular road network. |
| **Accounts, sync and payments** | Email/password, phone OTP, guest linking, profile, cloud sync, receipt email, Stripe checkout, Apple IAP, Google Play Billing, server verification, receipts, refunds and admin review | **Implemented and tested locally** | Native Google/Apple sign-in plugins and all provider credentials/store products remain owner-supplied; no live purchase has been executed. |
| **Presentation polish** | Character, armour, audio, HUD, UI, post-processing, 4K layout | **Substantially complete** | Automated tests and multi-resolution captures pass; live-device/4K performance not measured. |
| **Performance and platform hardening** | Profiling, LOD/occlusion, Windows build, release packaging, signing/notarisation | **In progress / blocked** | No Windows module; no profiler run; no physical 4K display. |
| **Authored AAA content** | Production meshes, textures, rigs, animation, cinematic audio | **Blocked** | Requires DCC assets and an art/audio production pipeline, which this code-only project does not have. |

### Current status in one line

The verified macOS build renders a materially richer, original neon world with recessed storefronts, layered facades, street trees, furniture, signage, wet-road lighting, textured rain and a working 2.6-second biome crossfade. It now has twenty 3 km districts with purpose-built architecture and unique landmark signatures, a seven-state time-of-day system, nine connected junction shapes with real curved decks, infinite total-XP progression, fifteen modes, a server-authoritative premium economy with five GEM tiers, real account/auth plumbing, conflict-safe offline sync, official Apple/Google/Stripe payment boundaries, receipts, refunds, five signature runners and exact-position revive. It remains a stylised procedural game, not a photoreal AAA production, and the premium service is not yet deployed.

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

- `VisualCaptureRunner` now captures 18 screens: menu, settings, premium-network settings, shop/garage, mode select, account sign-in, account recovery, account profile, events, world atlas, achievements, characters, GEM packages, gameplay ×2, mid-biome transition, tunnel and pause.
- Captures use synchronous `CaptureScreenshotAsTexture()` readback and PNG encoding to avoid the earlier asynchronous duplicated-frame path, and re-assert the requested resolution before each readback because macOS can recreate the window surface at the native backbuffer size.
- `sips` confirmed actual 1280×800, 1600×900 and 3840×2160 PNG dimensions for all 18 screens.
- The transition capture is taken 0.65 s into a real biome change while the crossfade is active.
- Player-build rendering is fixed: the `ScreenSpaceAmbientOcclusion` renderer feature was removed because its stripped resources aborted URP renderer construction; URP unused-variant stripping was disabled because every world material is created at runtime; `Hidden/Universal Render Pipeline/UberPost` was added to Always Included Shaders; and the PC renderer was restored to native render pass and Forward+. Player captures now show the lit city, runners, pickups, rain and post-processing instead of UI over a black world.
- HUD tickers now attach to their own text objects (score, distance and credits no longer share one `NumberTicker`), the telemetry card was enlarged to separate score from distance, and the centre panel now captions the timer `RUN TIME` / `TIME LEFT` above the digits with a full-width objective below. Pausing hides the HUD and dismisses in-flight toasts so neither can draw over the pause panel; resuming restores the HUD.
- The shared screen header lays section label, title and subtitle on three stacked rows, so long section names can no longer overlap the subtitle. In-run toasts start below both the timer card and the district banner.

---

## 3. Progression, districts and premium economy — implemented

### Infinite progression

- `LevelProgression` derives everything from one authoritative `long totalXp`: exact L1–L10 totals of 0, 450, 900, 1,800, 3,000, 4,500, 6,500, 9,000, 12,000 and 16,000, then 1.12× growth, a 1,000,000 XP requirement cap and a safe Int64 ceiling.
- Level, XP bars, milestone rewards and v4 save migration all use the same curve. Rewards are idempotent and never double-grant. A self-recursive `MaxTotalXp` implementation found by the new tests was replaced with a cached closed-form value.
- Legacy saves migrate their old `xp` into `totalXp` once; the legacy field is cleared and never written back.

### Twenty districts on 3 km zones

- `LocationCatalog` defines exactly 20 engine-free districts on exact 3,000 m zones — the twenty requested themes with deterministic wrap and no back-to-back repeats. The retired 21st district (Helion Ring) is removed by save v5 migration.
- `TrackManager` dispatches seventeen structural architecture families (city grid, luxury tech, reactor, tunnel, snow, desert, coast, forest, European, volcanic, Japanese, frozen harbor, alpine village, holographic river city, skybridge, spaceport and ruins), plus twenty unique landmark signatures. Related themes (the three coastal districts, the two desert districts, the two European highway districts and the two Japanese districts) share a structural family but have distinct palettes, sky, weather, road keys and signature geometry.
- `EnvironmentController` crossfades sky, fog, lighting and rain per zone and runs pooled snow, dust, ember, fog and time-of-day treatments.
- First discovery, visit count and furthest zone persist in save v5; the HUD banner appears only on the first discovery.

### Time of day

- `TimeOfDayCatalog` authors seven complete states: Early Morning, Morning, Afternoon, Evening, Sunset, Night and Late Night. Each profile carries sun elevation/azimuth/colour, moon colour/intensity, ambient intensity, reflection intensity, shadow strength, fog density/colour, neon and streetlight intensity, star visibility, cloud tint and weather intensity.
- `TimeOfDaySystem` provides smooth RGB/numeric blending between adjacent states, a 21 km default cycle and resolution for three modes: `0` authored-per-location, `1` dynamic cycle and `2` fixed phase. Settings exposes mode, fixed phase and cycle length; the values persist in save v5.
- `EnvironmentController` binds the resolved state to the sun/moon, ambient trilight, directional light and shadows, fog, sky-dome cache keyed by zone × phase, star field, cloud tint, neon/streetlight fills, reflections, rain/weather scale and traffic/train availability. A 30 km dynamic run advances from Evening through the full ring.
- `SettingsScreen` applies changes live and clamps hostile values; `DiscoveryScreen` and the HUD share the same phase labels.

### Connected junctions

- A deterministic scheduler places a junction every 15 segments, first at `10 + abs(seed % 15)`. A 126 m warning gantry precedes the commit; the runner commits at row 1, crosses median/branch rows 2–4 and merges at row 5.
- Nine authored kinds are implemented: split, crossroad, narrow bridge, broken-road jump, tunnel branch, T-junction, curved road, shortcut and collapsing bridge. Curved shapes displace the actual lane/deck geometry with matching slopes; branches are physically connected to the centre road, not a cosmetic fork.
- T-junction branch decks now extend to the central median, removing the visible road hole at the divider. Shortcut and curved roads use dedicated curved deck slices and matching collision offsets.
- Junction rows use connected divider/edge collision and generate a full-width `RoadGap` for broken-road/collapsing-bridge jumps. Ordinary full-width patterns are suppressed in neighbouring segments so the 30 m fairness spacing is preserved.
- Directional input commits the branch, the lane is held until the merge, and the centre-lane runner auto-commits to a safe branch at the commit line, so a median junction is never an unavoidable death. Lean/turn animation, camera roll and a HUD junction panel expose the decision.
- A pure `JunctionRules.RouteOffsetAt` drives geometry, pickups and runner collision from the same route offset. Signature hazards only occupy a full-width-empty row with empty neighbours, so trains, traffic, falling debris and sliding platforms never combine with an unread lane action.

### Modes and events

- `GameModes` now exposes 15 modes: Classic, Neon Circuit, Private Circuit, Practice, Time Trial, Daily, Weekly, Coin Rush, Survival, Monsoon Overdrive, First Contact, Mission, Distance, Boss Chase and Route Exploration. New enum values are appended so existing integer IDs remain stable, and completion is centralised through `ModeSpec.TargetDistance`.
- Boss Chase spawns a visible, non-colliding Warden mech ahead of the runner so the mode tests pursuit without adding a hidden second hitbox.
- The in-game **EVENT CALENDAR** is an offline deterministic local calendar built from the existing Daily, Weekly, Seasonal, Boss and Route modes. It does not claim a live server event feed.

### Server-authoritative GEMS and COINS

- The premium backend is a separate dependency-free Python 3.9+ HTTP + SQLite service (`Backend/premium_server.py`, `premium_store.py`, `payments.py`, `account_store.py`, `identity.py`, `premium_admin.py`) with guest/account tokens, wallet/ownership, configurable catalogue, ledger, transaction history, admin review, receipts and idempotent purchase/revive debits.
- **COINS are the earned soft currency** (`profile.credits`) and remain local gameplay progression merged through cloud sync. **GEMS are real-money only**; gameplay, missions, XP, random rewards and offline play never award them.
- Five GEM tiers are served and operator-configurable: `gems_80` STARTER 80/490 LKR, `gems_200` BASIC 210/1190, `gems_500` PREMIUM 550/2890, `gems_1200` MEGA 1380/6490 and `gems_2500` ULTIMATE 3000/12900. `NV_GEM_PACKAGE_<ID>_{GEMS,BONUS_GEMS,PRICE_LKR,NAME,DESCRIPTION,ENABLED}` overrides are validated with safe fallbacks. Admin edits can change name, description, reference price and enabled state but **cannot change GEM counts**.
- **Normal purchases use official store surfaces.** Unity IAP 5.4.4 opens Apple In-App Purchase on iOS and Google Play Billing on Android. Desktop/web opens Stripe-hosted Checkout in the system browser. No card number, CVV or provider secret enters the Unity client or the local save.
- **Server verification is mandatory.** Stripe credits only from a signed webhook; Apple verifies the receipt against the App Store with sandbox fallback; Google verifies the purchase token through the Android Publisher API with a service account. Idempotency is keyed on the provider event/transaction id, so duplicate webhooks or receipts do not credit twice.
- `GET /api/v1/store/payments` exposes capability flags. The shop refuses to start a charge when the matching provider is unconfigured, so a missing credential can never charge a player.
- GEMS are never awarded by gameplay. CREDITS remain the earned soft currency for local tech/cosmetics; all prior UI labels that incorrectly called credits “GEMS” were corrected.
- `PremiumService` is the only GEM authority client. It shows `STORE OFFLINE` when unreachable and never fabricates purchases, ownership or balance. Cached wallet values are display-only.
- The premium endpoint is stored in save settings and editable at **Settings → Network → Premium Store URL**; changing hosts clears the old server's cached wallet, token and entitlements before the next sync. The local default remains `http://127.0.0.1:8088`.
- The legacy manual bank-transfer workflow is gated by `NV_ENABLE_MANUAL_BANK_TRANSFER=1` (default false) and is no longer displayed in the player shop. It is retained only for historical reconciliation; there is no bank API or automatic settlement.

### Accounts, cloud sync and receipts

- `account_store.py` implements email/password registration, verification, login/logout, password reset, phone OTP, guest-credential linking, profile and receipt-email updates, identity listing, payment/refund history, admin account review and monotonic cloud progression sync.
- Account endpoints share the bearer-token authority with wallets but never fall back to guest auto-authentication, so account state cannot be silently attached to an anonymous wallet.
- **Offline play never requires an account.** Saves persist XP, level, best/total distance, COINS, character/unlocks/items/upgrades, missions, achievements, discoveries and settings locally. On sign-in, sync merges progression monotonically with `baseRevision` plus an idempotency key, unions reward ids and reports conflicts.
- Cloud sync **deliberately omits GEMS, transactions, premium ownership and provider tokens**; those remain premium-server state. It rejects oversized or revision-ahead snapshots and clamps implausible movement values. The local save is not cryptographically signed, so offline progression is not competitive anti-cheat evidence.
- `identity.py` verifies Google ID tokens against Google tokeninfo with audience/issuer/expiry checks, and verifies Apple identity tokens against Apple JWKS with pure-Python RS256 when the matching audience is configured. **The Unity build has no native Google/Apple sign-in plugin**, so those buttons explain the boundary rather than fabricating a session.
- `mailer.py` provides SMTP and SMS adapters. Receipts are sent only after verified settlement and include game name, player/account id, transaction id and provider reference, package/item, GEMS, amount/currency, provider, date and status, with no card data. With no credentials configured, messages are not sent and the UI states delivery is unavailable.

### Premium runners, upgrades and revive

- PHANTOM X owns `phase_dash` (1.5 s untouchable dash, 20 s cooldown, slight speed bias, no defence bonus). NOVA TITAN owns `nova_burst` (clears nearby hazards, grants a shield, 24 s cooldown, lower base speed, starts with one shield). ECHO, SABLE and HELIOS add sonar pathfinding, night-courier and solar-bulwark utility identities with distinct silhouettes and FX. All five are balanced sidegrades with server-verified ownership and selection.
- Premium upgrades are server-operated and level-capped, with current/next benefits and confirmation before purchase. The catalogue carries 18 products across 10 categories: characters, skins, armour, helmets, effects, movement effects, animations, abilities, revival and upgrades.
- Exact-position revive captures distance, lane, height, vertical velocity, slide state and speed at death, previews the pending result, performs one server-verified atomic GEM debit and resumes the same run with a short invulnerability window. Leaving, restarting or quitting finalises the pending run exactly once.
- When the wallet cannot afford a revive, **NOT ENOUGH GEMS!** offers BUY GEMS / RESTART / CLOSE. BUY GEMS opens the real GEM shop; it never mints a local revive.
- GEM SHOP now has **BUY GEMS / PREMIUM STORE / TRANSACTION HISTORY**. Bank account numbers, exact-amount instructions, proof upload and order-status copy were removed. Packages show provider-specific price guidance, the receipt email defaults to the verified account email and can be changed, and verified purchases appear in transaction history.
- The operator CLI adds `transactions`, `accounts`, `account`, `catalog`, `set-package`, `set-product` and `refund`; refunds require `--confirm` after the provider-side refund has been issued.

## 4. Defects found and fixed in this pass

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
| Long header section labels overlapped the subtitle on Account/Store/Discovery screens | `ScreenKit.Header` now lays section, title and subtitle on three stacked rows | 1600×900 capture and PlayMode screen construction pass |
| In-run toasts drew over the district banner and timer card | Toast top offset raised from 150 to 212 px when the HUD is active | Pause/toast PlayMode test and gameplay capture |
| The centre HUD timer was captioned `OBJECTIVE · DISTANCE`, making elapsed time look like a distance objective | Added a `RUN TIME` / `TIME LEFT` caption above the digits and widened the objective label | HUD capture and PlayMode HUD ticker test |
| Settings had music/effects/ambience but no master volume | Added a persisted `masterVolume` (default 1, clamped 0–1) multiplied into music, ambience, SFX, spatial SFX and pause attenuation; added the MASTER slider | EditMode save round-trip assertion and settings capture |
| Local capture server still served the old four-tier GEM catalogue | Restarted the clone server from the updated backend; `/api/v1/store/catalog` now returns all five tiers | Catalog curl check plus final GEM-shop capture |
| GEM shop still exposed the retired bank-transfer instructions after the payment rewrite | Rebuilt the screen around BUY GEMS / PREMIUM STORE / TRANSACTION HISTORY and removed account/proof/order copy | PlayMode tab/label assertions and retired-copy scan |
| Duplicate platform-purchase response zeroed the cached GEM display | Kept the cached wallet balance when the server reported an already-processed purchase | Backend duplicate-purchase tests |
| Google Play purchase payload omitted the package name needed for verification | Included the Android package name and product id in the verification request | Backend Google verifier tests and compile pass |
| Admin account review queried the wallet from the wrong SQLite connection | Attached the premium database before joining identities and balances | Backend admin account tests |
| Long button labels wrapped and were clipped at 4K | Made button labels single-line with horizontal/vertical overflow in `UITheme.Button` | 1600/1280/4K captures and final PlayMode pass |
| Seven new architecture builders (luxury, cryo harbor, alpine village, Seoul, skybridge, spaceport, ruins) existed but were never reachable | Wired styles 1 and 11–16 into `TrackManager.BuildDecor` | EditMode architecture/location contracts and full script compilation |
| Landmark signatures above 19 silently produced no landmark after the 20-theme remap | Remapped all twenty districts to unique signatures 0–19 and added headless/EditMode uniqueness/range assertions | Headless core 66/66 and EditMode location contract |
| T-junction branch deck stopped short of the central median, leaving a visible hole in the road | Extended the curved deck to the median and kept the painted lane on the route centre | Geometry inspection; EditMode route tests remain green |
| Pooled hazard/pickup diagnostics lagged one segment behind the active window | Moved diagnostic counters to the end of `TrackManager.Tick` after build/trim | 30 km PlayMode stream test |
| PlayMode restart test assumed every profile starts at 0 m, so a level-2 head-start upgrade failed a correct restart | Recomputed the expected upgraded start line from the profile and asserted the run reset below its prior distance | PlayMode restart test |
| Signature hazards could be placed next to a lane-blocking row | Extracted `HazardRules` and restricted event hazards to a full-width-empty row with empty neighbours | Headless core and EditMode hazard fairness tests |

### Automated capture limitation observed

On this macOS Metal host, one automated capture run produced a tiled/duplicated-UI readback frame. Subsequent clean runs produced normal gameplay, transition and tunnel frames, and the final 1280, 1600 and 4K sets were inspected. The harness now uses the synchronous texture path; the intermittent readback behaviour is documented as a QA-tooling limitation, not claimed as fixed. No matching rendering error appears in the player log, and PlayMode tests do not reproduce a UI duplication. Live 4K/monitor capture remains unverified.

---

## 5. Verification evidence

### Automated checks (29 October 2026)

| Check | Result |
|---|---|
| Static project validation | **936 / 936 checks passed** |
| Unity EditMode | **43 / 43 passed** |
| Unity PlayMode | **18 / 18 passed**, including a real 30 km streaming run |
| Headless core | **66 / 66 passed** |
| Python tests | **79 / 79 passed** (race authority + account/auth + premium/payment + operator CLI + package configuration) |
| Build compilation | **Succeeded**; no script compilation errors |
| macOS build | `Builds/macOS/NeonVelocity.app`, universal x86_64 + ARM64, ad-hoc signed, **122.5 MiB, 27.3 s build / 39.0 s wall**, rebuilt from Phase 03 source after the final label fix |
| iOS Xcode export | `Builds/iOS/Unity-iPhone.xcodeproj`, IL2CPP, Unity IAP 5.4.4 StoreKit plugin, film-grain resources; **1133.8 MiB, 16.7 s warm / 46.0 s first stripped build**; unsigned `Release`/`iphoneos` compile succeeds (140 MB arm64 app, 0 errors); `xcodebuild -list` passes |
| Runtime capture log | Final Phase 03 capture completed with no runtime exceptions or warnings |

PlayMode coverage: real bootstrap, title stage, Practice start, clean shutdown, quick-tap jump clearance, centralized navigation/Back routes, Settings-from-Pause, Results return, restart rebuilding a fresh run, garage cleanup, purchase-confirmation safety, Phase 03 shop tabs and retired bank-transfer removal, offline transaction-history state, premium-screen navigation, Account/Events/World Atlas/Achievements construction and return, PHANTOM X ability/cooldown, exact-distance revive, premium-endpoint cache reset, every main-menu destination label, independent HUD score/distance tickers, HUD/toast suppression and restoration across pause, and a real 30 km `TrackManager` stream across 3/6/9/15/30 km checkpoints with bounded pools and district/junction/save assertions.

### Visual captures

- **1600×900:** 18 Phase 02 screens, including the premium-network settings, account tabs, events, world atlas, achievements, characters, five GEM tiers, mid-transition and tunnel.
- **1280×800 (16:10):** 18 screens; HUD and menu remain anchored and legible.
- **3840×2160:** 18 screens; all PNGs confirmed genuinely 3840×2160. The host display is 1600×900, so this is an actual large backbuffer capture, not a physical 4K-monitor test.
- Rain, wet road, recessed storefronts, layered facades, trees, street furniture, billboards, lamp pools, tunnel geometry and the active biome crossfade were inspected.
- The 1600×900 and 3840×2160 sets predate the Phase 03 shop rewrite. A fresh **18-screen 1280×800 Phase 03 set** was captured from the final post-fix build (`Captures/phase03/1280x800`), including the rebuilt BUY GEMS / PREMIUM STORE / TRANSACTION HISTORY shop; the other tabs and receipt-success states remain code/PlayMode-only.

### Not verified

- Physical 4K/HDR display, cable negotiation and 4K frame rate.
- Windows/Linux/Android builds, a signed iOS archive/device install and real-device input/audio.
- CPU/GPU/memory/thermal profiling and long-session memory growth.
- Full manual playthrough of all 15 modes; every obstacle, power-up, junction branch, collision, restart and persistence path.
- Real two/five-client Unity races and reconnect through the UI.
- Audible audio balancing and loudness.
- Live manual confirmation that the intermittent screenshot-readback artifact never appears on a physical display.
- **Any live end-to-end provider purchase.** No real Stripe, Apple or Google transaction has been completed; checkout opening is not success. The backend paths are tested with stubs and generated keys.
- Production deployment, HTTPS/admin configuration and live settlement risk review.
- Real iOS/Android Unity IAP store sheets; no store products, merchant accounts or test devices were supplied.
- Drone chases, rideable moving platforms, multi-stage destructible structures, temporary vehicles, explicit Friend Challenge/Limited-Time Event mode entries and a fully drivable perpendicular 90° cross-street network. These are absent, not merely untested. Moving trains, traffic, falling debris, sliding hazard platforms and collapsing-bridge gaps are implemented.
- Live Apple/Google **sign-in** and SMTP/SMS delivery: server-side verifiers/adapters exist, but the Unity native sign-in plugins and provider credentials do not.

---

## 6. Performance observations

- The world remains segmented and pooled: segments, hazards and pickups are reused rather than instantiated without bound.
- Static decor is merged by material. The glow-card split adds one extra renderer per active segment, but avoids opaque shadow artifacts from additive cards.
- Rain emission scales with effects quality (220/400/650 particles per second; the particle cap scales up to roughly 2,100, or 300 in reduced-motion mode), with splashes capped separately.
- Facade and shop textures use generated mipmaps and world-scaled UVs to reduce shimmer.
- No LOD, occlusion culling, per-tree wind animation or GPU instancing pass was added in this slice.
- No profiler numbers are claimed. The universal build and 18-screen 4K capture run prove output and completion, not a frame-time target.
- A real 4K/GPU profile and low-end device profile are the next performance tasks.

---

## 7. Asset and tooling gaps

- No authored environment meshes, PBR texture libraries, character rigs, animation clips or DCC source. The result is intentionally a strong stylised procedural interpretation, not photoreal AAA.
- No downloaded music, voice, licensed art packs or third-party characters/logos. All runtime art and audio remain original/procedural or synthesized.
- Cinemachine, Shader Graph and VFX Graph are not installed; camera and effects are hand-coded.
- Windows build support is not installed, so the Windows target could not be built or run.
- No physical 4K/HDR display, capture card or real-device test hardware.
- Dynamic tree/foliage wind is not implemented; tree geometry is merged into static segment meshes for draw-call efficiency.

---

## 8. Next tasks

1. Run a full manual macOS QA pass: all menus, Back routes, save/settings persistence, restart, collision/death, results, exact revive and the new shop/transaction-history screens with a local service.
2. Deploy the premium service behind HTTPS, then run one real sandbox purchase per provider (Stripe test mode, Apple sandbox tester, Google Play licence tester), including duplicate replay, receipt delivery, refund reversal and transaction history.
3. Capture a fresh Phase 03 visual set including BUY GEMS / PREMIUM STORE / TRANSACTION HISTORY, to close the intermittent automated screenshot-readback limitation.
4. Profile CPU/GPU/frame time and memory at 1600×900 and 4K; add LOD/occlusion or instancing where measurements justify it.
5. Add per-theme shop-interior variants and additional facade/storefront variation to reduce visible repetition.
6. Implement foliage wind and small environmental animation through a custom shader or a supported graph tool.
7. Add stronger wet-surface/reflection work, SSAO/GI alternatives, controlled lightning and more weather-state transitions.
8. Build and test Windows on hardware; then validate Android/iOS IAP and sign-in on real devices where modules and credentials are available.
9. Exercise real two/five-client multiplayer, reconnect and network-failure UI paths.
10. Keep the progress, test, QA and limitations documents updated with every verified pass; never promote an unexecuted check to "passed".

---

## 9. Command reference

```bash
# Static validation (936 checks)
python3 Tools/validate_project.py

# Headless gameplay core (66 checks)
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet" \
  run --project Tools/CoreTests/CoreTests.csproj -c Release

# Backend tests (79: race authority + account/auth + premium/payment + operator CLI)
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
