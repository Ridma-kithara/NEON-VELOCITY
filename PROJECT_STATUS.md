# NEON VELOCITY — Project Status

**Studio:** RK VANTAGE GAMES  
**Last updated:** 30 October 2026  
**Engine:** Unity 6000.6.7f1 / URP 17.6 / Input System 1.20  
**Verified build:** `Builds/macOS/NeonVelocity.app` — rebuilt 29 Oct 2026 from Phase 03 source after the final shop label fix, universal x86_64 + ARM64, ad-hoc signed, **122.5 MiB, 27.3 s build (39.0 s wall)**  
**iOS export:** `Builds/iOS/Unity-iPhone.xcodeproj` — generated 30 Oct 2026 with the iOS module and Unity IAP 5.4.4; unsigned Release/iphoneos Xcode build compiled, not signed or run on a device  
**Repository:** `Ridma-kithara/NEON-VELOCITY` (`main`)

## 1. Current state

The game is playable offline, and the offline menu, garage, settings, shop, account, events, atlas, achievements, character and GEM flows are implemented. Phase 03 adds real account/auth plumbing, mandatory offline play with conflict-safe sync, a server-authoritative COIN/GEM economy, Apple/Google/Stripe payment integration boundaries, receipts, refunds, admin catalogue tools and exact-position revive. No live payment or OAuth provider transaction has been completed because merchant credentials, native Google/Apple sign-in plugins and hosting were not supplied. It remains an original stylised procedural game, not a photoreal AAA production.

| Area | State |
|---|---|
| Core gameplay | Implemented; deterministic three-lane runner, junctions, obstacles, pickups, power-ups, modes and progression |
| Offline play | Implemented and exercised through Practice/PlayMode; full manual all-mode playthrough still pending |
| Multiplayer authority | Implemented; Python loopback suite passes; real multi-client Unity race not run |
| Main menu / branding | Implemented, polished and captured at three resolutions |
| Settings | Implemented with master/music/effects/ambience volume, display resolution/mode, presets, render scale, textures, shadows, reflections, AA, effects, view distance, VSync, input and accessibility |
| Garage / customization | Implemented with a dedicated RenderTexture turntable, pose preview, drag rotation, owned/equipped states and purchase confirmation |
| Shop / currency | Two wallets: earned CREDITS for local tech/cosmetics, server-authoritative GEMS for premium runners and upgrades; every debit is confirmed |
| Navigation / Back | Centralized history and contextual Back; tested for Shop/Modes/Settings-from-Pause/Results/GEM Shop/account/events/atlas/achievements/modal routes |
| HUD / pause | Glass telemetry cards, rolling tickers, velocity gauge, district callouts, junction panel, captioned `RUN TIME` / `TIME LEFT` readout and premium ability state; pause hides the HUD and toasts |
| Progression | Infinite total-XP curve (exact L1–L10 thresholds, 1.12× growth after L10), milestone rewards, idempotent grants, derived level and v5 save schema with v4 legacy-XP migration |
| Locations | Twenty districts on exact 3 km zones — the twenty requested themes — with unique palettes, sky, weather, road keys, landmark signatures and hazard bias; 17 purpose-built architecture families |
| Time of day | Seven authored states (Early Morning through Late Night) with smooth sun/moon, sky, ambient/directional light, shadows, fog, streetlights, neon, stars, reflections and weather blending; authored, dynamic and fixed modes persist |
| Junctions | Nine connected kinds (split, crossroad, narrow bridge, broken-road jump, tunnel branch, T-junction, curved road, shortcut, collapsing bridge), deterministic scheduling, physically curved decks, 126 m warning, branch commitment, merge, connected collision and reward routes |
| Revive system | Exact-position server-authorised revive: captures distance/lane/pose, previews rewards, finalises once, debits once and resumes the same run with a stabilisation window |
| Premium runners | PHANTOM X, NOVA TITAN, ECHO, SABLE and HELIOS — balanced sidegrades with server ownership, distinct silhouettes, FX and utility abilities |
| Modes | Fifteen: Classic, Neon Circuit, Private Circuit, Practice, Time Trial, Daily, Weekly, Coin Rush, Survival, Monsoon Overdrive, First Contact, Mission, Distance, Boss Chase and Route Exploration |
| Accounts | Email/password, phone OTP, guest linking, profile, receipt email, verification/recovery, persistent sessions and monotonic cloud sync; Google and Apple identity tokens are server-verified when configured. Native Google/Apple sign-in plugins are absent, so the client explains the boundary rather than faking a session. |
| Payments | Server-catalogue GEM packages; Unity IAP Apple/Google store sheets on mobile and Stripe-hosted card checkout on desktop/web; server verification, signed webhooks, duplicate prevention, receipts and refunds. Live providers remain unconfigured. Manual bank transfer is legacy and disabled by default. |
| Loading card | Cinematic run-loading overlay with mode title/objective and progress bar |
| Visual upgrade | Beveled layered runner armour, generated materials, recessed storefront city, layered facades, street trees/furniture, wet practical light pools, textured rain/mist, abstract signage, subway station and post-processing |
| PC 4K support | Actual 3840 × 2160 render/capture verified on the host; physical 4K display and 4K performance not verified |
| Mobile quality profile | Implemented with independent mobile settings path; no device build/validation |
| Builds | macOS universal verified; iOS Xcode project generated and unsigned device build compiled (not signed/device-tested); Windows/Linux/WebGL/Android not built here |

