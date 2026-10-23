# NEON VELOCITY — Known Limitations

This document records what is genuinely complete, what is constrained by the current environment, and what would need further work before a commercial platform release. It should be read together with [TEST_REPORT.md](TEST_REPORT.md).

## 1. Verification status

### Verified in this environment (24 October 2026)

- Unity **6000.6.7f1** imported and compiled the project with **0 errors** after the editor-only assembly boundary was added.
- **50 / 50** headless gameplay-core tests, **9 / 9** Python authority tests and **709** static checks passed.
- **24 / 24** Unity EditMode tests passed.
- **8 / 8** Unity PlayMode tests passed: real bootstrap, title stage, Practice start, clean shutdown, quick-tap jump regression, centralized navigation/Back routes, Settings-from-Pause, Results return, garage cleanup and purchase-confirmation safety.
- Developer-run captures of eight screens at 1600×900, 1280×800 and 3840×2160 confirmed the generated menu, HUD, garage, beveled runner, road, tiered city facades, storefronts, wet reflections and a seeded subway-station segment render in the player. The subway frame uses a temporary time-scale acceleration and hazard clearing. The 4K PNGs are actual 3840×2160 output; no physical 4K display was available. This is a smoke check, not a full manual graphics review.
- A universal **x86_64 + ARM64** macOS player (`Builds/macOS/NeonVelocity.app`, 117.0 MiB) was built and launched. The final capture run completed without runtime exceptions.
- The app is **ad-hoc signed** only; there is no Developer ID, hardened runtime or notarisation.

### Still unverified

- A full manual playthrough of all 11 modes, every power-up, collision, pickup, result and persistence path.
- Two- and five-client races with real Unity clients; reconnect/disconnect through the game UI.
- Windows, Linux, WebGL, Android and iOS player builds.
- Real-device touch, gamepad, audio, suspend/resume and performance.
- Frame time, memory, battery and thermal profiling.
- Audible audio balance, loudness and device output for the 27 synthesized clips; only missing-resource checks were performed.
- Public-internet deployment, TLS, signing, notarisation and store packaging.

### Platform build status

| Target | Offline | Online (native TCP) | Build status |
|---|---|---|---|
| macOS | Implemented | Implemented | **Built and launched** (universal, ad-hoc signed, not notarised) |
| Windows 64-bit | Implemented | Implemented | Not built (module/tooling not verified) |
| Linux 64-bit | Implemented | Implemented | Not built (module/tooling not verified) |
| WebGL | Implemented | **Not supported** | Not built |
| Android | Implemented | Implemented in code | Not built (Android module/SDK absent) |
| iOS | Implemented | Implemented in code | Not built (iOS module absent) |

Do not claim a platform is supported until its build has been made, launched and tested on real hardware. macOS is the only target with a confirmed launch here, and its broader gameplay/manual pass is still outstanding.

## 2. Multiplayer constraints

### WebGL cannot play online

Browsers do not expose raw TCP sockets. `RaceClient` explicitly blocks online connections in non-editor WebGL builds and shows an error. WebGL builds are **offline-only**. Supporting WebGL multiplayer would require a WebSocket gateway and a server/protocol change; this is not implemented.

### Plaintext transport, no accounts

- `nv1` runs over **plain TCP with line-delimited JSON**. There is no TLS, encryption or message signing.
- Identity is an ephemeral server-issued player ID + bearer token held in memory for the client session. There are no passwords, persistent accounts, email recovery, cross-device identity or account linking.
- Anyone who can reach the server and obtains a token can act for that session. Use a trusted LAN/VPN or add transport security and authentication before exposing the service publicly.
- The stock client cannot speak TLS, so a TLS terminator alone is insufficient without a client change.

### Session and room lifetime

- Live accounts and rooms are **in-memory**. A server restart ends all active races and invalidates reconnection tokens.
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

## 3. Gameplay and content constraints

