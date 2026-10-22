using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Player;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonVelocity.World
{
    /// <summary>
    /// Animated title backdrop. A selected runner jogs on a floating neon platform while the
    /// camera drifts through an original skyline. Built only from primitives and generated
    /// materials, so it can never fail because a thumbnail or prefab is missing.
    /// </summary>
    public sealed class MenuStage : MonoBehaviour
    {
        private Camera _camera;
        private Transform _hero;
        private RunnerVisuals _runner;
        private float _time;
        private readonly List<Transform> _floaters = new List<Transform>();
        private readonly List<Renderer> _pulse = new List<Renderer>();
        private Transform _ring;
        private float _orbit;

        public Camera Camera => _camera;

        public static MenuStage Create(Transform parent, CharacterDefinition character, TrailDefinition trail,
            bool reducedMotion)
        {
            var root = new GameObject("Title Backdrop");
            root.transform.SetParent(parent, false);
            var stage = root.AddComponent<MenuStage>();
            stage.Build(character, trail, reducedMotion);
            return stage;
        }

        private void Build(CharacterDefinition character, TrailDefinition trail, bool reducedMotion)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = Palette.Hex("#081326");
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Palette.Hex("#16243B");

            var lightGo = new GameObject("Key Light");
            lightGo.transform.SetParent(transform, false);
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.55f, 0.75f, 1f);
            sun.intensity = 0.9f;
            sun.transform.rotation = Quaternion.Euler(32f, -35f, 0f);

            var rimGo = new GameObject("Rim Light");
            rimGo.transform.SetParent(transform, false);
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = Palette.Magenta;
            rim.intensity = 0.45f;
            rim.transform.rotation = Quaternion.Euler(18f, 150f, 0f);

            BuildPlatform();
            BuildSkyline();
            BuildHero(character, trail);
            BuildAtmosphere();
            BuildCamera(reducedMotion);
        }

        private static Material Lit(Color color, float metallic = 0.3f, float smooth = 0.6f, Color? emission = null,
            float intensity = 2f) => MaterialLibrary.Lit(color, metallic, smooth, emission, intensity);

        private void BuildPlatform()
        {
            var mats = new
            {
                dark = Lit(Palette.DeepPanel, 0.7f, 0.35f),
                panel = Lit(Palette.Panel, 0.4f, 0.55f),
                cyan = Lit(Palette.Cyan, 0.2f, 0.8f, Palette.Cyan, 3.4f),
                magenta = Lit(Palette.Magenta, 0.2f, 0.8f, Palette.Magenta, 2.8f),
                amber = Lit(Palette.Amber, 0.2f, 0.8f, Palette.Amber, 2.4f),
                white = Lit(Palette.White, 0.2f, 0.8f, Palette.White, 1.4f)
            };

            PrimitiveMeshes.Box(transform, "Platform", new Vector3(0f, -0.35f, -1.5f),
                new Vector3(9f, 0.7f, 16f), mats.dark);
            PrimitiveMeshes.Box(transform, "PlatformTop", new Vector3(0f, 0.02f, -1.5f),
                new Vector3(8.4f, 0.06f, 15.4f), mats.panel);
            for (int side = -1; side <= 1; side += 2)
            {
                PrimitiveMeshes.Box(transform, "EdgeGlow" + side, new Vector3(side * 4.2f, 0.09f, -1.5f),
                    new Vector3(0.06f, 0.03f, 15.2f), mats.cyan);
                for (int i = 0; i < 5; i++)
                {
                    PrimitiveMeshes.Box(transform, "Pylon" + side + "_" + i,
                        new Vector3(side * 5.1f, 1.4f, -7.8f + i * 3.2f),
                        new Vector3(0.12f, 2.8f, 0.12f), mats.white);
                    var cap = PrimitiveMeshes.Box(transform, "PylonCap" + side + "_" + i,
                        new Vector3(side * 5.1f, 2.85f, -7.8f + i * 3.2f),
                        new Vector3(0.35f, 0.08f, 0.35f), i % 2 == 0 ? mats.cyan : mats.magenta);
                    _pulse.Add(cap.GetComponent<Renderer>());
                }
            }

            // Holographic ring behind the runner.
            var ringGo = new GameObject("Hero Ring");
            ringGo.transform.SetParent(transform, false);
            ringGo.transform.localPosition = new Vector3(0f, 2.25f, 0.4f);
            _ring = ringGo.transform;
            for (int i = 0; i < 32; i++)
            {
                float angle = i / 32f * Mathf.PI * 2f;
                var seg = PrimitiveMeshes.Box(ringGo.transform, "R" + i,
                    new Vector3(Mathf.Cos(angle) * 2.3f, Mathf.Sin(angle) * 2.3f, 0f),
                    new Vector3(0.24f, 0.035f, 0.035f),
                    i % 3 == 0 ? mats.magenta : mats.cyan);
                seg.transform.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
                seg.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            // Floating title glyphs / distinct geometric markers.
            for (int i = 0; i < 7; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * (4.7f + (i % 3) * 1.3f);
                float y = 1.2f + i * 0.7f;
                float z = -2f - i * 2.1f;
                var marker = PrimitiveMeshes.Box(transform, "Marker" + i, new Vector3(x, y, z),
                    new Vector3(0.42f, 0.42f, 0.06f), i % 2 == 0 ? mats.cyan : mats.amber,
                    Quaternion.Euler(0f, 0f, 45f));
                marker.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                _floaters.Add(marker.transform);
            }
        }

        private void BuildSkyline()
        {
            var rng = new DeterministicRandom(0x51A7);
            var dark = Lit(Palette.Hex("#0A1425"), 0.2f, 0.3f);
            var mid = Lit(Palette.Hex("#14213A"), 0.4f, 0.4f);
            var cyan = Lit(Palette.Cyan, 0.1f, 0.8f, Palette.Cyan, 2.4f);
            var magenta = Lit(Palette.Magenta, 0.1f, 0.8f, Palette.Magenta, 2.2f);
            var amber = Lit(Palette.Amber, 0.1f, 0.8f, Palette.Amber, 1.9f);
            var facade = MaterialLibrary.UnlitTextured("NV_MenuFacade",
                TextureFactory.WindowFacade(424242), new Color(0.52f, 0.66f, 1f), new Vector2(2f, 5f));
            var facadeB = MaterialLibrary.UnlitTextured("NV_MenuFacadeB",
                TextureFactory.WindowFacade(909090), new Color(1f, 0.5f, 0.8f), new Vector2(3f, 4f));

            for (int i = 0; i < 26; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float x = side * rng.Range(11f, 34f);
                float z = -9f - rng.Range(0f, 62f);
                float w = rng.Range(3.5f, 9f);
                float d = rng.Range(4f, 11f);
                float h = rng.Range(8f, 42f);
                var body = PrimitiveMeshes.Box(transform, "Tower" + i, new Vector3(x, h * 0.5f, z),
                    new Vector3(w, h, d), i % 3 == 0 ? facade : (i % 3 == 1 ? facadeB : mid));
                body.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                PrimitiveMeshes.Box(transform, "TowerCap" + i, new Vector3(x, h + 0.2f, z),
                    new Vector3(w + 0.3f, 0.4f, d + 0.3f), dark);
                var stripe = PrimitiveMeshes.Box(transform, "TowerStripe" + i,
                    new Vector3(x - side * (w * 0.5f + 0.04f), h * 0.6f, z),
                    new Vector3(0.08f, h * 0.6f, 0.15f), i % 2 == 0 ? cyan : magenta);
                stripe.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                if (rng.Chance(0.35f))
                {
                    PrimitiveMeshes.Box(transform, "Antenna" + i, new Vector3(x, h + 2.2f, z),
                        new Vector3(0.08f, 4f, 0.08f), amber).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            // Ground haze panels under the towers.
            for (int i = 0; i < 9; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                var haze = PrimitiveMeshes.Create("Haze" + i, PrimitiveMeshes.Quad,
                    MaterialLibrary.Additive(i % 3 == 0 ? Palette.Magenta : Palette.Cyan), transform,
                    new Vector3(side * rng.Range(8f, 22f), 1.8f, -12f - i * 5.2f),
                    new Vector3(12f, 3.6f, 1f), Quaternion.Euler(0f, side * 58f, 0f));
                var renderer = haze.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            // Distant horizon bar gives the infinite city a readable silhouette.
            PrimitiveMeshes.Box(transform, "Horizon", new Vector3(0f, -0.4f, -86f),
                new Vector3(190f, 0.8f, 2f), cyan).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        private void BuildHero(CharacterDefinition character, TrailDefinition trail)
        {
            var hero = new GameObject("Menu Runner");
            hero.transform.SetParent(transform, false);
            hero.transform.localPosition = new Vector3(0f, 0f, -1.4f);
            hero.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            hero.transform.localScale = Vector3.one * 1.35f;
            _hero = hero.transform;
            _runner = hero.AddComponent<RunnerVisuals>();
            _runner.Build(character, trail, false, character != null ? character.callsign : "");
            _runner.SetPose(RunnerPose.Run);
            _runner.Tick(0f, 0.4f, 0f, RunnerPose.Run);
        }

        private void BuildAtmosphere()
        {
            var rng = new DeterministicRandom(0x9001);
            var starMaterial = MaterialLibrary.Additive(new Color(0.6f, 0.85f, 1f, 0.7f));
            var stars = new GameObject("Suspended Dust");
            stars.transform.SetParent(transform, false);
            var ps = stars.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 0.8f, 1f, 0.5f),
                new Color(1f, 0.4f, 0.8f, 0.5f));
            main.maxParticles = 220;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 18f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(75f, 26f, 75f);
            shape.position = new Vector3(0f, 12f, -35f);
            var renderer = stars.GetComponent<ParticleSystemRenderer>();
            renderer.material = starMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
        }

        private void BuildCamera(bool reducedMotion)
        {
            var cameraGo = new GameObject("Menu Camera");
            cameraGo.transform.SetParent(transform, false);
            cameraGo.tag = "MainCamera";
            _camera = cameraGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Palette.Hex("#040916");
            _camera.fieldOfView = 58f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 220f;
            _camera.allowHDR = true;
            _camera.allowMSAA = true;
            _camera.transform.position = new Vector3(3.1f, 2.7f, 6.6f);
            _camera.transform.LookAt(new Vector3(0f, 1.65f, -4.2f));
            cameraGo.AddComponent<AudioListener>();

            var data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "NV Menu Post";
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.75f);
            bloom.threshold.Override(0.85f);
            bloom.scatter.Override(0.65f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.55f);
            vignette.color.Override(Palette.Hex("#040711"));
            var ca = profile.Add<ChromaticAberration>(true);
            ca.intensity.Override(0.12f);
            var grade = profile.Add<ColorAdjustments>(true);
            grade.postExposure.Override(0.15f);
            grade.contrast.Override(12f);
            grade.saturation.Override(18f);
            var volumeGo = new GameObject("Menu Volume");
            volumeGo.transform.SetParent(transform, false);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 2f;
            volume.profile = profile;
            if (reducedMotion) _camera.fieldOfView = 54f;
        }

        public void SetCharacter(CharacterDefinition character, TrailDefinition trail)
        {
            if (_runner == null) return;
            _runner.Build(character, trail, false, "");
            _runner.SetPose(RunnerPose.Run);
        }

        private void Update()
        {
            _time += Time.unscaledDeltaTime;
            if (_camera != null)
            {
                _orbit += Time.unscaledDeltaTime * 0.09f;
                float x = Mathf.Sin(_orbit) * 1.1f;
                var target = new Vector3(x, 2.7f + Mathf.Sin(_time * 0.24f) * 0.12f, 6.6f);
                _camera.transform.position = Vector3.Lerp(_camera.transform.position, target, Time.unscaledDeltaTime * 1.1f);
                _camera.transform.LookAt(new Vector3(0f, 1.65f, -4.2f));
            }
            if (_runner != null)
                _runner.Tick(Time.unscaledDeltaTime, 0.35f, Mathf.Sin(_time * 0.8f) * 0.4f, RunnerPose.Run);
            if (_ring != null) _ring.Rotate(0f, 0f, Time.unscaledDeltaTime * -3f);
            for (int i = 0; i < _floaters.Count; i++)
            {
                var f = _floaters[i];
                if (f == null) continue;
                var p = f.localPosition;
                p.y += Mathf.Sin(_time * 1.2f + i) * Time.unscaledDeltaTime * 0.12f;
                f.localPosition = p;
                f.Rotate(0f, 0f, Time.unscaledDeltaTime * (18f + i * 2f));
            }
            for (int i = 0; i < _pulse.Count; i++)
            {
                var renderer = _pulse[i];
                if (renderer == null) continue;
                float pulse = 0.8f + Mathf.Sin(_time * 2.2f + i * 0.8f) * 0.2f;
                renderer.transform.localScale = new Vector3(0.35f * pulse, 0.08f * pulse, 0.35f * pulse);
            }
            if (_hero != null)
                _hero.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(_time * 1.4f)) * 0.015f, -1.4f);
        }

        public void Dispose()
        {
            if (_camera != null)
            {
                var listener = _camera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
            Destroy(gameObject);
        }
    }
}
