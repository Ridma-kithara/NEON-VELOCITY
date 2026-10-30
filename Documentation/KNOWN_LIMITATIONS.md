# NEON VELOCITY — Known Limitations

This document records what is genuinely complete, what is constrained by the current environment, and what would need further work before a commercial platform release. It should be read together with [TEST_REPORT.md](TEST_REPORT.md).

## 1. Verification status

### Verified in this environment (29 October 2026)

- Unity **6000.6.7f1** imported and compiled the project with **0 errors**.
- **66 / 66** headless gameplay-core tests, **79 / 79** Python tests (race authority, accounts/auth, premium wallet and catalogue, admin/operator CLI, Stripe/Apple/Google verification, receipts and package configuration), and **936 / 936** static checks passed.
- **43 / 43** Unity EditMode tests passed, including save migration, checkout/catalogue contracts and transaction display labels.
- **18 / 18** Unity PlayMode tests passed, including a real 30 km `TrackManager` stream across 3/6/9/15/30 km checkpoints with bounded pools, district/junction assertions and a save round-trip; plus real bootstrap, title stage, Practice start, clean shutdown, quick-tap jump regression, centralized navigation/Back routes, Settings-from-Pause, Results return, restart into a fresh run, garage cleanup, purchase-confirmation safety, Phase 03 shop tabs and removal of retired bank-transfer copy, offline transaction-history state, Account/Events/World Atlas/Achievements construction, PHANTOM X ability/cooldown, exact-distance revive without a world reset, premium-endpoint cache reset, main-menu destination-label visibility, independent HUD score/distance tickers, and HUD/toast suppression and restoration across pause.
- Progression, nine-junction safety, 20-district 3 km zoning/discovery, seven-state time-of-day resolution, signature-hazard fairness, 15 modes and save-v5 migration are covered across the headless core and EditMode suites; PlayMode covers the runtime UI, ability, revive and 30 km stream routes. The premium/payment/account service was exercised end-to-end at the HTTP/SQLite layer, including catalogue overrides, admin account/transaction review, GEM refund reversal, Stripe signed-webhook credit/refund, Apple receipt verification, Google Play service-account verification, receipt construction, duplicate purchase handling, account lifecycle, conflict-safe cloud sync and idempotent purchase/revive debit.
- Developer-run captures of **18 screens** at 1600×900, 1280×800 and 3840×2160 confirmed the generated Phase 02 menu, HUD, settings, garage, modes, account tabs, events, world atlas, achievements, character roster, five-tier GEM catalogue, corrected wallet account row/disclaimer, full-length button labels, beveled runner, rain-slicked road, raised sidewalks, recessed storefronts, layered facades, street trees/furniture, warm practical pools, layered skyline, textured rain, an active biome crossfade and a seeded subway-station segment render in the player. The 1600×900 and 3840×2160 sets predate the Phase 03 shop rewrite; a fresh **18-screen 1280×800** Phase 03 set captured from the final post-fix build includes the rebuilt GEM shop (`Captures/phase03/1280x800`). The other shop tabs and receipt-success states remain code/PlayMode-only. The 4K PNGs are actual 3840×2160 output; no physical 4K display was available. This is a smoke check, not a full manual graphics review.
- A universal **x86_64 + ARM64** macOS player (`Builds/macOS/NeonVelocity.app`) is built from the Phase 03 source with **Unity IAP 5.4.4** and launched. The app is **ad-hoc signed** only; there is no Developer ID, hardened runtime or notarisation.
- An **iOS Xcode project** (`Builds/iOS/Unity-iPhone.xcodeproj`) is generated from the same source with IL2CPP and Unity IAP 5.4.4. `xcodebuild -list` parses its targets/schemes, the export includes `StoreKit.framework`, the Unity StoreKit plugin and film-grain runtime resources, and an unsigned `Release` / `iphoneos` compile produced a 140 MB arm64 app without errors. It is **unsigned**; no Apple team, provisioning profile, archive, device install or device run exists yet.

### Still unverified

