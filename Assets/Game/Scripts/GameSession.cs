using System;
using System.Collections;
using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Effects;
using NeonVelocity.Multiplayer;
using NeonVelocity.Player;
using NeonVelocity.Progression;
using NeonVelocity.UI;
using NeonVelocity.Utilities;
using NeonVelocity.World;
using UnityEngine;

namespace NeonVelocity
{
    public sealed class RunResult
    {
        public GameModeId ModeId;
        public string ModeTitle;
        public float Distance;
        public float Time;
        public int Score;
        public int Coins;
        public int Jumps;
        public int Slides;
        public int NearMisses;
        public int PowerUpsUsed;
        public int CreditsEarned;
        public int XpEarned;
        public int Placement;
        public int TotalPlayers;
        public bool Crashed;
        public bool Online;
        public bool Verified;
        public bool NewBest;
        public string FailureNote;
        public string RaceId;
        public List<string> Messages = new List<string>();
    }

    /// <summary>
    /// Owns one run: generated track, runner, camera, environment and the rules of the selected
    /// mode. Offline runs resolve locally; online runs lock their final distance on the server
    /// and only display verified rewards returned by the authority.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        public ModeSpec Mode { get; private set; }
        public OnlineRaceContext Online { get; private set; }
        public TrackManager Track { get; private set; }
        public RunnerController Runner { get; private set; }
        public RunnerCamera CameraRig { get; private set; }
        public EnvironmentController Environment { get; private set; }
        public VfxService Vfx { get; private set; }
        public InputRouter Input { get; private set; }
        public bool CountdownVisible { get; private set; }
        public string CountdownText { get; private set; } = "";
        public bool IsPaused { get; private set; }
        public bool IsFinished { get; private set; }
        public bool IsLocalRunOver { get; private set; }
        public bool RaceStarted { get; private set; }
        public float RaceElapsed { get; private set; }
        public float RaceRemaining => Mathf.Max(0f, _duration - RaceElapsed);
        public float Duration => _duration;
        public float Distance => Runner != null ? Runner.Distance : 0f;
        public int Score => Runner != null ? Runner.Score : 0;
        public int Coins => Runner != null ? Runner.Coins : 0;
        public int Lives => Runner != null ? Runner.Lives : 1;
        public float Speed => Runner != null ? Runner.Speed : 0f;
        public string ObjectiveLabel { get; private set; } = "";
        public float ObjectiveProgress { get; private set; }
        public bool IsOnline => Online != null;
        public int LocalPlacement { get; private set; }
        public int OnlinePlayerCount { get; private set; }
        public IReadOnlyList<WirePlayer> OnlinePlayers => _players;
        public string ConnectionWarning { get; private set; } = "";
        public string Prompt { get; private set; } = "";
        public HUDScreen Hud { get; private set; }

        private GameApp _app;
        private int _seed;
        private float _countdown;
        private float _duration;
        private long _startAtMs;
        private long _raceEndAtMs;
        private float _snapshotTimer;
        private float _resultsDeadline;
        private bool _serverResultsApplied;
        private bool _localFinishSent;
        private float _lastCountdownBeep;
        private int _lastPrompt = -1;
        private readonly List<WirePlayer> _players = new List<WirePlayer>();
        private readonly Dictionary<string, RemoteRunner> _remotes = new Dictionary<string, RemoteRunner>();
        private RaceClient Net => Online?.Client;

        private static readonly float[] TutorialDistances = { 10f, 80f, 170f, 260f, 300f };
        private static readonly string[] TutorialPrompts =
        {
            "A / D  OR SWIPE LEFT / RIGHT  -  SWITCH LANES",
            "SPACE  OR SWIPE UP  -  JUMP OVER HAZARDS",
            "S  OR SWIPE DOWN  -  SLIDE UNDER LASERS",
            "NEAR MISSES BUILD BONUS SCORE -  HOLD YOUR LINE",
            "TRAINING COMPLETE"
        };

