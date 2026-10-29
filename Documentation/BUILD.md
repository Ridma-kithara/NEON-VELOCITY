# NEON VELOCITY — Build Guide

All player builds share the single boot scene `Assets/Game/Scenes/Main.unity`. Gameplay, UI, art and audio are generated at runtime, so no per-platform scene variants are required.

## 1. Install the right editor modules

Use Unity Hub → **Installs → 6000.6.7f1 → Add modules**:

| Target | Required module | Extra tooling |
|---|---|---|
| macOS | Mac Build Support (Mono or IL2CPP) | Xcode only for signing/notarisation |
| Windows 64-bit | Windows Build Support (Mono or IL2CPP) | — |
| Linux 64-bit | Linux Build Support (Mono or IL2CPP) | — |
| WebGL | WebGL Build Support | Modern browser; no raw-TCP online |
| Android APK/AAB | Android Build Support + SDK/NDK + OpenJDK | keystore for release signing |
| iOS | iOS Build Support | macOS + Xcode |

At hand-off, **macOS player support** was available and produced a verified universal build. Windows, Linux, Android and iOS modules were not available/verified here. No Android SDK, NDK or `adb` was present.

An active Unity licence is required before importing or building. The licence used for the verified build was a Personal entitlement.

## 2. Configure once

Open the project, then use the menu:

**NEON VELOCITY → Configure Project and Build Settings**

This applies and saves:

- Company/product: **RK VANTAGE GAMES / NEON VELOCITY**
- Version: **1.0.0**
- Bundle ID: **games.rkvantage.neonvelocity** for Standalone, Android and iOS
- Default window: 1920 × 1080; WebGL: 1280 × 720
- Landscape-only orientation with both landscape directions allowed
- Linear colour space
- Android: min SDK 26, ARM64 only, IL2CPP, internet permission forced
- Build scene list: only `Assets/Game/Scenes/Main.unity`, enabled
- Runtime URP shader templates under `Assets/Game/Resources/Materials/`, so URP/Lit and URP/Unlit variants remain available to runtime-created materials
- Always-included post-processing shaders (Bloom, Gaussian/Bokeh DoF, motion blur, Panini, UberPost)
- URP unused-post-processing-resource stripping disabled so runtime-created volume profiles work
- URP unused-variant stripping disabled in `UniversalRenderPipelineGlobalSettings.asset`; every world material is created at runtime, so its variants must not be removed
- PC renderer uses Forward+ with native render pass and no renderer features (the broken SSAO feature was removed)

The same settings are already persisted in `ProjectSettings/ProjectSettings.asset` and `ProjectSettings/EditorBuildSettings.asset`, but running the menu item is recommended after changing Unity versions.

## 3. Build from the editor

With the target module installed and the project open, use:

**NEON VELOCITY →**

- **Build macOS** → `Builds/macOS/NeonVelocity.app`
- **Build Windows 64-bit** → `Builds/Windows/NeonVelocity.exe`
- **Build Linux 64-bit** → `Builds/Linux/NeonVelocity.x86_64`
- **Build WebGL** → `Builds/WebGL/`
- **Build Android APK** → `Builds/Android/NeonVelocity.apk`
- **Build Android App Bundle (AAB)** → `Builds/Android/NeonVelocity.aab`
- **Build iOS Xcode Project** → `Builds/iOS/`
- **Build Active Target** → builds whichever target is active in Build Settings

Each build method calls `BuildPipeline.BuildPlayer`, logs the summary and exits batch mode with code 1 if the module is missing, the scene is missing or the build fails.

## 4. Batch / CI commands

macOS example (adjust the editor path and project path):

```bash
UNITY="/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity"
PROJECT="/absolute/path/to/NEON VELOCITY"

"$UNITY" -batchmode -quit -projectPath "$PROJECT" \
  -executeMethod NeonVelocity.EditorTools.BuildTools.BuildMacOS \
  -logFile "$PROJECT/Builds/build-macos.log"
```

Replace `BuildMacOS` with any entry point:

