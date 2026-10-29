using System;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Persistent world atlas. Every district is a pure function of distance, so discoveries
    /// are stored by id and survive restarts. The screen also shows the authored weather and
    /// architecture family so the player can plan a route instead of guessing.
    /// </summary>
    public sealed class DiscoveryScreen : MonoBehaviour
    {
        public static GameObject Build(GameApp app, UIRoot ui)
        {
            var root = ScreenKit.Root("Discovery");
            var screen = root.gameObject.AddComponent<DiscoveryScreen>();
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
            ScreenKit.Backdrop(root, UITheme.Cyan);
            int discovered = _app.Profile.discoveredLocations != null
                ? _app.Profile.discoveredLocations.Count : 0;
            ScreenKit.Header(root, _app, "ROUTE EXPLORATION", "WORLD ATLAS",
                "Twenty authored locations. Each 3 km projection reveals the next theme.",
                UITheme.Cyan, _app.NavigateBack);

            var summary = ScreenKit.Card(root, "Summary", new Color(0.03f, 0.06f, 0.11f, 0.95f));
            UITheme.Anchor((RectTransform)summary.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(36f, -252f), new Vector2(-36f, -158f));
            var title = UITheme.Label(summary.transform, "Count",
                discovered + " / " + LocationCatalog.Count + " DISTRICTS DISCOVERED",
                UITheme.HeadingSize, UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)title.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(28f, 0f), new Vector2(-420f, 0f));
            var hint = UITheme.Label(summary.transform, "Hint",
                "Missions, time trials and route exploration all reveal new districts.",
                UITheme.SmallSize, UITheme.Dim, TextAnchor.MiddleRight);
            UITheme.Anchor((RectTransform)hint.transform, new Vector2(0.45f, 0f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(-28f, 0f));
            ScreenKit.ProgressBar(summary.transform, "Discovery Bar",
                LocationCatalog.Count > 0 ? discovered / (float)LocationCatalog.Count : 0f,
                UITheme.Cyan, out _, new Vector2(28f, 16f), new Vector2(-28f, 24f));

            UITheme.Scroll(root, "Atlas Scroll", out RectTransform content);
            var scroll = root.Find("Atlas Scroll");
            UITheme.Anchor((RectTransform)scroll, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(36f, 44f), new Vector2(-36f, -264f));

            for (int i = 0; i < LocationCatalog.All.Length; i++)
                AddLocation(content, LocationCatalog.All[i], i);
            ScreenKit.Footer(root, "DISTRICT RECORDS SYNC WITH YOUR ACCOUNT WHEN ONLINE");
        }

        private void AddLocation(RectTransform content, LocationDefinition location, int index)
        {
            bool found = _app.Profile.HasDiscoveredLocation(location.id);
            var record = _app.Profile.FindLocationRecord(location.id);
            Color accent = found ? Palette.Hex(location.accentHex) : UITheme.Muted;
            var card = ScreenKit.Card(content, "District " + index,
                found ? new Color(0.035f, 0.065f, 0.12f, 0.96f) : new Color(0.025f, 0.035f, 0.06f, 0.9f));
            UITheme.Layout(card.gameObject, 92f, -1f, 92f);
            var stripe = UITheme.Img(card.transform, "Accent", accent);
            UITheme.Anchor((RectTransform)stripe.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 10f), new Vector2(5f, -10f));

            var number = UITheme.Label(card.transform, "Index", (index + 1).ToString("00"),
                UITheme.HeadingSize, accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)number.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(22f, 0f), new Vector2(84f, 0f));

            var name = UITheme.Label(card.transform, "Name", location.displayName,
                UITheme.HeadingSize, found ? UITheme.White : UITheme.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 0.45f), new Vector2(1f, 1f),
                new Vector2(92f, 0f), new Vector2(-380f, -8f));

            string meta = location.themeName + "  ·  " + Weather(location.weather) + "  ·  " +
                          TimeOfDay(location.timeOfDay);
            var subtitle = UITheme.Label(card.transform, "Meta", meta, UITheme.SmallSize,
                UITheme.Dim, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)subtitle.transform, new Vector2(0f, 0f), new Vector2(1f, 0.5f),
                new Vector2(92f, 4f), new Vector2(-380f, 0f));

            string state = found ? "DISCOVERED" : "UNDISCOVERED";
            if (record != null && record.visits > 0)
                state = "VISITS " + record.visits + "  ·  BEST " + UITheme.FormatDistance(record.bestDistance);
            var badge = UITheme.Badge(card.transform, "State", state, accent, found ? 300 : 180, 30);
            UITheme.Anchor((RectTransform)badge.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-330f, -15f), new Vector2(-24f, 15f));
        }

        private static string Weather(int weather)
        {
            switch (weather)
            {
                case 1: return "RAIN";
                case 2: return "SNOW";
                case 3: return "DUST";
                case 4: return "EMBERS";
                case 5: return "FOG";
                default: return "CLEAR";
            }
        }

        private static string TimeOfDay(int time) => TimeOfDayCatalog.Label(time);
    }
}
