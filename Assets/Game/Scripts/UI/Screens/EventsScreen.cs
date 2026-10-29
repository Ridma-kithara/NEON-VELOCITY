using System;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Time-limited operations calendar. Daily and weekly seeds are deterministic functions of the
    /// UTC period, so two runners see the same layout without a live event service. The seasonal
    /// operation and featured contracts are local, offline-safe event entries; they never award
    /// GEMS and never pretend a server event feed is connected.
    /// </summary>
    public sealed class EventsScreen : MonoBehaviour
    {
        private GameApp _app;
        private UIRoot _ui;
        private Text _clock;
        private Text _dailyReset;
        private Text _weeklyReset;
        private Text _dailyCountdown;
        private Text _weeklyCountdown;

        public static GameObject Build(GameApp app, UIRoot ui)
        {
            var root = ScreenKit.Root("Events");
            var screen = root.gameObject.AddComponent<EventsScreen>();
            screen._app = app;
            screen._ui = ui;
            screen.BuildLayout();
            return root.gameObject;
        }

        private void BuildLayout()
        {
            var root = (RectTransform)transform;
            ScreenKit.Backdrop(root, UITheme.Violet);
            ScreenKit.Header(root, _app, "EVENT CALENDAR", "EVENT OPERATIONS",
                "Daily and weekly signals rotate on UTC. Seasonal and featured contracts run offline.",
                UITheme.Violet, _app.NavigateBack);

            BuildSummary(root);

            UITheme.Scroll(root, "Event Scroll", out RectTransform content);
            var scroll = root.Find("Event Scroll");
            UITheme.Anchor((RectTransform)scroll, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(36f, 44f), new Vector2(-36f, -262f));

            var daily = GameModes.Get(GameModeId.DailyChallenge);
            var weekly = GameModes.Get(GameModeId.WeeklyChallenge);
            var seasonal = GameModes.Get(GameModeId.SeasonalEvent);
            var boss = GameModes.Get(GameModeId.BossChase);
            var route = GameModes.Get(GameModeId.RouteExploration);

            AddEventCard(content, "DAILY SIGNAL", "ROTATES AT 00:00 UTC", daily, UITheme.Cyan,
                OutDailyCountdown, true);
            AddEventCard(content, "WEEKLY PROTOCOL", "ROTATES MONDAY 00:00 UTC", weekly, UITheme.Violet,
                OutWeeklyCountdown, true);
            AddEventCard(content, "MONSOON OVERDRIVE", "SEASON 01  ·  LIMITED SEASON", seasonal,
                UITheme.Cyan, null, false);
            AddEventCard(content, "WARDEN PURSUIT", "FEATURED CONTRACT  ·  BOSS CHASE", boss,
                UITheme.Red, null, false);
            AddEventCard(content, "ROUTE SURVEY", "FEATURED CONTRACT  ·  JUNCTION HUNT", route,
                UITheme.Violet, null, false);

            ScreenKit.Footer(root, "EVENT SEEDS ARE LOCAL AND DETERMINISTIC  ·  ONLINE BOARDS REQUIRE AN ACCOUNT");
            RefreshClock();
        }

        private void BuildSummary(RectTransform root)
        {
            var card = UITheme.GlassCard(root, "Calendar Summary", new Color(0.04f, 0.035f, 0.08f, 0.96f));
            var panel = card.transform.parent as RectTransform;
            UITheme.Anchor(panel, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(36f, -250f), new Vector2(-36f, -158f));

            _clock = UITheme.Label(panel.transform, "Clock", "", UITheme.HeadingSize, UITheme.White,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_clock.transform, new Vector2(0f, 0f), new Vector2(0.42f, 1f),
                new Vector2(26f, 0f), new Vector2(-6f, 0f));

            var dailyLabel = UITheme.Label(panel.transform, "Daily Label", "DAILY RESET",
                UITheme.SmallSize, UITheme.Dim, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)dailyLabel.transform, new Vector2(0.42f, 0.52f),
                new Vector2(0.72f, 1f), new Vector2(6f, 0f), new Vector2(-6f, -6f));
            _dailyCountdown = UITheme.Label(panel.transform, "Daily Countdown", "", UITheme.HeadingSize,
                UITheme.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_dailyCountdown.transform, new Vector2(0.42f, 0f),
                new Vector2(0.72f, 0.52f), new Vector2(6f, -6f), new Vector2(-6f, 0f));
            _dailyReset = _dailyCountdown;

            var weeklyLabel = UITheme.Label(panel.transform, "Weekly Label", "WEEKLY RESET",
                UITheme.SmallSize, UITheme.Dim, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)weeklyLabel.transform, new Vector2(0.72f, 0.52f),
                new Vector2(1f, 1f), new Vector2(6f, 0f), new Vector2(-26f, -6f));
            _weeklyCountdown = UITheme.Label(panel.transform, "Weekly Countdown", "", UITheme.HeadingSize,
                UITheme.Violet, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_weeklyCountdown.transform, new Vector2(0.72f, 0f),
                new Vector2(1f, 0.52f), new Vector2(6f, -6f), new Vector2(-26f, 0f));
            _weeklyReset = _weeklyCountdown;
        }

        private void AddEventCard(RectTransform content, string title, string codeName, ModeSpec spec,
            Color accent, Action<Text> bindCountdown, bool limited)
        {
            var card = UITheme.GlassCard(content, title, new Color(0.03f, 0.05f, 0.09f, 0.96f));
            var panel = (RectTransform)card.transform.parent;
            UITheme.Layout(panel.gameObject, 236f, -1f, 236f);

            var stripe = UITheme.Img(panel.transform, "Accent", accent);
            UITheme.Anchor((RectTransform)stripe.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 14f), new Vector2(5f, -14f));

            var name = UITheme.Label(panel.transform, "Title", title, UITheme.HeadingSize, UITheme.White,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -48f), new Vector2(-360f, -14f));

            var meta = UITheme.Label(panel.transform, "Meta", codeName, UITheme.SmallSize, accent,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Anchor((RectTransform)meta.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-360f, -46f), new Vector2(-28f, -16f));

            var objective = UITheme.Label(panel.transform, "Objective", spec.Objective, UITheme.BodySize,
                UITheme.White, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)objective.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -86f), new Vector2(-330f, -52f));

            var rules = UITheme.Label(panel.transform, "Rules", spec.Rules, UITheme.SmallSize,
                UITheme.Dim, TextAnchor.UpperLeft);
            UITheme.Anchor((RectTransform)rules.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -140f), new Vector2(-330f, -90f));

            string target = spec.HasTargetDistance
                ? "TARGET  " + UITheme.FormatDistance(spec.TargetDistance)
                : (spec.IsTimed ? "TIMER  " + Mathf.RoundToInt(spec.DurationSeconds) + " SEC" : "ENDLESS");
            string multiplier = spec.CoinMultiplier > 1.01f
                ? "  ·  " + spec.CoinMultiplier.ToString("0.##") + "x CREDITS" : "";
            string modeId = spec.Id.ToString();
            var best = _app.Profile.FindModeBest(modeId);
            string record = best != null && best.distance > 0f
                ? "  ·  BEST " + UITheme.FormatDistance(best.distance) : "";
            var stats = UITheme.Label(panel.transform, "Stats", target + multiplier + record,
                UITheme.SmallSize, UITheme.Amber, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)stats.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(28f, 22f), new Vector2(-330f, 52f));

            if (limited && bindCountdown != null)
            {
                var countdown = UITheme.Label(panel.transform, "Countdown", "", UITheme.SmallSize,
                    accent, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)countdown.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(28f, 58f), new Vector2(-330f, 88f));
                bindCountdown(countdown);
            }
            else
            {
                var state = UITheme.Label(panel.transform, "State", "ACTIVE  ·  OFFLINE CALENDAR",
                    UITheme.SmallSize, UITheme.Lime, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)state.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(28f, 58f), new Vector2(-330f, 88f));
            }

            var start = UITheme.Button(panel.transform, "Start", "START EVENT", accent, () =>
            {
                _app.Audio.PlayUiClick();
                _app.StartMode(spec.Id);
            }, UITheme.BodySize);
            UITheme.Anchor((RectTransform)start.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-300f, 30f), new Vector2(-28f, 86f));
        }

        private void OutDailyCountdown(Text label) => _dailyCountdown = label;
        private void OutWeeklyCountdown(Text label) => _weeklyCountdown = label;

        private void Update()
        {
            RefreshClock();
        }

        private void RefreshClock()
        {
            DateTime utc = DateTime.UtcNow;
            if (_clock != null)
                _clock.text = utc.ToString("ddd dd MMM yyyy  ·  HH:mm:ss") + " UTC";
            string daily = "ENDS IN " + Remaining(NextMidnight(utc));
            string weekly = "ENDS IN " + Remaining(NextMonday(utc));
            if (_dailyReset != null) _dailyReset.text = daily;
            if (_weeklyReset != null) _weeklyReset.text = weekly;
            if (_dailyCountdown != null) _dailyCountdown.text = daily;
            if (_weeklyCountdown != null) _weeklyCountdown.text = weekly;
        }

        private static DateTime NextMidnight(DateTime utc) => utc.Date.AddDays(1);

        private static DateTime NextMonday(DateTime utc)
        {
            int delta = ((int)DayOfWeek.Monday - (int)utc.DayOfWeek + 7) % 7;
            if (delta == 0) delta = 7;
            return utc.Date.AddDays(delta);
        }

        private static string Remaining(DateTime target)
        {
            TimeSpan span = target - DateTime.UtcNow;
            if (span.TotalSeconds < 0) span = TimeSpan.Zero;
            return ((int)span.TotalHours).ToString("00") + ":" + span.Minutes.ToString("00") + ":" +
                   span.Seconds.ToString("00");
        }
    }
}
