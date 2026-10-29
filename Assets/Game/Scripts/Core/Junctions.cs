using System;

namespace NeonVelocity.Core
{
    /// <summary>
    /// Authored junction shapes. Every shape is generated from the same road grid; the
    /// curved shapes additionally displace the runnable lanes laterally along the segment.
    /// </summary>
    public enum JunctionKind
    {
        Split,
        Crossroads,
        NarrowBridge,
        BrokenRoad,
        TunnelBranch,
        TJunction,
        CurvedRoad,
        Shortcut,
        CollapsingBridge
    }

    public enum JunctionPhase
    {
        None,
        Warning,
        Commit,
        Branch,
        Merge
    }

    public enum JunctionBranch
    {
        None,
        Left,
        Right,
        Centre
    }

    /// <summary>
    /// One deterministic junction. The plan is a pure function of the run seed and segment
    /// index, so a live revive, a seek or another multiplayer peer always resolves the same
    /// physical road without transporting mutable junction state.
    /// </summary>
    public sealed class JunctionPlan
    {
        public int SegmentIndex;
        public JunctionKind Kind;
        public int Seed;
        public int CommitRow;
        public int BranchStartRow;
        public int BranchEndRow;
        public int MergeRow;
        public bool RewardOnLeft;
        public bool RewardIsSecret;
        /// <summary>-1 curves left, +1 curves right. Only used by curved shapes.</summary>
        public int CurveSign = 1;
        /// <summary>Peak lateral displacement in metres at the middle of a curved route.</summary>
        public float CurveMetres;

        public float WorldStartZ => SegmentIndex * TrackDirector.SegmentLength;
        public float WorldCommitZ => WorldStartZ + CommitRow * TrackDirector.RowSpacing;
        public float WorldBranchStartZ => WorldStartZ + BranchStartRow * TrackDirector.RowSpacing;
        public float WorldMergeZ => WorldStartZ + MergeRow * TrackDirector.RowSpacing;
        public float WorldEndZ => WorldStartZ + (MergeRow + 2) * TrackDirector.RowSpacing;

        public bool IsTJunction => Kind == JunctionKind.TJunction;
        public bool IsCurvedRoad => Kind == JunctionKind.CurvedRoad;
        public bool IsShortcut => Kind == JunctionKind.Shortcut;
        public bool IsCollapsingBridge => Kind == JunctionKind.CollapsingBridge;
        public bool HasCurvedRoute => IsTJunction || IsCurvedRoad || IsShortcut;
        public bool UsesMedian => Kind == JunctionKind.Split || Kind == JunctionKind.Crossroads ||
                                  Kind == JunctionKind.TunnelBranch || IsTJunction;
        public bool RequiresSideChoice => UsesMedian;
        public bool IsNarrowBridge => Kind == JunctionKind.NarrowBridge;
        public bool IsBrokenRoad => Kind == JunctionKind.BrokenRoad || IsCollapsingBridge;
        public int RewardLane => RewardOnLeft ? 0 : 2;
        public int StandardLane => RewardOnLeft ? 2 : 0;
        public int WarningStartSegment => Math.Max(0, SegmentIndex - JunctionRules.WarningLeadSegments);
        public string DisplayName => Kind switch
        {
            JunctionKind.Split => "SPLIT",
            JunctionKind.Crossroads => "CROSSROAD",
            JunctionKind.NarrowBridge => "NARROW BRIDGE",
            JunctionKind.BrokenRoad => "BROKEN ROAD",
            JunctionKind.TunnelBranch => "TUNNEL BRANCH",
            JunctionKind.TJunction => "T JUNCTION",
            JunctionKind.CurvedRoad => "CURVED ROAD",
            JunctionKind.Shortcut => "SHORTCUT",
            JunctionKind.CollapsingBridge => "COLLAPSING BRIDGE",
            _ => "JUNCTION"
        };

