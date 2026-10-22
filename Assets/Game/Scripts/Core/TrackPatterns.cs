using System;
using System.Collections.Generic;
using System.Text;

namespace NeonVelocity.Core
{
    public enum CellKind
    {
        Empty = 0,
        LowBarrier = 1,   // jump
        OverheadGate = 2, // slide
        Solid = 3,        // change lane
        Electric = 4,     // jump
        Drone = 5,        // slide or change lane
        MovingBlocker = 6 // change lane, telegraphed
    }

    public readonly struct TrackRow
    {
        public readonly CellKind Left;
        public readonly CellKind Centre;
        public readonly CellKind Right;

        public TrackRow(CellKind left, CellKind centre, CellKind right)
        {
            Left = left;
            Centre = centre;
            Right = right;
        }

        public CellKind this[int lane] => lane <= 0 ? Left : (lane == 1 ? Centre : Right);

        public bool CanPassWithoutAction(int lane) => this[lane] == CellKind.Empty;
        public bool RequiresSlide(int lane) => this[lane] == CellKind.OverheadGate || this[lane] == CellKind.Drone;
        public bool RequiresJump(int lane) => this[lane] == CellKind.LowBarrier || this[lane] == CellKind.Electric;
        public bool IsSolid(int lane) => this[lane] == CellKind.Solid || this[lane] == CellKind.MovingBlocker;
    }

    /// <summary>
    /// A hand-authored obstacle pattern. Patterns are deliberately short so they can be
    /// composed into varied but always navigable segments.
    /// </summary>
    public sealed class TrackPattern
    {
        public readonly string Id;
        public readonly int MinDifficulty;
        public readonly float RowSpacing;
        public readonly TrackRow[] Rows;
        public readonly bool CoinRich;
        public readonly bool Safe;

        public TrackPattern(string id, int minDifficulty, float rowSpacing, bool coinRich, params string[] rows)
        {
            Id = id;
            MinDifficulty = minDifficulty;
            RowSpacing = rowSpacing;
            CoinRich = coinRich;
            Safe = rows.Length == 0;
            Rows = new TrackRow[rows.Length];
            for (int i = 0; i < rows.Length; i++) Rows[i] = ParseRow(rows[i]);
        }

        public static TrackRow ParseRow(string row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (row.Length != 3) throw new FormatException($"Track row '{row}' must contain exactly 3 cells.");
            return new TrackRow(ParseCell(row[0]), ParseCell(row[1]), ParseCell(row[2]));
        }

        private static CellKind ParseCell(char c) => c switch
        {
            '.' => CellKind.Empty,
            'L' => CellKind.LowBarrier,
            'H' => CellKind.OverheadGate,
            'X' => CellKind.Solid,
            'E' => CellKind.Electric,
            'D' => CellKind.Drone,
            'M' => CellKind.MovingBlocker,
            _ => throw new FormatException($"Unknown track cell '{c}'.")
        };
    }

    /// <summary>Validates that a sequence of rows contains a physically navigable path.</summary>
    public static class TrackValidator
    {
        public const float MinimumFullRowActionSpacing = 30f;

        public static List<string> Validate(TrackPattern pattern)
        {
            var issues = new List<string>();
            if (pattern == null) { issues.Add("Pattern is null."); return issues; }
            if (pattern.Rows.Length == 0) return issues;

            float lastFullActionDistance = -1000f;
            for (int r = 0; r < pattern.Rows.Length; r++)
            {
                var row = pattern.Rows[r];
                bool anyPassable = false;
                bool allLow = true;
                bool allHigh = true;
                for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                {
                    if (!row.IsSolid(lane)) anyPassable = true;
                    if (!row.RequiresJump(lane)) allLow = false;
                    if (!row.RequiresSlide(lane)) allHigh = false;
                }

                if (!anyPassable) issues.Add($"{pattern.Id}: row {r} blocks every lane.");
                if (allLow || allHigh)
                {
                    float worldZ = r * pattern.RowSpacing;
                    if (worldZ - lastFullActionDistance < MinimumFullRowActionSpacing)
                        issues.Add($"{pattern.Id}: full-width actions at row {r} are too close together.");
                    lastFullActionDistance = worldZ;
                }
            }

            if (!HasLanePath(pattern)) issues.Add($"{pattern.Id}: no continuous lane path exists.");
            return issues;
        }

