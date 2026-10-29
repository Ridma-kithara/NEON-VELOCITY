using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Player;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonVelocity.World
{
    /// <summary>
    /// Biome lighting, fog, sky, rain and quality scaling for a run. The sky is an inverted,
    /// camera-independent gradient dome with an original star field, so no authored skybox is
    /// required and all four biomes can be switched at runtime.
    /// </summary>
    public sealed class EnvironmentController : MonoBehaviour
    {
        private GameApp _app;
        private TrackManager _track;
        private RunnerController _runner;
        private Camera _camera;
        private sealed class TrafficVehicle
        {
            public Transform Root;
            public float Speed;
            public float Phase;
            public float BaseY;
        }

        private Light _sun;
        private Light _moonLight;
        private Light _rim;
        private Light _cyanFill;
        private Light _magentaFill;
        private Light _warmFill;
        private Transform _dome;
        private Transform _skyDecor;
        private Transform _starsRoot;
        private Transform _moon;
        private readonly List<MeshRenderer> _starRenderers = new List<MeshRenderer>();
        private Transform _cloudA;
        private Transform _cloudB;
        private Material _domeMaterial;
        private MeshRenderer _domeFadeRenderer;
        private Material _domeFadeMaterial;
        private Material _pendingDomeMaterial;
        private bool _skyFadeActive;
        private float _skyFadeT;
        private readonly Dictionary<int, Material> _domeMaterials = new Dictionary<int, Material>();
        private readonly Dictionary<int, Material> _domeFadeMaterials = new Dictionary<int, Material>();
        private readonly Dictionary<int, Material> _cloudMaterials = new Dictionary<int, Material>();
        private readonly Dictionary<int, Cubemap> _reflections = new Dictionary<int, Cubemap>();
        private TimeOfDayState _timeOfDay;
        private int _timeOfDayMode;
        private int _timeOfDayFixedPhase = 2;
        private float _timeOfDayCycleMetres = TimeOfDayCatalog.DefaultCycleMetres;
        private Color _cloudTint = Color.white;

        private const float TransitionSeconds = 2.6f;
        private float _transition = 1f;
        private Color _fromFog;
        private Color _targetFog;
        private float _fromFogDensity;
        private float _targetFogDensity;
        private Color _fromAmbientSky;
        private Color _targetAmbientSky;
        private Color _fromAmbientEquator;
        private Color _targetAmbientEquator;
        private Color _fromAmbientGround;
        private Color _targetAmbientGround;
        private float _fromAmbientIntensity;
        private float _targetAmbientIntensity;
        private float _fromReflectionIntensity;
        private float _targetReflectionIntensity;
        private float _fromShadowStrength;
        private float _targetShadowStrength;
        private Color _fromSunColor;
        private Color _targetSunColor;
        private float _fromSunIntensity;
        private float _targetSunIntensity;
        private float _currentSunIntensity;
        private Quaternion _fromSunRotation;
        private Quaternion _targetSunRotation;
        private Quaternion _fromMoonRotation;
        private Quaternion _targetMoonRotation;
        private Color _fromMoonColor;
        private Color _targetMoonColor;
        private float _fromMoonIntensity;
        private float _targetMoonIntensity;
        private Color _fromRimColor;
        private Color _targetRimColor;
        private Color _fromCyanFill;
        private Color _targetCyanFill;
        private Color _fromMagentaFill;
        private Color _targetMagentaFill;
        private Color _fromWarmFill;
        private Color _targetWarmFill;
        private Color _fromBackground;
        private Color _targetBackground;
        private float _rainRate;
        private float _splashRate;
        /// <summary>Per-location rain intensity multiplier (visual only; weather codes stay 0-5).</summary>
        private float _rainTier = 1f;
        private float _targetRainRate;
        private float _targetSplashRate;
        private ParticleSystem _rain;
        private ParticleSystemRenderer _rainRenderer;
        private ParticleSystem _splash;
        private ParticleSystemRenderer _splashRenderer;
        private ParticleSystem _mist;
        private ParticleSystemRenderer _mistRenderer;
        private ParticleSystem _snow;
        private ParticleSystem _weather;
        private ParticleSystemRenderer _weatherRenderer;
        private Material _weatherSnowMaterial;
        private Material _weatherDustMaterial;
        private Material _weatherEmberMaterial;
        private int _weatherKind = -1;
        private readonly List<TrafficVehicle> _traffic = new List<TrafficVehicle>();
        private GameObject _trafficRoot;
        private GameObject _trainRoot;
        private GameObject _railRoot;
        private Transform _bossRoot;
        private int _biome = -1;
        private float _time;
        private float _flash;
        private float _thunderTimer;
        private bool _trainPassPlayed;
        private bool _reduced;
        private float _quality = 2f;
        private int _effectsQuality = 2;
        private int _reflectionQuality = 2;
        private int _viewDistance = 2;

        public int CurrentBiome => _biome;
        /// <summary>Current smoothly blended time-of-day state for HUD diagnostics and tests.</summary>
        public TimeOfDayState TimeOfDay => _timeOfDay;
        public string TimeOfDayLabel => _timeOfDay.Label;
        public int TimeOfDayMode => _timeOfDayMode;
        public int TimeOfDayFixedPhase => _timeOfDayFixedPhase;
        public float TimeOfDayCycleMetres => _timeOfDayCycleMetres;
        public bool IsSkyFading => _skyFadeActive;

        public static EnvironmentController Create(Transform parent, GameApp app, GameModeId mode, TrackManager track,
            RunnerController runner, Camera camera, bool reducedMotion, int graphicsPreset)
        {
            var go = new GameObject("Environment");
            go.transform.SetParent(parent, false);
            var env = go.AddComponent<EnvironmentController>();
            env.Build(app, mode, track, runner, camera, reducedMotion, graphicsPreset);
            return env;
        }

        private void Build(GameApp app, GameModeId mode, TrackManager track, RunnerController runner, Camera camera,
            bool reducedMotion, int graphicsPreset)
        {
            _app = app;
            _track = track;
            _runner = runner;
            _camera = camera;
            _reduced = reducedMotion;
            _quality = graphicsPreset;
            if (app != null && app.Profile != null)
            {
                var settings = app.Profile.settings;
                _effectsQuality = settings.effectsQuality;
                _reflectionQuality = settings.reflectionQuality;
                _viewDistance = settings.viewDistance;
                _quality = _effectsQuality;
                _timeOfDayMode = settings.timeOfDayMode;
                _timeOfDayFixedPhase = settings.timeOfDayPhase;
                _timeOfDayCycleMetres = settings.timeOfDayCycleMetres;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.011f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientIntensity = 1.05f;
            RenderSettings.reflectionIntensity = 0.95f;

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.intensity = graphicsPreset >= 1 ? 1.28f : 0.62f;
            _sun.color = new Color(0.7f, 0.84f, 1f);
            _sun.shadows = graphicsPreset >= 2 ? LightShadows.Soft : LightShadows.None;
            _sun.shadowStrength = 0.82f;
            _sun.shadowBias = 0.03f;
            _sun.shadowNormalBias = 0.35f;
            _sun.transform.rotation = Quaternion.Euler(38f, -28f, 0f);

            var moonGo = new GameObject("Moonlight");
            moonGo.transform.SetParent(transform, false);
            _moonLight = moonGo.AddComponent<Light>();
            _moonLight.type = LightType.Directional;
            _moonLight.color = new Color(0.62f, 0.76f, 1f);
            _moonLight.intensity = 0.32f;
            _moonLight.shadows = LightShadows.None;
            _moonLight.transform.rotation = Quaternion.Euler(32f, 152f, 0f);

            var rimGo = new GameObject("Neon Rim");
            rimGo.transform.SetParent(transform, false);
            _rim = rimGo.AddComponent<Light>();
            _rim.type = LightType.Directional;
            _rim.intensity = graphicsPreset >= 1 ? 0.42f : 0.22f;
            _rim.color = Palette.Magenta;
            _rim.transform.rotation = Quaternion.Euler(16f, 148f, 0f);

            BuildCameraLights();
            BuildDome();
            BuildRain();
            BuildWeather();
            BuildAtmosphere();
            BuildTraffic();
            BuildSkyTrain();
            if (mode == GameModeId.SeasonalEvent) BuildEmbers();
            if (mode == GameModeId.BossChase) BuildBossChase();

            _track.LocationChanged += OnLocationChanged;
            ApplyBiome(_track.CurrentZoneIndex < 0 ? 0 : _track.CurrentZoneIndex, true);
        }

        private void BuildCameraLights()
        {
            if (_camera == null) return;
            _cyanFill = CreateFillLight("Neon Fill Cyan", new Vector3(-4.8f, 2.2f, 7f), Palette.Cyan, 16f, 9f);
            _magentaFill = CreateFillLight("Neon Fill Magenta", new Vector3(4.8f, 2.6f, 7f), Palette.Magenta, 16f, 8f);
            _warmFill = CreateFillLight("Street Fill Warm", new Vector3(0f, 5.8f, 18f), new Color(1f, 0.62f, 0.3f), 26f, 5f);
            bool enabled = _effectsQuality >= 2;
            _cyanFill.enabled = enabled;
            _magentaFill.enabled = enabled;
            _warmFill.enabled = enabled;
        }

        public void ApplyVideoSettings(SettingsData settings)
        {
            if (settings == null) return;
            int oldMode = _timeOfDayMode;
            int oldPhase = _timeOfDayFixedPhase;
            float oldCycle = _timeOfDayCycleMetres;
            _timeOfDayMode = settings.timeOfDayMode;
            _timeOfDayFixedPhase = settings.timeOfDayPhase;
            _timeOfDayCycleMetres = settings.timeOfDayCycleMetres;
            _effectsQuality = settings.effectsQuality;
            _reflectionQuality = settings.reflectionQuality;
            _viewDistance = settings.viewDistance;
            _quality = settings.effectsQuality;
            if (_rain != null)
            {
                var main = _rain.main;
                main.maxParticles = _reduced ? 250 : Mathf.RoundToInt(1600f * (_effectsQuality + 1));
            }
            if (_splash != null)
            {
                var main = _splash.main;
                main.maxParticles = _reduced ? 120 : Mathf.RoundToInt(700f * (_effectsQuality + 1));
            }
            if (_sun != null)
            {
                _sun.shadows = settings.shadowQuality > 0 ? LightShadows.Soft : LightShadows.None;
                _targetSunIntensity = DesiredSunIntensity;
                if (_transition >= 0.999f) ApplyTransition(1f);
            }
            bool fills = _effectsQuality >= 2;
            if (_cyanFill != null) _cyanFill.enabled = fills;
            if (_magentaFill != null) _magentaFill.enabled = fills;
            if (_warmFill != null) _warmFill.enabled = fills;
            RenderSettings.ambientIntensity = DesiredAmbientIntensity;
            RenderSettings.reflectionIntensity = _reflectionQuality >= 2 ? 1f
                : (_reflectionQuality >= 1 ? 0.68f : 0.18f);
            var theme = _theme;
            if (theme != null)
            {
                bool rain = theme.rain || (_app != null && _app.Session != null &&
                            _app.Session.Mode.Id == GameModeId.SeasonalEvent);
                SetRain(rain, false);
            }
            // A time-of-day change is visible immediately; the environment rebuild reseats
            // the sky gradient, sun rotation and all seven lighting channels together.
            if (_biome >= 0 && (oldMode != _timeOfDayMode || oldPhase != _timeOfDayFixedPhase ||
                                Mathf.Abs(oldCycle - _timeOfDayCycleMetres) > 0.01f))
                ApplyBiome(_biome, true);
            else ApplyTimeOfDay(ResolveTimeOfDay(_biome));
        }

        private Light CreateFillLight(string name, Vector3 localPosition, Color color, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_camera.transform, false);
            go.transform.localPosition = localPosition;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
            return light;
        }

        private void BuildDome()
        {
            var domeGo = new GameObject("Sky Dome");
            domeGo.transform.SetParent(transform, false);
            _dome = domeGo.transform;
            var filter = domeGo.AddComponent<MeshFilter>();
            filter.sharedMesh = PrimitiveMeshes.Sphere;
            var renderer = domeGo.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // Radius 450 keeps the authored stars (r=350) and the moon inside the sky shell.
            domeGo.transform.localScale = Vector3.one * 900f;
            domeGo.transform.position = new Vector3(0f, 0f, 0f);

            // A slightly smaller transparent shell is used only while a biome change is
            // crossfading. It draws after the opaque dome so the old gradient stays visible
            // beneath the incoming one until the alpha reaches one.
            var fadeGo = new GameObject("Sky Crossfade");
            fadeGo.transform.SetParent(domeGo.transform, false);
            fadeGo.transform.localPosition = Vector3.zero;
            fadeGo.transform.localScale = Vector3.one * 0.997f;
            var fadeFilter = fadeGo.AddComponent<MeshFilter>();
            fadeFilter.sharedMesh = PrimitiveMeshes.Sphere;
            _domeFadeRenderer = fadeGo.AddComponent<MeshRenderer>();
            _domeFadeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _domeFadeRenderer.receiveShadows = false;
            _domeFadeRenderer.lightProbeUsage = LightProbeUsage.Off;
            _domeFadeRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            fadeGo.SetActive(false);

            // Stars and moon live in an unscaled sibling so their authored world-space radius
            // is not multiplied by the dome scale. Previously they were children of a 380x
            // sphere and ended up far beyond the camera far clip.
            var decorGo = new GameObject("Sky Decor");
            decorGo.transform.SetParent(transform, false);
            _skyDecor = decorGo.transform;
            var starsGo = new GameObject("Stars");
            starsGo.transform.SetParent(decorGo.transform, false);
            _starsRoot = starsGo.transform;
            var starMat = MaterialLibrary.Lit(Palette.White, 0f, 0.1f, new Color(0.65f, 0.9f, 1f), 2.4f);
            for (int i = 0; i < 90; i++)
            {
                var rng = new DeterministicRandom(0x5701 + i);
                float azimuth = rng.Range(0f, Mathf.PI * 2f);
                float elevation = rng.Range(0.16f, 1.35f);
                var p = new Vector3(
                    Mathf.Cos(azimuth) * Mathf.Cos(elevation),
                    Mathf.Sin(elevation),
                    Mathf.Sin(azimuth) * Mathf.Cos(elevation)) * 350f;
                var star = PrimitiveMeshes.Create("Star" + i, PrimitiveMeshes.Cube, starMat, starsGo.transform,
                    p, Vector3.one * rng.Range(0.55f, 1.6f));
                var starRenderer = star.GetComponent<MeshRenderer>();
                starRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _starRenderers.Add(starRenderer);
            }

            var moonGo = PrimitiveMeshes.Create("Moon", PrimitiveMeshes.Sphere,
                MaterialLibrary.Lit(new Color(0.8f, 0.9f, 1f), 0f, 1f, new Color(0.45f, 0.72f, 1f), 4.2f),
                decorGo.transform, new Vector3(170f, 210f, 240f), Vector3.one * 22f);
            moonGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            _moon = moonGo.transform;

            _cloudA = CreateCloudShell("High Cloud Layer", 0.89f);
            _cloudB = CreateCloudShell("Low Cloud Layer", 0.78f);
        }

        private Transform CreateCloudShell(string name, float localScale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_dome, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * localScale;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = PrimitiveMeshes.Sphere;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        private void BuildRain()
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(_camera != null ? _camera.transform : transform, false);
            go.transform.localPosition = new Vector3(0f, 11f, 12f);
            _rain = go.AddComponent<ParticleSystem>();
            var main = _rain.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 0.9f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(26f, 34f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.085f);
            main.startColor = new Color(0.72f, 0.86f, 1f, 1f);
            main.gravityModifier = 1.4f;
            main.maxParticles = _reduced ? 300 : Mathf.RoundToInt(1400f * Mathf.Clamp(_quality, 0.4f, 1.5f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = _rain.emission;
            emission.rateOverTime = 0f;
            var shape = _rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(26f, 1f, 26f);
            shape.position = new Vector3(0f, 0f, 12f);
            var velocity = _rain.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(2.2f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f);
            velocity.z = new ParticleSystem.MinMaxCurve(-6.5f);
            _rainRenderer = go.GetComponent<ParticleSystemRenderer>();
            _rainRenderer.material = MaterialLibrary.AdditiveTextured("NV_RainMaterial",
                TextureFactory.RainStreak(), new Color(0.55f, 0.75f, 1f, 1f));
            _rainRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            _rainRenderer.lengthScale = 7.0f;
            _rainRenderer.velocityScale = 0.03f;
            _rainRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _rainRenderer.receiveShadows = false;
            _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // Ground splashes make the road read as wet rather than just dark.
            var splashGo = new GameObject("Rain Splashes");
            splashGo.transform.SetParent(_camera != null ? _camera.transform : transform, false);
            splashGo.transform.localPosition = new Vector3(0f, -3.9f, 20f);
            _splash = splashGo.AddComponent<ParticleSystem>();
            var sm = _splash.main;
            sm.loop = true;
            sm.playOnAwake = false;
            sm.startLifetime = 0.32f;
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 2.1f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.055f);
            sm.startColor = new Color(0.6f, 0.85f, 1f, 0.6f);
            sm.gravityModifier = 0.8f;
            sm.maxParticles = _reduced ? 100 : 520;
            sm.simulationSpace = ParticleSystemSimulationSpace.Local;
            var se = _splash.emission;
            se.rateOverTime = 0f;
            var ss = _splash.shape;
            ss.shapeType = ParticleSystemShapeType.Box;
            ss.scale = new Vector3(22f, 0.1f, 38f);
            var sr = splashGo.GetComponent<ParticleSystemRenderer>();
            _splashRenderer = sr;
            sr.renderMode = ParticleSystemRenderMode.Billboard;
            sr.material = MaterialLibrary.ParticleTextured("NV_Splash", TextureFactory.RadialGlow(48),
                new Color(0.65f, 0.88f, 1f, 0.7f), Vector2.one);
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
            _splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>
        /// One pooled weather emitter is reconfigured per location: snow, wind-blown dust and
        /// rising embers reuse the same particle system, so twenty climates cost one system
        /// rather than twenty. Fog is expressed through the mist layer and fog density.
        /// </summary>
        private void BuildWeather()
        {
            var go = new GameObject("District Weather");
            go.transform.SetParent(_camera != null ? _camera.transform : transform, false);
            go.transform.localPosition = new Vector3(0f, 9f, 14f);
            _weather = go.AddComponent<ParticleSystem>();
            var main = _weather.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 3.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.11f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.97f, 1f, 0.95f));
            main.gravityModifier = 0.06f;
            main.maxParticles = _reduced ? 140 : Mathf.RoundToInt(900f * Mathf.Clamp(_quality, 0.45f, 1.5f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = _weather.emission;
            emission.rateOverTime = 0f;
            var shape = _weather.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(30f, 2.5f, 34f);
            var velocity = _weather.velocityOverLifetime;
            velocity.enabled = false;
            _weatherRenderer = go.GetComponent<ParticleSystemRenderer>();
            _weatherSnowMaterial = MaterialLibrary.AdditiveTextured("NV_WeatherSnow",
                TextureFactory.RadialGlow(32), new Color(0.9f, 0.96f, 1f, 0.9f));
            _weatherDustMaterial = MaterialLibrary.AdditiveTextured("NV_WeatherDust",
                TextureFactory.RadialGlow(32), new Color(0.92f, 0.7f, 0.42f, 0.8f));
            _weatherEmberMaterial = MaterialLibrary.AdditiveTextured("NV_WeatherEmber",
                TextureFactory.RadialGlow(32), new Color(1f, 0.45f, 0.18f, 0.95f));
            _weatherRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            _weatherRenderer.material = _weatherSnowMaterial;
            _weatherRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _weatherRenderer.receiveShadows = false;
            _weather.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void ApplyWeather(ThemeDefinition theme, bool immediate)
        {
            if (_weather == null) return;
            int kind = theme.weather;
            bool seasonal = _app != null && _app.Session != null &&
                            _app.Session.Mode.Id == GameModeId.SeasonalEvent;
            if (seasonal && kind == 0) kind = 4;
            if (kind == _weatherKind && !immediate) return;
            _weatherKind = kind;

            bool active = kind == 2 || kind == 3 || kind == 4;
            var main = _weather.main;
            var emission = _weather.emission;
            var velocity = _weather.velocityOverLifetime;
            velocity.enabled = false;
            switch (kind)
            {
                case 2: // snow
                    main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.0f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.97f, 1f, 0.95f));
                    main.gravityModifier = 0.06f;
                    _weatherRenderer.material = _weatherSnowMaterial;
                    // Blizzard tier: the avalanche bypass drives a wind-lashed storm while the
                    // fabrication terminal sits in calm falling flakes.
                    bool blizzard = theme != null && theme.id == "maple-frost";
                    if (blizzard)
                    {
                        emission.rateOverTime = _reduced ? 130f : 560f;
                        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
                        var wind = _weather.velocityOverLifetime;
                        wind.enabled = true;
                        wind.space = ParticleSystemSimulationSpace.Local;
                        wind.x = new ParticleSystem.MinMaxCurve(5.5f, 11f);
                        wind.y = new ParticleSystem.MinMaxCurve(-0.6f, 0.4f);
                        wind.z = new ParticleSystem.MinMaxCurve(-3.4f);
                    }
                    else
                    {
                        emission.rateOverTime = _reduced ? 70f : 300f;
                    }
                    break;
                case 3: // desert dust
                    main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6.5f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.075f);
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.74f, 0.46f, 0.72f));
                    main.gravityModifier = 0.01f;
                    _weatherRenderer.material = _weatherDustMaterial;
                    emission.rateOverTime = _reduced ? 60f : 260f;
                    velocity.enabled = true;
                    velocity.space = ParticleSystemSimulationSpace.Local;
                    velocity.x = new ParticleSystem.MinMaxCurve(7f, 12f);
                    velocity.y = new ParticleSystem.MinMaxCurve(-0.4f, 0.8f);
                    velocity.z = new ParticleSystem.MinMaxCurve(-2.4f);
                    break;
                case 4: // embers
                    main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 7f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.08f);
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.2f, 0.95f),
                        new Color(1f, 0.82f, 0.4f, 0.9f));
                    main.gravityModifier = -0.035f;
                    _weatherRenderer.material = _weatherEmberMaterial;
                    emission.rateOverTime = _reduced ? 28f : 110f;
                    break;
                default:
                    emission.rateOverTime = 0f;
                    break;
            }

            if (active)
            {
                if (!_weather.isPlaying) _weather.Play();
            }
            else if (_weather.isPlaying)
            {
                _weather.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (_mist != null)
            {
                var mistEmission = _mist.emission;
                mistEmission.rateOverTime = (_reduced ? 0.35f : 0.8f) * (kind == 5 ? 5.2f : 1f);
                var mistMain = _mist.main;
                Color mistColor = theme.fog;
                mistColor.a = kind == 5 ? 0.075f : 0.028f;
                mistMain.startColor = new ParticleSystem.MinMaxGradient(mistColor);
            }
        }

        private void BuildAtmosphere()
        {
            var go = new GameObject("Low Mist");
            go.transform.SetParent(_camera != null ? _camera.transform : transform, false);
            go.transform.localPosition = new Vector3(0f, 1.2f, 34f);
            _mist = go.AddComponent<ParticleSystem>();
            var main = _mist.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 13f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.04f, 0.22f);
            main.startSize = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startColor = new Color(0.5f, 0.72f, 1f, 0.022f);
            main.maxParticles = _reduced ? 10 : 22;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = _mist.emission;
            emission.rateOverTime = _reduced ? 0.35f : 0.8f;
            var shape = _mist.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(38f, 5f, 58f);
            _mistRenderer = go.GetComponent<ParticleSystemRenderer>();
            _mistRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            _mistRenderer.material = MaterialLibrary.ParticleTextured("NV_Mist",
                TextureFactory.RadialGlow(64), new Color(0.5f, 0.72f, 1f, 0.03f), Vector2.one);
            _mistRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _mistRenderer.receiveShadows = false;
            _mist.Play();
        }

        private void BuildTraffic()
        {
            _trafficRoot = new GameObject("Air Traffic");
            _trafficRoot.transform.SetParent(transform, false);
            var bodyMaterial = MaterialLibrary.Lit(Palette.Hex("#050A15"), 0.86f, 0.78f);
            var glassMaterial = MaterialLibrary.Lit(Palette.Hex("#14304A"), 0.95f, 0.98f,
                new Color(0.35f, 0.65f, 1f), 0.6f);
            var headMaterial = MaterialLibrary.Additive(new Color(0.72f, 0.92f, 1f));
            var tailMaterial = MaterialLibrary.Additive(Palette.Red);
            for (int i = 0; i < 9; i++)
            {
                var go = new GameObject("Air Car " + i);
                go.transform.SetParent(_trafficRoot.transform, false);
                var body = PrimitiveMeshes.Create("Body", PrimitiveMeshes.Cube, bodyMaterial, go.transform,
                    Vector3.zero, new Vector3(1.25f, 0.34f, 2.8f));
                body.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                PrimitiveMeshes.Box(go.transform, "Cabin", new Vector3(0f, 0.28f, 0.12f),
                    new Vector3(0.78f, 0.3f, 1.05f), glassMaterial);
                PrimitiveMeshes.Box(go.transform, "WingL", new Vector3(-0.85f, -0.04f, 0.1f),
                    new Vector3(0.5f, 0.05f, 1.2f), bodyMaterial);
                PrimitiveMeshes.Box(go.transform, "WingR", new Vector3(0.85f, -0.04f, 0.1f),
                    new Vector3(0.5f, 0.05f, 1.2f), bodyMaterial);
                for (int side = -1; side <= 1; side += 2)
                {
                    PrimitiveMeshes.Box(go.transform, "Headlight", new Vector3(side * 0.38f, 0.02f, -1.41f),
                        new Vector3(0.22f, 0.08f, 0.04f), headMaterial);
                    PrimitiveMeshes.Box(go.transform, "Taillight", new Vector3(side * 0.38f, 0.02f, 1.41f),
                        new Vector3(0.16f, 0.06f, 0.035f), tailMaterial);
                }
                PrimitiveMeshes.Box(go.transform, "Underglow", new Vector3(0f, -0.22f, 0f),
                    new Vector3(0.9f, 0.018f, 2.2f), i % 2 == 0 ? headMaterial : tailMaterial);
                var vehicle = new TrafficVehicle
                {
                    Root = go.transform,
                    Speed = 9f + (i % 5) * 4.2f,
                    Phase = i * 1.37f,
                    BaseY = 9f + (i % 6) * 3.1f
                };
                PlaceTraffic(vehicle, true);
                _traffic.Add(vehicle);
            }
        }

        private void BuildSkyTrain()
        {
            _railRoot = new GameObject("Maglev Line");
            _railRoot.transform.SetParent(transform, false);
            var railMaterial = MaterialLibrary.Lit(new Color(0.035f, 0.06f, 0.095f), 0.88f, 0.82f);
            var railGlow = MaterialLibrary.Additive(new Color(0.25f, 0.72f, 1f));
            for (int side = -1; side <= 1; side += 2)
            {
                PrimitiveMeshes.Box(_railRoot.transform, "Rail", new Vector3(38f + side * 1.9f, 14.4f, 70f),
                    new Vector3(0.22f, 0.22f, 350f), railMaterial);
                PrimitiveMeshes.Box(_railRoot.transform, "RailGlow", new Vector3(38f + side * 1.78f, 14.33f, 70f),
                    new Vector3(0.05f, 0.05f, 350f), railGlow);
            }

            _trainRoot = new GameObject("Sky Train");
            _trainRoot.transform.SetParent(transform, false);
            _trainRoot.transform.position = new Vector3(38f, 16f, 110f);
            var bodyMaterial = MaterialLibrary.Lit(new Color(0.045f, 0.075f, 0.12f), 0.9f, 0.88f);
            var trimMaterial = MaterialLibrary.Lit(new Color(0.55f, 0.68f, 0.82f), 0.95f, 0.94f);
            var windowMaterial = MaterialLibrary.Lit(new Color(0.28f, 0.62f, 0.95f), 0.55f, 0.97f,
                new Color(0.24f, 0.65f, 1f), 1.8f);
            var underMaterial = MaterialLibrary.Additive(new Color(0.22f, 0.82f, 1f));
            for (int car = 0; car < 4; car++)
            {
                var carRoot = new GameObject("Train Car " + car).transform;
                carRoot.SetParent(_trainRoot.transform, false);
                carRoot.localPosition = new Vector3(0f, 0f, -car * 8.6f);
                PrimitiveMeshes.Box(carRoot, "Body", Vector3.zero, new Vector3(3.4f, 1.25f, 7.7f), bodyMaterial);
                PrimitiveMeshes.Box(carRoot, "Roof", new Vector3(0f, 0.7f, 0f), new Vector3(3.1f, 0.16f, 7.3f), trimMaterial);
                PrimitiveMeshes.Box(carRoot, "WindowL", new Vector3(-1.71f, 0.12f, 0f),
                    new Vector3(0.025f, 0.28f, 6.9f), windowMaterial);
                PrimitiveMeshes.Box(carRoot, "WindowR", new Vector3(1.71f, 0.12f, 0f),
                    new Vector3(0.025f, 0.28f, 6.9f), windowMaterial);
                PrimitiveMeshes.Box(carRoot, "Skirt", new Vector3(0f, -0.63f, 0f),
                    new Vector3(3.0f, 0.11f, 7.2f), trimMaterial);
                PrimitiveMeshes.Box(carRoot, "Underglow", new Vector3(0f, -0.71f, 0f),
                    new Vector3(2.8f, 0.035f, 6.8f), underMaterial);
                if (car == 0)
                    PrimitiveMeshes.Box(carRoot, "Headlight", new Vector3(0f, 0.02f, -3.88f),
                        new Vector3(1.9f, 0.09f, 0.05f), underMaterial);
            }
        }

        private void UpdateSkyTrain(float dt, float runnerZ)
        {
            if (_trainRoot == null) return;
            if (_railRoot != null)
            {
                var railPos = _railRoot.transform.position;
                railPos.z = runnerZ + 70f;
                _railRoot.transform.position = railPos;
            }
            var p = _trainRoot.transform.position;
            p.z -= dt * 26f;
            p.y = 16f + Mathf.Sin(_time * 0.7f) * 0.06f;
            if (!_trainPassPlayed && p.z < runnerZ + 46f)
            {
                _trainPassPlayed = true;
                _app?.Audio.PlaySfxAt("sfx_train_pass", new Vector3(p.x, p.y, p.z), 0.42f, 18f, 280f);
            }
            if (p.z < runnerZ - 70f)
            {
                p.z = runnerZ + 195f;
                _trainPassPlayed = false;
            }
            _trainRoot.transform.position = p;
        }

        private void PlaceTraffic(TrafficVehicle vehicle, bool initial)
        {
            float z = _runner != null ? _runner.transform.position.z : 0f;
            float side = UnityEngine.Random.value > 0.5f ? 1f : -1f;
            vehicle.Root.position = new Vector3(
                side * UnityEngine.Random.Range(11f, 31f),
                vehicle.BaseY + UnityEngine.Random.Range(-0.8f, 0.8f),
                z + (initial ? UnityEngine.Random.Range(-15f, 150f) : UnityEngine.Random.Range(95f, 185f)));
            vehicle.BaseY = vehicle.Root.position.y;
            vehicle.Root.rotation = Quaternion.identity;
        }

        private void BuildEmbers()
        {
            var go = new GameObject("Monsoon Embers");
            go.transform.SetParent(_camera != null ? _camera.transform : transform, false);
            go.transform.localPosition = new Vector3(0f, 1f, 10f);
            _snow = go.AddComponent<ParticleSystem>();
            var main = _snow.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.07f);
            main.startColor = new ParticleSystem.MinMaxGradient(Palette.Cyan, Palette.Magenta);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = _reduced ? 30 : 90;
            var emission = _snow.emission;
            emission.rateOverTime = 14f;
            var shape = _snow.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(22f, 8f, 24f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = MaterialLibrary.Additive(Palette.Cyan);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _snow.Play();
        }

        /// <summary>
        /// Chase target for BOSS CHASE. The warden is a merged primitive assembly that runs
        /// ahead of the player and closes as the target distance approaches. It is deliberately
        /// non-colliding: the mode tests pursuit, not a second hitbox hidden in the road.
        /// </summary>
        private void BuildBossChase()
        {
            _bossRoot = new GameObject("Warden Frame").transform;
            _bossRoot.SetParent(transform, false);
            var body = MaterialLibrary.Lit(new Color(0.12f, 0.12f, 0.16f), 0.9f, 0.72f);
            var plate = MaterialLibrary.Lit(new Color(0.32f, 0.10f, 0.12f), 0.82f, 0.55f);
            var trim = MaterialLibrary.Lit(new Color(0.62f, 0.66f, 0.72f), 0.94f, 0.88f);
            var eye = MaterialLibrary.Additive(Palette.Red);
            var thruster = MaterialLibrary.Additive(Palette.Orange);

            PrimitiveMeshes.Box(_bossRoot, "Torso", new Vector3(0f, 2.55f, 0f), new Vector3(2.4f, 2.1f, 1.45f), plate);
            PrimitiveMeshes.Box(_bossRoot, "ChestPlate", new Vector3(0f, 2.65f, 0.76f), new Vector3(1.7f, 1.2f, 0.12f), body);
            PrimitiveMeshes.Box(_bossRoot, "Pelvis", new Vector3(0f, 1.38f, 0f), new Vector3(1.55f, 0.62f, 1.15f), body);
            PrimitiveMeshes.Box(_bossRoot, "Head", new Vector3(0f, 3.95f, 0.1f), new Vector3(0.88f, 0.72f, 0.86f), trim);
            PrimitiveMeshes.Box(_bossRoot, "Visor", new Vector3(0f, 3.98f, 0.56f), new Vector3(0.66f, 0.19f, 0.05f), eye);
            PrimitiveMeshes.Box(_bossRoot, "Crest", new Vector3(0f, 4.42f, 0f), new Vector3(0.12f, 0.36f, 0.9f), plate);
            for (int side = -1; side <= 1; side += 2)
            {
                PrimitiveMeshes.Box(_bossRoot, "Shoulder", new Vector3(side * 1.52f, 3.18f, 0f),
                    new Vector3(0.72f, 0.76f, 1.05f), plate);
                PrimitiveMeshes.Box(_bossRoot, "ShoulderGlow", new Vector3(side * 1.9f, 3.2f, 0f),
                    new Vector3(0.05f, 0.5f, 0.7f), eye);
                PrimitiveMeshes.Box(_bossRoot, "UpperArm", new Vector3(side * 1.58f, 2.35f, 0f),
                    new Vector3(0.46f, 1.1f, 0.52f), body);
                PrimitiveMeshes.Box(_bossRoot, "Forearm", new Vector3(side * 1.58f, 1.5f, 0f),
                    new Vector3(0.54f, 0.85f, 0.62f), plate);
                PrimitiveMeshes.Box(_bossRoot, "Thigh", new Vector3(side * 0.62f, 0.92f, 0f),
                    new Vector3(0.5f, 1.05f, 0.6f), plate);
                PrimitiveMeshes.Box(_bossRoot, "Shin", new Vector3(side * 0.62f, 0.25f, 0f),
                    new Vector3(0.58f, 0.62f, 0.78f), body);
                PrimitiveMeshes.Create("Thruster", PrimitiveMeshes.Cylinder, thruster, _bossRoot,
                    new Vector3(side * 0.48f, 2.2f, -0.85f), new Vector3(0.22f, 0.5f, 0.22f),
                    Quaternion.Euler(90f, 0f, 0f));
            }
            PrimitiveMeshes.Box(_bossRoot, "Spine", new Vector3(0f, 2.62f, -0.76f), new Vector3(0.1f, 1.7f, 0.06f), eye);
            _bossRoot.position = new Vector3(0f, 0f, 30f);
        }

        private void UpdateBossChase(Vector3 runnerPosition, float dt)
        {
            if (_bossRoot == null) return;
            float progress = _app?.Session != null && _app.Session.Mode.HasTargetDistance
                ? Mathf.Clamp01(_app.Session.Distance / _app.Session.Mode.TargetDistance) : 0f;
            float gap = Mathf.Lerp(32f, 11f, progress);
            float weave = Mathf.Sin(_time * 1.1f) * (2.4f - progress * 0.9f);
            _bossRoot.position = new Vector3(weave, 0.1f + Mathf.Sin(_time * 5.2f) * 0.05f, runnerPosition.z + gap);
            _bossRoot.rotation = Quaternion.Euler(0f, Mathf.Sin(_time * 0.9f) * 5f, Mathf.Sin(_time * 2.6f) * 2f);
        }

        private void OnLocationChanged(int zoneIndex) => ApplyBiome(zoneIndex, false);
        private void ApplyBiome(int biome, bool immediate)
        {
            if (_track == null || _dome == null) return;
            _biome = LocationCatalog.Wrap(biome);
            var theme = _app.Config.LocationTheme(_biome);
            if (theme == null) return;
            _theme = theme;
            _timeOfDay = ResolveTimeOfDay(_biome);
            Material material = GetDomeMaterial(_biome, _timeOfDay, theme);

            if (immediate)
            {
                _domeMaterial = material;
                _dome.GetComponent<MeshRenderer>().sharedMaterial = _domeMaterial;
                if (_domeFadeRenderer != null)
                {
                    _domeFadeRenderer.gameObject.SetActive(false);
                    _skyFadeActive = false;
                }
                _transition = 1f;
            }
            else
            {
                // Keep the old dome visible and blend the incoming gradient over it. The
                // fade shell is slightly closer to the camera, so depth testing lets it
                // cover the old sky while every building/tree remains in front.
                _pendingDomeMaterial = material;
                int fadeKey = DomeKey(_biome, _timeOfDay.Phase);
                if (!_domeFadeMaterials.TryGetValue(fadeKey, out Material fadeMaterial))
                {
                    fadeMaterial = MaterialLibrary.UnlitTransparentTextured(
                        "NV_SkyFade_" + _biome + "_" + (int)_timeOfDay.Phase,
                        material.mainTexture, new Color(1f, 1f, 1f, 0f), new Vector2(3f, 1f));
                    if (fadeMaterial.HasProperty("_Cull")) fadeMaterial.SetFloat("_Cull", 1f);
                    fadeMaterial.renderQueue = 3990;
                    _domeFadeMaterials[fadeKey] = fadeMaterial;
                }
                _domeFadeMaterial = fadeMaterial;
                SetFadeAlpha(0f);
                if (_domeFadeRenderer != null)
                {
                    _domeFadeRenderer.sharedMaterial = _domeFadeMaterial;
                    _domeFadeRenderer.gameObject.SetActive(true);
                }
                _skyFadeActive = true;
                _skyFadeT = 0f;
                _transition = 0f;
            }

            // Capture the currently displayed values, not the last target. This allows a
            // second biome change to interrupt a transition without snapping.
            _fromFog = RenderSettings.fogColor;
            _fromFogDensity = RenderSettings.fogDensity;
            _fromAmbientSky = RenderSettings.ambientSkyColor;
            _fromAmbientEquator = RenderSettings.ambientEquatorColor;
            _fromAmbientGround = RenderSettings.ambientGroundColor;
            _fromAmbientIntensity = RenderSettings.ambientIntensity;
            _fromReflectionIntensity = RenderSettings.reflectionIntensity;
            _fromSunColor = _sun != null ? _sun.color : Color.white;
            _fromSunIntensity = _sun != null ? _sun.intensity : 0f;
            _fromSunRotation = _sun != null ? _sun.transform.rotation : Quaternion.identity;
            _fromMoonColor = _moonLight != null ? _moonLight.color : new Color(0.62f, 0.76f, 1f);
            _fromMoonIntensity = _moonLight != null ? _moonLight.intensity : 0f;
            _fromMoonRotation = _moonLight != null ? _moonLight.transform.rotation : Quaternion.identity;
            _fromShadowStrength = _sun != null ? _sun.shadowStrength : 0f;
            _fromRimColor = _rim != null ? _rim.color : Palette.Magenta;
            _fromCyanFill = _cyanFill != null ? _cyanFill.color : Palette.Cyan;
            _fromMagentaFill = _magentaFill != null ? _magentaFill.color : Palette.Magenta;
            _fromWarmFill = _warmFill != null ? _warmFill.color : Palette.Amber;
            _fromBackground = _camera != null ? _camera.backgroundColor : theme.skyBottom;

            _targetFog = ThemedFog(theme, _timeOfDay);
            _targetFogDensity = ThemedFogDensity(theme, _timeOfDay);
            _targetAmbientSky = ThemedAmbientSky(theme, _timeOfDay);
            _targetAmbientEquator = ThemedAmbientEquator(theme, _timeOfDay);
            _targetAmbientGround = ThemedAmbientGround(theme, _timeOfDay);
            _targetAmbientIntensity = ThemedAmbientIntensity(_timeOfDay);
            _targetReflectionIntensity = ThemedReflectionIntensity(_timeOfDay);
            _targetSunColor = ThemedSunColor(theme, _timeOfDay);
            _targetSunIntensity = ThemedSunIntensity(_timeOfDay);
            _targetSunRotation = Quaternion.Euler(_timeOfDay.SunElevation, _timeOfDay.SunAzimuth, 0f);
            _targetMoonColor = ToColor(_timeOfDay.MoonColor);
            _targetMoonIntensity = _timeOfDay.MoonIntensity * (_quality >= 2f ? 0.9f : 0.6f);
            _targetMoonRotation = Quaternion.Euler(
                Mathf.Max(12f, -_timeOfDay.SunElevation + 18f), _timeOfDay.SunAzimuth + 180f, 0f);
            _targetShadowStrength = _timeOfDay.ShadowStrength;
            _targetRimColor = ThemedRim(theme, _timeOfDay);
            _targetCyanFill = ThemedCyanFill(theme);
            _targetMagentaFill = ThemedMagentaFill(theme);
            _targetWarmFill = ThemedWarmFill(theme);
            _targetBackground = ThemedBackground(theme, _timeOfDay);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientIntensity = _targetAmbientIntensity;
            RenderSettings.reflectionIntensity = _targetReflectionIntensity;

            if (!_reflections.TryGetValue(_biome, out Cubemap reflection))
            {
                reflection = TextureFactory.ReflectionCubemap("NV_Reflection_" + _biome,
                    ThemedSkyTop(theme, _timeOfDay), ThemedFog(theme, _timeOfDay), theme.accent, 64);
                _reflections[_biome] = reflection;
            }
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = reflection;

            if (!_cloudMaterials.TryGetValue(_biome, out Material cloudMaterial))
            {
                var cloudTexture = TextureFactory.CloudLayer(6100 + _biome);
                cloudMaterial = MaterialLibrary.UnlitAdditiveTextured("NV_CloudMat_" + _biome, cloudTexture,
                    new Color(0.45f, 0.68f, 1f, 0.3f), new Vector2(3f, 1f), true, 1);
                _cloudMaterials[_biome] = cloudMaterial;
            }
            _cloudTint = Color.Lerp(new Color(0.45f, 0.68f, 1f), ToColor(_timeOfDay.CloudTint), 0.55f);
            SetMaterialTint(cloudMaterial, new Color(_cloudTint.r, _cloudTint.g, _cloudTint.b, 0.3f));
            if (_cloudA != null) _cloudA.GetComponent<MeshRenderer>().sharedMaterial = cloudMaterial;
            if (_cloudB != null) _cloudB.GetComponent<MeshRenderer>().sharedMaterial = cloudMaterial;

            bool airTraffic = SupportsAirTraffic(theme);
            if (_trafficRoot != null) _trafficRoot.SetActive(airTraffic);
            bool skyTrain = SupportsSkyTrain(theme);
            if (_trainRoot != null) _trainRoot.SetActive(skyTrain);
            if (_railRoot != null) _railRoot.SetActive(skyTrain);

            bool rain = theme.weather == 1 || theme.rain || _app != null && _app.Session != null &&
                        _app.Session.Mode.Id == GameModeId.SeasonalEvent;
            _rainTier = RainTier(theme);
            SetRain(rain, immediate);
            ApplyWeather(theme, immediate);
            _camera?.GetComponent<RunnerCamera>()?.SetBiomeColor(theme.accent);
            if (immediate)
            {
                ApplyTransition(1f);
                ApplyTimeOfDay(_timeOfDay);
            }
            else ApplyTransition(0f);
        }

        private static int DomeKey(int zoneIndex, TimeOfDayPhase phase) =>
            LocationCatalog.Wrap(zoneIndex) * TimeOfDayCatalog.Count + (int)phase;

        private Material GetDomeMaterial(int zoneIndex, TimeOfDayState tod, ThemeDefinition theme)
        {
            int key = DomeKey(zoneIndex, tod.Phase);
            if (_domeMaterials.TryGetValue(key, out Material cached)) return cached;
            Color bottom = ThemedSkyBottom(theme, tod);
            Color top = ThemedSkyTop(theme, tod);
            var texture = TextureFactory.VerticalGradient(
                "NV_Sky_" + zoneIndex + "_" + (int)tod.Phase, bottom, top, 256);
            var material = new Material(MaterialLibrary.UnlitShader)
            {
                name = "NV_SkyDome_" + zoneIndex + "_" + (int)tod.Phase,
                mainTexture = texture
            };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", new Vector2(3f, 1f));
            }
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 1f); // Front-face culling for an interior dome.
            material.renderQueue = 1990;
            _domeMaterials[key] = material;
            return material;
        }

        private static Color ToColor(TodColor c) => new Color(c.R, c.G, c.B, 1f);

        private static Color ThemedSkyTop(ThemeDefinition theme, TimeOfDayState tod) =>
            Color.Lerp(theme.skyTop, ToColor(tod.SkyTop), 0.68f);

        private static Color ThemedSkyBottom(ThemeDefinition theme, TimeOfDayState tod) =>
            Color.Lerp(theme.skyBottom, ToColor(tod.SkyBottom), 0.78f);

        private static Color ThemedFog(ThemeDefinition theme, TimeOfDayState tod) =>
            Color.Lerp(theme.fog, ToColor(tod.Fog), 0.72f);

        private static float ThemedFogDensity(ThemeDefinition theme, TimeOfDayState tod)
        {
            float density = tod.FogDensity + (theme.weather == 5 ? 0.0055f : 0f) +
                            (theme.rain ? 0.0015f : 0f);
            return Mathf.Clamp(density, 0.004f, 0.03f);
        }

        private static Color ThemedAmbientSky(ThemeDefinition theme, TimeOfDayState tod) =>
            Color.Lerp(Color.Lerp(theme.skyTop, ToColor(tod.SkyTop), 0.58f), theme.accent, 0.1f);

        private static Color ThemedAmbientEquator(ThemeDefinition theme, TimeOfDayState tod) =>
            Color.Lerp(Color.Lerp(theme.buildingA, ToColor(tod.SkyBottom), 0.42f), theme.accent, 0.18f);

        private static Color ThemedAmbientGround(ThemeDefinition theme, TimeOfDayState tod) =>
            Color.Lerp(Color.Lerp(theme.ground, ToColor(tod.Fog), 0.34f), Palette.Void, 0.46f);

        private float ThemedAmbientIntensity(TimeOfDayState tod) =>
            DesiredAmbientIntensity * Mathf.Clamp(tod.AmbientIntensity / 1.05f, 0.3f, 1.1f);

        private float ThemedReflectionIntensity(TimeOfDayState tod)
        {
            float quality = _reflectionQuality >= 2 ? 1f : (_reflectionQuality >= 1 ? 0.68f : 0.18f);
            return quality * Mathf.Clamp01(tod.ReflectionIntensity);
        }

        private static Color ThemedSunColor(ThemeDefinition theme, TimeOfDayState tod) =>
            Color.Lerp(ToColor(tod.SunColor), theme.accent, 0.16f);

        private float ThemedSunIntensity(TimeOfDayState tod) => DesiredSunIntensity * tod.SunIntensity;

        private static Color ThemedRim(ThemeDefinition theme, TimeOfDayState tod) =>
            Color.Lerp(Color.Lerp(theme.accent, Palette.Magenta, 0.5f), ToColor(tod.MoonColor), 0.3f);

        private static Color ThemedCyanFill(ThemeDefinition theme) =>
            Color.Lerp(theme.accent, Palette.Cyan, 0.4f);

        private static Color ThemedMagentaFill(ThemeDefinition theme) =>
            Color.Lerp(theme.accent, Palette.Magenta, 0.55f);

        private static Color ThemedWarmFill(ThemeDefinition theme) =>
            Color.Lerp(Palette.Amber, theme.accent, 0.12f);

        private static Color ThemedBackground(ThemeDefinition theme, TimeOfDayState tod) =>
            Color.Lerp(theme.skyBottom, ToColor(tod.SkyBottom), 0.8f);

        private TimeOfDayState ResolveTimeOfDay(int zoneIndex)
        {
            int locationPhase = 0;
            var theme = _app != null ? _app.Config.LocationTheme(zoneIndex) : null;
            if (theme != null) locationPhase = theme.timeOfDay;
            float distance = _track != null ? _track.TrackDistance : 0f;
            return TimeOfDayCatalog.Resolve(_timeOfDayMode, _timeOfDayFixedPhase, distance,
                locationPhase, _timeOfDayCycleMetres);
        }

        private void ApplyTimeOfDay(TimeOfDayState tod)
        {
            _timeOfDay = tod;
            var theme = _theme;
            if (theme == null) return;

            if (_sun != null)
            {
                _sun.transform.rotation = Quaternion.Euler(tod.SunElevation, tod.SunAzimuth, 0f);
                _sun.color = ThemedSunColor(theme, tod);
                _currentSunIntensity = ThemedSunIntensity(tod);
                _sun.intensity = _currentSunIntensity;
                _sun.shadowStrength = tod.ShadowStrength;
            }
            if (_moonLight != null)
            {
                _moonLight.transform.rotation = Quaternion.Euler(
                    Mathf.Max(12f, -tod.SunElevation + 18f), tod.SunAzimuth + 180f, 0f);
                _moonLight.color = ToColor(tod.MoonColor);
                _moonLight.intensity = tod.MoonIntensity * (_quality >= 2f ? 0.9f : 0.6f);
            }
            if (_rim != null)
            {
                _rim.color = ThemedRim(theme, tod);
                _rim.intensity = (_quality >= 1f ? 0.42f : 0.22f) * (0.35f + tod.NeonIntensity * 0.55f);
            }
            if (_cyanFill != null)
                _cyanFill.intensity = 9f * tod.NeonIntensity;
            if (_magentaFill != null)
                _magentaFill.intensity = 8f * tod.NeonIntensity;
            if (_warmFill != null)
                _warmFill.intensity = 5f * tod.StreetlightIntensity;

            RenderSettings.fogColor = ThemedFog(theme, tod);
            RenderSettings.fogDensity = ThemedFogDensity(theme, tod);
            RenderSettings.ambientSkyColor = ThemedAmbientSky(theme, tod);
            RenderSettings.ambientEquatorColor = ThemedAmbientEquator(theme, tod);
            RenderSettings.ambientGroundColor = ThemedAmbientGround(theme, tod);
            RenderSettings.ambientIntensity = ThemedAmbientIntensity(tod);
            RenderSettings.reflectionIntensity = ThemedReflectionIntensity(tod);
            if (_camera != null) _camera.backgroundColor = ThemedBackground(theme, tod);

            bool showStars = tod.StarVisibility > 0.22f;
            for (int i = 0; i < _starRenderers.Count; i++)
                if (_starRenderers[i] != null) _starRenderers[i].enabled = showStars;
            if (_starsRoot != null) _starsRoot.gameObject.SetActive(showStars);
            if (_moon != null)
            {
                bool showMoon = tod.MoonIntensity > 0.04f;
                _moon.gameObject.SetActive(showMoon);
                if (showMoon)
                {
                    float elevation = Mathf.Lerp(0.18f, 0.92f, Mathf.Clamp01(tod.StarVisibility));
                    float azimuth = (tod.SunAzimuth + 180f) * Mathf.Deg2Rad;
                    _moon.localPosition = new Vector3(
                        Mathf.Sin(azimuth) * Mathf.Cos(elevation),
                        Mathf.Sin(elevation),
                        Mathf.Cos(azimuth) * Mathf.Cos(elevation)) * 350f;
                    _moon.localScale = Vector3.one * (16f + tod.StarVisibility * 8f);
                }
            }
            if (_camera != null && _theme != null)
                ApplyCloudTint(theme, tod);
        }

        private void ApplyCloudTint(ThemeDefinition theme, TimeOfDayState tod)
        {
            Color tint = Color.Lerp(new Color(0.45f, 0.68f, 1f), ToColor(tod.CloudTint), 0.55f);
            _cloudTint = tint;
            SetMaterialTint(_cloudA != null
                ? _cloudA.GetComponent<MeshRenderer>().sharedMaterial : null,
                new Color(tint.r, tint.g, tint.b, 0.3f));
            SetMaterialTint(_cloudB != null
                ? _cloudB.GetComponent<MeshRenderer>().sharedMaterial : null,
                new Color(tint.r, tint.g, tint.b, 0.24f));
        }

        private static void SetMaterialTint(Material material, Color color)
        {
            if (material == null) return;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private static bool SupportsAirTraffic(ThemeDefinition theme)
        {
            if (theme == null) return false;
            switch (theme.id)
            {
                case "obsidian-line":
                case "verdant-grid":
                case "maple-frost":
                case "aurora-pass":
                case "null-cathedral":
                case "cryo-harbor":
                    return false;
                default:
                    return true;
            }
        }

        private static bool SupportsSkyTrain(ThemeDefinition theme)
        {
            if (theme == null) return false;
            switch (theme.id)
            {
                case "sakura-circuit":
                case "seoul-nexus":
                case "neon-tokyo":
                case "skyway-09":
                case "kessler-reach":
                case "eurodyne-spire":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Location-keyed rain intensity. The weather contract stays 0-5 in the catalog; this
        /// only scales the visual emitter per climate so the four rainy districts read
        /// differently: a monsoon downpour, a city shower, canopy drip and a thin dead rain.
        /// </summary>
        private static float RainTier(ThemeDefinition theme)
        {
            if (theme == null) return 1f;
            switch (theme.id)
            {
                case "monsoon-quay": return 1.6f;    // flooded cargo terminal: heavy rain
                case "neon-tokyo": return 1.18f;     // dense city shower
                case "eurodyne-spire": return 0.85f; // steady continental rain
                case "verdant-grid": return 0.5f;    // canopy drip
                case "null-cathedral": return 0.45f; // dead rain through fog
                default: return 1f;
            }
        }

        private float DesiredSunIntensity => _quality >= 2f ? 1.18f : (_quality >= 1f ? 1.0f : 0.68f);
        private float DesiredAmbientIntensity => _quality >= 2f ? 1.05f : (_quality >= 1f ? 0.9f : 0.72f);

        private void SetRain(bool active, bool immediate)
        {
            float tier = Mathf.Clamp(_rainTier, 0.35f, 1.7f);
            float rainRate = _effectsQuality >= 2 ? 650f : (_effectsQuality >= 1 ? 400f : 220f);
            float splashRate = _effectsQuality >= 2 ? 220f : (_effectsQuality >= 1 ? 130f : 65f);
            _targetRainRate = active ? (_reduced ? 80f : rainRate * tier) : 0f;
            _targetSplashRate = active ? (_reduced ? 30f : splashRate * Mathf.Clamp(tier, 0.6f, 1.5f)) : 0f;
            if (active && _rainRenderer != null)
            {
                // Downpour shape: heavier tiers fall harder and lean further downwind.
                _rainRenderer.lengthScale = 5.6f + tier * 1.7f;
                var velocity = _rain.velocityOverLifetime;
                velocity.x = new ParticleSystem.MinMaxCurve(1.6f + tier * 1.1f);
                velocity.z = new ParticleSystem.MinMaxCurve(-4.2f * tier - 1.8f);
            }
            if (immediate)
            {
                _rainRate = _targetRainRate;
                _splashRate = _targetSplashRate;
                if (_rain != null)
                {
                    var emission = _rain.emission;
                    emission.rateOverTime = _rainRate;
                    if (active) { if (!_rain.isPlaying) _rain.Play(); }
                    else if (_rain.isPlaying) _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                if (_splash != null)
                {
                    var emission = _splash.emission;
                    emission.rateOverTime = _splashRate;
                    if (active) { if (!_splash.isPlaying) _splash.Play(); }
                    else if (_splash.isPlaying) _splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
            else if (active)
            {
                if (_rain != null && !_rain.isPlaying) _rain.Play();
                if (_splash != null && !_splash.isPlaying) _splash.Play();
            }
        }

        private void ApplyTransition(float t)
        {
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            RenderSettings.fogColor = Color.Lerp(_fromFog, _targetFog, blend);
            float fogBump = 1f + Mathf.Sin(blend * Mathf.PI) * 0.14f;
            RenderSettings.fogDensity = Mathf.Lerp(_fromFogDensity, _targetFogDensity, blend) * fogBump;
            RenderSettings.ambientSkyColor = Color.Lerp(_fromAmbientSky, _targetAmbientSky, blend);
            RenderSettings.ambientEquatorColor = Color.Lerp(_fromAmbientEquator, _targetAmbientEquator, blend);
            RenderSettings.ambientGroundColor = Color.Lerp(_fromAmbientGround, _targetAmbientGround, blend);
            RenderSettings.ambientIntensity = Mathf.Lerp(_fromAmbientIntensity, _targetAmbientIntensity, blend);
            RenderSettings.reflectionIntensity = Mathf.Lerp(_fromReflectionIntensity, _targetReflectionIntensity, blend);
            _currentSunIntensity = Mathf.Lerp(_fromSunIntensity, _targetSunIntensity, blend);
            if (_sun != null)
            {
                _sun.color = Color.Lerp(_fromSunColor, _targetSunColor, blend);
                _sun.intensity = _currentSunIntensity;
                _sun.transform.rotation = Quaternion.Slerp(_fromSunRotation, _targetSunRotation, blend);
                _sun.shadowStrength = Mathf.Lerp(_fromShadowStrength, _targetShadowStrength, blend);
            }
            if (_moonLight != null)
            {
                _moonLight.color = Color.Lerp(_fromMoonColor, _targetMoonColor, blend);
                _moonLight.intensity = Mathf.Lerp(_fromMoonIntensity, _targetMoonIntensity, blend);
                _moonLight.transform.rotation = Quaternion.Slerp(_fromMoonRotation, _targetMoonRotation, blend);
            }
            if (_rim != null) _rim.color = Color.Lerp(_fromRimColor, _targetRimColor, blend);
            if (_cyanFill != null) _cyanFill.color = Color.Lerp(_fromCyanFill, _targetCyanFill, blend);
            if (_magentaFill != null) _magentaFill.color = Color.Lerp(_fromMagentaFill, _targetMagentaFill, blend);
            if (_warmFill != null) _warmFill.color = Color.Lerp(_fromWarmFill, _targetWarmFill, blend);
            if (_camera != null) _camera.backgroundColor = Color.Lerp(_fromBackground, _targetBackground, blend);
        }

        private void UpdateSkyFade(float dt)
        {
            if (!_skyFadeActive || _domeFadeRenderer == null) return;
            _skyFadeT = Mathf.Min(1f, _skyFadeT + dt / (TransitionSeconds * 0.86f));
            float alpha = Mathf.SmoothStep(0f, 1f, _skyFadeT);
            SetFadeAlpha(alpha);
            if (_skyFadeT >= 0.94f && _pendingDomeMaterial != null)
            {
                _domeMaterial = _pendingDomeMaterial;
                _dome.GetComponent<MeshRenderer>().sharedMaterial = _domeMaterial;
                _skyFadeActive = false;
                _domeFadeRenderer.gameObject.SetActive(false);
            }
        }

        private void SetFadeAlpha(float alpha)
        {
            if (_domeFadeMaterial == null) return;
            var color = new Color(1f, 1f, 1f, alpha);
            if (_domeFadeMaterial.HasProperty("_BaseColor")) _domeFadeMaterial.SetColor("_BaseColor", color);
            if (_domeFadeMaterial.HasProperty("_Color")) _domeFadeMaterial.SetColor("_Color", color);
        }

        private void UpdateRain(float dt)
        {
            if (_rain != null)
            {
                _rainRate = Mathf.MoveTowards(_rainRate, _targetRainRate,
                    dt * Mathf.Max(70f, _targetRainRate * 0.8f));
                var emission = _rain.emission;
                emission.rateOverTime = _rainRate;
                if (_targetRainRate > 0.01f && !_rain.isPlaying) _rain.Play();
                if (_targetRainRate <= 0.01f && _rainRate <= 0.05f && _rain.isPlaying)
                    _rain.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            if (_splash != null)
            {
                _splashRate = Mathf.MoveTowards(_splashRate, _targetSplashRate,
                    dt * Mathf.Max(35f, _targetSplashRate * 0.8f));
                var emission = _splash.emission;
                emission.rateOverTime = _splashRate;
                if (_targetSplashRate > 0.01f && !_splash.isPlaying) _splash.Play();
                if (_targetSplashRate <= 0.01f && _splashRate <= 0.05f && _splash.isPlaying)
                    _splash.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _time += dt;
            if (_runner == null) return;
            var p = _runner.transform.position;
            if (_dome != null) _dome.position = new Vector3(p.x, 0f, p.z);
            if (_skyDecor != null) _skyDecor.position = new Vector3(p.x, 0f, p.z);
            if (_cloudA != null) _cloudA.Rotate(0f, dt * 0.65f, 0f, Space.Self);
            if (_cloudB != null) _cloudB.Rotate(0f, dt * -0.38f, 0f, Space.Self);

            if (_trafficRoot != null && _trafficRoot.activeSelf) UpdateTraffic(dt, p.z);
            if (_trainRoot != null && _trainRoot.activeSelf) UpdateSkyTrain(dt, p.z);
            if (_bossRoot != null) UpdateBossChase(p, dt);

            if (_transition < 1f)
            {
                _transition = Mathf.Min(1f, _transition + dt / TransitionSeconds);
                ApplyTransition(_transition);
            }
            else
            {
                // Authored, fixed and dynamic modes all resolve through the same seven-state
                // catalogue. Dynamic mode therefore moves the sun, moon, sky tint, fog,
                // shadows, neon, street lights and reflections continuously with distance.
                _timeOfDay = ResolveTimeOfDay(_biome);
                ApplyTimeOfDay(_timeOfDay);
            }
            UpdateSkyFade(dt);
            UpdateRain(dt);

            if (_biome == 0 && _flash > 0f) _flash = Mathf.Max(0f, _flash - dt * 5.2f);
            if (_biome == 0 && !_reduced && Time.time > _nextFlash)
            {
                _flash = 1f;
                _nextFlash = Time.time + UnityEngine.Random.Range(11f, 24f);
                _thunderTimer = 0.7f;
            }
            if (_thunderTimer > 0f)
            {
                _thunderTimer -= dt;
                if (_thunderTimer <= 0f)
                {
                    var storm = new Vector3(p.x + UnityEngine.Random.Range(-120f, 120f), 70f,
                        p.z + UnityEngine.Random.Range(80f, 220f));
                    _app?.Audio.PlaySfxAt("sfx_thunder", storm, 0.62f, 55f, 480f);
                }
            }

            if (_sun != null) _sun.intensity = _currentSunIntensity + _flash * 2.4f;
            if (_flash > 0.01f)
            {
                RenderSettings.ambientSkyColor = Color.Lerp(RenderSettings.ambientSkyColor, Color.white, _flash * 0.45f);
                if (_cyanFill != null) _cyanFill.intensity += _flash * 22f;
                if (_magentaFill != null) _magentaFill.intensity += _flash * 18f;
            }
        }

        private void UpdateTraffic(float dt, float runnerZ)
        {
            for (int i = 0; i < _traffic.Count; i++)
            {
                var vehicle = _traffic[i];
                if (vehicle.Root == null) continue;
                var p = vehicle.Root.position;
                p.z -= vehicle.Speed * dt;
                p.y = vehicle.BaseY + Mathf.Sin(_time * 0.55f + vehicle.Phase) * 0.24f;
                vehicle.Root.position = p;
                if (p.z < runnerZ - 26f) PlaceTraffic(vehicle, false);
            }
        }

        private float _nextFlash = 15f;
        private ThemeDefinition _theme;

        private void OnDestroy()
        {
            if (_track != null) _track.LocationChanged -= OnLocationChanged;
            foreach (var kv in _domeMaterials)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            _domeMaterials.Clear();
            foreach (var kv in _domeFadeMaterials)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            _domeFadeMaterials.Clear();
            foreach (var kv in _cloudMaterials)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            _cloudMaterials.Clear();
            foreach (var kv in _reflections)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            _reflections.Clear();
        }
    }
}