        public void Begin(GameApp app, ModeSpec mode, int seed, OnlineRaceContext online)
        {
            _app = app;
            Mode = mode;
            _seed = seed;
            Online = online;
            _duration = online != null ? online.DurationSeconds : (mode.IsTimed ? mode.DurationSeconds : 0f);
            if (online != null)
            {
                _startAtMs = online.StartAtMs;
                _raceEndAtMs = _startAtMs + (long)(_duration * 1000f);
                foreach (var player in online.Players) AddOrReplacePlayer(player);
            }

            var worldGo = new GameObject("Generated World");
            worldGo.transform.SetParent(transform, false);
            Track = worldGo.AddComponent<TrackManager>();
            Track.Initialise(app.Config, mode, seed);
            Track.ResetForRun();
            Track.Tick(0f);

            var inputGo = new GameObject("Input");
            inputGo.transform.SetParent(transform, false);
            Input = inputGo.AddComponent<InputRouter>();
            Input.SetSensitivity(app.Profile.settings.swipeSensitivity);
            Input.Enabled = false;

            var runnerGo = new GameObject("Runner");
            runnerGo.transform.SetParent(transform, false);
            Runner = runnerGo.AddComponent<RunnerController>();
            Runner.Begin(app, app.Config, mode, Track, Input, seed);

            Vfx = VfxService.Create(transform, app.Profile.settings.reducedMotion, 24);
            Runner.BindVfx(Vfx);
            Vfx.CreateEngineTrail(Runner.transform, app.Config.Character(app.Profile.selectedCharacter).trail);

            CameraRig = RunnerCamera.Create(transform, Runner, app.Profile.settings.reducedMotion,
                app.Profile.settings.cameraShake, app.Profile.settings.graphicsPreset);
            CameraRig.transform.position = new Vector3(0f, 4.2f, Runner.PlayerZ - 6.7f);
            Environment = EnvironmentController.Create(transform, app, mode.Id, Track, Runner, CameraRig.Camera,
                app.Profile.settings.reducedMotion, app.Profile.settings.graphicsPreset);
            var theme = app.Config.Theme(Track.CurrentBiome < 0 ? 0 : Track.CurrentBiome);
            Vfx.CreateSpeedLines(CameraRig.Camera, theme != null ? theme.accent : Palette.Cyan);
            Vfx.SetEngineTrailColor(theme != null ? theme.accent : Palette.Cyan);

            Runner.Died += OnRunnerDied;
            Runner.Crashed += OnRunnerCrashed;
            Runner.PowerUpCollected += OnPowerUpCollected;
            Runner.CreditsCollected += OnCreditsCollected;
            Runner.WorldRebased += OnWorldRebased;
            Track.BiomeChanged += OnBiomeChanged;
            Track.BiomeChanged += _ => UpdateObjectiveLabel();

            if (online != null)
            {
                Net.MessageReceived += OnNetMessage;
                Net.StateChanged += OnNetState;
                if (Net.HasRoomState) ApplyRoomState(Net.LastRoomState);
                _resultsDeadline = Time.unscaledTime + _duration + 30f;
            }

            if (online == null)
            {
                if (mode.IsTimed) _countdown = 3.1f;
                else _countdown = 2.6f;
                CountdownVisible = true;
                CountdownText = "3";
                App.Audio.PlaySfx("sfx_countdown", 0.7f);
            }
            else
            {
                _countdown = Mathf.Clamp((_startAtMs - Net.ServerNowMs) / 1000f, 0f, 8f);
                CountdownVisible = true;
                CountdownText = Mathf.Max(1, Mathf.CeilToInt(_countdown)).ToString();
            }

            Input.ResetTransient();
            UpdateObjectiveLabel();
            if (Mode.Id == GameModeId.Tutorial)
            {
                Prompt = TutorialPrompts[0];
                _lastPrompt = 0;
            }
            _app.Audio.PlayMusic("music_run", 0.55f);
        }

        private GameApp App => _app;

        private void Start()
        {
            // HUD is created after Begin by GameApp; pick it up on the first frame if present.
            if (Hud == null && _app != null && _app.UI != null) AttachHud(_app.UI.Hud);
        }

        public void AttachHud(HUDScreen hud)
        {
            Hud = hud;
            if (hud == null) return;
            hud.Bind(this);
            hud.SetOnlinePlayerList(_players);
            hud.SetPrompt(Prompt);
            hud.SetConnectionWarning(ConnectionWarning);
        }

