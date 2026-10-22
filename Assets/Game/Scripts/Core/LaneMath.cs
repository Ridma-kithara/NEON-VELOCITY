using System;

namespace NeonVelocity.Core
{
    /// <summary>Pure helpers for the fixed three-lane runner layout.</summary>
    public static class LaneMath
    {
        public const int LaneCount = 3;
        public const float LaneWidth = 2.7f;

        public static float LaneToX(int lane) => (lane - 1) * LaneWidth;

        public static int ClampLane(int lane) => lane < 0 ? 0 : (lane > 2 ? 2 : lane);

        public static int MoveLane(int lane, int direction) => ClampLane(lane + Math.Sign(direction));

        public static float Swing(float x, float targetX, float startX, float duration, float elapsed)
        {
            if (duration <= 0.0001f || elapsed >= duration) return targetX;
            float t = Math.Max(0f, Math.Min(1f, elapsed / duration));
            float eased = t * t * (3f - 2f * t);
            return startX + (targetX - startX) * eased;
        }

        public static bool IsLaneInputValid(int currentLane, int direction) =>
            direction != 0 && ClampLane(currentLane + Math.Sign(direction)) != currentLane;
    }
}
