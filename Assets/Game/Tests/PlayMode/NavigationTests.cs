using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NeonVelocity;
using NeonVelocity.Core;
using NeonVelocity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
        public IEnumerator RestartSessionRebuildsTheRunAndResetsDistance()
        {
            yield return WaitForApp();
            var app = GameApp.Instance;
            app.ShowMainMenu();
            yield return null;

            app.StartMode(GameModeId.Practice);
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((!app.IsInRun || app.Session == null) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(app.IsInRun, "Practice session did not start");
            var first = app.Session;

            // Let the countdown finish and the run advance so the restart has state to clear.
            yield return new WaitForSecondsRealtime(3.6f);
            Assert.Greater(first.Distance, 0f, "Practice session did not advance");

            app.RestartSession();
            deadline = Time.realtimeSinceStartup + 10f;
            while ((!app.IsInRun || app.Session == first) && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(app.IsInRun, "Restart did not start a run");
            Assert.IsNotNull(app.Session, "Restart produced no session");
            Assert.AreNotSame(first, app.Session, "Restart must rebuild the session");
            Assert.AreEqual(GameModeId.Practice, app.Session.Mode.Id,
                "Restart must preserve the current mode");
            Assert.IsFalse(app.Session.IsPaused, "Restart must not resume paused");
            // A fresh run begins at the profile's upgraded head-start distance, not necessarily
            // at zero. Asserting "< 1 m" made the test depend on a developer profile with no
            // head-start upgrade (the current local profile has level 2 = 110 m).
            int headStart = Mathf.RoundToInt(ProgressionMath.UpgradeValue("head_start",
                app.Profile.GetUpgradeLevel("head_start")));
            Assert.That(app.Session.Distance, Is.LessThan(first.Distance),
                "Restart must discard distance gained during the previous run");
            Assert.That(app.Session.Distance, Is.InRange(headStart - 1f, headStart + 5f),
                "Restart must begin at the upgraded start line");

            app.EndSession(false);
            yield return null;
            Assert.IsFalse(app.IsInRun, "Session did not shut down after restart");
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

        [UnityTest]
        public IEnumerator PremiumScreensBuildAndAllTabsOpenWithoutTheStore()
        {
            yield return WaitForApp();
            var app = GameApp.Instance;
            app.ShowMainMenu();
            yield return null;

            app.ShowGemShop();
            yield return null;
            Assert.AreEqual("GemShop", app.CurrentScreenName);
            var gemShop = app.UI.CurrentScreen.GetComponent<GemShopScreen>();
            Assert.IsNotNull(gemShop, "GEM SHOP screen component missing");
            var selectTab = typeof(GemShopScreen).GetMethod("SelectTab",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(selectTab, "GEM SHOP tab entry point changed without updating the QA test");
            foreach (GemShopScreen.GemShopTab tab in System.Enum.GetValues(typeof(GemShopScreen.GemShopTab)))
            {
                selectTab.Invoke(gemShop, new object[] { tab });
                yield return null;
                Assert.IsTrue(gemShop.isActiveAndEnabled);
            }

            // The shop must present the real-money flow, not the retired bank-transfer flow.
            string[] expectedTabs = { "BUY GEMS", "PREMIUM STORE", "TRANSACTION HISTORY" };
            Text[] texts = gemShop.GetComponentsInChildren<Text>(true);
            foreach (string label in expectedTabs)
            {
                Text match = null;
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i].text == label) { match = texts[i]; break; }
                }
                Assert.IsNotNull(match, "GEM SHOP tab missing: " + label);
            }
            string[] retiredCopy = { "BANK TRANSFER", "UPLOAD", "PROOF OF PAYMENT", "ORDER STATUS" };
            for (int i = 0; i < texts.Length; i++)
            {
                string value = texts[i].text != null ? texts[i].text.ToUpperInvariant() : "";
                for (int r = 0; r < retiredCopy.Length; r++)
                {
                    Assert.IsFalse(value.Contains(retiredCopy[r]),
                        "Retired bank-transfer copy still visible on GEM SHOP: " + texts[i].text);
                }
            }

            // With no premium backend configured the history tab must say so instead of
            // showing fake transactions.
            selectTab.Invoke(gemShop, new object[] { GemShopScreen.GemShopTab.Orders });
            yield return null;
            texts = gemShop.GetComponentsInChildren<Text>(true);
            bool offlineHistory = false;
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].text != null && texts[i].text.Contains("STORE OFFLINE") &&
                    texts[i].text.Contains("Transaction history"))
                    offlineHistory = true;
            }
            Assert.IsTrue(offlineHistory, "Offline transaction history must explain the store is unavailable");
            app.NavigateBack();
            yield return null;
            Assert.AreEqual("MainMenu", app.CurrentScreenName);

            app.ShowUpgrades();
            yield return null;
            Assert.AreEqual("Upgrades", app.CurrentScreenName);
            Assert.IsNotNull(app.UI.CurrentScreen.GetComponent<UpgradeScreen>());
            app.NavigateBack();
            yield return null;
            Assert.AreEqual("MainMenu", app.CurrentScreenName);

            app.ShowCharacter();
            yield return null;
            Assert.AreEqual("Character", app.CurrentScreenName);
            Assert.IsNotNull(app.UI.CurrentScreen.GetComponent<CharacterScreen>());
            app.NavigateBack();
            yield return null;
            yield return null;
            Assert.AreEqual("MainMenu", app.CurrentScreenName);
        }

        [UnityTest]
        public IEnumerator MainMenuShowsEveryDestinationLabel()
        {
            yield return WaitForApp();
            var app = GameApp.Instance;
            app.ShowMainMenu();
            yield return null;
            yield return null;

            var screen = app.UI.CurrentScreen;
            Assert.IsNotNull(screen, "Main menu screen missing");
            string[] expected =
            {
                "PLAY", "CONTINUE", "CHARACTER", "GEM SHOP", "UPGRADES",
                "MISSIONS", "MULTIPLAYER", "RANKINGS", "EVENTS", "ACCOUNT",
                "WORLD ATLAS", "ACHIEVEMENTS", "SETTINGS", "EXIT"
            };
            Text[] texts = screen.GetComponentsInChildren<Text>(true);
            foreach (string label in expected)
            {
                Text match = null;
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i].text == label) { match = texts[i]; break; }
                }
                Assert.IsNotNull(match, "Main-menu label missing: " + label);
                Assert.IsTrue(match.isActiveAndEnabled, "Main-menu label inactive: " + label);
                Assert.Greater(match.color.a, 0.5f, "Main-menu label is transparent: " + label);
                var rect = (RectTransform)match.transform;
                Assert.Greater(rect.rect.width, 40f, "Main-menu label is horizontally clipped: " + label);
                Assert.Greater(rect.rect.height, 12f, "Main-menu label is vertically clipped: " + label);
            }
        }

        [UnityTest]
        public IEnumerator AccountEventsAtlasAndAchievementsBuildAndReturnToMenu()
        {
            yield return WaitForApp();
            var app = GameApp.Instance;
            app.ShowMainMenu();
            yield return null;

            app.ShowAccount(AccountScreen.TabSignIn);
            yield return null;
            Assert.AreEqual("Account", app.CurrentScreenName);
            var account = app.UI.CurrentScreen.GetComponent<AccountScreen>();
            Assert.IsNotNull(account, "ACCOUNT screen component missing");
            var selectTab = typeof(AccountScreen).GetMethod("SelectTab",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(selectTab, "ACCOUNT tab entry point changed without updating the QA test");
            foreach (int tab in new[] { AccountScreen.TabCreate, AccountScreen.TabRecovery,
                         AccountScreen.TabProfile, AccountScreen.TabSignIn })
            {
                selectTab.Invoke(account, new object[] { tab });
                yield return null;
                Assert.IsTrue(account.isActiveAndEnabled);
            }
            app.NavigateBack();
            yield return null;
            Assert.AreEqual("MainMenu", app.CurrentScreenName);

            app.ShowEvents();
            yield return null;
            Assert.AreEqual("Events", app.CurrentScreenName);
            Assert.IsNotNull(app.UI.CurrentScreen.GetComponent<EventsScreen>());
            Text[] eventTexts = app.UI.CurrentScreen.GetComponentsInChildren<Text>(true);
            bool hasDaily = false;
            for (int i = 0; i < eventTexts.Length; i++)
                if (eventTexts[i].text == "DAILY SIGNAL") hasDaily = true;
            Assert.IsTrue(hasDaily, "EVENT OPERATIONS must list the daily signal");
            app.NavigateBack();
            yield return null;
            Assert.AreEqual("MainMenu", app.CurrentScreenName);

            app.ShowDiscovery();
            yield return null;
            Assert.AreEqual("Discovery", app.CurrentScreenName);
            Assert.IsNotNull(app.UI.CurrentScreen.GetComponent<DiscoveryScreen>());
            app.NavigateBack();
            yield return null;
            Assert.AreEqual("MainMenu", app.CurrentScreenName);

            app.ShowAchievements();
            yield return null;
            Assert.AreEqual("Achievements", app.CurrentScreenName);
            Assert.IsNotNull(app.UI.CurrentScreen.GetComponent<AchievementsScreen>());
            app.NavigateBack();
            yield return null;
            Assert.AreEqual("MainMenu", app.CurrentScreenName);
        }

        [UnityTest]
        public IEnumerator HudScoreAndDistanceTickersTrackTheRunIndependently()
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
            deadline = Time.realtimeSinceStartup + 10f;
            while (app.Session.Runner != null && !app.Session.Runner.Running &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return new WaitForSecondsRealtime(2.5f);

            Text score = null;
            Text distance = null;
            Text[] texts = app.UI.Hud.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].name == "Score") score = texts[i];
                else if (texts[i].name == "Distance") distance = texts[i];
            }
            Assert.IsNotNull(score, "HUD score label missing");
            Assert.IsNotNull(distance, "HUD distance label missing");
            Assert.Greater(app.Session.Distance, 2f, "Run did not advance far enough to populate the HUD");
            Assert.AreNotEqual("0", score.text, "HUD score ticker did not update");
            Assert.AreNotEqual("0 m", distance.text, "HUD distance ticker did not update");
            Assert.AreNotEqual(score.text, distance.text,
                "HUD score and distance must use independent tickers");

            app.EndSession(false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PauseHidesHudAndResumeRestoresIt()
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
            Assert.IsNotNull(app.UI.Hud, "HUD missing before pause");
            app.UI.Toast("TEST TOAST", Color.white, 5f);
            yield return null;
            Assert.Greater(app.UI.ToastLayer.childCount, 0, "Test toast was not created");

            app.ShowPause();
            yield return null;
            Assert.IsFalse(app.UI.Hud.gameObject.activeSelf,
                "HUD toasts and telemetry must be hidden while the pause panel is open");
            int activeToasts = 0;
            for (int i = 0; i < app.UI.ToastLayer.childCount; i++)
                if (app.UI.ToastLayer.GetChild(i).gameObject.activeSelf) activeToasts++;
            Assert.AreEqual(0, activeToasts,
                "In-flight toasts must be hidden when the pause panel opens");

            app.ResumeFromPause();
            yield return null;
            Assert.IsNotNull(app.UI.Hud, "HUD was destroyed by the pause flow");
            Assert.IsTrue(app.UI.Hud.gameObject.activeSelf, "HUD must return when the run resumes");

            app.EndSession(false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PremiumEndpointChangeClearsCachedWalletAndDoesNotTouchCredits()
        {
            yield return WaitForApp();
            var app = GameApp.Instance;
            string previousUrl = app.Profile.settings.premiumServerUrl;
            string previousToken = app.Profile.premium.token;
            int previousGems = app.Profile.premium.gems;
            int previousOrders = app.Profile.premium.pendingOrders;
            string previousCatalog = app.Profile.premium.catalogJson;
            string previousSync = app.Profile.premium.lastSyncUtc;
            var previousOwned = new List<PremiumOwnership>(app.Profile.premium.ownedItems);
            var previousCharacters = new List<string>(app.Profile.premium.characters);
            var previousUpgrades = new List<PremiumUpgradeEntry>(app.Profile.premium.upgrades);
            int creditsBefore = app.Profile.credits;
            try
            {
                app.Profile.premium.gems = 777;
                app.Profile.premium.pendingOrders = 3;
                app.Profile.premium.token = "stale-token";
                app.Profile.settings.premiumServerUrl = "https://premium.example.com/";

                app.Premium.Reconfigure(false);

                Assert.AreEqual("https://premium.example.com", app.Premium.ServerUrl,
                    "Trailing slashes must be normalised from the configured endpoint");
                Assert.IsFalse(app.Premium.Online);
                Assert.AreEqual(0, app.Profile.premium.gems,
                    "Cached GEMS from another authority must never be shown as current");
                Assert.AreEqual(0, app.Profile.premium.pendingOrders);
                Assert.AreEqual("", app.Profile.premium.token);
                Assert.AreEqual(creditsBefore, app.Profile.credits,
                    "Changing the premium endpoint must not touch earned CREDITS");
            }
            finally
            {
                app.Profile.settings.premiumServerUrl = previousUrl;
                app.Premium.Reconfigure(false);
                app.Profile.premium.token = previousToken;
                app.Profile.premium.gems = previousGems;
                app.Profile.premium.pendingOrders = previousOrders;
                app.Profile.premium.catalogJson = previousCatalog;
                app.Profile.premium.lastSyncUtc = previousSync;
                app.Profile.premium.ownedItems.Clear();
                app.Profile.premium.ownedItems.AddRange(previousOwned);
                app.Profile.premium.characters.Clear();
                app.Profile.premium.characters.AddRange(previousCharacters);
                app.Profile.premium.upgrades.Clear();
                app.Profile.premium.upgrades.AddRange(previousUpgrades);
                app.Premium.Initialise(app);
                app.SaveProfile();
            }
            yield return null;
        }
    }
}