        public bool IsBranchRow(int row) => row >= BranchStartRow && row < MergeRow;
        public bool IsMedianRow(int row) => UsesMedian && IsBranchRow(row);
        public bool IsNarrowRow(int row) => IsNarrowBridge && row >= BranchStartRow && row < MergeRow;
    }

    /// <summary>Shared, engine-free junction rules used by the runner, track and tests.</summary>
    public static class JunctionRules
    {
        /// <summary>Three 42 m segments of advance warning (126 m).</summary>
        public const int WarningLeadSegments = 3;
        public const int SpacingSegments = 15;
        public const int FirstJunctionSegment = 10;
        public const int WarningDistanceMetres = WarningLeadSegments * 42;
        /// <summary>Portion of a branch used to ease into and out of a lateral curve.</summary>
        private const float CurveEase = 0.32f;

        public static JunctionBranch BranchForLane(int lane) =>
            lane <= 0 ? JunctionBranch.Left : (lane >= 2 ? JunctionBranch.Right : JunctionBranch.None);

        public static int LaneForBranch(JunctionBranch branch) => branch switch
        {
            JunctionBranch.Left => 0,
            JunctionBranch.Right => 2,
            JunctionBranch.Centre => 1,
            _ => 1
        };

        /// <summary>
        /// Chooses a safe branch for a runner that reaches the commit line without a side
        /// selected. The centre lane is physically blocked by a median at every side-choice
        /// junction, so this guarantee prevents an unavoidable collision.
        /// </summary>
        public static JunctionBranch CommitForLane(int lane, int fallbackDirection, JunctionPlan plan)
        {
            if (plan == null || plan.IsCurvedRoad || plan.IsShortcut) return JunctionBranch.None;
            if (lane == 0) return JunctionBranch.Left;
            if (lane == 2) return JunctionBranch.Right;
            if (!plan.RequiresSideChoice) return JunctionBranch.None;
            if (fallbackDirection < 0) return JunctionBranch.Left;
            if (fallbackDirection > 0) return JunctionBranch.Right;
            return plan.RewardOnLeft ? JunctionBranch.Left : JunctionBranch.Right;
        }

        /// <summary>
        /// The lane the player is allowed to occupy at a branch. A committed side branch is
        /// held until the merge; the centre lane remains available until the median starts so
        /// the player can still make the choice during the approach.
        /// </summary>
        public static bool LaneAllowed(int lane, JunctionPlan plan, JunctionPhase phase, JunctionBranch committed)
        {
            if (plan == null) return true;
            if (plan.IsCurvedRoad || plan.IsShortcut) return true;
            if (phase != JunctionPhase.Branch && phase != JunctionPhase.Commit) return true;
            if (plan.IsNarrowBridge) return lane == 1;
            if (!plan.UsesMedian) return true;
            if (committed == JunctionBranch.Left) return lane == 0;
            if (committed == JunctionBranch.Right) return lane == 2;
            return true;
        }

        public static int ClampLane(int lane, JunctionPlan plan, JunctionPhase phase, JunctionBranch committed)
        {
            lane = LaneMath.ClampLane(lane);
            if (plan == null) return lane;
            if (plan.IsNarrowBridge && (phase == JunctionPhase.Branch || phase == JunctionPhase.Commit))
                return 1;
            if (plan.IsCurvedRoad || plan.IsShortcut) return lane;
            if (phase != JunctionPhase.Branch && phase != JunctionPhase.Commit) return lane;
            if (committed == JunctionBranch.Left) return 0;
            if (committed == JunctionBranch.Right) return 2;
            return lane;
        }

