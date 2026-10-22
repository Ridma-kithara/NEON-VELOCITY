using System;
using System.Collections.Generic;
using System.Globalization;

namespace NeonVelocity.Core
{
    public static class MissionFactory
    {
        private sealed class Template
        {
            public string Key;
            public string Title;
            public string Description;
            public MissionMetric Metric;
            public int Target;
            public int Credits;
            public int Xp;
            public string Scope;
        }

        private static readonly Template[] Templates =
        {
            new Template { Key="dist_short", Title="CITY SPRINT", Description="Run {0} metres in a single session.", Metric=MissionMetric.Distance, Target=500, Credits=150, Xp=90, Scope="daily" },
            new Template { Key="dist_mid", Title="GRID RUNNER", Description="Cover {0} metres across all runs today.", Metric=MissionMetric.Distance, Target=1800, Credits=220, Xp=140, Scope="daily" },
            new Template { Key="coin_short", Title="CREDIT SWEEP", Description="Collect {0} energy credits.", Metric=MissionMetric.Coins, Target=80, Credits=140, Xp=80, Scope="daily" },
            new Template { Key="coin_mid", Title="POWER BANK", Description="Bank {0} energy credits.", Metric=MissionMetric.Coins, Target=200, Credits=240, Xp=150, Scope="daily" },
            new Template { Key="runs", Title="REBOOT REPEAT", Description="Start {0} runs.", Metric=MissionMetric.Runs, Target=3, Credits=120, Xp=80, Scope="daily" },
            new Template { Key="jumps", Title="AIR TIME", Description="Perform {0} jumps.", Metric=MissionMetric.Jumps, Target=30, Credits=130, Xp=80, Scope="daily" },
            new Template { Key="slides", Title="LOW PROFILE", Description="Perform {0} slides.", Metric=MissionMetric.Slides, Target=20, Credits=130, Xp=80, Scope="daily" },
            new Template { Key="powerups", Title="SYSTEM SHOCK", Description="Activate {0} power-ups.", Metric=MissionMetric.PowerUps, Target=6, Credits=180, Xp=100, Scope="daily" },
            new Template { Key="score", Title="SCORE SPIKE", Description="Reach a session score of {0}.", Metric=MissionMetric.Score, Target=5000, Credits=200, Xp=120, Scope="daily" },
            new Template { Key="nearmiss", Title="CLOSE CALL", Description="Record {0} near misses.", Metric=MissionMetric.NearMisses, Target=15, Credits=160, Xp=100, Scope="daily" },
            new Template { Key="weekly_distance", Title="LONG HAUL", Description="Run {0} metres this week.", Metric=MissionMetric.Distance, Target=8000, Credits=900, Xp=600, Scope="weekly" },
            new Template { Key="weekly_coins", Title="GOLDEN WEEK", Description="Collect {0} credits this week.", Metric=MissionMetric.Coins, Target=900, Credits=850, Xp=550, Scope="weekly" },
            new Template { Key="weekly_races", Title="CIRCUIT REGULAR", Description="Complete {0} online races this week.", Metric=MissionMetric.Races, Target=3, Credits=1000, Xp=700, Scope="weekly" },
            new Template { Key="weekly_score", Title="APEX SIGNAL", Description="Reach a weekly best score of {0}.", Metric=MissionMetric.Score, Target=25000, Credits=1100, Xp=750, Scope="weekly" },
            new Template { Key="weekly_powerups", Title="OVERCLOCKED", Description="Activate {0} power-ups this week.", Metric=MissionMetric.PowerUps, Target=30, Credits=800, Xp=500, Scope="weekly" },
        };

        public static List<MissionState> GenerateDaily(string dateKey)
        {
            return Generate(dateKey, "daily", 3, 1000 + DeterministicRandom.StableHash("daily:" + dateKey));
        }

        public static List<MissionState> GenerateWeekly(string dateKey)
        {
            return Generate(dateKey, "weekly", 2, 2000 + DeterministicRandom.StableHash("weekly:" + dateKey));
        }

