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

        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-nv-capture", StringComparison.OrdinalIgnoreCase))
                {
                    _directory = args[i + 1];
                    break;
                }
            }

            if (string.IsNullOrEmpty(_directory)) return;
            Directory.CreateDirectory(_directory);
            // The player pauses when unfocused by default. A build smoke run may start behind
            // the terminal, so keep the scene advancing until the captures are complete.
            Application.runInBackground = true;
            StartCoroutine(CaptureRoutine());
        }

        private IEnumerator CaptureRoutine()
        {
            yield return new WaitForSecondsRealtime(2.4f);
            yield return Capture("menu.png");

            var app = GameApp.Instance;
            if (app == null)
            {
                Application.Quit();
                yield break;
            }

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
