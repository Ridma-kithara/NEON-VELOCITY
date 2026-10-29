using System;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Title screen. The 3D menu stage renders behind this translucent layout, so the player
    /// sees their runner standing on the rain-slick street amid the live city. The rail owns
    /// navigation, a slim right column keeps profile stats, and the hero stays framed through
    /// the gap between the two.
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
        private NumberTicker _currencyTicker;
        private Text _gemBalance;
        private NumberTicker _gemTicker;
        private Text _dailyLabel;
        private Button _dailyButton;
        private Button _continueButton;
        private Text _continueLabel;
        private Text _continueSub;
        private PulseBar _seasonBar;
        private Text _seasonLabel;

        private void BuildLayout(GameApp app, UIRoot ui)
        {
            _app = app;
            var root = (RectTransform)transform;
            ScreenKit.Backdrop(root, UITheme.Cyan, true);

            // Left rail: the menu lives over the city rather than hiding it.
            var rail = UITheme.Img(root, "Menu Rail", new Color(0.012f, 0.025f, 0.05f, 0.84f));
            UITheme.Anchor((RectTransform)rail.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(600f, 0f));
            rail.raycastTarget = true;

            var accentLine = UITheme.Img(rail.transform, "Rail Edge", UITheme.Cyan);
            UITheme.Anchor((RectTransform)accentLine.transform, new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-3f, 0f), Vector2.zero);
            var railGlow = UITheme.Img(rail.transform, "Rail Glow", UITheme.Cyan.Alpha(0.05f), UITheme.Glow, false);
            UITheme.Anchor((RectTransform)railGlow.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-320f, -420f), new Vector2(320f, 420f));

            var studio = UITheme.Label(rail.transform, "Studio", "RK VANTAGE GAMES", UITheme.SmallSize,
                UITheme.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)studio.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(48f, -58f), new Vector2(-40f, -22f));

            var title = UITheme.Label(rail.transform, "Title", "NEON\nVELOCITY", 78, UITheme.White,
                TextAnchor.UpperLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(44f, -270f), new Vector2(-40f, -64f));
            UITheme.AddShadow(title, new Color(0f, 0f, 0f, 0.65f), new Vector2(2f, -4f));
            var titleReflection = UITheme.Label(rail.transform, "Title Echo", "NEON VELOCITY", 78,
                UITheme.Cyan.Alpha(0.14f), TextAnchor.UpperLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)titleReflection.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(44f, -288f), new Vector2(-40f, -82f));

            var tagline = UITheme.Label(rail.transform, "Tagline", "OUTRUN THE BLACKOUT.", UITheme.BodySize,
                UITheme.Magenta, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)tagline.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(48f, -308f), new Vector2(-40f, -274f));

            var seasonBadge = UITheme.Img(rail.transform, "Season", UITheme.PanelSoft, UITheme.Rounded, false);
            UITheme.Anchor((RectTransform)seasonBadge.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(48f, -364f), new Vector2(400f, -318f));
            var seasonText = UITheme.Label(seasonBadge.transform, "Label", "SEASON 01  //  NEON MONSOON",
                UITheme.SmallSize, UITheme.Lime, TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Stretch((RectTransform)seasonText.transform, 8f, 3f, 8f, 3f);

            BuildMenu(root);

            // Right column: slim glass cards keep the hero visible in the gap.
            BuildProfile(root);
            BuildSeasonCard(root);
            BuildDaily(root);

            var footer = UITheme.Label(root, "Footer", "v" + Application.version + "   \u00B7   OFFLINE READY   \u00B7   " +
                "RACE 5P ONLINE", UITheme.SmallSize, UITheme.Muted, TextAnchor.MiddleRight);
            UITheme.Anchor((RectTransform)footer.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(660f, 20f), new Vector2(-32f, 48f));

            _currency = UITheme.Label(root, "Currency", "", UITheme.BodySize, UITheme.Amber,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_currency.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-470f, -66f), new Vector2(-34f, -26f));
            _currencyTicker = NumberTicker.Attach(gameObject, _currency, app.Profile.credits, FormatCredits);
            BuildGemChip(root);
            Refresh();
            app.ProfileChanged += Refresh;
        }

        private static string FormatCredits(long value)
        {
            return "\u25C6 " + value.ToString("N0") + " CREDITS";
        }

        private static string FormatGems(long value)
        {
            return "\u25C6 " + value.ToString("N0") + " GEMS";
        }

        /// <summary>
        /// Tappable GEM balance chip beside the CREDITS readout. It opens the premium wallet
        /// and ticks from the cached PremiumCache so the menu shows the real balance offline.
        /// </summary>
        private void BuildGemChip(RectTransform root)
        {
            int gems = _app.Profile.premium != null ? _app.Profile.premium.gems : 0;
            var chip = UITheme.Button(root, "Gem Balance", FormatGems(gems), UITheme.Magenta,
                () =>
                {
                    _app.Audio.PlayUiClick();
                    _app.ShowGemShop();
                }, UITheme.BodySize);
            var chipImage = chip.GetComponent<Image>();
            if (chipImage != null) chipImage.color = new Color(0.05f, 0.1f, 0.16f, 0.92f);
            UITheme.Anchor((RectTransform)chip.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-716f, -66f), new Vector2(-476f, -26f));
            _gemBalance = chip.GetComponentInChildren<Text>();
            if (_gemBalance != null)
            {
                _gemBalance.color = UITheme.Magenta;
                _gemBalance.fontSize = Mathf.Max(18, UITheme.BodySize - 4);
                _gemTicker = NumberTicker.Attach(gameObject, _gemBalance, gems, FormatGems);
            }
            var accent = UITheme.Img(chip.transform, "Chip Accent", UITheme.Magenta);
            UITheme.Anchor((RectTransform)accent.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(8f, -9f), new Vector2(13f, 9f));
            accent.raycastTarget = false;
        }

        private void BuildMenu(RectTransform root)
        {
            var menu = UITheme.NewRect(root, "Menu");
            UITheme.Anchor(menu, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(48f, -1010f), new Vector2(560f, -398f));
            var layout = menu.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            AddMenuButton(menu, "PLAY", "SOLO MODES \u00B7 CHALLENGES \u00B7 TRAINING", UITheme.Cyan,
                () => _app.ShowModes());            BuildContinueButton(menu);

            // Secondary destinations share a 2-up grid to keep the hero lane unobstructed.
            var grid1 = AddGridRow(menu);
            AddGridButton(grid1, "CHARACTER", "ROSTER \u00B7 POSES", UITheme.Magenta, () => _app.ShowCharacter());
            AddGridButton(grid1, "GEM SHOP", "BOOST GEMS", UITheme.Amber, () => _app.ShowGemShop());
            var grid2 = AddGridRow(menu);
            AddGridButton(grid2, "UPGRADES", "RUNNER TECH", UITheme.Lime, () => _app.ShowUpgrades());
            AddGridButton(grid2, "MISSIONS", "DAILY \u00B7 WEEKLY", UITheme.Amber, () => _app.ShowMissions());
            var grid3 = AddGridRow(menu);
            AddGridButton(grid3, "MULTIPLAYER", "RACE 5P", UITheme.Lime, () => _app.ShowMultiplayer());
            AddGridButton(grid3, "RANKINGS", "RECORDS", UITheme.Violet, () => _app.ShowLeaderboard());
            var grid4 = AddGridRow(menu);
            AddGridButton(grid4, "EVENTS", "CALENDAR", UITheme.Violet, () => _app.ShowEvents());
            bool signedIn = _app.Account != null && _app.Account.SignedIn;
            AddGridButton(grid4, "ACCOUNT", signedIn ? "SIGNED IN \u00B7 RECEIPTS" : "SIGN IN \u00B7 SYNC",
                UITheme.Cyan, () => _app.ShowAccount(signedIn ? 3 : 0));
            var grid5 = AddGridRow(menu);
            AddGridButton(grid5, "WORLD ATLAS", "21 DISTRICTS", UITheme.Lime, () => _app.ShowDiscovery());
            AddGridButton(grid5, "ACHIEVEMENTS", "CAREER", UITheme.Amber, () => _app.ShowAchievements());

            var bottomRow = UITheme.NewRect(menu, "Bottom Row");
            UITheme.Layout(bottomRow.gameObject, 48f, -1f, 48f);
            var settings = UITheme.Button(bottomRow, "Settings", "SETTINGS", UITheme.Muted,
                () => _app.ShowSettings(false), UITheme.SmallSize);
            UITheme.Anchor((RectTransform)settings.transform, new Vector2(0f, 0f), new Vector2(0.6f, 1f),
                Vector2.zero, new Vector2(-6f, 0f));
            var quit = UITheme.Button(bottomRow, "Quit", "EXIT", UITheme.Red, () =>
            {
                _app.Audio.PlayUiClick();
                _app.QuitApplication();
            }, UITheme.SmallSize);
            UITheme.Anchor((RectTransform)quit.transform, new Vector2(0.6f, 0f), new Vector2(1f, 1f),
                new Vector2(6f, 0f), Vector2.zero);
        }

        private static RectTransform AddGridRow(RectTransform menu)
        {
            var row = UITheme.NewRect(menu, "Grid Row");
            UITheme.Layout(row.gameObject, 64f, -1f, 64f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return row;
        }

        private void AddGridButton(RectTransform row, string title, string subtitle, Color accent, Action action)
        {
            var button = ScreenKit.MenuButton(row, title, subtitle, accent, () =>
            {
                _app.Audio.PlayUiClick();
                action();
            }, true, true);
            // HorizontalLayoutGroup sizes children from their preferred height. Without this,
            // grid buttons collapse to their intrinsic text height and the compact insets
            // squeeze the title rect to one pixel.
            UITheme.Layout(button.gameObject, 64f, -1f, 64f);
            // Softer glass fill keeps grid rows subordinate to PLAY/CONTINUE.
            var image = button.GetComponent<Image>();
            if (image != null) image.color = UITheme.PanelSoft;
        }

        private void AddMenuButton(RectTransform parent, string title, string subtitle, Color accent, Action action,
            float height = 80f)
        {
            var button = ScreenKit.MenuButton(parent, title, subtitle, accent, () =>
            {
                _app.Audio.PlayUiClick();
                action();
            });
            UITheme.Layout(button.gameObject, height, -1f, height);
        }

        private void BuildContinueButton(RectTransform menu)
        {
            var modeId = _app.Profile.lastModeId;
            GameModeId parsed = default;
            bool canContinue = !string.IsNullOrEmpty(modeId) && Enum.TryParse(modeId, true, out parsed)
                && !GameModes.IsOnline(parsed) && parsed != GameModeId.Tutorial;
            var spec = canContinue ? GameModes.Get(parsed) : null;

            _continueButton = ScreenKit.MenuButton(menu, "CONTINUE",
                canContinue ? (spec != null ? spec.Title : "LAST RUN") + BuildContinueSuffix(spec, canContinue) : "NO SAVED RUN \u2014 START WITH PLAY",
                UITheme.Magenta, () => { if (canContinue) _app.StartMode(parsed); }, canContinue);
            UITheme.Layout(_continueButton.gameObject, 80f, -1f, 80f);
            var texts = _continueButton.GetComponentsInChildren<Text>(true);
            foreach (var t in texts)
            {
                if (t.name == "Sub") _continueSub = t;
                if (t.name == "Label") _continueLabel = t;
            }
            if (!canContinue && _continueButton.targetGraphic != null)
            {
                _continueButton.targetGraphic.canvasRenderer.SetAlpha(0.55f);
            }
        }

        private static string BuildContinueSuffix(ModeSpec spec, bool canContinue)
        {
            if (canContinue && spec != null)
            {
                var best = GameApp.Instance.Profile.FindModeBest(spec.Id.ToString());
                if (best != null && best.distance > 0f)
                    return "  \u00B7  BEST " + UITheme.FormatDistance(best.distance);
            }
            return string.Empty;
        }

        private void BuildProfile(RectTransform root)
        {
            var border = UITheme.GlassCard(root, "Runner Card", new Color(0.025f, 0.05f, 0.09f, 0.9f));
            var panel = (RectTransform)border.transform.parent;
            UITheme.Anchor(panel, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-540f, -560f), new Vector2(-36f, -108f));
            var accent = UITheme.Img(panel.transform, "Accent", UITheme.Cyan);
            UITheme.Anchor((RectTransform)accent.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 16f), new Vector2(4f, -16f));

            var header = UITheme.Label(panel.transform, "Header", "ACTIVE RUNNER", UITheme.SmallSize,
                UITheme.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)header.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -44f), new Vector2(-24f, -14f));

            var profile = _app.Profile;
            var character = _app.Config.Character(profile.selectedCharacter);
            var name = UITheme.Label(panel.transform, "Name",
                profile.playerName + "  //  " + (character != null ? character.displayName : "RUNNER"),
                30, UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -88f), new Vector2(-24f, -46f));

            var callsign = UITheme.Label(panel.transform, "Callsign",
                character != null ? character.callsign + " \u00B7 " + character.perkLabel : "VANTA COURIER",
                UITheme.SmallSize, UITheme.Dim, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)callsign.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -116f), new Vector2(-24f, -90f));

            var level = UITheme.Label(panel.transform, "Level",
                "LEVEL " + profile.level + "   " + ProfileXpLabel(profile), UITheme.SmallSize, UITheme.Lime,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)level.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -150f), new Vector2(-24f, -120f));
            ScreenKit.ProgressBar(panel.transform, "Xp Bar", ProgressionMath.LevelProgress(profile.totalXp),
                UITheme.Lime, out _, new Vector2(28f, 114f), new Vector2(-28f, 122f));

            var best = UITheme.Label(panel.transform, "Best",
                "BEST RUN  " + UITheme.FormatDistance(profile.bestDistance) + "     SCORE  " +
                UITheme.FormatNumber(profile.bestScore), UITheme.SmallSize, UITheme.White,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)best.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(28f, 70f), new Vector2(-24f, 102f));

            var record = UITheme.Label(panel.transform, "Record",
                "RACES " + profile.lifetime.racesCompleted + "   WINS " + profile.lifetime.racesWon +
                "   RUNS " + profile.lifetime.totalRuns, UITheme.SmallSize, UITheme.Muted,
                TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)record.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(28f, 40f), new Vector2(-24f, 68f));

            var edit = UITheme.Button(panel.transform, "Edit", "EDIT CALLSIGN", UITheme.Muted,
                () => _app.ShowSettings(false), UITheme.SmallSize);
            UITheme.Anchor((RectTransform)edit.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-216f, 34f), new Vector2(-24f, 72f));
        }

        private static string ProfileXpLabel(SaveData profile)
        {
            long needed = ProgressionMath.XpRequiredForLevel(profile.level);
            long into = ProgressionMath.XpIntoCurrentLevel(profile.totalXp);
            return UITheme.FormatNumber(into) + " / " + UITheme.FormatNumber(needed) + " XP";
        }

        private void BuildSeasonCard(RectTransform root)
        {
            var border = UITheme.GlassCard(root, "Season Card", new Color(0.04f, 0.07f, 0.12f, 0.88f));
            var panel = (RectTransform)border.transform.parent;
            UITheme.Anchor(panel, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-540f, -700f), new Vector2(-36f, -574f));
            var accent = UITheme.Img(panel.transform, "Accent", UITheme.Lime);
            UITheme.Anchor((RectTransform)accent.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 16f), new Vector2(4f, -16f));

            var profile = _app.Profile;
            var header = UITheme.Label(panel.transform, "Header", "SEASON 01 PROGRESS \u2014 MONSOON LV " +
                profile.seasonLevel, UITheme.SmallSize, UITheme.Lime, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)header.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -42f), new Vector2(-24f, -12f));

            _seasonLabel = UITheme.Label(panel.transform, "Body", "", UITheme.SmallSize, UITheme.White,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_seasonLabel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -76f), new Vector2(-24f, -46f));

            ScreenKit.ProgressBar(panel.transform, "Season Bar", SeasonProgress(profile),
                UITheme.Lime, out var fill, new Vector2(26f, 18f), new Vector2(-26f, 46f));
            _seasonBar = PulseBar.Attach(fill);
            _seasonBar.Snap(SeasonProgress(profile));
        }

        private static float SeasonProgress(SaveData profile)
        {
            // Season levels advance every 500 season XP; show progress inside the level.
            return Mathf.Clamp01((profile.seasonXp % 500) / 500f);
        }

        private void BuildDaily(RectTransform root)
        {
            var border = UITheme.GlassCard(root, "Daily Card", new Color(0.04f, 0.07f, 0.12f, 0.92f));
            var panel = (RectTransform)border.transform.parent;
            UITheme.Anchor(panel, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-540f, 88f), new Vector2(-36f, 300f));
            var accent = UITheme.Img(panel.transform, "Accent", UITheme.Amber);
            UITheme.Anchor((RectTransform)accent.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 16f), new Vector2(4f, -16f));

            var header = UITheme.Label(panel.transform, "Header", "DAILY SUPPLY DROP", UITheme.SmallSize,
                UITheme.Amber, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)header.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -42f), new Vector2(-24f, -14f));

            _dailyLabel = UITheme.Label(panel.transform, "Body", "", UITheme.BodySize, UITheme.White,
                TextAnchor.UpperLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_dailyLabel.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(28f, 76f), new Vector2(-24f, -50f));

            _dailyButton = UITheme.Button(panel.transform, "Claim", "CLAIM SUPPLY", UITheme.Amber,
                ClaimDaily, UITheme.BodySize);
            UITheme.Anchor((RectTransform)_dailyButton.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(28f, 18f), new Vector2(-28f, 66f));
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
            if (_currencyTicker != null) _currencyTicker.Set(profile.credits);
            if (_gemTicker != null && profile.premium != null) _gemTicker.Set(profile.premium.gems);
            if (_seasonLabel != null)
            {
                _seasonLabel.text = "MONSOON LEVEL " + profile.seasonLevel + "   \u00B7   " +
                    UITheme.FormatNumber(profile.seasonXp) + " SEASON XP";
                if (_seasonBar != null) _seasonBar.Set(SeasonProgress(profile));
            }
            // Continue row tracks profile changes (mode still valid / best improved).
            if (_continueLabel != null && profile.lastModeId != null)
            {
                GameModeId parsed = default;
                bool canContinue = !string.IsNullOrEmpty(profile.lastModeId) &&
                    Enum.TryParse(profile.lastModeId, true, out parsed)
                    && !GameModes.IsOnline(parsed) && parsed != GameModeId.Tutorial;
                var spec = canContinue ? GameModes.Get(parsed) : null;
                if (_continueSub != null)
                    _continueSub.text = canContinue
                        ? (spec != null ? spec.Title : "LAST RUN") + BuildContinueSuffix(spec, canContinue)
                        : "NO SAVED RUN \u2014 START WITH PLAY";
                if (_continueButton != null)
                {
                    _continueButton.interactable = canContinue;
                    if (canContinue && _continueButton.targetGraphic != null)
                        _continueButton.targetGraphic.canvasRenderer.SetAlpha(1f);
                }
            }
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
