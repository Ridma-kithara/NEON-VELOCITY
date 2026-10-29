using System;
using System.Collections.Generic;

namespace NeonVelocity.Core
{
    [Serializable]
    public class SaveData
    {
        public const int CurrentSchemaVersion = 5;

        public int schemaVersion = CurrentSchemaVersion;
        public string playerId = "";
        public string playerName = "RUNNER-01";
        public int credits = 500;
        /// <summary>Legacy pre-v4 total XP. Read only for migration; never written after v4.</summary>
        public int xp = 0;
        /// <summary>Authoritative total XP. Level is always derived from this value.</summary>
        public long totalXp = 0;
        /// <summary>Derived cache of <see cref="LevelProgression.LevelForXp"/>.</summary>
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
        /// <summary>GameModeId of the most recent offline session; powers the CONTINUE shortcut.</summary>
        public string lastModeId = "";

        // ---- World exploration ---------------------------------------------------------
        public List<string> discoveredLocations = new List<string>();
        public int furthestLocationIndex = -1;
        public List<LocationRecord> locationRecords = new List<LocationRecord>();

        // ---- Premium (server-authoritative; cached values are display-only) ------------
        public PremiumCache premium = new PremiumCache();
        // ---- Account identity and cloud sync (never stores GEMS authority) --------------
        public AccountCache account = new AccountCache();
        public string selectedSkin = "";
        public string selectedArmor = "";
        public string selectedHelmet = "";
        public string selectedEffect = "";
        public string selectedMovementEffect = "";
        public string selectedAnimation = "";
        public string selectedPremiumAbility = "";

