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
            var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].transform.parent == null &&
                    cameras[i].gameObject.name != "Menu Camera")
                    Destroy(cameras[i].gameObject);
            }
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].transform.parent == null) Destroy(lights[i].gameObject);
            }
            var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
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
            if (MenuStage == null) CreateMenuStage();
            Audio.PlayMusic("music_menu", 0.5f);
            Audio.PlayAmbience("ambience_city", 0.35f);
            Audio.ResumeFromMenu();
            var screen = MainMenuScreen.Build(this, UI);
            UI.ShowScreen(screen);
            UI.SetSelected(screen.GetComponentInChildren<UnityEngine.UI.Button>()?.gameObject);
        }

        public void ShowModes()
        {
            Audio.PlayUiClick();
            UI.ShowScreen(ModeSelectScreen.Build(this, UI));
        }

        public void ShowMultiplayer()
        {
            Audio.PlayUiClick();
            UI.ShowScreen(MultiplayerScreen.Build(this, UI));
        }

        public void ShowShop(int tab = 0)
        {
            Audio.PlayUiClick();
            UI.ShowScreen(ShopScreen.Build(this, UI, tab));
        }

        public void ShowMissions()
        {
            Audio.PlayUiClick();
            UI.ShowScreen(MissionsScreen.Build(this, UI));
        }

        public void ShowLeaderboard()
        {
            Audio.PlayUiClick();
            UI.ShowScreen(LeaderboardScreen.Build(this, UI));
        }

        public void ShowSettings(bool returnToRun)
        {
            Audio.PlayUiClick();
            UI.ShowScreen(SettingsScreen.Build(this, UI, returnToRun));
        }

        public void ShowPause()
        {
            if (Session == null) return;
            Session.SetPaused(true);
            UI.ShowScreen(PauseScreen.Build(this, UI));
        }

        public void ResumeFromPause()
        {
            UI.CloseScreen();
            if (Session != null) Session.SetPaused(false);
        }

        public void ShowResults(RunResult result)
        {
            UI.HideHud();
            var screen = ResultsScreen.Build(this, UI, result);
            UI.ShowScreen(screen);
            Audio.PlayMusic(result.Online && result.Placement == 1 ? "music_menu" : "music_menu", 0.4f);
        }

        public void QuitApplication()
        {
            Audio.PlayUiClick();
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
            if (Audio != null)
            {
                Audio.SetSettings(s);
                Audio.ApplyVolumes();
                if (s.musicVolume <= 0.001f) Audio.StopMusic(0.2f);
            }
            Application.targetFrameRate = s.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = s.graphicsPreset >= 3 ? 4 : (s.graphicsPreset >= 2 ? 2 : 0);
            QualitySettings.shadows = s.graphicsPreset >= 1 ? ShadowQuality.All : ShadowQuality.Disable;
            QualitySettings.shadowDistance = s.graphicsPreset >= 3 ? 90f : (s.graphicsPreset >= 2 ? 65f : 40f);
            QualitySettings.lodBias = s.graphicsPreset >= 2 ? 1.4f : 1f;
            QualitySettings.globalTextureMipmapLimit = s.graphicsPreset <= 0 ? 2 : (s.graphicsPreset == 1 ? 1 : 0);
            QualitySettings.skinWeights = SkinWeights.TwoBones;
            var renderPipeline = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset;
            if (renderPipeline != null) renderPipeline.msaaSampleCount = 1;
            UI?.ApplyHighContrast(s.highContrast);
            if (MenuStage != null && s.reducedMotion)
            {
                // Rebuild the backdrop with its reduced-motion camera framing.
                DisposeMenuStage();
                CreateMenuStage();
            }
            if (save)
            {
                SaveService.Save();
                ProfileChanged?.Invoke();
            }
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
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (UI.HasModal)
                {
                    UI.CloseModal();
                }
                else if (Session != null && !Session.IsFinished)
                {
                    if (UI.IsScreenOpen) ResumeFromPause();
                    else ShowPause();
                }
                else if (!UI.IsScreenOpen && !IsInRun)
                {
                    QuitApplication();
                }
            }
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
