using System;
using System.Collections.Generic;

namespace NeonVelocity.Core
{
    [Serializable]
    public class SaveData
    {
        public const int CurrentSchemaVersion = 3;

        public int schemaVersion = CurrentSchemaVersion;
        public string playerId = "";
        public string playerName = "RUNNER-01";
        public int credits = 500;
        public int xp = 0;
        public int level = 1;
        public string selectedCharacter = "kira";
        public List<string> unlockedCharacters = new List<string> { "kira" };
        public List<string> unlockedTrails = new List<string> { "spark" };
        public string selectedTrail = "spark";
        public List<UpgradeEntry> upgrades = new List<UpgradeEntry>();
        public float bestDistance = 0f;
        public int bestScore = 0;
        public string bestModeId = "classic";
        public int totalCoins = 0;
        public string lastPlayedUtc = "";
        public string lastDailyClaimUtc = "";
        public int dailyStreak = 0;
        public List<string> redeemedRewardIds = new List<string>();
        public LifetimeStats lifetime = new LifetimeStats();
        public SettingsData settings = new SettingsData();
        public MissionSave missions = new MissionSave();
        public List<ModeBest> modeBests = new List<ModeBest>();
        public List<LeaderboardEntryData> localLeaderboard = new List<LeaderboardEntryData>();
        public bool tutorialCompleted = false;
        public bool hasSeenWelcome = false;
        public int seasonXp = 0;
        public int seasonLevel = 1;

        public UpgradeEntry FindUpgrade(string id)
        {
            if (upgrades == null) upgrades = new List<UpgradeEntry>();
            for (int i = 0; i < upgrades.Count; i++)
                if (upgrades[i].id == id) return upgrades[i];
            return null;
        }

        public int GetUpgradeLevel(string id)
        {
            var entry = FindUpgrade(id);
            return entry == null ? 0 : entry.level;
        }

        public ModeBest FindModeBest(string modeId)
        {
            if (modeBests == null) modeBests = new List<ModeBest>();
            for (int i = 0; i < modeBests.Count; i++)
                if (modeBests[i].modeId == modeId) return modeBests[i];
            return null;
        }

        public void MarkRedeemed(string id, int keepAtMost = 100)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (redeemedRewardIds == null) redeemedRewardIds = new List<string>();
            if (redeemedRewardIds.Contains(id)) return;
            redeemedRewardIds.Add(id);
            while (redeemedRewardIds.Count > keepAtMost) redeemedRewardIds.RemoveAt(0);
        }

