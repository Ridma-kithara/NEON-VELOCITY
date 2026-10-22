using System;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Title screen. The 3D menu stage renders behind this translucent layout, so the player
    /// immediately sees the world, their selected runner and the live city.
    /// </summary>
    public sealed class MainMenuScreen : MonoBehaviour
    {
        public static GameObject Build(GameApp app, UIRoot ui)
        {
            var root = ScreenKit.Root("Main Menu");
            var screen = root.gameObject.AddComponent<MainMenuScreen>();
            screen.BuildLayout(app, ui);
            return root.gameObject;
        }

        private GameApp _app;
        private Text _currency;
        private Text _dailyLabel;
        private Button _dailyButton;

        private void BuildLayout(GameApp app, UIRoot ui)
        {
            _app = app;
            var root = (RectTransform)transform;
            ScreenKit.Backdrop(root, UITheme.Cyan, true);

            // Left rail: the menu lives over the city rather than hiding it.
            var rail = UITheme.Img(root, "Menu Rail", new Color(0.012f, 0.025f, 0.05f, 0.84f));
            UITheme.Anchor((RectTransform)rail.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(640f, 0f));
            rail.raycastTarget = true;

            var accentLine = UITheme.Img(rail.transform, "Rail Edge", UITheme.Cyan);
            UITheme.Anchor((RectTransform)accentLine.transform, new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-3f, 0f), Vector2.zero);

            var studio = UITheme.Label(rail.transform, "Studio", "RK VANTAGE GAMES", UITheme.SmallSize,
                UITheme.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)studio.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(48f, -58f), new Vector2(-40f, -22f));

            var title = UITheme.Label(rail.transform, "Title", "NEON\nVELOCITY", 82, UITheme.White,
                TextAnchor.UpperLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(44f, -222f), new Vector2(-40f, -66f));
            UITheme.AddShadow(title, new Color(0f, 0f, 0f, 0.65f), new Vector2(2f, -4f));

            var tagline = UITheme.Label(rail.transform, "Tagline", "OUTRUN THE BLACKOUT.", UITheme.BodySize,
                UITheme.Magenta, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)tagline.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(48f, -262f), new Vector2(-40f, -228f));

            var season = UITheme.Img(rail.transform, "Season", UITheme.PanelSoft, UITheme.Rounded, false);
            UITheme.Anchor((RectTransform)season.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(48f, -320f), new Vector2(302f, -274f));
            var seasonText = UITheme.Label(season.transform, "Label", "SEASON 01  //  NEON MONSOON",
                UITheme.SmallSize, UITheme.Lime, TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Stretch((RectTransform)seasonText.transform, 8f, 3f, 8f, 3f);

            BuildMenu(root);

            // Right column: runner profile and daily supply.
            BuildProfile(root);
            BuildDaily(root);
            BuildSeason(root);

            var footer = UITheme.Label(root, "Footer", "v" + Application.version + "   \u00B7   OFFLINE READY   \u00B7   " +
                "MULTIPLAYER 5P", UITheme.SmallSize, UITheme.Muted, TextAnchor.MiddleRight);
            UITheme.Anchor((RectTransform)footer.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(680f, 20f), new Vector2(-32f, 48f));

            _currency = UITheme.Label(root, "Currency", "", UITheme.BodySize, UITheme.Amber,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_currency.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-460f, -64f), new Vector2(-32f, -24f));
            Refresh();
            app.ProfileChanged += Refresh;
        }

        private void BuildMenu(RectTransform root)
        {
            var menu = UITheme.NewRect(root, "Menu");
            UITheme.Anchor(menu, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(48f, -880f), new Vector2(560f, -336f));
            var layout = menu.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 9f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            AddMenuButton(menu, "PLAY", "All solo modes, challenges and training", UITheme.Cyan,
                () => _app.ShowModes());
            AddMenuButton(menu, "NEON CIRCUIT", "Live 5-player race or private room", UITheme.Lime,
                () => _app.ShowMultiplayer());
            AddMenuButton(menu, "RUNNERS & GEAR", "Unlock characters, trails and upgrades", UITheme.Magenta,
                () => _app.ShowShop());
            AddMenuButton(menu, "OPERATIONS", "Daily and weekly mission board", UITheme.Amber,
                () => _app.ShowMissions());
            AddMenuButton(menu, "RANKINGS", "Your records and circuit results", UITheme.Violet,
                () => _app.ShowLeaderboard());
            AddMenuButton(menu, "SETTINGS", "Audio, input, accessibility, server", UITheme.Muted,
                () => _app.ShowSettings(false));

            var bottomRow = UITheme.NewRect(menu, "Bottom Row");
            UITheme.Layout(bottomRow.gameObject, 54f, -1f, 54f);
            var tutorial = UITheme.Button(bottomRow, "Tutorial", "FIRST CONTACT", UITheme.Lime,
                () => { _app.Audio.PlayUiClick(); _app.StartMode(GameModeId.Tutorial); }, UITheme.SmallSize);
            UITheme.Anchor((RectTransform)tutorial.transform, new Vector2(0f, 0f), new Vector2(0.6f, 1f),
                Vector2.zero, new Vector2(-6f, 0f));
            var quit = UITheme.Button(bottomRow, "Quit", "QUIT", UITheme.Red, _app.QuitApplication,
                UITheme.SmallSize);
            UITheme.Anchor((RectTransform)quit.transform, new Vector2(0.6f, 0f), new Vector2(1f, 1f),
                new Vector2(6f, 0f), Vector2.zero);
        }

        private void AddMenuButton(RectTransform parent, string title, string subtitle, Color accent, Action action)
        {
            var button = ScreenKit.MenuButton(parent, title, subtitle, accent, () =>
            {
                _app.Audio.PlayUiClick();
                action();
            });
            UITheme.Layout(button.gameObject, 70f, -1f, 70f);
        }

        private void BuildProfile(RectTransform root)
        {
            var panel = ScreenKit.Card(root, "Runner Card", new Color(0.025f, 0.05f, 0.09f, 0.9f));
            UITheme.Anchor((RectTransform)panel.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-600f, -522f), new Vector2(-36f, -96f));
            var accent = UITheme.Img(panel.transform, "Accent", UITheme.Cyan);
            UITheme.Anchor((RectTransform)accent.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 18f), new Vector2(4f, -18f));

            var header = UITheme.Label(panel.transform, "Header", "ACTIVE RUNNER", UITheme.SmallSize,
                UITheme.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)header.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -46f), new Vector2(-24f, -14f));

            var profile = _app.Profile;
            var character = _app.Config.Character(profile.selectedCharacter);
            var name = UITheme.Label(panel.transform, "Name",
                profile.playerName + "  //  " + (character != null ? character.displayName : "RUNNER"),
                32, UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -92f), new Vector2(-24f, -48f));

            var callsign = UITheme.Label(panel.transform, "Callsign",
                character != null ? character.callsign + " \u00B7 " + character.perkLabel : "VANTA COURIER",
                UITheme.SmallSize, UITheme.Dim, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)callsign.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -120f), new Vector2(-24f, -94f));

            var level = UITheme.Label(panel.transform, "Level",
                "LEVEL " + profile.level + "   " + ProfileXpLabel(profile), UITheme.SmallSize, UITheme.Lime,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)level.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -156f), new Vector2(-24f, -126f));
            ScreenKit.ProgressBar(panel.transform, "Xp Bar", ProgressionMath.LevelProgress(profile.xp),
                UITheme.Lime, out _, new Vector2(28f, 116f), new Vector2(-28f, 124f));

            var best = UITheme.Label(panel.transform, "Best",
                "BEST RUN  " + UITheme.FormatDistance(profile.bestDistance) + "     SCORE  " +
                UITheme.FormatNumber(profile.bestScore), UITheme.SmallSize, UITheme.White,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)best.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(28f, 72f), new Vector2(-24f, 104f));

            var record = UITheme.Label(panel.transform, "Record",
                "RACES " + profile.lifetime.racesCompleted + "   WINS " + profile.lifetime.racesWon +
                "   RUNS " + profile.lifetime.totalRuns, UITheme.SmallSize, UITheme.Muted,
                TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)record.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(28f, 40f), new Vector2(-24f, 70f));

            var edit = UITheme.Button(panel.transform, "Edit", "EDIT CALLSIGN", UITheme.Muted,
                () => _app.ShowSettings(false), UITheme.SmallSize);
            UITheme.Anchor((RectTransform)edit.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-218f, 36f), new Vector2(-24f, 74f));
        }

        private static string ProfileXpLabel(SaveData profile)
        {
            int needed = ProgressionMath.XpRequiredForLevel(profile.level);
            int into = ProgressionMath.XpIntoCurrentLevel(profile.xp);
            return into + " / " + needed + " XP";
        }

        private void BuildDaily(RectTransform root)
        {
            var panel = ScreenKit.Card(root, "Daily Card", new Color(0.04f, 0.07f, 0.12f, 0.92f));
            UITheme.Anchor((RectTransform)panel.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-600f, 86f), new Vector2(-36f, 308f));
            var accent = UITheme.Img(panel.transform, "Accent", UITheme.Amber);
            UITheme.Anchor((RectTransform)accent.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 18f), new Vector2(4f, -18f));

            var header = UITheme.Label(panel.transform, "Header", "DAILY SUPPLY DROP", UITheme.SmallSize,
                UITheme.Amber, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)header.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -46f), new Vector2(-24f, -14f));

            _dailyLabel = UITheme.Label(panel.transform, "Body", "", UITheme.BodySize, UITheme.White,
                TextAnchor.UpperLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_dailyLabel.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(28f, 74f), new Vector2(-24f, -52f));

            _dailyButton = UITheme.Button(panel.transform, "Claim", "CLAIM SUPPLY", UITheme.Amber,
                ClaimDaily, UITheme.BodySize);
            UITheme.Anchor((RectTransform)_dailyButton.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(28f, 18f), new Vector2(-28f, 68f));
        }

        private void BuildSeason(RectTransform root)
        {
            var panel = ScreenKit.Card(root, "Season Card", new Color(0.04f, 0.07f, 0.12f, 0.86f));
            UITheme.Anchor((RectTransform)panel.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-600f, 322f), new Vector2(-36f, 500f));
            var profile = _app.Profile;
            var header = UITheme.Label(panel.transform, "Header", "SEASON 01 PROGRESS", UITheme.SmallSize,
                UITheme.Lime, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)header.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -42f), new Vector2(-24f, -12f));
            var body = UITheme.Label(panel.transform, "Body",
                "MONSOON LEVEL " + profile.seasonLevel + "\n" + UITheme.FormatNumber(profile.seasonXp) +
                " SEASON XP", UITheme.BodySize, UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)body.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(26f, 18f), new Vector2(-24f, -48f));
            var badge = UITheme.Badge(panel.transform, "Badge", "EVENT LIVE", UITheme.Lime, 128, 28);
            UITheme.Anchor((RectTransform)badge.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-160f, -48f), new Vector2(-26f, -20f));
        }

        private void ClaimDaily()
        {
            var result = _app.Progression.ClaimDailyReward(DateTime.UtcNow);
            _app.Audio.PlayUiClick();
            _app.UI.Toast(result.Message, result.Success ? UITheme.Amber : UITheme.Muted, 3f);
            if (result.Success) _app.SaveProfile();
            Refresh();
        }

        private void Refresh()
        {
            if (_app == null || _app.Profile == null) return;
            var profile = _app.Profile;
            if (_currency != null) ScreenKit.RefreshCurrency(_currency, _app);
            if (_dailyLabel != null)
            {
                string today = DateTime.UtcNow.ToString("O");
                bool canClaim = DailyRewards.CanClaim(profile.lastDailyClaimUtc, today);
                int nextStreak = canClaim
                    ? DailyRewards.StreakAfterClaim(profile.lastDailyClaimUtc, today, profile.dailyStreak)
                    : profile.dailyStreak;
                if (nextStreak <= 0) nextStreak = 1;
                _dailyLabel.text = canClaim
                    ? "DAY " + nextStreak + " READY\n+" + DailyRewards.CreditsForStreak(nextStreak) +
                      " CREDITS ON CLAIM"
                    : "STREAK " + profile.dailyStreak + " / 7\nNEXT DROP AT 00:00 UTC";
                if (_dailyButton != null)
                {
                    _dailyButton.interactable = canClaim;
                    var text = _dailyButton.GetComponentInChildren<Text>();
                    if (text != null) text.text = canClaim ? "CLAIM SUPPLY" : "ALREADY CLAIMED";
                }
            }
        }

        private void OnDestroy()
        {
            if (_app != null) _app.ProfileChanged -= Refresh;
        }
    }
}
