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
            public Material Trim;
            public Material Neon;
            public Material NeonAlt;
            public Material BuildingA;
            public Material BuildingB;
            public Material Roof;
            public Material Billboard;
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
                var m = new ThemeMaterials
                {
                    Ground = MaterialLibrary.Lit(theme.ground, 0.25f, 0.45f),
                    GroundDark = MaterialLibrary.Lit(Color.Lerp(theme.ground, Palette.Void, 0.6f), 0.15f, 0.25f),
                    Trim = MaterialLibrary.Lit(Color.Lerp(theme.buildingA, Palette.White, 0.2f), 0.75f, 0.7f),
                    Neon = MaterialLibrary.Lit(theme.accent, 0.2f, 0.8f, theme.accent, 3.1f),
                    NeonAlt = MaterialLibrary.Lit(Palette.Magenta, 0.2f, 0.8f, Palette.Magenta, 2.6f),
                    BuildingA = MaterialLibrary.Lit(theme.buildingA, 0.4f, 0.5f),
                    BuildingB = MaterialLibrary.Lit(theme.buildingB, 0.4f, 0.5f),
                    Roof = MaterialLibrary.Lit(Color.Lerp(theme.buildingB, Palette.Void, 0.55f), 0.4f, 0.35f),
                    Billboard = MaterialLibrary.Lit(theme.accent, 0.1f, 0.9f, theme.accent, 2.0f),
                    Pipe = MaterialLibrary.Lit(Color.Lerp(theme.buildingA, theme.accent, 0.12f), 0.8f, 0.7f),
                    White = MaterialLibrary.Lit(Palette.White, 0.2f, 0.8f, Palette.White, 1.2f)
                };
                // Window facades use a generated repeating texture and unlit shader for legibility.
                var texA = TextureFactory.WindowFacade(3301 + i);
                var texB = TextureFactory.WindowFacade(9901 + i);
                m.BuildingA = MaterialLibrary.UnlitTextured("NV_WinA_" + i, texA,
                    Color.Lerp(theme.buildingA, Color.white, 0.25f), new Vector2(2f, 3f));
                m.BuildingB = MaterialLibrary.UnlitTextured("NV_WinB_" + i, texB,
                    Color.Lerp(theme.buildingB, Color.white, 0.22f), new Vector2(3f, 2f));
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

            int biome = currentIndex / 4 % 4;
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
            b.Box(mats.Ground, new Vector3(0f, -0.28f, half), new Vector3(9.4f, 0.55f, SegmentLength + 0.05f));
            b.Box(mats.GroundDark, new Vector3(0f, -0.75f, half), new Vector3(7.5f, 0.5f, SegmentLength));
            // Under-structure pillars create scale and depth without any gameplay colliders.
            for (int i = 0; i < 3; i++)
            {
                float z = 7f + i * 14f;
                b.Box(mats.GroundDark, new Vector3(0f, -6f, z), new Vector3(6.2f, 10f, 1.2f));
            }
            // Lane guide lines.
            for (int lane = 0; lane < 2; lane++)
            {
                float x = -1.35f + lane * 2.7f;
                b.Box(mats.Neon, new Vector3(x, 0.015f, half), new Vector3(0.035f, 0.02f, SegmentLength));
            }
            // Dashes at lane centres.
            for (int lane = 0; lane < 3; lane++)
            {
                float x = LaneMath.LaneToX(lane);
                for (int z = 2; z < SegmentLength; z += 9)
                {
                    b.Box(mats.Trim, new Vector3(x, 0.02f, z), new Vector3(0.55f, 0.02f, 0.16f));
                }
            }
            // Safety rails and edge light bars.
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(mats.Trim, new Vector3(side * 4.85f, 0.55f, half), new Vector3(0.16f, 1.1f, SegmentLength));
                b.Box(mats.NeonAlt, new Vector3(side * 4.85f, 1.12f, half), new Vector3(0.08f, 0.08f, SegmentLength));
                for (int z = 3; z < SegmentLength; z += 12)
                    b.Box(mats.GroundDark, new Vector3(side * 4.95f, 0.35f, z), new Vector3(0.28f, 0.7f, 0.38f));
            }
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
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    float z = 2f + i * 13f + rng.Range(0f, 4f);
                    float x = side * rng.Range(9f, 17f);
                    float width = rng.Range(3f, 6f);
                    float depth = rng.Range(4f, 9f);
                    float height = rng.Range(7f, 32f);
                    Material facade = i % 2 == 0 ? mats.BuildingA : mats.BuildingB;
                    b.Box(facade, new Vector3(x, height * 0.5f - 0.2f, z), new Vector3(width, height, depth));
                    b.Box(mats.Roof, new Vector3(x, height + 0.05f, z), new Vector3(width + 0.35f, 0.35f, depth + 0.35f));
                    float nearX = x - side * (width * 0.5f + 0.08f);
                    b.Box(mats.Neon, new Vector3(nearX, height * 0.55f, z), new Vector3(0.08f, height * 0.7f, 0.16f));
                    b.Box(mats.Billboard, new Vector3(nearX - side * 0.05f, height * 0.68f, z), new Vector3(0.1f, 2.6f, depth * 0.55f));
                    if (rng.Chance(0.55f))
                    {
                        b.Box(mats.Trim, new Vector3(x, height + 2.4f, z), new Vector3(0.09f, 4.8f, 0.09f));
                        b.Box(mats.NeonAlt, new Vector3(x, height + 4.8f, z), new Vector3(0.25f, 0.1f, 0.25f));
                    }
                }
                // Mid-distance skyline fill.
                float farX = side * rng.Range(24f, 33f);
                float farHeight = rng.Range(20f, 43f);
                b.Box(mats.BuildingB, new Vector3(farX, farHeight * 0.5f, half), new Vector3(7f, farHeight, 9f));
                b.Box(mats.Neon, new Vector3(farX - side * 3.55f, farHeight * 0.7f, half),
                    new Vector3(0.1f, farHeight * 0.4f, 0.25f));
            }
            // Hover lane pylons beyond the rails.
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 2; i++)
                {
                    float z = 8f + i * 19f;
                    b.Box(mats.Trim, new Vector3(side * 7.2f, 2.6f, z), new Vector3(0.16f, 5.2f, 0.16f));
                    b.Box(mats.Neon, new Vector3(side * 7.2f, 5.2f, z), new Vector3(0.5f, 0.12f, 0.5f));
                }
        }

        private static void BuildReactor(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                // Hazard wall.
                b.Box(mats.BuildingB, new Vector3(side * 7.2f, 2.4f, half), new Vector3(2.2f, 4.8f, SegmentLength));
                for (int i = 0; i < 5; i++)
                {
                    float z = 4f + i * 8f;
                    b.Box(mats.Neon, new Vector3(side * 6.05f, 2.8f, z), new Vector3(0.08f, 0.16f, 5.2f));
                    b.Box(mats.NeonAlt, new Vector3(side * 6.02f, 1.2f, z), new Vector3(0.06f, 1.2f, 0.18f));
                }
                // Horizontal coolant pipes running down the segment.
                b.Cylinder(mats.Pipe, new Vector3(side * 5.7f, 1.15f, half), new Vector3(0.28f, 21f, 0.28f),
                    Quaternion.Euler(90f, 0f, 0f));
                b.Cylinder(mats.Pipe, new Vector3(side * 6.2f, 3.7f, half), new Vector3(0.18f, 21f, 0.18f),
                    Quaternion.Euler(90f, 0f, 0f));
                // Tanks and vents.
                for (int i = 0; i < 2; i++)
                {
                    float z = 8f + i * 19f;
                    b.Cylinder(mats.BuildingA, new Vector3(side * 10f, 3.2f, z), new Vector3(1.8f, 3.2f, 1.8f));
                    b.Cylinder(mats.Neon, new Vector3(side * 10f, 6.2f, z), new Vector3(1.5f, 0.1f, 1.5f));
                }
                b.Box(mats.Trim, new Vector3(side * 6.8f, 8f, half), new Vector3(0.2f, 0.2f, SegmentLength));
            }
            // Overhead gantry.
            b.Box(mats.Trim, new Vector3(0f, 7.4f, 18f), new Vector3(15f, 0.35f, 0.4f));
            b.Box(mats.Neon, new Vector3(0f, 7.15f, 18f), new Vector3(10f, 0.06f, 0.1f));
        }

        private static void BuildTunnel(DecorMeshBuilder b, ThemeMaterials mats, ThemeDefinition theme, DeterministicRandom rng)
        {
            float half = SegmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(mats.BuildingA, new Vector3(side * 6.5f, 3.2f, half), new Vector3(1.2f, 6.4f, SegmentLength));
                for (int i = 0; i < 7; i++)
                {
                    float z = 3f + i * 6f;
                    b.Box(mats.Trim, new Vector3(side * 5.85f, 3.2f, z), new Vector3(0.2f, 6.2f, 0.3f));
                    b.Box(mats.Neon, new Vector3(side * 5.8f, 1.1f, z), new Vector3(0.06f, 0.8f, 0.25f));
                }
            }
            b.Box(mats.GroundDark, new Vector3(0f, 7f, half), new Vector3(13f, 1f, SegmentLength));
            for (int i = 0; i < 4; i++)
            {
                float z = 6f + i * 10f;
                b.Box(mats.Trim, new Vector3(0f, 5.2f, z), new Vector3(13.2f, 0.35f, 0.6f));
                b.Box(mats.Neon, new Vector3(0f, 5.02f, z), new Vector3(7f, 0.06f, 0.2f));
                b.Box(mats.NeonAlt, new Vector3(-4.6f, 4.2f, z), new Vector3(0.1f, 0.2f, 0.5f));
            }
        }

        private static void BuildGateway(DecorMeshBuilder b, ThemeMaterials mats, DeterministicRandom rng)
        {
            float z = 5f;
            for (int side = -1; side <= 1; side += 2)
                b.Box(mats.Trim, new Vector3(side * 5.25f, 2.7f, z), new Vector3(0.28f, 5.4f, 0.28f));
            b.Box(mats.GroundDark, new Vector3(0f, 5.45f, z), new Vector3(10.8f, 0.5f, 0.5f));
            b.Box(mats.Neon, new Vector3(0f, 5.15f, z), new Vector3(8.4f, 0.06f, 0.12f));
            b.Box(mats.Billboard, new Vector3(0f, 5.55f, z - 0.28f), new Vector3(4.4f, 0.25f, 0.05f));
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
                var final = new Mesh { name = "NV_SegmentDecor" };
                var subMeshes = new List<CombineInstance>();
                var mats = new List<Material>();
                var temporary = new List<Mesh>();
                foreach (var kv in _groups)
                {
                    if (kv.Value.Count == 0) continue;
                    var sub = new Mesh();
                    temporary.Add(sub);
                    sub.CombineMeshes(kv.Value.ToArray(), true, true);
                    for (int i = 0; i < sub.subMeshCount; i++)
                    {
                        subMeshes.Add(new CombineInstance { mesh = sub, subMeshIndex = i });
                        mats.Add(kv.Key);
                    }
                }
                final.CombineMeshes(subMeshes.ToArray(), false, true);
                final.RecalculateBounds();
                for (int i = 0; i < temporary.Count; i++) UnityEngine.Object.Destroy(temporary[i]);
                materials = mats.ToArray();
                return final;
            }
        }
    }
}
