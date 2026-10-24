using System;
using System.Collections.Generic;
using NeonVelocity.Collectibles;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;

namespace NeonVelocity.World
{
    /// <summary>
    /// Streams deterministic 42-metre track segments, pools hazards and pickups, and rebases
    /// world coordinates every 2,000 metres. No segment or pickup grows without bound.
    /// </summary>
    public sealed class TrackManager : MonoBehaviour
    {
        public const float SegmentLength = TrackDirector.SegmentLength;
        public const int RowsPerSegment = TrackDirector.RowsPerSegment;
        public const float RowSpacing = TrackDirector.RowSpacing;

        private sealed class Segment
        {
            public int Index;
            public GameObject Root;
            public MeshFilter DecorFilter;
            public MeshRenderer DecorRenderer;
            public Mesh DecorMesh;
            public MeshFilter GlowFilter;
            public MeshRenderer GlowRenderer;
            public Mesh GlowMesh;
            public SegmentPlan Plan;
            public readonly List<Obstacle> Obstacles = new List<Obstacle>();
            public readonly List<EnergyPickup> Credits = new List<EnergyPickup>();
            public readonly List<PowerUpPickup> PowerUps = new List<PowerUpPickup>();
        }

        private sealed class ThemeMaterials
        {
            public Material Ground;
            public Material GroundDark;
            public Material Podium;
            public Material Sidewalk;
            public Material Puddle;
            public Material WetGlow;
            public Material WetGlowAlt;
            public Material Metal;
            public Material Paint;
            public Material Trim;
            public Material Neon;
            public Material NeonAlt;
            public Material BuildingA;
            public Material BuildingB;
            public Material BuildingC;
            public Material Roof;
            public Material Billboard;
            public Material Sign;
            public Material SignAlt;
            public Material Shop;
            public Material Pipe;
            public Material White;
            public Material Foliage;
            public Material FoliageDark;
            public Material FoliageLight;
            public Material Bark;
            public Material LampHaze;
            public Material WarmLamp;
            public Material WarmGlow;
            public Material NeonSoft;
        }

        private GameConfig _config;
        private ModeSpec _mode;
        private TrackDirector _director;
        private ObstaclePool _obstaclePool;
        private PickupPool _pickupPool;
        private Transform _segmentContainer;
        private Transform _poolContainer;
        private readonly Dictionary<int, Segment> _active = new Dictionary<int, Segment>();
        private readonly Stack<Segment> _freeSegments = new Stack<Segment>();
        private readonly List<Segment> _activeList = new List<Segment>();
        private readonly ThemeMaterials[] _themeMaterials = new ThemeMaterials[4];
        private int _nextIndex;
        private float _worldOffset;
        private int _currentBiome = -1;

        public float WorldOffset => _worldOffset;
        public int CurrentBiome => _currentBiome;
        public int ActiveSegmentCount => _active.Count;
        public int PooledObstacleCount { get; private set; }
        public int PooledPickupCount { get; private set; }
        public event Action<int> BiomeChanged;

        public void Initialise(GameConfig config, ModeSpec mode, int seed)
        {
            _config = config;
            _mode = mode;
            _director = new TrackDirector(seed, (int)mode.Id * 37);
            _segmentContainer = new GameObject("Track Segments").transform;
            _segmentContainer.SetParent(transform, false);
            _poolContainer = new GameObject("World Pools").transform;
            _poolContainer.SetParent(transform, false);
            _obstaclePool = new ObstaclePool(_poolContainer);
            _pickupPool = new PickupPool(_poolContainer);
            BuildThemeMaterials();
            Prewarm();
        }

        public void ResetForRun()
        {
            foreach (var segment in _activeList) ReleaseSegment(segment);
            _active.Clear();
            _activeList.Clear();
            _nextIndex = 0;
            _worldOffset = 0f;
            _currentBiome = -1;
        }

        public void SetMode(ModeSpec mode) => _mode = mode;

        private void BuildThemeMaterials()
        {
            for (int i = 0; i < 4; i++)
            {
                var theme = _config.Theme(i);
                if (theme == null) continue;
                var asphalt = TextureFactory.Asphalt(7701 + i);
                var asphaltNormal = TextureFactory.AsphaltNormal(7701 + i);
                var concrete = TextureFactory.Concrete(8801 + i);
                var transit = TextureFactory.SignBoard(4401 + i * 13, Palette.Magenta, i + 1);
                var adA = TextureFactory.BillboardArt(1201 + i * 7, theme.accent, i);
                var adB = TextureFactory.BillboardArt(8801 + i * 11, Palette.Magenta, i + 1);
                var shop = TextureFactory.ShopWindow(2101 + i * 17, theme.accent, i);
                var m = new ThemeMaterials
                {
                    Ground = MaterialLibrary.LitTextured("NV_AsphaltMat_" + i, asphalt,
                        Color.Lerp(theme.ground, Color.white, 0.24f), 0.22f, 0.94f,
                        new Vector2(2.4f, 10f), asphaltNormal, new Vector2(2.4f, 10f), 1.05f),
                    GroundDark = MaterialLibrary.LitTextured("NV_DeckMat_" + i, concrete,
                        Color.Lerp(theme.ground, Palette.Void, 0.48f), 0.25f, 0.42f,
                        new Vector2(2f, 8f)),
                    Podium = MaterialLibrary.LitTextured("NV_PodiumMat_" + i, concrete,
                        Color.Lerp(theme.buildingA, Color.white, 0.24f), 0.09f, 0.56f,
                        new Vector2(1.8f, 2.4f)),
                    Sidewalk = MaterialLibrary.LitTextured("NV_SidewalkMat_" + i, concrete,
                        Color.Lerp(theme.buildingA, Color.white, 0.18f), 0.12f, 0.5f,
                        new Vector2(2f, 12f)),
                    Puddle = MaterialLibrary.Lit(Color.Lerp(theme.skyBottom, Palette.Void, 0.45f), 0.92f, 0.99f),
                    WetGlow = MaterialLibrary.UnlitAdditiveTextured("NV_WetGlow_" + i,
                        TextureFactory.RadialGlow(64),
                        new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.3f), Vector2.one, false, -30),
                    WetGlowAlt = MaterialLibrary.UnlitAdditiveTextured("NV_WetGlowAlt_" + i,
                        TextureFactory.RadialGlow(64),
                        new Color(Palette.Magenta.r, Palette.Magenta.g, Palette.Magenta.b, 0.22f),
                        Vector2.one, false, -30),
                    Metal = MaterialLibrary.Lit(Color.Lerp(theme.buildingA, Palette.White, 0.13f), 0.88f, 0.76f),
                    Paint = MaterialLibrary.Lit(Color.Lerp(Palette.White, theme.accent, 0.18f), 0.08f, 0.74f),
                    Trim = MaterialLibrary.Lit(Color.Lerp(theme.buildingA, Palette.White, 0.2f), 0.75f, 0.7f),
                    Neon = MaterialLibrary.Lit(theme.accent, 0.2f, 0.8f, theme.accent, 3.1f),
                    NeonAlt = MaterialLibrary.Lit(Palette.Magenta, 0.2f, 0.8f, Palette.Magenta, 2.6f),
                    BuildingA = MaterialLibrary.Lit(theme.buildingA, 0.4f, 0.5f),
                    BuildingB = MaterialLibrary.Lit(theme.buildingB, 0.4f, 0.5f),
                    Roof = MaterialLibrary.Lit(Color.Lerp(theme.buildingB, Palette.Void, 0.55f), 0.4f, 0.35f),
                    Billboard = MaterialLibrary.UnlitTextured("NV_AdA_" + i, adA,
                        new Color(1.32f, 1.32f, 1.32f, 1f), Vector2.one),
                    Sign = MaterialLibrary.UnlitTextured("NV_Transit_" + i, transit,
                        new Color(1.05f, 1.05f, 1.05f, 1f), Vector2.one),
                    SignAlt = MaterialLibrary.UnlitTextured("NV_AdB_" + i, adB,
                        new Color(1.28f, 1.28f, 1.28f, 1f), Vector2.one),
                    Shop = MaterialLibrary.UnlitTextured("NV_ShopMat_" + i, shop,
                        new Color(1.16f, 1.16f, 1.16f, 1f), Vector2.one),
                    Pipe = MaterialLibrary.Lit(Color.Lerp(theme.buildingA, theme.accent, 0.12f), 0.8f, 0.7f),
                    White = MaterialLibrary.Lit(Palette.White, 0.2f, 0.8f, Palette.White, 1.2f),
                    WarmLamp = MaterialLibrary.Lit(Palette.Amber, 0.2f, 0.8f, Palette.Amber, 2.6f),
                    WarmGlow = MaterialLibrary.UnlitAdditiveTextured("NV_WarmGlow_" + i,
                        TextureFactory.RadialGlow(64),
                        new Color(1f, 0.62f, 0.3f, 0.24f), Vector2.one, false, -30),
                    NeonSoft = MaterialLibrary.Lit(Color.Lerp(theme.accent, Palette.White, 0.25f),
                        0.2f, 0.72f, Color.Lerp(theme.accent, Palette.White, 0.18f), 1.35f)
                };
                // Window atlases are mapped in world units by DecorMeshBuilder.Facade. Their
                // mipmaps prevent the previous per-pixel window shimmer at distance, and the
                // three families give each block a different architectural identity.
                var texA = TextureFactory.WindowFacade(3301 + i * 11);
                var texB = TextureFactory.WindowFacade(9901 + i * 17);
                var texC = TextureFactory.WindowFacade(6601 + i * 23, 1);
                Color facadeATint = Color.Lerp(Color.white, theme.accent, 0.10f) * 1.08f;
                Color facadeBTint = Color.Lerp(Color.white, Palette.Magenta, 0.10f) * 1.06f;
                Color facadeCTint = Color.Lerp(Color.white, Palette.Amber, 0.08f) * 1.04f;
                m.BuildingA = MaterialLibrary.UnlitTextured("NV_WinA_" + i, texA,
                    facadeATint, Vector2.one);
                m.BuildingB = MaterialLibrary.UnlitTextured("NV_WinB_" + i, texB,
                    facadeBTint, Vector2.one);
                m.BuildingC = MaterialLibrary.UnlitTextured("NV_WinC_" + i, texC,
                    facadeCTint, Vector2.one);

                Color leaf = Color.Lerp(new Color(0.055f, 0.16f, 0.10f), theme.accent, 0.11f);
                m.Foliage = MaterialLibrary.Lit(leaf, 0.02f, 0.32f, leaf, 0.22f);
                m.FoliageDark = MaterialLibrary.Lit(leaf * 0.58f, 0.02f, 0.28f, leaf * 0.58f, 0.12f);
                Color leafLight = Color.Lerp(new Color(0.12f, 0.30f, 0.17f), theme.accent, 0.10f);
                m.FoliageLight = MaterialLibrary.Lit(leafLight, 0.02f, 0.3f, leafLight, 0.16f);
                m.Bark = MaterialLibrary.Lit(new Color(0.18f, 0.125f, 0.083f), 0.05f, 0.32f,
                    new Color(0.16f, 0.10f, 0.055f), 0.35f);
                m.LampHaze = MaterialLibrary.UnlitAdditiveTextured("NV_LampHaze_" + i,
                    TextureFactory.LightShaft(),
                    new Color(1f, 0.7f, 0.38f, 0.17f), Vector2.one, false, -20);
                _themeMaterials[i] = m;
            }
        }