## 2. Final verification results

| Check | Result |
|---|---|
| Unity EditMode | **43 / 43 passed** |
| Unity PlayMode | **18 / 18 passed** (including the real 30 km TrackManager stream test) |
| Headless core | **66 / 66 passed** |
| Python backend | **79 / 79 passed** (9 race authority + premium/account HTTP + admin/catalogue/payment + 3 operator CLI) |
| Static integrity | **936 / 936 passed** |
| Compilation | **0 errors** in the tested assemblies |
| macOS build | **Succeeded**, universal x86_64 + arm64, ad-hoc signed, **122.5 MiB, 27.3 s build / 39.0 s wall** — rebuilt 29 Oct 2026 after the final shop label fix |
| iOS Xcode export | **Succeeded** at `Builds/iOS` (**1133.8 MiB**; **16.7 s** warm export, **46.0 s** first stripped export) with IL2CPP, bundle id `games.rkvantage.neonvelocity`, film-grain resources and the Unity IAP StoreKit plugin. An unsigned `Release` / `iphoneos` Xcode device build compiles the arm64 app; signing and device install/run are not performed here. |
| Captures | Fresh **18-screen Phase 03 set at 1280 × 800** (`Captures/phase03/1280x800`), including the rebuilt GEM shop after the label fix; the 1600 × 900 and 3840 × 2160 sets remain the Phase 02 captures |
| Runtime capture log | Final Phase 03 capture completed without runtime exceptions, shader-stripping warnings or missing-resource errors |

The PlayMode suite includes real bootstrap, Practice start, quick-tap jump clearance, Back-stack routing, title/quit modal priority, restart into a fresh run, garage preview cleanup, Results root return, Settings-from-pause return, purchase-confirmation safety, premium-screen and GEM SHOP tab construction, Account/Events/World Atlas/Achievements construction and return, PHANTOM X ability activation/cooldown, exact-distance revive without a world reset, premium-endpoint cache clearing, main-menu destination-label visibility, independent HUD score/distance tickers, and HUD/toast suppression and restoration across pause. The settings screen exposes the premium store URL with validation so a production build can select the deployed HTTPS authority.

## 3. Implemented in this pass

