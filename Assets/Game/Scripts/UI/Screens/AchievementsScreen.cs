using System;
using NeonVelocity.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Lifetime achievement cabinet. Achievements are awarded automatically by the progression
    /// service during a run; this screen never grants a duplicate reward and shows exact progress.
    /// </summary>
    public sealed class AchievementsScreen : MonoBehaviour
    {
        public static GameObject Build(GameApp app, UIRoot ui)
        {
            var root = ScreenKit.Root("Achievements");
            var screen = root.gameObject.AddComponent<AchievementsScreen>();
            screen._app = app;
            screen._ui = ui;
            screen.BuildLayout();
            return root.gameObject;
        }

        private GameApp _app;
        private UIRoot _ui;

        private void BuildLayout()
        {
            var root = (RectTransform)transform;
            ScreenKit.Backdrop(root, UITheme.Amber);
            int claimed = 0;
            var achievements = MissionFactory.Achievements;
            for (int i = 0; i < achievements.Length; i++)
                if (_app.Profile.IsRedeemed("ach:" + achievements[i].id)) claimed++;
            ScreenKit.Header(root, _app, "CAREER", "ACHIEVEMENTS",
                "Lifetime milestones. Rewards are credited once, automatically, during a run.",
                UITheme.Amber, _app.NavigateBack);

            var summary = ScreenKit.Card(root, "Summary", new Color(0.04f, 0.045f, 0.075f, 0.95f));
            UITheme.Anchor((RectTransform)summary.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(36f, -252f), new Vector2(-36f, -158f));
            var title = UITheme.Label(summary.transform, "Count",
                claimed + " / " + achievements.Length + " COMPLETE",
                UITheme.HeadingSize, UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)title.transform, new Vector2(0f, 0f), new Vector2(0.5f, 1f),
                new Vector2(28f, 0f), new Vector2(0f, 0f));
            var credits = UITheme.Label(summary.transform, "Credits",
                "TOTAL AWARDED  ◆ " + UITheme.FormatNumber(SumClaimed(achievements)),
                UITheme.BodySize, UITheme.Amber, TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Anchor((RectTransform)credits.transform, new Vector2(0.5f, 0f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(-28f, 0f));
            ScreenKit.ProgressBar(summary.transform, "Progress",
                achievements.Length > 0 ? claimed / (float)achievements.Length : 0f,
                UITheme.Amber, out _, new Vector2(28f, 16f), new Vector2(-28f, 24f));

            UITheme.Scroll(root, "Achievement Scroll", out RectTransform content);
            var scroll = root.Find("Achievement Scroll");
            UITheme.Anchor((RectTransform)scroll, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(36f, 44f), new Vector2(-36f, -264f));
            for (int i = 0; i < achievements.Length; i++)
                AddAchievement(content, achievements[i]);
            ScreenKit.Footer(root, "CAREER PROGRESS IS INCLUDED IN CLOUD SYNC");
        }

        private int SumClaimed(AchievementDefinition[] achievements)
        {
            int total = 0;
            for (int i = 0; i < achievements.Length; i++)
                if (_app.Profile.IsRedeemed("ach:" + achievements[i].id))
                    total += achievements[i].credits;
            return total;
        }

        private void AddAchievement(RectTransform content, AchievementDefinition achievement)
        {
            bool claimed = _app.Profile.IsRedeemed("ach:" + achievement.id);
            int progress = achievement.LifetimeProgress(_app.Profile.lifetime);
            bool complete = progress >= achievement.target;
            Color accent = claimed ? UITheme.Amber : complete ? UITheme.Lime : UITheme.Muted;
            var card = ScreenKit.Card(content, "Achievement " + achievement.id,
                claimed ? new Color(0.055f, 0.065f, 0.07f, 0.96f) : new Color(0.03f, 0.05f, 0.09f, 0.94f));
            UITheme.Layout(card.gameObject, 96f, -1f, 96f);
            var stripe = UITheme.Img(card.transform, "Accent", accent);
            UITheme.Anchor((RectTransform)stripe.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 10f), new Vector2(5f, -10f));

            var title = UITheme.Label(card.transform, "Title", achievement.title, UITheme.HeadingSize,
                claimed ? UITheme.White : UITheme.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)title.transform, new Vector2(0f, 0.46f), new Vector2(1f, 1f),
                new Vector2(28f, 0f), new Vector2(-220f, -6f));
            var description = UITheme.Label(card.transform, "Description", achievement.description,
                UITheme.SmallSize, UITheme.Dim, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)description.transform, new Vector2(0f, 0f), new Vector2(1f, 0.52f),
                new Vector2(28f, 4f), new Vector2(-220f, 0f));

            var reward = UITheme.Label(card.transform, "Reward",
                claimed ? "◆ " + achievement.credits + "  CLAIMED" : "◆ " + achievement.credits,
                UITheme.BodySize, accent, TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Anchor((RectTransform)reward.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-210f, -16f), new Vector2(-28f, 16f));

            float value = achievement.target > 0
                ? Mathf.Clamp01(progress / (float)achievement.target) : 0f;
            var track = UITheme.Img(card.transform, "Track", new Color(0.09f, 0.14f, 0.2f, 0.95f),
                UITheme.Rounded, false);
            UITheme.Anchor((RectTransform)track.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(28f, 12f), new Vector2(-220f, 20f));
            var fill = UITheme.Img(track.transform, "Fill", accent, UITheme.Rounded, false);
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(value, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var count = UITheme.Label(track.transform, "Count",
                Mathf.Min(progress, achievement.target).ToString("N0") + " / " + achievement.target.ToString("N0"),
                UITheme.SmallSize, UITheme.White, TextAnchor.MiddleRight);
            UITheme.Stretch((RectTransform)count.transform, 8f, 0f, 8f, 0f);
        }
    }
}