        /// <summary>
        /// Lateral displacement of a runnable lane at a distance local to the junction's
        /// segment. Curved roads bend every lane together, a T-junction bends each side lane
        /// outward around the median, and a shortcut bends only its marked side lane. The
        /// displacement eases to zero at both ends so neighbouring straight segments join
        /// without a seam.
        /// </summary>
        public static float RouteOffsetAt(JunctionPlan plan, float localZ, int lane)
        {
            if (plan == null || !plan.HasCurvedRoute) return 0f;
            float start = plan.BranchStartRow * TrackDirector.RowSpacing;
            float end = plan.MergeRow * TrackDirector.RowSpacing;
            float length = end - start;
            if (length < 0.01f || localZ <= start || localZ >= end) return 0f;
            float t = (localZ - start) / length;
            float bend = SmoothBell(t) * Math.Max(0f, plan.CurveMetres);
            if (plan.IsCurvedRoad)
                return (plan.CurveSign < 0 ? -bend : bend);
            if (plan.IsTJunction)
            {
                if (lane <= 0) return -bend;
                if (lane >= 2) return bend;
                return 0f;
            }
            if (plan.IsShortcut && lane == plan.RewardLane)
                return plan.RewardOnLeft ? -bend : bend;
            return 0f;
        }

        /// <summary>Slope of the lateral displacement, used to yaw curved road pieces.</summary>
        public static float RouteSlopeAt(JunctionPlan plan, float localZ, int lane)
        {
            const float sample = 0.5f;
            float before = RouteOffsetAt(plan, localZ - sample, lane);
            float after = RouteOffsetAt(plan, localZ + sample, lane);
            return (after - before) / (sample * 2f);
        }

        /// <summary>0 at both ends, 1 across the middle third; smooth C1 ramps at the joins.</summary>
        public static float SmoothBell(float t)
        {
            t = Clamp01(t);
            return Smooth01(t / CurveEase) * Smooth01((1f - t) / CurveEase);
        }

        private static float Smooth01(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);

        /// <summary>Resolves a distance to the visible phase of one junction.</summary>
        public static JunctionState Resolve(JunctionPlan plan, float distance)
        {
            if (plan == null) return default;
            float warning = plan.WorldCommitZ - WarningDistanceMetres;
            JunctionPhase phase;
            if (distance < warning) phase = JunctionPhase.None;
            else if (distance < plan.WorldCommitZ) phase = JunctionPhase.Warning;
            else if (distance < plan.WorldBranchStartZ) phase = JunctionPhase.Commit;
            else if (distance < plan.WorldMergeZ) phase = JunctionPhase.Branch;
            else if (distance < plan.WorldEndZ) phase = JunctionPhase.Merge;
            else phase = JunctionPhase.None;
            return new JunctionState
            {
                Plan = plan,
                Phase = phase,
                MetresToCommit = plan.WorldCommitZ - distance,
                MetresToMerge = plan.WorldMergeZ - distance
            };
        }
    }

    public struct JunctionState
    {
        public JunctionPlan Plan;
        public JunctionPhase Phase;
        public float MetresToCommit;
        public float MetresToMerge;
        public bool IsActive => Plan != null && Phase != JunctionPhase.None;
        public bool HasPlan => Plan != null;
    }

    /// <summary>
    /// Deterministic junction scheduler. A junction appears every fifteen segments, offset by
    /// the run seed, which leaves about 630 m of ordinary track between junctions. The first
    /// junction cannot appear before 420 m, so the opening is always readable.
    /// </summary>
    public static class JunctionScheduler
    {
        private static readonly JunctionKind[] Cycle =
        {
            JunctionKind.Split,
            JunctionKind.TJunction,
            JunctionKind.CurvedRoad,
            JunctionKind.NarrowBridge,
            JunctionKind.Shortcut,
            JunctionKind.Crossroads,
            JunctionKind.TunnelBranch,
            JunctionKind.BrokenRoad,
            JunctionKind.CollapsingBridge
        };

        public static int SlotForSeed(int seed) => Math.Abs(seed % JunctionRules.SpacingSegments);

        public static int FirstSegmentForSeed(int seed) =>
            JunctionRules.FirstJunctionSegment + SlotForSeed(seed);