### Junctions, time of day and modes
- Deterministic junction scheduler every 15 segments, first at `10 + abs(seed % 15)`; 126 m warning gantry, commit at row 1, median/branch rows 2–4 and merge at row 5.
- Nine junction shapes: split, crossroad, narrow bridge, broken road, tunnel branch, T-junction, curved road, shortcut and collapsing bridge. Curved shapes displace real lane/deck geometry and collision; T branch decks reach the central median; shortcuts have a dedicated curved reward route.
- Connected divider/edge collision, forced branch commitment, lane lock, centre-lane auto-commit to a safe branch, lean/camera roll, chevrons, reward beacons and a HUD junction panel.
- Full-width `RoadGap` for broken-road/collapsing-bridge jumps; ordinary full-width patterns suppressed in neighbouring segments to preserve 30 m fairness spacing.
- Both branches remain physically safe; the marked reward branch carries authored credits/prism pickups. Signature hazards are limited to full-width-empty rows with empty neighbours.
- Seven time-of-day states with smooth sun/moon colour/elevation, sky gradients, ambient/directional light, shadow strength, fog, neon, streetlights, stars, clouds, reflections and weather intensity. Authored-per-location, dynamic 21 km cycle and fixed-phase modes are persisted and editable in Settings.
- Fifteen modes with appended enum IDs and centralised `ModeSpec.TargetDistance` completion. Boss Chase uses a visible non-colliding Warden mech ahead of the runner.
- Offline deterministic **EVENT CALENDAR** built from the existing Daily/Weekly/Seasonal/Boss/Route modes; it does not claim a live server feed.

### Character, audio and world polish
- Cached 26-facet chamfered armour geometry, articulated palms/fingers and layered boots in `PrimitiveMeshes.cs` / `RunnerVisuals.cs`.
- Generated armour albedo, normal and height textures in `TextureFactory.cs`; transparent contact shadow and emissive trim.
- Landing compression, lane banking, jump/slide/fall/victory poses and optional vertical-velocity/ground-height animation parameters.
- Footsteps with wet/metal variants, lane whoosh, thunder and train pass-by clips; pooled spatial one-shots in `AudioManager.cs`.
- Wet-road reflection cards, puddle/curb light pools, storefront mullions/doorways, tiered facades and expanded subway station geometry in `TrackManager.cs`.
- Landing camera dip/FOV punch in `RunnerCamera.cs`, respecting reduced motion.
- Authoritative biome reporting from `TrackDirector` plans so lighting/audio match the generated segment.
- 32 original synthesized audio clips (3 music, 9 ambience, 20 SFX) with separate master, music, effects and ambience controls.

### Navigation and flow
- Central `AppScreen` history managed by `GameApp`.
- `IScreenBackHandler` for stateful screens; modal dismissal takes priority.
- Contextual Back from Settings to Pause when opened in a run, then Back resumes.
- Results returns to Main Menu and clears stale history.
- Title-screen Back/Escape opens the quit confirmation instead of rebuilding the menu.
- Header section/title/subtitle now occupy three stacked rows; in-run toasts start below the timer card and district banner.

### Graphics and presentation
- Scaled PC/mobile settings: Low, Medium, High, Ultra, Custom.
- Resolution, windowed/borderless (and Windows/Linux exclusive fullscreen offset), 30/60/120/144 FPS, render scale, textures, shadows, reflections, AA, effects and view distance.
- True 4K render/capture path validated; UI inspected at 3840 × 2160.
- Original layered runner with beveled armour, articulated hands/feet, chest core, backpack thrusters, helmet and emissive visor; four additional premium silhouettes.
- Player-build rendering fixed: removed the SSAO renderer feature whose stripped resources aborted URP renderer construction, disabled URP unused-variant stripping because every world material is created at runtime, and added `UberPost` to Always Included Shaders.
- Wet road, denser neon facade windows, procedural sign glyphs, maglev sky train, air traffic, rain, mist and post-processing.
- City-first opening biome for all seeds; deterministic multiplayer rotation.

