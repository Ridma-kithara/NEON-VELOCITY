using System;
using System.Collections.Generic;

namespace NeonVelocity.Core
{
    public enum GameModeId
    {
        Classic,
        OnlineRace,
        PrivateRace,
        Practice,
        TimeTrial,
        DailyChallenge,
        WeeklyChallenge,
        CoinRush,
        Survival,
        SeasonalEvent,
        Tutorial,
        Mission,
        Distance,
        BossChase,
        RouteExploration
    }

    public sealed class ModeSpec
    {
        public GameModeId Id;
        public string Title;
        public string CodeName;
        public string Tagline;
        public string Objective;
        public string Rules;
        public bool IsOnline;
        public bool IsPrivateRoom;
        public bool CountsProgression = true;
        public bool AllowsCrash = true;
        public bool AllowsBots;
        public int MaxPlayers = 1;
        public float DurationSeconds;
        public float SpeedMultiplier = 1f;
        public float CoinMultiplier = 1f;
        public float ScoreMultiplier = 1f;
        public int StartingLives = 1;
        public int SeedSalt;
        public int RecommendedLevel;
        public string AccentHex;
        /// <summary>Distance that completes the mode, or 0 for an endless run.</summary>
        public float TargetDistance;

        public bool IsTimed => DurationSeconds > 0.01f;
        public bool HasTargetDistance => TargetDistance > 0.01f;
        public bool HasPowerUps => Id != GameModeId.Tutorial;
        public bool IsBossChase => Id == GameModeId.BossChase;
        public bool IsRouteMode => Id == GameModeId.RouteExploration;
    }