        public bool IsRedeemed(string id) => redeemedRewardIds != null && redeemedRewardIds.Contains(id);
    }

    [Serializable]
    public class UpgradeEntry
    {
        public string id;
        public int level;
    }

    [Serializable]
    public class ModeBest
    {
        public string modeId;
        public float distance;
        public int score;
        public int coins;
        public string achievedUtc;
    }

    [Serializable]
    public class LifetimeStats
    {
        public int totalRuns;
        public float totalDistance;
        public int totalCoins;
        public int totalJumps;
        public int totalSlides;
        public int totalPowerUps;
        public int totalDeaths;
        public int racesCompleted;
        public int racesWon;
        public int bestRaceRank;
        public int missionsClaimed;
        public int dailyRewardsClaimed;
        public int nearMisses;
    }

    [Serializable]
    public class SettingsData
    {
        public float musicVolume = 0.62f;
        public float sfxVolume = 0.8f;
        public float ambienceVolume = 0.5f;
        public float swipeSensitivity = 1f;
        public int graphicsPreset = 2; // 0 Low, 1 Medium, 2 High, 3 Ultra
        public int targetFrameRate = 60;
        public bool cameraShake = true;
        public bool reducedMotion = false;
        public bool vibration = true;
        public bool highContrast = false;
        public string serverHost = "127.0.0.1";
        public int serverPort = 7777;
        public string preferredLanguage = "en";
    }

    [Serializable]
    public class MissionSave
    {
        public string dailyDateKey = "";
        public string weeklyDateKey = "";
        public List<MissionState> missions = new List<MissionState>();
    }

    [Serializable]
    public class MissionState
    {
        public string id;
        public string title;
        public string description;
        public string scope = "daily"; // daily, weekly, achievement
        public int metric;
        public int target;
        public int progress;
        public int rewardCredits;
        public int rewardXp;
        public bool claimed;
        public bool completed;
        public string expiresDateKey;
    }

    [Serializable]
    public class LeaderboardEntryData
    {
        public string playerName;
        public float distance;
        public int score;
        public int rank;
        public string modeId;
        public bool verified;
        public string achievedUtc;
    }

    public enum MissionMetric
    {
        Distance = 0,
        Coins = 1,
        Runs = 2,
        Jumps = 3,
        Slides = 4,
        PowerUps = 5,
        Races = 6,
        Score = 7,
        NearMisses = 8
    }

    /// <summary>Migration is deliberately explicit and tested; never assume defaults survive.</summary>
    public static class SaveMigration
    {
        public static void Normalise(SaveData data)
        {
            if (data == null) return;
            if (data.unlockedCharacters == null) data.unlockedCharacters = new List<string>();
            if (data.unlockedTrails == null) data.unlockedTrails = new List<string>();
            if (data.upgrades == null) data.upgrades = new List<UpgradeEntry>();
            if (data.redeemedRewardIds == null) data.redeemedRewardIds = new List<string>();
            if (data.modeBests == null) data.modeBests = new List<ModeBest>();
            if (data.localLeaderboard == null) data.localLeaderboard = new List<LeaderboardEntryData>();
            if (data.lifetime == null) data.lifetime = new LifetimeStats();
            if (data.settings == null) data.settings = new SettingsData();
            if (data.missions == null) data.missions = new MissionSave();
            if (data.missions.missions == null) data.missions.missions = new List<MissionState>();
            if (string.IsNullOrEmpty(data.playerName)) data.playerName = "RUNNER-01";
            if (data.playerName.Length > 20) data.playerName = data.playerName.Substring(0, 20);
            if (string.IsNullOrEmpty(data.selectedCharacter)) data.selectedCharacter = "kira";
            if (!data.unlockedCharacters.Contains("kira")) data.unlockedCharacters.Add("kira");
            if (!data.unlockedCharacters.Contains(data.selectedCharacter))
                data.selectedCharacter = "kira";
            if (!data.unlockedTrails.Contains("spark")) data.unlockedTrails.Add("spark");
            if (!data.unlockedTrails.Contains(data.selectedTrail)) data.selectedTrail = "spark";
            if (data.credits < 0) data.credits = 0;
            if (data.xp < 0) data.xp = 0;
            if (data.level < 1) data.level = 1;
            if (data.dailyStreak < 0 || data.dailyStreak > 7) data.dailyStreak = 0;
            if (data.bestDistance < 0f) data.bestDistance = 0f;
            if (data.seasonXp < 0) data.seasonXp = 0;
            if (data.seasonLevel < 1) data.seasonLevel = 1;
            ClampSettings(data.settings);

            while (data.redeemedRewardIds.Count > 100) data.redeemedRewardIds.RemoveAt(0);
            for (int i = 0; i < data.upgrades.Count; i++)
            {
                if (data.upgrades[i] == null) continue;
                data.upgrades[i].level = Math.Max(0, Math.Min(5, data.upgrades[i].level));
            }
        }

        public static void ClampSettings(SettingsData s)
        {
            s.musicVolume = Clamp01(s.musicVolume);
            s.sfxVolume = Clamp01(s.sfxVolume);
            s.ambienceVolume = Clamp01(s.ambienceVolume);
            s.swipeSensitivity = Math.Max(0.4f, Math.Min(2.5f, s.swipeSensitivity));
            s.graphicsPreset = Math.Max(0, Math.Min(3, s.graphicsPreset));
            s.targetFrameRate = s.targetFrameRate == 30 ? 30 : (s.targetFrameRate == 144 ? 144 : 60);
            if (s.serverPort < 1 || s.serverPort > 65535) s.serverPort = 7777;
            if (string.IsNullOrWhiteSpace(s.serverHost)) s.serverHost = "127.0.0.1";
        }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
