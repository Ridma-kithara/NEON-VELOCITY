using System;
using System.Collections.Generic;

namespace NeonVelocity.Core
{
    public sealed class SegmentPlan
    {
        public int Index;
        public int BiomeIndex;
        public int DifficultyTier;
        public string PatternId;
        public TrackRow[] Rows;
        public float WorldStartZ;
        public bool IsSafe;
        public bool IsBonus;
        public bool IsEvent;
        public int Seed;
        /// <summary>Authored junction overriding the segment rows, or null for ordinary track.</summary>
        public JunctionPlan Junction;
    }

    /// <summary>
    /// Deterministic segment director. All multiplayer peers receive the same race seed,
    /// so this produces exactly the same track for every runner without shipping obstacle
    /// state over the network. The director only chooses from human-validated patterns and
    /// enforces global action spacing.
    /// </summary>
    public sealed class TrackDirector
    {
        public const int RowsPerSegment = 6;
        public const float RowSpacing = 7f;
        public const float SegmentLength = RowsPerSegment * RowSpacing;
        public const int SafeOpeningSegments = 2;
        public const float FullWidthActionCooldown = 33f;

        private readonly int _seed;
        private readonly int _modeSalt;
        private readonly Dictionary<int, SegmentPlan> _plans = new Dictionary<int, SegmentPlan>();

        public TrackDirector(int seed, int modeSalt = 0)
        {
            _seed = seed;
            _modeSalt = modeSalt;
            ValidateLibrary();
        }

        public static void ValidateLibrary()
        {
            foreach (var pattern in TrackPatternLibrary.All)
            {
                var issues = TrackValidator.Validate(pattern);
                if (issues.Count > 0)
                    throw new InvalidOperationException("Invalid track pattern: " + string.Join(" | ", issues));
            }
        }

        public SegmentPlan GetSegment(int index)
        {
            if (_plans.TryGetValue(index, out SegmentPlan cached)) return cached;
            var plan = BuildSegment(index);
            _plans[index] = plan;
            // Keep memory bounded for very long runs, even with random access.
            if (_plans.Count > 192)
            {
                int oldest = int.MaxValue;
                foreach (int key in _plans.Keys)
                    if (key < oldest) oldest = key;
                if (oldest != int.MaxValue) _plans.Remove(oldest);
            }
            return plan;
        }

        /// <summary>Builds forward-only plans in order. Used by the runtime and by tests.</summary>
        public List<SegmentPlan> BuildRange(int startIndex, int count)
        {
            var results = new List<SegmentPlan>(count);
            for (int i = 0; i < count; i++)
            {
                var plan = BuildSegment(startIndex + i);
                results.Add(plan);
            }
            return results;
        }

        private SegmentPlan BuildSegment(int index)
        {
            var rng = new DeterministicRandom(unchecked(_seed + index * 73856093 + _modeSalt * 19349663));
            int worldDistance = index * RowsPerSegment;
            // Locations are a pure function of distance. The opening 3,000 m always resolves
            // to SAKURA CIRCUIT (zone 0), so every seed starts in the same readable district.
            int biome = LocationCatalog.IndexForDistance(index * SegmentLength);
            int difficulty = DifficultyForIndex(index);
            var rows = new TrackRow[RowsPerSegment];
            for (int i = 0; i < rows.Length; i++) rows[i] = new TrackRow(CellKind.Empty, CellKind.Empty, CellKind.Empty);

            var junction = JunctionScheduler.ForSegment(index, _seed);
            // A broken road is a full-width jump. Ordinary full-width patterns are kept out of
            // the neighbouring segments so the authored 30 m action-spacing guarantee survives
            // the junction override.
            bool nearBrokenRoad = JunctionScheduler.IsBrokenRoadNeighbour(index, _seed);

            bool safe = index < SafeOpeningSegments;
            bool bonus = !safe && rng.Chance(0.11f);
            bool specialEvent = !safe && !bonus && rng.Chance(0.07f);
            string patternId = safe ? "opening" : "bonus-arc";

            if (!safe)
            {
                // Full-width actions are confined to every third segment so the 33 m global
                // cooldown can be proven without carrying mutable state. This keeps random
                // access (seeks, live revive, tests) byte-identical to forward streaming.
                bool fullWidthSlot = index % 3 == 2;
                var candidates = new List<TrackPattern>();
                for (int i = 0; i < TrackPatternLibrary.All.Length; i++)
                {
                    var p = TrackPatternLibrary.All[i];
                    if (p.MinDifficulty > difficulty) continue;
                    if (p.Rows.Length > RowsPerSegment) continue;
                    if (ContainsFullWidthAction(p) && (!fullWidthSlot || nearBrokenRoad)) continue;
                    candidates.Add(p);
                }

                // At high tiers, favour the hardest authored patterns while always leaving
                // recovery patterns in the pool so a run can never become impossible.
                if (difficulty >= 5 && candidates.Count > 2)
                {
                    var harder = candidates.FindAll(p => p.MinDifficulty >= difficulty - 2);
                    if (harder.Count >= 2) candidates = harder;
                }

                if (candidates.Count == 0)
                {
                    if (junction != null)
                    {
                        JunctionScheduler.ApplyRows(junction, rows);
                        patternId = "junction-" + junction.Kind.ToString().ToLowerInvariant();
                    }
                    return new SegmentPlan
                    {
                        Index = index, BiomeIndex = biome, DifficultyTier = difficulty,
                        PatternId = junction != null ? patternId : "recovery", Rows = rows,
                        WorldStartZ = index * SegmentLength, IsSafe = junction == null,
                        IsEvent = junction != null, Seed = (int)rng.NextUInt(), Junction = junction
                    };
                }

                TrackPattern chosen;
                if (bonus)
                {
                    var coinPatterns = candidates.FindAll(p => p.CoinRich);
                    chosen = coinPatterns.Count > 0
                        ? coinPatterns[rng.Range(0, coinPatterns.Count)]
                        : candidates[rng.Range(0, candidates.Count)];
                }
                else chosen = candidates[rng.Range(0, candidates.Count)];

                patternId = chosen.Id;
                for (int r = 0; r < chosen.Rows.Length && r < rows.Length; r++) rows[r] = chosen.Rows[r];
            }

            if (junction != null)
            {
                JunctionScheduler.ApplyRows(junction, rows);
                patternId = "junction-" + junction.Kind.ToString().ToLowerInvariant();
                safe = false;
            }

            int previousLocation = index <= 0 ? biome : LocationCatalog.IndexForDistance((index - 1) * SegmentLength);
            return new SegmentPlan
            {
                Index = index,
                BiomeIndex = biome,
                DifficultyTier = difficulty,
                PatternId = patternId,
                Rows = rows,
                WorldStartZ = index * SegmentLength,
                IsSafe = safe,
                IsBonus = bonus || (safe && index == 1),
                IsEvent = specialEvent || junction != null || biome != previousLocation,
                Seed = (int)rng.NextUInt(),
                Junction = junction
            };
        }

