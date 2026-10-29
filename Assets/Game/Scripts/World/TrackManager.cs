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
            public Material Wood;
            public Material Sand;
            public Material Snow;
            public Material Rock;
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
        private readonly ThemeMaterials[] _locationMaterials = new ThemeMaterials[LocationCatalog.Count];
        private int _nextIndex;
        private float _worldOffset;
        private int _currentBiome = -1;
        private int _currentZone = -1;
        private float _lastDistance;

        public float WorldOffset => _worldOffset;
        public int CurrentBiome => _currentBiome;
        public int CurrentStyle => _currentBiome;
        public int CurrentLocation => _currentZone;
        public int CurrentZoneIndex => _currentZone;
        public float TrackDistance => _lastDistance;
        public string CurrentLocationName => _currentZone >= 0
            ? LocationCatalog.Get(_currentZone).displayName : "";
        public int ActiveSegmentCount => _active.Count;
        public int PooledObstacleCount { get; private set; }
        public int PooledPickupCount { get; private set; }
        public event Action<int> BiomeChanged;
        public event Action<int> LocationChanged;

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
            _currentZone = -1;
            _lastDistance = 0f;
        }

        public void SetMode(ModeSpec mode) => _mode = mode;

        private void BuildThemeMaterials()
        {
            // Four architectural texture families are shared by the fifteen districts. Each
            // district still gets its own palette, glow, glass and signage materials, which
            // keeps the world visually distinct without duplicating fifteen texture atlases.
            var asphalt = new Texture2D[4];
            var asphaltNormal = new Texture2D[4];
            var concrete = new Texture2D[4];
            var transit = new Texture2D[4];
            var adA = new Texture2D[4];
            var adB = new Texture2D[4];
            var shop = new Texture2D[4];
            var windowA = new Texture2D[4];
            var windowB = new Texture2D[4];
            var windowC = new Texture2D[4];
            for (int style = 0; style < 4; style++)
            {
                asphalt[style] = TextureFactory.Asphalt(7701 + style);
                asphaltNormal[style] = TextureFactory.AsphaltNormal(7701 + style);
                concrete[style] = TextureFactory.Concrete(8801 + style);
                transit[style] = TextureFactory.SignBoard(4401 + style * 13, Palette.Magenta, style + 1);
                adA[style] = TextureFactory.BillboardArt(1201 + style * 7, Palette.Cyan, style);
                adB[style] = TextureFactory.BillboardArt(8801 + style * 11, Palette.Magenta, style + 1);
                shop[style] = TextureFactory.ShopWindow(2101 + style * 17, Palette.Cyan, style);
                windowA[style] = TextureFactory.WindowFacade(3301 + style * 11);
                windowB[style] = TextureFactory.WindowFacade(9901 + style * 17);
                windowC[style] = TextureFactory.WindowFacade(6601 + style * 23, 1);
            }

            for (int index = 0; index < LocationCatalog.Count; index++)
            {
                var location = LocationCatalog.Get(index);
                var theme = _config.LocationTheme(index) ?? _config.Theme(index);
                if (theme == null || location == null) continue;
                int style = Mathf.Abs(location.style) % 4;
                string key = "_" + location.id;
                float wet = location.rain ? 0.34f : 0.2f;
                var m = new ThemeMaterials
                {
                    Ground = MaterialLibrary.LitTextured("NV_AsphaltMat" + key, asphalt[style],
                        Color.Lerp(theme.ground, Color.white, 0.24f), 0.22f, 0.94f,
                        new Vector2(2.4f, 10f), asphaltNormal[style], new Vector2(2.4f, 10f), 1.05f),
                    GroundDark = MaterialLibrary.LitTextured("NV_DeckMat" + key, concrete[style],
                        Color.Lerp(theme.ground, Palette.Void, 0.48f), 0.25f, 0.42f,
                        new Vector2(2f, 8f)),
                    Podium = MaterialLibrary.LitTextured("NV_PodiumMat" + key, concrete[style],
                        Color.Lerp(theme.buildingA, Color.white, 0.24f), 0.09f, 0.56f,
                        new Vector2(1.8f, 2.4f)),
                    Sidewalk = MaterialLibrary.LitTextured("NV_SidewalkMat" + key, concrete[style],
                        Color.Lerp(theme.buildingA, Color.white, 0.18f), 0.12f, 0.5f,
                        new Vector2(2f, 12f)),
                    Puddle = MaterialLibrary.Lit(Color.Lerp(theme.skyBottom, Palette.Void, 0.45f), 0.92f, 0.99f),
                    WetGlow = MaterialLibrary.UnlitAdditiveTextured("NV_WetGlow" + key,
                        TextureFactory.RadialGlow(64),
                        new Color(theme.accent.r, theme.accent.g, theme.accent.b, wet), Vector2.one, false, -30),
                    WetGlowAlt = MaterialLibrary.UnlitAdditiveTextured("NV_WetGlowAlt" + key,
                        TextureFactory.RadialGlow(64),
                        new Color(Palette.Magenta.r, Palette.Magenta.g, Palette.Magenta.b, wet * 0.72f),
                        Vector2.one, false, -30),
                    Metal = MaterialLibrary.Lit(Color.Lerp(theme.buildingA, Palette.White, 0.13f), 0.88f, 0.76f),
                    Paint = MaterialLibrary.Lit(Color.Lerp(Palette.White, theme.accent, 0.18f), 0.08f, 0.74f),
                    Trim = MaterialLibrary.Lit(Color.Lerp(theme.buildingA, Palette.White, 0.2f), 0.75f, 0.7f),
                    Neon = MaterialLibrary.Lit(theme.accent, 0.2f, 0.8f, theme.accent, 3.1f),
                    NeonAlt = MaterialLibrary.Lit(Palette.Magenta, 0.2f, 0.8f, Palette.Magenta, 2.6f),
                    BuildingA = MaterialLibrary.Lit(theme.buildingA, 0.4f, 0.5f),
                    BuildingB = MaterialLibrary.Lit(theme.buildingB, 0.4f, 0.5f),
                    Roof = MaterialLibrary.Lit(Color.Lerp(theme.buildingB, Palette.Void, 0.55f), 0.4f, 0.35f),
                    Billboard = MaterialLibrary.UnlitTextured("NV_AdA" + key, adA[style],
                        new Color(1.32f, 1.32f, 1.32f, 1f), Vector2.one),
                    Sign = MaterialLibrary.UnlitTextured("NV_Transit" + key, transit[style],
                        new Color(1.05f, 1.05f, 1.05f, 1f), Vector2.one),
                    SignAlt = MaterialLibrary.UnlitTextured("NV_AdB" + key, adB[style],
                        new Color(1.28f, 1.28f, 1.28f, 1f), Vector2.one),
                    Shop = MaterialLibrary.UnlitTextured("NV_ShopMat" + key, shop[style],
                        new Color(1.16f, 1.16f, 1.16f, 1f), Vector2.one),
                    Pipe = MaterialLibrary.Lit(Color.Lerp(theme.buildingA, theme.accent, 0.12f), 0.8f, 0.7f),
                    White = MaterialLibrary.Lit(Palette.White, 0.2f, 0.8f, Palette.White, 1.2f),
                    WarmLamp = MaterialLibrary.Lit(Palette.Amber, 0.2f, 0.8f, Palette.Amber, 2.6f),
                    WarmGlow = MaterialLibrary.UnlitAdditiveTextured("NV_WarmGlow" + key,
                        TextureFactory.RadialGlow(64),
                        new Color(1f, 0.62f, 0.3f, 0.24f), Vector2.one, false, -30),
                    NeonSoft = MaterialLibrary.Lit(Color.Lerp(theme.accent, Palette.White, 0.25f),
                        0.2f, 0.72f, Color.Lerp(theme.accent, Palette.White, 0.18f), 1.35f)
                };
                // Window atlases are mapped in world units by DecorMeshBuilder.Facade. Their
                // mipmaps prevent the previous per-pixel window shimmer at distance, and the
                // three families give each block a different architectural identity.
                Color facadeATint = Color.Lerp(Color.white, theme.accent, 0.10f) * 1.08f;
                Color facadeBTint = Color.Lerp(Color.white, Palette.Magenta, 0.10f) * 1.06f;
                Color facadeCTint = Color.Lerp(Color.white, Palette.Amber, 0.08f) * 1.04f;
                m.BuildingA = MaterialLibrary.UnlitTextured("NV_WinA" + key, windowA[style],
                    facadeATint, Vector2.one);
                m.BuildingB = MaterialLibrary.UnlitTextured("NV_WinB" + key, windowB[style],
                    facadeBTint, Vector2.one);
                m.BuildingC = MaterialLibrary.UnlitTextured("NV_WinC" + key, windowC[style],
                    facadeCTint, Vector2.one);

                Color leaf = Color.Lerp(new Color(0.055f, 0.16f, 0.10f), theme.accent, 0.11f);
                m.Foliage = MaterialLibrary.Lit(leaf, 0.02f, 0.32f, leaf, 0.22f);
                m.FoliageDark = MaterialLibrary.Lit(leaf * 0.58f, 0.02f, 0.28f, leaf * 0.58f, 0.12f);
                Color leafLight = Color.Lerp(new Color(0.12f, 0.30f, 0.17f), theme.accent, 0.10f);
                m.FoliageLight = MaterialLibrary.Lit(leafLight, 0.02f, 0.3f, leafLight, 0.16f);
                m.Bark = MaterialLibrary.Lit(new Color(0.18f, 0.125f, 0.083f), 0.05f, 0.32f,
                    new Color(0.16f, 0.10f, 0.055f), 0.35f);
                // Shared thematic substrates tinted per location: timber, sandstone, snow and
                // ripped rock. Each texture is generated once and reused by all 20 districts.
                m.Wood = MaterialLibrary.LitTextured("NV_WoodMat" + key, TextureFactory.WoodPlanks(3101),
                    Color.Lerp(new Color(0.42f, 0.28f, 0.16f), theme.buildingA, 0.16f), 0.04f, 0.4f,
                    new Vector2(1.4f, 1.4f));
                m.Sand = MaterialLibrary.LitTextured("NV_SandMat" + key, TextureFactory.Sandstone(5171),
                    Color.Lerp(new Color(0.78f, 0.62f, 0.4f), theme.buildingA, 0.2f), 0.02f, 0.5f,
                    new Vector2(2.2f, 2.2f));
                m.Snow = MaterialLibrary.LitTextured("NV_SnowMat" + key, TextureFactory.SnowSurface(7111),
                    Color.Lerp(new Color(0.86f, 0.92f, 1f), theme.buildingA, 0.05f), 0.06f, 0.62f,
                    new Vector2(1.8f, 1.8f));
                m.Rock = MaterialLibrary.LitTextured("NV_RockMat" + key, TextureFactory.Concrete(9444),
                    Color.Lerp(new Color(0.24f, 0.23f, 0.24f), theme.buildingB, 0.25f), 0.05f, 0.3f,
                    new Vector2(1.2f, 2.6f));
                m.LampHaze = MaterialLibrary.UnlitAdditiveTextured("NV_LampHaze" + key,
                    TextureFactory.LightShaft(),
                    new Color(1f, 0.7f, 0.38f, 0.17f), Vector2.one, false, -20);
                _locationMaterials[index] = m;
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
            _lastDistance = Mathf.Max(0f, distance);
            int currentIndex = Mathf.FloorToInt(_lastDistance / SegmentLength);
            if (generate)
            {
                int targetLast = currentIndex + Mathf.Max(2, _config.segmentsAhead);
                int firstWanted = Mathf.Max(0, currentIndex - Mathf.Max(0, _config.segmentsBehind) - 1);

                // A resume, seek or qualification jump must never iterate every skipped
                // segment. Clear the stream and rebuild only the visible window.
                bool jumpedForward = _nextIndex < firstWanted - 2;
                bool jumpedBackward = _nextIndex > targetLast + 2;
                if (jumpedForward || jumpedBackward)
                {
                    for (int i = 0; i < _activeList.Count; i++) ReleaseSegment(_activeList[i]);
                    _active.Clear();
                    _activeList.Clear();
                    _nextIndex = firstWanted;
                }

                int guard = 0;
                while (_nextIndex <= targetLast && guard++ < 256)
                {
                    if (!_active.ContainsKey(_nextIndex)) BuildSegment(_nextIndex);
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

            // Diagnostics are reported after the window has been built and trimmed. Updating
            // them inside BuildSegment made the counts lag one segment behind ActiveSegmentCount.
            PooledObstacleCount = _activeList.Count * 6;
            PooledPickupCount = _activeList.Count * 24;

            // Locations are pure distance zones. The event carries the zone index, not the
            // wrapped location id, so the first visit to each district is persistent.
            int zone = LocationCatalog.ZoneIndexForDistance(_lastDistance);
            if (zone != _currentZone)
            {
                _currentZone = zone;
                int style = LocationCatalog.Get(zone).style;
                bool styleChanged = style != _currentBiome;
                _currentBiome = style;
                LocationChanged?.Invoke(zone);
                if (styleChanged) BiomeChanged?.Invoke(style);
            }
        }

        /// <summary>Advance kinematic hazards from the run clock, not wall time.</summary>
        public void TickHazards(float simulationTime)
        {
            for (int i = 0; i < _activeList.Count; i++)
            {
                var segment = _activeList[i];
                if (segment?.Obstacles == null) continue;
                for (int j = 0; j < segment.Obstacles.Count; j++)
                    segment.Obstacles[j]?.TickMotion(simulationTime);
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
            int index = LocationCatalog.Wrap(plan.BiomeIndex);
            return _locationMaterials[index] ?? _locationMaterials[0];
        }

        private void BuildDecor(Segment segment)
        {
            var builder = new DecorMeshBuilder();
            var theme = _config.LocationTheme(segment.Plan.BiomeIndex);
            var mats = ThemeFor(segment.Plan);
            if (theme == null || mats == null) return;
            var location = LocationCatalog.Get(segment.Plan.BiomeIndex);
            int style = location != null ? location.style : 0;
            var rng = new DeterministicRandom(segment.Plan.Seed ^ 0x2A3B4C);

            BuildGround(builder, mats, segment.Plan, rng);
            switch (style)
            {
                case 1: BuildLuxury(builder, mats, theme, rng); break;
                case 2: BuildReactor(builder, mats, theme, rng); break;
                case 3: BuildTunnel(builder, mats, theme, rng); break;
                case 4: BuildSnow(builder, mats, theme, rng); break;
                case 5: BuildDesert(builder, mats, theme, rng); break;
                case 6: BuildCoast(builder, mats, theme, rng); break;
                case 7: BuildForest(builder, mats, theme, rng); break;
                case 8: BuildEuropean(builder, mats, theme, rng); break;
                case 9: BuildVolcanic(builder, mats, theme, rng); break;
                case 10: BuildJapanese(builder, mats, theme, rng); break;
                case 11: BuildCryoHarbor(builder, mats, theme, rng); break;
                case 12: BuildAlpineVillage(builder, mats, theme, rng); break;
                case 13: BuildSeoul(builder, mats, theme, rng); break;
                case 14: BuildSkybridge(builder, mats, theme, rng); break;
                case 15: BuildSpaceport(builder, mats, theme, rng); break;
                case 16: BuildRuins(builder, mats, theme, rng); break;
                default: BuildCity(builder, mats, theme, rng); break;
            }
            BuildSignature(builder, mats, theme, location, segment.Plan, rng);
            BuildJunction(builder, mats, location, segment.Plan, rng);

            if (segment.Plan.Index % 3 == 1) BuildGateway(builder, mats, rng);
            if (segment.Plan.IsEvent) BuildEventMarker(builder, mats);

            segment.DecorMesh = builder.Build(segment.Root.transform, false, out Material[] materials);
            segment.DecorFilter.sharedMesh = segment.DecorMesh;
            segment.DecorRenderer.sharedMaterials = materials;
            segment.GlowMesh = builder.Build(segment.Root.transform, true, out Material[] glowMaterials);
            segment.GlowFilter.sharedMesh = segment.GlowMesh;
            segment.GlowRenderer.sharedMaterials = glowMaterials;
        }

        /// <summary>
        /// One authored landmark family per district. Props sit outside the playable lanes so
        /// the silhouette changes with every 3 km zone without ever narrowing the dodge space.
        /// </summary>
        private static void BuildSignature(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme,
            LocationDefinition location, SegmentPlan plan, DeterministicRandom rng)
        {
            if (location == null) return;
            float z = SegmentLength * 0.5f + (plan.Index % 3 - 1) * 5.5f;
            switch (location.signature)
            {
                case 0: // Neon Sprawl: stacked holographic news pillars.
                    for (int side = -1; side <= 1; side += 2)
                    {
                        b.Box(mats.Metal, new Vector3(side * 6.4f, 2.2f, z), new Vector3(0.22f, 4.4f, 0.22f));
                        b.Quad(mats.Billboard, new Vector3(side * 6.4f, 3.4f, z + 0.14f),
                            new Vector3(1.5f, 2.4f, 1f), Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f));
                        b.Box(mats.Neon, new Vector3(side * 6.4f, 5.3f, z), new Vector3(0.9f, 0.05f, 0.06f));
                    }
                    break;
                case 1: // Skyway 09: monorail arch over the road.
                    b.Box(mats.Metal, new Vector3(-6.8f, 3.9f, z), new Vector3(0.5f, 7.8f, 0.5f));
                    b.Box(mats.Metal, new Vector3(6.8f, 3.9f, z), new Vector3(0.5f, 7.8f, 0.5f));
                    b.Box(mats.Trim, new Vector3(0f, 7.7f, z), new Vector3(14.2f, 0.55f, 0.7f));
                    b.Box(mats.Neon, new Vector3(0f, 7.35f, z - 0.4f), new Vector3(11f, 0.07f, 0.08f));
                    b.Quad(mats.Sign, new Vector3(0f, 6.7f, z - 0.38f), new Vector3(2.8f, 0.9f, 1f));
                    break;
                case 2: // Reactor Belt: glowing coolant coils.
                    for (int i = 0; i < 3; i++)
                    {
                        float x = 6.3f + i * 0.5f;
                        b.Cylinder(mats.Metal, new Vector3(x, 2.4f, z - 3f + i * 3f), new Vector3(1.1f, 4.8f, 1.1f));
                        b.Cylinder(mats.Neon, new Vector3(x, 4.9f, z - 3f + i * 3f), new Vector3(1.25f, 0.16f, 1.25f));
                    }
                    break;
                case 3: // Ghost Tunnel: sealed vault ring.
                    b.Cylinder(mats.Metal, new Vector3(6.5f, 3.1f, z), new Vector3(3.6f, 0.4f, 3.6f),
                        Quaternion.Euler(90f, 0f, 0f));
                    b.Cylinder(mats.NeonAlt, new Vector3(6.5f, 3.1f, z - 0.25f), new Vector3(3.15f, 0.1f, 3.15f),
                        Quaternion.Euler(90f, 0f, 0f));
                    break;
                case 4: // Monsoon Docks: cargo crane.
                    b.Box(mats.Metal, new Vector3(-7.2f, 4.2f, z), new Vector3(0.4f, 8.4f, 0.4f));
                    b.Box(mats.Trim, new Vector3(-4.3f, 8.1f, z), new Vector3(6.2f, 0.4f, 0.4f));
                    b.Box(mats.NeonAlt, new Vector3(-2.2f, 5.6f, z), new Vector3(0.07f, 4.7f, 0.07f));
                    b.Box(mats.SignAlt, new Vector3(-2.2f, 3.1f, z), new Vector3(1.8f, 1.3f, 1.2f));
                    break;
                case 5: // Solar Arcology: angled mirror array.
                    for (int i = 0; i < 3; i++)
                    {
                        b.Quad(mats.Billboard, new Vector3(6.8f, 4.6f + i * 0.9f, z - 3f + i * 2.4f),
                            new Vector3(3.2f, 1.7f, 1f), Quaternion.Euler(-24f, 204f, 0f));
                        b.Box(mats.Metal, new Vector3(6.8f, 2.4f, z - 3f + i * 2.4f), new Vector3(0.18f, 4.8f, 0.18f));
                    }
                    break;
                case 6: // Cryo Foundry: vent stacks.
                    for (int i = 0; i < 2; i++)
                    {
                        float x = -6.4f - i * 1.2f;
                        b.Cylinder(mats.Pipe, new Vector3(x, 3.2f, z + i * 4f), new Vector3(0.8f, 6.4f, 0.8f));
                        b.Cylinder(mats.NeonSoft, new Vector3(x, 6.5f, z + i * 4f), new Vector3(1.05f, 0.12f, 1.05f));
                    }
                    break;
                case 7: // Ember Transit: furnace chimney.
                    b.Box(mats.BuildingB, new Vector3(7f, 4.8f, z), new Vector3(2.2f, 9.6f, 2.2f));
                    b.Box(mats.WarmLamp, new Vector3(7f, 3.2f, z - 1.18f), new Vector3(1.3f, 0.85f, 0.08f));
                    b.Box(mats.NeonAlt, new Vector3(7f, 8.4f, z), new Vector3(2.5f, 0.1f, 2.5f));
                    break;
                case 8: // Verdant Grid: hydroponic planters.
                    for (int i = 0; i < 3; i++)
                    {
                        float zi = z - 4f + i * 3.2f;
                        b.Box(mats.Metal, new Vector3(-6.2f, 1.2f + i * 0.8f, zi), new Vector3(2.6f, 0.4f, 2.2f));
                        b.Foliage(mats.Foliage, new Vector3(-6.2f, 1.8f + i * 0.8f, zi), new Vector3(2.2f, 1.0f, 1.8f));
                        b.Box(mats.NeonSoft, new Vector3(-6.2f, 2.2f + i * 0.8f, zi), new Vector3(0.06f, 0.06f, 1.8f));
                    }
                    break;
                case 9: // Halcyon Spire: golden needle.
                    b.Cylinder(mats.Trim, new Vector3(6.6f, 4.5f, z), new Vector3(1.4f, 9f, 1.4f));
                    b.Cylinder(mats.WarmLamp, new Vector3(6.6f, 9.2f, z), new Vector3(1.05f, 0.24f, 1.05f));
                    b.Box(mats.Neon, new Vector3(6.6f, 6.2f, z), new Vector3(1.55f, 0.09f, 1.55f));
                    break;
                case 10: // Quantum Yard: gyroscopic rings.
                    for (int i = 0; i < 3; i++)
                    {
                        b.Cylinder(mats.Neon, new Vector3(6.4f, 3.6f, z), new Vector3(3.5f - i * 0.7f, 0.08f, 3.5f - i * 0.7f),
                            Quaternion.Euler(90f, 24f * i, 0f));
                    }
                    break;
                case 11: // Null Cathedral: silent arches.
                    for (int i = 0; i < 3; i++)
                    {
                        float zi = z - 5f + i * 5f;
                        b.Box(mats.Trim, new Vector3(-6.9f, 4.1f, zi), new Vector3(0.5f, 8.2f, 0.5f));
                        b.Box(mats.Trim, new Vector3(6.9f, 4.1f, zi), new Vector3(0.5f, 8.2f, 0.5f));
                        b.Box(mats.Sign, new Vector3(0f, 8.1f, zi), new Vector3(14.4f, 0.4f, 0.5f));
                    }
                    break;
                case 12: // Tidal Exchange: turbine wheel.
                    b.Cylinder(mats.Metal, new Vector3(6.6f, 3.4f, z), new Vector3(2.6f, 0.5f, 2.6f),
                        Quaternion.Euler(0f, 0f, 90f));
                    for (int i = 0; i < 4; i++)
                    {
                        b.Box(mats.NeonSoft, new Vector3(6.6f, 3.4f, z), new Vector3(0.16f, 2.4f, 0.7f),
                            Quaternion.Euler(0f, 0f, 45f * i));
                    }
                    break;
                case 13: // Aurora Relay: uplink mast.
                    b.Box(mats.Metal, new Vector3(-6.6f, 5.4f, z), new Vector3(0.34f, 10.8f, 0.34f));
                    for (int i = 0; i < 3; i++)
                        b.Cylinder(mats.NeonAlt, new Vector3(-6.6f, 4.2f + i * 2.1f, z),
                            new Vector3(2.2f - i * 0.4f, 0.07f, 2.2f - i * 0.4f));
                    break;
                case 14: // Obsidian Terminus: black monolith.
                    b.Box(mats.BuildingA, new Vector3(6.8f, 4.8f, z), new Vector3(2.4f, 9.6f, 1.6f));
                    b.Box(mats.NeonAlt, new Vector3(5.56f, 4.8f, z), new Vector3(0.06f, 8.8f, 0.08f));
                    b.Box(mats.NeonAlt, new Vector3(6.8f, 9.5f, z), new Vector3(2.4f, 0.08f, 1.6f));
                    break;
                case 15: // Sakura Circuit: petal shrine gate.
                    for (int i = 0; i < 3; i++)
                    {
                        float zi = z - 3.6f + i * 3.6f;
                        for (int side = -1; side <= 1; side += 2)
                            b.Box(mats.NeonAlt, new Vector3(side * 6.4f, 2.6f, zi), new Vector3(0.22f, 5.2f, 0.22f));
                        b.Box(mats.WarmLamp, new Vector3(0f, 5.5f, zi), new Vector3(13.4f, 0.26f, 0.34f));
                        b.Box(mats.Roof, new Vector3(0f, 5.82f, zi), new Vector3(14.6f, 0.2f, 0.55f));
                    }
                    break;
                case 16: // Dune Meridian: solar obelisk.
                    b.Cylinder(mats.BuildingA, new Vector3(6.9f, 4.4f, z), new Vector3(1.6f, 8.8f, 1.6f));
                    b.Cylinder(mats.NeonAlt, new Vector3(6.9f, 8.6f, z), new Vector3(2.3f, 0.14f, 2.3f));
                    b.Box(mats.WarmLamp, new Vector3(6.9f, 5.4f, z - 0.82f), new Vector3(0.5f, 3.2f, 0.06f));
                    break;
                case 17: // Kessler Reach: launch gantry.
                    b.Box(mats.Metal, new Vector3(-7.1f, 5.2f, z), new Vector3(0.55f, 10.4f, 0.55f));
                    b.Box(mats.Metal, new Vector3(-4.3f, 9.9f, z), new Vector3(6.2f, 0.34f, 0.34f));
                    b.Cylinder(mats.White, new Vector3(-3.4f, 4.2f, z), new Vector3(0.62f, 8.4f, 0.62f));
                    b.Cylinder(mats.Neon, new Vector3(-3.4f, 8.55f, z), new Vector3(0.5f, 0.5f, 0.5f),
                        Quaternion.Euler(0f, 0f, 45f));
                    b.Box(mats.NeonAlt, new Vector3(-3.4f, 1.1f, z), new Vector3(0.9f, 0.24f, 0.9f));
                    break;
                case 18: // Aurelia Prime: union obelisk and dome.
                    b.Cylinder(mats.Trim, new Vector3(6.8f, 4.6f, z), new Vector3(1.1f, 9.2f, 1.1f),
                        Quaternion.Euler(0f, 0f, 45f));
                    b.Cylinder(mats.Podium, new Vector3(6.8f, 1.1f, z), new Vector3(2.0f, 2.2f, 2.0f));
                    b.Foliage(mats.WarmLamp, new Vector3(6.8f, 9.4f, z), new Vector3(0.9f, 0.55f, 0.9f));
                    b.Box(mats.NeonSoft, new Vector3(6.8f, 6.5f, z), new Vector3(1.4f, 0.08f, 1.4f));
                    break;
                case 19: // Coral Expanse: living reef arch.
                    for (int side = -1; side <= 1; side += 2)
                    {
                        b.Cylinder(mats.NeonSoft, new Vector3(side * 6.2f, 2.1f, z), new Vector3(0.5f, 4.2f, 0.5f),
                            Quaternion.Euler(0f, 0f, side * -12f));
                        b.Cylinder(mats.Neon, new Vector3(side * 6.2f, 4.35f, z), new Vector3(0.2f, 0.2f, 0.2f));
                    }
                    b.Cylinder(mats.FoliageLight, new Vector3(0f, 4.6f, z), new Vector3(0.36f, 12.2f, 0.36f),
                        Quaternion.Euler(0f, 0f, 90f));
                    for (int i = 0; i < 5; i++)
                        b.Foliage(mats.NeonSoft, new Vector3(-4.4f + i * 2.2f, 4.95f, z),
                            new Vector3(0.9f, 0.5f, 0.7f));
                    break;
                case 20: // Retired Helion Ring signature; kept as a defensive fallback, unused by all 20 active themes.
                    for (int i = 0; i < 2; i++)
                        b.Cylinder(mats.Neon, new Vector3(6.6f, 3.4f + i * 2.4f, z),
                            new Vector3(2.8f - i * 0.5f, 0.12f, 2.8f - i * 0.5f), Quaternion.Euler(74f, i * 40f, 0f));
                    b.Box(mats.White, new Vector3(6.6f, 3.4f, z), new Vector3(0.16f, 4.2f, 0.16f));
                    b.Box(mats.NeonAlt, new Vector3(6.6f, 5.55f, z), new Vector3(1.1f, 0.1f, 1.1f));
                    break;
                default:
                    b.Box(mats.BuildingA, new Vector3(6.8f, 4.8f, z), new Vector3(2.4f, 9.6f, 1.6f));
                    b.Box(mats.NeonAlt, new Vector3(5.56f, 4.8f, z), new Vector3(0.06f, 8.8f, 0.08f));
                    break;
            }
        }

        /// <summary>
        /// Junction set dressing: an overhead directional gantry, lane chevrons, a reward
        /// beacon and kind-specific structure. All geometry stays outside the three running
        /// lanes; the collision authority is the divider/edge obstacle spawned by the pool.
        /// </summary>
        private static void BuildJunction(DecorMeshBuilder b, ThemeMaterials mats, LocationDefinition location,
            SegmentPlan plan, DeterministicRandom rng)
        {
            var junction = plan.Junction;
            if (junction == null) return;
            float commitZ = junction.CommitRow * RowSpacing;
            float branchStartZ = junction.BranchStartRow * RowSpacing;
            float branchEndZ = (junction.BranchEndRow + 1) * RowSpacing;

            // Approach chevrons and a bright stop line make the commitment point readable from
            // 126 m away, even at maximum speed.
            for (int i = 0; i < 3; i++)
            {
                float z = commitZ - 5.5f + i * 2.4f;
                for (int side = -1; side <= 1; side += 2)
                {
                    b.Box(mats.Paint, new Vector3(side * 1.5f, 0.019f, z),
                        new Vector3(0.22f, 0.012f, 2.0f), Quaternion.Euler(0f, side * 24f, 0f));
                    b.Box(mats.Paint, new Vector3(side * 3.15f, 0.019f, z),
                        new Vector3(0.22f, 0.012f, 2.0f), Quaternion.Euler(0f, side * 24f, 0f));
                }
            }
            b.Box(mats.Neon, new Vector3(0f, 0.022f, commitZ - 0.35f), new Vector3(8.1f, 0.014f, 0.3f));

            // Overhead gantry. Posts sit outside the guard rail so they never narrow the run.
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(mats.Metal, new Vector3(side * 4.62f, 2.65f, commitZ), new Vector3(0.26f, 5.3f, 0.26f));
                b.Box(mats.NeonSoft, new Vector3(side * 4.47f, 2.65f, commitZ), new Vector3(0.04f, 4.8f, 0.05f));
            }
            b.Box(mats.Metal, new Vector3(0f, 5.22f, commitZ), new Vector3(9.5f, 0.3f, 0.34f));
            b.Box(mats.GroundDark, new Vector3(0f, 4.52f, commitZ), new Vector3(5.1f, 1.08f, 0.2f));
            b.Box(mats.Neon, new Vector3(0f, 5.04f, commitZ - 0.14f), new Vector3(4.7f, 0.05f, 0.04f));

            // Two authored arrow stacks show that both lanes continue. The reward branch is
            // additionally marked by a beacon; neither side is a trap.
            AddDirectionArrow(b, mats.Neon, new Vector3(-2.75f, 4.52f, commitZ - 0.16f), -1f);
            AddDirectionArrow(b, mats.NeonAlt, new Vector3(2.75f, 4.52f, commitZ - 0.16f), 1f);

            int rewardLane = junction.RewardLane;
            float rewardX = LaneMath.LaneToX(rewardLane);
            Material rewardMaterial = junction.RewardIsSecret ? mats.NeonAlt : mats.WarmLamp;
            for (int i = 0; i < 4; i++)
            {
                float z = branchStartZ + 2.4f + i * 3.4f;
                float rx = rewardX + JunctionRules.RouteOffsetAt(junction, z, rewardLane);
                b.Box(rewardMaterial, new Vector3(rx, 0.7f, z), new Vector3(0.06f, 1.4f, 0.06f));
                b.Box(rewardMaterial, new Vector3(rx, 1.45f, z), new Vector3(0.34f, 0.08f, 0.34f),
                    Quaternion.Euler(45f, 0f, 45f));
            }
            b.Box(rewardMaterial, new Vector3(rewardX, 2.45f, branchStartZ + 5.8f), new Vector3(1.5f, 0.06f, 0.12f));

            if (junction.IsTJunction || junction.IsShortcut)
                BuildCurvedBranchDecks(b, mats, junction);

            // Lane edge glow runs the length of the branch, visually connecting the fork.
            if (!junction.HasCurvedRoute)
            {
                for (int row = junction.BranchStartRow; row < junction.MergeRow; row++)
                {
                    float z = row * RowSpacing + RowSpacing * 0.5f;
                    // Narrow bridges need a rail on the reclaimed side lanes, not a lane stripe.
                    if (junction.IsNarrowBridge)
                    {
                        for (int side = -1; side <= 1; side += 2)
                        {
                            b.Box(mats.Metal, new Vector3(side * 1.72f, 0.58f, z), new Vector3(0.11f, 1.16f, RowSpacing));
                            b.Box(mats.Neon, new Vector3(side * 1.66f, 1.17f, z), new Vector3(0.03f, 0.05f, RowSpacing));
                        }
                        b.Box(mats.Metal, new Vector3(0f, 4.2f, z), new Vector3(4.0f, 0.22f, RowSpacing * 0.92f));
                        b.Box(mats.NeonSoft, new Vector3(0f, 4.07f, z), new Vector3(2.8f, 0.04f, 0.08f));
                    }
                    else if (junction.UsesMedian)
                    {
                        b.Box(mats.Paint, new Vector3(-1.36f, 0.02f, z), new Vector3(0.045f, 0.012f, RowSpacing));
                        b.Box(mats.Paint, new Vector3(1.36f, 0.02f, z), new Vector3(0.045f, 0.012f, RowSpacing));
                    }
                }
            }

            if (junction.Kind == JunctionKind.Crossroads)
            {
                // A cross street reading: broad zebra bars and four signal heads. This is the
                // visual identity of the crossroads even though both branches remain parallel.
                for (int i = 0; i < 5; i++)
                    b.Box(mats.Paint, new Vector3(-3.3f + i * 1.65f, 0.021f, branchEndZ - 3.2f),
                        new Vector3(0.62f, 0.012f, 2.8f));
                for (int side = -1; side <= 1; side += 2)
                {
                    b.Box(mats.Metal, new Vector3(side * 4.5f, 3.15f, branchEndZ - 1.4f), new Vector3(0.18f, 6.3f, 0.18f));
                    b.Box(mats.GroundDark, new Vector3(side * 4.5f, 6.15f, branchEndZ - 1.4f), new Vector3(0.55f, 1.2f, 0.2f));
                    for (int light = 0; light < 3; light++)
                        b.Box(light == 0 ? mats.NeonAlt : mats.Neon,
                            new Vector3(side * 4.5f, 5.86f + light * 0.28f, branchEndZ - 1.52f),
                            new Vector3(0.3f, 0.16f, 0.04f));
                }
            }
            else if (junction.Kind == JunctionKind.TunnelBranch)
            {
                // Ribbed tunnel portal over the fork, open enough to keep the camera clear.
                for (int row = junction.BranchStartRow; row <= junction.BranchEndRow; row++)
                {
                    float z = row * RowSpacing + RowSpacing * 0.5f;
                    for (int side = -1; side <= 1; side += 2)
                        b.Box(mats.Trim, new Vector3(side * 4.55f, 2.35f, z), new Vector3(0.42f, 4.7f, 0.52f));
                    b.Box(mats.Trim, new Vector3(0f, 4.62f, z), new Vector3(9.3f, 0.44f, 0.52f));
                    b.Box(mats.Neon, new Vector3(0f, 4.35f, z - 0.28f), new Vector3(7.6f, 0.06f, 0.04f));
                }
            }
            else if (junction.IsBrokenRoad)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        float z = 11f + i * 3.1f;
                        b.Box(mats.Metal, new Vector3(side * 4.55f, 0.52f, z), new Vector3(0.14f, 1.04f, 2.4f),
                            Quaternion.Euler(i % 2 == 0 ? 7f : -9f, 0f, 0f));
                    }
                    b.Box(mats.Paint, new Vector3(side * 3.2f, 0.02f, 14.0f), new Vector3(1.4f, 0.012f, 0.18f),
                        Quaternion.Euler(0f, side * 18f, 0f));
                }
                if (junction.IsCollapsingBridge)
                {
                    // Fractured side slabs and exposed rebar sell the imminent collapse.
                    for (int i = 0; i < 6; i++)
                    {
                        float z = 10.5f + i * 2.4f;
                        b.Box(mats.GroundDark, new Vector3(-4.1f + (i % 2) * 0.6f, -0.32f, z),
                            new Vector3(1.4f, 0.26f, 1.6f), Quaternion.Euler(0f, i * 7f, i % 2 == 0 ? 9f : -11f));
                        b.Box(mats.Metal, new Vector3(3.9f - (i % 2) * 0.7f, 0.18f, z + 0.8f),
                            new Vector3(0.08f, 0.5f, 0.08f), Quaternion.Euler(i % 2 == 0 ? 22f : -18f, 0f, 0f));
                    }
                    b.Box(mats.NeonAlt, new Vector3(0f, 0.32f, 9.2f), new Vector3(7.8f, 0.06f, 0.08f));
                }
            }
            else if (junction.Kind == JunctionKind.TJunction)
            {
                // A perpendicular cross street makes the T readable: the main road meets a
                // lit avenue and the two branch decks curve out around the central median.
                float crossZ = branchStartZ + RowSpacing;
                b.Box(mats.Ground, new Vector3(0f, -0.283f, crossZ), new Vector3(26f, 0.55f, 7.2f));
                b.Box(mats.GroundDark, new Vector3(0f, -0.76f, crossZ), new Vector3(24f, 0.5f, 6.4f));
                for (int i = 0; i < 9; i++)
                    b.Box(mats.Paint, new Vector3(-6.4f + i * 1.6f, 0.02f, crossZ + 2.1f),
                        new Vector3(0.7f, 0.012f, 2.4f));
                for (int side = -1; side <= 1; side += 2)
                {
                    b.Box(mats.Metal, new Vector3(side * 5.4f, 3.4f, crossZ), new Vector3(0.2f, 6.8f, 0.2f));
                    b.Box(mats.GroundDark, new Vector3(side * 5.4f, 6.65f, crossZ), new Vector3(0.6f, 1.3f, 0.22f));
                    for (int light = 0; light < 3; light++)
                        b.Box(light == 0 ? mats.NeonAlt : mats.Neon,
                            new Vector3(side * 5.4f, 6.35f + light * 0.28f, crossZ - 0.14f),
                            new Vector3(0.32f, 0.17f, 0.045f));
                }
                // Cross-street lane markings and kerb glow.
                b.Box(mats.NeonSoft, new Vector3(0f, 0.018f, crossZ + 3.3f), new Vector3(22f, 0.02f, 0.08f));
                b.Box(mats.NeonSoft, new Vector3(0f, 0.018f, crossZ - 3.3f), new Vector3(22f, 0.02f, 0.08f));
            }
            else if (junction.Kind == JunctionKind.Shortcut)
            {
                int side = junction.RewardOnLeft ? -1 : 1;
                float sx = LaneMath.LaneToX(junction.RewardLane);
                float signZ = branchStartZ - 4.2f;
                b.Box(mats.Metal, new Vector3(sx, 2.0f, signZ), new Vector3(0.16f, 4.0f, 0.16f));
                b.Box(mats.SignAlt, new Vector3(sx, 3.4f, signZ), new Vector3(2.4f, 0.7f, 0.08f));
                AddDirectionArrow(b, mats.NeonAlt, new Vector3(sx + side * 0.4f, 3.4f, signZ - 0.06f), side);
                b.Box(mats.Neon, new Vector3(sx, 0.028f, branchStartZ + RowSpacing * 1.5f),
                    new Vector3(2.1f, 0.02f, 3.4f));
            }
            else if (junction.Kind == JunctionKind.CurvedRoad)
            {
                // Reflective bend boards on the outside of the curve reinforce the deck.
                int side = junction.CurveSign;
                for (int i = 0; i < 4; i++)
                {
                    float z = 8f + i * 7.4f;
                    float x = JunctionRules.RouteOffsetAt(junction, z, 1) + side * 4.2f;
                    b.Box(mats.Metal, new Vector3(x, 2.3f, z), new Vector3(0.14f, 4.6f, 0.14f));
                    b.Box(mats.SignAlt, new Vector3(x + side * 0.08f, 3.5f, z),
                        new Vector3(0.06f, 1.1f, 2.2f));
                    AddDirectionArrow(b, mats.NeonAlt,
                        new Vector3(x + side * 0.12f, 3.5f, z - 0.6f), side);
                }
                b.Box(mats.NeonSoft, new Vector3(0f, 0.024f, SegmentLength * 0.5f),
                    new Vector3(7.2f, 0.012f, 0.12f));
            }

            // A small district-coloured crest on the gantry ties the junction to the biome.
            if (location != null)
                b.Box(mats.Sign, new Vector3(0f, 3.88f, commitZ - 0.14f), new Vector3(2.1f, 0.46f, 0.05f));
        }

        /// <summary>
        /// Builds the physical side deck for the two curved branch shapes. The panel follows
        /// the exact same lateral function the runner uses, so the visible road and the
        /// collision route cannot drift apart. Panels overlap by 0.15 m to avoid seams.
        /// </summary>
        private static void BuildCurvedBranchDecks(DecorMeshBuilder b, ThemeMaterials mats, JunctionPlan junction)
        {
            float start = junction.BranchStartRow * RowSpacing;
            float end = junction.MergeRow * RowSpacing;
            const float step = 2.1f;
            for (int side = -1; side <= 1; side += 2)
            {
                if (junction.IsShortcut && side != (junction.RewardOnLeft ? -1 : 1)) continue;
                int lane = side < 0 ? 0 : 2;
                for (float z = start + 0.7f; z < end; z += step)
                {
                    float x = LaneMath.LaneToX(lane) + JunctionRules.RouteOffsetAt(junction, z, lane);
                    float yaw = Mathf.Atan(JunctionRules.RouteSlopeAt(junction, z, lane)) * Mathf.Rad2Deg;
                    var rot = Quaternion.Euler(0f, yaw, 0f);
                    // A T-junction deck must reach the central median. Without the extra width
                    // the curved lane pulls away from the divider and leaves a visible hole in
                    // the road. Shortcuts keep the narrower marked lane deck.
                    float deckWidth = 3.05f;
                    float deckX = x;
                    if (junction.IsTJunction)
                    {
                        float outer = x + side * 1.62f;
                        deckWidth = Mathf.Abs(outer) + 0.2f;
                        deckX = outer * 0.5f;
                    }
                    b.Box(mats.Ground, new Vector3(deckX, -0.275f, z), new Vector3(deckWidth, 0.5f, step + 0.15f), rot);
                    b.Box(mats.Paint, new Vector3(x, 0.014f, z), new Vector3(0.06f, 0.012f, step), rot);
                    b.Box(mats.NeonSoft, new Vector3(x + side * 1.28f, 0.022f, z),
                        new Vector3(0.06f, 0.014f, step), rot);
                    b.Box(mats.Metal, new Vector3(x + side * 1.62f, 0.55f, z),
                        new Vector3(0.12f, 1.1f, step + 0.1f), rot);
                    b.Box(mats.Neon, new Vector3(x + side * 1.56f, 1.12f, z),
                        new Vector3(0.045f, 0.05f, step), rot);
                }
                // Nose chevrons make the entrance legible at speed.
                for (int i = 0; i < 2; i++)
                {
                    float z = start - 3.2f + i * 1.6f;
                    b.Box(mats.Paint, new Vector3(side * 2.6f, 0.019f, z),
                        new Vector3(0.2f, 0.012f, 1.5f), Quaternion.Euler(0f, side * 26f, 0f));
                }
            }
        }

        private static void AddDirectionArrow(DecorMeshBuilder b, Material material, Vector3 centre, float direction)
        {
            b.Box(material, centre + new Vector3(direction * 0.38f, 0f, 0f), new Vector3(0.72f, 0.16f, 0.04f));
            b.Box(material, centre + new Vector3(direction * 0.72f, 0.16f, 0f),
                new Vector3(0.44f, 0.13f, 0.04f), Quaternion.Euler(0f, 0f, direction * 38f));
            b.Box(material, centre + new Vector3(direction * 0.72f, -0.16f, 0f),
                new Vector3(0.44f, 0.13f, 0.04f), Quaternion.Euler(0f, 0f, direction * -38f));
        }

        private static void BuildGround(DecorMeshBuilder b, ThemeMaterials mats, SegmentPlan plan, DeterministicRandom rng)
        {
            if (plan.Junction != null && plan.Junction.IsCurvedRoad)
            {
                BuildCurvedGround(b, mats, plan.Junction, rng);
                return;
            }
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

            // Direction chevrons in the centre lane, only on the city and skyway styles.
            int groundStyle = LocationCatalog.Get(plan.BiomeIndex).style;
            if (groundStyle < 2)
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

        /// <summary>
        /// A genuine bend: instead of swapping the sky colour, the asphalt, markings, curbs,
        /// rails and wet reflections are rebuilt in slices that follow the same lateral curve
        /// the runner follows. Both ends return to centre so the straight neighbours join
        /// without a visible seam.
        /// </summary>
        private static void BuildCurvedGround(DecorMeshBuilder b, ThemeMaterials mats,
            JunctionPlan junction, DeterministicRandom rng)
        {
            const int slices = 16;
            float slice = SegmentLength / slices;
            for (int i = 0; i < slices; i++)
            {
                float z = (i + 0.5f) * slice;
                float x = JunctionRules.RouteOffsetAt(junction, z, 1);
                float yaw = Mathf.Atan(JunctionRules.RouteSlopeAt(junction, z, 1)) * Mathf.Rad2Deg;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                b.Box(mats.Ground, new Vector3(x, -0.28f, z), new Vector3(9.4f, 0.55f, slice + 0.12f), rot);
                b.Box(mats.GroundDark, new Vector3(x, -0.75f, z), new Vector3(7.5f, 0.5f, slice + 0.08f), rot);
                for (int lane = 0; lane < 2; lane++)
                    b.Box(mats.Paint, new Vector3(x - 1.35f + lane * 2.7f, 0.011f, z),
                        new Vector3(0.045f, 0.01f, slice + 0.05f), rot);
                for (int side = -1; side <= 1; side += 2)
                {
                    b.Box(mats.Paint, new Vector3(x + side * 3.98f, 0.011f, z),
                        new Vector3(0.07f, 0.01f, slice + 0.05f), rot);
                    b.Box(mats.Metal, new Vector3(x + side * 4.85f, 0.55f, z),
                        new Vector3(0.14f, 1.1f, slice + 0.1f), rot);
                    b.Box(mats.Neon, new Vector3(x + side * 4.78f, 1.13f, z),
                        new Vector3(0.04f, 0.045f, slice), rot);
                    b.Box(mats.Sidewalk, new Vector3(x + side * 6.15f, 0.04f, z),
                        new Vector3(2.1f, 0.2f, slice + 0.1f), rot);
                }
                if (i % 2 == 0)
                {
                    for (int lane = 0; lane < 3; lane++)
                        b.Box(mats.Neon, new Vector3(x + LaneMath.LaneToX(lane), 0.018f, z),
                            new Vector3(0.15f, 0.012f, 0.10f), rot);
                }
                if (i % 4 == 0)
                {
                    int side = junction.CurveSign;
                    b.Box(mats.NeonAlt, new Vector3(x + side * 4.25f, 0.85f, z),
                        new Vector3(0.42f, 0.08f, 0.06f),
                        Quaternion.Euler(0f, yaw + side * 28f, 0f));
                }
            }
            for (int i = 0; i < 3; i++)
            {
                float z = 7f + i * 14f;
                float x = JunctionRules.RouteOffsetAt(junction, z, 1);
                b.Box(mats.GroundDark, new Vector3(x, -6f, z), new Vector3(6.2f, 10f, 1.2f));
            }
            for (int i = 0; i < 6; i++)
            {
                int lane = rng.Range(0, 3);
                float z = rng.Range(4f, SegmentLength - 4f);
                float x = LaneMath.LaneToX(lane) + JunctionRules.RouteOffsetAt(junction, z, lane) +
                          rng.Range(-0.55f, 0.55f);
                Material glow = i % 3 == 0 ? mats.WetGlowAlt : mats.WetGlow;
                b.Quad(glow, new Vector3(x, 0.042f, z),
                    new Vector3(rng.Range(0.45f, 1.1f), rng.Range(2.1f, 4.8f), 1f),
                    Quaternion.Euler(90f, rng.Range(-8f, 8f), 0f));
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
                for (int i = 0; i < 6; i++)
                {
                    float farX = side * rng.Range(21f, 49f);
                    float farZ = rng.Range(-5f, SegmentLength + 5f);
                    float farH = rng.Range(18f, 78f);
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
                    if (farH > 40f && rng.Chance(0.6f))
                    {
                        // Antenna array on the taller midground mass.
                        for (int mast = 0; mast < 2; mast++)
                        {
                            b.Box(mats.Trim, new Vector3(farX - farW * 0.24f + mast * farW * 0.48f,
                                    farH + rng.Range(2f, 3.6f), farZ),
                                new Vector3(0.12f, rng.Range(3.2f, 5.4f), 0.12f));
                        }
                    }
                    if (rng.Chance(0.5f))
                    {
                        b.Box(mats.NeonAlt, new Vector3(farX, farH + 0.7f, farZ),
                            new Vector3(0.34f, 0.14f, 0.34f));
                    }
                    if (rng.Chance(0.45f))
                    {
                        b.Box(mats.Trim, new Vector3(farX, farH + 3f, farZ),
                            new Vector3(0.1f, 6f, 0.1f));
                        b.Box(mats.NeonAlt, new Vector3(farX, farH + 6f, farZ),
                            new Vector3(0.3f, 0.12f, 0.3f));
                    }
                }
                for (int i = 0; i < 4; i++)
                {
                    float farX = side * rng.Range(54f, 98f);
                    float farZ = rng.Range(-8f, SegmentLength + 8f);
                    float farH = rng.Range(30f, 104f);
                    float farW = rng.Range(10f, 24f);
                    float farD = rng.Range(10f, 24f);
                    b.Box(mats.GroundDark, new Vector3(farX, farH * 0.5f, farZ),
                        new Vector3(farW, farH, farD));
                    b.Box(mats.NeonAlt, new Vector3(farX - side * (farW * 0.5f + 0.04f),
                            farH * 0.76f, farZ),
                        new Vector3(0.1f, 0.35f, farD * 0.7f));
                    b.Box(mats.Neon, new Vector3(farX, farH + 0.4f, farZ),
                        new Vector3(0.32f, 0.5f, 0.32f));
                    if (rng.Chance(0.4f))
                    {
                        b.Quad(mats.Billboard, new Vector3(farX + side * (farW * 0.51f + 0.06f),
                                farH * rng.Range(0.55f, 0.8f), farZ),
                            new Vector3(0.06f, farH * rng.Range(0.22f, 0.35f), farW * 0.55f),
                            Quaternion.Euler(0f, side * -8f, 0f));
                    }
                }

                // A sky bridge joins two midground masses above the street canyon.
                if (rng.Chance(0.36f))
                {
                    float bridgeY = rng.Range(14f, 21f);
                    float bridgeZ = rng.Range(2f, SegmentLength - 6f);
                    b.Box(mats.GroundDark, new Vector3(side * 19.5f, bridgeY, bridgeZ),
                        new Vector3(7.5f, 1.15f, 1.7f));
                    b.Box(mats.Trim, new Vector3(side * 19.5f, bridgeY + 0.75f, bridgeZ - 0.8f),
                        new Vector3(7.3f, 0.12f, 0.12f));
                    b.Box(mats.Trim, new Vector3(side * 19.5f, bridgeY + 0.75f, bridgeZ + 0.8f),
                        new Vector3(7.3f, 0.12f, 0.12f));
                    b.Box(mats.Neon, new Vector3(side * 19.5f, bridgeY - 0.68f, bridgeZ),
                        new Vector3(6.5f, 0.09f, 0.14f));
                }
            }

            // Cross-city sky bridge arching over the running canyon between the block rows.
            if (rng.Chance(0.5f))
            {
                float archY = rng.Range(17f, 25f);
                float archZ = rng.Range(6f, SegmentLength - 6f);
                b.Box(mats.GroundDark, new Vector3(0f, archY, archZ), new Vector3(15.5f, 1.3f, 2.2f));
                b.Box(mats.Trim, new Vector3(0f, archY + 0.95f, archZ - 1.05f),
                    new Vector3(15.2f, 0.14f, 0.14f));
                b.Box(mats.Trim, new Vector3(0f, archY + 0.95f, archZ + 1.05f),
                    new Vector3(15.2f, 0.14f, 0.14f));
                for (int foot = 0; foot < 4; foot++)
                {
                    float footX = -7.2f + foot * 4.8f;
                    b.Box(mats.GroundDark, new Vector3(footX, archY * 0.5f, archZ + 1.9f),
                        new Vector3(0.4f, archY, 0.4f));
                }
                b.Box(mats.Neon, new Vector3(0f, archY - 0.78f, archZ), new Vector3(13f, 0.1f, 0.16f));
            }

            // A media board hangs over the street on almost every second segment.
            if (rng.Chance(0.62f))
            {
                float boardY = rng.Range(11f, 16f);
                float boardZ = rng.Range(2f, SegmentLength - 4f);
                b.Box(mats.Metal, new Vector3(0f, boardY, boardZ), new Vector3(9.5f, 2.7f, 0.24f));
                b.Quad(mats.Billboard, new Vector3(0f, boardY, boardZ + 0.16f),
                    new Vector3(9f, 2.35f, 1f));
                b.Box(mats.GroundDark, new Vector3(-4.6f, boardY * 0.5f, boardZ),
                    new Vector3(0.34f, boardY, 0.34f));
                b.Box(mats.GroundDark, new Vector3(4.6f, boardY * 0.5f, boardZ),
                    new Vector3(0.34f, boardY, 0.34f));
                b.Box(mats.NeonAlt, new Vector3(0f, boardY - 1.55f, boardZ + 0.2f),
                    new Vector3(9.4f, 0.09f, 0.1f));
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

        /// <summary>Luxury tech district: gold-edged glass podiums, topiary and crown-lit towers.</summary>
        private static void BuildLuxury(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(mats.GroundDark, new Vector3(side * 6.7f, 0.16f, half), new Vector3(2.6f, 0.32f, SegmentLength));
                b.Box(mats.Trim, new Vector3(side * 5.44f, 0.34f, half), new Vector3(0.08f, 0.06f, SegmentLength));
                b.Box(mats.BuildingA, new Vector3(side * 8.4f, 2.2f, half), new Vector3(3.2f, 4.4f, SegmentLength));
                for (int i = 0; i < 6; i++)
                {
                    float z = 3.2f + i * 6.6f;
                    b.Box(mats.White, new Vector3(side * 6.76f, 2.2f, z), new Vector3(0.08f, 3.9f, 2.5f));
                    b.Box(mats.NeonSoft, new Vector3(side * 6.71f, 3.7f, z), new Vector3(0.04f, 0.06f, 2.1f));
                    b.Box(mats.Sign, new Vector3(side * 6.68f, 4.5f, z), new Vector3(0.05f, 0.34f, 1.6f));
                    b.Box(mats.WarmLamp, new Vector3(side * 6.6f, 2.85f, z + 1.1f), new Vector3(0.06f, 0.08f, 0.3f));
                }
                for (int i = 0; i < 3; i++)
                {
                    float z = 6f + i * 13f + rng.Range(-1.4f, 1.4f);
                    float x = side * (10.6f + rng.Range(0f, 1.4f));
                    float height = rng.Range(17f, 27f);
                    b.Facade(mats.BuildingB, new Vector3(x, height * 0.5f, z),
                        new Vector3(5.4f, height, 7.8f), new Vector2(i * 0.25f, 0f));
                    b.Box(mats.Roof, new Vector3(x, height + 0.12f, z), new Vector3(5.8f, 0.36f, 8.2f));
                    b.Box(mats.Trim, new Vector3(x - side * 2.67f, height * 0.62f, z),
                        new Vector3(0.12f, height * 0.74f, 0.16f));
                    b.Box(mats.Neon, new Vector3(x - side * 2.72f, height + 0.48f, z),
                        new Vector3(0.05f, 0.08f, 5.4f));
                }
                for (int i = 0; i < 4; i++)
                {
                    float z = 4.5f + i * 9.4f;
                    b.Cylinder(mats.Bark, new Vector3(side * 6.35f, 0.72f, z), new Vector3(0.2f, 1.44f, 0.2f));
                    b.Foliage(mats.Foliage, new Vector3(side * 6.35f, 1.85f, z),
                        new Vector3(1.15f, 1.25f, 1.15f), Quaternion.Euler(0f, i * 24f, 0f));
                }
            }
            b.Box(mats.Trim, new Vector3(0f, 5.7f, half), new Vector3(10.2f, 0.1f, 0.14f));
            b.Box(mats.NeonSoft, new Vector3(0f, 5.58f, half), new Vector3(8.4f, 0.04f, 0.06f));

            // Halcyon-spire signature: twin fountain basins and gilded promenade arches.
            for (int side = -1; side <= 1; side += 2)
            {
                // Fountain: plinth, stone basin, luminous water and a rising jet.
                b.Cylinder(mats.Trim, new Vector3(side * 6.1f, 0.02f, 20f),
                    new Vector3(1.5f, 0.02f, 1.5f));
                b.Cylinder(mats.Podium, new Vector3(side * 6.1f, 0.17f, 20f),
                    new Vector3(1.2f, 0.17f, 1.2f));
                b.Cylinder(mats.NeonSoft, new Vector3(side * 6.1f, 0.3f, 20f),
                    new Vector3(0.95f, 0.1f, 0.95f));
                b.Cylinder(mats.White, new Vector3(side * 6.1f, 0.78f, 20f),
                    new Vector3(0.08f, 0.35f, 0.08f));
                b.Foliage(mats.Neon, new Vector3(side * 6.1f, 1.32f, 20f),
                    new Vector3(0.42f, 0.25f, 0.42f));
                // Gilded arch framing the promenade with a warm cove light.
                float az = 12f;
                for (int p = -1; p <= 1; p += 2)
                    b.Box(mats.Trim, new Vector3(side * 5.9f, 1.7f, az + p * 1.05f),
                        new Vector3(0.14f, 1.7f, 0.16f));
                b.Box(mats.Trim, new Vector3(side * 5.9f, 3.55f, az), new Vector3(0.1f, 0.3f, 2.35f));
                b.Box(mats.WarmLamp, new Vector3(side * 5.78f, 3.44f, az),
                    new Vector3(0.06f, 0.06f, 2.2f));
            }
        }

        /// <summary>Frozen industrial harbor: insulated quays, container stacks and ice cranes.</summary>
        private static void BuildCryoHarbor(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(mats.GroundDark, new Vector3(side * 7.5f, 1.9f, half), new Vector3(1.9f, 3.8f, SegmentLength));
                for (int i = 0; i < 8; i++)
                {
                    float z = 2.6f + i * 5.2f;
                    b.Box(mats.Metal, new Vector3(side * 6.52f, 1.9f, z), new Vector3(0.08f, 3.5f, 4.3f));
                    b.Box(mats.NeonSoft, new Vector3(side * 6.46f, 3.2f, z), new Vector3(0.03f, 0.06f, 3.7f));
                    if (i % 2 == 0)
                        b.Box(mats.NeonAlt, new Vector3(side * 6.4f, 0.62f, z), new Vector3(0.04f, 0.08f, 1.9f));
                }
                for (int i = 0; i < 4; i++)
                {
                    float z = 4f + i * 10f;
                    float x = side * (9.4f + (i % 2) * 1.5f);
                    float y = 1.35f + (i % 2) * 2.45f;
                    Material boxMat = i % 2 == 0 ? mats.BuildingB : mats.Shop;
                    b.Box(boxMat, new Vector3(x, y, z), new Vector3(3.7f, 2.4f, 5.7f),
                        Quaternion.Euler(0f, side * (i % 2 == 0 ? 2f : -3f), 0f));
                    b.Box(mats.Metal, new Vector3(x, y + 1.25f, z), new Vector3(3.82f, 0.1f, 5.82f));
                    b.Box(mats.White, new Vector3(x, y + 1.34f, z), new Vector3(3.5f, 0.08f, 5.5f));
                }
                float craneZ = 11f + (side > 0 ? 11.5f : 0f);
                b.Box(mats.Metal, new Vector3(side * 10.6f, 7f, craneZ), new Vector3(0.55f, 14f, 0.55f));
                b.Box(mats.Metal, new Vector3(side * 8f, 13.6f, craneZ), new Vector3(5.8f, 0.42f, 0.52f));
                b.Box(mats.NeonAlt, new Vector3(side * 5.2f, 13.32f, craneZ), new Vector3(0.14f, 0.12f, 0.14f));
                b.Box(mats.Metal, new Vector3(side * 5.2f, 9.4f, craneZ), new Vector3(0.05f, 8f, 0.05f));
                b.Box(mats.WarmLamp, new Vector3(side * 5.2f, 5.34f, craneZ), new Vector3(0.22f, 0.26f, 0.22f));
                for (int i = 0; i < 4; i++)
                {
                    float z = 3.2f + i * 9.2f;
                    b.Box(mats.White, new Vector3(side * 5.95f, 0.24f, z),
                        new Vector3(1.7f, 0.48f, rng.Range(2.8f, 4.6f)), Quaternion.Euler(0f, rng.Range(-8f, 8f), 0f));
                    b.Box(mats.Pipe, new Vector3(side * 6.9f, 0.32f, z + 1.6f), new Vector3(0.16f, 0.16f, 3.2f));
                }
            }
            b.Box(mats.Metal, new Vector3(0f, 8.6f, 20f), new Vector3(14.5f, 0.34f, 0.34f));
            b.Box(mats.Neon, new Vector3(0f, 8.36f, 20f), new Vector3(9.5f, 0.05f, 0.08f));

            // Cryo-harbor signature: drifting ice floes on the far water and mooring
            // bollards with hawser rings along the quay coping.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 4; i++)
                {
                    float z = 3.5f + i * 9.6f + rng.Range(-1.6f, 1.6f);
                    float x = side * (14.4f + rng.Range(0f, 1.4f));
                    float s = rng.Range(1.6f, 3.1f);
                    b.Box(mats.White, new Vector3(x, 0.12f, z), new Vector3(s, 0.24f, s * 0.8f),
                        Quaternion.Euler(0f, rng.Range(-14f, 14f), 0f));
                    b.Box(mats.NeonSoft, new Vector3(x, 0.26f, z), new Vector3(s * 0.7f, 0.03f, s * 0.55f),
                        Quaternion.Euler(0f, rng.Range(-10f, 10f), 0f));
                }
                for (int i = 0; i < 3; i++)
                {
                    float z = 5f + i * 12.4f;
                    b.Cylinder(mats.Metal, new Vector3(side * 6.2f, 0.3f, z),
                        new Vector3(0.3f, 0.3f, 0.3f));
                    b.Box(mats.NeonAlt, new Vector3(side * 6.2f, 0.62f, z),
                        new Vector3(0.1f, 0.06f, 0.34f));
                    b.Box(mats.Rock, new Vector3(side * 6.2f, 0.06f, z),
                        new Vector3(1.1f, 0.12f, 1.1f));
                }
            }
        }

        /// <summary>Alpine village: timber chalets, deep gables, flower boxes and a cable car.</summary>
        private static void BuildAlpineVillage(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    float z = 4.5f + i * 13.2f + rng.Range(-0.8f, 0.8f);
                    float x = side * 9.8f;
                    float width = 5.2f;
                    b.Box(mats.GroundDark, new Vector3(x, 0.55f, z), new Vector3(width + 0.5f, 1.1f, 6.8f));
                    b.Facade(mats.BuildingA, new Vector3(x, 3.1f, z), new Vector3(width, 4.2f, 6.2f),
                        new Vector2(i * 0.25f, 0.25f));
                    b.Box(mats.Roof, new Vector3(x, 5.38f, z - 1.55f), new Vector3(width + 0.9f, 0.22f, 3.4f),
                        Quaternion.Euler(side * 24f, 0f, 0f));
                    b.Box(mats.Roof, new Vector3(x, 5.38f, z + 1.55f), new Vector3(width + 0.9f, 0.22f, 3.4f),
                        Quaternion.Euler(-side * 24f, 0f, 0f));
                    float nearX = x - side * (width * 0.5f + 0.03f);
                    b.Box(mats.Trim, new Vector3(nearX, 4.35f, z), new Vector3(0.16f, 0.1f, 4.4f));
                    b.Box(mats.Trim, new Vector3(nearX, 3.2f, z), new Vector3(0.12f, 0.09f, 4.2f));
                    for (int post = -1; post <= 1; post++)
                        b.Box(mats.Trim, new Vector3(nearX, 3.8f, z + post * 1.5f), new Vector3(0.08f, 1.1f, 0.08f));
                    for (int flower = 0; flower < 3; flower++)
                        b.Foliage(mats.Foliage, new Vector3(nearX - side * 0.18f, 4.52f, z - 1.4f + flower * 1.4f),
                            new Vector3(0.52f, 0.28f, 0.52f), Quaternion.Euler(0f, flower * 33f, 0f));
                    for (int lamp = -1; lamp <= 1; lamp += 2)
                        b.Box(mats.WarmLamp, new Vector3(nearX - side * 0.1f, 2.65f, z + lamp * 1.55f),
                            new Vector3(0.05f, 0.32f, 0.24f));
                }
                b.Box(mats.Metal, new Vector3(side * 7.35f, 4.8f, half), new Vector3(0.34f, 9.6f, 0.34f));
                b.Box(mats.Metal, new Vector3(side * 6.6f, 9.5f, half), new Vector3(1.8f, 0.16f, 0.16f));
                for (int i = 0; i < 4; i++)
                {
                    float z = 3f + i * 11f;
                    float x = side * (11.4f + rng.Range(0f, 1.6f));
                    b.Cylinder(mats.Bark, new Vector3(x, 1.4f, z), new Vector3(0.22f, 2.8f, 0.22f));
                    for (int tier = 0; tier < 3; tier++)
                        b.Foliage(mats.FoliageDark, new Vector3(x, 2.9f + tier * 0.95f, z),
                            new Vector3(2.35f - tier * 0.52f, 1.15f, 2.35f - tier * 0.52f),
                            Quaternion.Euler(0f, i * 29f + tier * 17f, 0f));
                    b.Foliage(mats.White, new Vector3(x, 4.88f, z), new Vector3(1.05f, 0.28f, 1.05f));
                }
            }
            b.Box(mats.Metal, new Vector3(0f, 10.3f, half), new Vector3(13.6f, 0.05f, 0.05f));
            b.Box(mats.NeonSoft, new Vector3(0f, 10.24f, half), new Vector3(6.2f, 0.02f, 0.03f));
            b.Box(mats.Trim, new Vector3(2.8f, 9.82f, half), new Vector3(0.9f, 0.72f, 1.25f));
            b.Box(mats.WarmLamp, new Vector3(2.8f, 9.58f, half - 0.63f), new Vector3(0.66f, 0.06f, 0.03f));
        }

        /// <summary>Holographic river city: glass canals, media facades and an elevated maglev.</summary>
        private static void BuildSeoul(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(mats.GroundDark, new Vector3(side * 6.9f, -0.48f, half), new Vector3(2.7f, 0.68f, SegmentLength));
                b.Box(mats.WetGlow, new Vector3(side * 6.65f, -0.12f, half), new Vector3(2.2f, 0.03f, SegmentLength));
                b.Box(mats.NeonSoft, new Vector3(side * 5.52f, 0.6f, half), new Vector3(0.06f, 0.05f, SegmentLength));
                for (int i = 0; i < 10; i++)
                    b.Box(mats.Metal, new Vector3(side * 5.54f, 0.94f, 2f + i * 4.1f), new Vector3(0.05f, 0.7f, 0.05f));
                for (int i = 0; i < 3; i++)
                {
                    float z = 6f + i * 13f + rng.Range(-0.8f, 0.8f);
                    float x = side * (10.4f + rng.Range(0f, 1.2f));
                    b.Facade(mats.BuildingB, new Vector3(x, 9f, z), new Vector3(5.8f, 18f, 7.4f),
                        new Vector2(i * 0.25f, 0.5f));
                    b.Box(mats.Roof, new Vector3(x, 18.15f, z), new Vector3(6.1f, 0.34f, 7.7f));
                    float screenX = side * 7.46f;
                    b.Box(mats.Sign, new Vector3(screenX, 10.6f, z), new Vector3(0.09f, 5.8f, 3.6f));
                    b.Box(mats.Neon, new Vector3(screenX - side * 0.06f, 10.6f, z - 1.72f),
                        new Vector3(0.04f, 5.4f, 0.08f));
                    b.Box(mats.NeonAlt, new Vector3(screenX - side * 0.06f, 10.6f, z + 1.72f),
                        new Vector3(0.04f, 5.4f, 0.08f));
                    for (int band = 0; band < 4; band++)
                        b.Box(mats.NeonSoft, new Vector3(screenX - side * 0.07f, 8.4f + band * 1.45f, z),
                            new Vector3(0.03f, 0.05f, 3.1f));
                }
                b.Box(mats.Metal, new Vector3(side * 8.7f, 6.3f, half), new Vector3(0.55f, 0.4f, SegmentLength));
                for (int i = 0; i < 3; i++)
                    b.Box(mats.Metal, new Vector3(side * 8.7f, 3.1f, 6f + i * 14f), new Vector3(0.38f, 6.2f, 0.38f));
                if (side > 0)
                {
                    b.Box(mats.White, new Vector3(side * 8.7f, 6.92f, half), new Vector3(1.5f, 0.9f, 8f));
                    b.Box(mats.Neon, new Vector3(side * 8.7f, 6.72f, half), new Vector3(1.56f, 0.06f, 7.4f));
                    b.Box(mats.NeonAlt, new Vector3(side * 8.7f, 7.28f, half), new Vector3(1.1f, 0.04f, 5.8f));
                }
                for (int i = 0; i < 3; i++)
                    b.Box(mats.NeonSoft, new Vector3(side * 4.92f, 4.1f + i * 0.34f, 7f + i * 9f),
                        new Vector3(0.03f, 0.16f, 1.3f), Quaternion.Euler(0f, side * 18f, 0f));
            }

            // Seoul-nexus signature: laminated glass splash walls and pedestrian bridge arcs
            // tow-stretching over the canal water.
            for (int side = -1; side <= 1; side += 2)
            {
                // Glass splash walls along the canal coping, lit from below.
                for (int w = 0; w < 3; w++)
                {
                    float z = 7f + w * 13f;
                    b.Box(mats.NeonSoft, new Vector3(side * 6.06f, 0.62f, z), new Vector3(0.04f, 1.2f, 4.4f));
                    b.Box(mats.Metal, new Vector3(side * 6.08f, 1.26f, z), new Vector3(0.06f, 0.08f, 4.5f));
                }
                // Two arcs stepping over the canal with under-lighting on the waterline.
                for (int arc = 0; arc < 2; arc++)
                {
                    float z = 12f + arc * 16f;
                    for (int k = 0; k <= 4; k++)
                    {
                        float t = k / 4f;
                        float x = side * (5.9f + 1.7f * t);
                        float y = 0.5f + Mathf.Sin(t * Mathf.PI) * 0.85f;
                        b.Box(mats.Metal, new Vector3(x, y, z), new Vector3(0.7f, 0.12f, 0.9f));
                    }
                    b.Box(mats.Neon, new Vector3(side * 6.75f, 1.36f, z), new Vector3(3.4f, 0.05f, 0.5f));
                    b.Quad(mats.WetGlowAlt, new Vector3(side * 6.75f, 0.05f, z),
                        new Vector3(3.6f, 3.4f, 1f), Quaternion.Euler(90f, 0f, 0f));
                }
            }
        }

        /// <summary>Giant skybridge: deck-edge glass, mega pylons and suspended garden capsules.</summary>
        private static void BuildSkybridge(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(mats.Metal, new Vector3(side * 5.15f, 0.56f, half), new Vector3(0.14f, 1.12f, SegmentLength));
                b.Box(mats.NeonSoft, new Vector3(side * 5.08f, 1.14f, half), new Vector3(0.04f, 0.05f, SegmentLength));
                for (int i = 0; i < 9; i++)
                    b.Box(mats.Metal, new Vector3(side * 5.15f, 0.9f, 1.8f + i * 4.6f), new Vector3(0.08f, 0.5f, 0.08f));
                for (int i = 0; i < 2; i++)
                {
                    float z = 8f + i * 22f;
                    float x = side * 11.5f;
                    b.Facade(mats.BuildingA, new Vector3(x, 15f, z), new Vector3(7f, 30f, 9.5f),
                        new Vector2(i * 0.25f, 0f));
                    b.Box(mats.Roof, new Vector3(x, 30.2f, z), new Vector3(7.4f, 0.4f, 9.9f));
                    b.Box(mats.Neon, new Vector3(x - side * 3.52f, 23f, z), new Vector3(0.06f, 12f, 0.14f));
                    b.Box(mats.NeonAlt, new Vector3(x - side * 3.52f, 28.4f, z), new Vector3(0.06f, 0.08f, 6.2f));
                }
                b.Box(mats.Metal, new Vector3(side * 7.4f, 10.4f, half), new Vector3(0.42f, 20.8f, 0.42f));
                b.Box(mats.Neon, new Vector3(side * 7.4f, 20.9f, half), new Vector3(0.15f, 0.1f, 0.15f));
                for (int i = 0; i < 6; i++)
                {
                    float z = 3f + i * 7.4f;
                    b.Box(mats.Metal, new Vector3(side * 5.4f, 14f, z), new Vector3(0.05f, 12f, 0.05f));
                    b.Box(mats.NeonSoft, new Vector3(side * 5.36f, 8.2f, z), new Vector3(0.03f, 0.05f, 0.05f));
                }
                for (int i = 0; i < 3; i++)
                {
                    float z = 5f + i * 13f;
                    b.Cylinder(mats.Metal, new Vector3(side * 10.2f, 8.4f, z), new Vector3(1.35f, 1.5f, 1.35f));
                    b.Cylinder(mats.Neon, new Vector3(side * 10.2f, 9.2f, z), new Vector3(1.15f, 0.06f, 1.15f));
                    b.Foliage(mats.Foliage, new Vector3(side * 10.2f, 10.1f, z), new Vector3(2.3f, 1.5f, 2.3f),
                        Quaternion.Euler(0f, i * 31f, 0f));
                }
            }
            b.Box(mats.Metal, new Vector3(0f, 20.2f, half), new Vector3(15.4f, 0.3f, 0.4f));
            b.Box(mats.Neon, new Vector3(0f, 19.96f, half), new Vector3(11.2f, 0.05f, 0.08f));
            b.Box(mats.Metal, new Vector3(0f, 12.4f, half), new Vector3(13.2f, 0.16f, 0.22f));

            // Skyway-09 signature: catenary power arcs spanning the deck between pylons.
            for (int a = 0; a < 2; a++)
            {
                float z = 10f + a * 21f;
                for (int k = 0; k <= 5; k++)
                {
                    float t = k / 5f;
                    float x = -7.6f + 15.2f * t;
                    float y = 14.6f - Mathf.Sin(t * Mathf.PI) * 2.3f;
                    b.Box(mats.Metal, new Vector3(x, y, z), new Vector3(0.1f, 0.14f, 0.14f));
                    b.Box(mats.Neon, new Vector3(x, y - 0.14f, z), new Vector3(0.07f, 0.03f, 0.07f));
                }
                // Insulator posts reaching up to each arc end.
                for (int side = -1; side <= 1; side += 2)
                    b.Cylinder(mats.White, new Vector3(side * 7.6f, 13f, z),
                        new Vector3(0.3f, 1.6f, 0.3f));
            }
        }

        /// <summary>Spaceport city: launch gantries, fuel tanks, radar dishes and floodlights.</summary>
        private static void BuildSpaceport(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(mats.GroundDark, new Vector3(side * 7.7f, 2.2f, half), new Vector3(1.9f, 4.4f, SegmentLength));
                for (int i = 0; i < 8; i++)
                {
                    float z = 2.4f + i * 5.1f;
                    b.Box(mats.Metal, new Vector3(side * 6.7f, 2.2f, z), new Vector3(0.16f, 4.2f, 0.24f));
                    b.Box(mats.Paint, new Vector3(side * 6.58f, 0.72f, z), new Vector3(0.05f, 0.5f, 2.4f),
                        Quaternion.Euler(0f, side * 22f, 0f));
                    b.Box(mats.NeonAlt, new Vector3(side * 6.55f, 3.6f, z), new Vector3(0.04f, 0.14f, 1.5f));
                }
                float gantryZ = 7f + (side > 0 ? 15f : 0f);
                for (int leg = -1; leg <= 1; leg += 2)
                    b.Box(mats.Metal, new Vector3(side * 10.2f, 10f, gantryZ + leg * 1.5f),
                        new Vector3(0.55f, 20f, 0.55f));
                for (int level = 1; level <= 4; level++)
                    b.Box(mats.Metal, new Vector3(side * 10.2f, level * 4.2f, gantryZ),
                        new Vector3(0.22f, 0.22f, 3.8f));
                b.Box(mats.Metal, new Vector3(side * 8.9f, 17.4f, gantryZ), new Vector3(3.4f, 0.45f, 0.55f));
                b.Box(mats.WarmLamp, new Vector3(side * 7.3f, 17.1f, gantryZ), new Vector3(0.2f, 0.26f, 0.28f));
                b.Box(mats.Metal, new Vector3(side * 7.4f, 9.6f, gantryZ), new Vector3(3.8f, 0.24f, 0.3f));
                b.Cylinder(mats.Metal, new Vector3(side * 10.8f, 4.3f, 25f), new Vector3(2.4f, 8.6f, 2.4f));
                b.Cylinder(mats.Neon, new Vector3(side * 10.8f, 8.7f, 25f), new Vector3(2.5f, 0.1f, 2.5f));
                b.Cylinder(mats.Pipe, new Vector3(side * 8.2f, 1.1f, 25f), new Vector3(0.24f, 6.6f, 0.24f),
                    Quaternion.Euler(0f, 0f, 90f));
                b.Cylinder(mats.Metal, new Vector3(side * 9.5f, 7.2f, 34f), new Vector3(2.3f, 0.24f, 2.3f),
                    Quaternion.Euler(side * 32f, 0f, 0f));
                b.Box(mats.Metal, new Vector3(side * 6.45f, 3.4f, 12f), new Vector3(0.16f, 6.8f, 0.16f));
                b.Box(mats.Neon, new Vector3(side * 6.38f, 6.7f, 12f), new Vector3(0.1f, 0.28f, 0.55f));
            }
            b.Box(mats.Metal, new Vector3(0f, 9.5f, 20f), new Vector3(14.8f, 0.36f, 0.4f));
            b.Box(mats.Neon, new Vector3(0f, 9.24f, 20f), new Vector3(10f, 0.06f, 0.1f));
            b.Box(mats.Sign, new Vector3(0f, 8.55f, 20f), new Vector3(4.4f, 1.1f, 0.08f));

            // Kessler-reach signature: a two-stage stack on a launch plinth with hold-down
            // claws and an engine glow deck, mirrored behind each gantry line.
            for (int side = -1; side <= 1; side += 2)
            {
                float px = side * 13.4f;
                float pz = 24f;
                // Launch plinth and scorch deck.
                b.Cylinder(mats.GroundDark, new Vector3(px, 0.25f, pz), new Vector3(3f, 0.25f, 3f));
                b.Cylinder(mats.WarmGlow, new Vector3(px, 0.53f, pz), new Vector3(1.7f, 0.02f, 1.7f));
                // First stage, interstage and payload fairing.
                b.Cylinder(mats.White, new Vector3(px, 2.3f, pz), new Vector3(1.5f, 2f, 1.5f));
                b.Cylinder(mats.Paint, new Vector3(px, 4.7f, pz), new Vector3(1.52f, 0.45f, 1.52f));
                b.Cylinder(mats.White, new Vector3(px, 6.2f, pz), new Vector3(1.5f, 1.6f, 1.5f));
                b.Cylinder(mats.BuildingB, new Vector3(px, 7.7f, pz), new Vector3(1.65f, 0.6f, 1.65f));
                b.Foliage(mats.Neon, new Vector3(px, 8.5f, pz), new Vector3(1.2f, 0.9f, 1.2f));
                // Hold-down claws gripping the first stage skirt.
                for (int claw = 0; claw < 4; claw++)
                {
                    float ang = claw * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                    float cx = px + Mathf.Cos(ang) * 1.35f;
                    float cz = pz + Mathf.Sin(ang) * 1.35f;
                    b.Box(mats.Metal, new Vector3(cx, 0.75f, cz), new Vector3(0.22f, 1.1f, 0.22f));
                    b.Box(mats.NeonAlt, new Vector3(cx, 1.34f, cz), new Vector3(0.24f, 0.06f, 0.24f));
                }
                // Propellant ducts running from the gantry line to the plinth.
                for (int duct = -1; duct <= 1; duct += 2)
                    b.Box(mats.Pipe, new Vector3(side * 11.1f, 0.4f, pz + duct * 3.6f),
                        new Vector3(4.6f, 0.2f, 0.5f));
            }
        }

        /// <summary>Abandoned cyber city: collapsed slabs, exposed rebar and dead signage.</summary>
        private static void BuildRuins(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 5; i++)
                {
                    float z = 2.6f + i * 8.1f + rng.Range(-0.8f, 0.8f);
                    float height = rng.Range(2.6f, 7.2f);
                    float x = side * (9.4f + rng.Range(0f, 1.2f));
                    b.Box(mats.GroundDark, new Vector3(x, height * 0.5f, z),
                        new Vector3(4.6f, height, rng.Range(4.2f, 6.2f)));
                    b.Box(mats.Roof, new Vector3(x, height + 0.12f, z),
                        new Vector3(2.2f, 0.34f, rng.Range(1.8f, 3.2f)),
                        Quaternion.Euler(rng.Range(-9f, 9f), rng.Range(-16f, 16f), side * rng.Range(4f, 14f)));
                    for (int rod = 0; rod < 3; rod++)
                        b.Box(mats.Metal, new Vector3(x - side * 2.1f, height + 0.5f, z - 1.4f + rod * 1.3f),
                            new Vector3(0.06f, rng.Range(0.7f, 1.8f), 0.06f),
                            Quaternion.Euler(rng.Range(-18f, 18f), 0f, side * rng.Range(-12f, 12f)));
                    b.Box(mats.SignAlt, new Vector3(side * 7.06f, 1.75f, z), new Vector3(0.04f, 0.42f, 2.3f));
                    if (i % 2 == 0)
                        b.Box(mats.NeonAlt, new Vector3(side * 7.02f, 1.75f, z), new Vector3(0.03f, 0.06f, 1.7f));
                }
                for (int i = 0; i < 6; i++)
                {
                    float z = 3.4f + i * 6.2f;
                    b.Box(mats.GroundDark, new Vector3(side * (5.85f + rng.Range(0f, 0.7f)), 0.22f, z),
                        new Vector3(rng.Range(0.7f, 1.5f), rng.Range(0.28f, 0.55f), rng.Range(1.1f, 2.1f)),
                        Quaternion.Euler(rng.Range(-8f, 8f), rng.Range(0f, 360f), rng.Range(-10f, 10f)));
                }
                b.Box(mats.Pipe, new Vector3(side * 6.4f, 2.6f, half), new Vector3(0.22f, 5.2f, 0.22f));
                b.Box(mats.NeonSoft, new Vector3(side * 6.3f, 4.9f, half), new Vector3(0.04f, 0.08f, 3.4f));
                for (int i = 0; i < 2; i++)
                {
                    float z = 10f + i * 19f;
                    b.Box(mats.GroundDark, new Vector3(side * 7.8f, 0.8f, z), new Vector3(2.6f, 1.6f, 4.8f),
                        Quaternion.Euler(0f, side * 24f, side * 12f));
                }
            }
            b.Box(mats.Metal, new Vector3(0f, 7.2f, 20f), new Vector3(13.4f, 0.24f, 0.3f));
            b.Box(mats.SignAlt, new Vector3(0f, 6.72f, 20f), new Vector3(5.2f, 0.62f, 0.06f));
            b.Box(mats.NeonSoft, new Vector3(0f, 6.42f, 20f), new Vector3(3.4f, 0.03f, 0.05f));

            // Null-cathedral signature: a fallen monolith and a cracked standing twin with
            // dead cables strung between the surviving poles.
            for (int side = -1; side <= 1; side += 2)
            {
                // Fallen slab resing diagonally on a rubble cone.
                b.Box(mats.BuildingB, new Vector3(side * 10.6f, 2.9f, 15f),
                    new Vector3(1.1f, 13f, 4.4f), Quaternion.Euler(6f, side * 14f, side * 62f));
                b.Foliage(mats.GroundDark, new Vector3(side * 12.9f, 0.5f, 17.4f),
                    new Vector3(3.4f, 1.15f, 3.8f), Quaternion.Euler(0f, side * 24f, 0f));
                b.Box(mats.GroundDark, new Vector3(side * 11.9f, 0.45f, 15.4f),
                    new Vector3(1.7f, 0.9f, 2.4f), Quaternion.Euler(0f, 0f, side * 20f));
                // Standing twin with a glowing fault seam.
                b.Box(mats.BuildingB, new Vector3(side * 12.3f, 5.1f, 33.5f),
                    new Vector3(1.5f, 10.2f, 3.1f));
                b.Box(mats.NeonAlt, new Vector3(side * 11.5f, 5.1f, 33.5f),
                    new Vector3(0.06f, 7.4f, 0.3f));
                // Dipping dead cables from the pipe pole toward the slab.
                for (int k = 0; k <= 4; k++)
                {
                    float t = k / 4f;
                    float x = side * (6.6f + 3.4f * t);
                    float y = 4.8f - Mathf.Sin(t * Mathf.PI) * 1.6f;
                    b.Box(mats.Metal, new Vector3(x, y, 24.5f + side * 2f),
                        new Vector3(0.9f, 0.07f, 0.07f), Quaternion.Euler(0f, side * 6f, 0f));
                }
            }
        }

        /// <summary>Snowbound highway: ploughed banks, frosted service walls and conifer stands.</summary>
        private static void BuildSnow(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                // Ploughed snow banks sit over the sidewalk and recede from the running lanes.
                for (int i = 0; i < 7; i++)
                {
                    float z = 2.5f + i * 6.2f + rng.Range(-0.9f, 0.9f);
                    float h = rng.Range(0.4f, 0.95f);
                    b.Box(mats.White, new Vector3(side * 6.05f, 0.18f + h * 0.5f, z),
                        new Vector3(2.1f, h, rng.Range(3.4f, 5.6f)),
                        Quaternion.Euler(0f, rng.Range(-7f, 7f), 0f));
                }
                // Insulated service wall, heat trace and frozen pipework.
                b.Box(mats.GroundDark, new Vector3(side * 8.4f, 2.7f, half), new Vector3(1.5f, 5.4f, SegmentLength));
                for (int i = 0; i < 6; i++)
                {
                    float z = 3f + i * 7f;
                    b.Box(mats.Metal, new Vector3(side * 7.62f, 2.7f, z), new Vector3(0.18f, 5.2f, 0.24f));
                    b.Cylinder(mats.Pipe, new Vector3(side * 7.36f, 1.45f, z + 2.1f),
                        new Vector3(0.16f, 2.6f, 0.16f), Quaternion.Euler(90f, 0f, 0f));
                    b.Box(mats.NeonSoft, new Vector3(side * 7.56f, 3.1f, z + 1.6f), new Vector3(0.05f, 0.07f, 4.6f));
                }
                // Snow-laden conifers beyond the wall.
                for (int i = 0; i < 3; i++)
                {
                    float z = 6f + i * 13.5f + rng.Range(-2.2f, 2.2f);
                    float x = side * (10.2f + rng.Range(0f, 2.2f));
                    b.Cylinder(mats.Bark, new Vector3(x, 1.5f, z), new Vector3(0.24f, 3f, 0.24f));
                    for (int tier = 0; tier < 3; tier++)
                        b.Foliage(mats.FoliageDark, new Vector3(x, 3.1f + tier * 1.05f, z),
                            new Vector3(2.5f - tier * 0.52f, 1.3f, 2.5f - tier * 0.52f),
                            Quaternion.Euler(0f, i * 37f + tier * 19f, 0f));
                    b.Foliage(mats.White, new Vector3(x, 5.15f, z), new Vector3(1.15f, 0.32f, 1.15f));
                }
                // Heated shelter with a warm lamp cone.
                if (rng.Chance(0.7f))
                {
                    float z = 15f + rng.Range(-4f, 10f);
                    b.Box(mats.Metal, new Vector3(side * 6.9f, 2.4f, z), new Vector3(1.7f, 0.16f, 3.4f),
                        Quaternion.Euler(0f, 0f, side * 6f));
                    for (int post = -1; post <= 1; post += 2)
                        b.Box(mats.Metal, new Vector3(side * 6.9f, 1.2f, z + post * 1.4f), new Vector3(0.1f, 2.4f, 0.1f));
                    b.Box(mats.WarmLamp, new Vector3(side * 7.2f, 2.15f, z - 1.2f), new Vector3(0.2f, 0.16f, 0.7f));
                    b.Quad(mats.WarmGlow, new Vector3(side * 6.9f, 0.22f, z),
                        new Vector3(2.2f, 3.1f, 1f), Quaternion.Euler(90f, 0f, 0f));
                }
            }
            // Snow guards over the road so the flakes read as weather, not a screen overlay.
            for (int i = 0; i < 2; i++)
            {
                float z = 10f + i * 21f;
                b.Box(mats.Metal, new Vector3(0f, 6.4f, z), new Vector3(9.8f, 0.14f, 0.18f));
                b.Box(mats.NeonSoft, new Vector3(0f, 6.28f, z), new Vector3(7.6f, 0.04f, 0.06f));
            }

            // Maple-frost signature: slatted avalanche fences and wind-polished ice boulders.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 2; i++)
                {
                    float z = 9f + i * 19f;
                    for (int p = 0; p < 4; p++)
                        b.Box(mats.Wood, new Vector3(side * 9.6f, 0.62f, z + p * 2f),
                            new Vector3(0.14f, 1.24f, 0.14f));
                    for (int lath = -1; lath <= 1; lath++)
                        b.Box(mats.Wood, new Vector3(side * 9.6f, 0.66f + lath * 0.44f, z + 3f),
                            new Vector3(0.06f, 0.2f, 6.4f));
                    b.Box(mats.Snow, new Vector3(side * 9.6f, 0.1f, z + 3f),
                        new Vector3(4.2f, 0.2f, 7f));
                }
                for (int i = 0; i < 3; i++)
                {
                    float z = 6f + i * 12f + rng.Range(-2.6f, 2.6f);
                    float x = side * (12.6f + rng.Range(0f, 2f));
                    float s = rng.Range(1.2f, 2.4f);
                    b.Foliage(mats.Rock, new Vector3(x, s * 0.42f, z), new Vector3(s, s * 0.8f, s * 0.9f),
                        Quaternion.Euler(0f, rng.Range(0f, 180f), 0f));
                    b.Foliage(mats.Snow, new Vector3(x, s * 0.78f, z),
                        new Vector3(s * 0.62f, s * 0.24f, s * 0.6f));
                }
            }
        }

        /// <summary>Desert megastructure: sun-bleached pylons, solar arrays and dune berms.</summary>
        private static void BuildDesert(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                // Stone service wall with deep carved window slots.
                b.Box(mats.BuildingA, new Vector3(side * 8.6f, 2.2f, half), new Vector3(1.8f, 4.4f, SegmentLength));
                for (int i = 0; i < 6; i++)
                {
                    float z = 4f + i * 6.6f;
                    b.Box(mats.BuildingB, new Vector3(side * 7.62f, 2.3f, z), new Vector3(0.34f, 4.4f, 0.9f));
                    b.Box(mats.WarmLamp, new Vector3(side * 7.42f, 2.5f, z), new Vector3(0.06f, 0.7f, 0.28f));
                }
                // Dune berms.
                for (int i = 0; i < 4; i++)
                {
                    float z = 3f + i * 11f + rng.Range(-1.5f, 1.5f);
                    b.Box(mats.Trim, new Vector3(side * (5.85f + rng.Range(0f, 0.7f)), 0.22f, z),
                        new Vector3(1.5f, 0.44f, rng.Range(4.5f, 7.5f)),
                        Quaternion.Euler(0f, rng.Range(-9f, 9f), side * -4f));
                }
                // Photovoltaic field tilted toward the sun.
                for (int i = 0; i < 3; i++)
                {
                    float z = 8f + i * 11f;
                    b.Box(mats.Metal, new Vector3(side * 10.6f, 1.1f, z), new Vector3(0.1f, 2.2f, 0.1f));
                    b.Box(mats.NeonAlt, new Vector3(side * 10.6f, 2.5f, z), new Vector3(4.4f, 0.08f, 2.3f),
                        Quaternion.Euler(0f, 0f, side * -22f));
                    b.Box(mats.Trim, new Vector3(side * 10.6f, 2.35f, z - 1.05f), new Vector3(4.2f, 0.05f, 0.08f));
                }
                // Wind turbine with three blades.
                if (rng.Chance(0.65f))
                {
                    float z = 18f + rng.Range(-6f, 8f);
                    float x = side * 12.5f;
                    b.Cylinder(mats.White, new Vector3(x, 4.4f, z), new Vector3(0.16f, 8.8f, 0.16f));
                    b.Cylinder(mats.Metal, new Vector3(x, 8.7f, z), new Vector3(0.24f, 0.24f, 0.24f), Quaternion.Euler(90f, 0f, 0f));
                    for (int blade = 0; blade < 3; blade++)
                        b.Box(mats.White, new Vector3(x, 8.7f, z), new Vector3(0.1f, 3.2f, 0.34f),
                            Quaternion.Euler(blade * 120f, 0f, 0f));
                }
                // Rock outcrops.
                for (int i = 0; i < 2; i++)
                    b.Foliage(mats.BuildingB, new Vector3(side * (9.7f + rng.Range(0f, 1.6f)), 0.35f, rng.Range(3f, 39f)),
                        new Vector3(1.6f, 0.8f, 1.4f), Quaternion.Euler(0f, rng.Range(0f, 180f), 0f));
            }
            // Shade sail gantry.
            b.Box(mats.Trim, new Vector3(0f, 6.6f, 23f), new Vector3(11.5f, 0.16f, 0.22f));
            b.Quad(mats.White, new Vector3(0f, 6.2f, 22.4f), new Vector3(9.6f, 4.6f, 1f), Quaternion.Euler(90f, 0f, 0f));

            // Hot-side signature: a massive cooling dish anchored outside the trench.
            if (theme.id == "solar-arcology")
            {
                float ds = 26f;
                for (int i = 0; i < 2; i++)
                {
                    float x = (i == 0 ? -1f : 1f) * 15.4f;
                    b.Cylinder(mats.Metal, new Vector3(x, 0.8f, ds), new Vector3(1.3f, 1.6f, 1.3f));
                    b.Cylinder(mats.White, new Vector3(x, 5.4f, ds), new Vector3(4.6f, 0.5f, 4.6f),
                        Quaternion.Euler(i == 0 ? 24f : -24f, 0f, 0f));
                    b.Cylinder(mats.Sand, new Vector3(x, 5.4f, ds + (i == 0 ? 2.1f : -2.1f)),
                        new Vector3(4.7f, 0.12f, 4.7f), Quaternion.Euler(i == 0 ? 18f : -18f, 0f, 0f));
                    b.Cylinder(mats.Neon, new Vector3(x, 6.7f, ds), new Vector3(0.5f, 1.4f, 0.5f));
                }
            }
            else
            {
                // Dune-meridian: sandstone souk tents and teal-glass megablocks.
                float ds = 24f;
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float z = 5f + i * 8.4f;
                        float x = side * (11.6f + rng.Range(0f, 1.5f));
                        b.Facade(mats.Sand, new Vector3(x, 2.6f, z), new Vector3(4.8f, 5.2f, 7.2f),
                            new Vector2(i * 0.3f, 0.2f));
                        b.Box(mats.NeonAlt, new Vector3(x - side * 2.42f, 3.4f, z),
                            new Vector3(0.06f, 2.6f, 5f));
                        b.Box(mats.Trim, new Vector3(x, 5.4f, z), new Vector3(5.2f, 0.35f, 7.6f));
                    }
                    // Palm row (curved trunk plus frond fans).
                    for (int i = 0; i < 3; i++)
                        AddPalm(b, mats, side, 7.8f + i * 12f, side * 6.6f, rng);
                }
                b.Box(mats.Sand, new Vector3(0f, 0.22f, ds - 22f), new Vector3(6.4f, 0.3f, 18f),
                    Quaternion.Euler(0f, 7f, 0f));
            }
        }

        /// <summary>
        /// Curved-trunk palm with a five-frond fan. Cheap silhouette reads tropical at speed.
        /// </summary>
        private static void AddPalm(DecorMeshBuilder b, ThemeMaterials mats, int bend, float z, float x,
            DeterministicRandom rng)
        {
            float lean = rail(bend, 0.35f);
            b.Cylinder(mats.Bark, new Vector3(x + lean * 0.35f, 1.35f, z), new Vector3(0.17f, 2.7f, 0.17f),
                Quaternion.Euler(0f, 0f, -lean * 32f));
            b.Cylinder(mats.Bark, new Vector3(x + lean * 0.85f, 3.3f, z), new Vector3(0.12f, 1.6f, 0.12f),
                Quaternion.Euler(0f, 0f, -lean * 46f));
            for (int frond = 0; frond < 5; frond++)
            {
                float angle = frond / 5f * Mathf.PI * 2f;
                b.Cylinder(mats.Foliage, new Vector3(x + lean * 1.15f, 3.95f, z),
                    new Vector3(0.65f, 0.07f, 0.22f),
                    Quaternion.Euler(0f, angle * Mathf.Rad2Deg, -22f + frond * 4f));
            }
        }

        private static float rail(int side, float value) => side * value;

        /// <summary>Coastal highway: water aprons, boardwalk rails, coral towers and a lighthouse.</summary>
        private static void BuildCoast(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                // Animated-looking ocean panels beyond the service walk.
                b.Box(mats.Puddle, new Vector3(side * 14.5f, -0.32f, half), new Vector3(17f, 0.07f, SegmentLength + 0.2f));
                b.Box(mats.GroundDark, new Vector3(side * 7.1f, -0.2f, half), new Vector3(3.2f, 0.5f, SegmentLength));
                // Boardwalk rails and pilings.
                for (int i = 0; i < 8; i++)
                {
                    float z = 2.5f + i * 5.4f;
                    b.Cylinder(mats.Bark, new Vector3(side * 6.35f, -0.2f, z), new Vector3(0.16f, 2.2f, 0.16f));
                    b.Box(mats.Metal, new Vector3(side * 6.28f, 0.98f, z), new Vector3(0.1f, 0.1f, 5.4f));
                }
                b.Box(mats.Metal, new Vector3(side * 6.28f, 1.05f, half), new Vector3(0.13f, 0.06f, SegmentLength));
                // Coral growths in the shallows.
                for (int i = 0; i < 3; i++)
                {
                    float z = 6f + i * 12f + rng.Range(-2f, 2f);
                    float x = side * (8.4f + rng.Range(0f, 2.2f));
                    float h = rng.Range(1.2f, 2.8f);
                    b.Cylinder(mats.NeonSoft, new Vector3(x, h * 0.5f - 0.1f, z), new Vector3(0.34f, h, 0.34f),
                        Quaternion.Euler(rng.Range(-16f, 16f), 0f, rng.Range(-16f, 16f)));
                    b.Cylinder(mats.FoliageLight, new Vector3(x + side * 0.4f, h * 0.34f, z + 0.4f),
                        new Vector3(0.6f, h * 0.72f, 0.6f), Quaternion.Euler(rng.Range(-22f, 22f), 0f, 0f));
                    b.Cylinder(mats.Neon, new Vector3(x, h + 0.06f, z), new Vector3(0.16f, 0.08f, 0.16f));
                }
                // Lighthouse silhouette on every other segment.
                if (rng.Chance(0.5f))
                {
                    float z = 20f + rng.Range(-8f, 8f);
                    float x = side * 14.4f;
                    b.Cylinder(mats.White, new Vector3(x, 4.2f, z), new Vector3(1.15f, 8.4f, 1.15f));
                    b.Cylinder(mats.NeonAlt, new Vector3(x, 8.5f, z), new Vector3(1.35f, 0.5f, 1.35f));
                    b.Cylinder(mats.WarmLamp, new Vector3(x, 9.1f, z), new Vector3(0.9f, 0.75f, 0.9f));
                    b.Cylinder(mats.Metal, new Vector3(x, 9.75f, z), new Vector3(1.0f, 0.2f, 1.0f));
                }
            }
            // Salt-air footbridge and hanging lamps.
            b.Box(mats.Metal, new Vector3(0f, 6.4f, 14f), new Vector3(10.5f, 0.14f, 0.16f));
            for (int i = -2; i <= 2; i++)
                b.Box(mats.NeonSoft, new Vector3(i * 2.1f, 6.05f, 14f), new Vector3(0.08f, 0.5f, 0.08f));

            // Per-theme coastal identity so the three districts never read as recolours.
            if (theme.id == "pacific-verge")
            {
                // Cliffhead strata palisades behind the guard rail.
                for (int side2 = -1; side2 <= 1; side2 += 2)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        float z = 3.6f + i * 9.4f;
                        float x = side2 * (15.6f + rng.Range(0f, 1.8f));
                        float h = 6.4f + i * 1.4f;
                        b.Facade(mats.Rock, new Vector3(x, h * 0.5f - 0.6f, z),
                            new Vector3(5.6f, h, 8.4f), new Vector2(0f, i * 0.22f));
                        b.Box(mats.Rock, new Vector3(x * 0.86f, h * 0.82f, z),
                            new Vector3(4.2f, h * 0.44f, 7.4f), Quaternion.Euler(0f, side2 * 5f, 0f));
                        b.Foliage(mats.FoliageDark, new Vector3(x * 0.7f, h * 0.98f, z),
                            new Vector3(2.6f, 0.9f, 2.6f), Quaternion.Euler(0f, i * 27f, 0f));
                    }
                }
            }
            else if (theme.id == "coral-expanse")
            {
                // Overwater stilt decks with thatched cabanas.
                for (int side2 = -1; side2 <= 1; side2 += 2)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float z = 6f + i * 11.5f;
                        float x = side2 * (10.8f + rng.Range(0f, 1.4f));
                        for (int post = -1; post <= 1; post += 2)
                            b.Cylinder(mats.Bark, new Vector3(x + post * 2.1f, 0.2f, z + side2 * 1.6f),
                                new Vector3(0.18f, 2.6f, 0.18f));
                        b.Facade(mats.Wood, new Vector3(x, 1.7f, z), new Vector3(5f, 0.5f, 4.4f),
                            new Vector2(i * 0.4f, 0f));
                        b.Box(mats.Sign, new Vector3(x, 3.1f, z), new Vector3(4.2f, 2.4f, 4f),
                            Quaternion.Euler(0f, side2 * 8f, 0f));
                        b.Cylinder(mats.Rock, new Vector3(x, 3.6f, z), new Vector3(4.4f, 1.1f, 4.4f),
                            Quaternion.Euler(0f, 0f, 0f));
                        b.Box(mats.WarmLamp, new Vector3(x, 2.72f, z), new Vector3(3.4f, 0.06f, 3f));
                    }
                    AddPalm(b, mats, side2, 12f, side2 * 6.9f, rng);
                }
            }
            else
            {
                // Monsoon-quay: working harbour with moored junk skiffs and coiled rope bollards.
                for (int side2 = -1; side2 <= 1; side2 += 2)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float z = 6.5f + i * 10.8f;
                        float x = side2 * (12.6f + rng.Range(0f, 1.2f));
                        b.Box(mats.Wood, new Vector3(x, 0.3f, z), new Vector3(2.1f, 0.6f, 5.6f),
                            Quaternion.Euler(0f, side2 * 4f, 0f));
                        b.Box(mats.Trim, new Vector3(x, 0.78f, z), new Vector3(1.7f, 0.32f, 5f));
                        b.Box(mats.NeonAlt, new Vector3(x, 1.62f, z), new Vector3(0.08f, 0.5f, 0.4f));
                        b.Box(mats.Wood, new Vector3(x + side2 * 1.3f, 1.9f, z + 1.4f),
                            new Vector3(0.09f, 3.4f, 0.09f), Quaternion.Euler(0f, 0f, side2 * 8f));
                        b.Foliage(mats.FoliageDark, new Vector3(x + side2 * 1.3f, 3.5f, z + 1.5f),
                            new Vector3(1.1f, 0.16f, 0.65f), Quaternion.Euler(0f, side2 * 18f, 0f));
                    }
                    for (int boll = 0; boll < 3; boll++)
                    {
                        float z = 4f + boll * 13f;
                        b.Cylinder(mats.Metal, new Vector3(side2 * 6.5f, 0.24f, z),
                            new Vector3(0.22f, 0.5f, 0.22f));
                        b.Foliage(mats.Bark, new Vector3(side2 * 6.34f, 0.4f, z),
                            new Vector3(0.34f, 0.14f, 0.34f));
                    }
                }
            }
        }

        /// <summary>Forest research overpass: canopy trunks, ferns, logs and a glass lab front.</summary>
        private static void BuildForest(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                // Glass-walled research frontage set behind the treeline.
                b.Box(mats.BuildingB, new Vector3(side * 8.7f, 1.9f, half), new Vector3(1.4f, 3.8f, SegmentLength));
                for (int i = 0; i < 5; i++)
                {
                    float z = 5f + i * 8f;
                    b.Box(mats.NeonSoft, new Vector3(side * 7.94f, 1.8f, z), new Vector3(0.06f, 1.5f, 2.6f));
                    b.Box(mats.Trim, new Vector3(side * 7.88f, 1.8f, z), new Vector3(0.06f, 1.6f, 0.08f));
                }
                // Old-growth trunks and layered canopies.
                for (int i = 0; i < 3; i++)
                {
                    float z = 5f + i * 13.2f + rng.Range(-2f, 2f);
                    float x = side * (6.9f + rng.Range(0f, 2.8f));
                    float h = rng.Range(5.6f, 9.4f);
                    b.Cylinder(mats.Bark, new Vector3(x, h * 0.5f, z), new Vector3(0.36f, h, 0.36f));
                    b.Cylinder(mats.Bark, new Vector3(x - side * 0.25f, h * 0.55f, z + 0.4f),
                        new Vector3(0.1f, 2.2f, 0.1f), Quaternion.Euler(24f, 0f, side * 18f));
                    b.Foliage(mats.Foliage, new Vector3(x, h + 0.5f, z),
                        new Vector3(3.4f, 1.8f, 3.4f), Quaternion.Euler(0f, i * 41f, 0f));
                    b.Foliage(mats.FoliageDark, new Vector3(x + side * 0.9f, h - 0.5f, z - 0.8f),
                        new Vector3(2.4f, 1.3f, 2.4f));
                    b.Foliage(mats.FoliageLight, new Vector3(x - side * 0.7f, h + 1.15f, z + 0.7f),
                        new Vector3(2.1f, 1.1f, 2.1f));
                }
                // Ferns and undergrowth between the rail and trees.
                for (int i = 0; i < 6; i++)
                    b.Foliage(mats.FoliageDark, new Vector3(side * (5.5f + rng.Range(0f, 0.8f)), 0.34f,
                        rng.Range(2f, 40f)), new Vector3(rng.Range(0.5f, 1.1f), 0.55f, rng.Range(0.5f, 1.2f)));
                // Fallen log.
                if (rng.Chance(0.55f))
                    b.Cylinder(mats.Bark, new Vector3(side * 7.4f, 0.45f, 18f + rng.Range(-4f, 6f)),
                        new Vector3(0.42f, 3.2f, 0.42f), Quaternion.Euler(0f, 0f, 90f));
                // Bio-luminescent strip on the research wall.
                b.Box(mats.Neon, new Vector3(side * 7.86f, 3.55f, half), new Vector3(0.05f, 0.06f, SegmentLength));
            }
            // Canopy bridge with hanging vines.
            b.Box(mats.Bark, new Vector3(0f, 8.1f, 21f), new Vector3(12.6f, 0.42f, 0.6f));
            for (int i = -2; i <= 2; i++)
                b.Cylinder(mats.FoliageDark, new Vector3(i * 2.4f, 7.25f, 21f), new Vector3(0.08f, 1.5f, 0.08f));

            // Verdant-grid signature: glass research dome and canopy sensor towers.
            float domeZ = 27f;
            for (int side = -1; side <= 1; side += 2)
            {
                b.Cylinder(mats.BuildingB, new Vector3(side * 12.8f, 1.65f, domeZ),
                    new Vector3(4.6f, 1.65f, 4.6f));
                b.Foliage(mats.NeonSoft, new Vector3(side * 12.8f, 3.6f, domeZ),
                    new Vector3(4.7f, 2.5f, 4.7f));
                b.Cylinder(mats.Trim, new Vector3(side * 12.8f, 5.4f, domeZ),
                    new Vector3(0.22f, 1.2f, 0.22f));
                b.Box(mats.Neon, new Vector3(side * 12.8f, 6.2f, domeZ), new Vector3(0.3f, 0.1f, 0.3f));
                // Canopy observation tower climbing above the treeline.
                float tz = 16f;
                b.Cylinder(mats.Metal, new Vector3(side * 10.4f, 4.2f, tz), new Vector3(0.4f, 4.2f, 0.4f));
                b.Box(mats.White, new Vector3(side * 10.4f, 8.9f, tz), new Vector3(2f, 1.5f, 2f));
                b.Box(mats.Trim, new Vector3(side * 10.4f, 9.8f, tz), new Vector3(2.2f, 0.14f, 2.2f));
                b.Box(mats.Neon, new Vector3(side * 10.4f, 8.9f, tz), new Vector3(0.12f, 1.1f, 0.12f));
                b.Box(mats.Neon, new Vector3(side * 10.4f, 10.1f, tz), new Vector3(0.9f, 0.05f, 0.9f));
            }
        }

        /// <summary>European promenade: colonnades, cornices, domes and warm plaza lamps.</summary>
        private static void BuildEuropean(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                // Stone block with arched shopfronts.
                b.Box(mats.Podium, new Vector3(side * 9.1f, 2.5f, half), new Vector3(2.6f, 5f, SegmentLength));
                for (int i = 0; i < 4; i++)
                {
                    float z = 6f + i * 10f;
                    b.Box(mats.Shop, new Vector3(side * 7.72f, 1.7f, z), new Vector3(0.08f, 2.2f, 2.0f));
                    b.Box(mats.Trim, new Vector3(side * 7.66f, 2.9f, z), new Vector3(0.16f, 0.22f, 2.4f));
                    b.Box(mats.WarmGlow, new Vector3(side * 7.64f, 0.55f, z), new Vector3(0.02f, 0.1f, 1.8f));
                }
                // Colonnade along the sidewalk edge.
                for (int i = 0; i < 5; i++)
                {
                    float z = 4f + i * 8.4f;
                    b.Cylinder(mats.Trim, new Vector3(side * 6.6f, 2.2f, z), new Vector3(0.34f, 4.4f, 0.34f));
                    b.Box(mats.Podium, new Vector3(side * 6.6f, 0.16f, z), new Vector3(0.7f, 0.32f, 0.7f));
                    b.Box(mats.Trim, new Vector3(side * 6.6f, 4.32f, z), new Vector3(0.62f, 0.2f, 0.62f));
                }
                b.Box(mats.Trim, new Vector3(side * 6.6f, 4.62f, half), new Vector3(0.85f, 0.4f, SegmentLength));
                b.Box(mats.Podium, new Vector3(side * 6.6f, 4.92f, half), new Vector3(1.1f, 0.22f, SegmentLength));
                // Plaza lamps.
                for (int i = 0; i < 3; i++)
                {
                    float z = 8f + i * 12f;
                    b.Cylinder(mats.Metal, new Vector3(side * 5.55f, 1.65f, z), new Vector3(0.1f, 3.3f, 0.1f));
                    b.Box(mats.WarmLamp, new Vector3(side * 5.55f, 3.45f, z), new Vector3(0.42f, 0.55f, 0.42f));
                    b.Box(mats.Metal, new Vector3(side * 5.55f, 3.78f, z), new Vector3(0.55f, 0.1f, 0.55f));
                }
                // Domed pavilion every other segment.
                if (rng.Chance(0.55f))
                {
                    float z = 24f + rng.Range(-8f, 6f);
                    b.Cylinder(mats.Podium, new Vector3(side * 10.8f, 2.2f, z), new Vector3(1.8f, 4.4f, 1.8f));
                    b.Cylinder(mats.Trim, new Vector3(side * 10.8f, 4.5f, z), new Vector3(2.15f, 0.32f, 2.15f));
                    b.Foliage(mats.WarmLamp, new Vector3(side * 10.8f, 5.3f, z), new Vector3(2.0f, 1.2f, 2.0f));
                    b.Box(mats.NeonSoft, new Vector3(side * 10.8f, 6.35f, z), new Vector3(0.18f, 0.7f, 0.18f));
                }
            }
            // Festoon lights across the promenade.
            for (int i = -3; i <= 3; i++)
                b.Box(mats.WarmLamp, new Vector3(i * 1.55f, 6.15f + Mathf.Abs(i) * 0.06f, 17f),
                    new Vector3(0.07f, 0.07f, 0.07f));
            b.Box(mats.Metal, new Vector3(0f, 6.05f, 17f), new Vector3(10.2f, 0.03f, 0.03f));

            // Pair identity split: continental data promenade vs mountain divide viaduct.
            if (theme.id == "eurodyne-spire")
            {
                // Glass atria with warm atrium cores along the plaza back edge.
                for (int side2 = -1; side2 <= 1; side2 += 2)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        float z = 12f + i * 17f;
                        float x = side2 * (10.9f + rng.Range(0f, 1f));
                        b.Box(mats.BuildingB, new Vector3(x, 2.1f, z), new Vector3(4.6f, 4.2f, 6.6f));
                        b.Box(mats.Trim, new Vector3(x, 4.35f, z), new Vector3(5f, 0.34f, 7f));
                        b.Box(mats.WarmLamp, new Vector3(x - side2 * 2.33f, 2f, z),
                            new Vector3(0.05f, 2f, 5f));
                        b.Box(mats.NeonSoft, new Vector3(x - side2 * 2.33f, 3.6f, z),
                            new Vector3(0.04f, 0.06f, 5f));
                    }
                    // Suspended tram wires with hanging city lamps.
                    for (int w = 0; w < 4; w++)
                    {
                        float z = 5.5f + w * 9.5f;
                        b.Cylinder(mats.Metal, new Vector3(side2 * 7.4f, 4.2f, z),
                            new Vector3(0.09f, 4.2f, 0.09f));
                        b.Cylinder(mats.Metal, new Vector3(side2 * 4.6f, 3f, z),
                            new Vector3(0.06f, 3f, 0.06f));
                        b.Box(mats.Metal, new Vector3(side2 * 6f, 8.3f, z),
                            new Vector3(5.6f, 0.05f, 0.05f));
                        b.Box(mats.WarmLamp, new Vector3(side2 * 6f, 7.9f, z),
                            new Vector3(0.22f, 0.3f, 0.22f));
                    }
                }
            }
            else
            {
                // Aurora pass: cut-rock faces with snow caps and relay masts with dishes.
                for (int side2 = -1; side2 <= 1; side2 += 2)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float z = 5f + i * 13f;
                        float x = side2 * (14.6f + rng.Range(0f, 2.2f));
                        float h = 8.4f + i * 1.8f;
                        b.Facade(mats.Rock, new Vector3(x, h * 0.5f - 1f, z),
                            new Vector3(7f, h, 10.5f), new Vector2(0f, i * 0.2f));
                        b.Box(mats.Snow, new Vector3(x, h - 0.85f, z),
                            new Vector3(7.2f, 0.7f, 10.7f), Quaternion.Euler(0f, 0f, side2 * -5f));
                        b.Foliage(mats.FoliageDark, new Vector3(x * 0.78f, h * 0.92f - 0.5f, z),
                            new Vector3(2.8f, 1f, 2.8f), Quaternion.Euler(0f, i * 31f, 0f));
                    }
                    for (int m = 0; m < 2; m++)
                    {
                        float z = 10f + m * 21f;
                        float x = side2 * (11.8f + m * 1.1f);
                        b.Box(mats.Metal, new Vector3(x, 0.25f, z), new Vector3(0.8f, 0.5f, 0.8f));
                        b.Cylinder(mats.Metal, new Vector3(x, 3.4f, z), new Vector3(0.22f, 3.4f, 0.22f));
                        b.Cylinder(mats.White, new Vector3(x, 6.75f, z), new Vector3(1.3f, 0.34f, 1.3f),
                            Quaternion.Euler(38f, m * 74f, 0f));
                        b.Box(mats.Neon, new Vector3(x, 7.05f, z), new Vector3(0.2f, 0.1f, 0.2f));
                        b.Box(mats.NeonAlt, new Vector3(x, 0.9f, z), new Vector3(0.08f, 1.4f, 0.08f));
                    }
                }
                for (int side = -1; side <= 1; side += 2)
                    b.Box(mats.Snow, new Vector3(side * 6.5f, 0.13f, 27f), new Vector3(2.4f, 0.24f, 8.5f),
                        Quaternion.Euler(0f, 6f, 0f));
            }
        }

        /// <summary>Volcanic freight line: basalt columns, lava fissures and furnace vents.</summary>
        private static void BuildVolcanic(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                // Lava glow trenches sit on the service walk, outside the collision lane.
                for (int i = 0; i < 5; i++)
                {
                    float z = 3f + i * 8.4f + rng.Range(-1.4f, 1.4f);
                    b.Box(mats.WarmGlow, new Vector3(side * 5.55f, 0.026f, z),
                        new Vector3(0.85f, 0.012f, rng.Range(2.4f, 5.2f)),
                        Quaternion.Euler(0f, rng.Range(-16f, 16f), 0f));
                    b.Box(mats.Trim, new Vector3(side * 5.05f, 0.14f, z), new Vector3(0.12f, 0.28f, 6f));
                }
                // Basalt colonnade.
                for (int i = 0; i < 5; i++)
                {
                    float z = 5f + i * 8f;
                    float h = rng.Range(3.4f, 7.8f);
                    b.Cylinder(mats.BuildingB, new Vector3(side * 7.2f, h * 0.5f, z),
                        new Vector3(0.78f, h, 0.78f), Quaternion.Euler(0f, i * 23f, 0f));
                    b.Box(mats.NeonAlt, new Vector3(side * 7.2f, h + 0.2f, z), new Vector3(0.9f, 0.4f, 0.9f));
                }
                // Furnace vent with a hot cap and chimney.
                for (int i = 0; i < 2; i++)
                {
                    float z = 12f + i * 20f;
                    b.Cylinder(mats.Metal, new Vector3(side * 10.2f, 2.4f, z), new Vector3(1.5f, 4.8f, 1.5f));
                    b.Cylinder(mats.WarmLamp, new Vector3(side * 10.2f, 4.9f, z), new Vector3(1.65f, 0.2f, 1.65f));
                    b.Box(mats.GroundDark, new Vector3(side * 10.2f, 7.1f, z), new Vector3(0.7f, 4.2f, 0.7f));
                }
                // Ash drifts.
                for (int i = 0; i < 4; i++)
                    b.Foliage(mats.GroundDark, new Vector3(side * (6.3f + rng.Range(0f, 0.8f)), 0.2f,
                        rng.Range(2f, 40f)), new Vector3(rng.Range(1f, 2f), 0.35f, rng.Range(1.2f, 2.4f)));
            }
            // Ore gantry with warning beacons.
            b.Box(mats.Metal, new Vector3(0f, 7.2f, 20f), new Vector3(13.4f, 0.4f, 0.5f));
            for (int i = -3; i <= 3; i++)
                b.Box(i == 0 ? mats.WarmLamp : mats.NeonAlt, new Vector3(i * 1.7f, 6.92f, 20f),
                    new Vector3(0.16f, 0.16f, 0.1f));
        }

        /// <summary>Japanese neon expressway: torii portals, pagoda roofs, lanterns and cherry trees.</summary>
        private static void BuildJapanese(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                // Low timber frontage with shoji panels and deep eaves.
                b.Box(mats.BuildingA, new Vector3(side * 8.6f, 1.8f, half), new Vector3(2.1f, 3.6f, SegmentLength));
                for (int i = 0; i < 5; i++)
                {
                    float z = 4.5f + i * 8f;
                    b.Box(mats.WarmLamp, new Vector3(side * 7.5f, 1.75f, z), new Vector3(0.06f, 1.5f, 2.2f));
                    b.Box(mats.GroundDark, new Vector3(side * 7.44f, 1.75f, z), new Vector3(0.04f, 1.7f, 0.1f));
                    b.Box(mats.GroundDark, new Vector3(side * 7.44f, 2.5f, z), new Vector3(0.08f, 0.1f, 2.4f));
                }
                b.Box(mats.Roof, new Vector3(side * 7.9f, 3.72f, half), new Vector3(2.6f, 0.28f, SegmentLength + 0.4f),
                    Quaternion.Euler(0f, 0f, side * 8f));
                // Stone lanterns.
                for (int i = 0; i < 3; i++)
                {
                    float z = 7f + i * 12.5f;
                    b.Box(mats.Trim, new Vector3(side * 5.6f, 0.75f, z), new Vector3(0.32f, 1.5f, 0.32f));
                    b.Box(mats.WarmLamp, new Vector3(side * 5.6f, 1.65f, z), new Vector3(0.46f, 0.4f, 0.46f));
                    b.Box(mats.Roof, new Vector3(side * 5.6f, 1.94f, z), new Vector3(0.72f, 0.16f, 0.72f));
                }
                // Cherry trees with layered blossom canopies.
                for (int i = 0; i < 2; i++)
                {
                    float z = 10f + i * 20f + rng.Range(-2f, 2f);
                    float x = side * (9.3f + rng.Range(0f, 1.8f));
                    b.Cylinder(mats.Bark, new Vector3(x, 1.9f, z), new Vector3(0.26f, 3.8f, 0.26f));
                    b.Cylinder(mats.Bark, new Vector3(x - side * 0.5f, 2.8f, z + 0.3f),
                        new Vector3(0.1f, 1.8f, 0.1f), Quaternion.Euler(0f, 0f, side * 34f));
                    b.Foliage(mats.SignAlt, new Vector3(x, 4.2f, z), new Vector3(3.2f, 1.5f, 3.2f),
                        Quaternion.Euler(0f, i * 44f, 0f));
                    b.Foliage(mats.NeonAlt, new Vector3(x + side * 0.8f, 4.7f, z - 0.6f),
                        new Vector3(2.0f, 1.0f, 2.0f));
                }
                // Pagoda silhouette.
                if (rng.Chance(0.6f))
                {
                    float z = 22f + rng.Range(-7f, 6f);
                    float x = side * 11.4f;
                    for (int tier = 0; tier < 3; tier++)
                    {
                        float y = 1.4f + tier * 2.2f;
                        float w = 3.4f - tier * 0.7f;
                        b.Box(mats.BuildingA, new Vector3(x, y, z), new Vector3(w, 2.0f, w));
                        b.Box(mats.Roof, new Vector3(x, y + 1.15f, z), new Vector3(w + 1.1f, 0.26f, w + 1.1f),
                            Quaternion.Euler(0f, tier * 45f, 0f));
                        b.Box(mats.WarmLamp, new Vector3(x, y + 0.2f, z - w * 0.5f), new Vector3(w * 0.4f, 0.5f, 0.05f));
                    }
                    b.Box(mats.Neon, new Vector3(x, 7.7f, z), new Vector3(0.1f, 1.2f, 0.1f));
                }
            }
            // Torii portal over the road, high enough to clear the runner and camera.
            float toriiZ = 16f + rng.Range(-2f, 4f);
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(mats.NeonAlt, new Vector3(side * 5.45f, 2.9f, toriiZ), new Vector3(0.34f, 5.8f, 0.34f));
                b.Box(mats.Roof, new Vector3(side * 5.45f, 0.18f, toriiZ), new Vector3(0.5f, 0.36f, 0.5f));
            }
            b.Box(mats.NeonAlt, new Vector3(0f, 5.95f, toriiZ), new Vector3(12.2f, 0.34f, 0.42f));
            b.Box(mats.Roof, new Vector3(0f, 6.28f, toriiZ), new Vector3(13.2f, 0.22f, 0.62f));
            b.Box(mats.NeonAlt, new Vector3(0f, 5.0f, toriiZ), new Vector3(9.4f, 0.26f, 0.3f));

            // Pair identity split: petal-cloud mountain pass vs dense lantern alley.
            if (theme.id == "sakura-circuit")
            {
                // Mount silhouette behind the treeline; the road threads a mountain pass.
                for (int side2 = -1; side2 <= 1; side2 += 2)
                {
                    float bx = side2 * 17.5f;
                    b.Facade(mats.Rock, new Vector3(bx, 5.4f, half), new Vector3(9f, 13f, SegmentLength + 2f),
                        new Vector2(0.1f, 0.4f));
                    b.Box(mats.Snow, new Vector3(bx, 12.1f, half), new Vector3(9.2f, 0.9f, SegmentLength + 2f),
                        Quaternion.Euler(0f, 0f, side2 * -6f));
                }
                // Drifting petals suspended over the tree berms, clear of the runner lanes.
                for (int petal = 0; petal < 6; petal++)
                {
                    int side2 = petal % 2 == 0 ? -1 : 1;
                    b.Foliage(mats.SignAlt, new Vector3(side2 * rng.Range(5.7f, 7.6f), rng.Range(1.6f, 5.4f),
                            2f + petal * 6.4f),
                        new Vector3(0.16f, 0.05f, 0.05f), Quaternion.Euler(petal * 40f, petal * 24f, 0f));
                }
            }
            else
            {
                // Hanging lantern strands across the frontage and stacked vending cabinets.
                for (int side2 = -1; side2 <= 1; side2 += 2)
                {
                    float strandY = 4.35f;
                    b.Box(mats.Metal, new Vector3(side2 * 2.7f, strandY, half), new Vector3(5.6f, 0.05f, 0.05f));
                    for (int lamp = 0; lamp < 4; lamp++)
                    {
                        float z = 7.4f + lamp * 9.6f;
                        b.Box(mats.NeonAlt, new Vector3(side2 * 3.3f, strandY - 0.14f, z),
                            new Vector3(0.16f, 0.5f, 0.16f));
                        b.Cylinder(mats.WarmLamp, new Vector3(side2 * 2.7f, strandY - 0.42f, z),
                            new Vector3(0.2f, 0.34f, 0.2f));
                    }
                    // Tight vertical banner forestry — the neon-brick alley wall.
                    for (int pole = 0; pole < 4; pole++)
                    {
                        float z = 4.8f + pole * 8.2f;
                        b.Box(mats.Metal, new Vector3(side2 * 5.95f, 2.5f, z),
                            new Vector3(0.09f, 5f, 0.09f));
                        b.Box(mats.Sign, new Vector3(side2 * 5.78f, 2.9f, z),
                            new Vector3(0.06f, 3.4f, 0.75f));
                        b.Box(mats.NeonSoft, new Vector3(side2 * 5.74f, 1.1f, z),
                            new Vector3(0.05f, 0.9f, 0.5f));
                    }
                }
            }
        }

        private static void BuildReactor(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {            float half = SegmentLength * 0.5f;
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

            // Reactor-belt signature: hyperboloid cooling tower, vent stacks and a robot
            // assembly cell. The tower looms past the wall so verticals stop at venetian height.
            float towerZ = 30f;
            for (int side = -1; side <= 1; side += 2)
            {
                // Steam vent stack with valve wheels on the shoulder line.
                float vz = 9f + (side > 0 ? 25f : 0f);
                b.Cylinder(mats.Metal, new Vector3(side * 11.6f, 3f, vz), new Vector3(1.35f, 3f, 1.35f));
                b.Cylinder(mats.WarmGlow, new Vector3(side * 11.6f, 6.2f, vz), new Vector3(1.1f, 0.4f, 1.1f));
                b.Cylinder(mats.NeonSoft, new Vector3(side * 11.6f, 6.4f, vz), new Vector3(0.9f, 0.06f, 0.9f));
                b.Cylinder(mats.Metal, new Vector3(side * 10.6f, 2.2f, vz + 3.4f),
                    new Vector3(0.2f, 2.2f, 0.2f));
                b.Cylinder(mats.Trim, new Vector3(side * 10.6f, 3.4f, vz + 2.1f),
                    new Vector3(0.5f, 0.05f, 0.5f), Quaternion.Euler(0f, 0f, 90f));

                // Robot assembly cell: stacked chassis shells on a conveyor spine.
                float rz = 19f;
                b.Box(mats.Metal, new Vector3(side * 8.9f, 0.8f, rz), new Vector3(0.3f, 1.6f, 9f));
                for (int shell = 0; shell < 3; shell++)
                {
                    float z = rz - 3f + shell * 3f;
                    b.Box(mats.BuildingB, new Vector3(side * 8.9f, 1.35f, z), new Vector3(1.15f, 0.9f, 1.6f),
                        Quaternion.Euler(0f, 0f, shell % 2 == 0 ? 3f : -3f));
                    b.Box(mats.Neon, new Vector3(side * 8.35f, 1.35f, z), new Vector3(0.05f, 0.4f, 0.16f));
                }
                b.Cylinder(mats.Paint, new Vector3(side * 8.9f, 1.85f, rz),
                    new Vector3(0.4f, 0.45f, 0.4f), Quaternion.Euler(0f, 0f, 90f));
            }
            // Cooling tower pair rising past the hazard walls: pale shell, waist, hot rim.
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 15.2f;
                b.Cylinder(mats.BuildingA, new Vector3(x, 4f, towerZ), new Vector3(5.4f, 4f, 5.4f));
                b.Cylinder(mats.BuildingA, new Vector3(x, 9.2f, towerZ), new Vector3(4.2f, 2.5f, 4.2f));
                b.Cylinder(mats.BuildingA, new Vector3(x, 12.2f, towerZ), new Vector3(4.8f, 0.6f, 4.8f));
                b.Cylinder(mats.NeonSoft, new Vector3(x, 12.6f, towerZ), new Vector3(4.4f, 0.08f, 4.4f));
                b.Cylinder(mats.WarmGlow, new Vector3(x, 13.8f, towerZ), new Vector3(2.8f, 0.5f, 2.8f));
            }
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

            // Obsidian-line signature: wall-mounted signal heads, emergency bays and
            // kilometre plates. Everything is inset into the existing walls and ribs.
            for (int i = 0; i < 3; i++)
            {
                float z = 9f + i * 12f;
                for (int side = -1; side <= 1; side += 2)
                {
                    // Signal head: black case, red aspect, bracket sunk into the rib face.
                    b.Box(mats.GroundDark, new Vector3(side * 5.72f, 4.5f, z),
                        new Vector3(0.3f, 0.85f, 0.3f));
                    b.Box(mats.Neon, new Vector3(side * 5.86f, 4.5f, z),
                        new Vector3(0.05f, 0.22f, 0.22f), Quaternion.Euler(90f, 0f, 0f));
                    b.Box(mats.NeonAlt, new Vector3(side * 5.86f, 4.22f, z),
                        new Vector3(0.04f, 0.1f, 0.5f));
                }
            }
            // Emergency phone/fire bays on alternating walls with painted backboards.
            for (int i = 0; i < 2; i++)
            {
                float z = 16f + i * 15f;
                int side = i % 2 == 0 ? -1 : 1;
                b.Box(mats.GroundDark, new Vector3(side * 5.9f, 1.7f, z), new Vector3(0.5f, 1.7f, 1.1f));
                b.Box(mats.Paint, new Vector3(side * 5.61f, 1.7f, z), new Vector3(0.08f, 1.3f, 0.8f));
                b.Box(mats.Neon, new Vector3(side * 5.57f, 2.2f, z), new Vector3(0.05f, 0.16f, 0.4f));
                b.Quad(mats.WetGlow, new Vector3(side * 5.36f, 0.38f, z),
                    new Vector3(0.9f, 1.9f, 1f), Quaternion.Euler(90f, 0f, 0f));
            }
            // Kilometre plates on the column line, plus a painted ceiling centre band.
            for (int i = 0; i < 4; i++)
            {
                float z = 6f + i * 10f;
                int side = i % 2 == 0 ? 1 : -1;
                b.Box(mats.White, new Vector3(side * 5.73f, 3.6f, z), new Vector3(0.05f, 0.4f, 0.55f));
                b.Box(mats.Neon, new Vector3(side * 5.7f, 3.6f, z), new Vector3(0.06f, 0.3f, 0.05f));
            }
            b.Box(mats.NeonSoft, new Vector3(0f, 6.47f, half), new Vector3(3.2f, 0.03f, SegmentLength));
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
            int hazardBias = LocationCatalog.Get(plan.BiomeIndex).hazardBias;
            var junction = plan.Junction;
            for (int r = 0; r < plan.Rows.Length; r++)
            {
                var row = plan.Rows[r];
                float z = r * RowSpacing + RowSpacing * 0.5f;
                for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                {
                    CellKind cell = row[lane];
                    if (cell == CellKind.Empty) continue;
                    if (JunctionSuppressesCell(junction, r, lane)) continue;
                    float x = LaneMath.LaneToX(lane) +
                              JunctionRules.RouteOffsetAt(junction, z, lane);
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
                            move = 0.55f; speed = 0.78f + hazardBias * 0.1f;
                            phase = rng.Range(0f, 6.28f);
                            break;
                        default: // Solid
                            float laserChance = 0.30f + (hazardBias >= 3 ? 0.12f : 0f);
                            if (rng.Chance(laserChance) || hazardBias >= 4)
                            {
                                kind = ObstacleKind.LaserWall; depth = 0.65f; minY = 0f; maxY = 2.35f;
                            }
                            else
                            {
                                kind = ObstacleKind.CargoCrate; depth = 1.7f; minY = 0f; maxY = 1.92f;
                            }
                            if (rng.Chance(0.12f + hazardBias * 0.015f))
                            {
                                kind = ObstacleKind.RotatingBar; depth = 2.0f; minY = 0f; maxY = 2.25f;
                                spin = 120f + hazardBias * 12f;
                            }
                            break;
                    }
                    var obstacle = _obstaclePool.Spawn(kind, segment.Root.transform,
                        new Vector3(x, 0f, z), lane, width, depth, minY, maxY, move, speed, phase, spin);
                    segment.Obstacles.Add(obstacle);
                }
            }
            SpawnJunctionHazards(segment, junction);
            SpawnSignatureHazard(segment, rng);
        }

        /// <summary>
        /// Event-only signature hazards. They are placed on a row that is empty across the
        /// full width and whose neighbours are empty, so a train, vehicle, drop or platform can
        /// never combine with a lane action the player has not seen yet. Chance scales with the
        /// director's difficulty tier; the base run still keeps long quiet stretches.
        /// </summary>
        private void SpawnSignatureHazard(Segment segment, DeterministicRandom rng)
        {
            var plan = segment.Plan;
            if (plan.Junction != null || plan.IsSafe || !plan.IsEvent) return;
            float chance = Mathf.Min(0.46f, 0.16f + plan.DifficultyTier * 0.035f);
            if (!rng.Chance(chance)) return;
            int row = HazardRules.FindSignatureRow(plan.Rows);
            if (row < 0) return;
            float z = row * RowSpacing + RowSpacing * 0.5f;
            int pick = rng.Range(0, 4);
            int lane = rng.Range(0, LaneMath.LaneCount);
            float phase = rng.Range(0f, 6.28f);
            Obstacle hazard;
            switch (pick)
            {
                case 0:
                    hazard = _obstaclePool.Spawn(ObstacleKind.MaglevTrain, segment.Root.transform,
                        new Vector3(0f, 0f, z), 1, 2.5f, 3.7f, 0f, 2.35f, 7.8f, 0.55f, phase);
                    break;
                case 1:
                    hazard = _obstaclePool.Spawn(ObstacleKind.TrafficVehicle, segment.Root.transform,
                        new Vector3(LaneMath.LaneToX(lane), 0f, z), lane, 2.0f, 2.4f, 0f, 1.7f,
                        3.4f, 1.15f, phase);
                    break;
                case 2:
                    hazard = _obstaclePool.Spawn(ObstacleKind.FallingDebris, segment.Root.transform,
                        new Vector3(LaneMath.LaneToX(lane), 4.4f, z), lane, 1.6f, 1.5f,
                        -0.82f, 0.82f, 0f, 0f, phase, 0.55f);
                    break;
                default:
                    hazard = _obstaclePool.Spawn(ObstacleKind.MovingPlatform, segment.Root.transform,
                        new Vector3(LaneMath.LaneToX(lane), 0f, z), lane, 2.1f, 2.5f, 0f, 1.2f,
                        3.2f, 0.6f, phase);
                    break;
            }
            segment.Obstacles.Add(hazard);
        }

        private static bool JunctionSuppressesCell(JunctionPlan junction, int row, int lane)
        {
            if (junction == null) return false;
            if (junction.IsShortcut && junction.IsBranchRow(row) && lane == junction.RewardLane) return true;
            if (junction.IsMedianRow(row)) return lane == 1;
            if (junction.IsNarrowRow(row)) return lane != 1;
            if (junction.IsBrokenRoad) return row == 2;
            return false;
        }

        /// <summary>
        /// Spawns the continuous collision surfaces that make a junction physical. A single
        /// long divider is used rather than three row-sized crates so the median cannot be
        /// squeezed through between rows at high speed.
        /// </summary>
        private void SpawnJunctionHazards(Segment segment, JunctionPlan junction)
        {
            if (junction == null) return;
            if (junction.UsesMedian)
            {
                float start = junction.BranchStartRow * RowSpacing;
                float end = (junction.BranchEndRow + 1) * RowSpacing;
                var divider = _obstaclePool.Spawn(ObstacleKind.JunctionDivider, segment.Root.transform,
                    new Vector3(0f, 0f, (start + end) * 0.5f), 1, 1.8f, end - start, 0f, 1.34f);
                divider.transform.localScale = new Vector3(1f, 1f, Mathf.Max(1f, end - start));
                segment.Obstacles.Add(divider);
            }
            else if (junction.IsShortcut)
            {
                // The outer guard rail follows the shortcut's curve row by row. The inner
                // edge stays open so a late lane change is possible but visually committed.
                int lane = junction.RewardLane;
                int side = junction.RewardOnLeft ? -1 : 1;
                for (int row = junction.BranchStartRow; row < junction.MergeRow; row++)
                {
                    float z = row * RowSpacing + RowSpacing * 0.5f;
                    float x = LaneMath.LaneToX(lane) + JunctionRules.RouteOffsetAt(junction, z, lane) +
                              side * 1.55f;
                    var edge = _obstaclePool.Spawn(ObstacleKind.JunctionEdge, segment.Root.transform,
                        new Vector3(x, 0f, z), lane, 0.34f, RowSpacing + 0.1f, 0f, 1.2f);
                    segment.Obstacles.Add(edge);
                }
            }
            else if (junction.IsNarrowBridge)
            {
                float start = junction.BranchStartRow * RowSpacing;
                float end = (junction.BranchEndRow + 1) * RowSpacing;
                float depth = end - start;
                for (int side = -1; side <= 1; side += 2)
                {
                    var edge = _obstaclePool.Spawn(ObstacleKind.JunctionEdge, segment.Root.transform,
                        new Vector3(side * 2.7f, 0f, (start + end) * 0.5f), side < 0 ? 0 : 2,
                        2.08f, depth, 0f, 1.34f);
                    edge.transform.localScale = new Vector3(1f, 1f, Mathf.Max(1f, depth));
                    segment.Obstacles.Add(edge);
                }
            }
            else if (junction.IsBrokenRoad)
            {
                bool collapsing = junction.IsCollapsingBridge;
                var gap = _obstaclePool.Spawn(
                    collapsing ? ObstacleKind.CollapsingBridge : ObstacleKind.RoadGap,
                    segment.Root.transform,
                    new Vector3(0f, 0f, 2 * RowSpacing + RowSpacing * 0.5f), 1,
                    8.6f, collapsing ? 4.4f : 3.6f, 0f, 0.74f);
                segment.Obstacles.Add(gap);
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
                        float laneX = LaneMath.LaneToX(lane);
                        if (cell == CellKind.LowBarrier || cell == CellKind.Electric)
                        {
                            // A readable arc rewards a well-timed jump.
                            float[] heights = { 1.2f, 1.75f, 2.15f, 1.75f, 1.2f };
                            for (int i = 0; i < heights.Length; i++)
                            {
                                float z = r * RowSpacing + 1.5f + i * 1.05f;
                                float x = laneX + JunctionRules.RouteOffsetAt(plan.Junction, z, lane);
                                AddCredit(segment, new Vector3(x, heights[i], z), rng);
                            }
                        }
                        else
                        {
                            float y = cell == CellKind.OverheadGate || cell == CellKind.Drone ? 0.62f : 0.92f;
                            for (int i = 0; i < (coinRush ? 3 : 2); i++)
                            {
                                float z = r * RowSpacing + 1.6f + i * 2.3f;
                                float x = laneX + JunctionRules.RouteOffsetAt(plan.Junction, z, lane);
                                AddCredit(segment, new Vector3(x, y, z), rng);
                            }
                        }
                    }
                }
                else if (fullAction && rng.Chance(0.45f))
                {
                    int lane = passable[rng.Range(0, passable.Count)];
                    float laneX = LaneMath.LaneToX(lane);
                    float y = row[lane] == CellKind.OverheadGate ? 0.6f : 1.4f;
                    for (int i = 0; i < 3; i++)
                    {
                        float z = r * RowSpacing + 1.7f + i * 1.5f;
                        float x = laneX + JunctionRules.RouteOffsetAt(plan.Junction, z, lane);
                        AddCredit(segment, new Vector3(x, y, z), rng);
                    }
                }
            }

            SpawnJunctionPickups(segment, plan, rng);

            // Rare prism shard near an open path.
            if (rng.Chance(plan.IsBonus ? 0.35f : 0.12f))
            {
                int lane = FindOpenLane(plan, rng.Range(0, plan.Rows.Length), rng);
                if (lane >= 0)
                {
                    float z = rng.Range(4f, SegmentLength - 4f);
                    float x = LaneMath.LaneToX(lane) + JunctionRules.RouteOffsetAt(plan.Junction, z, lane);
                    var prism = _pickupPool.SpawnCredit(segment.Root.transform,
                        new Vector3(x, 1.25f, z), rng.Range(0f, 8f), true);
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
                        float x = LaneMath.LaneToX(lane) + JunctionRules.RouteOffsetAt(plan.Junction, z, lane);
                        var pickup = _pickupPool.SpawnPowerUp(segment.Root.transform, definition,
                            new Vector3(x, 1.2f, z), rng.Range(0f, 6.28f));
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

        /// <summary>
        /// Junction rewards are deliberately authored, not random: the marked branch gets a
        /// visible credit line and the secret variant adds one prism. Both branches are always
        /// physically safe, so the choice is a reward route, never a hidden trap.
        /// </summary>
        private void SpawnJunctionPickups(Segment segment, SegmentPlan plan, DeterministicRandom rng)
        {
            var junction = plan.Junction;
            if (junction == null || junction.IsCurvedRoad) return;
            if (junction.IsBrokenRoad)
            {
                for (int i = 0; i < 6; i++)
                {
                    float z = 2 * RowSpacing + 0.2f + i * 0.72f;
                    float y = 1.15f + Mathf.Sin(i / 5f * Mathf.PI) * 0.95f;
                    AddCredit(segment, new Vector3(
                        LaneMath.LaneToX(junction.RewardLane) +
                        JunctionRules.RouteOffsetAt(junction, z, junction.RewardLane), y, z), rng);
                }
                if (junction.RewardIsSecret)
                {
                    float z = 2 * RowSpacing + 2.8f;
                    var prism = _pickupPool.SpawnCredit(segment.Root.transform,
                        new Vector3(LaneMath.LaneToX(junction.StandardLane) +
                                    JunctionRules.RouteOffsetAt(junction, z, junction.StandardLane),
                            1.35f, z),
                        rng.Range(0f, 8f), true);
                    segment.Credits.Add(prism);
                }
                return;
            }

            int lane = junction.IsNarrowBridge ? 1 : junction.RewardLane;
            float laneX = LaneMath.LaneToX(lane);
            for (int row = junction.BranchStartRow; row < junction.MergeRow; row++)
            {
                float baseZ = row * RowSpacing;
                float z1 = baseZ + 2.2f;
                float z2 = baseZ + 4.6f;
                AddCredit(segment, new Vector3(laneX + JunctionRules.RouteOffsetAt(junction, z1, lane),
                    0.95f, z1), rng);
                AddCredit(segment, new Vector3(laneX + JunctionRules.RouteOffsetAt(junction, z2, lane),
                    0.95f, z2), rng);
                if (junction.RewardIsSecret)
                {
                    float z3 = baseZ + 3.4f;
                    AddCredit(segment, new Vector3(laneX + JunctionRules.RouteOffsetAt(junction, z3, lane),
                        1.55f, z3), rng);
                }
            }
            if (junction.RewardIsSecret)
            {
                float z = (junction.MergeRow - 0.55f) * RowSpacing;
                var prism = _pickupPool.SpawnCredit(segment.Root.transform,
                    new Vector3(laneX + JunctionRules.RouteOffsetAt(junction, z, lane), 1.35f, z),
                    rng.Range(0f, 8f), true);
                segment.Credits.Add(prism);
            }
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

        /// <summary>
        /// Resolves the nearest active junction for a run distance. The director is queried
        /// directly rather than only the streamed segments, so the HUD can show a 126 m warning
        /// before the junction segment has been built.
        /// </summary>
        public bool GetJunctionState(float distance, out JunctionState state)
        {
            state = default;
            if (_director == null) return false;
            int current = Mathf.Max(0, Mathf.FloorToInt(Mathf.Max(0f, distance) / SegmentLength));
            JunctionState best = default;
            float bestDistance = float.MaxValue;
            for (int index = current - 4; index <= current + 4; index++)
            {
                if (index < 0) continue;
                var plan = _director.GetSegment(index).Junction;
                if (plan == null) continue;
                var candidate = JunctionRules.Resolve(plan, distance);
                if (!candidate.IsActive) continue;
                float marker = Mathf.Abs(candidate.MetresToCommit);
                if (marker < bestDistance)
                {
                    bestDistance = marker;
                    best = candidate;
                }
            }
            state = best;
            return best.IsActive;
        }

        /// <summary>
        /// Lateral displacement of a lane at the current run distance. The runner, junction
        /// pickups and world geometry all use this same pure function, which keeps the visible
        /// road and the collision route physically connected.
        /// </summary>
        public float GetJunctionRouteOffset(float distance, int lane)
        {
            if (!GetJunctionState(distance, out var state)) return 0f;
            return JunctionRules.RouteOffsetAt(state.Plan, distance - state.Plan.WorldStartZ, lane);
        }

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
