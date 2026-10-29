using System;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>Full solo-mode catalogue with live records and a rules briefing before launch.</summary>
    public sealed class ModeSelectScreen : MonoBehaviour
    {
        private GameApp _app;
        private UIRoot _ui;

        public static GameObject Build(GameApp app, UIRoot ui)
        {
            var root = ScreenKit.Root("Mode Select");
            var screen = root.gameObject.AddComponent<ModeSelectScreen>();
            screen.BuildLayout(app, ui);
            return root.gameObject;
        }

        private void BuildLayout(GameApp app, UIRoot ui)
        {
            _app = app;
            _ui = ui;
            var root = (RectTransform)transform;
            ScreenKit.Backdrop(root, UITheme.Cyan);
            ScreenKit.Header(root, app, "SOLO", "RUN SELECT", GameModes.All.Count + " protocols. One grid. Choose your line.",
                UITheme.Cyan, app.NavigateBack);

            UITheme.Scroll(root, "Mode Scroll", out RectTransform content);
            var scroll = root.Find("Mode Scroll");
            UITheme.Anchor((RectTransform)scroll, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(36f, 56f), new Vector2(-36f, -158f));

            var modes = GameModes.All;
            for (int i = 0; i < modes.Count; i++)
            {
                BuildCard(content, modes[i], i);
                if (i + 1 < modes.Count) UITheme.Divider(content, new Color(1f, 1f, 1f, 0.05f), 1f);
            }

            var online = UITheme.Button(root, "Online", "OPEN 5P MULTIPLAYER", UITheme.Lime,
                app.ShowMultiplayer, UITheme.BodySize);
            UITheme.Anchor((RectTransform)online.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-390f, 12f), new Vector2(-36f, 54f));
        }

        private void BuildCard(RectTransform content, ModeSpec mode, int index)
        {
            var card = ScreenKit.Card(content, "Mode " + mode.Id, new Color(0.035f, 0.065f, 0.12f, 0.92f));
            UITheme.Layout(card.gameObject, 146f, -1f, 146f);

            var accent = UITheme.Img(card.transform, "Accent", Palette.Hex(mode.AccentHex));
            UITheme.Anchor((RectTransform)accent.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 14f), new Vector2(4f, -14f));

            var indexLabel = UITheme.Label(card.transform, "Index", (index + 1).ToString("00"),
                UITheme.SmallSize, Palette.Hex(mode.AccentHex), TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)indexLabel.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(22f, -48f), new Vector2(58f, -16f));

            var title = UITheme.Label(card.transform, "Title", mode.Title, UITheme.HeadingSize, UITheme.White,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(70f, -48f), new Vector2(-610f, -8f));

            var tag = UITheme.Label(card.transform, "Tag", mode.CodeName + "  /  " + mode.Tagline,
                UITheme.SmallSize, Palette.Hex(mode.AccentHex), TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)tag.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(72f, 72f), new Vector2(-610f, 98f));

            var objective = UITheme.Label(card.transform, "Objective", mode.Objective, UITheme.SmallSize,
                UITheme.Dim, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)objective.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(72f, 44f), new Vector2(-610f, 72f));

            var best = _app.Profile.FindModeBest(mode.Id.ToString());
            string stat = best != null && best.distance > 0f
                ? "BEST " + UITheme.FormatDistance(best.distance) + "   SCORE " + UITheme.FormatNumber(best.score)
                : "NO RECORD YET";
            var stats = UITheme.Label(card.transform, "Stats", stat, UITheme.SmallSize, UITheme.Muted,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)stats.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(72f, 14f), new Vector2(-610f, 42f));

            if (mode.IsTimed)
            {
                var timed = UITheme.Badge(card.transform, "Timer", Mathf.RoundToInt(mode.DurationSeconds) + " SEC",
                    Palette.Hex(mode.AccentHex), 108, 28);
                UITheme.Anchor((RectTransform)timed.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-560f, -48f), new Vector2(-440f, -18f));
            }
            if (mode.Id == GameModeId.OnlineRace || mode.Id == GameModeId.PrivateRace)
            {
                var online = UITheme.Badge(card.transform, "Online", "5P LIVE", UITheme.Lime, 96, 28);
                UITheme.Anchor((RectTransform)online.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-440f, -48f), new Vector2(-336f, -18f));
            }

            var brief = UITheme.Button(card.transform, "Brief", "BRIEF", UITheme.Muted,
                () => ShowBrief(mode), UITheme.SmallSize, false);
            UITheme.Anchor((RectTransform)brief.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-310f, 30f), new Vector2(-196f, 82f));

            var start = UITheme.Button(card.transform, "Start", mode.IsOnline ? "LOBBY" : "START",
                Palette.Hex(mode.AccentHex), () => Launch(mode), UITheme.BodySize);
            UITheme.Anchor((RectTransform)start.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-184f, 30f), new Vector2(-26f, 82f));
        }

        private void ShowBrief(ModeSpec mode)
        {
            _app.Audio.PlayUiClick();
            string body = mode.Rules + "\n\nOBJECTIVE\n" + mode.Objective +
                          "\n\nMODE FACTORS\nSPEED x" + mode.SpeedMultiplier.ToString("0.00") +
                        "   CREDITS x" + mode.CoinMultiplier.ToString("0.00") +
                          (mode.IsOnline ? "\nPLAYERS " + mode.MaxPlayers + "   SERVER-AUTHORITATIVE" : "");
            _ui.ShowModal(mode.Title, body, mode.IsOnline ? "LOBBY" : "START", () => Launch(mode));
        }

        private void Launch(ModeSpec mode)
        {
            _app.Audio.PlayUiClick();
            if (mode.IsOnline) _app.ShowMultiplayer();
            else _app.StartMode(mode.Id);
        }
    }
}