        public UpgradeEntry FindUpgrade(string id)
        {
            if (upgrades == null) upgrades = new List<UpgradeEntry>();
            for (int i = 0; i < upgrades.Count; i++)
                if (upgrades[i] != null && upgrades[i].id == id) return upgrades[i];
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

        public LocationRecord FindLocationRecord(string locationId)
        {
            if (locationRecords == null) locationRecords = new List<LocationRecord>();
            for (int i = 0; i < locationRecords.Count; i++)
                if (locationRecords[i].locationId == locationId) return locationRecords[i];
            return null;
        }

        public bool HasDiscoveredLocation(string locationId) =>
            discoveredLocations != null && discoveredLocations.Contains(locationId);

        public PremiumOwnership FindPremiumOwnership(string productId)
        {
            if (premium == null) premium = new PremiumCache();
            if (premium.ownedItems == null) premium.ownedItems = new List<PremiumOwnership>();
            for (int i = 0; i < premium.ownedItems.Count; i++)
                if (premium.ownedItems[i].productId == productId) return premium.ownedItems[i];
            return null;
        }

        public int PremiumUpgradeLevel(string upgradeId)
        {
            if (premium == null) premium = new PremiumCache();
            if (premium.upgrades == null) premium.upgrades = new List<PremiumUpgradeEntry>();
            for (int i = 0; i < premium.upgrades.Count; i++)
                if (premium.upgrades[i].upgradeId == upgradeId) return premium.upgrades[i].level;
            return 0;
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
    public class LocationRecord
    {
        public string locationId;
        public int zoneIndex;
        public float bestDistance;
        public int visits;
        public string firstSeenUtc;
        public string lastSeenUtc;
    }

    [Serializable]
    public class PremiumCache
    {
        public string accountId = "";
        public string deviceKey = "";
        public string token = "";
        public string tokenIssuedUtc = "";
        public int gems = 0;
        public int pendingOrders = 0;
        public string lastSyncUtc = "";
        public string lastCatalogUtc = "";
        public string catalogJson = "";
        public string lastOrderId = "";
        public string lastOrderUtc = "";
        public List<PremiumOwnership> ownedItems = new List<PremiumOwnership>();
        public List<string> characters = new List<string>();
        public string selectedPremiumAbility = "";
        public List<PremiumUpgradeEntry> upgrades = new List<PremiumUpgradeEntry>();
        /// <summary>True when the cached token belongs to a registered account, not a guest.</summary>
        public bool persistentSession = false;
    }

    /// <summary>
    /// Non-authoritative account metadata for the UI and cloud-sync cursor. GEMS, entitlements
    /// and bearer tokens are never trusted from this block; they remain in PremiumCache and
    /// are always reconciled with the server.
    /// </summary>
    [Serializable]
    public class AccountCache
    {
        public string accountId = "";
        public string email = "";
        public string phone = "";
        public string provider = "";
        public string receiptEmail = "";
        public bool emailVerified = false;
        public bool phoneVerified = false;
        public bool signedIn = false;
        public int syncRevision = 0;
        public string lastSyncUtc = "";
        public string lastSyncSummary = "";
        public string lastSyncError = "";
    }

    [Serializable]
    public class PremiumOwnership
    {
        public string productId;
        public string category;
        public int level;
        public string acquiredUtc;
    }

    [Serializable]
    public class PremiumUpgradeEntry
    {
        public string upgradeId;
        public int level;
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
        public int levelsGained;
        public int locationsDiscovered;
        public int premiumPurchases;
    }

    [Serializable]
    public class SettingsData
    {
        public float masterVolume = 1f;
        public float musicVolume = 0.62f;
        public float sfxVolume = 0.8f;
        public float ambienceVolume = 0.5f;
        public float swipeSensitivity = 1f;
        public int graphicsPreset = 2; // 0 Low, 1 Medium, 2 High, 3 Ultra, 4 Custom
        public int targetFrameRate = 60;
        // Desktop display configuration. Width/height of 0 means "leave the current desktop
        // mode untouched"; mobile builds ignore these fields entirely.
        public int screenWidth;
        public int screenHeight;
        public int fullscreenMode; // 0 windowed, 1 borderless, 2 exclusive where supported
        public bool vSync;
        public float renderScale = 1f;
        public int textureQuality; // 0 full, 1 half, 2 quarter (mipmap limit)
        public int shadowQuality = 2; // 0 off, 1 low, 2 high, 3 ultra
        public int reflectionQuality = 2; // 0 off, 1 standard, 2 high
        public int antiAliasing = 2; // 0 off, 2x, 4x, 8x
        public int effectsQuality = 2; // 0 low, 1 medium, 2 high
        public int viewDistance = 2; // 0 near, 1 standard, 2 far
        public bool cameraShake = true;
        public bool reducedMotion = false;
        public bool vibration = true;
        public bool highContrast = false;
        public string serverHost = "127.0.0.1";
        public int serverPort = 7777;
        public string preferredLanguage = "en";
        /// <summary>HTTPS endpoint of the premium GEMS service. Configure this for production.</summary>
        public string premiumServerUrl = "http://127.0.0.1:8088";

        // ---- Time of day -------------------------------------------------------------
        /// <summary>0 authored per location, 1 dynamic cycle, 2 fixed phase.</summary>
        public int timeOfDayMode = 0;
        /// <summary>Fixed phase used when <see cref="timeOfDayMode"/> is 2 (0..6).</summary>
        public int timeOfDayPhase = 2;
        /// <summary>Metres for one complete seven-state cycle in dynamic mode.</summary>
        public float timeOfDayCycleMetres = 21_000f;
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
        NearMisses = 8,
        Locations = 9
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
            if (data.discoveredLocations == null) data.discoveredLocations = new List<string>();
            if (data.locationRecords == null) data.locationRecords = new List<LocationRecord>();
            if (data.premium == null) data.premium = new PremiumCache();
            if (data.account == null) data.account = new AccountCache();
            if (data.account.accountId == null) data.account.accountId = "";
            if (data.account.email == null) data.account.email = "";
            if (data.account.phone == null) data.account.phone = "";
            if (data.account.provider == null) data.account.provider = "";
            if (data.account.receiptEmail == null) data.account.receiptEmail = "";
            if (data.account.lastSyncUtc == null) data.account.lastSyncUtc = "";
            if (data.account.lastSyncSummary == null) data.account.lastSyncSummary = "";
            if (data.account.lastSyncError == null) data.account.lastSyncError = "";
            if (data.account.syncRevision < 0) data.account.syncRevision = 0;

            // v3 and older stored total XP as an int on the old linear curve. Convert the
            // player's level and fraction into the authored total-XP curve exactly once.
            if (data.schemaVersion < 4 || (data.totalXp <= 0L && data.xp > 0))
            {
                long legacy = Math.Max(0, data.xp);
                if (legacy > 0L)
                    data.totalXp = LevelProgression.FromLegacyXp(legacy, data.level);
                data.xp = 0;
            }
            data.totalXp = LevelProgression.SanitiseXp(data.totalXp);
            data.level = LevelProgression.LevelForXp(data.totalXp);

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
            if (data.dailyStreak < 0 || data.dailyStreak > 7) data.dailyStreak = 0;
            if (data.bestDistance < 0f) data.bestDistance = 0f;
            if (data.seasonXp < 0) data.seasonXp = 0;
            if (data.seasonLevel < 1) data.seasonLevel = 1;
            if (data.furthestLocationIndex < -1) data.furthestLocationIndex = -1;
            if (data.furthestLocationIndex >= LocationCatalog.Count)
                data.furthestLocationIndex = LocationCatalog.Count - 1;
            ClampSettings(data.settings);

            // v5 rebuilt the world from twenty-one five-kilometre districts to twenty
            // three-kilometre locations. Drop retired location ids instead of rendering
            // phantom discoveries, and re-seat the furthest index inside the new catalogue.
            if (data.schemaVersion < 5)
            {
                for (int i = data.discoveredLocations.Count - 1; i >= 0; i--)
                    if (LocationCatalog.Find(data.discoveredLocations[i]) == null)
                        data.discoveredLocations.RemoveAt(i);
                for (int i = data.locationRecords.Count - 1; i >= 0; i--)
                    if (LocationCatalog.Find(data.locationRecords[i].locationId) == null)
                        data.locationRecords.RemoveAt(i);
                if (data.furthestLocationIndex >= LocationCatalog.Count)
                    data.furthestLocationIndex = LocationCatalog.Count - 1;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = data.discoveredLocations.Count - 1; i >= 0; i--)
            {
                string id = data.discoveredLocations[i];
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                    data.discoveredLocations.RemoveAt(i);
            }
            for (int i = data.locationRecords.Count - 1; i >= 0; i--)
            {
                var record = data.locationRecords[i];
                if (record == null || string.IsNullOrEmpty(record.locationId))
                    data.locationRecords.RemoveAt(i);
            }
            if (data.discoveredLocations.Count > 0 && data.furthestLocationIndex < 0)
                data.furthestLocationIndex = 0;

            if (data.premium.deviceKey == null) data.premium.deviceKey = "";
            if (data.premium.token == null) data.premium.token = "";
            if (data.premium.catalogJson == null) data.premium.catalogJson = "";
            if (data.premium.ownedItems == null) data.premium.ownedItems = new List<PremiumOwnership>();
            if (data.premium.characters == null) data.premium.characters = new List<string>();
            if (data.premium.upgrades == null) data.premium.upgrades = new List<PremiumUpgradeEntry>();
            if (data.premium.gems < 0) data.premium.gems = 0;
            if (data.premium.pendingOrders < 0) data.premium.pendingOrders = 0;
            data.premium.deviceKey = data.premium.deviceKey.Trim();
            if (data.premium.deviceKey.Length > 128)
                data.premium.deviceKey = data.premium.deviceKey.Substring(0, 128);
            for (int i = data.premium.upgrades.Count - 1; i >= 0; i--)
            {
                var upgrade = data.premium.upgrades[i];
                if (upgrade == null || string.IsNullOrEmpty(upgrade.upgradeId))
                    data.premium.upgrades.RemoveAt(i);
                else upgrade.level = Math.Max(0, Math.Min(3, upgrade.level));
            }

            while (data.redeemedRewardIds.Count > 100) data.redeemedRewardIds.RemoveAt(0);
            for (int i = 0; i < data.upgrades.Count; i++)
            {
                if (data.upgrades[i] == null) continue;
                data.upgrades[i].level = Math.Max(0, Math.Min(5, data.upgrades[i].level));
            }
        }

        public static void ClampSettings(SettingsData s)
        {
            if (s == null) return;
            s.masterVolume = Clamp01(s.masterVolume);
            s.musicVolume = Clamp01(s.musicVolume);
            s.sfxVolume = Clamp01(s.sfxVolume);
            s.ambienceVolume = Clamp01(s.ambienceVolume);
            s.swipeSensitivity = Math.Max(0.4f, Math.Min(2.5f, s.swipeSensitivity));
            s.graphicsPreset = Math.Max(0, Math.Min(4, s.graphicsPreset));
            s.targetFrameRate = s.targetFrameRate == 30 || s.targetFrameRate == 120 ||
                s.targetFrameRate == 144 ? s.targetFrameRate : 60;
            s.screenWidth = Math.Max(0, Math.Min(7680, s.screenWidth));
            s.screenHeight = Math.Max(0, Math.Min(4320, s.screenHeight));
            if (s.screenWidth > 0 && s.screenHeight <= 0) s.screenWidth = 0;
            if (s.screenHeight > 0 && s.screenWidth <= 0) s.screenHeight = 0;
            s.fullscreenMode = Math.Max(0, Math.Min(2, s.fullscreenMode));
            s.renderScale = Math.Max(0.5f, Math.Min(1f, s.renderScale));
            s.textureQuality = Math.Max(0, Math.Min(2, s.textureQuality));
            s.shadowQuality = Math.Max(0, Math.Min(3, s.shadowQuality));
            s.reflectionQuality = Math.Max(0, Math.Min(2, s.reflectionQuality));
            s.antiAliasing = s.antiAliasing >= 8 ? 8 : (s.antiAliasing >= 4 ? 4 : (s.antiAliasing >= 2 ? 2 : 0));
            s.effectsQuality = Math.Max(0, Math.Min(2, s.effectsQuality));
            s.viewDistance = Math.Max(0, Math.Min(2, s.viewDistance));
            if (s.serverPort < 1 || s.serverPort > 65535) s.serverPort = 7777;
            if (string.IsNullOrWhiteSpace(s.serverHost)) s.serverHost = "127.0.0.1";
            if (string.IsNullOrWhiteSpace(s.premiumServerUrl))
                s.premiumServerUrl = "http://127.0.0.1:8088";
            else s.premiumServerUrl = s.premiumServerUrl.Trim();
            if (s.premiumServerUrl.Length > 300)
                s.premiumServerUrl = s.premiumServerUrl.Substring(0, 300);
            s.timeOfDayMode = Math.Max(0, Math.Min(2, s.timeOfDayMode));
            s.timeOfDayPhase = TimeOfDayCatalog.ClampPhase(s.timeOfDayPhase);
            if (float.IsNaN(s.timeOfDayCycleMetres) || float.IsInfinity(s.timeOfDayCycleMetres) ||
                s.timeOfDayCycleMetres <= 0f)
                s.timeOfDayCycleMetres = TimeOfDayCatalog.DefaultCycleMetres;
            s.timeOfDayCycleMetres = Math.Max(TimeOfDayCatalog.MinimumCycleMetres,
                Math.Min(TimeOfDayCatalog.MaximumCycleMetres, s.timeOfDayCycleMetres));
        }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