        /// <summary>
        /// Breadth-first search across rows. A player may stay in a lane or move one lane
        /// between rows; all non-solid lanes are reachable by jumping or sliding.
        /// </summary>
        public static bool HasLanePath(TrackPattern pattern)
        {
            if (pattern.Rows.Length == 0) return true;
            bool[] reachable = new bool[LaneMath.LaneCount];
            for (int lane = 0; lane < LaneMath.LaneCount; lane++) reachable[lane] = !pattern.Rows[0].IsSolid(lane);
            if (!Any(reachable)) return false;

            for (int r = 1; r < pattern.Rows.Length; r++)
            {
                bool[] next = new bool[LaneMath.LaneCount];
                for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                {
                    if (pattern.Rows[r].IsSolid(lane)) continue;
                    if (reachable[lane] ||
                        (lane > 0 && reachable[lane - 1]) ||
                        (lane < LaneMath.LaneCount - 1 && reachable[lane + 1]))
                        next[lane] = true;
                }
                reachable = next;
                if (!Any(reachable)) return false;
            }
            return true;
        }

        private static bool Any(bool[] values)
        {
            for (int i = 0; i < values.Length; i++) if (values[i]) return true;
            return false;
        }

        public static string Describe(TrackPattern pattern)
        {
            var sb = new StringBuilder();
            foreach (var row in pattern.Rows)
                sb.Append(row.Left).Append(row.Centre).Append(row.Right).Append('/');
            return sb.ToString();
        }
    }

    /// <summary>Original obstacle vocabulary used by the procedural track director.</summary>
    public static class TrackPatternLibrary
    {
        public static readonly TrackPattern[] All =
        {
            new TrackPattern("clean-run", 0, 7f, false),
            new TrackPattern("coin-lane", 0, 7f, true),
            new TrackPattern("single-hop", 0, 7f, false,
                ".L.", "...", "..."),
            new TrackPattern("single-duck", 0, 7f, false,
                ".H.", "...", "..."),
            new TrackPattern("weave-left", 0, 7f, false,
                "X..", "X..", "..."),
            new TrackPattern("weave-right", 0, 7f, false,
                "..X", "..X", "..."),
            new TrackPattern("double-crate", 0, 7f, false,
                "XX.", ".X.", "..."),
            new TrackPattern("hop-weave", 1, 7f, false,
                ".L.", "XL.", ".X.", "..."),
            new TrackPattern("duck-weave", 1, 7f, false,
                ".H.", "XH.", ".X.", "..."),
            new TrackPattern("electric-lanes", 1, 7f, false,
                "E..", "EX.", ".X.", "..."),
            new TrackPattern("drone-corridor", 1, 7f, false,
                "D.D", ".X.", "..."),
            new TrackPattern("crate-stack", 1, 7f, false,
                "X.L", "X..", "..X", "..."),
            new TrackPattern("full-hurdle", 1, 8f, false,
                "LLL", "...", "..."),
            new TrackPattern("full-laser", 1, 8f, false,
                "HHH", "...", "..."),
            new TrackPattern("slalom", 2, 7f, false,
                "X.L", "X.L", "..X", "X..", "..."),
            new TrackPattern("moving-blockers", 2, 7.5f, false,
                "M..", ".M.", "..M", "M..", "..."),
            new TrackPattern("gauntlet", 3, 7f, false,
                "X.L", "XX.", "H..", "..X", "L..", "..."),
            new TrackPattern("drone-dance", 3, 7f, false,
                "D.X", "X.D", ".X.", "..."),
            new TrackPattern("overdrive-weave", 4, 7f, false,
                "X.L", "X.X", "XX.", "X.H", "X..", "..."),
            new TrackPattern("bonus-arc", 0, 7f, true,
                "L..", "...", "..."),
        };
    }
}