### Infinite progression, districts and server-authoritative GEMS
- `LevelProgression` drives every reward from one authoritative `long totalXp`: exact L1–L10 totals (0, 450, 900, 1,800, 3,000, 4,500, 6,500, 9,000, 12,000, 16,000), 1.12× growth afterwards, a 1,000,000 XP requirement cap and safe Int64 arithmetic.
- `LocationCatalog` defines 20 engine-free districts on exact 3 km zones. `TrackManager` dispatches 17 purpose-built structural families and 20 unique landmark signatures; `EnvironmentController` crossfades per-zone sky, fog, lighting and rain, runs pooled snow, dust, ember and fog weather, and binds the seven time-of-day states. Discovery and time-of-day settings persist in save v5.
- `TrackDirector` is random-access, deterministic, location-indexed and difficulty-tiered. Hazards use simulation-time motion and sampled rotating collision.
- Save schema v5 stores authoritative total XP, 3 km location records, time-of-day mode/phase/cycle, premium cache/ownership, cosmetic loadout, expanded lifetime stats, master volume and server URL, with v4 legacy-XP migration, retired-district removal and defensive normalisation.
- Premium backend (`Backend/premium_server.py`, `premium_store.py`, `payments.py`, `account_store.py`, `identity.py`, SQLite): five GEM tiers, server wallet/ownership/ledger, idempotent purchases/revives, account/cloud-sync endpoints, admin catalogue/account/transaction/refund routes, Stripe hosted checkout plus signed webhooks, Apple receipt verification, Google Play service-account verification, receipts and operator CLI. GEMS are never minted locally or by gameplay.
- Unity `PremiumService` is the only GEM authority client: online wallet, ownership, platform purchase verification, transaction history, purchases, idempotent revive and explicit STORE OFFLINE errors. Cached balances are display-only. The endpoint is editable at **Settings → Network → Premium Store URL**.
- `Assets/Game/Scripts/Premium/PlatformStore.cs` uses Unity IAP 5.4.4 for Apple/Google store sheets. It forwards the raw purchase token/receipt to the backend, which verifies with the provider before crediting. Store purchases are blocked when the server has no provider capability.
- GEM SHOP now has **BUY GEMS / PREMIUM STORE / TRANSACTION HISTORY**. The bank account, exact-amount transfer instructions, receipt upload and order-proof UI were removed. Packages show provider-specific price guidance, the receipt email is selected from the verified account and can be changed, and successful purchases show verified transaction records.
- Exact-position revive keeps the crashed session alive, previews rewards, performs a server-verified atomic debit and resumes the captured distance/lane/pose with invulnerability. Insufficient balance shows **NOT ENOUGH GEMS!** with BUY GEMS / RESTART / CLOSE. Leaving, restarting or quitting finalises the pending run exactly once.
- Account sync merges progression with the server (max scalar progress, unioned reward IDs, conflict-safe revisions) and deliberately excludes GEMS, transactions and premium ownership, which remain server-owned. Offline play never requires a profile.
- The secure operator CLI supports `transactions`, `accounts`, `account`, `catalog`, `set-package`, `set-product` and `refund`; destructive actions require explicit confirmation and credentials are read from the environment or a prompt.
- PHANTOM X (`phase_dash`), NOVA TITAN (`nova_burst`), ECHO, SABLE and HELIOS are server-owned sidegrades with HUD ability state and Q/E or gamepad-shoulder activation.
- CREDITS labelling corrected everywhere outside the premium store; level-up, discovery, purchase, selection, upgrade, revive and ability sounds respect the existing mix settings.