    /// <summary>Single source of truth for every playable mode's rules.</summary>
    public static class GameModes
    {
        private static readonly List<ModeSpec> Specs = new List<ModeSpec>
        {
            new ModeSpec
            {
                Id = GameModeId.Classic, Title = "CLASSIC RUN", CodeName = "ENDLESS",
                Tagline = "Outrun the grid. Chase your record.",
                Objective = "Run as far as possible without crashing.",
                Rules = "Speed rises with distance and time. One collision ends the run. Power-ups are active.",
                MaxPlayers = 1, DurationSeconds = 0f, AccentHex = "#28F6FF", AllowsCrash = true
            },
            new ModeSpec
            {
                Id = GameModeId.OnlineRace, Title = "NEON CIRCUIT", CodeName = "5P ONLINE",
                Tagline = "Five runners. One shared circuit. No second chances.",
                Objective = "Finish ahead of four rivals on a shared 120-second circuit.",
                Rules = "All runners share a deterministic track and start signal. A crash ends your run. Server ranks results.",
                IsOnline = true, MaxPlayers = 5, AllowsBots = true, DurationSeconds = 120f,
                SpeedMultiplier = 1.08f, AccentHex = "#B6FF3B", CountsProgression = true
            },
            new ModeSpec
            {
                Id = GameModeId.PrivateRace, Title = "PRIVATE CIRCUIT", CodeName = "FRIENDS",
                Tagline = "Assemble your crew. Own the room code.",
                Objective = "Beat your friends across the same neon circuit.",
                Rules = "Up to five runners share a private room code, a server countdown and a deterministic track.",
                IsOnline = true, IsPrivateRoom = true, MaxPlayers = 5, AllowsBots = true,
                DurationSeconds = 120f, SpeedMultiplier = 1.08f, AccentHex = "#FF2E9A"
            },
            new ModeSpec
            {
                Id = GameModeId.Practice, Title = "PRACTICE LOOP", CodeName = "OFFLINE",
                Tagline = "Learn the line. No pressure.",
                Objective = "Train lane changes, jumps and slides without losing a run.",
                Rules = "Slower acceleration, automatic crash recovery, no competitive rewards. Works fully offline.",
                MaxPlayers = 1, DurationSeconds = 0f, SpeedMultiplier = 0.82f,
                CountsProgression = false, AllowsCrash = false, AccentHex = "#7C93B8"
            },
            new ModeSpec
            {
                Id = GameModeId.TimeTrial, Title = "TIME TRIAL", CodeName = "60 SECONDS",
                Tagline = "One minute. Maximum distance.",
                Objective = "Cover the greatest distance in 60 seconds.",
                Rules = "The timer is fixed. Collect credits and chain the score multiplier. Results are local.",
                MaxPlayers = 1, DurationSeconds = 60f, SpeedMultiplier = 1.14f, AccentHex = "#FFD166"
            },
            new ModeSpec
            {
                Id = GameModeId.DailyChallenge, Title = "DAILY SIGNAL", CodeName = "NEW EVERY DAY",
                Tagline = "The grid resets at midnight.",
                Objective = "Clear 600 metres on today's deterministic track.",
                Rules = "Seeded by UTC date. Same layout for every runner. First completion pays a daily bonus.",
                MaxPlayers = 1, DurationSeconds = 0f, SpeedMultiplier = 1.02f, SeedSalt = 1009,
                AccentHex = "#28F6FF", TargetDistance = 600f
            },
            new ModeSpec
            {
                Id = GameModeId.WeeklyChallenge, Title = "WEEKLY PROTOCOL", CodeName = "SEVEN-DAY OP",
                Tagline = "One long haul. One shared seed.",
                Objective = "Reach 1,500 metres on this week's track.",
                Rules = "Seeded by ISO week. Higher density patterns and an increased credit payout.",
                MaxPlayers = 1, DurationSeconds = 0f, SpeedMultiplier = 1.06f, CoinMultiplier = 1.5f,
                SeedSalt = 2003, AccentHex = "#A78BFA", TargetDistance = 1500f
            },
            new ModeSpec
            {
                Id = GameModeId.CoinRush, Title = "COIN RUSH", CodeName = "90 SECONDS",
                Tagline = "Every lane is a payline.",
                Objective = "Bank as many energy credits as possible in 90 seconds.",
                Rules = "Credit density is tripled. Magnet pickups are common. Score still tracks distance.",
                MaxPlayers = 1, DurationSeconds = 90f, CoinMultiplier = 3f,
                ScoreMultiplier = 0.6f, AccentHex = "#FFD166"
            },
            new ModeSpec
            {
                Id = GameModeId.Survival, Title = "SURVIVAL SHIFT", CodeName = "THREE LIVES",
                Tagline = "The grid is hungry tonight.",
                Objective = "Survive as long as possible with three lives.",
                Rules = "Collisions consume a life instead of ending the run immediately. Speed ramps faster.",
                MaxPlayers = 1, DurationSeconds = 0f, SpeedMultiplier = 1.18f,
                StartingLives = 3, AccentHex = "#FF3B5C"
            },
            new ModeSpec
            {
                Id = GameModeId.SeasonalEvent, Title = "MONSOON OVERDRIVE", CodeName = "SEASON 01",
                Tagline = "The city is flooded with light.",
                Objective = "Reach 1,000 metres during the Neon Monsoon event.",
                Rules = "Rain-soaked city visuals, double season XP and a boosted credit multiplier.",
                MaxPlayers = 1, DurationSeconds = 0f, SpeedMultiplier = 1.04f,
                CoinMultiplier = 1.5f, AccentHex = "#28F6FF", TargetDistance = 1000f
            },
            new ModeSpec
            {
                Id = GameModeId.Tutorial, Title = "FIRST CONTACT", CodeName = "TUTORIAL",
                Tagline = "Three moves. One endless city.",
                Objective = "Complete the guided 300-metre training line.",
                Rules = "Live prompts teach lane switching, jumping and sliding. No crashes during training.",
                MaxPlayers = 1, DurationSeconds = 0f, SpeedMultiplier = 0.62f,
                CountsProgression = false, AllowsCrash = false, AccentHex = "#B6FF3B", TargetDistance = 300f
            },
            new ModeSpec
            {
                Id = GameModeId.Mission, Title = "CONTRACT RUN", CodeName = "MISSION",
                Tagline = "Three clauses. One delivery.",
                Objective = "Complete a 1,200-metre contract while surviving two hits.",
                Rules = "A structured contract: one target distance, two lives and a 25% credit bonus. No server account required.",
                MaxPlayers = 1, DurationSeconds = 0f, SpeedMultiplier = 1.04f, CoinMultiplier = 1.25f,
                StartingLives = 2, SeedSalt = 3001, AccentHex = "#FFD166", TargetDistance = 1200f
            },
            new ModeSpec
            {
                Id = GameModeId.Distance, Title = "DISTANCE DRIVE", CodeName = "2 KM",
                Tagline = "Two kilometres. No excuses.",
                Objective = "Reach exactly 2,000 metres before your first crash.",
                Rules = "One life, higher speed and a 25% score bonus. The run completes the instant you cross 2 km.",
                MaxPlayers = 1, DurationSeconds = 0f, SpeedMultiplier = 1.10f, ScoreMultiplier = 1.25f,
                SeedSalt = 4001, AccentHex = "#7CFFB2", TargetDistance = 2000f
            },
            new ModeSpec
            {
                Id = GameModeId.BossChase, Title = "BOSS CHASE", CodeName = "WARDEN PURSUIT",
                Tagline = "It runs ahead. Close the gap.",
                Objective = "Catch the warden mech within 1,500 metres.",
                Rules = "A warden frame weaves ahead of the run. Two lives, faster acceleration and no safe opening after the first kilometre.",
                MaxPlayers = 1, DurationSeconds = 0f, SpeedMultiplier = 1.16f, StartingLives = 2,
                SeedSalt = 5003, AccentHex = "#FF3B5C", TargetDistance = 1500f
            },
            new ModeSpec
            {
                Id = GameModeId.RouteExploration, Title = "ROUTE SURVEY", CodeName = "JUNCTION HUNT",
                Tagline = "Every fork is a discovery.",
                Objective = "Survey 2,500 metres and commit through as many junctions as possible.",
                Rules = "Two lives, 40% extra credits and a route score bonus for every junction branch taken.",
                MaxPlayers = 1, DurationSeconds = 0f, SpeedMultiplier = 1.02f, CoinMultiplier = 1.4f,
                StartingLives = 2, SeedSalt = 6007, AccentHex = "#A78BFA", TargetDistance = 2500f
            }
        };

        public static IReadOnlyList<ModeSpec> All => Specs;

        public static ModeSpec Get(GameModeId id)
        {
            for (int i = 0; i < Specs.Count; i++) if (Specs[i].Id == id) return Specs[i];
            return Specs[0];
        }

        public static ModeSpec Get(string id)
        {
            if (Enum.TryParse(id, true, out GameModeId parsed)) return Get(parsed);
            return Specs[0];
        }

        public static bool IsOnline(GameModeId id) => id == GameModeId.OnlineRace || id == GameModeId.PrivateRace;
    }
}