- A full manual playthrough of all 15 modes, every power-up, collision, junction branch, pickup, result and persistence path.
- Two- and five-client races with real Unity clients; reconnect/disconnect through the game UI.
- Windows, Linux, WebGL and Android player builds; a signed iOS archive/device install.
- Real-device touch, gamepad, audio, suspend/resume and performance.
- Frame time, memory, battery and thermal profiling.
- Audible audio balance, loudness and device output for the 32 synthesized clips; only missing-resource checks were performed.
- Live physical-display confirmation that an intermittent macOS Metal screenshot-readback artifact (one tiled/duplicated-UI frame, clean on reruns) cannot appear outside the capture harness. Root cause not isolated.
- Public-internet deployment, TLS, signing, notarisation and store packaging.
- **Any live end-to-end provider transaction.** No Stripe, Apple App Store or Google Play purchase has been completed. Checkout opening alone is not treated as success; only a server-verified webhook or store receipt credits GEMS.
- A fresh visual capture of the **PREMIUM STORE**, **TRANSACTION HISTORY** and receipt-success states; the default **BUY GEMS** package screen is captured at 1280×800, and all three tabs are constructed and asserted by PlayMode.
- Unity HTTP purchase/revive/receipt flows against a deployed premium service on real hardware. The client protocol compiles, and offline, insufficient-funds and server-error paths are tested separately.
- Live Apple/Google **sign-in**. The server verifies Google ID tokens and Apple identity tokens, but the Unity build contains no native Google/Apple sign-in plugin, so those buttons explain the boundary instead of fabricating a session.
- Unity IAP purchases on real iOS/Android store sandboxes. The adapter is integrated and compiles, but no store product, merchant account or test device was supplied.
- Live SMTP receipt/verification delivery and live SMS OTP delivery; adapters exist but no sending domain or gateway was supplied.

### Requested mechanics not implemented

- Drone chases, moving platforms, destructible sections and temporary vehicles.
- Explicit Friend Challenge and Limited-Time Event mode entries. Private Circuit represents friend races, Monsoon Overdrive represents the seasonal event mode, and the in-game event calendar is a local deterministic schedule rather than a live server feed.
- Full 90° axis-changing T-turn/curve junctions. The implemented family is a connected Y-split/crossroad fork with lane lock, reward routing and merge.
- Cryptographic local save tamper resistance. Server caps and pure premium authority limit the impact, but offline save edits are not cryptographically blocked.

### Platform build status

| Target | Offline | Online (native TCP) | Build status |
|---|---|---|---|
| macOS | Implemented | Implemented | **Built and launched** (universal, ad-hoc signed, not notarised) |
| Windows 64-bit | Implemented | Implemented | Not built (module/tooling not verified) |
| Linux 64-bit | Implemented | Implemented | Not built (module/tooling not verified) |
| WebGL | Implemented | **Not supported** | Not built |
| Android | Implemented | Implemented in code | Not built (Android module/SDK absent) |
| iOS | Implemented | Implemented in code | **Xcode project generated**; unsigned, no device/archive run |

Do not claim a platform is supported until its build has been made, launched and tested on real hardware. macOS is the only target with a confirmed launch here, and its broader gameplay/manual pass is still outstanding.

## 2. Multiplayer constraints

### WebGL cannot play online

Browsers do not expose raw TCP sockets. `RaceClient` explicitly blocks online connections in non-editor WebGL builds and shows an error. WebGL builds are **offline-only**. Supporting WebGL multiplayer would require a WebSocket gateway and a server/protocol change; this is not implemented.

### Race transport and identity are separate from accounts

- `nv1` runs over **plain TCP with line-delimited JSON**. There is no TLS, encryption or message signing.
- The race authority uses an ephemeral server-issued player ID + bearer token held in memory for the client session. Persistent email/password, phone and provider-linked identities live in the separate premium/account service; they do not authenticate the race socket.
- Anyone who can reach the race server and obtains a token can act for that session. Use a trusted LAN/VPN or add transport security and authentication before exposing the service publicly.
- The stock client cannot speak TLS, so a TLS terminator alone is insufficient without a client change.

### Session and room lifetime

- Race rooms and tokens are **in-memory**. A server restart ends all active races and invalidates reconnection tokens.
- Only the leaderboard is persisted (`<data>/leaderboard.json`, atomic replace, best-per-name+mode, capped storage).
- Reconnection works for a transient socket drop **within the same running client**: the client retries up to three times with backoff and the server holds the slot for 45 seconds. Closing/restarting the game loses the token and requires rejoining.
- If a disconnected player does not return, results use the last validated snapshot. A player disconnected at finalisation is not eligible for competitive rewards.
- Results packets are retained for 120 seconds; a late reconnect after room expiry cannot retrieve them.
- Rooms are capped at five total runners. Quick match chooses an open public lobby and fills remaining slots with bots only when the host starts.
- Host departure reassigns the host to another connected human, but this path is covered only by server logic, not by a Unity client UI test.
- There is no matchmaking rating, region selection, party system, spectator mode, voice or text chat.
- The server is a single asyncio process with no clustering, horizontal scaling, cross-process room routing or Redis/database persistence.

### Bots are pacing agents

