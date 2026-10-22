using System.Collections;
using System.Reflection;
using NeonVelocity;
using NeonVelocity.Core;
using NeonVelocity.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVelocity.Tests
{
    /// <summary>
    /// PlayMode smoke coverage. Boots the real runtime (no scene wiring), confirms the main menu
    /// exists, starts a practice run and shuts it down cleanly.
    /// </summary>
    public sealed class BootSmokeTests
    {
        [UnityTest]
        public IEnumerator AppBootsMainMenuAndStartsPractice()
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (GameApp.Instance == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsNotNull(GameApp.Instance, "GameApp did not bootstrap");
            var app = GameApp.Instance;

            yield return null;
            Assert.IsNotNull(app.UI, "UI root missing");
            Assert.IsNotNull(app.SaveService, "Save service missing");
            Assert.IsNotNull(app.Profile, "Profile missing");
            Assert.IsTrue(app.UI.IsScreenOpen, "Main menu did not open");
            Assert.IsNotNull(app.MenuStage, "Menu stage missing");
            Assert.AreEqual("NEON VELOCITY", Application.productName, "Product name branding");

            app.StartMode(GameModeId.Practice);
            deadline = Time.realtimeSinceStartup + 10f;
            while (!app.IsInRun && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(app.IsInRun, "Practice session did not start");
            Assert.IsNotNull(app.Session);
            Assert.AreEqual(GameModeId.Practice, app.Session.Mode.Id);
            Assert.IsNotNull(app.Session.Runner, "Runner missing");
            Assert.IsNotNull(app.Session.Track, "Track missing");
            Assert.IsNotNull(app.Session.Input, "Input missing");
            Assert.IsNotNull(app.UI.Hud, "HUD missing");
            Assert.That(app.Session.Track.ActiveSegmentCount, Is.GreaterThan(0));

            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsTrue(app.UI.Hud != null);
            Assert.That(app.Session.Duration, Is.EqualTo(0f));
            Assert.IsFalse(app.Session.IsOnline);

            // Let the countdown finish and run the real update loop for a few seconds so
            // gameplay/VFX/collision systems are exercised, not just constructed.
            yield return new WaitForSecondsRealtime(3.5f);
            Assert.That(app.Session.Distance, Is.GreaterThan(0f), "Runner did not advance after countdown");
            Assert.IsFalse(app.Session.IsFinished, "Practice run ended unexpectedly during smoke window");

            app.EndSession(false);
            yield return null;
            Assert.IsFalse(app.IsInRun, "Session did not shut down");
        }

        [UnityTest]
        public IEnumerator QuickTapJumpClearsLowBarrierHeight()
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (GameApp.Instance == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsNotNull(GameApp.Instance, "GameApp did not bootstrap");
            var app = GameApp.Instance;
            yield return null;

            app.StartMode(GameModeId.Practice);
            deadline = Time.realtimeSinceStartup + 10f;
            while (!app.IsInRun && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(app.IsInRun, "Practice session did not start");
            var runner = app.Session.Runner;
            var track = app.Session.Track;
            Assert.IsNotNull(runner, "Runner missing");

            deadline = Time.realtimeSinceStartup + 8f;
            while (!runner.Running && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(runner.Running, "Race did not start");
            track.ClearHazardsAround(runner.PlayerZ, 240f);
            yield return null;

            Assert.IsTrue(app.Session.Input.Enabled, "Input router was disabled after race start");
            int jumpsBefore = runner.Jumps;
            float maxHeight = runner.Height;

            // Drive the same private handlers the InputRouter events call. Simulated Input
            // System state does not always advance inside a headless PlayMode test frame, but
            // these handlers are exactly what the player invokes on a one-frame key tap.
            var requestJump = typeof(RunnerController).GetMethod("RequestJump",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var releaseJump = typeof(RunnerController).GetMethod("ReleaseJump",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(requestJump, "RequestJump handler not found");
            Assert.IsNotNull(releaseJump, "ReleaseJump handler not found");
            requestJump.Invoke(runner, null);
            releaseJump.Invoke(runner, null);
            yield return null;
            Assert.Greater(runner.Jumps, jumpsBefore, "Quick tap did not register a jump");
            Assert.IsFalse(runner.IsGrounded, "Runner did not leave the ground on a quick tap");

            float watch = Time.realtimeSinceStartup + 0.55f;
            while (Time.realtimeSinceStartup < watch)
            {
                track.ClearHazardsAround(runner.PlayerZ, 60f);
                maxHeight = Mathf.Max(maxHeight, runner.Height);
                yield return null;
            }
            Assert.Greater(maxHeight, 1.0f,
                $"Quick-tap jump only reached {maxHeight:F2} m; low barriers need > 1.0 m");
            Assert.IsFalse(runner.IsDead, "Runner died during the jump check");

            app.EndSession(false);
            yield return null;
            Assert.IsFalse(app.IsInRun, "Session did not shut down");
        }
    }
}
