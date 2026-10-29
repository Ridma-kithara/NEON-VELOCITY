using System;

namespace NeonVelocity.Core
{
    /// <summary>
    /// Authoritative, total-XP driven progression. Levels 1-10 use the authored table
    /// (0, 450, 900, 1,800, 3,000, 4,500, 6,500, 9,000, 12,000, 16,000) and every level
    /// above 10 costs 1.12x the previous requirement. XP requirements are capped at
    /// 1,000,000 so a very long-lived profile can never overflow or demand an absurd grind.
    ///
    /// The level number is always derived from total XP. Levels are effectively unlimited:
    /// the arithmetic supports one million levels while staying inside Int64.
    /// </summary>
    public static class LevelProgression
    {
        /// <summary>Authored thresholds. Index 0 is level 1, index 9 is level 10.</summary>
        public static readonly long[] Thresholds =
        {
            0L, 450L, 900L, 1_800L, 3_000L, 4_500L, 6_500L, 9_000L, 12_000L, 16_000L
        };

        public const int TableMaxLevel = 10;
        public const int MaxLevel = 1_000_000;
        public const int MilestoneInterval = 5;
        public const long RequirementCap = 1_000_000L;

        private const double Growth = 1.12;
        private const double BaseRequirement = 4_000.0;

        /// <summary>Highest XP the game will accept; keeps every derived value inside long.</summary>
        public static long MaxTotalXp => MaxTotal;

        // Computed once from the closed form. TotalXpForLevel clamps to this value, so it must
        // never call back into MaxTotalXp (that recursion overflows the stack at runtime).
        private static readonly long MaxTotal = ComputeTotalXpForLevel(MaxLevel);

        /// <summary>
        /// XP required to advance from <paramref name="level"/> to level + 1.
        /// Level 10 -> 11 costs 4,480 by design (the table's last step is 4,000).
        /// </summary>
        public static long RequirementForLevel(int level)
        {
            if (level < 1) return 0L;
            if (level < TableMaxLevel)
                return Thresholds[level] - Thresholds[level - 1];
            if (level < TableMaxLevel + 1)
                return 4_480L;
            // Level N >= 11: 4,000 * 1.12^(N-9), i.e. 4,480 for N=10, 5,018 for N=11...
            double raw = BaseRequirement * Math.Pow(Growth, level - 9);
            if (double.IsInfinity(raw) || double.IsNaN(raw) || raw >= RequirementCap) return RequirementCap;
            return Math.Max(1L, (long)Math.Round(raw));
        }

        /// <summary>Exact cumulative XP required to reach <paramref name="level"/>.</summary>
        public static long TotalXpForLevel(int level)
        {
            return Math.Min(ComputeTotalXpForLevel(level), MaxTotal);
        }

        private static long ComputeTotalXpForLevel(int level)
        {
            if (level <= 1) return 0L;
            if (level > MaxLevel) level = MaxLevel;
            if (level <= TableMaxLevel) return Thresholds[level - 1];

            // Number of growth terms from level 10 up to (but not including) level.
            // Requirement(10) = 4,480, requirement(11) = 5,018, ... until the cap.
            int cappedFrom = CapLevel();
            long total = Thresholds[TableMaxLevel - 1];

            if (level - 1 <= cappedFrom)
            {
                double terms = level - 10; // requirements 10..level-1
                double sum = 4_480.0 * (Math.Pow(Growth, terms) - 1.0) / (Growth - 1.0);
                total += (long)Math.Round(sum);
            }
            else
            {
                double terms = cappedFrom - 9; // 10..cappedFrom inclusive
                double sum = 4_480.0 * (Math.Pow(Growth, terms) - 1.0) / (Growth - 1.0);
                total += (long)Math.Round(sum);
                total += (long)(level - 1 - cappedFrom) * RequirementCap;
            }

            return total;
        }

        private static int CapLevel()
        {
            // Smallest level whose requirement reaches the cap (58 for the current tuning).
            double raw = Math.Log(RequirementCap / BaseRequirement) / Math.Log(Growth) + 9.0;
            return Math.Max(TableMaxLevel + 1, (int)Math.Ceiling(raw));
        }

        /// <summary>Total XP is authoritative; level is always derived from it.</summary>
        public static int LevelForXp(long totalXp)
        {
            if (totalXp <= 0L) return 1;
            if (totalXp >= MaxTotalXp) return MaxLevel;
            int low = 1;
            int high = MaxLevel;
            while (low < high)
            {
                int mid = low + (high - low + 1) / 2;
                if (TotalXpForLevel(mid) <= totalXp) low = mid;
                else high = mid - 1;
            }
            return Math.Max(1, low);
        }

