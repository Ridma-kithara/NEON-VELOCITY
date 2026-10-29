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
        private readonly List<Renderer> _pulse = new List<Renderer>();
        private Transform _ring;
        private Transform _cloudA;
        private Transform _cloudB;
        private float _orbit;
        private Transform _halo;
        private readonly List<Transform> _traffic = new List<Transform>();
        private readonly List<float> _trafficPhase = new List<float>();
        private readonly List<float> _trafficSpeed = new List<float>();
        private readonly List<float> _trafficHeight = new List<float>();
        private readonly List<float> _trafficDepth = new List<float>();

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
            RenderSettings.fogDensity = 0.0095f;
            RenderSettings.fogColor = Palette.Hex("#071120");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.Hex("#0D1C36");
            RenderSettings.ambientEquatorColor = Palette.Hex("#142B4B");
            RenderSettings.ambientGroundColor = Palette.Hex("#02050B");
            RenderSettings.ambientIntensity = 1.05f;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = TextureFactory.ReflectionCubemap("NV_MenuReflection",
                Palette.Hex("#0A1B36"), Palette.Hex("#183F5C"), Palette.Cyan, 64);

            var lightGo = new GameObject("Key Light");
            lightGo.transform.SetParent(transform, false);
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.62f, 0.8f, 1f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.78f;
            sun.transform.rotation = Quaternion.Euler(34f, -38f, 0f);

            var rimGo = new GameObject("Rim Light");
            rimGo.transform.SetParent(transform, false);
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(0.5f, 0.65f, 1f);
            rim.intensity = 0.6f;
            rim.transform.rotation = Quaternion.Euler(18f, 150f, 0f);

            var fillCyan = CreatePointLight("Hero Cyan Fill", new Vector3(-3.8f, 3.1f, 2.2f), Palette.Cyan, 11f, 5.2f);
            fillCyan.transform.SetParent(transform, false);
            var fillBlue = CreatePointLight("Hero Blue Fill", new Vector3(3.9f, 1.9f, 0.6f),
                new Color(0.35f, 0.55f, 1f), 9f, 2.6f);
            fillBlue.transform.SetParent(transform, false);
            var heroKey = CreatePointLight("Hero White Key", new Vector3(0.4f, 2.8f, 4.6f),
                new Color(1f, 0.93f, 0.86f), 8f, 4.4f);
            heroKey.transform.SetParent(transform, false);

            BuildSky();
            BuildPlatform();
            BuildSkyline();
            BuildHero(character, trail);
            BuildAtmosphere();
            BuildCamera(reducedMotion);
        }

        private static Light CreatePointLight(string name, Vector3 position, Color color, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            return light;
        }

        private void BuildSky()
        {
            var skyTexture = TextureFactory.VerticalGradient("NV_MenuSky",
                Palette.Hex("#153B5D"), Palette.Hex("#020610"), 256);
            var skyMaterial = MaterialLibrary.UnlitTextured("NV_MenuSkyMat", skyTexture,
                new Color(0.82f, 0.9f, 1f), new Vector2(1f, 1f));
            skyMaterial.SetFloat("_Cull", 1f);
            skyMaterial.renderQueue = 1990;

            var dome = PrimitiveMeshes.Create("Menu Sky Dome", PrimitiveMeshes.Sphere, skyMaterial, transform,
                Vector3.zero, Vector3.one * 900f);
            dome.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // A sparse front-facing star field sits inside the dome but outside the city.
            var starMat = MaterialLibrary.Lit(Palette.White, 0f, 0.1f,
                new Color(0.62f, 0.86f, 1f), 2.6f);
            var rng = new DeterministicRandom(0x51A9);
            for (int i = 0; i < 48; i++)
            {
                float azimuth = rng.Range(-Mathf.PI * 0.62f, Mathf.PI * 0.62f);
                float elevation = rng.Range(0.12f, 1.1f);
                var p = new Vector3(
                    Mathf.Sin(azimuth) * Mathf.Cos(elevation),
                    Mathf.Sin(elevation),
                    -Mathf.Cos(azimuth) * Mathf.Cos(elevation)) * 330f;
                var star = PrimitiveMeshes.Create("Menu Star" + i, PrimitiveMeshes.Cube, starMat, transform,
                    p, Vector3.one * rng.Range(0.45f, 1.2f));
                star.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            var moon = PrimitiveMeshes.Create("Menu Moon", PrimitiveMeshes.Sphere,
                MaterialLibrary.Lit(new Color(0.82f, 0.9f, 1f), 0f, 1f,
                    new Color(0.45f, 0.7f, 1f), 4.4f),
                transform, new Vector3(88f, 122f, -282f), Vector3.one * 17f);
            moon.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var cloudMaterial = MaterialLibrary.UnlitAdditiveTextured("NV_MenuClouds",
                TextureFactory.CloudLayer(7777), new Color(0.48f, 0.67f, 1f, 0.26f),
                new Vector2(2.5f, 1f), true, 1);
            _cloudA = CreateCloudShell("Menu Clouds High", 0.91f, cloudMaterial, dome.transform);
            _cloudB = CreateCloudShell("Menu Clouds Low", 0.8f, cloudMaterial, dome.transform);
        }

        private Transform CreateCloudShell(string name, float scale, Material material, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = PrimitiveMeshes.Sphere;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        private static Material Lit(Color color, float metallic = 0.3f, float smooth = 0.6f, Color? emission = null,
            float intensity = 2f) => MaterialLibrary.Lit(color, metallic, smooth, emission, intensity);

        private void BuildPlatform()
        {
            var asphalt = TextureFactory.Asphalt(5150);
            var normal = TextureFactory.AsphaltNormal(5150);
            var wetTop = MaterialLibrary.LitTextured("NV_MenuWetTop", asphalt,
                Color.Lerp(Palette.Panel, Color.white, 0.08f), 0.2f, 0.92f,
                new Vector2(2f, 4f), normal, new Vector2(2f, 4f), 1.3f);
            var puddle = Lit(Palette.Hex("#02060C"), 0.95f, 1f);
            var mats = new
            {
                dark = Lit(Palette.DeepPanel, 0.7f, 0.35f),
                panel = wetTop,
                cyan = Lit(Palette.Cyan, 0.2f, 0.8f, Palette.Cyan, 3.4f),
                magenta = Lit(Palette.Magenta, 0.2f, 0.8f, Palette.Magenta, 2.8f),
                amber = Lit(Palette.Amber, 0.2f, 0.8f, Palette.Amber, 2.4f),
                white = Lit(Palette.White, 0.2f, 0.8f, Palette.White, 1.4f)
            };

            PrimitiveMeshes.Box(transform, "Platform", new Vector3(0f, -0.42f, -1.5f),
                new Vector3(9f, 0.7f, 16f), mats.dark);
            PrimitiveMeshes.Box(transform, "PlatformTop", new Vector3(0f, 0.02f, -1.5f),
                new Vector3(8.4f, 0.06f, 15.4f), mats.panel);
            PrimitiveMeshes.Box(transform, "PlatformUnderGlow", new Vector3(0f, -0.79f, -1.5f),
                new Vector3(7.8f, 0.04f, 14.8f), mats.cyan);
            for (int i = 0; i < 5; i++)
            {
                float z = -7.4f + i * 3.1f;
                PrimitiveMeshes.Box(transform, "Puddle" + i,
                    new Vector3((i % 2 == 0 ? -1f : 1f) * (1.3f + (i % 3) * 0.55f), 0.055f, z),
                    new Vector3(1.1f + (i % 2) * 0.6f, 0.008f, 1.5f + (i % 3) * 0.4f),
                    puddle);
            }
            for (int i = 0; i < 3; i++)
            {
                PrimitiveMeshes.Box(transform, "PlatformSeam" + i,
                    new Vector3(0f, 0.052f, -7.2f + i * 5.4f),
                    new Vector3(8.35f, 0.005f, 0.035f), mats.dark);
            }
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

            // Showcase halo: a counter-rotating light pool locked to the hero's feet.
            var haloGo = new GameObject("Hero Halo");
            haloGo.transform.SetParent(transform, false);
            haloGo.transform.localPosition = new Vector3(0f, 0.075f, -1.4f);
            _halo = haloGo.transform;
            for (int i = 0; i < 20; i++)
            {
                float angle = i / 20f * Mathf.PI * 2f;
                var seg = PrimitiveMeshes.Box(haloGo.transform, "H" + i,
                    new Vector3(Mathf.Cos(angle) * 1.35f, 0f, Mathf.Sin(angle) * 1.35f),
                    new Vector3(0.34f, 0.02f, 0.03f),
                    i % 3 == 0 ? mats.magenta : mats.cyan);
                seg.transform.localRotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);
                seg.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
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
            var facadeA = MaterialLibrary.UnlitTextured("NV_MenuFacadeA",
                TextureFactory.WindowFacade(424242), new Color(0.85f, 0.94f, 1f), new Vector2(2f, 3.6f));
            var facadeB = MaterialLibrary.UnlitTextured("NV_MenuFacadeB",
                TextureFactory.WindowFacade(909090), new Color(1f, 0.78f, 0.9f), new Vector2(2.4f, 3.2f));
            var facadeC = MaterialLibrary.UnlitTextured("NV_MenuFacadeC",
                TextureFactory.WindowFacade(131313, 0), new Color(0.82f, 1f, 0.95f), new Vector2(1.7f, 3.1f));

            // Mid layer: window-lit towers framing the hero on both flanks.
            for (int i = 0; i < 7; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float x = side * rng.Range(11f, 40f);
                float z = -9f - rng.Range(0f, 66f);
                float w = rng.Range(3.5f, 9f);
                float d = rng.Range(4f, 11f);
                float h = rng.Range(9f, 58f);
                Material skin = h > 34f ? facadeA
                    : (rng.Chance(0.55f) ? facadeB : (rng.Chance(0.45f) ? facadeC : mid));
                var body = PrimitiveMeshes.Box(transform, "Tower" + i, new Vector3(x, h * 0.5f, z),
                    new Vector3(w, h, d), skin);
                body.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                PrimitiveMeshes.Box(transform, "TowerCap" + i, new Vector3(x, h + 0.2f, z),
                    new Vector3(w + 0.3f, 0.4f, d + 0.3f), dark);
                if (rng.Chance(0.62f))
                {
                    var stripe = PrimitiveMeshes.Box(transform, "TowerStripe" + i,
                        new Vector3(x - side * (w * 0.5f + 0.04f), h * 0.62f, z),
                        new Vector3(0.08f, h * rng.Range(0.4f, 0.8f), 0.15f), i % 2 == 0 ? cyan : magenta);
                    stripe.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
                if (h > 30f && rng.Chance(0.55f))
                {
                    for (int m = 0; m < 3; m++)
                    {
                        var mast = PrimitiveMeshes.Box(transform, "TowerMast" + i + "_" + m,
                            new Vector3(x - w * 0.28f + m * w * 0.28f, h + rng.Range(1.6f, 3.6f), z),
                            new Vector3(0.07f, rng.Range(2.2f, 4.8f), 0.07f), mid);
                        mast.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    }
                }
                if (rng.Chance(0.4f))
                {
                    var beacon = PrimitiveMeshes.SphereObject(transform, "Beacon" + i, new Vector3(x, h + 0.75f, z),
                        0.16f, rng.Chance(0.5f) ? amber : cyan);
                    beacon.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            // Far layer: silhouette masses behind the atmospheric haze.
            for (int i = 0; i < 3; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float x = side * rng.Range(46f, 96f);
                float z = -52f - rng.Range(0f, 40f);
                float w = rng.Range(6f, 14f);
                float h = rng.Range(26f, 96f);
                var body = PrimitiveMeshes.Box(transform, "FarTower" + i, new Vector3(x, h * 0.5f, z),
                    new Vector3(w, h, rng.Range(6f, 16f)), mid);
                body.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var haze = PrimitiveMeshes.Create("FarHaze" + i, PrimitiveMeshes.Quad,
                    MaterialLibrary.Additive(i % 2 == 0 ? Palette.Cyan : Palette.Magenta), transform,
                    new Vector3(x * 0.82f, h * 0.4f, z + 9f),
                    new Vector3(w * 1.7f, h * 0.55f, 1f));
                var hazeR = haze.GetComponent<MeshRenderer>();
                hazeR.shadowCastingMode = ShadowCastingMode.Off;
                hazeR.receiveShadows = false;
            }

            var signMaterial = MaterialLibrary.UnlitTextured("NV_MenuSignA",
                TextureFactory.BillboardArt(6161, Palette.Cyan, 1),
                new Color(1.3f, 1.3f, 1.3f, 1f), Vector2.one);
            var signMaterialB = MaterialLibrary.UnlitTextured("NV_MenuSignB",
                TextureFactory.BillboardArt(8181, Palette.Magenta, 2),
                new Color(1.3f, 1.3f, 1.3f, 1f), Vector2.one);
            var signMaterialC = MaterialLibrary.UnlitTextured("NV_MenuSignC",
                TextureFactory.BillboardArt(4747, Palette.Amber, 0),
                new Color(1.25f, 1.25f, 1.25f, 1f), Vector2.one);
            for (int i = 0; i < 8; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float x = side * rng.Range(9.5f, 14.5f);
                float z = -12f - i * 7.5f - rng.Range(0f, 3f);
                float y = rng.Range(3.4f, 8.5f);
                Material signSkin = i % 3 == 2 ? signMaterialC : (i % 2 == 0 ? signMaterial : signMaterialB);
                var sign = PrimitiveMeshes.Box(transform, "BladeSign" + i,
                    new Vector3(x, y, z), new Vector3(1.15f, 2.8f, 0.08f), signSkin);
                sign.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                PrimitiveMeshes.Box(transform, "SignArm" + i, new Vector3(x + side * 0.45f, y + 1.55f, z),
                    new Vector3(0.9f, 0.06f, 0.08f), mid);
            }

            // Skybridges: lit glass decks crossing the street canyon between towers.
            for (int i = 0; i < 2; i++)
            {
                float y = 15.5f + i * 10.5f;
                float z = -21f - i * 16f;
                float span = rng.Range(23f, 32f);
                var deck = PrimitiveMeshes.Box(transform, "SkyBridge" + i, new Vector3(0f, y, z),
                    new Vector3(span, 0.55f, 2.7f), dark);
                deck.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                PrimitiveMeshes.Box(transform, "BridgeDeck" + i, new Vector3(0f, y + 0.36f, z),
                    new Vector3(span, 0.1f, 2.2f), mid);
                var bridgeGlow = PrimitiveMeshes.Box(transform, "BridgeGlow" + i, new Vector3(0f, y - 0.41f, z),
                    new Vector3(span * 0.92f, 0.07f, 0.18f), i % 2 == 0 ? cyan : magenta);
                bridgeGlow.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                for (int rail = 0; rail < 6; rail++)
                {
                    float railX = -span * 0.45f + rail * span * 0.18f;
                    PrimitiveMeshes.Box(transform, "BridgeRail" + i + "_" + rail, new Vector3(railX, y + 0.9f, z - 1.2f),
                        new Vector3(0.06f, 0.7f, 0.06f), mid).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    PrimitiveMeshes.Box(transform, "BridgeRailBack" + i + "_" + rail, new Vector3(railX, y + 0.9f, z + 1.2f),
                        new Vector3(0.06f, 0.7f, 0.06f), mid).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
                PrimitiveMeshes.Box(transform, "BridgeBeamFront" + i, new Vector3(0f, y + 0.85f, z - 1.2f),
                    new Vector3(span, 0.07f, 0.07f), mid);
                PrimitiveMeshes.Box(transform, "BridgeBeamBack" + i, new Vector3(0f, y + 0.85f, z + 1.2f),
                    new Vector3(span, 0.07f, 0.07f), mid);
            }

            // Elevated airway with lantern pylons running behind the runner.
            {
                float y = 12.8f;
                float z = -34f;
                var deck = PrimitiveMeshes.Box(transform, "AirwayDeck", new Vector3(0f, y, z),
                    new Vector3(84f, 0.6f, 3.4f), dark);
                deck.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                PrimitiveMeshes.Box(transform, "AirwayGlowFront", new Vector3(0f, y - 0.44f, z - 1.5f),
                    new Vector3(82f, 0.05f, 0.14f), cyan).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                for (int p = 0; p < 8; p++)
                {
                    float px = -34f + p * 9.6f;
                    var pylon = PrimitiveMeshes.Box(transform, "AirwayPylon" + p, new Vector3(px, y * 0.5f, z + 1.6f),
                        new Vector3(0.28f, y, 0.28f), mid);
                    pylon.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    var lantern = PrimitiveMeshes.Box(transform, "AirwayLantern" + p, new Vector3(px, y + 0.9f, z),
                        new Vector3(0.4f, 0.12f, 0.6f), p % 2 == 0 ? amber : magenta);
                    lantern.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            BuildAirTraffic(rng, mid, cyan, magenta);

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

            // Distant horizon silhouette and a dim reflective ribbon beyond the platform.
            PrimitiveMeshes.Box(transform, "Horizon", new Vector3(0f, -2f, -86f),
                new Vector3(190f, 4f, 2f), mid).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            PrimitiveMeshes.Box(transform, "HorizonGlow", new Vector3(0f, 0.15f, -85f),
                new Vector3(190f, 0.05f, 0.12f), cyan).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        private void BuildAirTraffic(DeterministicRandom rng, Material hull, Material cyan, Material magenta)
        {
            for (int i = 0; i < 6; i++)
            {
                var shuttle = new GameObject("Shuttle" + i);
                shuttle.transform.SetParent(transform, false);
                _trafficHeight.Add(rng.Range(13f, 34f));
                _trafficDepth.Add(rng.Range(-62f, -16f));
                _trafficPhase.Add(rng.Range(0f, Mathf.PI * 2f));
                _trafficSpeed.Add(rng.Range(0.05f, 0.11f) * (rng.Chance(0.5f) ? 1f : -1f));
                var wing = PrimitiveMeshes.Box(shuttle.transform, "Wing", Vector3.zero,
                    new Vector3(2.5f, 0.18f, 0.7f), hull);
                wing.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var spine = PrimitiveMeshes.Box(shuttle.transform, "Spine", Vector3.zero,
                    new Vector3(0.6f, 0.24f, 1.7f), hull);
                spine.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                Material engineMaterial = i % 2 == 0 ? cyan : magenta;
                var portGlow = PrimitiveMeshes.Box(shuttle.transform, "PortGlow", new Vector3(-1.05f, -0.02f, 0f),
                    new Vector3(0.55f, 0.05f, 0.12f), engineMaterial);
                portGlow.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var starGlow = PrimitiveMeshes.Box(shuttle.transform, "StarGlow", new Vector3(1.05f, -0.02f, 0f),
                    new Vector3(0.55f, 0.05f, 0.12f), engineMaterial);
                starGlow.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                _traffic.Add(shuttle.transform);
            }
        }

        private void UpdateTraffic(float unscaledTime)
        {
            for (int i = 0; i < _traffic.Count; i++)
            {
                var shuttle = _traffic[i];
                if (shuttle == null) continue;
                float wave = unscaledTime * _trafficSpeed[i] + _trafficPhase[i];
                float x = Mathf.Sin(wave) * 62f;
                float y = _trafficHeight[i] + Mathf.Sin(unscaledTime * 0.7f + i) * 0.7f;
                shuttle.position = new Vector3(x, y, _trafficDepth[i]);
                float heading = Mathf.Cos(wave) * Mathf.Sign(_trafficSpeed[i]);
                shuttle.rotation = Quaternion.Euler(0f, heading > 0f ? -90f : 90f, 0f);
            }
        }

        private void BuildHero(CharacterDefinition character, TrailDefinition trail)
        {
            var hero = new GameObject("Menu Runner");
            hero.transform.SetParent(transform, false);
            hero.transform.localPosition = new Vector3(0f, 0f, -1.4f);
            hero.transform.localRotation = Quaternion.identity;
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

            var rainGo = new GameObject("Menu Rain");
            rainGo.transform.SetParent(transform, false);
            rainGo.transform.localPosition = new Vector3(0f, 20f, -28f);
            var rain = rainGo.AddComponent<ParticleSystem>();
            var rainMain = rain.main;
            rainMain.loop = true;
            rainMain.startLifetime = 1.25f;
            rainMain.startSpeed = new ParticleSystem.MinMaxCurve(23f, 30f);
            rainMain.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.032f);
            rainMain.startColor = new Color(0.55f, 0.8f, 1f, 0.42f);
            rainMain.gravityModifier = 1.1f;
            rainMain.maxParticles = 1000;
            var rainEmission = rain.emission;
            rainEmission.rateOverTime = 520f;
            var rainShape = rain.shape;
            rainShape.shapeType = ParticleSystemShapeType.Box;
            rainShape.scale = new Vector3(90f, 1f, 80f);
            var rainRenderer = rainGo.GetComponent<ParticleSystemRenderer>();
            rainRenderer.material = MaterialLibrary.Additive(new Color(0.55f, 0.8f, 1f));
            rainRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            rainRenderer.lengthScale = 9f;
            rainRenderer.velocityScale = 0.08f;
            rainRenderer.shadowCastingMode = ShadowCastingMode.Off;
            rainRenderer.receiveShadows = false;
            rain.Play();

            var splashGo = new GameObject("Menu Splashes");
            splashGo.transform.SetParent(transform, false);
            splashGo.transform.localPosition = new Vector3(0f, 0.12f, -1.5f);
            var splash = splashGo.AddComponent<ParticleSystem>();
            var splashMain = splash.main;
            splashMain.loop = true;
            splashMain.startLifetime = 0.3f;
            splashMain.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.8f);
            splashMain.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.05f);
            splashMain.startColor = new Color(0.65f, 0.9f, 1f, 0.6f);
            splashMain.maxParticles = 180;
            var splashEmission = splash.emission;
            splashEmission.rateOverTime = 90f;
            var splashShape = splash.shape;
            splashShape.shapeType = ParticleSystemShapeType.Box;
            splashShape.scale = new Vector3(8.2f, 0.05f, 15f);
            var splashRenderer = splashGo.GetComponent<ParticleSystemRenderer>();
            splashRenderer.material = MaterialLibrary.ParticleTextured("NV_MenuSplash",
                TextureFactory.RadialGlow(48), new Color(0.65f, 0.9f, 1f, 0.65f), Vector2.one);
            splashRenderer.shadowCastingMode = ShadowCastingMode.Off;
            splashRenderer.receiveShadows = false;
            splash.Play();
        }

        private void BuildCamera(bool reducedMotion)
        {
            var cameraGo = new GameObject("Menu Camera");
            cameraGo.transform.SetParent(transform, false);
            cameraGo.tag = "MainCamera";
            _camera = cameraGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Palette.Hex("#040916");
            _camera.fieldOfView = 54f;
            _camera.nearClipPlane = 0.08f;
            _camera.farClipPlane = 620f;
            _camera.allowHDR = true;
            _camera.allowMSAA = true;
            _camera.transform.position = new Vector3(3.0f, 2.2f, 6.4f);
            _camera.transform.LookAt(new Vector3(-2.1f, 1.32f, -4.8f));
            cameraGo.AddComponent<AudioListener>();

            var data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = !VisualCaptureRunner.PostProcessingDisabled;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "NV Menu Post";
            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.72f);
            bloom.threshold.Override(0.95f);
            bloom.scatter.Override(0.68f);
            bloom.highQualityFiltering.Override(true);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.38f);
            vignette.smoothness.Override(0.58f);
            vignette.color.Override(Palette.Hex("#040711"));
            var ca = profile.Add<ChromaticAberration>(true);
            ca.intensity.Override(reducedMotion ? 0.02f : 0.065f);
            var grade = profile.Add<ColorAdjustments>(true);
            grade.postExposure.Override(0.22f);
            grade.contrast.Override(9f);
            grade.saturation.Override(7f);
            grade.colorFilter.Override(new Color(0.94f, 0.98f, 1f));
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(reducedMotion ? 0f : 0.14f);
            grain.response.Override(0.78f);
            if (!reducedMotion)
            {
                var dof = profile.Add<DepthOfField>(true);
                dof.mode.Override(DepthOfFieldMode.Bokeh);
                dof.focusDistance.Override(7.4f);
                dof.aperture.Override(5.6f);
                dof.focalLength.Override(58f);
                dof.bladeCount.Override(5);
                dof.bladeCurvature.Override(1f);
            }
            var volumeGo = new GameObject("Menu Volume");
            volumeGo.transform.SetParent(transform, false);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 2f;
            volume.profile = profile;
            if (reducedMotion) _camera.fieldOfView = 56f;
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
                _orbit += Time.unscaledDeltaTime * 0.075f;
                float x = Mathf.Sin(_orbit) * 0.82f;
                var target = new Vector3(x + 0.35f, 2.2f + Mathf.Sin(_time * 0.21f) * 0.1f, 6.4f);
                _camera.transform.position = Vector3.Lerp(_camera.transform.position, target, Time.unscaledDeltaTime * 1.1f);
                _camera.transform.LookAt(new Vector3(-2.1f, 1.32f, -4.8f));
                _camera.transform.rotation *= Quaternion.Euler(0f, 0f, Mathf.Sin(_time * 0.17f) * 0.45f);
            }
            UpdateTraffic(_time);
            if (_cloudA != null) _cloudA.Rotate(0f, Time.unscaledDeltaTime * 0.5f, 0f, Space.Self);
            if (_cloudB != null) _cloudB.Rotate(0f, Time.unscaledDeltaTime * -0.3f, 0f, Space.Self);
            if (_runner != null)
                _runner.Tick(Time.unscaledDeltaTime, 0.35f, Mathf.Sin(_time * 0.8f) * 0.4f, RunnerPose.Run);
            if (_ring != null) _ring.Rotate(0f, 0f, Time.unscaledDeltaTime * -3f);
            if (_halo != null) _halo.Rotate(0f, Time.unscaledDeltaTime * 11f, 0f, Space.Self);
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
