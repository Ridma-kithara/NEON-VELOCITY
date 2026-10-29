using System;
using System.Collections.Generic;

namespace NeonVelocity.Core
{
    public static class ProgressionMath
    {
        public const int MaxPlayerLevel = LevelProgression.MaxLevel;
        public const int MaxUpgradeLevel = 5;

        public static long XpRequiredForLevel(int level) => LevelProgression.RequirementForLevel(level);
        public static long TotalXpForLevel(int level) => LevelProgression.TotalXpForLevel(level);

        /// <summary>Adds XP and returns the number of levels gained. Overflow-safe.</summary>
        public static int AddXp(ref long totalXp, long amount, out int newLevel) =>
            LevelProgression.AddXp(ref totalXp, amount, out newLevel);

        public static int LevelFromXp(long totalXp) => LevelProgression.LevelForXp(totalXp);
        public static long XpIntoCurrentLevel(long totalXp) => LevelProgression.XpIntoCurrentLevel(totalXp);
        public static long XpToNextLevel(long totalXp) => LevelProgression.XpToNextLevel(totalXp);
        public static float LevelProgress(long totalXp) => LevelProgression.LevelProgress(totalXp);

        public static int ScoreForRun(float distance, int coins, float multiplier, int bonus)
        {
            int distanceScore = (int)Math.Floor(Math.Max(0f, distance) * 10f * Math.Max(1f, multiplier));
            int coinScore = Math.Max(0, coins) * 15;
            return Math.Max(0, distanceScore + coinScore + bonus);
        }

        public static int XpForRun(float distance, int score, int coins, float modeMultiplier)
        {
            int xp = (int)(distance * 0.6f + score * 0.02f + coins * 0.8f);
            return Math.Max(0, (int)(xp * Math.Max(1f, modeMultiplier)));
        }

        public static int CreditsForRun(int collectedCoins, float distance, float coinMultiplier)
        {
            int baseCredits = Math.Max(0, collectedCoins) + (int)(Math.Max(0f, distance) / 45f);
            return Math.Max(0, (int)Math.Round(baseCredits * Math.Max(0.5f, coinMultiplier)));
        }

        public static int UpgradeCost(string id, int nextLevel)
        {
            int baseCost = id switch
            {
                "magnet" => 600,
                "shield" => 750,
                "multiplier" => 800,
                "boost" => 700,
                "head_start" => 900,
                _ => 650
            };
            return baseCost + Math.Max(0, nextLevel - 1) * 500;
        }

        public static float UpgradeValue(string id, int level)
        {
            int safeLevel = Math.Max(0, Math.Min(MaxUpgradeLevel, level));
            return id switch
            {
                "magnet" => 1f + safeLevel * 0.16f,
                "shield" => 1f + safeLevel * 0.18f,
                "multiplier" => 1f + safeLevel * 0.15f,
                "boost" => 1f + safeLevel * 0.10f,
                "head_start" => safeLevel * 55f,
                _ => 1f + safeLevel * 0.1f
            };
        }

        public static int SeasonLevel(int seasonXp)
        {
            if (seasonXp < 0) return 1;
            return Math.Min(50, 1 + seasonXp / 1200);
        }
    }

    public static class DailyRewards
    {
        private static readonly int[] Credits = { 100, 150, 200, 300, 400, 500, 800 };

        public static int CreditsForStreak(int streak)
        {
            if (streak <= 0) return Credits[0];
            int index = (streak - 1) % Credits.Length;
            return Credits[index];
        }

        public static int StreakAfterClaim(string lastClaimUtc, string todayUtc, int currentStreak)
        {
            if (string.IsNullOrEmpty(lastClaimUtc)) return 1;
            if (!DateTime.TryParse(lastClaimUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTime last)) return 1;
            if (!DateTime.TryParse(todayUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTime today)) return 1;
            int days = (today.Date - last.Date).Days;
            if (days <= 0) return 0; // already claimed today
            if (days == 1) return Math.Min(7, Math.Max(1, currentStreak + 1));
            return 1;
        }

        public static bool CanClaim(string lastClaimUtc, string todayUtc)
        {
            if (string.IsNullOrEmpty(lastClaimUtc)) return true;
            if (!DateTime.TryParse(lastClaimUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTime last)) return true;
            if (!DateTime.TryParse(todayUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTime today)) return true;
            return today.Date > last.Date;
        }
    }

    /// <summary>Deterministic, platform-stable pseudo-random generator (xorshift32).</summary>
    public sealed class DeterministicRandom
    {
        private uint _state;

        public DeterministicRandom(int seed)
        {
            _state = (uint)seed;
            if (_state == 0) _state = 0x9E3779B9u;
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public float Next01() => (NextUInt() & 0xFFFFFF) / 16777216f;
        public int Range(int minInclusive, int maxExclusive) =>
            maxExclusive <= minInclusive ? minInclusive : minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        public float Range(float min, float max) => min + (max - min) * Next01();
        public bool Chance(float probability) => Next01() < probability;

        public static int StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619u;
                }
                return (int)(hash & 0x7FFFFFFF);
            }
        }
    }
}
