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
    /// Matchmaking and lobby for the authoritative five-player circuit. The screen only sends
    /// intent (quick match, room code, ready, start); every roster/start/result decision comes
    /// back from the server. Bots are added by the authority when the host starts with open slots.
    /// </summary>
    public sealed class MultiplayerScreen : MonoBehaviour, IScreenBackHandler
    {
        private enum Pending
        {
            None,
            QuickMatch,
            CreateRoom,
            JoinRoom
        }

        private sealed class LobbyRow
        {
            public GameObject Root;
            public Text Name;
            public Text Meta;
            public Text Slot;
            public Image Background;
        }

        private GameApp _app;
        private UIRoot _ui;
        private Pending _pending;
        private string _joinCode = "";
        private bool _launched;

        private Text _status;
        private Image _statusDot;
        private Text _serverLabel;
        private Text _roomCode;
        private Text _roomState;
        private Text _rosterCount;
        private Text _hint;
        private InputField _codeInput;
        private Button _quickButton;
        private Button _createButton;
        private Button _joinButton;
        private Button _readyButton;
        private Button _startButton;
        private Button _leaveButton;
        private Button _copyButton;
        private readonly List<LobbyRow> _rows = new List<LobbyRow>(5);

        private RaceClient Client => _app.Race;
        private WireMessage Room => Client.LastRoomState;
        private bool InRoom => Room != null && !string.IsNullOrEmpty(Room.roomCode);
        private bool IsHost => InRoom && Room.hostId == Client.PlayerId;
        private bool LocalReady
        {
            get
            {
                if (Room?.players == null) return false;
                for (int i = 0; i < Room.players.Count; i++)
                    if (Room.players[i].playerId == Client.PlayerId) return Room.players[i].ready;
                return false;
            }
        }

        public static GameObject Build(GameApp app, UIRoot ui)
        {
            var root = ScreenKit.Root("Multiplayer");
            var screen = root.gameObject.AddComponent<MultiplayerScreen>();
            screen._app = app;
            screen._ui = ui;
            screen.BuildLayout();
            screen.Connect();
            return root.gameObject;
        }

        private void BuildLayout()
        {
            var root = (RectTransform)transform;
            ScreenKit.Backdrop(root, UITheme.Lime);
            ScreenKit.Header(root, _app, "MULTIPLAYER", "NEON CIRCUIT",
                "Five runners. One deterministic track. Server-authoritative results.",
                UITheme.Lime, Back);

            BuildConnectionPanel(root);
            BuildLobbyPanel(root);

            var note = UITheme.Label(root, "Note",
                "60 Hz snapshots  \u00B7  120-second shared circuit  \u00B7  disconnect grace 45 s  \u00B7  " +
                "bots fill open slots on host start",
                UITheme.SmallSize, UITheme.Muted, TextAnchor.MiddleCenter);
            UITheme.Anchor((RectTransform)note.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(36f, 14f), new Vector2(-36f, 42f));
            note.gameObject.name = "Protocol Note";
        }

        private void BuildConnectionPanel(RectTransform root)
        {
            var panel = ScreenKit.Card(root, "Connection", new Color(0.035f, 0.065f, 0.12f, 0.94f));
            UITheme.Anchor((RectTransform)panel.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(36f, 62f), new Vector2(568f, -158f));

            var heading = UITheme.Label(panel.transform, "Heading", "CONNECTION", UITheme.SmallSize,
                UITheme.Lime, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)heading.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -48f), new Vector2(-26f, -16f));

            _statusDot = UITheme.Img(panel.transform, "Dot", UITheme.Muted, TextureFactory.Diamond, false);
            UITheme.Anchor((RectTransform)_statusDot.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(26f, -86f), new Vector2(42f, -70f));

            _status = UITheme.Label(panel.transform, "Status", "OFFLINE", UITheme.SmallSize, UITheme.Muted,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_status.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(50f, -90f), new Vector2(-24f, -60f));

            _serverLabel = UITheme.Label(panel.transform, "Server", "", UITheme.SmallSize, UITheme.Dim,
                TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)_serverLabel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -122f), new Vector2(-24f, -92f));
            _serverLabel.text = _app.Profile.settings.serverHost + ":" + _app.Profile.settings.serverPort;

            var separator = UITheme.Img(panel.transform, "Separator", new Color(1f, 1f, 1f, 0.08f));
            UITheme.Anchor((RectTransform)separator.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -136f), new Vector2(-26f, -134f));

            _quickButton = UITheme.Button(panel.transform, "Quick", "QUICK MATCH", UITheme.Lime,
                QuickMatch, UITheme.BodySize);
            UITheme.Anchor((RectTransform)_quickButton.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -212f), new Vector2(-26f, -152f));

            _createButton = UITheme.Button(panel.transform, "Create", "CREATE PRIVATE ROOM", UITheme.Magenta,
                CreatePrivate, UITheme.BodySize);
            UITheme.Anchor((RectTransform)_createButton.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -286f), new Vector2(-26f, -226f));

            var joinLabel = UITheme.Label(panel.transform, "Join Label", "JOIN WITH ROOM CODE", UITheme.SmallSize,
                UITheme.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)joinLabel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -322f), new Vector2(-24f, -292f));

            _codeInput = UITheme.Input(panel.transform, "Code", "", "6-CHARACTER CODE", UITheme.SmallSize);
            UITheme.Anchor((RectTransform)_codeInput.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -384f), new Vector2(-150f, -330f));
            _codeInput.characterLimit = 8;
            _codeInput.onEndEdit.AddListener(value => _joinCode = value);

            _joinButton = UITheme.Button(panel.transform, "Join", "JOIN", UITheme.Cyan, JoinPrivate,
                UITheme.BodySize);
            UITheme.Anchor((RectTransform)_joinButton.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-138f, -384f), new Vector2(-26f, -330f));

            var divider = UITheme.Img(panel.transform, "Divider", new Color(1f, 1f, 1f, 0.08f));
            UITheme.Anchor((RectTransform)divider.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -406f), new Vector2(-26f, -404f));

            var info = UITheme.Label(panel.transform, "Info",
                "HOW IT WORKS\n\n1  Connect to the race authority.\n" +
                "2  Quick match joins the open public lobby.\n" +
                "3  Every human toggles READY.\n" +
                "4  The host starts; bots fill empty slots.\n" +
                "5  The server locks distance, ranks results and grants verified rewards.\n\n" +
                "A dropped runner has 45 seconds to reconnect with a server token. " +
                "Unverified results never grant rewards.",
                UITheme.SmallSize, UITheme.Dim, TextAnchor.UpperLeft);
            UITheme.Anchor((RectTransform)info.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(26f, 24f), new Vector2(-24f, -420f));
        }

        private void BuildLobbyPanel(RectTransform root)
        {
            var panel = ScreenKit.Card(root, "Lobby", new Color(0.025f, 0.05f, 0.095f, 0.94f));
            UITheme.Anchor((RectTransform)panel.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(586f, 62f), new Vector2(-36f, -158f));

            var heading = UITheme.Label(panel.transform, "Heading", "CIRCUIT LOBBY", UITheme.SmallSize,
                UITheme.Lime, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)heading.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -48f), new Vector2(-28f, -16f));

            _roomCode = UITheme.Label(panel.transform, "Room Code", "NO ROOM", 34, UITheme.White,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_roomCode.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -96f), new Vector2(-300f, -52f));

            _roomState = UITheme.Label(panel.transform, "Room State", "CONNECTING TO AUTHORITY...", UITheme.SmallSize,
                UITheme.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_roomState.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(30f, -126f), new Vector2(-300f, -98f));

            _copyButton = UITheme.Button(panel.transform, "Copy", "COPY CODE", UITheme.Cyan,
                CopyCode, UITheme.SmallSize, false);
            UITheme.Anchor((RectTransform)_copyButton.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-240f, -100f), new Vector2(-28f, -50f));

            _rosterCount = UITheme.Label(panel.transform, "Count", "0 / 5 RUNNERS", UITheme.SmallSize,
                UITheme.Lime, TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_rosterCount.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(300f, -100f), new Vector2(-260f, -52f));

            var roster = UITheme.NewRect(panel.transform, "Roster");
            UITheme.Anchor(roster, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -566f), new Vector2(-28f, -146f));
            for (int i = 0; i < RaceRules.MaxPlayers; i++) BuildRow(roster, i);

            _hint = UITheme.Label(panel.transform, "Hint",
                "Quick match or enter a private code to enter the lobby.",
                UITheme.SmallSize, UITheme.Dim, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)_hint.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(28f, 100f), new Vector2(-28f, 134f));

            _readyButton = UITheme.Button(panel.transform, "Ready", "READY", UITheme.Cyan, ToggleReady,
                UITheme.BodySize);
            UITheme.Anchor((RectTransform)_readyButton.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(28f, 28f), new Vector2(268f, 86f));

            _startButton = UITheme.Button(panel.transform, "Start", "START CIRCUIT", UITheme.Lime, StartRace,
                UITheme.BodySize);
            UITheme.Anchor((RectTransform)_startButton.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(282f, 28f), new Vector2(582f, 86f));

            _leaveButton = UITheme.Button(panel.transform, "Leave", "LEAVE LOBBY", UITheme.Red, LeaveRoom,
                UITheme.BodySize, false);
            UITheme.Anchor((RectTransform)_leaveButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-238f, 28f), new Vector2(-28f, 86f));
        }

        private void BuildRow(RectTransform roster, int index)
        {
            var row = UITheme.Img(roster, "Runner " + index,
                index % 2 == 0 ? new Color(0.04f, 0.075f, 0.13f, 0.9f) : new Color(0.03f, 0.055f, 0.1f, 0.85f),
                UITheme.Rounded, false);
            UITheme.Anchor((RectTransform)row.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -index * 84f - 78f), new Vector2(0f, -index * 84f));

            var slot = UITheme.Label(row.transform, "Slot", (index + 1).ToString(), 24, UITheme.Lime,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)slot.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(14f, 4f), new Vector2(74f, -4f));

            var name = UITheme.Label(row.transform, "Name", "OPEN SLOT", UITheme.BodySize, UITheme.Dim,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 0.35f), new Vector2(1f, 1f),
                new Vector2(88f, 0f), new Vector2(-360f, -6f));

            var meta = UITheme.Label(row.transform, "Meta", "AWAITING RUNNER", UITheme.SmallSize,
                UITheme.Muted, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)meta.transform, new Vector2(0f, 0f), new Vector2(1f, 0.4f),
                new Vector2(88f, 6f), new Vector2(-360f, 0f));

            var state = UITheme.Label(row.transform, "State", "", UITheme.SmallSize, UITheme.Muted,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Anchor((RectTransform)state.transform, new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-286f, 4f), new Vector2(-24f, -4f));

            _rows.Add(new LobbyRow
            {
                Root = row.gameObject, Name = name, Meta = meta, Slot = state, Background = row
            });
        }

        // ---- Network lifecycle ------------------------------------------------------------

        private void Connect()
        {
            var settings = _app.Profile.settings;
            Client.StateChanged -= OnState;
            Client.MessageReceived -= OnMessage;
            Client.StateChanged += OnState;
            Client.MessageReceived += OnMessage;
            Client.SetPlayerName(_app.Profile.playerName);
            SetStatus("CONNECTING...", UITheme.Amber);
            if (!Client.IsConnected)
                Client.ConnectFresh(settings.serverHost, settings.serverPort);
            else
                SetStatus("LINK ESTABLISHED", UITheme.Lime);
            UpdateControls();
        }

        private void QuickMatch()
        {
            if (_launched) return;
            _app.Audio.PlayUiClick();
            _pending = Pending.QuickMatch;
            if (Client.IsConnected) SendPending();
            else
            {
                SetStatus("CONNECTING FOR QUICK MATCH...", UITheme.Amber);
                var s = _app.Profile.settings;
                Client.ConnectFresh(s.serverHost, s.serverPort);
            }
            UpdateControls();
        }

        private void CreatePrivate()
        {
            if (_launched) return;
            _app.Audio.PlayUiClick();
            _pending = Pending.CreateRoom;
            if (Client.IsConnected) SendPending();
            else
            {
                SetStatus("CONNECTING TO CREATE ROOM...", UITheme.Amber);
                var s = _app.Profile.settings;
                Client.ConnectFresh(s.serverHost, s.serverPort);
            }
            UpdateControls();
        }

        private void JoinPrivate()
        {
            if (_launched) return;
            string code = (_codeInput != null ? _codeInput.text : _joinCode);
            code = string.IsNullOrEmpty(code) ? "" : code.Trim().ToUpperInvariant();
            if (code.Length < 4)
            {
                _ui.Toast("ENTER A VALID ROOM CODE", UITheme.Red, 2.4f);
                return;
            }
            _joinCode = code;
            _app.Audio.PlayUiClick();
            _pending = Pending.JoinRoom;
            if (Client.IsConnected) SendPending();
            else
            {
                SetStatus("CONNECTING TO PRIVATE ROOM...", UITheme.Amber);
                var s = _app.Profile.settings;
                Client.ConnectFresh(s.serverHost, s.serverPort);
            }
            UpdateControls();
        }

        private void SendPending()
        {
            if (_pending == Pending.None) return;
            if (!Client.IsConnected)
            {
                SetStatus("WAITING FOR LINK...", UITheme.Amber);
                return;
            }
            Pending action = _pending;
            WireMessage message;
            switch (action)
            {
                case Pending.QuickMatch:
                    message = Client.NewMessage("quick_match");
                    message.mode = "online";
                    break;
                case Pending.CreateRoom:
                    message = Client.NewMessage("create_room");
                    message.mode = "private";
                    message.privateRoom = true;
                    break;
                default:
                    message = Client.NewMessage("join_room");
                    message.mode = "private";
                    message.roomCode = _joinCode;
                    message.privateRoom = true;
                    break;
            }
            Client.Send(message);
            _pending = Pending.None;
            SetStatus(action == Pending.QuickMatch ? "SEARCHING FOR CIRCUIT..." : "REQUESTING ROOM...", UITheme.Amber);
        }

        private void OnState(RaceClient.NetState state, string detail)
        {
            if (this == null) return;
            switch (state)
            {
                case RaceClient.NetState.Connecting:
                    SetStatus("CONNECTING...", UITheme.Amber);
                    break;
                case RaceClient.NetState.Connected:
                    SetStatus("LINK ESTABLISHED - AUTHENTICATING...", UITheme.Cyan);
                    break;
                case RaceClient.NetState.Reconnecting:
                    SetStatus("SIGNAL LOST - RESTORING LINK...", UITheme.Amber);
                    break;
                case RaceClient.NetState.Failed:
                    SetStatus(string.IsNullOrEmpty(detail) ? "SERVER UNAVAILABLE" : detail, UITheme.Red);
                    _ui.Toast("CIRCUIT SERVER UNAVAILABLE", UITheme.Red, 3f);
                    break;
                case RaceClient.NetState.Disconnected:
                    SetStatus("DISCONNECTED", UITheme.Muted);
                    break;
            }
            UpdateControls();
        }

        private void OnMessage(WireMessage message)
        {
            if (this == null || message == null) return;
            switch (message.type)
            {
                case "welcome":
                case "welcome_back":
                    SetStatus("LINK ESTABLISHED - " + (message.name ?? Client.PlayerName), UITheme.Lime);
                    if (_pending != Pending.None) SendPending();
                    break;
                case "room_state":
                case "state_sync":
                    ApplyRoom(message);
                    break;
                case "countdown":
                    if (message.startAtMs > 0) Launch(message);
                    break;
                case "race_start":
                    if (!_launched && message.startAtMs > 0) Launch(message);
                    break;
                case "left_room":
                    Client.ClearRoomState();
                    SetStatus("LINK ESTABLISHED", UITheme.Lime);
                    UpdateLobby();
                    UpdateControls();
                    break;
                case "error":
                    _ui.Toast(string.IsNullOrEmpty(message.error) ? "SERVER REJECTED REQUEST" : message.error,
                        UITheme.Red, 3f);
                    SetStatus(string.IsNullOrEmpty(message.error) ? "REQUEST REJECTED" : message.error,
                        UITheme.Red);
                    _pending = Pending.None;
                    UpdateControls();
                    break;
            }
        }

        private void ApplyRoom(WireMessage message)
        {
            // Both room_state and state_sync carry the same flat payload; LastRoomState is updated
            // by RaceClient before dispatch, so read from that single source.
            UpdateLobby();
            UpdateControls();
            if (message.phase == "countdown" && message.startAtMs > 0)
            {
                Launch(message);
            }
            else if (message.phase == "racing" && message.startAtMs > 0 && !_launched)
            {
                Launch(message);
            }
        }

        private void UpdateLobby()
        {
            var room = Room;
            if (room == null || string.IsNullOrEmpty(room.roomCode))
            {
                _roomCode.text = "NO ROOM";
                _roomState.text = Client.IsConnected ? "CONNECTED - SELECT A MATCH TYPE" : "OFFLINE";
                _rosterCount.text = "0 / 5 RUNNERS";
                for (int i = 0; i < _rows.Count; i++) SetRowEmpty(i);
                _hint.text = "Quick match or enter a private code to enter the lobby.";
                if (_copyButton != null) _copyButton.interactable = false;
                return;
            }

            _roomCode.text = "ROOM  " + room.roomCode;
            _roomState.text = (room.privateRoom ? "PRIVATE CIRCUIT" : "PUBLIC CIRCUIT") + "  \u00B7  " +
                PhaseLabel(room.phase) + (IsHost ? "  \u00B7  YOU ARE HOST" : "");
            int count = room.players != null ? room.players.Count : 0;
            _rosterCount.text = count + " / " + RaceRules.MaxPlayers + " RUNNERS";
            if (_copyButton != null) _copyButton.interactable = room.privateRoom;

            for (int i = 0; i < _rows.Count; i++)
            {
                if (room.players != null && i < room.players.Count) SetRowPlayer(i, room.players[i], room);
                else SetRowEmpty(i);
            }

            if (room.phase != "lobby" && room.phase != null)
                _hint.text = "DEPLOYING TO THE CIRCUIT...";
            else if (IsHost && AllHumansReady(room))
                _hint.text = "ALL RUNNERS READY. START WHEN YOU ARE.";
            else if (IsHost)
                _hint.text = "WAITING FOR EVERY RUNNER TO READY. BOTS FILL OPEN SLOTS ON START.";
            else
                _hint.text = "WAITING FOR HOST TO START THE CIRCUIT.";
        }

        private static string PhaseLabel(string phase)
        {
            switch (phase)
            {
                case "countdown": return "COUNTDOWN";
                case "racing": return "RACING";
                case "finished": return "FINISHED";
                default: return "LOBBY";
            }
        }

        private static bool AllHumansReady(WireMessage room)
        {
            if (room.players == null || room.players.Count == 0) return false;
            for (int i = 0; i < room.players.Count; i++)
            {
                if (room.players[i].bot) continue;
                if (!room.players[i].ready) return false;
            }
            return true;
        }

        private void SetRowPlayer(int index, WirePlayer player, WireMessage room)
        {
            var row = _rows[index];
            row.Root.SetActive(true);
            row.Name.text = player.name;
            row.Name.color = player.playerId == Client.PlayerId ? UITheme.Cyan : UITheme.White;
            string tags = "";
            if (player.bot) tags += "BOT  ";
            if (player.host) tags += "HOST  ";
            if (player.playerId == Client.PlayerId) tags += "YOU  ";
            tags += player.connected ? "LIVE" : "RECONNECTING";
            row.Meta.text = tags;
            row.Slot.text = player.ready ? "READY" : "NOT READY";
            row.Slot.color = player.ready ? UITheme.Lime : UITheme.Muted;
            row.Background.color = player.playerId == Client.PlayerId
                ? new Color(0.06f, 0.13f, 0.17f, 0.95f)
                : new Color(0.04f, 0.075f, 0.13f, 0.9f);
            row.Name.gameObject.SetActive(true);
            row.Meta.gameObject.SetActive(true);
        }

        private void SetRowEmpty(int index)
        {
            var row = _rows[index];
            row.Root.SetActive(true);
            row.Name.text = "OPEN SLOT";
            row.Name.color = UITheme.Dim;
            row.Name.gameObject.SetActive(true);
            row.Meta.gameObject.SetActive(true);
            row.Meta.text = "BOT WILL FILL ON START";
            row.Slot.text = "OPEN";
            row.Slot.color = UITheme.Muted;
            row.Background.color = new Color(0.025f, 0.045f, 0.08f, 0.72f);
        }

        private void UpdateControls()
        {
            bool connected = Client.IsConnected;
            bool room = InRoom;
            if (_quickButton != null)
            {
                _quickButton.interactable = connected && !room && !_launched;
                _createButton.interactable = connected && !room && !_launched;
                _joinButton.interactable = connected && !room && !_launched;
            }
            if (_readyButton == null) return;
            _readyButton.interactable = room && !_launched;
            var readyText = _readyButton.GetComponentInChildren<Text>();
            if (readyText != null) readyText.text = LocalReady ? "CANCEL READY" : "READY";
            _readyButton.GetComponent<Image>().color = LocalReady
                ? new Color(0.22f, 0.32f, 0.17f, 0.96f)
                : new Color(0.09f, 0.15f, 0.27f, 0.96f);

            bool canStart = room && IsHost && !_launched && AllHumansReady(Room);
            _startButton.interactable = canStart;
            var startText = _startButton.GetComponentInChildren<Text>();
            if (startText != null)
                startText.text = IsHost ? (canStart ? "START CIRCUIT" : "AWAITING READY") : "HOST STARTS";
            _leaveButton.interactable = room;
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            _status.text = text;
            _status.color = color;
            if (_statusDot != null) _statusDot.color = color;
        }

        private void ToggleReady()
        {
            if (!InRoom || _launched) return;
            _app.Audio.PlayUiClick();
            var message = Client.NewMessage("ready");
            message.ready = !LocalReady;
            Client.Send(message);
        }

        private void StartRace()
        {
            if (!InRoom || _launched) return;
            if (!IsHost)
            {
                _ui.Toast("ONLY THE HOST CAN START", UITheme.Red, 2.2f);
                return;
            }
            if (!AllHumansReady(Room))
            {
                _ui.Toast("EVERY RUNNER MUST BE READY", UITheme.Red, 2.2f);
                return;
            }
            _app.Audio.PlayUiClick();
            var message = Client.NewMessage("start_race");
            message.mode = Room.privateRoom ? "private" : "online";
            Client.Send(message);
            SetStatus("START AUTHORISED - WAITING FOR COUNTDOWN...", UITheme.Lime);
            _startButton.interactable = false;
        }

        private void Launch(WireMessage source)
        {
            if (_launched) return;
            var room = Room;
            var seedSource = room != null && room.seed != 0 ? room : source;
            int seed = seedSource.seed != 0 ? seedSource.seed : 1;
            long startAt = source.startAtMs > 0 ? source.startAtMs
                : (room != null ? room.startAtMs : 0);
            float duration = source.raceDurationSeconds > 0f
                ? source.raceDurationSeconds
                : (room != null && room.raceDurationSeconds > 0f
                    ? room.raceDurationSeconds : RaceRules.RaceDurationSeconds);
            string raceId = !string.IsNullOrEmpty(source.raceId)
                ? source.raceId : (room != null ? room.raceId : "");
            // Prefer the launch packet: it is the authoritative roster including bots.
            var players = source.players != null && source.players.Count > 0
                ? new List<WirePlayer>(source.players)
                : (room != null && room.players != null
                    ? new List<WirePlayer>(room.players)
                    : new List<WirePlayer>());
            bool isPrivate = room != null && room.privateRoom;
            var spec = GameModes.Get(isPrivate ? GameModeId.PrivateRace : GameModeId.OnlineRace);

            _launched = true;
            _pending = Pending.None;
            _app.Audio.PlayUiClick();
            _ui.Toast("DEPLOYING TO THE CIRCUIT", UITheme.Lime, 1.6f);
            _app.StartOnlineSession(spec, seed, startAt, duration, players, raceId);
        }

        private void LeaveRoom()
        {
            if (Client.IsConnected)
            {
                var leave = Client.NewMessage("leave_room");
                Client.Send(leave);
            }
            Client.ClearRoomState();
            Client.Disconnect(true);
            _pending = Pending.None;
            SetStatus("DISCONNECTED", UITheme.Muted);
            UpdateLobby();
            UpdateControls();
        }

        private void CopyCode()
        {
            if (Room == null || string.IsNullOrEmpty(Room.roomCode)) return;
            GUIUtility.systemCopyBuffer = Room.roomCode;
            _app.Audio.PlayUiClick();
            _ui.Toast("ROOM CODE COPIED", UITheme.Cyan, 2f);
        }

        private void Back()
        {
            if (_launched) return;
            _app.Audio.PlayUiClick();
            if (InRoom)
            {
                _ui.Toast("LEFT THE LOBBY", UITheme.Muted, 1.8f);
                Client.ClearRoomState();
            }
            _app.NavigateBack();
        }

        public bool HandleBackRequest()
        {
            Back();
            return true;
        }

        private void OnDestroy()
        {
            if (Client != null)
            {
                Client.StateChanged -= OnState;
                Client.MessageReceived -= OnMessage;
                if (!_launched)
                {
                    if (Client.IsConnected)
                    {
                        var leave = Client.NewMessage("leave_room");
                        Client.Send(leave);
                    }
                    Client.Disconnect(true);
                    Client.ClearRoomState();
                }
            }
        }
    }
}