### Tests and tooling
- Headless core: 66 tests, including nine-junction scheduling/geometry/fairness, exact L1–L10 XP thresholds, post-10 growth/cap, level-gain accounting, 3 km zone mapping, 20-district uniqueness/wrap, landmark-signature uniqueness, hazard-row fairness, time-of-day cycling and discovery memory.
- EditMode: 43 tests, including v4 legacy-XP migration, premium-cache normalisation, save round-trip including master volume and time-of-day settings, authored XP/location/junction/ToD contracts and transaction display labels.
- PlayMode: 18 tests, including a real 30 km `TrackManager` stream across 3/6/9/15/30 km checkpoints with bounded pools, district transitions and save round-trip; plus navigation, restart rebuilding a fresh run, purchase-confirmation flows, Phase 03 shop tabs and retired bank-transfer copy removal, offline transaction history, account/events/atlas/achievements, PHANTOM X ability/cooldown, exact-distance revive, premium-endpoint cache reset, every main-menu destination label, independent HUD score/distance tickers, and HUD/toast suppression and restoration across pause.
- Premium/payment/account backend: 79 tests covering race authority, catalogue overrides, wallet idempotency, admin accounts/transactions/refunds, Stripe/Apple/Google verification, receipts, duplicate purchase handling, account lifecycle, cloud sync, operator CLI and malformed/unauthorised input.
- VisualCaptureRunner captures 18 screens with selectable resolution; it re-asserts the requested resolution before each readback.

## 4. Generated / authored assets

All runtime assets remain original and procedural:
- geometry from Unity primitives;
- textures generated by `TextureFactory`;
- UI built at runtime;
- 32 audio clips synthesized by `Tools/generate_audio.py` (music, ambience and SFX; 112.534 s total);
- no copyrighted characters, logos, third-party art packs or proprietary assets.

This is intentionally a code-only/original pipeline. It is not a production DCC asset pipeline, and the result is stylised rather than photoreal.

## 5. Tested vs. unverified

### Verified
- macOS build compiles, launches and completes the capture flow without runtime exceptions.
- The fresh Phase 03 16:10 set renders all 18 screens at 1280 × 800, including the rebuilt GEM shop, five-tier catalogue, corrected wallet account row/disclaimer and full-length button labels; the Phase 02 16:9 and 4K sets remain the only captures at those sizes. Gameplay and transition captures show textured rain, wet roads, raised sidewalks, recessed storefronts, street trees/furniture, signage and the active crossfade.
- Core logic, authority, junction safety, settings clamps/persistence, navigation, purchase-confirmation logic, XP thresholds, 3 km location zoning, time-of-day resolution, a real 30 km stream and save v5 migration pass automated checks.
- Offline Practice launch, jump regression, navigation, premium-screen construction, Phase 03 shop labels and transaction-history state, ability activation and exact-distance revive pass in PlayMode.
- Premium/payment/account tests run the real HTTP service against a temporary SQLite database: catalogue overrides, admin account/transaction review, GEM refund reversal, Stripe signed-webhook credit/refund, Apple receipt adapter, Google Play service-account verifier, receipt construction, duplicate purchase handling, purchase/revive debit, idempotent replays, account lifecycle and operator CLI all pass.
- Player captures reached a real subway segment after fast-forwarding; biome environment and geometry agree. No missing audio clips, runtime exceptions or stripped shaders were reported. Audio was not auditioned.

### Not verified
- Live GEM purchase on any provider: no HTTPS host, Stripe live key, Apple shared secret/product IDs/sandbox account or Google Play service account/licence tester was supplied. No real card, App Store or Play Store transaction has been completed. Checkout opening alone is not treated as success.
- Live SMTP/SMS delivery and live Google/Apple sign-in; the native sign-in plugins and provider credentials do not exist. Server-side token verification is unit-tested with generated keys.
- Physical 4K display output, HDR, cable/mode negotiation and 60 FPS at 4K.
- Live confirmation that an intermittent macOS Metal screenshot-readback artifact (one tiled/duplicated-UI frame, clean on reruns) cannot appear on a physical display.
- LOD, occlusion culling, profiler-driven frame-time validation and dynamic tree/foliage wind; none were implemented in the environment slice.
- Windows, Linux, WebGL and Android builds; signed iOS archive/device install, real Android system Back behaviour and Unity IAP on device.
- Real touch, gamepad focus order, audio balance, suspend/resume and safe areas on devices.
- Two/five real Unity clients, latency/loss, reconnect through the UI, NAT/TLS.
- CPU/GPU/memory/thermal profiling and long-session memory growth.
- Full manual playthrough of all 15 modes, every power-up/obstacle/pickup/result path and every junction branch.
- Audible audio balance, loudness and device output; no listening test was performed.
- macOS Developer ID signing, hardened runtime, notarisation and store packaging.