        private static bool ContainsFullWidthAction(TrackPattern p)
        {
            foreach (var row in p.Rows)
            {
                bool allLow = row.Left == CellKind.LowBarrier && row.Centre == CellKind.LowBarrier && row.Right == CellKind.LowBarrier;
                bool allHigh = row.Left == CellKind.OverheadGate && row.Centre == CellKind.OverheadGate && row.Right == CellKind.OverheadGate;
                if (allLow || allHigh) return true;
            }
            return false;
        }

        private static int FirstFullWidthRow(TrackPattern p)
        {
            for (int i = 0; i < p.Rows.Length; i++)
            {
                var row = p.Rows[i];
                if ((row.Left == CellKind.LowBarrier && row.Centre == CellKind.LowBarrier && row.Right == CellKind.LowBarrier) ||
                    (row.Left == CellKind.OverheadGate && row.Centre == CellKind.OverheadGate && row.Right == CellKind.OverheadGate))
                    return i;
            }
            return 0;
        }

        public static int DifficultyForIndex(int index)
        {
            float distance = index * SegmentLength;
            if (distance < 450f) return 0;
            if (distance < 1100f) return 1;
            if (distance < 2200f) return 2;
            if (distance < 4000f) return 3;
            if (distance < 6500f) return 4;
            if (distance < 9500f) return 5;
            if (distance < 13500f) return 6;
            if (distance < 19000f) return 7;
            return 8;
        }

        public static int SeedForRun(string modeId, DateTime utc, string playerId = "")
        {
            int hash = DeterministicRandom.StableHash(modeId + ":" + utc.ToString("yyyyMMddHH") + ":" + playerId);
            return hash == 0 ? 1 : hash;
        }
    }

    /// <summary>Validates full generated sequences, including boundaries between segments.</summary>
    public static class GeneratedTrackValidator
    {
        public static List<string> Validate(IReadOnlyList<SegmentPlan> segments, float rowSpacing = TrackDirector.RowSpacing)
        {
            var issues = new List<string>();
            if (segments == null) { issues.Add("Segment list is null."); return issues; }
            var rows = new List<TrackRow>();
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (segment == null) { issues.Add($"Null segment at {i}."); continue; }
                rows.AddRange(segment.Rows);
            }

            if (rows.Count == 0) return issues;
            var reachable = new bool[LaneMath.LaneCount];
            for (int lane = 0; lane < LaneMath.LaneCount; lane++) reachable[lane] = !rows[0].IsSolid(lane);
            if (!Any(reachable)) issues.Add("First row blocks every lane.");

            float lastFullAction = -1000f;
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                bool any = false;
                bool allLow = true;
                bool allHigh = true;
                for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                {
                    if (!row.IsSolid(lane)) any = true;
                    if (!row.RequiresJump(lane)) allLow = false;
                    if (!row.RequiresSlide(lane)) allHigh = false;
                }
                if (!any) issues.Add($"Row {r} blocks every lane.");
                if (allLow || allHigh)
                {
                    float z = r * rowSpacing;
                    if (z - lastFullAction < TrackValidator.MinimumFullRowActionSpacing - 0.01f)
                        issues.Add($"Full-width actions at rows {r - 1}/{r} are too close.");
                    lastFullAction = z;
                }

                if (r > 0)
                {
                    var next = new bool[LaneMath.LaneCount];
                    for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                    {
                        if (row.IsSolid(lane)) continue;
                        if (reachable[lane] || (lane > 0 && reachable[lane - 1]) ||
                            (lane < LaneMath.LaneCount - 1 && reachable[lane + 1])) next[lane] = true;
                    }
                    reachable = next;
                    if (!Any(reachable)) issues.Add($"No navigable lane at row {r}.");
                }
            }
            return issues;
        }

        private static bool Any(bool[] values)
        {
            for (int i = 0; i < values.Length; i++) if (values[i]) return true;
            return false;
        }
    }
}
