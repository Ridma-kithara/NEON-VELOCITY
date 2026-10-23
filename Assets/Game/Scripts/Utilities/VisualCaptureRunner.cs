using System;
using System.Collections;
using System.IO;
using NeonVelocity.Core;
using UnityEngine;

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

            app.ShowSettings(false);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Capture("settings.png");

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
            string path = Path.Combine(_directory, file);
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[VISUAL CAPTURE] {path}");
            yield return new WaitForSecondsRealtime(0.8f);
        }
    }
}