        private void Update()
        {
            if (IsFinished) return;
            if (IsPaused) return;

            if (CountdownVisible)
            {
                TickCountdown(Time.unscaledDeltaTime);
                UpdateRemotes(Time.deltaTime);
                return;
            }
            if (!RaceStarted) return;

            if (IsLocalRunOver)
            {
                UpdateRemotes(Time.deltaTime);
                Vfx.SetSpeedLines(false);
                if (Net != null && Time.unscaledTime > _resultsDeadline)
                    CompleteUnverified("SERVER RESULT TIMEOUT");
                return;
            }

            RaceElapsed += Time.unscaledDeltaTime;
            Vfx.SetSpeedLines(Speed > 20f, SpeedNormalisedForVfx);
            Runner.Tick(Time.deltaTime);

            if (Mode.Id == GameModeId.Tutorial) UpdateTutorial();
            UpdateOnline(Time.unscaledDeltaTime);
            CheckRunEnd();
            UpdateObjectiveLabel();
        }

        private float SpeedNormalisedForVfx => Runner == null ? 0f : Runner.SpeedNormalised;

        private void TickCountdown(float unscaledDelta)
        {
            if (Online != null)
            {
                long now = Net != null ? Net.ServerNowMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                float remaining = (_startAtMs - now) / 1000f;
                CountdownText = Mathf.Max(1, Mathf.CeilToInt(remaining)).ToString();
                RaceElapsed = Mathf.Max(0f, (now - _startAtMs) / 1000f);
                if (remaining <= 0f)
                {
                    StartRace();
                    return;
                }
                if (RuntimeSettingsSound(remaining)) App.Audio.PlayCountdown(false);
                return;
            }

            _countdown -= unscaledDelta;
            if (_countdown > 0f)
            {
                int number = Mathf.CeilToInt(_countdown);
                CountdownText = number.ToString();
                if (number != _lastCountdownBeep)
                {
                    _lastCountdownBeep = number;
                    App.Audio.PlayCountdown(false);
                }
            }
            if (_countdown <= 0f)
            {
                StartRace();
            }
        }

        private bool RuntimeSettingsSound(float remaining)
        {
            int number = Mathf.CeilToInt(remaining);
            if (number != _lastCountdownBeep)
            {
                _lastCountdownBeep = number;
                return true;
            }
            return false;
        }

        private void StartRace()
        {
            if (RaceStarted) return;
            RaceStarted = true;
            CountdownVisible = false;
            CountdownText = "";
            Input.Enabled = true;
            Input.ResetTransient();
            Runner.StartRunning();
            App.Audio.PlayCountdown(true);
            if (Online != null)
            {
                RaceElapsed = Mathf.Max(0f, (Net.ServerNowMs - _startAtMs) / 1000f);
                _resultsDeadline = Time.unscaledTime + Mathf.Max(10f, _duration - RaceElapsed) + 30f;
            }
            if (Prompt == "" && Mode.Id != GameModeId.Tutorial)
                Hud?.ShowBanner("GO", UITheme.Cyan, 0.8f);
        }

        private void CheckRunEnd()
        {
            if (IsFinished || IsLocalRunOver) return;
            if (Online != null)
            {
                long end = _raceEndAtMs;
                if (Net.ServerNowMs >= end)
                    CompleteLocalOnline("finished");
                return;
            }
            if (Mode.IsTimed && RaceElapsed >= _duration)
            {
                FinishRun(false, true);
                return;
            }
            switch (Mode.Id)
            {
                case GameModeId.DailyChallenge when Distance >= 600f:
                case GameModeId.WeeklyChallenge when Distance >= 1500f:
                case GameModeId.SeasonalEvent when Distance >= 1000f:
                case GameModeId.Tutorial when Distance >= 300f:
                    FinishRun(false, true);
                    break;
            }
        }

        private void OnRunnerDied()
        {
            if (IsFinished || IsLocalRunOver) return;
            CameraRig?.Shake(0.9f);
            Hud?.Flash(Palette.Red, 0.55f);
            Hud?.SetRunOver(true);
            if (Online != null)
            {
                CompleteLocalOnline("dead");
            }
            else
            {
                FinishRun(true, false);
            }
        }

        private void OnRunnerCrashed()
        {
            CameraRig?.Shake(0.4f);
            Hud?.Flash(Palette.Red, 0.28f);
        }