        public static bool IsJunctionSegment(int segmentIndex, int seed)
        {
            if (segmentIndex < JunctionRules.FirstJunctionSegment) return false;
            int first = FirstSegmentForSeed(seed);
            return segmentIndex >= first &&
                   (segmentIndex - first) % JunctionRules.SpacingSegments == 0;
        }

        public static JunctionPlan ForSegment(int segmentIndex, int seed)
        {
            if (!IsJunctionSegment(segmentIndex, seed)) return null;
            int first = FirstSegmentForSeed(seed);
            int ordinal = (segmentIndex - first) / JunctionRules.SpacingSegments;
            JunctionKind kind = Cycle[ordinal % Cycle.Length];
            var rng = new DeterministicRandom(unchecked(seed + segmentIndex * 83492791));
            bool rewardLeft = rng.Chance(0.5f);
            bool secret = rng.Chance(0.34f);
            int curveSign = rng.Chance(0.5f) ? -1 : 1;
            float curveMetres = kind switch
            {
                JunctionKind.CurvedRoad => 1.25f,
                JunctionKind.TJunction => 1.15f,
                JunctionKind.Shortcut => 1.45f,
                _ => 0f
            };
            int commitRow = kind == JunctionKind.NarrowBridge ? 0 : 1;
            int branchStartRow = kind == JunctionKind.NarrowBridge ? 1 : 2;
            if (kind == JunctionKind.CurvedRoad)
            {
                // A curved road owns the whole middle of the segment so the bend reads as a
                // road, not as a short obstacle. The ease still returns to centre at row 5.
                commitRow = 0;
                branchStartRow = 1;
            }
            return new JunctionPlan
            {
                SegmentIndex = segmentIndex,
                Kind = kind,
                Seed = (int)rng.NextUInt(),
                CommitRow = commitRow,
                BranchStartRow = branchStartRow,
                BranchEndRow = 4,
                MergeRow = 5,
                RewardOnLeft = rewardLeft,
                RewardIsSecret = secret,
                CurveSign = curveSign,
                CurveMetres = curveMetres
            };
        }

        /// <summary>True when the segment itself or a neighbouring segment is a broken road.</summary>
        public static bool IsBrokenRoadNeighbour(int segmentIndex, int seed)
        {
            for (int offset = -1; offset <= 1; offset++)
            {
                var plan = ForSegment(segmentIndex + offset, seed);
                if (plan != null && plan.IsBrokenRoad) return true;
            }
            return false;
        }

        /// <summary>Writes the physical cells of a junction into the segment row buffer.</summary>
        public static void ApplyRows(JunctionPlan plan, TrackRow[] rows)
        {
            if (plan == null || rows == null) return;
            // Curved roads keep their ordinary pattern because every lane stays connected.
            // A shortcut also keeps its pattern; only its marked lane is cleared at spawn time.
            if (plan.IsCurvedRoad || plan.IsShortcut) return;
            // Clear the authored pattern so the junction owns its approach and exit.
            for (int i = 0; i < rows.Length; i++)
                rows[i] = new TrackRow(CellKind.Empty, CellKind.Empty, CellKind.Empty);
            if (plan.IsBrokenRoad)
            {
                // One full-width jump. TrackDirector suppresses full-width actions in the two
                // neighbouring segments, preserving the 30 m fairness spacing.
                rows[2] = new TrackRow(CellKind.LowBarrier, CellKind.LowBarrier, CellKind.LowBarrier);
                return;
            }
            for (int row = plan.BranchStartRow; row < plan.MergeRow && row < rows.Length; row++)
            {
                if (plan.UsesMedian)
                    rows[row] = new TrackRow(CellKind.Empty, CellKind.Solid, CellKind.Empty);
                else if (plan.IsNarrowBridge)
                    rows[row] = new TrackRow(CellKind.Solid, CellKind.Empty, CellKind.Solid);
            }
        }
    }
}
