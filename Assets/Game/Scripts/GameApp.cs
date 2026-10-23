using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using NeonVelocity.Audio;
using NeonVelocity.Core;
using NeonVelocity.Multiplayer;
using NeonVelocity.Persistence;
using NeonVelocity.Progression;
using NeonVelocity.UI;
using NeonVelocity.Utilities;
using NeonVelocity.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace NeonVelocity
{
    /// <summary>
    /// Application root. Boots from an empty scene, owns persistent services (save, progression,
    /// audio, networking, UI) and is the only place that switches between the title backdrop and
    /// a playable run. No scene-object references are required, making the build portable.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        private enum AppScreen
        {
            MainMenu,
            Modes,
            Multiplayer,
            Shop,
            Missions,
            Leaderboard,
            Settings,
            Pause,
            Results
        }

        public static GameApp Instance { get; private set; }

        public GameConfig Config { get; private set; }
        public SaveService SaveService { get; private set; }
        public ProgressionService Progression { get; private set; }
        public AudioManager Audio { get; private set; }
        public UIRoot UI { get; private set; }
        public RaceClient Race { get; private set; }
        public MenuStage MenuStage { get; private set; }
        public GameSession Session { get; private set; }
        public SaveData Profile => SaveService != null ? SaveService.Data : null;
        public bool IsInRun => Session != null;

        public event Action ProfileChanged;

        private GameObject _servicesRoot;
        private bool _transitioning;
        private readonly List<AppScreen> _navigation = new List<AppScreen>();
        private AppScreen _currentScreen = AppScreen.MainMenu;
        private bool _settingsReturnToRun;

        public bool CanNavigateBack => _navigation.Count > 0;
        public string CurrentScreenName => _currentScreen.ToString();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var root = new GameObject("NEON VELOCITY");
            root.AddComponent<GameApp>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            PurgeTemplateSceneObjects();

            Config = GameConfig.Current;
            SaveService = new SaveService();
            Progression = new ProgressionService(SaveService);
            Progression.ProgressChanged += () => ProfileChanged?.Invoke();

            _servicesRoot = new GameObject("Services");
            _servicesRoot.transform.SetParent(transform, false);
            var audioGo = new GameObject("Audio");
            audioGo.transform.SetParent(_servicesRoot.transform, false);
            Audio = audioGo.AddComponent<AudioManager>();
            Audio.Initialise(Profile.settings);

            Race = gameObject.AddComponent<RaceClient>();
            Race.SetPlayerName(Profile.playerName);

            ApplySettings(false);

            var uiGo = new GameObject("Interface");
            uiGo.transform.SetParent(transform, false);
            UI = uiGo.AddComponent<UIRoot>();
            UI.Initialise(this);

            CreateMenuStage();
            ShowMainMenu();
            Audio.PlayMusic("music_menu", 0.5f);
            Audio.PlayAmbience("ambience_city", 0.35f);

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-nv-capture") >= 0)
                gameObject.AddComponent<VisualCaptureRunner>();
        }

        private void Start()
        {
            StartCoroutine(StartupRoutine());
        }

        private IEnumerator StartupRoutine()
        {
            // Give systems one frame, then present the daily supply drop if it is available.
            yield return null;
            if (!Profile.hasSeenWelcome)
            {
                Profile.hasSeenWelcome = true;
                SaveService.Save();
            }
        }

        private void PurgeTemplateSceneObjects()
        {
            // The URP template scene ships with a camera and light. Remove them so our generated
            // title stage owns the only camera/audio-listener in the scene.
            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].transform.parent == null &&
                    cameras[i].gameObject.name != "Menu Camera")
                    Destroy(cameras[i].gameObject);
            }
            var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].transform.parent == null) Destroy(lights[i].gameObject);
            }
            var listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude);
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null && listeners[i].gameObject.GetComponent<Camera>() != null &&
                    listeners[i].gameObject.name != "Menu Camera")
                    Destroy(listeners[i].gameObject);
            }
        }

        private void CreateMenuStage()
        {
            if (MenuStage != null) return;
            var character = Config.Character(Profile.selectedCharacter);
            var trail = Config.Trail(Profile.selectedTrail);
            MenuStage = World.MenuStage.Create(transform, character, trail, Profile.settings.reducedMotion);
        }

        private void DisposeMenuStage()
        {
            if (MenuStage == null) return;
            MenuStage.Dispose();
            MenuStage = null;
        }

        // ---- Navigation -------------------------------------------------------------------

        public void ShowMainMenu()
        {
            if (_transitioning) return;
            UI.CloseModal();
            UI.CloseScreen();
            UI.HideHud();
            if (Session != null) EndSession(false);
            _navigation.Clear();
            _currentScreen = AppScreen.MainMenu;
            _settingsReturnToRun = false;
            if (MenuStage == null) CreateMenuStage();
            Audio.PlayMusic("music_menu", 0.5f);
            Audio.PlayAmbience("ambience_city", 0.35f);
            Audio.ResumeFromMenu();
            var screen = MainMenuScreen.Build(this, UI);
            UI.ShowScreen(screen);
        }

        public void ShowModes()
        {
            Audio.PlayUiClick();
            OpenScreen(AppScreen.Modes);
        }

        public void ShowMultiplayer()
        {
            Audio.PlayUiClick();
            OpenScreen(AppScreen.Multiplayer);
        }

        public void ShowShop(int tab = 0)
        {
            Audio.PlayUiClick();
            OpenScreen(AppScreen.Shop);
        }

        public void ShowMissions()
        {
            Audio.PlayUiClick();
            OpenScreen(AppScreen.Missions);
        }

        public void ShowLeaderboard()
        {
            Audio.PlayUiClick();
            OpenScreen(AppScreen.Leaderboard);
        }

        public void ShowSettings(bool returnToRun)
        {
            Audio.PlayUiClick();
            _settingsReturnToRun = returnToRun;
            OpenScreen(AppScreen.Settings);
        }

        public void ShowPause()
        {
            if (Session == null || Session.IsFinished) return;
            if (_currentScreen == AppScreen.Pause && UI.IsScreenOpen) return;
            _navigation.Clear();
            _currentScreen = AppScreen.Pause;
            Session.SetPaused(true);
            UI.ShowScreen(PauseScreen.Build(this, UI));
        }

        public void ResumeFromPause()
        {
            UI.CloseScreen();
            if (Session != null) Session.SetPaused(false);
            _navigation.Clear();
            _settingsReturnToRun = false;
            _currentScreen = AppScreen.MainMenu;
        }

        public void ShowResults(RunResult result)
        {
            UI.HideHud();
            UI.CloseScreen();
            _navigation.Clear();
            _currentScreen = AppScreen.Results;
            var screen = ResultsScreen.Build(this, UI, result);
            UI.ShowScreen(screen);
            Audio.PlayMusic("music_menu", 0.4f);
        }

        /// <summary>
        /// Central contextual Back action for touch buttons, Escape and gamepad East.
        /// Modals dismiss first, screen-specific handlers get priority, then the navigation
        /// stack is used. The main menu is the only root and never sits on the stack.
        /// </summary>
        public void HandleBack()
        {
            if (UI == null) return;
            if (UI.HasModal)
            {
                UI.CloseModal();
                return;
            }

            if (_currentScreen == AppScreen.Pause)
            {
                ResumeFromPause();
                return;
            }

            // The title screen is the navigation root. Back/Escape there asks before quitting
            // rather than silently rebuilding the same screen.
            if (_currentScreen == AppScreen.MainMenu && UI.IsScreenOpen)
            {
                QuitApplication();
                return;
            }

            var handler = UI.CurrentScreen != null
                ? UI.CurrentScreen.GetComponent<IScreenBackHandler>()
                : null;
            if (handler != null && handler.HandleBackRequest()) return;

            NavigateBack();
        }

        public void NavigateBack()
        {
            if (UI == null) return;
            Audio.PlayUiClick();
            if (_navigation.Count > 0)
            {
                var previous = _navigation[_navigation.Count - 1];
                _navigation.RemoveAt(_navigation.Count - 1);
                _currentScreen = previous;
                BuildScreen(previous);
                return;
            }

            if (_settingsReturnToRun && Session != null && !Session.IsFinished)
            {
                _currentScreen = AppScreen.Pause;
                _settingsReturnToRun = false;
                Session.SetPaused(true);
                UI.ShowScreen(PauseScreen.Build(this, UI));
                return;
            }

            if (Session != null && !Session.IsFinished)
            {
                ShowPause();
                return;
            }

            ShowMainMenu();
        }

        private void OpenScreen(AppScreen screen)
        {
            if (UI == null || _transitioning) return;
            if (_currentScreen != screen && _currentScreen != AppScreen.MainMenu &&
                _currentScreen != AppScreen.Results && UI.IsScreenOpen)
                _navigation.Add(_currentScreen);

            _currentScreen = screen;
            BuildScreen(screen);
        }

        private void BuildScreen(AppScreen screen)
        {
            switch (screen)
            {
                case AppScreen.Modes:
                    UI.ShowScreen(ModeSelectScreen.Build(this, UI));
                    break;
                case AppScreen.Multiplayer:
                    UI.ShowScreen(MultiplayerScreen.Build(this, UI));
                    break;
                case AppScreen.Shop:
                    UI.ShowScreen(ShopScreen.Build(this, UI, 0));
                    break;
                case AppScreen.Missions:
                    UI.ShowScreen(MissionsScreen.Build(this, UI));
                    break;
                case AppScreen.Leaderboard:
                    UI.ShowScreen(LeaderboardScreen.Build(this, UI));
                    break;
                case AppScreen.Settings:
                    UI.ShowScreen(SettingsScreen.Build(this, UI, _settingsReturnToRun));
                    break;
                case AppScreen.Pause:
                    if (Session == null) { ShowMainMenu(); return; }
                    Session.SetPaused(true);
                    UI.ShowScreen(PauseScreen.Build(this, UI));
                    break;
                case AppScreen.Results:
                    ShowMainMenu();
                    break;
                default:
                    ShowMainMenu();
                    break;
            }
        }

        public void QuitApplication()
        {
            Audio.PlayUiClick();
            if (UI == null) return;
            UI.ShowModal("QUIT NEON VELOCITY?",
                "Your profile, settings and unlocks are saved automatically. Return to the grid?",
                "QUIT", QuitNow, "STAY");
        }

        private void QuitNow()
        {
#if UNITY_EDITOR
            Debug.Log("[NEON VELOCITY] Quit requested (ignored in editor).");
#else
            Application.Quit();
#endif
        }

        // ---- Run lifecycle ---------------------------------------------------------------

        public void StartMode(GameModeId id)
        {
            var spec = GameModes.Get(id);
            if (GameModes.IsOnline(id))
            {
                ShowMultiplayer();
                return;
            }
            int seed = BuildOfflineSeed(spec);
            StartSession(spec, seed, null);
        }

        public void StartOnlineSession(ModeSpec spec, int seed, long startAtMs, float durationSeconds,
            List<WirePlayer> players, string raceId)
        {
            var context = new OnlineRaceContext
            {
                Client = Race,
                Seed = seed,
                StartAtMs = startAtMs,
                DurationSeconds = durationSeconds > 0f ? durationSeconds : RaceRules.RaceDurationSeconds,
                Players = players ?? new List<WirePlayer>(),
                RaceId = raceId
            };
            StartSession(spec, seed, context);
        }

        private int BuildOfflineSeed(ModeSpec spec)
        {
            DateTime utc = DateTime.UtcNow;
            int seed;
            switch (spec.Id)
            {
                case GameModeId.DailyChallenge:
                    seed = DeterministicRandom.StableHash("daily:" + MissionFactory.DailyKey(utc) + ":" + Profile.playerId);
                    break;
                case GameModeId.WeeklyChallenge:
                    seed = DeterministicRandom.StableHash("weekly:" + MissionFactory.WeeklyKey(utc) + ":" + Profile.playerId);
                    break;
                case GameModeId.SeasonalEvent:
                    seed = DeterministicRandom.StableHash("monsoon:" + utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                    break;
                default:
                    seed = UnityEngine.Random.Range(1, int.MaxValue);
                    break;
            }
            seed = unchecked(seed + spec.SeedSalt * 7919);
            return seed == 0 ? 1 : Mathf.Abs(seed);
        }

        public void StartSession(ModeSpec spec, int seed, OnlineRaceContext online)
        {
            if (_transitioning) return;
            _transitioning = true;
            StopAllCoroutines();
            StartCoroutine(StartSessionRoutine(spec, seed, online));
        }

        private IEnumerator StartSessionRoutine(ModeSpec spec, int seed, OnlineRaceContext online)
        {
            UI.FadeOut(0.2f);
            yield return new WaitForSecondsRealtime(0.2f);
            UI.CloseScreen();
            UI.CloseModal();
            UI.HideHud();
            EndSession(false);
            DisposeMenuStage();
            _navigation.Clear();
            _currentScreen = AppScreen.MainMenu;
            _settingsReturnToRun = false;
            Audio.PlayUiClick();
            var sessionGo = new GameObject("Run - " + spec.Title);
            sessionGo.transform.SetParent(transform, false);
            Session = sessionGo.AddComponent<GameSession>();
            Session.Begin(this, spec, seed, online);
            UI.ShowHud();
            Session.AttachHud(UI.Hud);
            UI.FadeIn(0.22f);
            Audio.PlayMusic("music_run", 0.55f);
            Audio.PlayAmbience(spec.Id == GameModeId.SeasonalEvent ? "ambience_rain" : "ambience_city", 0.4f);
            _transitioning = false;
        }

        public void RestartSession()
        {
            if (Session == null) return;
            var spec = Session.Mode;
            bool online = Session.Online != null;
            if (online)
            {
                ShowMainMenu();
                return;
            }
            int seed = BuildOfflineSeed(spec);
            StartSession(spec, seed, null);
        }

        public void EndSession(bool returnToMenu)
        {
            UI.CloseScreen();
            UI.CloseModal();
            if (Session != null)
            {
                var session = Session;
                Session = null;
                session.DisposeSession();
            }
            UI.HideHud();
            Time.timeScale = 1f;
            if (returnToMenu)
            {
                CreateMenuStage();
                Audio.ResumeFromMenu();
                ShowMainMenu();
            }
        }

        // ---- Settings and profile ---------------------------------------------------------

        public void ApplySettings(bool save)
        {
            var s = Profile.settings;
            SaveMigration.ClampSettings(s);
            ApplyPresetDefaults(s);
            if (Audio != null)
            {
                Audio.SetSettings(s);
                Audio.ApplyVolumes();
                if (s.musicVolume <= 0.001f) Audio.StopMusic(0.2f);
            }

            Application.targetFrameRate = s.targetFrameRate;
            QualitySettings.vSyncCount = s.vSync ? 1 : 0;
            QualitySettings.antiAliasing = s.antiAliasing;
            QualitySettings.shadows = s.shadowQuality > 0 ? ShadowQuality.All : ShadowQuality.Disable;
            QualitySettings.shadowResolution = s.shadowQuality >= 3 ? ShadowResolution.VeryHigh
                : s.shadowQuality >= 2 ? ShadowResolution.High
                : s.shadowQuality >= 1 ? ShadowResolution.Medium : ShadowResolution.Low;
            QualitySettings.shadowDistance = s.shadowQuality >= 3 ? 110f
                : s.shadowQuality >= 2 ? 80f
                : s.shadowQuality >= 1 ? 55f : 30f;
            QualitySettings.realtimeReflectionProbes = s.reflectionQuality > 0;
            QualitySettings.lodBias = s.viewDistance >= 2 ? 1.6f : (s.viewDistance >= 1 ? 1.15f : 0.85f);
            QualitySettings.globalTextureMipmapLimit = s.textureQuality;
            QualitySettings.skinWeights = SkinWeights.TwoBones;
            RenderSettings.reflectionIntensity = s.reflectionQuality >= 2 ? 0.95f
                : (s.reflectionQuality >= 1 ? 0.62f : 0.18f);

            var renderPipeline = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset;
            if (renderPipeline != null)
            {
                renderPipeline.msaaSampleCount = Mathf.Max(1, s.antiAliasing);
                renderPipeline.renderScale = Mathf.Clamp(s.renderScale, 0.5f, 1f);
            }

            ApplyDisplaySettings(s);
            UI?.ApplyHighContrast(s.highContrast);
            if (MenuStage != null && s.reducedMotion)
            {
                // Rebuild the backdrop with its reduced-motion camera framing.
                DisposeMenuStage();
                CreateMenuStage();
            }
            Session?.ApplyVideoSettings();
            if (save)
            {
                SaveService.Save();
                ProfileChanged?.Invoke();
            }
        }

        /// <summary>Expands a named quality preset into the individual renderer settings.</summary>
        public static void ApplyPresetDefaults(SettingsData s)
        {
            switch (s.graphicsPreset)
            {
                case 0:
                    s.renderScale = 0.67f;
                    s.textureQuality = 2;
                    s.shadowQuality = 0;
                    s.reflectionQuality = 0;
                    s.antiAliasing = 0;
                    s.effectsQuality = 0;
                    s.viewDistance = 0;
                    break;
                case 1:
                    s.renderScale = 0.85f;
                    s.textureQuality = 1;
                    s.shadowQuality = 1;
                    s.reflectionQuality = 1;
                    s.antiAliasing = 2;
                    s.effectsQuality = 1;
                    s.viewDistance = 1;
                    break;
                case 2:
                    s.renderScale = 1f;
                    s.textureQuality = 0;
                    s.shadowQuality = 2;
                    s.reflectionQuality = 2;
                    s.antiAliasing = 2;
                    s.effectsQuality = 2;
                    s.viewDistance = 2;
                    break;
                case 3:
                    s.renderScale = 1f;
                    s.textureQuality = 0;
                    s.shadowQuality = 3;
                    s.reflectionQuality = 2;
                    s.antiAliasing = 4;
                    s.effectsQuality = 2;
                    s.viewDistance = 2;
                    break;
            }
        }

        private static void ApplyDisplaySettings(SettingsData s)
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            FullScreenMode mode = s.fullscreenMode == 1
                ? FullScreenMode.FullScreenWindow
                : (s.fullscreenMode == 2 ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.Windowed);
            int width = s.screenWidth;
            int height = s.screenHeight;
            if (width <= 0 || height <= 0)
            {
                // No explicit resolution yet. A windowed request can be satisfied without
                // touching the desktop, but leaving borderless/exclusive needs real dimensions.
                if (mode == FullScreenMode.Windowed && Screen.fullScreenMode == FullScreenMode.Windowed)
                    return;
                width = Display.main.systemWidth;
                height = Display.main.systemHeight;
            }
            if (width <= 0 || height <= 0) return;
            if (Screen.width == width && Screen.height == height && Screen.fullScreenMode == mode) return;
            Screen.SetResolution(width, height, mode);
#endif
        }

        public void SaveProfile()
        {
            SaveService.Save();
            ProfileChanged?.Invoke();
        }

        public string SelectedCharacterId => Profile != null ? Profile.selectedCharacter : "kira";

        private void Update()
        {
            if (UI == null) return;
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;
            bool escapePressed = keyboard != null &&
                (keyboard.escapeKey.wasPressedThisFrame || keyboard.pKey.wasPressedThisFrame);
            bool controllerBack = gamepad != null && gamepad.buttonEast.wasPressedThisFrame;
            bool startPressed = gamepad != null && gamepad.startButton.wasPressedThisFrame;

            if (UI.HasModal)
            {
                if (escapePressed || controllerBack) UI.CloseModal();
                return;
            }

            if (Session != null && !Session.IsFinished)
            {
                if (startPressed || escapePressed)
                {
                    if (UI.IsScreenOpen)
                    {
                        if (_currentScreen == AppScreen.Pause) ResumeFromPause();
                        else HandleBack();
                    }
                    else ShowPause();
                    return;
                }
                // East is the slide button during play, so it only acts as Back while a
                // menu/pause/settings screen is actually in front of the run.
                if (controllerBack && UI.IsScreenOpen) HandleBack();
                return;
            }

            if (escapePressed || controllerBack) HandleBack();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && Session != null && !Session.IsFinished) SaveService.Save();
        }

        private void OnApplicationQuit()
        {
            SaveService?.Save();
        }
    }

    public sealed class OnlineRaceContext
    {
        public RaceClient Client;
        public int Seed;
        public long StartAtMs;
        public float DurationSeconds;
        public List<WirePlayer> Players;
        public string RaceId;
    }
}
