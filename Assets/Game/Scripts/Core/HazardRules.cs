using System.Collections.Generic;

namespace NeonVelocity.Core
{
    /// <summary>
    /// Pure placement rules for event-only signature hazards (maglev trains, traffic,
    /// falling debris, moving platforms). A signature hazard is only allowed on a row that is
    /// empty across all lanes and whose neighbouring rows are also empty. That guarantee keeps
    /// a sweeping train or a dropping block from combining with a lane action the player has
    /// not had a chance to read, so every event remains fair at any speed.
    /// </summary>
    public static class HazardRules
    {
        /// <summary>First row that may receive an event hazard.</summary>
        public const int FirstCandidateRow = 1;
        /// <summary>Last row that may receive an event hazard, before the segment exit.</summary>
        public const int LastCandidateRow = 4;

        public static bool RowIsEmpty(IReadOnlyList<TrackRow> rows, int row)
        {
            if (rows == null || row < 0 || row >= rows.Count) return false;
            for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                if (rows[row][lane] != CellKind.Empty) return false;
            return true;
        }

        /// <summary>
        /// Finds a row whose previous, own and next rows are all empty. Rows are scanned
        /// backwards so the hazard sits late in the segment, giving the player the longest
        /// possible view of it. Returns -1 when the pattern leaves no fair slot.
        /// </summary>
        public static int FindSignatureRow(IReadOnlyList<TrackRow> rows)
        {
            if (rows == null || rows.Count < 3) return -1;
            int last = System.Math.Min(LastCandidateRow, rows.Count - 2);
            for (int row = last; row >= FirstCandidateRow; row--)
            {
                if (RowIsEmpty(rows, row - 1) && RowIsEmpty(rows, row) && RowIsEmpty(rows, row + 1))
                    return row;
            }
            return -1;
        }

        /// <summary>
        /// True when a signature hazard would be safe at this row. Exposed separately so the
        /// runtime can assert the same rule the tests verify.
        /// </summary>
        public static bool CanHostSignatureHazard(IReadOnlyList<TrackRow> rows, int row) =>
            RowIsEmpty(rows, row - 1) && RowIsEmpty(rows, row) && RowIsEmpty(rows, row + 1);
    }
}