        private static List<MissionState> Generate(string dateKey, string scope, int count, int seed)
        {
            var candidates = new List<Template>();
            foreach (var t in Templates) if (t.Scope == scope) candidates.Add(t);
            var rng = new DeterministicRandom(seed);
            var results = new List<MissionState>();
            int attempts = 0;
            while (results.Count < count && attempts++ < 80)
            {
                var template = candidates[rng.Range(0, candidates.Count)];
                string id = template.Key + ":" + scope + ":" + dateKey;
                if (results.Exists(m => m.id == id)) continue;
                int variance = scope == "daily" ? rng.Range(0, 3) : rng.Range(0, 3);
                int target = ScaleTarget(template.Target, variance);
                string description = string.Format(CultureInfo.InvariantCulture, template.Description, target);
                results.Add(new MissionState
                {
                    id = id,
                    title = template.Title,
                    description = description,
                    scope = scope,
                    metric = (int)template.Metric,
                    target = target,
                    progress = 0,
                    rewardCredits = template.Credits + variance * 15,
                    rewardXp = template.Xp + variance * 10,
                    claimed = false,
                    completed = false,
                    expiresDateKey = dateKey
                });
            }
            return results;
        }

        private static int ScaleTarget(int target, int variance)
        {
            if (variance == 0) return target;
            float factor = variance == 1 ? 1.2f : 1.45f;
            int scaled = (int)(target * factor);
            return ((scaled + 24) / 25) * 25;
        }

        public static string DailyKey(DateTime utc) => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public static string WeeklyKey(DateTime utc) =>
            $"{ISOWeek.GetYear(utc):0000}-W{ISOWeek.GetWeekOfYear(utc):00}";

        /// <summary>Achievement definitions keyed to lifetime statistics (auto-claimed).</summary>
        public static readonly AchievementDefinition[] Achievements =
        {
            new AchievementDefinition("first_run", "BOOT SEQUENCE", "Complete your first run.", MissionMetric.Runs, 1, 100),
            new AchievementDefinition("distance_1k", "KILOMETRE CLUB", "Accumulate 1,000 lifetime metres.", MissionMetric.Distance, 1000, 150),
            new AchievementDefinition("distance_10k", "GRID VETERAN", "Accumulate 10,000 lifetime metres.", MissionMetric.Distance, 10000, 400),
            new AchievementDefinition("distance_100k", "FRONTIER LEGEND", "Accumulate 100,000 lifetime metres.", MissionMetric.Distance, 100000, 1500),
            new AchievementDefinition("coins_1k", "CREDIT RUNNER", "Collect 1,000 lifetime credits.", MissionMetric.Coins, 1000, 300),
            new AchievementDefinition("runs_25", "CIRCUIT REGULAR", "Complete 25 runs.", MissionMetric.Runs, 25, 350),
            new AchievementDefinition("races_5", "PACK RUNNER", "Complete 5 online races.", MissionMetric.Races, 5, 500),
            new AchievementDefinition("jumps_500", "SKYLINE", "Perform 500 jumps.", MissionMetric.Jumps, 500, 300),
            new AchievementDefinition("powerups_100", "OVERCLOCKED", "Activate 100 power-ups.", MissionMetric.PowerUps, 100, 500)
        };
    }

    [Serializable]
    public class AchievementDefinition
    {
        public string id;
        public string title;
        public string description;
        public MissionMetric metric;
        public int target;
        public int credits;

        public AchievementDefinition(string id, string title, string description, MissionMetric metric, int target, int credits)
        {
            this.id = id;
            this.title = title;
            this.description = description;
            this.metric = metric;
            this.target = target;
            this.credits = credits;
        }

        public int LifetimeProgress(LifetimeStats stats) => metric switch
        {
            MissionMetric.Distance => (int)stats.totalDistance,
            MissionMetric.Coins => stats.totalCoins,
            MissionMetric.Runs => stats.totalRuns,
            MissionMetric.Jumps => stats.totalJumps,
            MissionMetric.Slides => stats.totalSlides,
            MissionMetric.PowerUps => stats.totalPowerUps,
            MissionMetric.Races => stats.racesCompleted,
            MissionMetric.Score => stats.totalRuns,
            MissionMetric.NearMisses => stats.nearMisses,
            _ => 0
        };
    }
}