| Method | Output |
|---|---|
| `BuildMacOS` | `Builds/macOS/NeonVelocity.app` |
| `BuildWindows` | `Builds/Windows/NeonVelocity.exe` |
| `BuildLinux` | `Builds/Linux/NeonVelocity.x86_64` |
| `BuildWebGL` | `Builds/WebGL` |
| `BuildAndroid` | `Builds/Android/NeonVelocity.apk` |
| `BuildAndroidBundle` | `Builds/Android/NeonVelocity.aab` |
| `BuildIOS` | `Builds/iOS` (Xcode project) |
| `BuildActiveTarget` | active target's configured path |

CI hosts need a Unity licence (licensed runner or floating licence) and the matching module. Build logs are the source of truth; do not claim a target until a build and launch have actually succeeded.

## 5. Platform notes

### macOS

- Produces an `.app` bundle. The project does not configure notarisation, hardened runtime, entitlements or signing identity.
- **BuildMacOS** sets `OSXStandalone.UserBuildSettings.architecture` to **x86_64 + ARM64**, producing a universal app. The final verified build was **122.5 MiB**, built in **27.3 s** (**39.0 s wall** including Unity startup), and launched on an Intel Mac.
- The tested app is ad-hoc signed only. For distribution, sign with a Developer ID and notarise separately.
- Online needs outbound TCP access to port 7777 (or the configured port).
- Optional visual capture: launch the executable with `-nv-capture /absolute/output/directory -nv-capture-size 1600x900` (use `3840x2160` for a 4K pass). The player saves 18 screens — `menu.png`, `settings.png`, `settings-network.png`, `shop-garage.png`, `modes.png`, `account-signin.png`, `account-recovery.png`, `account-profile.png`, `events.png`, `discovery.png`, `achievements.png`, `characters.png`, `gem-shop-packages.png`, `gameplay-01.png`, `gameplay-02.png`, `gameplay-transition.png`, `gameplay-tunnel.png` and `pause.png` — then exits. This developer flag does not affect normal launch. The harness re-asserts the requested resolution before each readback.

### Windows / Linux

- Scripts exist and use the same boot scene. Neither was built here because the modules are not installed.
- No installer or store packaging is configured; the raw player output is produced.

### WebGL

- **Online races are intentionally unavailable** in WebGL builds. `RaceClient` refuses raw TCP sockets in the browser and shows an explanatory error. All offline modes work.
- Make the build with **Build WebGL**, serve the folder over HTTP(S); `file://` loading is not supported by Unity's loader.
- Browser autoplay policy may delay audio until the first user gesture.
- WebGL build size/compression settings follow Unity defaults; no custom server headers are included.

### Android

- Requirements once the module is installed: SDK, NDK, OpenJDK matching Unity 6000.6.7f1, and an Android device/emulator.
- Configuration: ARM64, IL2CPP, min SDK 26, internet permission, landscape.
- For a release APK/AAB create a keystore and configure **Player Settings → Publishing Settings**. No keystore is included.
- Use the LAN/public server IP in **Settings → Network**; `127.0.0.1` points at the phone.
- Raw TCP is used. Verify the network path/firewall; no TLS is implemented.

### iOS

- Requires macOS, Xcode and the iOS Build Support module.
- **Build iOS Xcode Project** writes a project to `Builds/iOS`; open it in Xcode, set a signing team and run/archive there.
- Unity generates the Xcode project but this repository does not configure provisioning profiles, capabilities or App Store metadata.
- If connecting to a LAN server, iOS local-network privacy requirements may require adding a local network usage description in the generated Xcode project. This has not been verified.
- Online requires TCP reachability; no TLS or App Transport Security changes are configured.

## 6. Server and premium endpoints for builds

### Multiplayer authority

The client defaults to `127.0.0.1:7777`, which is only useful on the same machine. For devices and remote players:

1. Run the server where clients can reach it: `python3 Backend/server.py --host 0.0.0.0 --port 7777`.
2. Open the game's **Settings → Network** and enter the reachable host and port.
3. Save, return to the menu and open **MULTIPLAYER**.

