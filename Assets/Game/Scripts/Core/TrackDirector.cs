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
        private TrackPattern _lastPattern;
        private TrackPattern _secondLastPattern;
        private float _lastFullWidthActionDistance = -1000f;
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
            // Keep memory bounded for very long runs.
            if (_plans.Count > 256)
            {
                int oldest = index - 128;
                _plans.Remove(oldest);
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
            var biome = ((index / 4) + Math.Abs(_seed % 4)) % 4;
            int difficulty = DifficultyForIndex(index);
            var rows = new TrackRow[RowsPerSegment];
            for (int i = 0; i < rows.Length; i++) rows[i] = new TrackRow(CellKind.Empty, CellKind.Empty, CellKind.Empty);

            bool safe = index < SafeOpeningSegments;
            bool bonus = !safe && rng.Chance(0.11f);
            bool specialEvent = !safe && !bonus && rng.Chance(0.06f);
            string patternId = safe ? "opening" : "bonus-arc";

            if (!safe)
            {
                var candidates = new List<TrackPattern>();
                for (int i = 0; i < TrackPatternLibrary.All.Length; i++)
                {
                    var p = TrackPatternLibrary.All[i];
                    if (p.MinDifficulty > difficulty) continue;
                    if (p == _lastPattern || p == _secondLastPattern) continue;
                    if (p.Rows.Length > RowsPerSegment) continue;
                    float placementZ = (worldDistance + (index < SafeOpeningSegments ? 0 : 0)) * RowSpacing;
                    if (ContainsFullWidthAction(p) && !CanPlaceFullWidthPattern(index, placementZ)) continue;
                    candidates.Add(p);
                }

                if (candidates.Count == 0)
                {
                    _lastPattern = null;
                    return new SegmentPlan
                    {
                        Index = index, BiomeIndex = biome, DifficultyTier = difficulty,
                        PatternId = "recovery", Rows = rows, WorldStartZ = index * SegmentLength,
                        IsSafe = true, Seed = rng.NextUInt().GetHashCode()
                    };
                }

                // Bias toward bonus coin patterns in Coin Rush (callers pass a coin-rich salt).
                TrackPattern chosen = candidates[rng.Range(0, candidates.Count)];
                if (bonus)
                {
                    var coinPatterns = candidates.FindAll(p => p.CoinRich);
                    if (coinPatterns.Count > 0) chosen = coinPatterns[rng.Range(0, coinPatterns.Count)];
                    else chosen = candidates[rng.Range(0, candidates.Count)];
                }

                patternId = chosen.Id;
                int rowOffset = 0;
                for (int r = 0; r < chosen.Rows.Length && r < rows.Length; r++)
                {
                    int logicalRow = worldDistance + rowOffset + r;
                    rows[r] = chosen.Rows[r];
                }

                if (ContainsFullWidthAction(chosen))
                    _lastFullWidthActionDistance = (index * RowsPerSegment + FirstFullWidthRow(chosen)) * RowSpacing;

                _secondLastPattern = _lastPattern;
                _lastPattern = chosen;
            }

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
                IsEvent = specialEvent,
                Seed = (int)rng.NextUInt()
            };
        }

        private bool CanPlaceFullWidthPattern(int index, float unused)
        {
            // Full-width actions only start in the first two rows and must respect the
            // global cooldown from the previous full-width pattern.
            float startDistance = index * SegmentLength;
            return startDistance - _lastFullWidthActionDistance >= FullWidthActionCooldown;
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
            return 4;
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
