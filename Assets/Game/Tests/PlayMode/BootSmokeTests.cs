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

        [UnityTest]
        public IEnumerator PremiumRunnerAbility_ActivatesWithCooldown()
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (GameApp.Instance == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsNotNull(GameApp.Instance, "GameApp did not bootstrap");
            var app = GameApp.Instance;
            app.ShowMainMenu();
            yield return null;

            string previousCharacter = app.Profile.selectedCharacter;
            bool wasUnlocked = app.Profile.unlockedCharacters.Contains("phantom");
            try
            {
                if (!wasUnlocked) app.Profile.unlockedCharacters.Add("phantom");
                app.Progression.SelectCharacter("phantom");

                app.StartMode(GameModeId.Practice);
                deadline = Time.realtimeSinceStartup + 10f;
                while (!app.IsInRun && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.IsTrue(app.IsInRun, "Practice session did not start");
                var runner = app.Session.Runner;
                Assert.IsNotNull(runner);
                Assert.AreEqual("phantom", app.Profile.selectedCharacter);
                Assert.AreEqual("phase_dash", runner.AbilityId, "PHANTOM X must equip PHASE DASH");
                Assert.IsTrue(runner.HasAbility);

                deadline = Time.realtimeSinceStartup + 8f;
                while (!runner.Running && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.IsTrue(runner.Running, "Race did not start");

                runner.ActivateAbility();
                yield return null;
                Assert.IsTrue(runner.AbilityActive, "Activating PHASE DASH must enter the active state");
                Assert.Greater(runner.AbilityCooldownRemaining, 0f, "Ability must start its cooldown");
                Assert.GreaterOrEqual(runner.AbilityCooldownDuration, 8f, "Cooldowns are clamped to a fair minimum");

                float cooldownAfterFirst = runner.AbilityCooldownRemaining;
                runner.ActivateAbility();
                Assert.AreEqual(cooldownAfterFirst, runner.AbilityCooldownRemaining, 0.0001f,
                    "Repeated presses must not stack or refresh the ability");
            }
            finally
            {
                if (wasUnlocked)
                {
                    if (!app.Profile.unlockedCharacters.Contains("phantom"))
                        app.Profile.unlockedCharacters.Add("phantom");
                }
                else app.Profile.unlockedCharacters.Remove("phantom");
                if (!app.Profile.unlockedCharacters.Contains(previousCharacter))
                    app.Profile.unlockedCharacters.Add(previousCharacter);
                app.Progression.SelectCharacter(previousCharacter);
                app.EndSession(false);
                app.ShowMainMenu();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Revive_RestoresExactDistanceWithoutResettingTheRun()
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (GameApp.Instance == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsNotNull(GameApp.Instance, "GameApp did not bootstrap");
            var app = GameApp.Instance;
            app.ShowMainMenu();
            yield return null;

            app.StartMode(GameModeId.Practice);
            deadline = Time.realtimeSinceStartup + 10f;
            while (!app.IsInRun && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(app.IsInRun, "Practice session did not start");
            var runner = app.Session.Runner;
            Assert.IsNotNull(runner);

            deadline = Time.realtimeSinceStartup + 8f;
            while (!runner.Running && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(runner.Running, "Race did not start");
            app.Session.Track.ClearHazardsAround(runner.PlayerZ, 80f);
            yield return null;

            var crash = runner.CaptureReviveState();
            Assert.Greater(crash.Distance, 0f, "Run must have advanced before the revive snapshot");
            float distanceAtDeath = runner.Distance;
            int scoreAtDeath = runner.Score;
            runner.Kill();
            yield return null;
            Assert.IsTrue(runner.IsDead, "Kill must stop the runner");
            Assert.IsFalse(runner.Running);

            runner.ReviveAt(crash, 3f);
            Assert.IsFalse(runner.IsDead, "Revive must clear the death state");
            Assert.IsTrue(runner.Running, "Revive must resume the same run");
            Assert.AreEqual(distanceAtDeath, runner.Distance, 0.01f,
                "Revive must resume at the exact death distance, not the start line");
            Assert.AreEqual(crash.Lane, runner.Lane, "Revive must preserve the lane");
            Assert.That(runner.Score, Is.GreaterThanOrEqualTo(scoreAtDeath),
                "Revive must not erase distance or score accumulated before the crash");
            Assert.Greater(app.Session.Distance, 0f, "Revive must not reset world progression");
            yield return null;

            app.EndSession(false);
            app.ShowMainMenu();
            yield return null;
        }
    }
}