Bots fill empty slots to five and are simulated by the server at 17.0–23.5 m/s with a small wobble. They have final distances, scores and placements, but they do not run the client track simulation, avoid obstacles, jump, slide or crash. Remote runners (human or bot) are visual interpolations of server state; there is no player-to-player collision physics.

### Anti-cheat is bounded, not replay-proof

- Snapshots are limited by a 45 m/s envelope + 35 m slack, 8 m rewind tolerance, a hard `45 m/s × elapsed + 80 m` cap, score/coin caps relative to distance, lane clamps and disqualification after eight violations.
- Finish claims before the server clock reaches the circuit end are rejected with a 0.5 s grace; early finish abuse cannot end the race.
- A missing finish packet is finalised from the last validated snapshots.
- Competitive rewards and the online leaderboard are server-computed, and the client cannot submit reward amounts. Client reward application is idempotent per `raceId`.
- This is not a full deterministic simulation/replay verification system. A modified client could still cheat within the accepted envelope, pause locally, or fabricate local statistics used for **local-only** missions. Server rewards and the online leaderboard remain bounded by the server's snapshots, but a determined attacker is not cryptographically prevented.

### Pausing during an online race

`Esc` opens the pause menu in a race. For an online race, the local simulation stops while the server clock and other runners continue. This gives no additional distance and can only hurt the pausing player's race time, but it is not a server-enforced pause. There is no competitive pause/vote mechanism.

## 3. Premium economy, payments and accounts

- **GEMS are premium-only and server-authoritative.** Gameplay, missions, XP, random rewards and offline play never award GEMS. The local save's cached GEM balance is display/offline convenience only. Purchases, ownership and revive debits are accepted only from the premium service.
- Five tiers are served: STARTER 80, BASIC 210, PREMIUM 550, MEGA 1380 and ULTIMATE 3000 GEMS, with server-side reference prices and validated operator overrides. GEM counts are immutable through the admin route. Client-supplied prices or amounts are never trusted.
- **Normal purchases use official store surfaces.** iOS uses Apple In-App Purchase through Unity IAP, Android uses Google Play Billing through Unity IAP, and desktop/web uses Stripe-hosted Checkout in the system browser. Card numbers, CVV and provider secrets never enter Unity or the local save.
- **Server verification is mandatory.** Stripe credits only from a signed webhook; Apple verifies the receipt with the shared secret and sandbox fallback; Google verifies the purchase token through the Android Publisher API with a service account. All providers key idempotency on the provider transaction id, so duplicate webhooks/receipts do not credit twice.
- **No provider is configured or live in this environment.** The service is tested with stubbed transports/unit keys, not a real merchant account. The shop reads `/api/v1/store/payments` and refuses to start a charge when the matching provider capability is absent.
- **Apple/Google store products must be created with the package ids** (`gems_80`, `gems_200`, ...) unless matching alias maps are configured on both server and client. The client registers package ids with Unity IAP.
- The service has **not been deployed**. It needs an HTTPS reverse proxy, a strong admin credential and secret, off-host encrypted backups and monitoring. Binding the Python process directly to the public internet or enabling insecure HTTP in production is unsupported.
- The local default `http://127.0.0.1:8088` is a development convenience. A shipped build that keeps it will show **STORE OFFLINE** and must not fabricate balances or purchases. The endpoint can be changed in **Settings → Network → Premium Store URL**, and changing hosts clears the previous server's cached wallet and entitlements.
- Admin routes are protected by a server-side credential and constant-time hash comparison, but there is no MFA, multi-admin role system or hardware key in this repository. Put the admin namespace behind a VPN/allow-list and restrict access.
- **Legacy manual bank transfer is disabled by default** (`NV_ENABLE_MANUAL_BANK_TRANSFER=1` opts in). It is retained only for historical order reconciliation and is not exposed in the Phase 03 shop. It has no bank API or automated settlement.
- **Refunds:** Stripe full refunds reverse credited GEMS automatically through the signed webhook. Apple/Google refunds must be issued in the store console and then reversed with `premium_admin.py refund ... --confirm`. Stripe partial refunds are recorded but do not reverse a proportional GEM amount.
- **Receipts** are sent only after verified settlement when SMTP is configured. They contain the game name, player/account id, transaction id and provider reference, item/package, GEMS, amount and currency, provider, date and status; no card data. The destination defaults to the account's receipt/verified email and can be changed; the UI warns when it differs from the verified account email.
- **Accounts** support email/password, verification, reset, phone OTP, guest-credential linking, Google/Apple token verification when configured, profile and receipt-email updates, and monotonic cloud sync. The server-side token verifiers are tested; native Google/Apple **sign-in** plugins are absent from the Unity client, so those provider buttons explain the boundary.
- Cloud sync deliberately omits GEMS, transactions, premium ownership and provider tokens; those remain premium-server state. It uses `baseRevision` plus an idempotency key, merges progression monotonically, rejects oversized/revision-ahead snapshots and returns a conflict summary. The local save is not cryptographically signed, so offline progression is not competitive anti-cheat evidence.
- **Email/SMS delivery requires credentials.** With none configured, verification/reset/OTP/receipt messages are not sent and the UI says delivery is unavailable. Receipts are never emailed before a verified payment.
- **Store policy:** card checkout and direct-to-consumer sales may conflict with platform billing rules when distributed through an app store. Use Apple/Google IAP on their platforms and review policy before release.
- No terms of sale, privacy policy, refund policy, tax treatment, chargeback workflow or age-consent flow has been authored.
- There is no admin web UI, bulk-action tool, CSV export, dispute portal or account-deletion tooling. The operator CLI covers transaction/account review, catalogue edits and refunds.

