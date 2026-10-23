using System;
using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Multiplayer;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Personal records, local verified/offline boards and the authoritative server's global
    /// ladder. The server board is fetched on demand so single-player never needs a connection.
    /// </summary>
    public sealed class LeaderboardScreen : MonoBehaviour
    {
        private GameApp _app;
        private UIRoot _ui;
        private int _tab;
        private List<WireLeaderboardEntry> _serverEntries;
        private bool _fetching;
        private bool _ownsConnection;
        private bool _requestSent;

        public static GameObject Build(GameApp app, UIRoot ui) => Build(app, ui, 0, null, false, false);

        private static GameObject Build(GameApp app, UIRoot ui, int tab,
            List<WireLeaderboardEntry> entries, bool fetching, bool requestSent)
        {
            var root = ScreenKit.Root("Rankings");
            var screen = root.gameObject.AddComponent<LeaderboardScreen>();
            screen._app = app;
            screen._ui = ui;
            screen._tab = Mathf.Clamp(tab, 0, 2);
            screen._serverEntries = entries;
            screen._fetching = fetching;
            screen._requestSent = requestSent;
            screen.BuildLayout();
            return root.gameObject;
        }

        private void BuildLayout()
        {
            var root = (RectTransform)transform;
            ScreenKit.Backdrop(root, UITheme.Violet);
            ScreenKit.Header(root, _app, "RANKINGS", "GRID RANKINGS",
                "Local records are always available. Circuit rankings are server-authoritative.",
                UITheme.Violet, _app.NavigateBack);
            BuildTabs(root);

            if (_tab == 0)
            {
                BuildPersonal(root);
                return;
            }

            UITheme.Scroll(root, "Rank Scroll", out RectTransform content);
            var scroll = root.Find("Rank Scroll");
            UITheme.Anchor((RectTransform)scroll, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(36f, 42f), new Vector2(-36f, -222f));

            if (_tab == 1) BuildLocal(content);
            else BuildServer(content);
        }

        private void BuildTabs(RectTransform root)
        {
            var bar = UITheme.Img(root, "Tabs", new Color(0.02f, 0.04f, 0.075f, 0.4f));
            UITheme.Anchor((RectTransform)bar.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(36f, -216f), new Vector2(-36f, -158f));
            string[] labels = { "CAREER", "LOCAL BOARDS", "CIRCUIT GLOBAL" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var button = UITheme.Button(bar.transform, labels[i], labels[i],
                    i == _tab ? UITheme.Violet : UITheme.Muted,
                    () => { _app.Audio.PlayUiClick(); _tab = index; Rebuild(); }, UITheme.BodySize, i == _tab);
                UITheme.Anchor((RectTransform)button.transform, new Vector2(i / 3f, 0f), new Vector2((i + 1) / 3f, 1f),
                    new Vector2(6f, 8f), new Vector2(-6f, 8f));
            }
        }

        private void BuildPersonal(RectTransform root)
        {
            var profile = _app.Profile;
            var stats = profile.lifetime;
            var panel = ScreenKit.Card(root, "Career", new Color(0.035f, 0.065f, 0.12f, 0.94f));
            UITheme.Anchor((RectTransform)panel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(36f, -640f), new Vector2(-36f, -236f));

            var title = UITheme.Label(panel.transform, "Title", "CAREER RECORD  //  " + profile.playerName,
                UITheme.HeadingSize, UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -62f), new Vector2(-28f, -18f));

            BuildStat(panel.transform, "BEST DISTANCE", UITheme.FormatDistance(profile.bestDistance), 0, UITheme.Cyan);
            BuildStat(panel.transform, "BEST SCORE", UITheme.FormatNumber(profile.bestScore), 1, UITheme.Amber);
            BuildStat(panel.transform, "TOTAL RUNS", stats.totalRuns.ToString("N0"), 2, UITheme.White);
            BuildStat(panel.transform, "TOTAL DISTANCE", UITheme.FormatDistance(stats.totalDistance), 3, UITheme.White);
            BuildStat(panel.transform, "CREDITS COLLECTED", UITheme.FormatNumber(stats.totalCoins), 4, UITheme.Amber);
            BuildStat(panel.transform, "NEAR MISSES", stats.nearMisses.ToString("N0"), 5, UITheme.Magenta);

            BuildStat(panel.transform, "RACES COMPLETED", stats.racesCompleted.ToString("N0"), 6, UITheme.Lime);
            BuildStat(panel.transform, "RACES WON", stats.racesWon.ToString("N0"), 7, UITheme.Lime);
            BuildStat(panel.transform, "BEST CIRCUIT RANK", stats.bestRaceRank > 0 ? "#" + stats.bestRaceRank : "-", 8, UITheme.Lime);
            BuildStat(panel.transform, "SEASON LEVEL", profile.seasonLevel.ToString(), 9, UITheme.Violet);
            BuildStat(panel.transform, "LIFETIME JUMPS", stats.totalJumps.ToString("N0"), 10, UITheme.White);
            BuildStat(panel.transform, "LIFETIME SLIDES", stats.totalSlides.ToString("N0"), 11, UITheme.White);

            var modePanel = ScreenKit.Card(root, "Mode Bests", new Color(0.035f, 0.065f, 0.12f, 0.94f));
            UITheme.Anchor((RectTransform)modePanel.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(36f, 42f), new Vector2(-36f, -656f));
            var heading = UITheme.Label(modePanel.transform, "Heading", "PERSONAL BESTS BY PROTOCOL",
                UITheme.SmallSize, UITheme.Violet, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)heading.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -44f), new Vector2(-28f, -12f));

            UITheme.Scroll(modePanel.transform, "Best Scroll", out RectTransform content);
            var scroll = modePanel.transform.Find("Best Scroll");
            UITheme.Anchor((RectTransform)scroll, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(18f, 14f), new Vector2(-18f, -50f));
            var modes = GameModes.All;
            for (int i = 0; i < modes.Count; i++)
            {
                var best = profile.FindModeBest(modes[i].Id.ToString());
                if (best == null || best.distance <= 0f) continue;
                var row = UITheme.Img(content, "Best " + modes[i].Id, new Color(0.04f, 0.075f, 0.13f, 0.8f),
                    UITheme.Rounded, false);
                UITheme.Layout(row.gameObject, 58f, -1f, 58f);
                var mode = UITheme.Label(row.transform, "Mode", modes[i].Title, UITheme.BodySize,
                    Palette.Hex(modes[i].AccentHex), TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Stretch((RectTransform)mode.transform, 18f, 4f, 460f, 4f);
                var distance = UITheme.Label(row.transform, "Distance",
                    UITheme.FormatDistance(best.distance) + "   SCORE " + UITheme.FormatNumber(best.score),
                    UITheme.SmallSize, UITheme.White, TextAnchor.MiddleRight, FontStyle.Bold);
                UITheme.Stretch((RectTransform)distance.transform, 360f, 4f, 18f, 4f);
            }
        }

        private void BuildStat(Transform parent, string label, string value, int index, Color color)
        {
            int column = index % 3;
            int row = index / 3;
            float width = (800f - column * 8f) / 3f;
            float x = 28f + column * (width + 12f);
            float y = -96f - row * 88f;
            var stat = UITheme.Img(parent, label, new Color(0.045f, 0.08f, 0.14f, 0.72f),
                UITheme.Rounded, false);
            UITheme.Anchor((RectTransform)stat.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y - 68f), new Vector2(x + width, y));
            var valueLabel = UITheme.Label(stat.transform, "Value", value, 30, color, TextAnchor.MiddleLeft,
                FontStyle.Bold);
            UITheme.Anchor((RectTransform)valueLabel.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(16f, 22f), new Vector2(-12f, -8f));
            var nameLabel = UITheme.Label(stat.transform, "Name", label, 14, UITheme.Muted,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)nameLabel.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(16f, 6f), new Vector2(-12f, 26f));
        }

        private void BuildLocal(RectTransform content)
        {
            var entries = _app.Profile.localLeaderboard;
            if (entries == null || entries.Count == 0)
            {
                var empty = UITheme.Label(content, "Empty", "NO VERIFIED RUNS YET.\nFINISH A RUN TO OPEN YOUR LOCAL BOARD.",
                    UITheme.BodySize, UITheme.Dim, TextAnchor.MiddleCenter);
                UITheme.Layout(empty.gameObject, 120f, -1f, 120f);
                return;
            }
            var header = BuildBoardHeader(content, "YOUR TOP 20 RUNS", "OFFLINE + VERIFIED");
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var row = UITheme.Img(content, "Entry " + i, i % 2 == 0
                    ? new Color(0.04f, 0.075f, 0.13f, 0.9f)
                    : new Color(0.03f, 0.055f, 0.1f, 0.85f), UITheme.Rounded, false);
                UITheme.Layout(row.gameObject, 66f, -1f, 66f);
                var rank = UITheme.Label(row.transform, "Rank", "#" + (i + 1), 26,
                    i == 0 ? UITheme.Amber : UITheme.Violet, TextAnchor.MiddleCenter, FontStyle.Bold);
                UITheme.Anchor((RectTransform)rank.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(14f, 4f), new Vector2(86f, -4f));
                var name = UITheme.Label(row.transform, "Name", entry.playerName, UITheme.BodySize,
                    UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                    new Vector2(104f, 4f), new Vector2(-680f, -4f));
                var mode = UITheme.Label(row.transform, "Mode", GameModes.Get(entry.modeId).Title,
                    UITheme.SmallSize, UITheme.Dim, TextAnchor.MiddleLeft);
                UITheme.Anchor((RectTransform)mode.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(430f, 4f), new Vector2(700f, -4f));
                var distance = UITheme.Label(row.transform, "Distance", UITheme.FormatDistance(entry.distance),
                    UITheme.BodySize, UITheme.Cyan, TextAnchor.MiddleRight, FontStyle.Bold);
                UITheme.Anchor((RectTransform)distance.transform, new Vector2(1f, 0f), new Vector2(1f, 1f),
                    new Vector2(-330f, 4f), new Vector2(-170f, -4f));
                var status = UITheme.Badge(row.transform, "Status", entry.verified ? "VERIFIED" : "LOCAL",
                    entry.verified ? UITheme.Lime : UITheme.Muted, 120, 26);
                UITheme.Anchor((RectTransform)status.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-156f, -13f), new Vector2(-24f, 13f));
            }
        }

        private GameObject BuildBoardHeader(RectTransform content, string title, string subtitle)
        {
            var header = UITheme.Img(content, "Board Header", new Color(0.05f, 0.09f, 0.16f, 0.9f),
                UITheme.Rounded, false);
            UITheme.Layout(header.gameObject, 62f, -1f, 62f);
            var label = UITheme.Label(header.transform, "Label", title, UITheme.BodySize, UITheme.Violet,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)label.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(22f, 4f), new Vector2(-500f, -4f));
            var sub = UITheme.Label(header.transform, "Sub", subtitle, UITheme.SmallSize, UITheme.Muted,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Anchor((RectTransform)sub.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(500f, 4f), new Vector2(-22f, -4f));
            return header.gameObject;
        }

        private void BuildServer(RectTransform content)
        {
            var header = BuildBoardHeader(content, "GLOBAL CIRCUIT LADDER", _fetching ? "SYNCING..." : "SERVER AUTHORITY");
            if (_serverEntries == null)
            {
                var note = UITheme.Label(content, "Note",
                    "Connect to the race server to fetch verified circuit records.\nServer: " +
                    _app.Profile.settings.serverHost + ":" + _app.Profile.settings.serverPort,
                    UITheme.BodySize, UITheme.Dim, TextAnchor.MiddleCenter);
                UITheme.Layout(note.gameObject, 110f, -1f, 110f);
                var fetch = UITheme.Button(content, "Fetch", _fetching ? "CONTACTING..." : "CONNECT & FETCH",
                    UITheme.Lime, Fetch, UITheme.BodySize);
                UITheme.Layout(fetch.gameObject, 64f, -1f, 64f);
                if (_fetching) fetch.interactable = false;
                return;
            }
            if (_serverEntries.Count == 0)
            {
                var empty = UITheme.Label(content, "Empty", "THE SERVER HAS NO VERIFIED RACES YET.",
                    UITheme.BodySize, UITheme.Dim, TextAnchor.MiddleCenter);
                UITheme.Layout(empty.gameObject, 110f, -1f, 110f);
                return;
            }
            for (int i = 0; i < _serverEntries.Count; i++)
            {
                var entry = _serverEntries[i];
                var row = UITheme.Img(content, "Global " + i, i % 2 == 0
                    ? new Color(0.04f, 0.075f, 0.13f, 0.9f)
                    : new Color(0.03f, 0.055f, 0.1f, 0.85f), UITheme.Rounded, false);
                UITheme.Layout(row.gameObject, 66f, -1f, 66f);
                var rank = UITheme.Label(row.transform, "Rank", "#" + entry.rank, 26,
                    entry.rank == 1 ? UITheme.Amber : UITheme.Violet, TextAnchor.MiddleCenter, FontStyle.Bold);
                UITheme.Anchor((RectTransform)rank.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(14f, 4f), new Vector2(86f, -4f));
                var name = UITheme.Label(row.transform, "Name", entry.name, UITheme.BodySize, UITheme.White,
                    TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                    new Vector2(104f, 4f), new Vector2(-680f, -4f));
                var mode = UITheme.Label(row.transform, "Mode", GameModes.Get(entry.mode).Title, UITheme.SmallSize,
                    UITheme.Dim, TextAnchor.MiddleLeft);
                UITheme.Anchor((RectTransform)mode.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(430f, 4f), new Vector2(700f, -4f));
                var distance = UITheme.Label(row.transform, "Distance", UITheme.FormatDistance(entry.distance),
                    UITheme.BodySize, UITheme.Cyan, TextAnchor.MiddleRight, FontStyle.Bold);
                UITheme.Anchor((RectTransform)distance.transform, new Vector2(1f, 0f), new Vector2(1f, 1f),
                    new Vector2(-330f, 4f), new Vector2(-170f, -4f));
                var score = UITheme.Label(row.transform, "Score", UITheme.FormatNumber(entry.score),
                    UITheme.SmallSize, UITheme.Muted, TextAnchor.MiddleRight);
                UITheme.Anchor((RectTransform)score.transform, new Vector2(1f, 0f), new Vector2(1f, 1f),
                    new Vector2(-166f, 4f), new Vector2(-26f, -4f));
            }
            var refresh = UITheme.Button(content, "Refresh", "REFRESH GLOBAL LADDER", UITheme.Lime,
                Fetch, UITheme.BodySize);
            UITheme.Layout(refresh.gameObject, 58f, -1f, 58f);
        }

        private void Fetch()
        {
            if (_fetching) return;
            var settings = _app.Profile.settings;
            _fetching = true;
            _requestSent = false;
            _serverEntries = null;
            _app.Audio.PlayUiClick();
            _app.Race.StateChanged += OnState;
            _app.Race.MessageReceived += OnMessage;
            _ownsConnection = true;
            if (_app.Race.IsConnected)
            {
                RequestLeaderboard();
            }
            else
            {
                _app.Race.ConnectFresh(settings.serverHost, settings.serverPort);
            }
            _app.UI.Toast("CONTACTING CIRCUIT AUTHORITY...", UITheme.Cyan, 1.8f);
        }

        private void OnState(RaceClient.NetState state, string detail)
        {
            if (state == RaceClient.NetState.Connected && !_requestSent)
            {
                RequestLeaderboard();
            }
            else if (state == RaceClient.NetState.Failed)
            {
                _fetching = false;
                _app.UI.Toast(string.IsNullOrEmpty(detail) ? "SERVER UNAVAILABLE" : detail, UITheme.Red, 3f);
                Cleanup();
                Rebuild();
            }
        }

        private void RequestLeaderboard()
        {
            if (_requestSent) return;
            _requestSent = true;
            var request = _app.Race.NewMessage("leaderboard");
            request.mode = "circuit";
            _app.Race.Send(request);
        }

        private void OnMessage(WireMessage message)
        {
            if (message == null) return;
            if (message.type == "welcome" || message.type == "welcome_back")
            {
                RequestLeaderboard();
                return;
            }
            if (message.type == "leaderboard")
            {
                _serverEntries = message.leaderboard ?? new List<WireLeaderboardEntry>();
                _fetching = false;
                Cleanup();
                Rebuild();
            }
            else if (message.type == "error")
            {
                _fetching = false;
                _app.UI.Toast(message.error, UITheme.Red, 3f);
                Cleanup();
                Rebuild();
            }
        }

        private void Cleanup()
        {
            _app.Race.StateChanged -= OnState;
            _app.Race.MessageReceived -= OnMessage;
            if (_ownsConnection)
            {
                _app.Race.Disconnect(true);
                _ownsConnection = false;
            }
        }

        private void Rebuild()
        {
            _app.UI.ShowScreen(Build(_app, _ui, _tab, _serverEntries, _fetching, _requestSent));
        }

        private void OnDestroy()
        {
            Cleanup();
        }
    }
}
