using System;
using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>Daily and weekly operations plus lifetime achievement progress.</summary>
    public sealed class MissionsScreen : MonoBehaviour
    {
        private GameApp _app;
        private UIRoot _ui;
        private int _tab;

        public static GameObject Build(GameApp app, UIRoot ui) => Build(app, ui, 0);

        public static GameObject Build(GameApp app, UIRoot ui, int tab)
        {
            var root = ScreenKit.Root("Operations");
            var screen = root.gameObject.AddComponent<MissionsScreen>();
            screen._app = app;
            screen._ui = ui;
            screen._tab = Mathf.Clamp(tab, 0, 2);
            screen.BuildLayout();
            return root.gameObject;
        }

        private void BuildLayout()
        {
            var root = (RectTransform)transform;
            ScreenKit.Backdrop(root, UITheme.Amber);
            ScreenKit.Header(root, _app, "OPERATIONS", "MISSION BOARD",
                "Daily resets at 00:00 UTC. Weekly protocols roll over every Monday.", UITheme.Amber,
                _app.NavigateBack);
            BuildTabs(root);

            UITheme.Scroll(root, "Mission Scroll", out RectTransform content);
            var scroll = root.Find("Mission Scroll");
            UITheme.Anchor((RectTransform)scroll, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(36f, 42f), new Vector2(-36f, -222f));

            if (_tab == 0) BuildMissions(content, "daily");
            else if (_tab == 1) BuildMissions(content, "weekly");
            else BuildAchievements(content);
        }

        private void BuildTabs(RectTransform root)
        {
            var bar = UITheme.Img(root, "Tabs", new Color(0.02f, 0.04f, 0.075f, 0.4f));
            UITheme.Anchor((RectTransform)bar.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(36f, -216f), new Vector2(-36f, -158f));
            string[] labels = { "DAILY SIGNALS", "WEEKLY PROTOCOLS", "ACHIEVEMENTS" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var button = UITheme.Button(bar.transform, labels[i], labels[i],
                    i == _tab ? UITheme.Amber : UITheme.Muted,
                    () => { _app.Audio.PlayUiClick(); _tab = index; Rebuild(); }, UITheme.BodySize, i == _tab);
                UITheme.Anchor((RectTransform)button.transform, new Vector2(i / 3f, 0f), new Vector2((i + 1) / 3f, 1f),
                    new Vector2(6f, 8f), new Vector2(-6f, 8f));
            }
        }

        private void BuildMissions(RectTransform content, string scope)
        {
            var missions = _app.Progression.ActiveMissions(scope);
            var banner = UITheme.Img(content, "Summary", new Color(0.04f, 0.07f, 0.12f, 0.85f), UITheme.Rounded, false);
            UITheme.Layout(banner.gameObject, 68f, -1f, 68f);
            string summary = scope == "daily"
                ? "TODAY  " + DateTime.UtcNow.ToString("dddd, dd MMMM").ToUpperInvariant()
                : "WEEK  " + MissionFactory.WeeklyKey(DateTime.UtcNow);
            var summaryLabel = UITheme.Label(banner.transform, "Summary Label", summary, UITheme.SmallSize,
                UITheme.Amber, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Stretch((RectTransform)summaryLabel.transform, 22f, 4f, 22f, 4f);
            int completed = 0;
            for (int i = 0; i < missions.Count; i++) if (missions[i].completed) completed++;
            var countLabel = UITheme.Label(banner.transform, "Count", completed + " / " + missions.Count + " COMPLETE",
                UITheme.SmallSize, UITheme.White, TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Stretch((RectTransform)countLabel.transform, 22f, 4f, 22f, 4f);

            for (int i = 0; i < missions.Count; i++) BuildMissionCard(content, missions[i]);
            if (missions.Count == 0)
            {
                var empty = UITheme.Label(content, "Empty", "NO OPERATIONS AVAILABLE. CHECK BACK AFTER THE NEXT RESET.",
                    UITheme.BodySize, UITheme.Dim, TextAnchor.MiddleCenter);
                UITheme.Layout(empty.gameObject, 90f, -1f, 90f);
            }
        }

        private void BuildMissionCard(RectTransform content, MissionState mission)
        {
            var card = ScreenKit.Card(content, "Mission " + mission.id, new Color(0.035f, 0.065f, 0.12f, 0.94f));
            UITheme.Layout(card.gameObject, 150f, -1f, 150f);

            Color accent = mission.completed ? UITheme.Lime : UITheme.Amber;
            var edge = UITheme.Img(card.transform, "Edge", accent);
            UITheme.Anchor((RectTransform)edge.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 14f), new Vector2(4f, -14f));

            var title = UITheme.Label(card.transform, "Title", mission.title, UITheme.HeadingSize,
                UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -52f), new Vector2(-360f, -16f));

            var desc = UITheme.Label(card.transform, "Desc", mission.description, UITheme.SmallSize,
                UITheme.Dim, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)desc.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -84f), new Vector2(-360f, -54f));

            ScreenKit.ProgressBar(card.transform, "Progress",
                mission.target > 0 ? mission.progress / (float)mission.target : 0f, accent, out _,
                new Vector2(26f, 66f), new Vector2(-360f, 78f));

            var progress = UITheme.Label(card.transform, "Progress Text",
                mission.progress + " / " + mission.target, UITheme.SmallSize, UITheme.White,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)progress.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(26f, 34f), new Vector2(-360f, 62f));

            var reward = UITheme.Label(card.transform, "Reward",
                "\u25C6 " + mission.rewardCredits + "    +" + mission.rewardXp + " XP", UITheme.SmallSize,
                UITheme.Amber, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)reward.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(26f, 8f), new Vector2(-360f, 30f));

            string id = mission.id;
            if (mission.claimed)
            {
                var claimed = UITheme.Button(card.transform, "Claimed", "CLAIMED", UITheme.Muted, null,
                    UITheme.BodySize, false);
                claimed.interactable = false;
                UITheme.Anchor((RectTransform)claimed.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-294f, -28f), new Vector2(-26f, 28f));
            }
            else if (mission.completed)
            {
                var claim = UITheme.Button(card.transform, "Claim", "CLAIM REWARD", UITheme.Lime,
                    () => Claim(id), UITheme.BodySize);
                UITheme.Anchor((RectTransform)claim.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-294f, -28f), new Vector2(-26f, 28f));
            }
            else
            {
                var status = UITheme.Badge(card.transform, "Status", "IN PROGRESS", UITheme.Muted, 170, 30);
                UITheme.Anchor((RectTransform)status.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-224f, -15f), new Vector2(-54f, 15f));
            }
        }

        private void Claim(string missionId)
        {
            _app.Audio.PlayUiClick();
            bool ok = _app.Progression.ClaimMission(missionId);
            _app.UI.Toast(ok ? "OPERATION REWARD CLAIMED" : "REWARD NOT AVAILABLE",
                ok ? UITheme.Lime : UITheme.Red, 2.5f);
            Rebuild();
        }

        private void BuildAchievements(RectTransform content)
        {
            var stats = _app.Profile.lifetime;
            var definitions = MissionFactory.Achievements;
            for (int i = 0; i < definitions.Length; i++)
            {
                var achievement = definitions[i];
                bool redeemed = _app.Profile.IsRedeemed("ach:" + achievement.id);
                int progress = achievement.LifetimeProgress(stats);
                float normalised = achievement.target > 0 ? Mathf.Clamp01(progress / (float)achievement.target) : 0f;
                var card = ScreenKit.Card(content, "Achievement " + achievement.id,
                    new Color(0.035f, 0.065f, 0.12f, 0.94f));
                UITheme.Layout(card.gameObject, 126f, -1f, 126f);

                var icon = UITheme.Img(card.transform, "Icon", redeemed ? UITheme.Lime : UITheme.Violet,
                    TextureFactory.Diamond, false);
                UITheme.Anchor((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(26f, -26f), new Vector2(78f, 26f));

                var title = UITheme.Label(card.transform, "Title", achievement.title, UITheme.HeadingSize,
                    UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(100f, -48f), new Vector2(-300f, -14f));

                var desc = UITheme.Label(card.transform, "Desc", achievement.description, UITheme.SmallSize,
                    UITheme.Dim, TextAnchor.MiddleLeft);
                UITheme.Anchor((RectTransform)desc.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(100f, -78f), new Vector2(-300f, -50f));

                ScreenKit.ProgressBar(card.transform, "Progress", normalised,
                    redeemed ? UITheme.Lime : UITheme.Violet, out _, new Vector2(100f, 28f), new Vector2(-300f, 40f));

                var progressLabel = UITheme.Label(card.transform, "Progress Text",
                    Mathf.Min(progress, achievement.target) + " / " + achievement.target, UITheme.SmallSize,
                    UITheme.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)progressLabel.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(100f, 4f), new Vector2(-300f, 28f));

                var badge = UITheme.Badge(card.transform, "State",
                    redeemed ? "UNLOCKED  \u25C6 " + achievement.credits : "\u25C6 " + achievement.credits,
                    redeemed ? UITheme.Lime : UITheme.Amber, 170, 30);
                UITheme.Anchor((RectTransform)badge.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-220f, -15f), new Vector2(-40f, 15f));
            }
        }

        private void Rebuild()
        {
            _app.UI.ShowScreen(Build(_app, _ui, _tab));
        }
    }
}
