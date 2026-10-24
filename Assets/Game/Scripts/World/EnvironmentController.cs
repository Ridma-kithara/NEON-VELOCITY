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
        private Light _rim;
        private Light _cyanFill;
        private Light _magentaFill;
        private Light _warmFill;
        private Transform _dome;
        private Transform _skyDecor;
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
        private Color _fromSunColor;
        private Color _targetSunColor;
        private float _fromSunIntensity;
        private float _targetSunIntensity;
        private float _currentSunIntensity;
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
        private float _targetRainRate;
        private float _targetSplashRate;
        private ParticleSystem _rain;
        private ParticleSystemRenderer _rainRenderer;
        private ParticleSystem _splash;
        private ParticleSystemRenderer _splashRenderer;
        private ParticleSystem _mist;
        private ParticleSystemRenderer _mistRenderer;
        private ParticleSystem _snow;
        private readonly List<TrafficVehicle> _traffic = new List<TrafficVehicle>();
        private GameObject _trafficRoot;
        private GameObject _trainRoot;
        private GameObject _railRoot;
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
            BuildAtmosphere();
            BuildTraffic();
            BuildSkyTrain();
            if (mode == GameModeId.SeasonalEvent) BuildEmbers();

            _track.BiomeChanged += OnBiomeChanged;
            ApplyBiome(_track.CurrentBiome < 0 ? 0 : _track.CurrentBiome, true);
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
                var star = PrimitiveMeshes.Create("Star" + i, PrimitiveMeshes.Cube, starMat, decorGo.transform,
                    p, Vector3.one * rng.Range(0.55f, 1.6f));
                star.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            var moonGo = PrimitiveMeshes.Create("Moon", PrimitiveMeshes.Sphere,
                MaterialLibrary.Lit(new Color(0.8f, 0.9f, 1f), 0f, 1f, new Color(0.45f, 0.72f, 1f), 4.2f),
                decorGo.transform, new Vector3(170f, 210f, 240f), Vector3.one * 22f);
            moonGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

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

        private void OnBiomeChanged(int biome) => ApplyBiome(biome, false);

        private void ApplyBiome(int biome, bool immediate)
        {
            if (_track == null || _dome == null) return;
            _biome = Mathf.Abs(biome) % 4;
            var theme = _app.Config.Theme(_biome);
            if (theme == null) return;
            _theme = theme;

            if (!_domeMaterials.TryGetValue(_biome, out Material material))
            {
                var texture = TextureFactory.VerticalGradient("NV_Sky_" + _biome, theme.skyBottom, theme.skyTop, 256);
                material = new Material(MaterialLibrary.UnlitShader)
                {
                    name = "NV_SkyDome_" + _biome,
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
                _domeMaterials[_biome] = material;
            }

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
                if (!_domeFadeMaterials.TryGetValue(_biome, out Material fadeMaterial))
                {
                    fadeMaterial = MaterialLibrary.UnlitTransparentTextured("NV_SkyFade_" + _biome,
                        material.mainTexture, new Color(1f, 1f, 1f, 0f), new Vector2(3f, 1f));
                    if (fadeMaterial.HasProperty("_Cull")) fadeMaterial.SetFloat("_Cull", 1f);
                    fadeMaterial.renderQueue = 3990;
                    _domeFadeMaterials[_biome] = fadeMaterial;
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
            _fromSunColor = _sun != null ? _sun.color : Color.white;
            _fromSunIntensity = _sun != null ? _sun.intensity : 0f;
            _fromRimColor = _rim != null ? _rim.color : Palette.Magenta;
            _fromCyanFill = _cyanFill != null ? _cyanFill.color : Palette.Cyan;
            _fromMagentaFill = _magentaFill != null ? _magentaFill.color : Palette.Magenta;
            _fromWarmFill = _warmFill != null ? _warmFill.color : Palette.Amber;
            _fromBackground = _camera != null ? _camera.backgroundColor : theme.skyBottom;

            _targetFog = theme.fog;
            _targetFogDensity = theme.rain ? 0.0100f : 0.0068f;
            _targetAmbientSky = Color.Lerp(theme.skyTop, theme.accent, 0.16f);
            _targetAmbientEquator = Color.Lerp(theme.buildingA, theme.accent, 0.22f);
            _targetAmbientGround = Color.Lerp(theme.ground, Palette.Void, 0.52f);
            _targetSunColor = Color.Lerp(new Color(0.72f, 0.84f, 1f), theme.accent, 0.22f);
            _targetSunIntensity = DesiredSunIntensity;
            _targetRimColor = Color.Lerp(theme.accent, Palette.Magenta, 0.5f);
            _targetCyanFill = Color.Lerp(theme.accent, Palette.Cyan, 0.4f);
            _targetMagentaFill = Color.Lerp(theme.accent, Palette.Magenta, 0.55f);
            _targetWarmFill = Color.Lerp(Palette.Amber, theme.accent, 0.12f);
            _targetBackground = theme.skyBottom;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientIntensity = DesiredAmbientIntensity;
            RenderSettings.reflectionIntensity = _reflectionQuality >= 2 ? 1f
                : (_reflectionQuality >= 1 ? 0.68f : 0.18f);

            if (!_reflections.TryGetValue(_biome, out Cubemap reflection))
            {
                reflection = TextureFactory.ReflectionCubemap("NV_Reflection_" + _biome,
                    theme.skyTop, theme.fog, theme.accent, 64);
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
            if (_cloudA != null) _cloudA.GetComponent<MeshRenderer>().sharedMaterial = cloudMaterial;
            if (_cloudB != null) _cloudB.GetComponent<MeshRenderer>().sharedMaterial = cloudMaterial;

            if (_trafficRoot != null) _trafficRoot.SetActive(_biome == 0 || _biome == 1);
            if (_trainRoot != null) _trainRoot.SetActive(_biome == 0 || _biome == 1);
            if (_railRoot != null) _railRoot.SetActive(_biome == 0 || _biome == 1);

            bool rain = theme.rain || _app != null && _app.Session != null &&
                        _app.Session.Mode.Id == GameModeId.SeasonalEvent;
            SetRain(rain, immediate);
            _camera?.GetComponent<RunnerCamera>()?.SetBiomeColor(theme.accent);
            ApplyTransition(immediate ? 1f : 0f);
        }

        private float DesiredSunIntensity => _quality >= 2f ? 1.18f : (_quality >= 1f ? 1.0f : 0.68f);
        private float DesiredAmbientIntensity => _quality >= 2f ? 1.05f : (_quality >= 1f ? 0.9f : 0.72f);

        private void SetRain(bool active, bool immediate)
        {
            float rainRate = _effectsQuality >= 2 ? 650f : (_effectsQuality >= 1 ? 400f : 220f);
            float splashRate = _effectsQuality >= 2 ? 220f : (_effectsQuality >= 1 ? 130f : 65f);
            _targetRainRate = active ? (_reduced ? 80f : rainRate) : 0f;
            _targetSplashRate = active ? (_reduced ? 30f : splashRate) : 0f;
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
            _currentSunIntensity = Mathf.Lerp(_fromSunIntensity, _targetSunIntensity, blend);
            if (_sun != null)
            {
                _sun.color = Color.Lerp(_fromSunColor, _targetSunColor, blend);
                _sun.intensity = _currentSunIntensity;
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

            if (_transition < 1f)
            {
                _transition = Mathf.Min(1f, _transition + dt / TransitionSeconds);
                ApplyTransition(_transition);
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
                RenderSettings.ambientSkyColor = Color.Lerp(RenderSettings.ambientSkyColor, Color.white, _flash * 0.45f);
            if (_cyanFill != null) _cyanFill.intensity = 10f + _flash * 22f;
            if (_magentaFill != null) _magentaFill.intensity = 8f + _flash * 18f;
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
            if (_track != null) _track.BiomeChanged -= OnBiomeChanged;
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