The server has no authentication beyond ephemeral session tokens and no TLS. Use a trusted LAN or a VPN. For public internet deployment, place it behind infrastructure you control and add transport security before exposing it broadly; the stock Unity client cannot speak TLS, so that requires a client/protocol change.

### Premium GEM service

- The game defaults the premium endpoint to `http://127.0.0.1:8088` for local testing. Set the production **https://** endpoint in **Settings → Network → Premium Store URL**.
- Changing the endpoint clears that profile's cached GEM balance, transaction history, token and entitlements before the next sync. Server state remains authoritative; an unreachable service shows **STORE OFFLINE** and cannot sell or revive.
- The service is separate from the race authority. Deploy `Backend/premium_server.py` behind an HTTPS reverse proxy with admin credentials, backups and monitoring. Full procedure: [PREMIUM_DEPLOYMENT.md](../Backend/PREMIUM_DEPLOYMENT.md).
- Five GEM tiers are served and can be overridden with `NV_GEM_PACKAGE_<ID>_{GEMS,BONUS_GEMS,PRICE_LKR,NAME,DESCRIPTION,ENABLED}`. Stripe-hosted checkout needs a server secret, price IDs, webhook secret and HTTPS return pages. The Unity client never receives secret keys or price IDs.
- **Unity IAP 4.15.1 is integrated** (`com.unity.purchasing`) for Apple In-App Purchase on iOS and Google Play Billing on Android. The client forwards the store receipt/token to the backend, which verifies it before crediting GEMS. Store products must use the backend package ids (`gems_80`, `gems_200`, ...) unless matching alias maps are configured on both server and client.
- **No store product or merchant account is configured.** The shop reads `/api/v1/store/payments` and will not start a platform purchase when the matching provider capability is absent. Apple/Google billing cannot be called live until the store products, credentials and device builds exist.
- The retired manual bank-transfer flow is **off by default**. Only `NV_ENABLE_MANUAL_BANK_TRANSFER=1` re-enables the legacy routes, and it is not shown in the Phase 03 shop.
- Account/cloud-sync endpoints run in the same service. Email verification/reset/receipt delivery needs SMTP credentials; phone OTP needs an SMS gateway. Server-side Google ID-token and Apple identity-token verification exist, but the Unity build has no native Google/Apple sign-in plugin.
- Normal purchases never accept card details in Unity. Card checkout is hosted by Stripe; Apple/Google use their official store sheets. Card numbers, CVV and provider secrets must never be placed in the project or a build.

## 7. Pre-release verification checklist

A target is only "verified" after all of the following on real hardware:

- [x] Unity editor imports the project with zero compile errors.
- [x] EditMode tests pass (core + save + display/settings + master volume + progression/locations/junctions + transaction labels): **43 / 43**.
- [x] PlayMode tests pass (boot, practice, jump, navigation/Back, settings-from-pause, results, restart, garage cleanup, purchase confirmation, Phase 03 shop tabs/offline history, account/events/atlas/achievements, ability/cooldown, exact revive, endpoint cache reset, menu-label visibility, independent HUD tickers and pause HUD visibility): **18 / 18**.
- [ ] Offline modes start, pause, crash, finish and persist progression after restart on every target.
- [ ] The backend starts, and 2–5 humans can match, ready, count down, race and receive results.
- [ ] A player can disconnect and reconnect within 45 s without losing their slot.
- [x] macOS build succeeds and launches outside the editor (**universal, launched on Intel**).
- [ ] Audio is audible and input works with the target's keyboard/gamepad/touch.
- [ ] Online connectivity is tested from the target network (WebGL expected to fail by design).
- [ ] The deployed premium endpoint is set and STORE OFFLINE, catalogue, one sandbox purchase per provider, duplicate replay, receipt, refund, ownership, transaction-history and revive paths are tested against it.
- [ ] Signing/store packaging (Android/iOS/macOS) is completed separately.

The macOS universal build has been produced and launched. Windows/Linux/WebGL/Android/iOS builds have not; see [TEST_REPORT.md](TEST_REPORT.md).