## 4. Gameplay and content constraints

- **Offline practice/tutorial** intentionally grant no competitive progression. Daily, weekly and seasonal bonuses are once per period and stored locally.
- The local save is editable by the player. It is not authoritative for online rewards, the server leaderboard or the GEM wallet; local credits/XP/cosmetics can be modified with a text editor, but GEMS cannot be minted locally.
- Cloud sync exists through the account service with schema checks, monotonic progression and server-authoritative GEMS, but there is no conflict-resolution screen and no cryptographic local-save integrity.
- The **language setting** is persisted but the UI/content is English-only. No localization tables are shipped.
- Accessibility is limited to reduced motion, high contrast, camera-shake toggle, master/music/effects/ambience volume, sensitivity and UI scale from the canvas. There are no colour-blind palettes, subtitle/caption options, full key remapping or screen-reader support.
- Only one input layout is provided for keyboard/gamepad/touch. Gamepad UI navigation relies on Unity's default UI navigation; focus behavior is not verified.
- There is no tutorial replay option after completion besides selecting First Contact again.
- Character/trail cosmetics are visual only. Permanent upgrades affect utility, while the five premium runners are deliberately balanced sidegrades with distinct cooldown abilities, not raw power upgrades.
- The track has 20 districts on 3 km zones, assembled from 17 structural architecture families plus 20 unique landmark signatures, palettes, road keys and 20 authored patterns. Related Japanese, coastal, desert and European highway themes share a structural family but differ in palette, sky, weather, road dressing and landmark. Variety is procedural but finite; no licensed music, voice acting or story campaign is included.
- Junctions are deterministic and physically safe on both branches. Nine shapes include real curved-lane decks, T branches, shortcuts and a collapsing-bridge gap, but the game is still a three-lane runner rather than a fully drivable perpendicular 90° cross-street network. Moving trains, traffic vehicles, falling debris and sliding platforms exist as hazards; the moving platform slides sideways and is not a rideable platform, and drone chases, multi-stage destructible structures and temporary vehicles remain absent.
- Time dilation is disabled in online races; other power-ups are personal pickups on the shared deterministic track.
- Offline leaderboard entries are local. Online leaderboard identity is name-based; identical names merge, and there is no moderation or name reservation.

## 5. Presentation, audio and performance

- All world geometry, textures, UI and audio are generated from Unity primitives/procedural code and original synthesis. The look/mix is therefore stylized and bounded by that approach.
- No final art/audio polish pass, platform loudness normalization, mixer snapshot automation or dynamic-range testing has been performed.
- No frame-time, memory, battery, thermal or loading-time profiling exists for any target. The procedural runtime world and UI may need optimization on low-end Android/iOS devices. The 4K capture proves output, not a 4K frame-rate target.
- The environment slice has no explicit LOD, occlusion culling or GPU-instancing pass, and tree/foliage animation is static merged geometry. Street-level light pools and rain are deliberately subtle; storefront interiors repeat within a theme.
- WebGL autoplay policies may delay or mute audio until user interaction; this is not verified.
- URP renderer selection and the core runtime shader set have been validated in the built player. General unused-variant stripping remains disabled for the PC/Deferred quality level because its world materials are created at runtime; the iOS build enables it only for the forward-only Mobile level and restores the setting afterward. A full visual/quality pass across all 20 districts and every quality preset has not been performed.
- The runtime generates materials and meshes; repeated rapid restarts could stress memory management. This has not been measured.
- One automated macOS Metal capture run produced a tiled/duplicated-UI readback image. Clean reruns and the final 18-screen capture sets were inspected; the artifact was not reproduced by PlayMode tests or found in the player log. It remains a QA-tooling risk until live compositor capture can be checked.