### Requested but not implemented
- Drone chases, moving platforms, destructible sections and temporary vehicles.
- Explicit Friend Challenge and Limited-Time Event mode entries (Private Circuit represents friend races; Monsoon Overdrive represents the event mode; the EVENT CALENDAR is local and deterministic).
- Full 90° axis-changing T-turn/curve junctions; the implemented junction family is a connected Y-split/crossroad fork with lane lock and merge.
- Cryptographic local save tamper resistance; server caps and pure premium authority limit impact, but offline save edits are not cryptographically blocked.

## 6. Known risks / next work

1. Deploy the premium service behind HTTPS with a real admin credential/secret, provider credentials, backups and monitoring. Configure Stripe live keys, webhook secret, price map and HTTPS return URLs; run a controlled live card purchase, refund and receipt delivery test.
2. Configure Apple/Google merchant accounts, store products and product maps, add native Google/Apple sign-in plugins if provider login is required, then run sandbox store purchases on real iOS/Android hardware. Verify every purchase against the backend and confirm one-time credit.
3. Configure SMTP/SMS providers and verify a sending domain; test verification, reset, OTP and receipt email end to end.
4. Run a full manual QA pass on macOS (menus, Back, saves/settings, restart, collision/death, results, exact revive, shop tabs, transaction history). Select an Apple team in `Builds/iOS/Unity-iPhone.xcodeproj`, run/archive it on a device, then produce and test Windows and Android builds on real hardware.
5. Capture a fresh Phase 03 visual set including the rebuilt GEM SHOP and transaction history; keep the synchronous harness as a fallback.
6. Profile 4K and low-end mobile; measure frame time, memory and thermal behaviour, then add LOD/occlusion or instancing where justified.
7. Add per-theme storefront/facade variants and dynamic foliage wind to reduce repetition and add environmental motion.
8. Test two/five-client Unity races against the Python authority, including disconnect/reconnect.
9. Verify Android system Back, touch safe areas and controller navigation on devices.
10. Add production art/rigging/cosmetics pipeline if photoreal AAA quality is required beyond procedural assets, plus code signing, notarisation, store metadata, privacy policy and release automation.

## 7. Document map

- [QA_REPORT.md](QA_REPORT.md) — defects, root causes, fixes, tests and remaining gaps.
- [Documentation/VISUAL_DIRECTOR_PASS.md](Documentation/VISUAL_DIRECTOR_PASS.md) — scope, changes, QA and external-asset needs for the 25 October visual/UI pass.
- [Documentation/TEST_REPORT.md](Documentation/TEST_REPORT.md) — executed test details and platform status.
- [Documentation/PHASE03_FINAL_REPORT.md](Documentation/PHASE03_FINAL_REPORT.md) — Phase 03 auth, offline, economy, payment, receipt, security and test status.
- [Documentation/DEVELOPMENT_PROGRESS.md](Documentation/DEVELOPMENT_PROGRESS.md) — roadmap, phase status, defects, gaps, performance observations and next tasks.
- [Documentation/KNOWN_LIMITATIONS.md](Documentation/KNOWN_LIMITATIONS.md) — operational, multiplayer, content and release constraints.
- [Backend/PREMIUM_DEPLOYMENT.md](Backend/PREMIUM_DEPLOYMENT.md) — premium API contract, provider setup, receipts, refunds, security, backup, HTTPS and owner deployment checklist.
- [README.md](README.md) — controls, setup, build commands and verification notice.
