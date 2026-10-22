using System;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>End-of-run summary, including verification state and every reward granted.</summary>
    public sealed class ResultsScreen : MonoBehaviour
    {
        private GameApp _app;
        private UIRoot _ui;
        private RunResult _result;

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

            if (_result.NewBest)
            {
                var best = UITheme.Badge(root, "New Best", "NEW PERSONAL BEST", UITheme.Amber, 260, 34);
                UITheme.Anchor((RectTransform)best.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-130f, -284f), new Vector2(130f, -250f));
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
                    : "LOCAL RESULT  \u00B7  SAVED TO PROFILE",
                UITheme.SmallSize, _result.Verified ? UITheme.Lime : UITheme.Red,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)verification.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -628f), new Vector2(-24f, -596f));

            BuildMessages(root);
            BuildButtons(root);
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
                new Vector2(-560f, 116f), new Vector2(560f, 322f));
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

        private void BuildButtons(RectTransform root)
        {
            var row = UITheme.NewRect(root, "Buttons");
            UITheme.Anchor(row, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 32f), new Vector2(0f, 96f));

            var menu = UITheme.Button(row, "Menu", "MAIN MENU", UITheme.Muted, () =>
            {
                _app.Audio.PlayUiClick();
                _app.ShowMainMenu();
            }, UITheme.BodySize);
            UITheme.Anchor((RectTransform)menu.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(-380f, 0f), new Vector2(-14f, 0f));

            var again = UITheme.Button(row, "Again",
                _result.Online ? "NEW CIRCUIT" : "RUN AGAIN", UITheme.Cyan, () =>
                {
                    _app.Audio.PlayUiClick();
                    if (_result.Online) _app.ShowMultiplayer();
                    else _app.RestartSession();
                }, UITheme.BodySize);
            UITheme.Anchor((RectTransform)again.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(14f, 0f), new Vector2(380f, 0f));

            var share = UITheme.Button(row, "Modes", "CHOOSE MODE", UITheme.Violet, () =>
            {
                _app.Audio.PlayUiClick();
                _app.ShowModes();
            }, UITheme.BodySize);
            UITheme.Anchor((RectTransform)share.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(394f, 0f), new Vector2(624f, 0f));
        }
    }
}