        private void Prewarm()
        {
            foreach (ObstacleKind kind in Enum.GetValues(typeof(ObstacleKind)))
            {
                var obstacle = _obstaclePool.Spawn(kind, _poolContainer, Vector3.zero, 0, 2f, 1f, 0f, 1f);
                _obstaclePool.Return(obstacle);
            }
            // Prime the pooled floor with a healthy budget of credits to avoid first-run hitches.
            var tempParent = new GameObject("PickupPrewarm").transform;
            tempParent.SetParent(_poolContainer, false);
            var prewarm = new List<EnergyPickup>();
            for (int i = 0; i < 140; i++)
                prewarm.Add(_pickupPool.SpawnCredit(tempParent, Vector3.zero, i * 0.1f, i % 23 == 0));
            for (int i = 0; i < prewarm.Count; i++) _pickupPool.Return(prewarm[i]);
            Destroy(tempParent.gameObject);
        }

        public void Tick(float distance, bool generate = true)
        {
            if (_director == null) return;
            int currentIndex = Mathf.FloorToInt(Mathf.Max(0f, distance) / SegmentLength);
            if (generate)
            {
                int targetLast = currentIndex + Mathf.Max(2, _config.segmentsAhead);
                while (_nextIndex <= targetLast)
                {
                    BuildSegment(_nextIndex);
                    _nextIndex++;
                }

                int firstAllowed = currentIndex - Mathf.Max(0, _config.segmentsBehind);
                if (_active.Count > 0)
                {
                    var toRemove = new List<Segment>();
                    foreach (var kv in _active) if (kv.Key < firstAllowed) toRemove.Add(kv.Value);
                    for (int i = 0; i < toRemove.Count; i++)
                    {
                        _active.Remove(toRemove[i].Index);
                        _activeList.Remove(toRemove[i]);
                        ReleaseSegment(toRemove[i]);
                    }
                }
            }

            // Ask the director for the biome of the segment the runner is actually on. The
            // director applies the seeded rotation offset after the opening city section, so
            // using a local index/4 formula here would desynchronise lighting, rain and audio
            // from the geometry as soon as the seed offset was non-zero.
            int biome = _director.GetSegment(currentIndex).BiomeIndex;
            if (biome != _currentBiome)
            {
                _currentBiome = biome;
                BiomeChanged?.Invoke(biome);
            }
        }

        public void ShiftWorld(float delta)
        {
            if (Mathf.Approximately(delta, 0f)) return;
            _worldOffset += delta;
            foreach (var segment in _activeList)
            {
                if (segment?.Root == null) continue;
                var p = segment.Root.transform.position;
                p.z += delta;
                segment.Root.transform.position = p;
            }
        }

        private void BuildSegment(int index)
        {
            Segment segment = _freeSegments.Count > 0 ? _freeSegments.Pop() : CreateSegment();
            segment.Index = index;
            segment.Plan = _director.GetSegment(index);
            segment.Root.transform.SetParent(_segmentContainer, false);
            segment.Root.transform.position = new Vector3(0f, 0f, index * SegmentLength + _worldOffset);
            segment.Root.name = $"Segment_{index:0000}_{segment.Plan.PatternId}";
            segment.Root.SetActive(true);
            segment.Obstacles.Clear();
            segment.Credits.Clear();
            segment.PowerUps.Clear();
            BuildDecor(segment);
            SpawnObstacles(segment);
            SpawnPickups(segment);
            _active[index] = segment;
            _activeList.Add(segment);
            _activeList.Sort((a, b) => a.Index.CompareTo(b.Index));
            PooledObstacleCount = _activeList.Count * 6;
            PooledPickupCount = _activeList.Count * 24;
        }

        private Segment CreateSegment()
        {
            var root = new GameObject("Segment");
            root.transform.SetParent(_segmentContainer, false);
            var filter = root.AddComponent<MeshFilter>();
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            // Additive cards (haze, wet-light pools) must not cast opaque shadows. Keeping
            // them in a second merged renderer preserves one draw call per material while
            // allowing shadow casting to be disabled for them independently.
            var glow = new GameObject("Glow Cards");
            glow.transform.SetParent(root.transform, false);
            var glowFilter = glow.AddComponent<MeshFilter>();
            var glowRenderer = glow.AddComponent<MeshRenderer>();
            glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glowRenderer.receiveShadows = false;
            return new Segment
            {
                Root = root, DecorFilter = filter, DecorRenderer = renderer,
                GlowFilter = glowFilter, GlowRenderer = glowRenderer
            };
        }

        private void ReleaseSegment(Segment segment)
        {
            if (segment == null) return;
            for (int i = 0; i < segment.Obstacles.Count; i++) _obstaclePool.Return(segment.Obstacles[i]);
            for (int i = 0; i < segment.Credits.Count; i++) _pickupPool.Return(segment.Credits[i]);
            for (int i = 0; i < segment.PowerUps.Count; i++) _pickupPool.Return(segment.PowerUps[i]);
            segment.Obstacles.Clear();
            segment.Credits.Clear();
            segment.PowerUps.Clear();
            if (segment.DecorMesh != null)
            {
                Destroy(segment.DecorMesh);
                segment.DecorMesh = null;
                segment.DecorFilter.sharedMesh = null;
            }
            if (segment.GlowMesh != null)
            {
                Destroy(segment.GlowMesh);
                segment.GlowMesh = null;
                segment.GlowFilter.sharedMesh = null;
            }
            segment.Root.SetActive(false);
            segment.Root.transform.SetParent(_poolContainer, false);
            _freeSegments.Push(segment);
        }

        private ThemeMaterials ThemeFor(SegmentPlan plan)
        {
            int index = Mathf.Abs(plan.BiomeIndex) % 4;
            return _themeMaterials[index] ?? _themeMaterials[0];
        }

        private void BuildDecor(Segment segment)
        {
            var builder = new DecorMeshBuilder();
            var theme = _config.Theme(segment.Plan.BiomeIndex);
            var mats = ThemeFor(segment.Plan);
            if (theme == null || mats == null) return;
            var rng = new DeterministicRandom(segment.Plan.Seed ^ 0x2A3B4C);

            BuildGround(builder, mats, segment.Plan, rng);
            if (segment.Plan.BiomeIndex % 4 == 3) BuildTunnel(builder, mats, theme, rng);
            else if (segment.Plan.BiomeIndex % 4 == 2) BuildReactor(builder, mats, theme, rng);
            else BuildCity(builder, mats, theme, rng);

            if (segment.Plan.Index % 3 == 1) BuildGateway(builder, mats, rng);
            if (segment.Plan.IsEvent) BuildEventMarker(builder, mats);

            segment.DecorMesh = builder.Build(segment.Root.transform, false, out Material[] materials);
            segment.DecorFilter.sharedMesh = segment.DecorMesh;
            segment.DecorRenderer.sharedMaterials = materials;
            segment.GlowMesh = builder.Build(segment.Root.transform, true, out Material[] glowMaterials);
            segment.GlowFilter.sharedMesh = segment.GlowMesh;
            segment.GlowRenderer.sharedMaterials = glowMaterials;
        }

        private static void BuildGround(DecorMeshBuilder b, ThemeMaterials mats, SegmentPlan plan, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            // Textured asphalt, a thicker structural deck and service pillars underneath.
            b.Box(mats.Ground, new Vector3(0f, -0.28f, half), new Vector3(9.4f, 0.55f, SegmentLength + 0.05f));
            b.Box(mats.GroundDark, new Vector3(0f, -0.75f, half), new Vector3(7.5f, 0.5f, SegmentLength));
            for (int i = 0; i < 3; i++)
            {
                float z = 7f + i * 14f;
                b.Box(mats.GroundDark, new Vector3(0f, -6f, z), new Vector3(6.2f, 10f, 1.2f));
            }

            // Painted lane separators, edge lines and small reflective studs. The previous
            // cyan lines read as a grid; white paint plus warm edge lamps looks more like a road.
            for (int lane = 0; lane < 2; lane++)
            {
                float x = -1.35f + lane * 2.7f;
                for (float z = 1f; z < SegmentLength; z += 8f)
                    b.Box(mats.Paint, new Vector3(x, 0.012f, z + 1.7f), new Vector3(0.13f, 0.014f, 3.4f));
                b.Box(mats.Paint, new Vector3(x, 0.011f, half), new Vector3(0.035f, 0.01f, SegmentLength));
            }
            for (int lane = 0; lane < 3; lane++)
            {
                float x = LaneMath.LaneToX(lane);
                for (int z = 5; z < SegmentLength; z += 12)
                    b.Box(mats.Neon, new Vector3(x, 0.018f, z), new Vector3(0.16f, 0.012f, 0.09f));
            }

            // Direction chevrons in the centre lane, only on the city and skyway biomes.
            if (plan.BiomeIndex % 4 < 2)
            {
                for (int arrow = 0; arrow < 2; arrow++)
                {
                    float z = 13f + arrow * 16f;
                    b.Box(mats.Paint, new Vector3(-0.38f, 0.017f, z),
                        new Vector3(0.13f, 0.012f, 1.05f), Quaternion.Euler(0f, 29f, 0f));
                    b.Box(mats.Paint, new Vector3(0.38f, 0.017f, z),
                        new Vector3(0.13f, 0.012f, 1.05f), Quaternion.Euler(0f, -29f, 0f));
                    b.Box(mats.Paint, new Vector3(0f, 0.017f, z - 0.52f),
                        new Vector3(0.13f, 0.012f, 0.9f));
                }
            }
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(mats.Paint, new Vector3(side * 3.98f, 0.011f, half), new Vector3(0.09f, 0.01f, SegmentLength));

                // Curbs, raised sidewalks and a continuous reflective gutter.
                b.Box(mats.Puddle, new Vector3(side * 4.34f, 0.008f, half), new Vector3(0.52f, 0.012f, SegmentLength));
                b.Box(mats.Sidewalk, new Vector3(side * 6.15f, 0.04f, half), new Vector3(2.1f, 0.2f, SegmentLength));
                b.Box(mats.Metal, new Vector3(side * 5.08f, 0.1f, half), new Vector3(0.18f, 0.2f, SegmentLength));
                b.Box(mats.Neon, new Vector3(side * 5.02f, 0.215f, half), new Vector3(0.035f, 0.025f, SegmentLength));
                for (int z = 2; z < SegmentLength; z += 7)
                    b.Box(mats.GroundDark, new Vector3(side * 6.15f, 0.145f, z), new Vector3(2.12f, 0.012f, 0.035f));

                // Paved forecourt at sidewalk height fills the setback in front of recessed
                // buildings. Beyond it a lower service apron and the far skyline block out the
                // void that appeared whenever a procedural block left an alley gap.
                b.Box(mats.Sidewalk, new Vector3(side * 9.05f, 0.04f, half),
                    new Vector3(3.7f, 0.2f, SegmentLength + 0.1f));
                b.Box(mats.GroundDark, new Vector3(side * 20.0f, -0.15f, half),
                    new Vector3(18.2f, 0.3f, SegmentLength + 0.2f));
                b.Box(mats.Metal, new Vector3(side * 10.92f, 0.03f, half),
                    new Vector3(0.1f, 0.28f, SegmentLength));
                for (int z = 4; z < SegmentLength; z += 9)
                    b.Box(mats.GroundDark, new Vector3(side * 8.6f, 0.145f, z),
                        new Vector3(2.0f, 0.012f, 0.03f));

                // Safety rail with posts, reflective caps and drain grates.
                b.Box(mats.Metal, new Vector3(side * 4.85f, 0.55f, half), new Vector3(0.14f, 1.1f, SegmentLength));
                b.Box(mats.NeonAlt, new Vector3(side * 4.85f, 1.12f, half), new Vector3(0.06f, 0.06f, SegmentLength));
                for (int z = 3; z < SegmentLength; z += 8)
                {
                    b.Box(mats.Metal, new Vector3(side * 4.85f, 0.3f, z), new Vector3(0.3f, 0.6f, 0.16f));
                    b.Box(mats.Neon, new Vector3(side * 4.78f, 0.72f, z), new Vector3(0.04f, 0.1f, 0.1f));
                }
                for (int z = 6; z < SegmentLength; z += 14)
                {
                    b.Box(mats.Metal, new Vector3(side * 4.42f, 0.028f, z), new Vector3(0.54f, 0.025f, 1.1f));
                    for (int slat = -1; slat <= 1; slat++)
                        b.Box(mats.GroundDark, new Vector3(side * 4.42f + slat * 0.13f, 0.045f, z),
                            new Vector3(0.055f, 0.012f, 0.92f));
                }
            }

