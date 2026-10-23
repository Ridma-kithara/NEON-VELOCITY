using System.Collections;
using System.Reflection;
using NeonVelocity;
using NeonVelocity.Core;
using NeonVelocity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVelocity.Tests
{
    /// <summary>
    /// End-to-end UI navigation coverage for the centralized Back stack, modal priority,
    /// settings-from-pause flow and garage preview lifecycle.
    /// </summary>
    public sealed class NavigationTests
    {
        private static IEnumerator WaitForApp()
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (GameApp.Instance == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsNotNull(GameApp.Instance, "GameApp did not bootstrap");
            yield return null;
        }

        [UnityTest]
        public IEnumerator BackReturnsThroughPushedScreensToMainMenu()
        {
            yield return WaitForApp();
            var app = GameApp.Instance;
            app.ShowMainMenu();
            yield return null;

            Assert.AreEqual("MainMenu", app.CurrentScreenName);
            app.ShowModes();
            yield return null;
            Assert.AreEqual("Modes", app.CurrentScreenName);
            app.ShowShop(0);
            yield return null;
            Assert.AreEqual("Shop", app.CurrentScreenName);
            Assert.IsTrue(app.CanNavigateBack, "Shop should have Modes on the history stack");

            app.NavigateBack();
            yield return null;
            Assert.AreEqual("Modes", app.CurrentScreenName, "Back from Shop must return to Modes");
            Assert.IsTrue(app.UI.IsScreenOpen);

            app.NavigateBack();
            yield return null;
            Assert.AreEqual("MainMenu", app.CurrentScreenName, "Back from Modes must return to Main Menu");
            Assert.IsFalse(app.CanNavigateBack, "Main Menu is the only navigation root");
            Assert.IsTrue(app.UI.IsScreenOpen, "Main Menu must remain visible");
        }

        [UnityTest]
        public IEnumerator BackDismissesModalBeforeLeavingTheScreen()
        {
            yield return WaitForApp();
            var app = GameApp.Instance;
            app.ShowMainMenu();
            yield return null;

            app.QuitApplication();
            yield return null;
            Assert.IsTrue(app.UI.HasModal, "Quit must ask for confirmation");
            app.HandleBack();
            yield return null;
            Assert.IsFalse(app.UI.HasModal, "Back must dismiss the confirmation modal first");
            Assert.AreEqual("MainMenu", app.CurrentScreenName);
            Assert.IsTrue(app.UI.IsScreenOpen, "Dismissing a modal must not close the underlying screen");

            app.HandleBack();
            yield return null;
            Assert.IsTrue(app.UI.HasModal, "Back on the title screen must confirm before quitting");
            Assert.AreEqual("MainMenu", app.CurrentScreenName);
            app.HandleBack();
            yield return null;
            Assert.IsFalse(app.UI.HasModal);
        }

        [UnityTest]
        public IEnumerator SettingsFromPauseReturnsToPauseThenResumesRun()
        {
            yield return WaitForApp();
            var app = GameApp.Instance;
            app.ShowMainMenu();
            yield return null;

            app.StartMode(GameModeId.Practice);
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!app.IsInRun && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(app.IsInRun, "Practice session did not start");

            app.ShowPause();
            yield return null;
            Assert.AreEqual("Pause", app.CurrentScreenName);
            Assert.IsTrue(app.Session.IsPaused, "Pause screen must pause the session");

            app.ShowSettings(true);
            yield return null;
            Assert.AreEqual("Settings", app.CurrentScreenName);

            app.HandleBack();
            yield return null;
            Assert.AreEqual("Pause", app.CurrentScreenName,
                "Back from Settings opened in a run must rebuild Pause");
            Assert.IsTrue(app.Session.IsPaused, "Settings Back must not silently resume");

            app.HandleBack();
            yield return null;
            Assert.IsFalse(app.UI.IsScreenOpen, "Back from Pause must return to the run");
            Assert.IsFalse(app.Session.IsPaused, "Resuming from Pause must unpause the session");
            Assert.IsTrue(app.IsInRun);

            app.EndSession(false);
            app.ShowMainMenu();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ResultsBackReturnsToMainMenuRoot()
        {
            yield return WaitForApp();
            var app = GameApp.Instance;
            app.ShowMainMenu();
            yield return null;

            app.ShowResults(new RunResult
            {
                ModeId = GameModeId.Practice,
                ModeTitle = "Practice Loop",
                Distance = 123f,
                Time = 12.3f,
                Score = 1234,
                CreditsEarned = 12,
                XpEarned = 20
            });
            yield return null;
            Assert.AreEqual("Results", app.CurrentScreenName);
            Assert.IsTrue(app.UI.IsScreenOpen);

            app.HandleBack();
            yield return null;
            Assert.AreEqual("MainMenu", app.CurrentScreenName, "Results Back must return to the Main Menu root");
            Assert.IsTrue(app.UI.IsScreenOpen, "Main Menu must be shown after leaving Results");
            Assert.IsFalse(app.CanNavigateBack, "Results must not leave a stale history entry");
        }

        [UnityTest]
        public IEnumerator ShopPurchaseRequiresConfirmationBeforeCharging()
        {
            yield return WaitForApp();
            var app = GameApp.Instance;
            app.ShowMainMenu();
            yield return null;
            app.ShowShop(0);
            yield return null;

            int credits = app.Profile.credits;
            int level = app.Profile.level;
            bool wasUnlocked = app.Profile.unlockedCharacters.Contains("volt");
            try
            {
                if (wasUnlocked) app.Profile.unlockedCharacters.Remove("volt");
                app.Profile.level = Mathf.Max(level, 2);
                app.Profile.credits = Mathf.Max(credits, 2500);
                int eligibleCredits = app.Profile.credits;

                var shop = app.UI.CurrentScreen.GetComponent<ShopScreen>();
                var request = typeof(ShopScreen).GetMethod("RequestCharacter",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(request, "Shop purchase entry point changed without updating the QA test");
                request.Invoke(shop, new object[] { "volt" });
                yield return null;
                Assert.IsTrue(app.UI.HasModal, "UNLOCK must open a purchase confirmation");
                Assert.IsFalse(app.Profile.unlockedCharacters.Contains("volt"),
                    "Opening confirmation must not unlock the runner");
                Assert.AreEqual(eligibleCredits, app.Profile.credits,
                    "Opening confirmation must not charge credits");

                request.Invoke(shop, new object[] { "volt" });
                yield return null;
                Assert.IsTrue(app.UI.HasModal, "Repeated clicks must not stack a second purchase dialog");
                Assert.AreEqual(eligibleCredits, app.Profile.credits);
                app.HandleBack();
                yield return null;
                Assert.IsFalse(app.UI.HasModal);
            }
            finally
            {
                app.Profile.credits = credits;
                app.Profile.level = level;
                if (!wasUnlocked) app.Profile.unlockedCharacters.Remove("volt");
                else if (!app.Profile.unlockedCharacters.Contains("volt")) app.Profile.unlockedCharacters.Add("volt");
            }
        }

        [UnityTest]
        public IEnumerator GaragePreviewIsDestroyedWhenShopCloses()
        {
            yield return WaitForApp();
            var app = GameApp.Instance;
            app.ShowMainMenu();
            yield return null;

            app.ShowShop(0);
            yield return null;
            yield return null;
            Assert.IsNotNull(Object.FindAnyObjectByType<GaragePreview>(), "Garage preview was not created");

            app.NavigateBack();
            yield return null;
            yield return null;
            Assert.IsNull(Object.FindAnyObjectByType<GaragePreview>(),
                "Garage preview camera, render texture and stage must be destroyed with Shop");
            Assert.AreEqual("MainMenu", app.CurrentScreenName);
        }
    }
}
