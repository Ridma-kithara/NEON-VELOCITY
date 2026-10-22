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
        private Light _sun;
        private Light _rim;
        private Transform _dome;
        private Material _domeMaterial;
        private readonly Dictionary<int, Material> _domeMaterials = new Dictionary<int, Material>();
        private ParticleSystem _rain;
        private ParticleSystemRenderer _rainRenderer;
        private ParticleSystem _snow;
        private int _biome = -1;
        private float _time;
        private float _flash;
        private bool _reduced;
        private float _quality = 2f;

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

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.011f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Palette.Hex("#1A2942");

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.intensity = graphicsPreset >= 1 ? 1.05f : 0.55f;
            _sun.color = new Color(0.62f, 0.78f, 1f);
            _sun.shadows = graphicsPreset >= 2 ? LightShadows.Soft : LightShadows.None;
            _sun.shadowStrength = 0.55f;
            _sun.shadowBias = 0.04f;
            _sun.transform.rotation = Quaternion.Euler(36f, -32f, 0f);

            var rimGo = new GameObject("Neon Rim");
            rimGo.transform.SetParent(transform, false);
            _rim = rimGo.AddComponent<Light>();
            _rim.type = LightType.Directional;
            _rim.intensity = graphicsPreset >= 1 ? 0.5f : 0.25f;
            _rim.color = Palette.Magenta;
            _rim.transform.rotation = Quaternion.Euler(16f, 148f, 0f);

            BuildDome();
            BuildRain();
            if (mode == GameModeId.SeasonalEvent) BuildEmbers();

            _track.BiomeChanged += OnBiomeChanged;
            ApplyBiome(_track.CurrentBiome < 0 ? 0 : _track.CurrentBiome, true);
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
            domeGo.transform.localScale = Vector3.one * 380f;
            domeGo.transform.position = new Vector3(0f, 0f, 0f);

            // Stars are fixed to the dome and therefore stay above the city skyline.
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
                var star = PrimitiveMeshes.Create("Star" + i, PrimitiveMeshes.Cube, starMat, domeGo.transform,
                    p, Vector3.one * rng.Range(0.35f, 1.1f));
                star.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            var moonGo = PrimitiveMeshes.Create("Moon", PrimitiveMeshes.Sphere,
                MaterialLibrary.Lit(new Color(0.8f, 0.9f, 1f), 0f, 1f, new Color(0.45f, 0.72f, 1f), 4.2f),
                domeGo.transform, new Vector3(170f, 210f, 240f), Vector3.one * 22f);
            moonGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
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
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.022f);
            main.startColor = new Color(0.55f, 0.8f, 1f, 0.42f);
            main.gravityModifier = 1.4f;
            main.maxParticles = _reduced ? 250 : Mathf.RoundToInt(900f * Mathf.Clamp(_quality, 0.4f, 1.5f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = _rain.emission;
            emission.rateOverTime = 0f;
            var shape = _rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(34f, 1f, 34f);
            shape.position = new Vector3(0f, 0f, 12f);
            var velocity = _rain.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.z = new ParticleSystem.MinMaxCurve(-7f, -4f);
            _rainRenderer = go.GetComponent<ParticleSystemRenderer>();
            _rainRenderer.material = MaterialLibrary.Additive(new Color(0.55f, 0.8f, 1f));
            _rainRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            _rainRenderer.lengthScale = 7f;
            _rainRenderer.velocityScale = 0.09f;
            _rainRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _rainRenderer.receiveShadows = false;
            _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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
            _domeMaterial = material;
            _dome.GetComponent<MeshRenderer>().sharedMaterial = _domeMaterial;

            RenderSettings.fogColor = theme.fog;
            RenderSettings.fogDensity = theme.rain ? 0.013f : 0.009f;
            RenderSettings.ambientLight = Color.Lerp(theme.buildingA, theme.accent, 0.12f);
            if (_camera != null)
            {
                _camera.backgroundColor = theme.skyBottom;
            }
            if (_sun != null)
            {
                _sun.color = Color.Lerp(Color.white, theme.accent, 0.34f);
                _sun.intensity = _quality >= 1 ? 1.0f : 0.55f;
            }
            if (_rim != null) _rim.color = Color.Lerp(theme.accent, Palette.Magenta, 0.5f);

            bool rain = theme.rain || _app != null && _app.Session != null &&
                        _app.Session.Mode.Id == GameModeId.SeasonalEvent;
            SetRain(rain);
            if (_rain != null)
            {
                _rainRenderer.material = MaterialLibrary.Additive(Color.Lerp(theme.accent, Color.white, 0.4f));
            }
            _camera?.GetComponent<RunnerCamera>()?.SetBiomeColor(theme.accent);
        }

        private void SetRain(bool active)
        {
            if (_rain == null) return;
            if (active)
            {
                var emission = _rain.emission;
                emission.rateOverTime = _reduced ? 90f : 280f;
                if (!_rain.isPlaying) _rain.Play();
            }
            else
            {
                var emission = _rain.emission;
                emission.rateOverTime = 0f;
            }
        }

        private void Update()
        {
            _time += Time.deltaTime;
            if (_runner == null) return;
            if (_dome != null)
            {
                var p = _runner.transform.position;
                _dome.position = new Vector3(p.x, 0f, p.z);
            }
            if (_biome == 0 && _flash > 0f)
            {
                _flash = Mathf.Max(0f, _flash - Time.deltaTime * 6f);
            }
            if (_biome == 0 && !_reduced && Time.time > _nextFlash)
            {
                _flash = 1f;
                _nextFlash = Time.time + UnityEngine.Random.Range(14f, 28f);
            }
            if (_sun != null && _flash > 0.01f)
            {
                var theme = _app.Config.Theme(_biome);
                _sun.intensity = (_quality >= 1 ? 1.0f : 0.55f) + _flash * 2.2f;
                if (theme != null) RenderSettings.ambientLight = Color.Lerp(theme.buildingA, Color.white, _flash * 0.35f);
            }
            else if (_sun != null)
            {
                _sun.intensity = _quality >= 1 ? 1.0f : 0.55f;
            }
        }

        private float _nextFlash = 15f;

        private void OnDestroy()
        {
            if (_track != null) _track.BiomeChanged -= OnBiomeChanged;
            foreach (var kv in _domeMaterials)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            _domeMaterials.Clear();
        }
    }
}
