using System;
using NeonVelocity.Core;
using NeonVelocity.Premium;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>End-of-run summary, including verification state and every reward granted.</summary>
    public sealed class ResultsScreen : MonoBehaviour, IScreenBackHandler
    {
        private GameApp _app;
        private UIRoot _ui;
        private RunResult _result;
        private Button _reviveButton;
        private Text _reviveLabel;
        private Text _reviveStatus;
        private bool _reviveBusy;

        private int ReviveCost => _app != null && _app.Premium != null
            ? _app.Premium.ReviveCostGems
            : Mathf.Max(1, _result != null ? _result.ReviveCostGems : PremiumService.DefaultReviveCostGems);

        public static GameObject Build(GameApp app, UIRoot ui, RunResult result)
        {
            var root = ScreenKit.Root("Results");
            var screen = root.gameObject.AddComponent<ResultsScreen>();
            screen._app = app;
            screen._ui = ui;
            screen._result = result;
            screen.BuildLayout();
            return root.gameObject;
        }

        private void BuildLayout()
        {
            var root = (RectTransform)transform;
            var accent = !_result.Verified ? UITheme.Red
                : _result.Online && _result.Placement == 1 ? UITheme.Amber
                : _result.Online ? UITheme.Lime : UITheme.Cyan;
            ScreenKit.Backdrop(root, accent);

            var back = UITheme.Button(root, "Back", "\u2039  MAIN MENU", UITheme.Muted,
                () => _app.LeaveResultsToMenu(), UITheme.SmallSize, false);
            UITheme.Anchor((RectTransform)back.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(32f, -82f), new Vector2(212f, -34f));

            var title = UITheme.Label(root, "Eyebrow",
                _result.Online ? "AUTHORITATIVE CIRCUIT RESULT" : "SOLO RUN COMPLETE",
                UITheme.SmallSize, accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -88f), new Vector2(-24f, -52f));

            string headline;
            if (_result.Online && _result.Verified)
                headline = "#" + _result.Placement + "  /  " + _result.TotalPlayers;
            else if (_result.Online)
                headline = "UNVERIFIED";
            else
                headline = UITheme.FormatDistance(_result.Distance);
            var big = UITheme.Label(root, "Headline", headline, 96, UITheme.White, TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UITheme.Anchor((RectTransform)big.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -206f), new Vector2(-24f, -82f));
            UITheme.AddOutline(big, new Color(accent.r, accent.g, accent.b, 0.55f), new Vector2(2f, -2f));

            var mode = UITheme.Label(root, "Mode",
                (_result.ModeTitle ?? "RUN") + "  \u00B7  " + (_result.Crashed ? "SIGNAL LOST" : "CIRCUIT CLEARED"),
                UITheme.BodySize, accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)mode.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -242f), new Vector2(-24f, -208f));

            if (_result.NewBest || _result.LevelUpAchieved)
            {
                bool both = _result.NewBest && _result.LevelUpAchieved;
                float centre = both ? 392f : 130f;
                if (_result.NewBest)
                {
                    var best = UITheme.Badge(root, "New Best", "NEW PERSONAL BEST", UITheme.Amber,
                        both ? 380 : 260, 34);
                    UITheme.Anchor((RectTransform)best.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                        both ? new Vector2(-centre, -284f) : new Vector2(-130f, -284f),
                        both ? new Vector2(-centre + 380f, -250f) : new Vector2(130f, -250f));
                }
                if (_result.LevelUpAchieved)
                {
                    var level = UITheme.Badge(root, "Level Up",
                        "LEVEL UP  \u25B8  RUNNER LV " + _result.NewLevel, UITheme.Lime,
                        both ? 380 : 320, 34);
                    UITheme.Anchor((RectTransform)level.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                        both ? new Vector2(centre - 380f, -284f) : new Vector2(-160f, -284f),
                        both ? new Vector2(centre, -250f) : new Vector2(160f, -250f));
                }
            }

            BuildStats(root);

            var rewards = UITheme.Label(root, "Rewards",
                _result.Online && !_result.Verified
                    ? "NO REWARDS - RESULT NOT VERIFIED"
                    : "+" + UITheme.FormatNumber(_result.CreditsEarned) + " CREDITS      +" +
                      UITheme.FormatNumber(_result.XpEarned) + " XP",
                32, _result.Verified ? UITheme.Amber : UITheme.Red, TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)rewards.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -592f), new Vector2(-24f, -540f));

            var verification = UITheme.Label(root, "Verification",
                _result.Online
                    ? (_result.Verified
                        ? "SERVER-VERIFIED  \u00B7  RACE " + (_result.RaceId ?? "").ToUpperInvariant()
                        : (_result.FailureNote ?? "CONNECTION LOST - NO REWARDS GRANTED"))
                    : _result.PreviewOnly
                        ? "RUN HELD FOR REVIVE  \u00B7  REWARDS APPLY WHEN YOU LEAVE"
                        : "LOCAL RESULT  \u00B7  SAVED TO PROFILE",
                UITheme.SmallSize, _result.Verified ? UITheme.Lime : UITheme.Red,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)verification.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -628f), new Vector2(-24f, -596f));

            BuildMessages(root);
            BuildRevivePanel(root);
            BuildButtons(root);
        }

        /// <summary>
        /// Exact-position revive offer. The authority debits the GEM price atomically; only
        /// then does the held session resume at the captured metre, lane and pose.
        /// </summary>
        private void BuildRevivePanel(RectTransform root)
        {
            if (!_result.ReviveAvailable) return;
            var border = UITheme.GlassCard(root, "Revive Panel", new Color(0.055f, 0.03f, 0.07f, 0.94f), 0.6f);
            var panel = border.transform.parent as RectTransform;
            border.color = UITheme.Magenta.Alpha(0.7f);
            UITheme.Anchor(panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-560f, 300f), new Vector2(560f, 456f));

            var heading = UITheme.Label(panel.transform, "Heading", "EXACT-POSITION REVIVE", UITheme.SmallSize,
                UITheme.Magenta, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)heading.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -42f), new Vector2(-310f, -12f));

            var body = UITheme.Label(panel.transform, "Body",
                "RESUME AT " + UITheme.FormatDistance(_result.Distance) + " IN " +
                (_result.LocationName ?? "THE GRID") + ".\nThe same run, lane and score continue. No world reset.",
                16, UITheme.Dim, TextAnchor.UpperLeft);
            UITheme.Anchor((RectTransform)body.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -104f), new Vector2(-310f, -46f));

            _reviveStatus = UITheme.Label(panel.transform, "Status", "", UITheme.SmallSize,
                UITheme.Lime, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_reviveStatus.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(26f, 12f), new Vector2(-310f, 38f));

            _reviveButton = UITheme.Button(panel.transform, "Revive",
                "REVIVE  \u25C6 " + ReviveCost, UITheme.Magenta, ReviveNow, UITheme.HeadingSize);
            UITheme.Anchor((RectTransform)_reviveButton.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-296f, -36f), new Vector2(-18f, 36f));
            _reviveLabel = _reviveButton.GetComponentInChildren<Text>();
            UiFx.Hover(_reviveButton.gameObject, 1.04f);
            UiFx.Press(_reviveButton.gameObject, 0.96f);
            RefreshReviveStatus();
        }

        private void RefreshReviveStatus()
        {
            if (_reviveStatus == null) return;
            var premium = _app.Premium;
            bool online = premium != null && premium.Online;
            int gems = premium != null ? premium.Gems : 0;
            _reviveStatus.text = "WALLET  \u25C6 " + UITheme.FormatNumber(gems) + " GEMS   \u00B7   " +
                                 (online ? "STORE ONLINE" : "STORE OFFLINE");
            _reviveStatus.color = online ? UITheme.Lime : UITheme.Red;
        }

        private void SetReviveBusy(bool busy)
        {
            _reviveBusy = busy;
            if (_reviveButton != null) _reviveButton.interactable = !busy;
            if (_reviveLabel != null)
                _reviveLabel.text = busy
                    ? "AUTHORIZING..."
                    : "REVIVE  \u25C6 " + ReviveCost;
        }

        private void ReviveNow()
        {
            if (_reviveBusy) return;
            _app.Audio.PlayUiClick();
            var premium = _app.Premium;
            if (premium == null || !premium.Online)
            {
                ShowOfflineModal();
                return;
            }
            if (premium.Gems < ReviveCost)
            {
                ShowInsufficientGems();
                return;
            }
            if (string.IsNullOrEmpty(_result.ReviveSessionId))
            {
                _ui.Toast("REVIVE SESSION EXPIRED - RESTART THE RUN", UITheme.Red, 2.8f);
                return;
            }
            SetReviveBusy(true);
            premium.Revive(_result.ReviveSessionId, _result.Distance, _result.LocationIndex,
                _result.ReviveRequestId, OnReviveConfirmed, OnReviveFailed);
        }

        private void OnReviveConfirmed(PremiumReviveResult result)
        {
            SetReviveBusy(false);
            if (result == null) return;
            _app.Audio.PlaySfx("sfx_revive", 0.9f);
            _ui.Toast("REVIVE CONFIRMED  \u00B7  " + result.costGems + " GEMS DEBITED", UITheme.Lime, 2.6f);
            if (!_app.RevivePendingRun())
            {
                _ui.Toast("REVIVE SESSION ENDED - REWARDS SAVED", UITheme.Red, 2.8f);
                _app.LeaveResultsToMenu();
            }
        }

        private void OnReviveFailed(PremiumError error)
        {
            SetReviveBusy(false);
            RefreshReviveStatus();
            if (error == null)
            {
                _ui.Toast("REVIVE FAILED", UITheme.Red, 2.6f);
                return;
            }
            if (error.code == "INSUFFICIENT_GEMS")
            {
                ShowInsufficientGems();
                return;
            }
            if (error.offline)
            {
                ShowOfflineModal();
                return;
            }
            _ui.Toast(error.Friendly, UITheme.Red, 2.8f);
        }

        private void ShowInsufficientGems()
        {
            int gems = _app.Premium != null ? _app.Premium.Gems : 0;
            _ui.ShowChoiceModal("NOT ENOUGH GEMS!",
                "A revive costs " + ReviveCost + " GEMS. Your server wallet holds " +
                UITheme.FormatNumber(gems) + " GEMS.\nBuy GEMS in the official store, restart " +
                "this run, or close the revive offer. GEMS are never awarded by gameplay.",
                "BUY GEMS", () => _app.ShowGemShopFromResults(),
                "RESTART", () => _app.RestartFromResults(),
                "CLOSE", null);
        }

        private void ShowOfflineModal()
        {
            _ui.ShowModal("STORE OFFLINE",
                "The premium service cannot be reached, so a revive cannot be authorized or debited. " +
                "Start the service and retry.",
                "RETRY", () =>
                {
                    _app.Premium?.SyncAll(_ =>
                    {
                        if (this == null) return;
                        RefreshReviveStatus();
                        if (_app.Premium != null && _app.Premium.Online)
                            _ui.Toast("STORE ONLINE - RETRY THE REVIVE", UITheme.Lime, 2.4f);
                    });
                }, "CLOSE");
        }

        private void BuildStats(RectTransform root)
        {
            var grid = UITheme.NewRect(root, "Stats");
            UITheme.Anchor(grid, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-560f, -520f), new Vector2(560f, -308f));
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(214f, 94f);
            layout.spacing = new Vector2(14f, 12f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 5;
            layout.childAlignment = TextAnchor.MiddleCenter;

            AddStat(grid, "DISTANCE", UITheme.FormatDistance(_result.Distance), UITheme.Cyan);
            AddStat(grid, "SCORE", UITheme.FormatNumber(_result.Score), UITheme.Amber);
            AddStat(grid, "TIME", UITheme.FormatTime(_result.Time), UITheme.White);
            AddStat(grid, "ENERGY", _result.Coins.ToString("N0"), UITheme.Amber);
            AddStat(grid, "NEAR MISSES", _result.NearMisses.ToString("N0"), UITheme.Magenta);

            if (_result.TotalPlayers > 1)
                AddStat(grid, "PLACEMENT", "#" + _result.Placement, UITheme.Lime);
            else
                AddStat(grid, "JUMPS", _result.Jumps.ToString("N0"), UITheme.Cyan);
            AddStat(grid, "SLIDES", _result.Slides.ToString("N0"), UITheme.Cyan);
            AddStat(grid, "POWER-UPS", _result.PowerUpsUsed.ToString("N0"), UITheme.Violet);
            AddStat(grid, "CRASHES", _result.Crashed ? "1" : "0", UITheme.Red);
            AddStat(grid, "PLAYERS", Mathf.Max(1, _result.TotalPlayers).ToString(), UITheme.White);
        }

        private void AddStat(Transform parent, string label, string value, Color color)
        {
            var stat = UITheme.Img(parent, label, new Color(0.045f, 0.08f, 0.14f, 0.86f), UITheme.Rounded, false);
            var valueLabel = UITheme.Label(stat.transform, "Value", value, 28, color, TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UITheme.Anchor((RectTransform)valueLabel.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(6f, 28f), new Vector2(-6f, -6f));
            var nameLabel = UITheme.Label(stat.transform, "Label", label, 14, UITheme.Muted,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)nameLabel.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(4f, 4f), new Vector2(-4f, 26f));
        }

        private void BuildMessages(RectTransform root)
        {
            var panel = UITheme.Img(root, "Messages Panel", new Color(0.03f, 0.055f, 0.1f, 0.82f),
                UITheme.Rounded, false);
            UITheme.Anchor((RectTransform)panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-560f, 116f), new Vector2(560f, _result.ReviveAvailable ? 288f : 322f));
            var heading = UITheme.Label(panel.transform, "Heading", "OPERATIONS FEED", UITheme.SmallSize,
                UITheme.Lime, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)heading.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(22f, -40f), new Vector2(-22f, -10f));

            var body = UITheme.Label(panel.transform, "Body", BuildMessageText(), UITheme.SmallSize,
                UITheme.White, TextAnchor.UpperLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)body.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(22f, 14f), new Vector2(-22f, -46f));
        }

        private string BuildMessageText()
        {
            if (_result.Messages == null || _result.Messages.Count == 0)
                return _result.Online && !_result.Verified
                    ? "THE SERVER DID NOT CONFIRM THIS RESULT. RECONNECT AND RACE AGAIN TO EARN REWARDS."
                    : "NO NEW MISSION COMPLETIONS. KEEP RUNNING TO BUILD YOUR NEXT STREAK.";
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < _result.Messages.Count; i++)
            {
                if (i > 0) builder.Append('\n');
                builder.Append("\u25B8  ").Append(_result.Messages[i]);
            }
            return builder.ToString();
        }

        public bool HandleBackRequest()
        {
            _app.LeaveResultsToMenu();
            return true;
        }

        private void BuildButtons(RectTransform root)
        {
            var row = UITheme.NewRect(root, "Buttons");
            UITheme.Anchor(row, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 32f), new Vector2(0f, 96f));

            var menu = UITheme.Button(row, "Menu", "MAIN MENU", UITheme.Muted, () =>
            {
                _app.Audio.PlayUiClick();
                _app.LeaveResultsToMenu();
            }, UITheme.BodySize);
            UITheme.Anchor((RectTransform)menu.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(-380f, 0f), new Vector2(-14f, 0f));

            var again = UITheme.Button(row, "Again",
                _result.Online ? "NEW CIRCUIT" : "RUN AGAIN", UITheme.Cyan, () =>
                {
                    _app.Audio.PlayUiClick();
                    if (_result.Online) _app.ShowMultiplayer();
                    else _app.RestartFromResults();
                }, UITheme.BodySize);
            UITheme.Anchor((RectTransform)again.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(14f, 0f), new Vector2(380f, 0f));

            var share = UITheme.Button(row, "Modes", "CHOOSE MODE", UITheme.Violet, () =>
            {
                _app.ChooseModeFromResults();
            }, UITheme.BodySize);
            UITheme.Anchor((RectTransform)share.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(394f, 0f), new Vector2(624f, 0f));
        }
    }
}