## 6. Platform/package constraints

- macOS and iOS player support are installed; the macOS universal build succeeded and the iOS Xcode project was generated and compiles unsigned. Windows, Linux and Android modules are not installed and must be added through Unity Hub.
- No Android SDK, NDK, OpenJDK, device or `adb` is available. No keystore is included.
- The iOS Xcode project is generated and compiles unsigned for `iphoneos`, but no Apple team, provisioning profile, signed archive, device install or App Store metadata is configured.
- No macOS notarisation, hardened runtime, entitlements or Developer ID signing identity is configured; the tested build is ad-hoc signed.
- Unity's player settings persist a default host of `127.0.0.1:7777`. Every remote device must change this in Settings; a build shipped with defaults only connects to a server on the same device.
- The race server requires TCP port 7777 (or configured port) to be reachable. No relay, NAT traversal, UPnP, matchmaking service or domain/TLS setup exists.
- Android/iOS network privacy prompts and local-network permissions have not been tested. iOS LAN access may require an `NSLocalNetworkUsageDescription` generated in Xcode.
- The premium service is a separate dependency-free Python HTTP + SQLite process, not part of the asyncio race authority. Production requires an HTTPS reverse proxy, a strong admin secret, backups and monitoring; none is provisioned here.

## 7. Documentation and licensing constraints

- The project is maintained in the GitHub repository `Ridma-kithara/NEON-VELOCITY`, but has **no CI pipeline**, code-signing identity or release channel. Builds and releases must be produced separately.
- URP post-processing variant/resource stripping is disabled in `UniversalRenderPipelineGlobalSettings.asset` because the volume profile (including film grain) is created at runtime; the always-included post-processing shaders and six runtime material templates remain in the build. This increases the iOS Metal shader set and build time but preserves the tested post-processing look. The tested macOS bundle is 122.5 MiB.
- Third-party packages are Unity packages only (URP, Input System, uGUI, Test Framework, IDE integrations), governed by Unity's package/template licences. There is no third-party runtime dependency list beyond Unity itself and Python's standard library.
- Original assets are delivered as-is for RK VANTAGE GAMES. No trademark registration or copyright registration is claimed or performed by this project.
- `Tools/generate_audio.py` reproduces the 32 WAV files; `Tools/generate_meta.py` reproduces meta GUIDs from asset paths. Regenerating metas for existing assets is safe only while an asset path is unchanged.

## 8. Before commercial release

At minimum:

1. ~~Open and import with a licensed matching Unity editor; resolve console/import issues.~~ **Done for Unity 6000.6.7f1 on macOS.**
2. ~~Run EditMode and PlayMode tests; add a full automated playthrough and five-client integration test.~~ **EditMode 43/43, PlayMode 18/18 and a real 30 km PlayMode stream test pass; full manual playthrough and five-client Unity integration still needed.**
3. Perform the manual checklist in the test report on each target device.
4. ~~Produce and launch builds for every claimed platform; test touch, controller, audio and suspend/resume.~~ **macOS universal build produced and launched; other targets still needed.**
5. Deploy the premium service behind HTTPS with a strong admin secret, off-host encrypted backups and monitoring; verify `/api/v1/health`, catalogue, account registration/login, cloud sync and admin routes against the deployed host.
6. Configure and sandbox-test **one real purchase per provider**: Stripe test mode (price IDs, signed webhook, duplicate event, receipt, full refund); Apple sandbox tester (product IDs, receipt verification, duplicate replay); Google Play licence tester (service account, product IDs, purchase token verification, duplicate replay). Then repeat with production credentials.
7. Configure SMTP/SMS providers and a verified sending domain; test verification, reset, OTP and receipt email end to end.
8. Title and exercise the Unity client against the deployed premium service, including STORE OFFLINE, configured/unconfigured provider states, cancel/failure, insufficient GEMS, real purchase, receipt, ownership, transaction history and exact-position revive.
9. Add native Google/Apple sign-in plugins and test server token verification end to end; configure the OAuth client/service id.
10. Author terms of sale, privacy, refund, tax and age-consent policies; confirm card/store sales are permitted by each distribution channel.
11. Add TLS/authentication and deploy the race authority in a secured environment if online play is public.
12. Complete signing, notarisation, store listings and platform compliance.
13. Profile and optimize on low-end hardware; validate save migration across release versions.
14. Decide whether to support WebGL multiplayer; if yes, design a WebSocket gateway.