- **Offline practice/tutorial** intentionally grant no competitive progression. Daily, weekly and seasonal bonuses are once per period and stored locally.
- The local save is editable by the player. It is not authoritative for online rewards or the server leaderboard, but local credits/XP/cosmetics can be modified with a text editor.
- Cloud import/export exists as JSON (`SaveService.ExportJson`/`ImportCloudJson`) but there is **no cloud service, account sync or conflict resolution UI**. The import heuristic prefers the larger `xp + credits × 0.2` profile.
- The **language setting** is persisted but the UI/content is English-only. No localization tables are shipped.
- Accessibility is limited to reduced motion, high contrast, camera-shake toggle, volume/sensitivity settings and UI scale from the canvas. There are no colour-blind palettes, subtitle/caption options, full key remapping or screen-reader support.
- Only one input layout is provided for keyboard/gamepad/touch. Gamepad UI navigation relies on Unity's default UI navigation; focus behavior is not verified.
- There is no tutorial replay option after completion besides selecting First Contact again.
- Character/trail cosmetics are visual only; balance comes from permanent upgrades.
- The track has four visual themes and 20 authored patterns; variety is procedural but finite. No licensed music, voice acting or story campaign is included.
- Time dilation is disabled in online races; other power-ups are personal pickups on the shared deterministic track.
- Offline leaderboard entries are local. Online leaderboard identity is name-based; identical names merge, and there is no moderation or name reservation.

## 4. Presentation, audio and performance

- All world geometry, textures, UI and audio are generated from Unity primitives/procedural code and original synthesis. The look/mix is therefore stylized and bounded by that approach.
- No final art/audio polish pass, platform loudness normalization, mixer snapshot automation or dynamic-range testing has been performed.
- No frame-time, memory, battery, thermal or loading-time profiling exists for any target. The procedural runtime world and UI may need optimization on low-end Android/iOS devices.
- WebGL autoplay policies may delay or mute audio until user interaction; this is not verified.
- URP renderer selection and the core runtime shader set have been validated in the built player, but a full visual/quality pass across all four themes and quality presets has not.
- The runtime generates materials and meshes; repeated rapid restarts could stress memory management. This has not been measured.

## 5. Platform/package constraints

- macOS player support was available and a universal build succeeded. Windows, Linux, Android and iOS modules were not available/verified here and must be added through Unity Hub.
- No Android SDK, NDK, OpenJDK, device or `adb` is available. No keystore is included.
- No Xcode iOS export, provisioning profile, signing team or App Store metadata is configured.
- No macOS notarisation, hardened runtime, entitlements or Developer ID signing identity is configured; the tested build is ad-hoc signed.
- Unity's player settings persist a default host of `127.0.0.1:7777`. Every remote device must change this in Settings; a build shipped with defaults only connects to a server on the same device.
- The server requires TCP port 7777 (or configured port) to be reachable. No relay, NAT traversal, UPnP, matchmaking service or domain/TLS setup exists.
- Android/iOS network privacy prompts and local-network permissions have not been tested. iOS LAN access may require an `NSLocalNetworkUsageDescription` generated in Xcode.

## 6. Documentation and licensing constraints

- The project is maintained in the GitHub repository `Ridma-kithara/NEON-VELOCITY`, but has **no CI pipeline**, code-signing identity or release channel. Builds and releases must be produced separately.
- URP post-processing resource stripping is disabled in `UniversalRenderPipelineGlobalSettings.asset` so runtime-created volume profiles keep Bloom, depth of field, motion blur and Panini shaders. This increases player size slightly; the tested macOS bundle is 117.0 MiB.
- Third-party packages are Unity packages only (URP, Input System, uGUI, Test Framework, IDE integrations), governed by Unity's package/template licences. There is no third-party runtime dependency list beyond Unity itself and Python's standard library.
- Original assets are delivered as-is for RK VANTAGE GAMES. No trademark registration or copyright registration is claimed or performed by this project.
- `Tools/generate_audio.py` reproduces the WAV files; `Tools/generate_meta.py` reproduces meta GUIDs from asset paths. Regenerating metas for existing assets is safe only while an asset path is unchanged.

## 7. Before commercial release

At minimum:

1. ~~Open and import with a licensed matching Unity editor; resolve console/import issues.~~ **Done for Unity 6000.6.7f1 on macOS.**
2. ~~Run EditMode and PlayMode tests; add a full automated playthrough and five-client integration test.~~ **EditMode/PlayMode smoke tests pass; full automated playthrough and five-client Unity integration still needed.**
3. Perform the manual checklist in the test report on each target device.
4. ~~Produce and launch builds for every claimed platform; test touch, controller, audio and suspend/resume.~~ **macOS universal build produced and launched; other targets still needed.**
5. Add TLS/authentication and deploy the authority in a secured environment if online play is public.
6. Complete signing, notarisation, store listings, privacy policy and platform compliance.
7. Profile and optimize on low-end hardware; validate save migration across release versions.
8. Decide whether to support WebGL multiplayer; if yes, design a WebSocket gateway.