            // Manhole covers, cracks, wet patches and road repairs break up the tiling.
            b.Cylinder(mats.Metal, new Vector3(1.68f, 0.014f, 18f), new Vector3(0.52f, 0.012f, 0.52f));
            b.Cylinder(mats.GroundDark, new Vector3(1.68f, 0.021f, 18f), new Vector3(0.4f, 0.008f, 0.4f));
            for (int i = 0; i < 3; i++)
            {
                float x = rng.Range(-3.7f, 3.7f);
                float z = rng.Range(3f, SegmentLength - 3f);
                b.Box(mats.GroundDark, new Vector3(x, 0.006f, z),
                    new Vector3(rng.Range(0.018f, 0.04f), 0.006f, rng.Range(1.2f, 3.2f)),
                    Quaternion.Euler(0f, rng.Range(-24f, 24f), 0f));
            }
            for (int i = 0; i < 3; i++)
            {
                float x = rng.Range(-3.9f, 3.9f);
                float z = rng.Range(4f, SegmentLength - 3f);
                b.Box(mats.Puddle, new Vector3(x, 0.004f, z),
                    new Vector3(rng.Range(0.9f, 2.2f), 0.012f, rng.Range(1.6f, 4.2f)),
                    Quaternion.Euler(0f, rng.Range(-14f, 14f), 0f));
            }

