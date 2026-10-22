using System;

namespace NeonVelocity.Core
{
    /// <summary>
    /// Frame-rate independent speed progression. Speed is derived from run distance and
    /// elapsed race time, never from frame count. Values are in metres and metres/second.
    /// </summary>
    public sealed class SpeedModel
    {
        public float BaseSpeed { get; }
        public float MaxSpeed { get; }
        public float AccelerationPerSecond { get; }
        public float AccelerationPerDistance { get; }
        public float BoostMultiplier { get; private set; } = 1f;

        public SpeedModel(
            float baseSpeed = 9f,
            float maxSpeed = 34f,
            float accelerationPerSecond = 0.16f,
            float accelerationPerDistance = 0.004f)
        {
            BaseSpeed = Math.Max(1f, baseSpeed);
            MaxSpeed = Math.Max(BaseSpeed + 1f, maxSpeed);
            AccelerationPerSecond = Math.Max(0f, accelerationPerSecond);
            AccelerationPerDistance = Math.Max(0f, accelerationPerDistance);
        }

        public float SpeedAt(float distanceMetres, float elapsedSeconds)
        {
            float distanceTerm = Math.Max(0f, distanceMetres) * AccelerationPerDistance;
            float timeTerm = Math.Max(0f, elapsedSeconds) * AccelerationPerSecond;
            float normalised = (BaseSpeed + distanceTerm + timeTerm) / MaxSpeed;
            // Soft saturation keeps the last approach smooth instead of snapping at the cap.
            normalised = 1f - (float)Math.Exp(-normalised * 1.15f);
            float raw = BaseSpeed + (MaxSpeed - BaseSpeed) * normalised;
            return Math.Min(MaxSpeed, raw) * BoostMultiplier;
        }

        public float SpeedAtNormalised(float distanceMetres) =>
            BaseSpeed + (MaxSpeed - BaseSpeed) * (1f - (float)Math.Exp(-Math.Max(0f, distanceMetres) / 1800f));

        /// <summary>Call every frame with an instantaneous multiplier (for Speed Boost).</summary>
        public void SetBoost(float multiplier) => BoostMultiplier = Math.Max(1f, multiplier);

        /// <summary>Maximum distance physically reachable in a window, used by validation.</summary>
        public float MaxPlausibleDistance(float elapsedSeconds) => MaxSpeed * BoostMultiplier * Math.Max(0f, elapsedSeconds) + 25f;

        public int DifficultyStage(float distanceMetres)
        {
            if (distanceMetres < 450f) return 0;
            if (distanceMetres < 1100f) return 1;
            if (distanceMetres < 2200f) return 2;
            if (distanceMetres < 4000f) return 3;
            return 4;
        }
    }
}