        private void OnCreditsCollected(int value)
        {
            Hud?.PulseCredits(value);
        }

        private void OnPowerUpCollected(PowerUpDefinition definition)
        {
            Hud?.Flash(definition.color, 0.15f);
            Hud?.Toast(definition.displayName, definition.color);
        }

        private void OnWorldRebased()
        {
            _remotes.Clear();
            foreach (var remote in FindObjectsByType<RemoteRunner>(FindObjectsInactive.Exclude))
            {
                if (remote != null && remote.PlayerId != Net?.PlayerId) Destroy(remote.gameObject);
            }
        }

        private void OnBiomeChanged(int biome)
        {
            var theme = App.Config.Theme(biome);
            if (theme == null) return;
            string ambience = biome switch
            {
                1 => "ambience_skyway",
                2 => "ambience_reactor",
                3 => "ambience_tunnel",
                _ => theme.rain ? "ambience_rain" : "ambience_city"
            };
            App.Audio.PlayAmbience(ambience, 0.4f);
            Vfx?.SetEngineTrailColor(theme.accent);
        }

        // ---- Online race ------------------------------------------------------------------

        private void UpdateOnline(float dt)
        {
            if (Online == null) return;
            _snapshotTimer -= dt;
            if (_snapshotTimer <= 0f)
            {
                _snapshotTimer = RaceRules.SnapshotIntervalSeconds;
                SendSnapshot();
            }
            UpdateRemotes(dt);
        }

        private void SendSnapshot()
        {
            if (Net == null || !Net.IsConnected) return;
            var message = Net.NewMessage("snapshot");
            message.distance = Runner.Distance;
            message.speed = Runner.Speed;
            message.score = Runner.Score;
            message.coins = Runner.Coins;
            message.lane = Runner.Lane;
            message.runnerState = Runner.IsGrounded ? (Runner.IsSliding ? "slide" : "run") : "jump";
            message.lives = Runner.Lives;
            message.nearMisses = Runner.NearMisses;
            message.powerUpsUsed = Runner.PowerUpsUsed;
            message.survivalTime = RaceElapsed;
            Net.Send(message);
        }

        private void UpdateRemotes(float dt)
        {
            if (Online == null) return;
            float localDistance = Runner.Distance;
            float localZ = Runner.PlayerZ;
            for (int i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                if (player == null || player.playerId == Net?.PlayerId) continue;
                if (!_remotes.TryGetValue(player.playerId, out RemoteRunner remote) || remote == null)
                {
                    remote = RemoteRunner.Create(transform, App, player);
                    _remotes[player.playerId] = remote;
                }
                remote.ApplyState(player);
                remote.Tick(localDistance, localZ, dt);
            }
            Hud?.SetOnlinePlayerList(_players);
        }

