using System.Collections;
using NeonVelocity;
using NeonVelocity.Core;
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

            app.EndSession(false);
            yield return null;
            Assert.IsFalse(app.IsInRun, "Session did not shut down");
        }
    }
}