            // Soft additive smears sit on top of the glossy asphalt and read as the wet
            // reflection of lane neon, shop signs and the amber street lamps. They are flat
            // cards, so the effect costs only a handful of triangles and no extra lights.
            for (int i = 0; i < 6; i++)
            {
                int lane = rng.Range(0, 3);
                float x = LaneMath.LaneToX(lane) + rng.Range(-0.55f, 0.55f);
                float z = rng.Range(4f, SegmentLength - 4f);
                Material glow = i % 3 == 0 ? mats.WetGlowAlt : mats.WetGlow;
                b.Quad(glow, new Vector3(x, 0.042f, z),
                    new Vector3(rng.Range(0.45f, 1.1f), rng.Range(2.1f, 4.8f), 1f),
                    Quaternion.Euler(90f, rng.Range(-8f, 8f), 0f));
            }
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    float z = 6f + i * 13f + rng.Range(-2f, 2f);
                    b.Quad(i == 1 ? mats.WetGlowAlt : mats.WetGlow,
                        new Vector3(side * 4.12f, 0.043f, z),
                        new Vector3(0.55f, 2.7f, 1f), Quaternion.Euler(90f, 0f, 0f));
                }
            }

        }

        private static void BuildCity(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            // Recessed frontage is the foundation of the city upgrade. Buildings are placed
            // from their near face outward, so even the widest block can no longer eat into
            // the 2.1 m sidewalk. That leaves room for real street furniture and makes the
            // road read as a canyon rather than a corridor past a window wall.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    float z = 2.4f + i * 14.1f + rng.Range(-1.1f, 1.1f);
                    float width = rng.Range(5f, 8.4f);
                    float depth = rng.Range(7.3f, 10.5f);
                    float nearFace = 7.7f + rng.Range(0.1f, 2.3f);
                    float x = side * (nearFace + width * 0.5f);
                    bool lowRise = rng.Chance(0.2f);
                    float height = lowRise ? rng.Range(6.2f, 9.5f) : rng.Range(14f, 35f);
                    int style = rng.Range(0, 3);
                    Material facade = style == 0 ? mats.BuildingA : style == 1 ? mats.BuildingB : mats.BuildingC;
                    bool crown = height > 20f && rng.Chance(0.58f);
                    float lowerHeight = crown ? height * rng.Range(0.60f, 0.72f) : height;
                    float crownHeight = height - lowerHeight;
                    float upperWidth = width * rng.Range(0.62f, 0.78f);
                    float upperDepth = depth * rng.Range(0.68f, 0.82f);
                    Vector2 uvOffset = new Vector2(rng.Range(0, 4) * 0.25f, rng.Range(0, 4) * 0.25f);
                    float nearX = side * nearFace;
                    float upperNearX = x - side * (upperWidth * 0.5f + 0.04f);

                    b.Facade(facade, new Vector3(x, (lowerHeight - 0.15f) * 0.5f, z),
                        new Vector3(width, lowerHeight + 0.15f, depth), uvOffset);
                    b.Box(mats.Roof, new Vector3(x, lowerHeight + 0.06f, z),
                        new Vector3(width + 0.36f, 0.32f, depth + 0.36f));
                    if (crown)
                    {
                        b.Facade(facade, new Vector3(x, (lowerHeight + height) * 0.5f, z),
                            new Vector3(upperWidth, crownHeight, upperDepth), uvOffset + new Vector2(0.25f, 0f));
                        b.Box(mats.Roof, new Vector3(x, height + 0.06f, z),
                            new Vector3(upperWidth + 0.4f, 0.32f, upperDepth + 0.4f));
                    }

                    // A masonry podium gives the shops a base and hides the window atlas
                    // behind the ground floor. The metal band and corner pilasters make the
                    // transition from pavement to tower deliberate.
                    b.Box(mats.Podium, new Vector3(x, 1.55f, z),
                        new Vector3(width + 0.08f, 3.1f, depth + 0.08f));
                    b.Box(mats.Trim, new Vector3(nearX - side * 0.08f, 3.06f, z),
                        new Vector3(0.22f, 0.12f, depth + 0.12f));
                    b.Box(mats.Trim, new Vector3(nearX - side * 0.08f, 1.55f, z - depth * 0.48f),
                        new Vector3(0.24f, 3.1f, 0.22f));
                    b.Box(mats.Trim, new Vector3(nearX - side * 0.08f, 1.55f, z + depth * 0.48f),
                        new Vector3(0.24f, 3.1f, 0.22f));

                    // One deep shop window plus a separate recessed door. The old build put two
                    // identical abstract signs on every block; this keeps variety while adding
                    // mullions, sills, an awning and a lit transom.
                    float shopZ = z + depth * 0.12f;
                    float shopWidth = depth * 0.48f;
                    b.Box(mats.Shop, new Vector3(nearX - side * 0.17f, 1.72f, shopZ),
                        new Vector3(0.06f, 1.5f, shopWidth));
                    b.Box(mats.Metal, new Vector3(nearX - side * 0.22f, 0.98f, shopZ),
                        new Vector3(0.04f, 0.07f, shopWidth + 0.06f));
                    b.Box(mats.Metal, new Vector3(nearX - side * 0.22f, 2.5f, shopZ),
                        new Vector3(0.04f, 0.06f, shopWidth + 0.08f));
                    b.Box(mats.Metal, new Vector3(nearX - side * 0.22f, 1.74f, shopZ),
                        new Vector3(0.035f, 1.52f, 0.045f));
                    b.Box(mats.Metal, new Vector3(nearX - side * 0.22f, 1.74f, shopZ - shopWidth * 0.27f),
                        new Vector3(0.035f, 1.52f, 0.035f));
                    b.Box(mats.Metal, new Vector3(nearX - side * 0.22f, 1.74f, shopZ + shopWidth * 0.27f),
                        new Vector3(0.035f, 1.52f, 0.035f));
                    b.Box(mats.Trim, new Vector3(nearX - side * 0.53f, 2.94f, shopZ),
                        new Vector3(1.05f, 0.08f, shopWidth + 0.2f));
                    b.Box(mats.NeonAlt, new Vector3(nearX - side * 1.03f, 2.87f, shopZ),
                        new Vector3(0.055f, 0.045f, shopWidth + 0.16f));
                    b.Box(mats.WarmLamp, new Vector3(nearX - side * 0.27f, 2.56f, shopZ),
                        new Vector3(0.035f, 0.035f, shopWidth * 0.8f));
                    b.Quad(mats.WarmGlow, new Vector3(nearX - side * 1.25f, 0.155f, shopZ),
                        new Vector3(1.55f, shopWidth * 0.88f, 1f), Quaternion.Euler(90f, 0f, 0f));

                    // Occasional flat wall sign beside the shop window. Its portrait art
                    // reads better than stretching a landscape atlas across the fascia.
                    if (rng.Chance(0.5f))
                    {
                        float wallSignZ = shopZ + shopWidth * 0.60f + 0.28f;
                        if (wallSignZ < z + depth * 0.47f)
                        {
                            b.Box(mats.Metal, new Vector3(nearX - side * 0.13f, 3.92f, wallSignZ),
                                new Vector3(0.18f, 1.18f, 0.66f));
                            b.Box(mats.SignAlt, new Vector3(nearX - side * 0.23f, 3.92f, wallSignZ),
                                new Vector3(0.035f, 1.06f, 0.54f));
                        }
                    }

                    float doorZ = z - depth * 0.34f;
                    b.Box(mats.GroundDark, new Vector3(nearX - side * 0.16f, 1.24f, doorZ),
                        new Vector3(0.12f, 2.48f, 1.45f));
                    b.Box(mats.Metal, new Vector3(nearX - side * 0.23f, 1.22f, doorZ),
                        new Vector3(0.035f, 2.3f, 0.045f));
                    b.Box(mats.Neon, new Vector3(nearX - side * 0.245f, 2.38f, doorZ),
                        new Vector3(0.025f, 0.09f, 0.92f));

                    // Physical floor ledges catch the moving running lights; selected
                    // residential blocks get a readable balcony rail instead of another flat
                    // window texture.
                    int floors = Mathf.Min(9, Mathf.FloorToInt(Mathf.Max(0f, lowerHeight - 3.2f) / 3.2f));
                    bool residential = style == 2;
                    for (int floor = 1; floor <= floors; floor++)
                    {
                        float floorY = 3.1f + floor * 3.2f;
                        b.Box(mats.Roof, new Vector3(nearX - side * 0.15f, floorY, z),
                            new Vector3(0.36f, 0.1f, depth * 0.9f));
                        if (residential && floor % 2 == 0 && rng.Chance(0.72f))
                        {
                            b.Box(mats.Metal, new Vector3(nearX - side * 0.34f, floorY + 0.72f, z),
                                new Vector3(0.045f, 0.045f, depth * 0.84f));
                            for (int post = 0; post < 5; post++)
                            {
                                float pz = z + (post - 2) * depth * 0.16f;
                                b.Box(mats.Metal, new Vector3(nearX - side * 0.34f, floorY + 0.34f, pz),
                                    new Vector3(0.035f, 0.68f, 0.035f));
                            }
                        }
                    }

                    // Air-conditioning boxes, service pipes and facade neon break up the
                    // repeated window bays at close range.
                    int acCount = lowRise ? 1 : rng.Range(1, 4);
                    for (int ac = 0; ac < acCount; ac++)
                    {
                        float ay = 4.2f + rng.Range(0f, Mathf.Max(0.2f, lowerHeight - 6.2f));
                        float az = z + rng.Range(-depth * 0.4f, depth * 0.4f);
                        b.Box(mats.Metal, new Vector3(nearX - side * 0.26f, ay, az),
                            new Vector3(0.42f, 0.34f, 0.68f));
                        b.Box(mats.GroundDark, new Vector3(nearX - side * 0.48f, ay, az),
                            new Vector3(0.06f, 0.2f, 0.5f));
                    }
                    for (int pipe = 0; pipe < 2; pipe++)
                    {
                        float pz = z + (pipe == 0 ? 1f : -1f) * depth * 0.47f;
                        b.Cylinder(mats.Pipe, new Vector3(nearX - side * 0.12f, lowerHeight * 0.5f, pz),
                            new Vector3(0.055f, lowerHeight * 0.5f, 0.055f));
                    }
                    if (rng.Chance(0.46f))
                    {
                        b.Box(mats.NeonSoft, new Vector3(nearX - side * 0.1f, lowerHeight * 0.55f,
                                z - depth * 0.47f),
                            new Vector3(0.035f, lowerHeight * 0.56f, 0.06f));
                    }

                    // Large facade media panel. Only some blocks carry one, preserving the
                    // residential masonry silhouettes.
                    if (rng.Chance(0.56f))
                    {
                        float billboardHeight = Mathf.Clamp(lowerHeight * 0.28f, 2.2f, 5.2f);
                        float billboardY = Mathf.Min(lowerHeight - 1.1f,
                            5f + rng.Range(0f, Mathf.Max(0.2f, lowerHeight - 8f)));
                        b.Box(mats.Billboard, new Vector3(nearX - side * 0.17f, billboardY, z - depth * 0.03f),
                            new Vector3(0.08f, billboardHeight, depth * 0.38f));
                        b.Box(mats.Metal, new Vector3(nearX - side * 0.22f, billboardY,
                                z - depth * 0.23f),
                            new Vector3(0.06f, billboardHeight + 0.18f, 0.08f));
                    }

                    // Blade sign projecting over the forecourt.
                    if (rng.Chance(0.58f))
                    {
                        float signZ = z + rng.Range(-depth * 0.3f, depth * 0.3f);
                        float signY = Mathf.Min(lowerHeight - 1.5f, 4.4f + rng.Range(0f, 2.1f));
                        float signX = nearX - side * 0.82f;
                        if (signY > 3.5f)
                        {
                            b.Box(mats.Metal, new Vector3(signX, signY + 1.5f, signZ),
                                new Vector3(0.22f, 0.12f, 1.18f));
                            b.Box(mats.SignAlt, new Vector3(signX - side * 0.09f, signY, signZ),
                                new Vector3(0.09f, 2.8f, 0.92f));
                            b.Box(mats.Neon, new Vector3(signX - side * 0.16f, signY - 1.5f, signZ),
                                new Vector3(0.035f, 0.4f, 0.74f));
                        }
                    }

                    // Roof plant, water tanks and antennas make the skyline legible in
                    // silhouette, even when a block is mostly hidden by a tree.
                    if (height > 7f)
                    {
                        for (int unit = 0; unit < 2; unit++)
                        {
                            b.Box(mats.Metal, new Vector3(
                                    x + rng.Range(-width * 0.24f, width * 0.24f), height + 0.65f,
                                    z + rng.Range(-depth * 0.24f, depth * 0.24f)),
                                new Vector3(1.15f, 0.85f, 1.35f));
                        }
                    }
                    if (rng.Chance(0.42f))
                    {
                        b.Cylinder(mats.Metal, new Vector3(x, height + 1.2f, z),
                            new Vector3(0.85f, 0.55f, 0.85f));
                        b.Box(mats.Neon, new Vector3(x, height + 1.8f, z),
                            new Vector3(0.5f, 0.05f, 0.5f));
                    }
                    if (rng.Chance(0.48f))
                    {
                        b.Box(mats.Trim, new Vector3(x, height + 2.4f, z),
                            new Vector3(0.09f, 4.8f, 0.09f));
                        b.Box(mats.NeonAlt, new Vector3(x, height + 4.8f, z),
                            new Vector3(0.25f, 0.1f, 0.25f));
                    }
                    if (rng.Chance(0.38f))
                    {
                        b.Box(mats.Neon, new Vector3(x, height + 0.3f, z - depth * 0.5f),
                            new Vector3(width * 0.72f, 0.045f, 0.06f));
                    }

                    b.Box(mats.Puddle, new Vector3(nearX - side * 1.0f, 0.008f, z + depth * 0.3f),
                        new Vector3(1.4f, 0.012f, 2.8f));
                }

                // Street trees, benches, a bin and a name sign. These are merged into the
                // segment mesh, so the street gains depth without adding per-object draw calls.
                AddStreetTree(b, mats, side, 9.8f + rng.Range(-0.6f, 0.6f), rng);
                AddStreetTree(b, mats, side, 27.4f + rng.Range(-0.6f, 0.6f), rng);
                AddBench(b, mats, side, 23.2f + rng.Range(-0.5f, 0.5f));
                AddLitterBin(b, mats, side, 35.4f + rng.Range(-0.4f, 0.4f), rng);
                AddStreetSign(b, mats, side, 13.4f + rng.Range(-0.4f, 0.4f), rng);

                // Street lamps now carry a soft additive cone. It is a card, not a real
                // volumetric light, but it gives the wet road and rain a source to read against.
                for (int i = 0; i < 3; i++)
                {
                    float z = 5f + i * 13f;
                    b.Box(mats.Metal, new Vector3(side * 6.55f, 2.3f, z), new Vector3(0.11f, 4.6f, 0.11f));
                    b.Box(mats.Metal, new Vector3(side * 6.08f, 4.47f, z), new Vector3(1.02f, 0.08f, 0.12f));
                    b.Box(mats.Trim, new Vector3(side * 6.55f, 4.14f, z), new Vector3(0.2f, 0.2f, 0.2f));
                    b.Box(mats.Trim, new Vector3(side * 5.66f, 4.47f, z), new Vector3(0.64f, 0.12f, 0.3f));
                    b.Box(mats.WarmLamp, new Vector3(side * 5.66f, 4.4f, z), new Vector3(0.44f, 0.03f, 0.2f));
                    b.Quad(mats.LampHaze, new Vector3(side * 5.66f, 2.2f, z),
                        new Vector3(1.35f, 4.4f, 1f));
                    b.Quad(mats.WarmGlow, new Vector3(side * 5.62f, 0.155f, z),
                        new Vector3(2.2f, 3.6f, 1f), Quaternion.Euler(90f, 0f, 0f));
                    // A second, dimmer pool spills over the curb onto the racing surface.
                    b.Quad(mats.WarmGlow, new Vector3(side * 3.95f, 0.022f, z),
                        new Vector3(1.25f, 2.1f, 1f), Quaternion.Euler(90f, 0f, 0f));
                    b.Box(mats.Metal, new Vector3(side * 6.55f, 0.24f, z),
                        new Vector3(0.18f, 0.48f, 0.18f));
                    if (i < 2)
                    {
                        b.Box(mats.Metal, new Vector3(side * 4.62f, 0.38f, z + 4f),
                            new Vector3(0.1f, 0.76f, 0.1f));
                        b.Box(mats.NeonAlt, new Vector3(side * 4.62f, 0.78f, z + 4f),
                            new Vector3(0.13f, 0.06f, 0.13f));
                    }
                }

                // Midground towers and darker far silhouettes are generated in layers. The
                // fog fades them successively, which creates atmospheric depth beyond the
                // playable canyon without any authored skybox.
                for (int i = 0; i < 4; i++)
                {
                    float farX = side * rng.Range(21f, 49f);
                    float farZ = rng.Range(-5f, SegmentLength + 5f);
                    float farH = rng.Range(18f, 64f);
                    float farW = rng.Range(6f, 15f);
                    float farD = rng.Range(7f, 16f);
                    Material farFacade = i % 3 == 0 ? mats.BuildingC : i % 2 == 0 ? mats.BuildingB : mats.BuildingA;
                    b.Facade(farFacade, new Vector3(farX, farH * 0.5f, farZ),
                        new Vector3(farW, farH, farD),
                        new Vector2(rng.Range(0, 4) * 0.25f, rng.Range(0, 4) * 0.25f));
                    b.Box(mats.Roof, new Vector3(farX, farH + 0.05f, farZ),
                        new Vector3(farW + 0.3f, 0.28f, farD + 0.3f));
                    if (rng.Chance(0.55f))
                    {
                        b.Box(mats.Neon, new Vector3(farX - side * (farW * 0.5f + 0.05f),
                                farH * 0.7f, farZ),
                            new Vector3(0.08f, farH * 0.42f, 0.25f));
                    }
                    if (rng.Chance(0.45f))
                    {
                        b.Box(mats.Trim, new Vector3(farX, farH + 3f, farZ),
                            new Vector3(0.1f, 6f, 0.1f));
                        b.Box(mats.NeonAlt, new Vector3(farX, farH + 6f, farZ),
                            new Vector3(0.3f, 0.12f, 0.3f));
                    }
                }
                for (int i = 0; i < 2; i++)
                {
                    float farX = side * rng.Range(54f, 98f);
                    float farZ = rng.Range(-8f, SegmentLength + 8f);
                    float farH = rng.Range(30f, 92f);
                    float farW = rng.Range(10f, 24f);
                    float farD = rng.Range(10f, 24f);
                    b.Box(mats.GroundDark, new Vector3(farX, farH * 0.5f, farZ),
                        new Vector3(farW, farH, farD));
                    b.Box(mats.NeonAlt, new Vector3(farX - side * (farW * 0.5f + 0.04f),
                            farH * 0.76f, farZ),
                        new Vector3(0.1f, 0.35f, farD * 0.7f));
                    b.Box(mats.Neon, new Vector3(farX, farH + 0.4f, farZ),
                        new Vector3(0.32f, 0.5f, 0.32f));
                }
            }

            // Cables and suspended lamps bridge the street above the runner.
            for (int i = 0; i < 2; i++)
            {
                float z = 7f + i * 19f;
                float y = 13.5f + i * 4.2f;
                b.Cylinder(mats.Metal, new Vector3(0f, y, z), new Vector3(0.035f, 9.5f, 0.035f),
                    Quaternion.Euler(0f, 0f, 90f));
                for (int light = -1; light <= 1; light++)
                {
                    b.Box(mats.Neon, new Vector3(light * 4.1f, y - 0.32f, z),
                        new Vector3(0.15f, 0.22f, 0.15f));
                    b.Box(mats.Metal, new Vector3(light * 4.1f, y - 0.16f, z),
                        new Vector3(0.03f, 0.32f, 0.03f));
                }
            }

            // Hover pylons stand just outside the forecourt instead of clipping the new
            // recessed storefronts.
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 2; i++)
                {
                    float z = 7.2f + i * 21f;
                    b.Box(mats.Trim, new Vector3(side * 7.0f, 2.8f, z),
                        new Vector3(0.16f, 5.6f, 0.16f));
                    b.Box(mats.Neon, new Vector3(side * 7.0f, 5.6f, z),
                        new Vector3(0.55f, 0.12f, 0.55f));
                    b.Box(mats.NeonAlt, new Vector3(side * 7.0f, 0.08f, z),
                        new Vector3(0.4f, 0.1f, 0.4f));
                }
        }

        private static void AddStreetTree(DecorMeshBuilder b, ThemeMaterials mats, int side, float z,
            DeterministicRandom rng)
        {
            float x = side * 6.28f;
            float yaw = rng.Range(0f, 360f);
            float lean = rng.Range(-3f, 3f);

            // Square planter, visible soil, metal guard and a strip of low neon.
            b.Box(mats.GroundDark, new Vector3(x, 0.43f, z), new Vector3(1.16f, 0.58f, 1.16f));
            b.Box(mats.Trim, new Vector3(x, 0.74f, z), new Vector3(1.24f, 0.08f, 1.24f));
            b.Box(mats.Bark, new Vector3(x, 0.79f, z), new Vector3(0.96f, 0.03f, 0.96f));
            for (int edge = -1; edge <= 1; edge += 2)
            {
                b.Box(mats.NeonAlt, new Vector3(x, 0.77f, z + edge * 0.61f),
                    new Vector3(0.64f, 0.025f, 0.04f));
                b.Box(mats.NeonAlt, new Vector3(x + edge * 0.61f, 0.77f, z),
                    new Vector3(0.04f, 0.025f, 0.64f));
            }
            for (int gx = -1; gx <= 1; gx += 2)
            for (int gz = -1; gz <= 1; gz += 2)
            {
                b.Box(mats.Metal, new Vector3(x + gx * 0.43f, 1.12f, z + gz * 0.43f),
                    new Vector3(0.045f, 0.72f, 0.045f));
            }
            b.Box(mats.Metal, new Vector3(x, 1.49f, z - 0.43f), new Vector3(0.9f, 0.035f, 0.035f));
            b.Box(mats.Metal, new Vector3(x, 1.49f, z + 0.43f), new Vector3(0.9f, 0.035f, 0.035f));

            b.Cylinder(mats.Bark, new Vector3(x, 1.72f, z), new Vector3(0.29f, 1.2f, 0.29f),
                Quaternion.Euler(lean, yaw, lean * 0.5f));
            b.Cylinder(mats.Bark, new Vector3(x - side * 0.22f, 2.82f, z - 0.1f),
                new Vector3(0.11f, 0.55f, 0.11f), Quaternion.Euler(0f, 0f, side * 32f));
            b.Cylinder(mats.Bark, new Vector3(x + side * 0.2f, 2.92f, z + 0.12f),
                new Vector3(0.10f, 0.5f, 0.10f), Quaternion.Euler(0f, 0f, -side * 26f));

            b.Foliage(mats.FoliageDark, new Vector3(x, 3.55f, z),
                new Vector3(1.32f, 0.92f, 1.32f), Quaternion.Euler(0f, yaw, 0f));
            b.Foliage(mats.FoliageLight, new Vector3(x + side * 0.3f, 3.78f, z - 0.24f),
                new Vector3(1.06f, 0.8f, 1.08f), Quaternion.Euler(0f, yaw + 38f, 0f));
            b.Foliage(mats.Foliage, new Vector3(x - side * 0.32f, 3.92f, z + 0.22f),
                new Vector3(0.94f, 0.72f, 0.92f), Quaternion.Euler(0f, yaw - 22f, 0f));
        }

        private static void AddBench(DecorMeshBuilder b, ThemeMaterials mats, int side, float z)
        {
            float x = side * 6.46f;
            b.Box(mats.Trim, new Vector3(x, 0.56f, z), new Vector3(0.56f, 0.1f, 1.42f));
            for (int slat = -1; slat <= 1; slat++)
                b.Box(mats.Bark, new Vector3(x + slat * 0.14f, 0.62f, z),
                    new Vector3(0.1f, 0.035f, 1.34f));
            b.Box(mats.Trim, new Vector3(x + side * 0.25f, 0.94f, z),
                new Vector3(0.08f, 0.7f, 1.42f));
            for (int slat = 0; slat < 3; slat++)
                b.Box(mats.Bark, new Vector3(x + side * 0.3f, 0.72f + slat * 0.19f, z),
                    new Vector3(0.045f, 0.14f, 1.34f));
            for (int leg = -1; leg <= 1; leg += 2)
            {
                b.Box(mats.Metal, new Vector3(x, 0.32f, z + leg * 0.55f),
                    new Vector3(0.44f, 0.56f, 0.08f));
                b.Box(mats.Metal, new Vector3(x + side * 0.24f, 0.55f, z + leg * 0.55f),
                    new Vector3(0.07f, 1.2f, 0.07f));
            }
        }

        private static void AddLitterBin(DecorMeshBuilder b, ThemeMaterials mats, int side, float z,
            DeterministicRandom rng)
        {
            float x = side * 6.82f;
            b.Cylinder(mats.Metal, new Vector3(x, 0.54f, z), new Vector3(0.25f, 0.4f, 0.25f));
            b.Cylinder(mats.GroundDark, new Vector3(x, 0.94f, z), new Vector3(0.28f, 0.045f, 0.28f));
            b.Box(mats.NeonAlt, new Vector3(x - side * 0.25f, 0.66f, z),
                new Vector3(0.035f, 0.24f, 0.22f));
            if (rng.Chance(0.55f))
            {
                b.Box(mats.Trim, new Vector3(x, 0.2f, z), new Vector3(0.36f, 0.05f, 0.36f));
            }
        }

        private static void AddStreetSign(DecorMeshBuilder b, ThemeMaterials mats, int side, float z,
            DeterministicRandom rng)
        {
            float x = side * 6.94f;
            b.Box(mats.Metal, new Vector3(x, 1.6f, z), new Vector3(0.07f, 3.2f, 0.07f));
            b.Box(mats.Metal, new Vector3(x - side * 0.45f, 2.92f, z),
                new Vector3(0.9f, 0.055f, 0.055f));
            b.Box(mats.Billboard, new Vector3(x - side * 0.73f, 2.62f, z),
                new Vector3(0.045f, 0.68f, 1.18f));
            b.Box(mats.Metal, new Vector3(x - side * 0.73f, 3.02f, z),
                new Vector3(0.055f, 0.06f, 1.28f));
            b.Box(mats.Neon, new Vector3(x, 3.3f, z), new Vector3(0.1f, 0.1f, 0.1f));
            if (rng.Chance(0.7f))
            {
                b.Box(mats.SignAlt, new Vector3(x - side * 0.08f, 2.15f, z + 0.38f),
                    new Vector3(0.04f, 1.1f, 0.32f));
            }
        }

        private static void BuildReactor(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                // Concrete hazard wall with steel ribs and warning lamps.
                b.Box(mats.GroundDark, new Vector3(side * 7.2f, 2.4f, half), new Vector3(2.2f, 4.8f, SegmentLength));
                for (int i = 0; i < 8; i++)
                {
                    float z = 2f + i * 5.4f;
                    b.Box(mats.Metal, new Vector3(side * 6.03f, 2.4f, z), new Vector3(0.22f, 4.6f, 0.2f));
                    b.Box(mats.Neon, new Vector3(side * 5.99f, 2.8f, z), new Vector3(0.06f, 0.16f, 4.1f));
                    b.Box(mats.NeonAlt, new Vector3(side * 5.96f, 1.2f, z), new Vector3(0.05f, 1.2f, 0.16f));
                }
                // Hazard chevrons on the floor shoulder.
                for (int i = 0; i < 6; i++)
                    b.Box(mats.Paint, new Vector3(side * 4.3f, 0.02f, 3f + i * 7f),
                        new Vector3(0.55f, 0.01f, 2.4f), Quaternion.Euler(0f, side * 18f, 0f));

                // Horizontal coolant pipes and valve wheels.
                b.Cylinder(mats.Pipe, new Vector3(side * 5.7f, 1.15f, half), new Vector3(0.28f, 21f, 0.28f),
                    Quaternion.Euler(90f, 0f, 0f));
                b.Cylinder(mats.Pipe, new Vector3(side * 6.2f, 3.7f, half), new Vector3(0.18f, 21f, 0.18f),
                    Quaternion.Euler(90f, 0f, 0f));
                for (int i = 0; i < 3; i++)
                {
                    float z = 8f + i * 13f;
                    b.Cylinder(mats.Metal, new Vector3(side * 5.7f, 1.55f, z), new Vector3(0.48f, 0.08f, 0.48f));
                    b.Box(mats.Neon, new Vector3(side * 5.7f, 1.55f, z), new Vector3(0.5f, 0.05f, 0.5f));
                }
                // Tanks, vents and overhead cable trays.
                for (int i = 0; i < 2; i++)
                {
                    float z = 8f + i * 19f;
                    b.Cylinder(mats.Metal, new Vector3(side * 10f, 3.2f, z), new Vector3(1.8f, 3.2f, 1.8f));
                    b.Cylinder(mats.Neon, new Vector3(side * 10f, 6.2f, z), new Vector3(1.5f, 0.1f, 1.5f));
                    b.Cylinder(mats.GroundDark, new Vector3(side * 10f, 7.4f, z), new Vector3(0.4f, 1.1f, 0.4f));
                }
                b.Box(mats.Metal, new Vector3(side * 6.8f, 8f, half), new Vector3(0.2f, 0.2f, SegmentLength));
                b.Box(mats.Neon, new Vector3(side * 6.75f, 8.15f, half), new Vector3(0.06f, 0.04f, SegmentLength));
            }
            // Overhead gantry with an illuminated sign.
            b.Box(mats.Metal, new Vector3(0f, 7.4f, 18f), new Vector3(15f, 0.35f, 0.4f));
            b.Box(mats.Neon, new Vector3(0f, 7.15f, 18f), new Vector3(10f, 0.06f, 0.1f));
            b.Box(mats.Sign, new Vector3(0f, 6.6f, 18f), new Vector3(4.2f, 1.4f, 0.08f));
        }

        private static void BuildTunnel(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                // Structural shell, tiled wall bays and lit steel ribs.
                b.Box(mats.GroundDark, new Vector3(side * 6.5f, 3.2f, half), new Vector3(1.2f, 6.4f, SegmentLength));
                b.Box(mats.GroundDark, new Vector3(side * 5.86f, 1.5f, half), new Vector3(0.08f, 1.9f, SegmentLength));
                for (int i = 0; i < 7; i++)
                {
                    float z = 3f + i * 6f;
                    b.Box(mats.Metal, new Vector3(side * 5.78f, 3.2f, z), new Vector3(0.2f, 6.2f, 0.3f));
                    b.Box(mats.Neon, new Vector3(side * 5.72f, 1.15f, z), new Vector3(0.06f, 0.85f, 0.24f));
                    b.Box(mats.NeonAlt, new Vector3(side * 5.74f, 4.55f, z), new Vector3(0.04f, 0.3f, 0.14f));
                    b.Box(mats.Metal, new Vector3(side * 5.72f, 2.5f, z), new Vector3(0.06f, 0.08f, 4.7f));
                    b.Box(mats.GroundDark, new Vector3(side * 5.81f, 3.55f + (i % 2) * 0.55f, z + 2.6f),
                        new Vector3(0.025f, 0.55f, 2.2f));
                }

                // Subway platform behind the existing safety rail. The rail reads as the
                // platform-edge barrier instead of being duplicated or removed.
                b.Box(mats.Sidewalk, new Vector3(side * 5.46f, 0.17f, half), new Vector3(1.02f, 0.34f, SegmentLength));
                b.Box(mats.Metal, new Vector3(side * 4.97f, 0.19f, half), new Vector3(0.09f, 0.38f, SegmentLength));
                b.Box(mats.NeonAlt, new Vector3(side * 4.925f, 0.385f, half), new Vector3(0.05f, 0.02f, SegmentLength));
                b.Box(mats.Paint, new Vector3(side * 5.18f, 0.366f, half), new Vector3(0.18f, 0.012f, SegmentLength));
                b.Box(mats.Metal, new Vector3(side * 5.86f, 0.37f, half), new Vector3(0.14f, 0.1f, SegmentLength));

                for (int i = 0; i < 3; i++)
                {
                    float z = 6f + i * 16f;
                    b.Box(mats.Metal, new Vector3(side * 5.58f, 1.95f, z), new Vector3(0.24f, 3.55f, 0.24f));
                    b.Box(mats.Trim, new Vector3(side * 5.58f, 0.38f, z), new Vector3(0.42f, 0.12f, 0.42f));
                    b.Box(mats.Trim, new Vector3(side * 5.58f, 3.72f, z), new Vector3(0.38f, 0.1f, 0.38f));
                    b.Box(mats.Neon, new Vector3(side * 5.44f, 2.0f, z), new Vector3(0.06f, 2.4f, 0.08f));
                    b.Box(mats.NeonAlt, new Vector3(side * 5.44f, 0.7f, z), new Vector3(0.04f, 0.25f, 0.08f));
                    b.Box(mats.Metal, new Vector3(side * 5.55f, 3.92f, z), new Vector3(0.34f, 0.06f, 0.34f));
                }

                // Bench, litter bin and platform light pools.
                for (int i = 0; i < 2; i++)
                {
                    float z = 12f + i * 20f;
                    b.Box(mats.Metal, new Vector3(side * 5.52f, 0.58f, z), new Vector3(0.62f, 0.1f, 1.55f));
                    b.Box(mats.Metal, new Vector3(side * 5.82f, 0.95f, z), new Vector3(0.1f, 0.5f, 1.55f));
                    b.Box(mats.Metal, new Vector3(side * 5.52f, 0.4f, z - 0.62f), new Vector3(0.1f, 0.45f, 0.1f));
                    b.Box(mats.Metal, new Vector3(side * 5.52f, 0.4f, z + 0.62f), new Vector3(0.1f, 0.45f, 0.1f));
                    b.Box(mats.NeonAlt, new Vector3(side * 5.88f, 0.55f, z + 2.4f), new Vector3(0.16f, 0.42f, 0.3f));
                    b.Quad(mats.WetGlow, new Vector3(side * 5.25f, 0.374f, z),
                        new Vector3(1.2f, 2.4f, 1f), Quaternion.Euler(90f, 0f, 0f));
                }

                // Station signage and route maps on the platform wall.
                b.Box(mats.Sign, new Vector3(side * 5.78f, 3.1f, 15f), new Vector3(0.06f, 0.85f, 3.0f));
                b.Box(mats.Metal, new Vector3(side * 5.82f, 3.1f, 15f), new Vector3(0.07f, 0.98f, 3.22f));
                b.Box(mats.Billboard, new Vector3(side * 5.82f, 2.15f, 33f), new Vector3(0.06f, 1.25f, 2.4f));
                b.Box(mats.Neon, new Vector3(side * 5.79f, 2.15f, 33f), new Vector3(0.025f, 0.06f, 2.0f));

                // Pipes, clamps and junction boxes.
                b.Cylinder(mats.Pipe, new Vector3(side * 5.45f, 5.65f, half), new Vector3(0.12f, 21f, 0.12f),
                    Quaternion.Euler(90f, 0f, 0f));
                b.Cylinder(mats.Metal, new Vector3(side * 5.18f, 5.38f, half), new Vector3(0.06f, 21f, 0.06f),
                    Quaternion.Euler(90f, 0f, 0f));
                for (int i = 0; i < 6; i++)
                {
                    float z = 4f + i * 8f;
                    b.Box(mats.Metal, new Vector3(side * 5.45f, 5.65f, z), new Vector3(0.2f, 0.28f, 0.1f));
                    b.Box(mats.Trim, new Vector3(side * 5.78f, 4.95f, z + 3f), new Vector3(0.14f, 0.7f, 0.42f));
                }
            }
            b.Box(mats.GroundDark, new Vector3(0f, 7f, half), new Vector3(13f, 1f, SegmentLength));
            for (int i = 0; i < 4; i++)
            {
                float z = 6f + i * 10f;
                b.Box(mats.Metal, new Vector3(0f, 5.2f, z), new Vector3(13.2f, 0.35f, 0.6f));
                b.Box(mats.Neon, new Vector3(0f, 5.02f, z), new Vector3(7f, 0.06f, 0.2f));
                b.Box(mats.NeonAlt, new Vector3(-4.6f, 4.2f, z), new Vector3(0.1f, 0.2f, 0.5f));
                b.Box(mats.Metal, new Vector3(0f, 6.2f, z), new Vector3(12f, 0.08f, 0.12f));
            }

            // Suspended station name and departure board over the tracks.
            b.Box(mats.Metal, new Vector3(0f, 6.1f, 20f), new Vector3(9.4f, 0.08f, 0.08f));
            b.Box(mats.Sign, new Vector3(0f, 5.55f, 20f), new Vector3(4.8f, 0.72f, 0.08f));
            b.Box(mats.Neon, new Vector3(0f, 5.15f, 20f), new Vector3(3.9f, 0.05f, 0.06f));
            b.Box(mats.Metal, new Vector3(-2.1f, 5.85f, 20f), new Vector3(0.06f, 1.1f, 0.06f));
            b.Box(mats.Metal, new Vector3(2.1f, 5.85f, 20f), new Vector3(0.06f, 1.1f, 0.06f));
            b.Box(mats.Sign, new Vector3(0f, 4.55f, 36f), new Vector3(3.3f, 0.5f, 0.08f));
            b.Box(mats.Metal, new Vector3(0f, 4.86f, 36f), new Vector3(3.5f, 0.06f, 0.08f));
            for (int side = -1; side <= 1; side += 2)
            {
                b.Quad(mats.WetGlow, new Vector3(side * 5.25f, 0.374f, 38f),
                    new Vector3(1.1f, 2.8f, 1f), Quaternion.Euler(90f, 0f, 0f));
            }
        }

        private static void BuildGateway(DecorMeshBuilder b, ThemeMaterials mats, DeterministicRandom rng)
        {
            float z = 5f;
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(mats.Metal, new Vector3(side * 5.25f, 2.7f, z), new Vector3(0.3f, 5.4f, 0.3f));
                b.Box(mats.Neon, new Vector3(side * 5.08f, 2.9f, z), new Vector3(0.05f, 4.6f, 0.18f));
                b.Box(mats.SignAlt, new Vector3(side * 5.4f, 3.1f, z - 0.18f), new Vector3(0.08f, 2.6f, 0.55f));
                b.Box(mats.Metal, new Vector3(side * 4.9f, 4.9f, z), new Vector3(0.65f, 0.16f, 0.65f));
            }
            b.Box(mats.Metal, new Vector3(0f, 5.45f, z), new Vector3(10.8f, 0.5f, 0.5f));
            b.Box(mats.Neon, new Vector3(0f, 5.15f, z), new Vector3(8.4f, 0.06f, 0.12f));
            b.Box(mats.Billboard, new Vector3(0f, 5.55f, z - 0.28f), new Vector3(4.4f, 0.25f, 0.05f));
            b.Box(mats.NeonAlt, new Vector3(0f, 6.15f, z), new Vector3(6.6f, 0.05f, 0.08f));
        }

        private static void BuildEventMarker(DecorMeshBuilder b, ThemeMaterials mats)
        {
            b.Box(mats.NeonAlt, new Vector3(0f, 0.04f, 38f), new Vector3(9.0f, 0.02f, 0.2f));
            b.Box(mats.Neon, new Vector3(0f, 0.05f, 36.8f), new Vector3(9.0f, 0.02f, 0.06f));
        }

        private void SpawnObstacles(Segment segment)
        {
            var plan = segment.Plan;
            var rng = new DeterministicRandom(plan.Seed ^ 0x0BADC0DE);
            for (int r = 0; r < plan.Rows.Length; r++)
            {
                var row = plan.Rows[r];
                float z = r * RowSpacing + RowSpacing * 0.5f;
                for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                {
                    CellKind cell = row[lane];
                    if (cell == CellKind.Empty) continue;
                    float x = LaneMath.LaneToX(lane);
                    ObstacleKind kind;
                    float width = 2.08f, depth = 1.0f, minY = 0f, maxY = 1f;
                    float move = 0f, speed = 0f, phase = 0f, spin = 0f;
                    switch (cell)
                    {
                        case CellKind.LowBarrier:
                            kind = ObstacleKind.JumpBarrier; depth = 0.85f; minY = 0f; maxY = 0.99f;
                            break;
                        case CellKind.OverheadGate:
                            kind = ObstacleKind.OverheadLaser; depth = 0.9f; minY = 1.32f; maxY = 2.55f;
                            break;
                        case CellKind.Electric:
                            kind = ObstacleKind.ElectricPad; depth = 2.0f; minY = 0f; maxY = 0.24f;
                            break;
                        case CellKind.Drone:
                            kind = ObstacleKind.HoverDrone; depth = 1.2f; minY = 1.14f; maxY = 2.3f;
                            break;
                        case CellKind.MovingBlocker:
                            kind = ObstacleKind.MovingBlocker; depth = 1.1f; minY = 0f; maxY = 2.0f;
                            move = 0.55f; speed = 0.9f; phase = rng.Range(0f, 6.28f);
                            break;
                        default: // Solid
                            if (rng.Chance(0.3f)) { kind = ObstacleKind.LaserWall; depth = 0.65f; minY = 0f; maxY = 2.35f; }
                            else { kind = ObstacleKind.CargoCrate; depth = 1.7f; minY = 0f; maxY = 1.92f; }
                            if (rng.Chance(0.12f)) { kind = ObstacleKind.RotatingBar; depth = 2.0f; minY = 0f; maxY = 2.25f; spin = 140f; }
                            break;
                    }
                    var obstacle = _obstaclePool.Spawn(kind, segment.Root.transform,
                        new Vector3(x, 0f, z), lane, width, depth, minY, maxY, move, speed, phase, spin);
                    segment.Obstacles.Add(obstacle);
                }
            }
        }

        private void SpawnPickups(Segment segment)
        {
            var plan = segment.Plan;
            var rng = new DeterministicRandom(plan.Seed ^ 0x51ED17);
            bool coinRush = _mode != null && _mode.Id == GameModeId.CoinRush;
            bool bonus = plan.IsBonus || plan.IsSafe;

            for (int r = 0; r < plan.Rows.Length; r++)
            {
                TrackRow row = plan.Rows[r];
                bool fullAction = IsFullWidthAction(row);
                var passable = new List<int>();
                var empty = new List<int>();
                for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                {
                    if (!row.IsSolid(lane)) passable.Add(lane);
                    if (row[lane] == CellKind.Empty) empty.Add(lane);
                }
                if (passable.Count == 0) continue;

                bool wantsLaneCoins = bonus || coinRush || rng.Chance(0.72f);
                if (wantsLaneCoins)
                {
                    int lanesToCoin = coinRush ? Mathf.Min(3, passable.Count) : (bonus ? Mathf.Min(2, passable.Count) : 1);
                    for (int n = 0; n < lanesToCoin; n++)
                    {
                        int lane = empty.Count > 0 && n == 0 ? empty[rng.Range(0, empty.Count)] : passable[rng.Range(0, passable.Count)];
                        CellKind cell = row[lane];
                        float x = LaneMath.LaneToX(lane);
                        if (cell == CellKind.LowBarrier || cell == CellKind.Electric)
                        {
                            // A readable arc rewards a well-timed jump.
                            float[] heights = { 1.2f, 1.75f, 2.15f, 1.75f, 1.2f };
                            for (int i = 0; i < heights.Length; i++)
                            {
                                float z = r * RowSpacing + 1.5f + i * 1.05f;
                                AddCredit(segment, new Vector3(x, heights[i], z), rng);
                            }
                        }
                        else
                        {
                            float y = cell == CellKind.OverheadGate || cell == CellKind.Drone ? 0.62f : 0.92f;
                            for (int i = 0; i < (coinRush ? 3 : 2); i++)
                            {
                                float z = r * RowSpacing + 1.6f + i * 2.3f;
                                AddCredit(segment, new Vector3(x, y, z), rng);
                            }
                        }
                    }
                }
                else if (fullAction && rng.Chance(0.45f))
                {
                    int lane = passable[rng.Range(0, passable.Count)];
                    float x = LaneMath.LaneToX(lane);
                    float y = row[lane] == CellKind.OverheadGate ? 0.6f : 1.4f;
                    for (int i = 0; i < 3; i++)
                        AddCredit(segment, new Vector3(x, y, r * RowSpacing + 1.7f + i * 1.5f), rng);
                }
            }

            // Rare prism shard near an open path.
            if (rng.Chance(plan.IsBonus ? 0.35f : 0.12f))
            {
                int lane = FindOpenLane(plan, rng.Range(0, plan.Rows.Length), rng);
                if (lane >= 0)
                {
                    var prism = _pickupPool.SpawnCredit(segment.Root.transform,
                        new Vector3(LaneMath.LaneToX(lane), 1.25f, rng.Range(4f, SegmentLength - 4f)), rng.Range(0f, 8f), true);
                    segment.Credits.Add(prism);
                }
            }

            // Power-up frequency. Slow-motion is converted to score in online races.
            bool tutorialGuarantee = _mode != null && _mode.Id == GameModeId.Tutorial && plan.Index == 2;
            float chance = tutorialGuarantee ? 1f : (plan.IsSafe ? 0.23f : 0.29f);
            if (coinRush) chance += 0.15f;
            if (rng.Chance(chance))
            {
                var definition = PickPowerUp(rng, GameModes.IsOnline(_mode.Id));
                if (definition != null)
                {
                    int row = FindOpenRow(plan, rng);
                    int lane = FindOpenLane(plan, row, rng);
                    if (lane >= 0)
                    {
                        float z = row * RowSpacing + rng.Range(2f, 5f);
                        var pickup = _pickupPool.SpawnPowerUp(segment.Root.transform, definition,
                            new Vector3(LaneMath.LaneToX(lane), 1.2f, z), rng.Range(0f, 6.28f));
                        segment.PowerUps.Add(pickup);
                    }
                }
            }
        }

        private static bool IsFullWidthAction(TrackRow row)
        {
            if (row.Left == CellKind.LowBarrier && row.Centre == CellKind.LowBarrier && row.Right == CellKind.LowBarrier) return true;
            if (row.Left == CellKind.OverheadGate && row.Centre == CellKind.OverheadGate && row.Right == CellKind.OverheadGate) return true;
            if (row.Left == CellKind.Electric && row.Centre == CellKind.Electric && row.Right == CellKind.Electric) return true;
            return false;
        }

        private void AddCredit(Segment segment, Vector3 localPosition, DeterministicRandom rng)
        {
            bool prism = rng.Chance(0.018f);
            var pickup = _pickupPool.SpawnCredit(segment.Root.transform, localPosition, rng.Range(0f, 8f), prism);
            segment.Credits.Add(pickup);
        }

        private PowerUpDefinition PickPowerUp(DeterministicRandom rng, bool online)
        {
            float total = 0f;
            var defs = _config.powerUps;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d == null) continue;
                if (online && !d.enabledInRaces) continue;
                total += Mathf.Max(0f, d.spawnWeight);
            }
            if (total <= 0f) return null;
            float roll = rng.Range(0f, total);
            float cursor = 0f;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d == null || (online && !d.enabledInRaces)) continue;
                cursor += Mathf.Max(0f, d.spawnWeight);
                if (roll <= cursor) return d;
            }
            return defs[defs.Count - 1];
        }

        private int FindOpenLane(SegmentPlan plan, int row, DeterministicRandom rng)
        {
            if (plan.Rows.Length == 0) return 1;
            row = Mathf.Clamp(row, 0, plan.Rows.Length - 1);
            var rowData = plan.Rows[row];
            var candidates = new List<int>();
            for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                if (rowData[lane] == CellKind.Empty) candidates.Add(lane);
            return candidates.Count == 0 ? -1 : candidates[rng.Range(0, candidates.Count)];
        }

        private int FindOpenRow(SegmentPlan plan, DeterministicRandom rng)
        {
            int start = rng.Range(0, Mathf.Max(1, plan.Rows.Length));
            for (int i = 0; i < plan.Rows.Length; i++)
            {
                int row = (start + i) % plan.Rows.Length;
                for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                    if (plan.Rows[row][lane] == CellKind.Empty) return row;
            }
            return 0;
        }

        // ---- Runtime queries -------------------------------------------------------------

        public void CollectObstacles(float zMin, float zMax, List<Obstacle> results)
        {
            results.Clear();
            for (int i = 0; i < _activeList.Count; i++)
            {
                var segment = _activeList[i];
                if (segment?.Root == null) continue;
                float start = segment.Root.transform.position.z;
                if (zMax < start - 2f || zMin > start + SegmentLength + 2f) continue;
                var list = segment.Obstacles;
                for (int j = 0; j < list.Count; j++)
                {
                    var obstacle = list[j];
                    if (obstacle != null && obstacle.Active) results.Add(obstacle);
                }
            }
        }

        public void CollectCredits(float zMin, float zMax, List<EnergyPickup> results)
        {
            results.Clear();
            for (int i = 0; i < _activeList.Count; i++)
            {
                var segment = _activeList[i];
                if (segment?.Root == null) continue;
                float start = segment.Root.transform.position.z;
                if (zMax < start - 4f || zMin > start + SegmentLength + 4f) continue;
                var list = segment.Credits;
                for (int j = 0; j < list.Count; j++)
                {
                    var pickup = list[j];
                    if (pickup != null && !pickup.Collected && pickup.gameObject.activeSelf) results.Add(pickup);
                }
            }
        }

        public void CollectPowerUps(float zMin, float zMax, List<PowerUpPickup> results)
        {
            results.Clear();
            for (int i = 0; i < _activeList.Count; i++)
            {
                var segment = _activeList[i];
                if (segment?.Root == null) continue;
                float start = segment.Root.transform.position.z;
                if (zMax < start - 4f || zMin > start + SegmentLength + 4f) continue;
                var list = segment.PowerUps;
                for (int j = 0; j < list.Count; j++)
                {
                    var pickup = list[j];
                    if (pickup != null && !pickup.Collected && pickup.gameObject.activeSelf) results.Add(pickup);
                }
            }
        }

        public ThemeDefinition CurrentTheme
        {
            get
            {
                var segment = GetSegmentAtDistance(0f);
                return segment == null ? _config.Theme(0) : _config.Theme(segment.Plan.BiomeIndex);
            }
        }

        public SegmentPlan CurrentPlan
        {
            get
            {
                var segment = GetSegmentAtDistance(0f);
                return segment?.Plan;
            }
        }

        private Segment GetSegmentAtDistance(float distance)
        {
            if (_activeList.Count == 0) return null;
            int index = Mathf.FloorToInt(Mathf.Max(0f, distance) / SegmentLength);
            if (_active.TryGetValue(index, out Segment exact)) return exact;
            Segment nearest = null;
            int best = int.MaxValue;
            for (int i = 0; i < _activeList.Count; i++)
            {
                int delta = Mathf.Abs(_activeList[i].Index - index);
                if (delta < best)
                {
                    best = delta;
                    nearest = _activeList[i];
                }
            }
            return nearest;
        }

        /// <summary>Removes hazards around a world-space position. Used for head-start safety and practice resets.</summary>
        public void ClearHazardsAround(float worldZ, float radius)
        {
            for (int i = 0; i < _activeList.Count; i++)
            {
                var segment = _activeList[i];
                if (segment?.Root == null) continue;
                float start = segment.Root.transform.position.z;
                if (worldZ + radius < start || worldZ - radius > start + SegmentLength) continue;
                for (int j = segment.Obstacles.Count - 1; j >= 0; j--)
                {
                    var obstacle = segment.Obstacles[j];
                    if (obstacle == null) { segment.Obstacles.RemoveAt(j); continue; }
                    if (Mathf.Abs(obstacle.transform.position.z - worldZ) <= radius)
                    {
                        _obstaclePool.Return(obstacle);
                        segment.Obstacles.RemoveAt(j);
                    }
                }
            }
        }

        /// <summary>Clears collected pickups so a segment can be safely reused.</summary>
        public void ResetPickupsInActiveSegments()
        {
            for (int i = 0; i < _activeList.Count; i++)
            {
                var segment = _activeList[i];
                for (int j = 0; j < segment.Credits.Count; j++)
                {
                    if (segment.Credits[j] != null) segment.Credits[j].ResetPickup();
                }
                for (int j = 0; j < segment.PowerUps.Count; j++)
                {
                    if (segment.PowerUps[j] != null) segment.PowerUps[j].ResetPickup();
                }
            }
        }

        public bool TryGetUpcomingHazard(float fromZ, float lookAhead, out ObstacleKind kind, out int lane, out float z)
        {
            kind = ObstacleKind.JumpBarrier;
            lane = 1;
            z = float.MaxValue;
            bool found = false;
            for (int i = 0; i < _activeList.Count; i++)
            {
                var segment = _activeList[i];
                if (segment?.Root == null) continue;
                for (int j = 0; j < segment.Obstacles.Count; j++)
                {
                    var obstacle = segment.Obstacles[j];
                    if (obstacle == null || !obstacle.Active) continue;
                    float oz = obstacle.transform.position.z;
                    if (oz < fromZ + 6f || oz > fromZ + lookAhead) continue;
                    if (!found || oz < z)
                    {
                        found = true;
                        z = oz;
                        kind = obstacle.Kind;
                        lane = obstacle.Lane;
                    }
                }
            }
            return found;
        }

        // ---- Decor mesh builder ----------------------------------------------------------

        private sealed class DecorMeshBuilder
        {
            private readonly Dictionary<Material, List<CombineInstance>> _groups =
                new Dictionary<Material, List<CombineInstance>>();

            public const float FacadeTileWidth = 10.4f; // 4 window bays per texture tile.
            public const float FacadeTileHeight = 12f;  // 4 floors per texture tile.

            public void Box(Material mat, Vector3 position, Vector3 scale, Quaternion? rotation = null) =>
                Add(PrimitiveMeshes.Cube, mat, position, scale, rotation ?? Quaternion.identity);

            public void Cylinder(Material mat, Vector3 position, Vector3 scale, Quaternion? rotation = null) =>
                Add(PrimitiveMeshes.Cylinder, mat, position, scale, rotation ?? Quaternion.identity);

            public void Foliage(Material mat, Vector3 position, Vector3 scale, Quaternion? rotation = null) =>
                Add(PrimitiveMeshes.Foliage, mat, position, scale, rotation ?? Quaternion.identity);

            /// <summary>Flat textured card used for soft wet-light reflections and signage glow.</summary>
            public void Quad(Material mat, Vector3 position, Vector3 scale, Quaternion? rotation = null) =>
                Add(PrimitiveMeshes.Quad, mat, position, scale, rotation ?? Quaternion.identity);

            /// <summary>
            /// Building body with world-scaled window UVs. A regular scaled cube stretches one
            /// 4x4 window atlas over every face, which made small shops and tall towers use the
            /// same giant window module. This mesh repeats the atlas by real bay/floor size.
            /// </summary>
            public void Facade(Material mat, Vector3 position, Vector3 size, Vector2 uvOffset,
                Quaternion? rotation = null)
            {
                if (mat == null) return;
                var vertices = new List<Vector3>(24);
                var normals = new List<Vector3>(24);
                var uvs = new List<Vector2>(24);
                var triangles = new List<int>(36);
                float ux = size.x / FacadeTileWidth;
                float uy = size.y / FacadeTileHeight;
                float uz = size.z / FacadeTileWidth;
                Vector3 h = size * 0.5f;

                AddFacadeFace(vertices, normals, uvs, triangles,
                    new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z),
                    new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z),
                    Vector3.forward, new Vector2(ux, uy), uvOffset);
                AddFacadeFace(vertices, normals, uvs, triangles,
                    new Vector3(h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, -h.z),
                    new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z),
                    Vector3.back, new Vector2(ux, uy), uvOffset);
                AddFacadeFace(vertices, normals, uvs, triangles,
                    new Vector3(h.x, -h.y, h.z), new Vector3(h.x, -h.y, -h.z),
                    new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z),
                    Vector3.right, new Vector2(uz, uy), uvOffset);
                AddFacadeFace(vertices, normals, uvs, triangles,
                    new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, h.z),
                    new Vector3(-h.x, h.y, h.z), new Vector3(-h.x, h.y, -h.z),
                    Vector3.left, new Vector2(uz, uy), uvOffset);
                AddFacadeFace(vertices, normals, uvs, triangles,
                    new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z),
                    new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z),
                    Vector3.up, new Vector2(ux, uz), uvOffset);
                AddFacadeFace(vertices, normals, uvs, triangles,
                    new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z),
                    new Vector3(h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, -h.z),
                    Vector3.down, new Vector2(ux, uz), uvOffset);

                var mesh = new Mesh { name = "NV_FacadeBox" };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0, false);
                mesh.RecalculateBounds();
                Add(mesh, mat, position, Vector3.one, rotation ?? Quaternion.identity);
            }

            private static void AddFacadeFace(List<Vector3> vertices, List<Vector3> normals,
                List<Vector2> uvs, List<int> triangles, Vector3 p0, Vector3 p1, Vector3 p2,
                Vector3 p3, Vector3 normal, Vector2 uvScale, Vector2 uvOffset)
            {
                int start = vertices.Count;
                vertices.Add(p0); vertices.Add(p1); vertices.Add(p2); vertices.Add(p3);
                for (int i = 0; i < 4; i++) normals.Add(normal);
                uvs.Add(uvOffset);
                uvs.Add(uvOffset + new Vector2(uvScale.x, 0f));
                uvs.Add(uvOffset + uvScale);
                uvs.Add(uvOffset + new Vector2(0f, uvScale.y));
                bool flip = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normal) < 0f;
                if (!flip)
                {
                    triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                    triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
                }
                else
                {
                    triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                    triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
                }
            }

            private void Add(Mesh mesh, Material mat, Vector3 position, Vector3 scale, Quaternion rotation)
            {
                if (mat == null) return;
                if (!_groups.TryGetValue(mat, out var list))
                {
                    list = new List<CombineInstance>();
                    _groups[mat] = list;
                }
                list.Add(new CombineInstance
                {
                    mesh = mesh,
                    transform = Matrix4x4.TRS(position, rotation, scale)
                });
            }

            public Mesh Build(Transform root, bool glowPass, out Material[] materials)
            {
                // Build the merged vertex streams ourselves. Mesh.CombineMeshes' two-stage
                // merge-submeshes-then-combine path produces zeroed vertex positions in macOS
                // player builds even though the source primitives are readable. A direct copy
                // with baked matrices is both deterministic and player-safe.
                var mats = new List<Material>();
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var subTriangles = new List<List<int>>();

                foreach (var kv in _groups)
                {
                    if (kv.Value.Count == 0) continue;
                    // Transparent additive cards live above the opaque range. They are built
                    // into a separate renderer whose shadow casting is disabled.
                    bool transparent = kv.Key.renderQueue >= 2500;
                    if (transparent != glowPass) continue;
                    mats.Add(kv.Key);
                    var triangles = new List<int>();
                    for (int instanceIndex = 0; instanceIndex < kv.Value.Count; instanceIndex++)
                    {
                        CombineInstance instance = kv.Value[instanceIndex];
                        Mesh source = instance.mesh;
                        if (source == null) continue;

                        Vector3[] sourceVertices = source.vertices;
                        Vector3[] sourceNormals = source.normals;
                        Vector2[] sourceUvs = source.uv;
                        int[] sourceTriangles = source.triangles;
                        Matrix4x4 matrix = instance.transform;
                        Matrix4x4 normalMatrix = matrix.inverse.transpose;
                        bool flip = matrix.determinant < 0f;
                        int firstVertex = vertices.Count;

                        for (int i = 0; i < sourceVertices.Length; i++)
                        {
                            vertices.Add(matrix.MultiplyPoint3x4(sourceVertices[i]));
                            normals.Add(sourceNormals.Length == sourceVertices.Length
                                ? normalMatrix.MultiplyVector(sourceNormals[i]).normalized
                                : Vector3.up);
                            uvs.Add(sourceUvs.Length == sourceVertices.Length ? sourceUvs[i] : Vector2.zero);
                        }

                        for (int i = 0; i + 2 < sourceTriangles.Length; i += 3)
                        {
                            triangles.Add(firstVertex + sourceTriangles[i]);
                            if (flip)
                            {
                                triangles.Add(firstVertex + sourceTriangles[i + 2]);
                                triangles.Add(firstVertex + sourceTriangles[i + 1]);
                            }
                            else
                            {
                                triangles.Add(firstVertex + sourceTriangles[i + 1]);
                                triangles.Add(firstVertex + sourceTriangles[i + 2]);
                            }
                        }
                    }
                    subTriangles.Add(triangles);
                }

                if (mats.Count == 0 || vertices.Count == 0)
                {
                    materials = Array.Empty<Material>();
                    return null;
                }

                var final = new Mesh { name = glowPass ? "NV_SegmentGlow" : "NV_SegmentDecor" };
                if (vertices.Count > 65535) final.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                final.SetVertices(vertices);
                final.SetNormals(normals);
                final.SetUVs(0, uvs);
                final.subMeshCount = mats.Count;
                for (int i = 0; i < mats.Count; i++)
                    final.SetTriangles(subTriangles[i], i, false);
                final.RecalculateTangents();
                final.RecalculateBounds();
                materials = mats.ToArray();
                return final;
            }
        }
    }
}
