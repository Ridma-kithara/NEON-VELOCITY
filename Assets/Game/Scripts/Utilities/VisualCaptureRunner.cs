using System;
using System.Collections;
using System.IO;
using NeonVelocity.Core;
using NeonVelocity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.Utilities
{
    /// <summary>
    /// Developer-only visual capture harness. Activated with the <c>-nv-capture &lt;directory&gt;</c>
    /// command-line switch so an automated build smoke run can inspect the generated menu and
    /// gameplay presentation without relying on OS screen-recording permission. The flag is
    /// ignored during normal play.
    /// </summary>
    public sealed class VisualCaptureRunner : MonoBehaviour
    {
        private string _directory;
        private int _captureWidth;
        private int _captureHeight;
        private bool _fast;

        /// <summary>
        /// Developer escape hatch used to isolate player-only post-processing failures from
        /// scene/camera problems. Only active when the flag is explicitly supplied.
        /// </summary>
        public static bool PostProcessingDisabled
        {
            get { return HasArgument("-nv-nopost"); }
        }

        private static bool HasArgument(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-nv-capture", StringComparison.OrdinalIgnoreCase))
                {
                    _directory = args[i + 1];
                }
                else if (string.Equals(args[i], "-nv-capture-size", StringComparison.OrdinalIgnoreCase))
                {
                    ParseSize(args[i + 1]);
                }
            }

            if (string.IsNullOrEmpty(_directory)) return;
            _fast = HasArgument("-nv-capture-fast");
            Directory.CreateDirectory(_directory);
            // The player pauses when unfocused by default. A build smoke run may start behind
            // the terminal, so keep the scene advancing until the captures are complete.
            Application.runInBackground = true;
            StartCoroutine(CaptureRoutine());
        }

        private void ParseSize(string value)
        {
            try
            {
                string[] parts = value.ToLowerInvariant().Split('x');
                if (parts.Length != 2) return;
                int width, height;
                if (int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height) &&
                    width >= 640 && height >= 360)
                {
                    _captureWidth = width;
                    _captureHeight = height;
                }
            }
            catch (Exception)
            {
                // A malformed optional flag must never prevent a normal capture run.
            }
        }

        private IEnumerator CaptureRoutine()
        {
            yield return new WaitForSecondsRealtime(2.4f);
            if (_captureWidth > 0 && _captureHeight > 0)
            {
                // Windows are requested explicitly so automation can exercise the 4K UI layout
                // even when the host display is smaller. The screenshot's own dimensions are the
                // authoritative result; the final report records any OS-level clamping.
                Screen.SetResolution(_captureWidth, _captureHeight, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(1.6f);
            }
            yield return Capture("menu.png");

            var app = GameApp.Instance;
            if (app == null)
            {
                Application.Quit();
                yield break;
            }

            if (_fast)            {
                app.ShowMainMenu();
                yield return new WaitForSecondsRealtime(0.4f);
                app.StartMode(GameModeId.Practice);
                float fastDeadline = Time.realtimeSinceStartup + 12f;
                while ((app.Session == null || app.Session.Runner == null || !app.Session.Runner.Running) &&
                       Time.realtimeSinceStartup < fastDeadline)
                    yield return null;
                float fastStart = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - fastStart < 1.6f)
                {
                    KeepRunSafe(app);
                    yield return null;
                }
                yield return Capture("gameplay-01.png");
                Debug.Log("[VISUAL CAPTURE] Fast complete: " + _directory);
                Application.Quit();
                yield break;
            }

            app.ShowSettings(false);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Capture("settings.png");

            // The premium endpoint sits at the bottom of the settings list; scroll it into view
            // so the network/premium configuration is covered by the visual smoke test too.
            var settingsScroll = app.UI.CurrentScreen != null
                ? app.UI.CurrentScreen.transform.Find("Settings Scroll") : null;
            var scroll = settingsScroll != null ? settingsScroll.GetComponent<ScrollRect>() : null;
            if (scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 0f;
                yield return new WaitForSecondsRealtime(0.4f);
                scroll.verticalNormalizedPosition = 0f;
                yield return Capture("settings-network.png");
            }

            app.ShowMainMenu();
            yield return new WaitForSecondsRealtime(0.6f);
            app.ShowShop(0);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Capture("shop-garage.png");

            app.ShowMainMenu();
            yield return new WaitForSecondsRealtime(0.6f);
            app.ShowModes();
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Capture("modes.png");

            app.ShowMainMenu();
            yield return new WaitForSecondsRealtime(0.5f);
            app.ShowAccount(AccountScreen.TabSignIn);
            yield return new WaitForSecondsRealtime(1.1f);
            yield return Capture("account-signin.png");

            app.ShowAccount(AccountScreen.TabRecovery);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Capture("account-recovery.png");

            app.ShowAccount(AccountScreen.TabProfile);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Capture("account-profile.png");

            app.ShowMainMenu();
            yield return new WaitForSecondsRealtime(0.5f);
            app.ShowEvents();
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Capture("events.png");

            app.ShowMainMenu();
            yield return new WaitForSecondsRealtime(0.5f);
            app.ShowDiscovery();
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Capture("discovery.png");

            app.ShowMainMenu();
            yield return new WaitForSecondsRealtime(0.5f);
            app.ShowAchievements();
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Capture("achievements.png");

            app.ShowMainMenu();
            yield return new WaitForSecondsRealtime(0.5f);
            app.ShowCharacter();
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Capture("characters.png");

            app.ShowMainMenu();
            yield return new WaitForSecondsRealtime(0.5f);
            app.ShowGemShop();
            yield return new WaitForSecondsRealtime(1.3f);
            yield return Capture("gem-shop-packages.png");

            app.ShowMainMenu();
            yield return new WaitForSecondsRealtime(0.6f);
            app.StartMode(GameModeId.Practice);
            float deadline = Time.realtimeSinceStartup + 12f;
            while ((app.Session == null || app.Session.Runner == null || !app.Session.Runner.Running) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;

            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 1.2f)
            {
                KeepRunSafe(app);
                yield return null;
            }
            yield return Capture("gameplay-01.png");

            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 3.5f)
            {
                KeepRunSafe(app);
                yield return null;
            }
            yield return Capture("gameplay-02.png");

            // Accelerate through the deterministic biome cycle so the suburb/station pass is
            // captured too. Locations are now 3 km apart, so the old 22-second budget could not
            // reach even one transition. Hazards are cleared while fast-forwarding, keeping this
            // a visual smoke test rather than a claim that the run was played at normal speed.
            bool reachedTunnel = false;
            bool capturedTransition = false;
            int lastBiome = app.Session != null && app.Session.Track != null
                ? app.Session.Track.CurrentBiome : -1;
            if (app.Session != null && app.Session.Track != null && !app.Session.IsFinished)
            {
                Time.timeScale = 6f;
                // High-resolution captures can run well below real time because Unity clamps
                // maximumDeltaTime, so distance advances slower per wall-clock second at 4K.
                float accelerateUntil = Time.realtimeSinceStartup + 240f;
                while (Time.realtimeSinceStartup < accelerateUntil)
                {
                    KeepRunSafe(app);
                    int biome = app.Session.Track.CurrentBiome;
                    if (!capturedTransition && biome != lastBiome)
                    {
                        // Hold the run briefly during a real biome crossfade so the lighting,
                        // sky shell and rain fade can be inspected in the captured frame.
                        capturedTransition = true;
                        lastBiome = biome;
                        Time.timeScale = 1f;
                        float transitionStart = Time.realtimeSinceStartup;
                        while (Time.realtimeSinceStartup - transitionStart < 0.65f)
                        {
                            KeepRunSafe(app);
                            yield return null;
                        }
                        yield return Capture("gameplay-transition.png");
                        Time.timeScale = 6f;
                        continue;
                    }
                    lastBiome = biome;
                    if (biome % 4 == 3)
                    {
                        reachedTunnel = true;
                        break;
                    }
                    yield return null;
                }
                Time.timeScale = 1f;
                yield return new WaitForSecondsRealtime(0.8f);
                if (reachedTunnel) yield return Capture("gameplay-tunnel.png");
            }

            if (app.Session != null && !app.Session.IsFinished)
            {
                app.ShowPause();
                yield return new WaitForSecondsRealtime(0.7f);
                yield return Capture("pause.png");
            }

            app.ShowMainMenu();
            yield return new WaitForSecondsRealtime(0.5f);
            Debug.Log("[VISUAL CAPTURE] Complete: " + _directory);
            Application.Quit();
        }

        private static void KeepRunSafe(GameApp app)
        {
            if (app.Session == null || app.Session.Runner == null || app.Session.Track == null) return;
            app.Session.Track.ClearHazardsAround(app.Session.Runner.PlayerZ, 45f);
        }

        private IEnumerator Capture(string file)
        {
            // macOS can recreate the player surface at the display's native backbuffer size
            // after the window has been in the background, so re-assert the requested size
            // immediately before each readback to keep a capture set dimensionally consistent.
            if (_captureWidth > 0 && _captureHeight > 0 &&
                (Screen.width != _captureWidth || Screen.height != _captureHeight))
            {
                Screen.SetResolution(_captureWidth, _captureHeight, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(0.35f);
            }
            string path = Path.Combine(_directory, file);
            // Capture through an explicit back-buffer readback. The asynchronous file path
            // has shown intermittent duplicated-UI frames on macOS Metal, so use the
            // synchronous texture path, then encode only after the readback has completed.
            yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            if (shot != null)
            {
                File.WriteAllBytes(path, shot.EncodeToPNG());
                Destroy(shot);
            }
            Debug.Log($"[VISUAL CAPTURE] {path}");
            yield return new WaitForSecondsRealtime(0.8f);
        }
    }
}
