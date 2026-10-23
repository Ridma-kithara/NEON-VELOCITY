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
            public SegmentPlan Plan;
            public readonly List<Obstacle> Obstacles = new List<Obstacle>();
            public readonly List<EnergyPickup> Credits = new List<EnergyPickup>();
            public readonly List<PowerUpPickup> PowerUps = new List<PowerUpPickup>();
        }

        private sealed class ThemeMaterials
        {
            public Material Ground;
            public Material GroundDark;
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
            public Material Roof;
            public Material Billboard;
            public Material Sign;
            public Material SignAlt;
            public Material Pipe;
            public Material White;
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
                var signA = TextureFactory.SignBoard(1201 + i * 7, theme.accent, i);
                var signB = TextureFactory.SignBoard(4401 + i * 13, Palette.Magenta, i + 1);
                var m = new ThemeMaterials
                {
                    Ground = MaterialLibrary.LitTextured("NV_AsphaltMat_" + i, asphalt,
                        Color.Lerp(theme.ground, Color.white, 0.24f), 0.22f, 0.94f,
                        new Vector2(2.4f, 10f), asphaltNormal, new Vector2(2.4f, 10f), 1.05f),
                    GroundDark = MaterialLibrary.LitTextured("NV_DeckMat_" + i, concrete,
                        Color.Lerp(theme.ground, Palette.Void, 0.48f), 0.25f, 0.42f,
                        new Vector2(2f, 8f)),
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
                    Billboard = MaterialLibrary.UnlitTextured("NV_SignA_" + i, signA,
                        new Color(1.05f, 1.05f, 1.05f, 1f), Vector2.one),
                    Sign = MaterialLibrary.UnlitTextured("NV_SignB_" + i, signB,
                        new Color(1.05f, 1.05f, 1.05f, 1f), Vector2.one),
                    SignAlt = MaterialLibrary.UnlitTextured("NV_SignC_" + i,
                        TextureFactory.SignBoard(9901 + i * 5, theme.accent, i + 2),
                        new Color(1f, 1f, 1f, 1f), Vector2.one),
                    Pipe = MaterialLibrary.Lit(Color.Lerp(theme.buildingA, theme.accent, 0.12f), 0.8f, 0.7f),
                    White = MaterialLibrary.Lit(Palette.White, 0.2f, 0.8f, Palette.White, 1.2f)
                };
                // Window facades are HDR-tinted so scattered lit windows bleed into bloom while
                // the unlit mullions stay deep and the silhouette remains readable at speed.
                var texA = TextureFactory.WindowFacade(3301 + i);
                var texB = TextureFactory.WindowFacade(9901 + i);
                Color facadeATint = Color.Lerp(theme.buildingA, Color.white, 0.62f) * 1.42f;
                Color facadeBTint = Color.Lerp(theme.buildingB, Color.white, 0.58f) * 1.36f;
                m.BuildingA = MaterialLibrary.UnlitTextured("NV_WinA_" + i, texA,
                    facadeATint, new Vector2(2f, 3f));
                m.BuildingB = MaterialLibrary.UnlitTextured("NV_WinB_" + i, texB,
                    facadeBTint, new Vector2(3f, 2f));
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
            return new Segment { Root = root, DecorFilter = filter, DecorRenderer = renderer };
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

            segment.DecorMesh = builder.Build(segment.Root.transform, out Material[] materials);
            segment.DecorFilter.sharedMesh = segment.DecorMesh;
            segment.DecorRenderer.sharedMaterials = materials;
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

            for (int z = 10; z < SegmentLength; z += 12)
                b.Box(mats.GroundDark, new Vector3(0f, 0.007f, z), new Vector3(9.2f, 0.008f, 0.04f));

            if (!plan.IsSafe)
            {
                for (int i = 0; i < 2; i++)
                {
                    b.Box(mats.NeonAlt, new Vector3(0f, 0.025f, 1f + i * 4f), new Vector3(8.8f, 0.01f, 0.12f));
                }
            }
        }

        private static void BuildCity(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    float z = 1f + i * 14f + rng.Range(-1.5f, 1.5f);
                    float x = side * rng.Range(9.2f, 12.5f);
                    float width = rng.Range(4.4f, 6.8f);
                    float depth = rng.Range(8f, 12f);
                    float height = rng.Range(10f, 29f);
                    Material facade = i % 2 == 0 ? mats.BuildingA : mats.BuildingB;
                    int tiers = height > 19f ? 2 : 1;
                    float lowerHeight = tiers == 2 ? height * 0.68f : height;
                    float crownHeight = height - lowerHeight;
                    float upperWidth = width * 0.72f;
                    float upperDepth = depth * 0.78f;
                    b.Box(facade, new Vector3(x, lowerHeight * 0.5f - 0.2f, z),
                        new Vector3(width, lowerHeight, depth));
                    b.Box(mats.Roof, new Vector3(x, lowerHeight - 0.02f, z),
                        new Vector3(width + 0.34f, 0.3f, depth + 0.34f));
                    if (crownHeight > 0.5f)
                    {
                        b.Box(facade, new Vector3(x, lowerHeight + crownHeight * 0.5f - 0.2f, z),
                            new Vector3(upperWidth, crownHeight, upperDepth));
                        b.Box(mats.Roof, new Vector3(x, height + 0.08f, z),
                            new Vector3(upperWidth + 0.4f, 0.35f, upperDepth + 0.4f));
                    }
                    else
                    {
                        b.Box(mats.Roof, new Vector3(x, height + 0.08f, z),
                            new Vector3(width + 0.4f, 0.35f, depth + 0.4f));
                    }
                    float nearX = x - side * (width * 0.5f + 0.04f);
                    float upperNearX = x - side * (upperWidth * 0.5f + 0.04f);
                    b.Box(mats.Neon, new Vector3(upperNearX, lowerHeight + crownHeight * 0.56f, z),
                        new Vector3(0.065f, crownHeight * 0.64f, 0.12f));
                    b.Box(mats.Trim, new Vector3(nearX, lowerHeight + 0.1f, z - depth * 0.5f),
                        new Vector3(0.18f, 0.18f, 0.18f));
                    b.Box(mats.Trim, new Vector3(nearX, lowerHeight + 0.1f, z + depth * 0.5f),
                        new Vector3(0.18f, 0.18f, 0.18f));

                    // Ground-floor storefront, awning and bright product windows.
                    b.Box(mats.GroundDark, new Vector3(nearX - side * 0.06f, 1.4f, z),
                        new Vector3(0.14f, 2.8f, depth * 0.84f));
                    for (int store = 0; store < 2; store++)
                    {
                        float sz = z + (store - 0.5f) * depth * 0.42f;
                        b.Box(mats.Sign, new Vector3(nearX - side * 0.15f, 1.55f, sz),
                            new Vector3(0.08f, 1.35f, depth * 0.28f));
                        b.Box(mats.Metal, new Vector3(nearX - side * 0.19f, 1.55f, sz),
                            new Vector3(0.035f, 0.055f, depth * 0.29f));
                        b.Box(mats.Metal, new Vector3(nearX - side * 0.19f, 1.0f, sz),
                            new Vector3(0.035f, 0.045f, depth * 0.29f));
                        b.Quad(mats.WetGlow, new Vector3(nearX - side * 1.7f, 0.044f, sz),
                            new Vector3(1.4f, 2.5f, 1f), Quaternion.Euler(90f, 0f, 0f));
                    }
                    // Recessed doorway and a lit transom keep the storefront from reading as
                    // a flat repeating decal at speed.
                    b.Box(mats.GroundDark, new Vector3(nearX - side * 0.16f, 1.1f, z - depth * 0.3f),
                        new Vector3(0.12f, 2.2f, 1.25f));
                    b.Box(mats.Neon, new Vector3(nearX - side * 0.23f, 2.25f, z - depth * 0.3f),
                        new Vector3(0.025f, 0.1f, 0.8f));
                    b.Box(mats.Metal, new Vector3(nearX - side * 0.68f, 2.9f, z),
                        new Vector3(1.4f, 0.09f, depth * 0.64f));
                    b.Box(mats.NeonAlt, new Vector3(nearX - side * 1.34f, 2.82f, z),
                        new Vector3(0.055f, 0.05f, depth * 0.6f));

                    // Window ledges and balcony slabs give the block a readable human scale.
                    int floors = Mathf.Min(7, Mathf.FloorToInt(lowerHeight / 3.2f));
                    for (int floor = 1; floor <= floors; floor++)
                    {
                        b.Box(mats.Roof, new Vector3(nearX, floor * 3.2f, z),
                            new Vector3(0.24f, 0.11f, depth * 0.92f));
                        if (floor % 2 == 1)
                            b.Box(mats.Metal, new Vector3(nearX - side * 0.11f, floor * 3.2f + 0.12f, z),
                                new Vector3(0.04f, 0.035f, depth * 0.78f));
                    }
                    if (crownHeight > 3f)
                    {
                        for (int floor = 0; floor < 2; floor++)
                        {
                            float wy = lowerHeight + 2.3f + floor * 3.1f;
                            if (wy > height - 1.2f) break;
                            b.Box(mats.Roof, new Vector3(upperNearX, wy, z),
                                new Vector3(0.2f, 0.09f, upperDepth * 0.9f));
                            b.Box(mats.Neon, new Vector3(upperNearX - side * 0.02f, wy + 0.04f, z),
                                new Vector3(0.035f, 0.035f, upperDepth * 0.5f));
                        }
                        b.Box(mats.Metal, new Vector3(upperNearX, height - 0.35f, z),
                            new Vector3(0.06f, 0.06f, upperDepth * 0.88f));
                    }
                    if (rng.Chance(0.55f))
                    {
                        for (int floor = 1; floor <= 3; floor++)
                        {
                            b.Box(mats.Metal, new Vector3(nearX - side * 0.28f, floor * 3.2f - 1.1f, z),
                                new Vector3(0.46f, 0.1f, depth * 0.5f));
                            b.Box(mats.Neon, new Vector3(nearX - side * 0.5f, floor * 3.2f - 1.1f, z),
                                new Vector3(0.035f, 0.4f, depth * 0.42f));
                        }
                    }

                    float billboardY = crownHeight > 0.5f
                        ? lowerHeight + crownHeight * 0.5f
                        : height * 0.64f;
                    float billboardX = crownHeight > 0.5f ? upperNearX : nearX;
                    b.Box(mats.Neon, new Vector3(billboardX, billboardY, z - depth * 0.15f),
                        new Vector3(0.07f, Mathf.Max(2.2f, billboardY * 0.52f), 0.13f));
                    b.Box(mats.Billboard, new Vector3(billboardX - side * 0.05f, billboardY, z),
                        new Vector3(0.09f, 2.7f, depth * 0.54f));

                    // Blade sign projecting over the sidewalk.
                    if (rng.Chance(0.7f))
                    {
                        float signZ = z + rng.Range(-depth * 0.3f, depth * 0.3f);
                        float signY = 4.6f + rng.Range(0f, 3.2f);
                        float signX = nearX - side * 0.82f;
                        b.Box(mats.Metal, new Vector3(signX, signY + 1.55f, signZ),
                            new Vector3(0.22f, 0.12f, 1.22f));
                        b.Box(mats.SignAlt, new Vector3(signX - side * 0.09f, signY, signZ),
                            new Vector3(0.09f, 2.9f, 0.95f));
                        b.Box(mats.Neon, new Vector3(signX - side * 0.16f, signY - 1.55f, signZ),
                            new Vector3(0.035f, 0.45f, 0.76f));
                    }

                    // Rooftop HVAC, water tanks and antennas.
                    for (int unit = 0; unit < 2; unit++)
                    {
                        b.Box(mats.Metal, new Vector3(x + rng.Range(-width * 0.24f, width * 0.24f),
                                height + 0.65f, z + rng.Range(-depth * 0.24f, depth * 0.24f)),
                            new Vector3(1.15f, 0.85f, 1.35f));
                    }
                    if (rng.Chance(0.5f))
                    {
                        b.Cylinder(mats.Metal, new Vector3(x, height + 1.2f, z),
                            new Vector3(0.9f, 0.55f, 0.9f));
                        b.Box(mats.Neon, new Vector3(x, height + 1.8f, z), new Vector3(0.5f, 0.05f, 0.5f));
                    }
                    if (rng.Chance(0.55f))
                    {
                        b.Box(mats.Trim, new Vector3(x, height + 2.4f, z), new Vector3(0.09f, 4.8f, 0.09f));
                        b.Box(mats.NeonAlt, new Vector3(x, height + 4.8f, z), new Vector3(0.25f, 0.1f, 0.25f));
                    }

                    // Wet reflections around the storefront.
                    b.Box(mats.Puddle, new Vector3(nearX - side * 0.9f, 0.008f, z + depth * 0.3f),
                        new Vector3(1.4f, 0.012f, 2.8f));
                }

                // Street lamps just beyond the sidewalk.
                for (int i = 0; i < 3; i++)
                {
                    float z = 5f + i * 13f;
                    b.Box(mats.Metal, new Vector3(side * 6.55f, 2.3f, z), new Vector3(0.11f, 4.6f, 0.11f));
                    b.Box(mats.Metal, new Vector3(side * 6.05f, 4.5f, z), new Vector3(1.1f, 0.1f, 0.14f));
                    b.Box(mats.Neon, new Vector3(side * 5.55f, 4.44f, z), new Vector3(0.45f, 0.05f, 0.24f));
                    b.Quad(mats.WetGlow, new Vector3(side * 5.5f, 0.044f, z),
                        new Vector3(1.8f, 3.2f, 1f), Quaternion.Euler(90f, 0f, 0f));
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

                // Layered far skyline with emissive crowns.
                for (int i = 0; i < 3; i++)
                {
                    float farX = side * rng.Range(23f, 40f);
                    float farZ = rng.Range(-2f, SegmentLength + 2f);
                    float farH = rng.Range(20f, 56f);
                    float farW = rng.Range(6f, 13f);
                    float farD = rng.Range(6f, 14f);
                    Material facade = i % 2 == 0 ? mats.BuildingB : mats.BuildingA;
                    b.Box(facade, new Vector3(farX, farH * 0.5f, farZ), new Vector3(farW, farH, farD));
                    b.Box(mats.Neon, new Vector3(farX - side * (farW * 0.5f + 0.06f), farH * 0.62f, farZ),
                        new Vector3(0.1f, farH * 0.5f, 0.3f));
                    if (rng.Chance(0.5f))
                    {
                        b.Box(mats.Trim, new Vector3(farX, farH + 3f, farZ), new Vector3(0.1f, 6f, 0.1f));
                        b.Box(mats.NeonAlt, new Vector3(farX, farH + 6f, farZ), new Vector3(0.3f, 0.12f, 0.3f));
                    }
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
                    b.Box(mats.Neon, new Vector3(light * 4.1f, y - 0.32f, z), new Vector3(0.15f, 0.22f, 0.15f));
                    b.Box(mats.Metal, new Vector3(light * 4.1f, y - 0.16f, z), new Vector3(0.03f, 0.32f, 0.03f));
                }
            }

            // Hover lane pylons beyond the rails.
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 2; i++)
                {
                    float z = 8f + i * 19f;
                    b.Box(mats.Trim, new Vector3(side * 7.6f, 2.8f, z), new Vector3(0.16f, 5.6f, 0.16f));
                    b.Box(mats.Neon, new Vector3(side * 7.6f, 5.6f, z), new Vector3(0.55f, 0.12f, 0.55f));
                    b.Box(mats.NeonAlt, new Vector3(side * 7.6f, 0.08f, z), new Vector3(0.4f, 0.1f, 0.4f));
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
            b.Box(mats.Billboard, new Vector3(0f, 4.55f, 36f), new Vector3(3.3f, 0.5f, 0.08f));
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

            public void Box(Material mat, Vector3 position, Vector3 scale, Quaternion? rotation = null) =>
                Add(PrimitiveMeshes.Cube, mat, position, scale, rotation ?? Quaternion.identity);

            public void Cylinder(Material mat, Vector3 position, Vector3 scale, Quaternion? rotation = null) =>
                Add(PrimitiveMeshes.Cylinder, mat, position, scale, rotation ?? Quaternion.identity);

            /// <summary>Flat textured card used for soft wet-light reflections and signage glow.</summary>
            public void Quad(Material mat, Vector3 position, Vector3 scale, Quaternion? rotation = null) =>
                Add(PrimitiveMeshes.Quad, mat, position, scale, rotation ?? Quaternion.identity);

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

            public Mesh Build(Transform root, out Material[] materials)
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

                var final = new Mesh { name = "NV_SegmentDecor" };
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