        public static long TotalXpForCurrentLevel(long totalXp)
        {
            return TotalXpForLevel(LevelForXp(totalXp));
        }

        public static long XpIntoCurrentLevel(long totalXp)
        {
            long into = Math.Max(0L, totalXp) - TotalXpForCurrentLevel(totalXp);
            return Math.Max(0L, into);
        }

        public static long XpToNextLevel(long totalXp)
        {
            int level = LevelForXp(totalXp);
            if (level >= MaxLevel) return 0L;
            return Math.Max(0L, TotalXpForLevel(level + 1) - Math.Max(0L, totalXp));
        }

        public static float LevelProgress(long totalXp)
        {
            int level = LevelForXp(totalXp);
            if (level >= MaxLevel) return 1f;
            long start = TotalXpForLevel(level);
            long next = TotalXpForLevel(level + 1);
            long span = Math.Max(1L, next - start);
            return Math.Max(0f, Math.Min(1f, (Math.Max(0L, totalXp) - start) / (float)span));
        }

        /// <summary>Clamps impossible/negative totals and returns the safe store value.</summary>
        public static long SanitiseXp(long totalXp)
        {
            if (totalXp < 0L) return 0L;
            return Math.Min(totalXp, MaxTotalXp);
        }

        /// <summary>Adds XP without overflowing and returns the number of levels gained.</summary>
        public static int AddXp(ref long totalXp, long amount, out int newLevel)
        {
            long before = SanitiseXp(totalXp);
            int beforeLevel = LevelForXp(before);
            if (amount < 0L) amount = 0L;
            long sum = before;
            if (amount > MaxTotalXp - before) sum = MaxTotalXp;
            else sum = before + amount;
            totalXp = sum;
            newLevel = LevelForXp(sum);
            return Math.Max(0, newLevel - beforeLevel);
        }

        /// <summary>Is this a milestone level (every 5 levels) that should grant a reward?</summary>
        public static bool IsMilestone(int level) => level > 0 && level % MilestoneInterval == 0;

        /// <summary>
        /// Migrates a pre-v4 profile. Old saves stored linear-curve total XP; preserve the
        /// player's level and fraction rather than dropping or inflating their progress.
        /// </summary>
        public static long FromLegacyXp(long legacyXp, int legacyLevel)
        {
            if (legacyXp <= 0L) return 0L;
            int safeLevel = Math.Max(1, Math.Min(100, legacyLevel));
            int beforeLevel = 1;
            long remaining = legacyXp;
            while (beforeLevel < 100 && remaining >= 450L + Math.Max(0, beforeLevel - 1) * 220L)
            {
                remaining -= 450L + Math.Max(0, beforeLevel - 1) * 220L;
                beforeLevel++;
            }
            safeLevel = Math.Max(safeLevel, beforeLevel);
            long oldRequirement = 450L + Math.Max(0, beforeLevel - 1) * 220L;
            double fraction = oldRequirement <= 0L ? 0.0 : Math.Max(0.0, Math.Min(1.0, remaining / (double)oldRequirement));
            long newStart = TotalXpForLevel(safeLevel);
            long newRequirement = RequirementForLevel(safeLevel);
            return SanitiseXp(newStart + (long)Math.Round(newRequirement * fraction));
        }

        /// <summary>Reward table for milestone levels. Credits and cosmetics only, never GEMS.</summary>
        public static MilestoneReward MilestoneFor(int level)
        {
            if (level <= 0 || !IsMilestone(level)) return MilestoneReward.None;
            int credits = 250 + level * 60;
            string cosmetic = level switch
            {
                10 => "title:GRID VETERAN",
                20 => "trail:chrome",
                30 => "aura:aurora",
                50 => "title:APEX LEGEND",
                _ => ""
            };
            string label = level switch
            {
                10 => "GRID VETERAN TITLE",
                20 => "CHROME WAKE TRAIL",
                30 => "AURORA AURA",
                50 => "APEX LEGEND TITLE",
                _ => "SUPPLY CACHE"
            };
            return new MilestoneReward
            {
                Level = level,
                Credits = credits,
                UnlockId = cosmetic,
                Label = label
            };
        }
    }

    public struct MilestoneReward
    {
        public int Level;
        public int Credits;
        public string UnlockId;
        public string Label;

        public static MilestoneReward None => new MilestoneReward { Level = 0, Credits = 0, UnlockId = "", Label = "" };
        public bool IsValid => Level > 0;
    }
}