        private void AddOrReplacePlayer(WirePlayer player)
        {
            if (player == null) return;
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].playerId == player.playerId)
                {
                    _players[i] = player;
                    return;
                }
            }
            _players.Add(player);
            if (player.playerId == Net?.PlayerId) return;
            if (Runner != null && !_remotes.ContainsKey(player.playerId))
            {
                var remote = RemoteRunner.Create(transform, App, player);
                _remotes[player.playerId] = remote;
            }
        }

        private void OnNetMessage(WireMessage message)
        {
            if (message == null || Online == null) return;
            switch (message.type)
            {
                case "state_sync":
                case "room_state":
                    ApplyRoomState(message);
                    break;
                case "race_start":
                    if (message.seed != 0 && message.startAtMs > 0)
                    {
                        _startAtMs = message.startAtMs;
                        _raceEndAtMs = _startAtMs + (long)(_duration * 1000f);
                    }
                    break;
                case "race_results":
                    CompleteOnline(message);
                    break;
                case "error":
                    if (!string.IsNullOrEmpty(message.error)) Hud?.ShowBanner(message.error, Palette.Red, 2.5f);
                    break;
                case "left_room":
                    break;
            }
        }

        private void ApplyRoomState(WireMessage message)
        {
            if (message.players == null || message.players.Count == 0) return;
            _players.Clear();
            for (int i = 0; i < message.players.Count; i++) AddOrReplacePlayer(message.players[i]);
            LocalPlacement = FindLocalPlacement();
            OnlinePlayerCount = _players.Count;
            if (message.startAtMs > 0 && message.phase == "racing" && !RaceStarted && !CountdownVisible)
            {
                _startAtMs = message.startAtMs;
                _raceEndAtMs = _startAtMs + (long)(_duration * 1000f);
                _countdown = Mathf.Clamp((_startAtMs - Net.ServerNowMs) / 1000f, 0f, 5f);
                CountdownVisible = true;
                CountdownText = Mathf.Max(1, Mathf.CeilToInt(_countdown)).ToString();
            }
            // A finished room_state is not a result packet. Rewards are only applied from the
            // dedicated race_results message (or the stored copy replayed on reconnect).
        }

        private int FindLocalPlacement()
        {
            var sorted = new List<WirePlayer>(_players);
            RaceRules.SortAndAssignRanks(sorted);
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].playerId == Net?.PlayerId) return sorted[i].placement;
            }
            return 0;
        }

        private void CompleteLocalOnline(string state)
        {
            if (IsLocalRunOver || IsFinished) return;
            IsLocalRunOver = true;
            Input.Enabled = false;
            Runner.Finish(state == "finished");
            Vfx?.SetSpeedLines(false);
            if (!_localFinishSent)
            {
                _localFinishSent = true;
                SendFinish(state);
            }
            if (state == "dead")
            {
                Hud?.SetRunOver(true);
                Hud?.ShowBanner("ELIMINATED - AWAITING OFFICIAL RESULT", Palette.Red, 4f);
                Hud?.SetSpectating(true);
            }
            else
            {
                Hud?.SetRunOver(true);
                Hud?.ShowBanner("CIRCUIT COMPLETE - VERIFYING RESULT", Palette.Lime, 4f);
                Hud?.SetSpectating(true);
            }
            _resultsDeadline = Time.unscaledTime + 45f;
        }

        private void SendFinish(string state)
        {
            if (Net == null || !Net.IsConnected) return;
            var message = Net.NewMessage("finish");
            message.distance = Runner.Distance;
            message.speed = Runner.Speed;
            message.score = Runner.Score;
            message.coins = Runner.Coins;
            message.lane = Runner.Lane;
            message.runnerState = state;
            message.lives = Runner.Lives;
            message.nearMisses = Runner.NearMisses;
            message.powerUpsUsed = Runner.PowerUpsUsed;
            message.survivalTime = RaceElapsed;
            message.finished = state == "finished";
            Net.Send(message);
        }

        private void OnNetState(RaceClient.NetState state, string detail)
        {
            if (Online == null) return;
            switch (state)
            {
                case RaceClient.NetState.Reconnecting:
                    ConnectionWarning = "RECONNECTING TO RACE SERVER...";
                    break;
                case RaceClient.NetState.Connected:
                    ConnectionWarning = "";
                    break;
                case RaceClient.NetState.Failed:
                case RaceClient.NetState.Disconnected:
                    ConnectionWarning = "CONNECTION LOST - RESULT CANNOT BE VERIFIED";
                    if (IsLocalRunOver) _resultsDeadline = Time.unscaledTime + 5f;
                    break;
            }
            Hud?.SetConnectionWarning(ConnectionWarning);
        }

        private void CompleteOnline(WireMessage message)
        {
            if (IsFinished || _serverResultsApplied) return;
            _serverResultsApplied = true;
            IsFinished = true;
            IsLocalRunOver = true;
            Input.Enabled = false;
            Vfx?.SetSpeedLines(false);

            var players = message.players ?? _players;
            RaceRules.SortAndAssignRanks(players);
            WirePlayer me = null;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].playerId == Net?.PlayerId) { me = players[i]; break; }
            }
            if (me == null && _players.Count > 0)
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    if (_players[i].playerId == Net?.PlayerId) { me = _players[i]; break; }
                }
            }

            int credits = me != null && me.rewardsCredits > 0 ? me.rewardsCredits : Mathf.Max(0, message.rewardsCredits);
            int xp = me != null && me.rewardsXp > 0 ? me.rewardsXp : Mathf.Max(0, message.rewardsXp);
            int placement = me != null && me.placement > 0 ? me.placement : Mathf.Max(1, message.place);
            string raceId = !string.IsNullOrEmpty(message.raceId) ? message.raceId : Online?.RaceId;
            int total = Mathf.Max(1, players.Count);
            float finalDistance = me != null && me.distance > 0f ? me.distance : Runner.Distance;
            int finalCoins = me != null ? me.coins : Runner.Coins;
            var summary = new RunSummary
            {
                ModeId = Mode.Id,
                Distance = finalDistance,
                Score = Runner.Score,
                Coins = finalCoins,
                Jumps = Runner.Jumps,
                Slides = Runner.Slides,
                PowerUpsUsed = Runner.PowerUpsUsed,
                NearMisses = Runner.NearMisses,
                SurvivalSeconds = Runner.Elapsed,
                Crashed = Runner.IsDead,
                OnlineResult = true,
                RacePlacement = placement,
                RaceTotal = total,
                ServerCredits = credits,
                ServerXp = xp,
                ServerRaceId = raceId
            };
            bool newBest = finalDistance > App.Profile.bestDistance;
            var rewards = App.Progression.ApplyRun(summary);

            var result = new RunResult
            {
                ModeId = Mode.Id,
                ModeTitle = Mode.Title,
                Distance = finalDistance,
                Time = RaceElapsed,
                Score = Runner.Score,
                Coins = finalCoins,
                Jumps = Runner.Jumps,
                Slides = Runner.Slides,
                NearMisses = Runner.NearMisses,
                PowerUpsUsed = Runner.PowerUpsUsed,
                CreditsEarned = credits,
                XpEarned = xp,
                Placement = placement,
                TotalPlayers = total,
                Crashed = Runner.IsDead,
                Online = true,
                Verified = true,
                NewBest = newBest,
                RaceId = raceId,
                Messages = rewards.Messages ?? new List<string>()
            };
            result.Messages.Add("SERVER-VERIFIED RESULT");
            IsPaused = false;
            Time.timeScale = 1f;
            Hud?.SetSpectating(false);
            App.ShowResults(result);
        }

        private void CompleteUnverified(string reason)
        {
            if (IsFinished) return;
            IsFinished = true;
            Input.Enabled = false;
            var result = new RunResult
            {
                ModeId = Mode.Id,
                ModeTitle = Mode.Title,
                Distance = Runner.Distance,
                Time = RaceElapsed,
                Score = Runner.Score,
                Coins = Runner.Coins,
                Jumps = Runner.Jumps,
                Slides = Runner.Slides,
                NearMisses = Runner.NearMisses,
                PowerUpsUsed = Runner.PowerUpsUsed,
                Crashed = Runner.IsDead,
                Online = true,
                Verified = false,
                Placement = 0,
                TotalPlayers = Mathf.Max(_players.Count, 1),
                FailureNote = reason + " - NO REWARDS GRANTED"
            };
            Time.timeScale = 1f;
            App.ShowResults(result);
        }

        // ---- Offline finish ---------------------------------------------------------------

        private void FinishRun(bool crashed, bool victory)
        {
            if (IsFinished) return;
            IsFinished = true;
            Runner.Finish(victory);
            Input.Enabled = false;
            Vfx?.SetSpeedLines(false);
            Hud?.SetRunOver(true);
            Hud?.ShowBanner(victory ? "RUN COMPLETE" : "SIGNAL LOST", victory ? Palette.Lime : Palette.Red, 2f);
            StartCoroutine(DelayedResults(crashed, victory));
        }

        private IEnumerator DelayedResults(bool crashed, bool victory)
        {
            yield return new WaitForSecondsRealtime(0.85f);
            int nearMissCredits = Runner.NearMisses * 3;
            bool newBest = Runner.Distance > App.Profile.bestDistance;
            var summary = new RunSummary
            {
                ModeId = Mode.Id,
                Distance = Runner.Distance,
                Score = Runner.Score,
                Coins = Runner.Coins,
                Jumps = Runner.Jumps,
                Slides = Runner.Slides,
                PowerUpsUsed = Runner.PowerUpsUsed,
                NearMisses = Runner.NearMisses,
                SurvivalSeconds = Runner.Elapsed,
                Crashed = crashed,
                OnlineResult = false,
                NearMissCredits = nearMissCredits,
                ScoreBonus = 0
            };
            var rewards = App.Progression.ApplyRun(summary);
            var result = new RunResult
            {
                ModeId = Mode.Id,
                ModeTitle = Mode.Title,
                Distance = Runner.Distance,
                Time = RaceElapsed,
                Score = Runner.Score,
                Coins = Runner.Coins,
                Jumps = Runner.Jumps,
                Slides = Runner.Slides,
                NearMisses = Runner.NearMisses,
                PowerUpsUsed = Runner.PowerUpsUsed,
                CreditsEarned = rewards.Credits,
                XpEarned = rewards.Xp,
                Placement = 1,
                TotalPlayers = 1,
                Crashed = crashed,
                Online = false,
                Verified = true,
                NewBest = newBest,
                Messages = rewards.Messages ?? new List<string>()
            };
            if (Mode.Id == GameModeId.Tutorial && victory)
                result.Messages.Add("FIRST CONTACT COMPLETE");
            App.ShowResults(result);
        }

        private void UpdateTutorial()
        {
            float distance = Runner.Distance;
            int step = 0;
            for (int i = 0; i < TutorialDistances.Length; i++)
                if (distance >= TutorialDistances[i]) step = i;
            if (step == _lastPrompt) return;
            _lastPrompt = step;
            Prompt = TutorialPrompts[Mathf.Clamp(step, 0, TutorialPrompts.Length - 1)];
            Hud?.SetPrompt(Prompt);
        }

        private void UpdateObjectiveLabel()
        {
            float target = Mode.Id switch
            {
                GameModeId.DailyChallenge => 600f,
                GameModeId.WeeklyChallenge => 1500f,
                GameModeId.SeasonalEvent => 1000f,
                GameModeId.Tutorial => 300f,
                _ => 0f
            };
            if (target > 0f)
            {
                ObjectiveLabel = $"TARGET {target:0} M";
                ObjectiveProgress = Mathf.Clamp01(Distance / target);
            }
            else if (Mode.IsTimed)
            {
                ObjectiveLabel = "TIME REMAINING";
                ObjectiveProgress = Mathf.Clamp01(RaceElapsed / Mathf.Max(1f, _duration));
            }
            else if (Mode.Id == GameModeId.CoinRush)
            {
                ObjectiveLabel = "BANK ENERGY";
                ObjectiveProgress = 0f;
            }
            else if (Mode.StartingLives > 1)
            {
                ObjectiveLabel = "LIVES REMAINING";
                ObjectiveProgress = Mathf.Clamp01(Lives / (float)Mathf.Max(1, Mode.StartingLives));
            }
            else
            {
                ObjectiveLabel = "DISTANCE";
                ObjectiveProgress = 0f;
            }
        }

        public string TimerLabel
        {
            get
            {
                if (Mode.IsTimed || IsOnline)
                    return UITheme.FormatTime(RaceRemaining);
                return UITheme.FormatTime(RaceElapsed);
            }
        }

        public void SetPaused(bool paused)
        {
            IsPaused = paused;
            if (Input != null) Input.Enabled = !paused && !IsFinished;
            Time.timeScale = paused ? 0f : (Runner != null ? Runner.PreferredTimeScale : 1f);
            if (!paused) Input?.ResetTransient();
        }

        public void RequestRestartFromPause()
        {
            Time.timeScale = 1f;
            App.RestartSession();
        }

        public void QuitToMenu()
        {
            Time.timeScale = 1f;
            App.EndSession(true);
        }

        public void DisposeSession()
        {
            Time.timeScale = 1f;
            if (Online != null && Net != null)
            {
                Net.MessageReceived -= OnNetMessage;
                Net.StateChanged -= OnNetState;
                var leave = Net.NewMessage("leave_room");
                Net.Send(leave);
                Net.Disconnect(true);
            }
            if (Runner != null)
            {
                Runner.Died -= OnRunnerDied;
                Runner.Crashed -= OnRunnerCrashed;
                Runner.PowerUpCollected -= OnPowerUpCollected;
                Runner.CreditsCollected -= OnCreditsCollected;
                Runner.WorldRebased -= OnWorldRebased;
                Runner.StopInput();
            }
            Destroy(gameObject);
        }
    }
}
