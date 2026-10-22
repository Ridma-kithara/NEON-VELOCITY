using System;
using System.Collections.Generic;

namespace NeonVelocity.Core
{
    public enum RacePhase
    {
        Offline,
        Connecting,
        Lobby,
        Countdown,
        Racing,
        Results,
        Error
    }

    /// <summary>Flat wire message used by both the Unity client and the Python authority.</summary>
    [Serializable]
    public class WireMessage
    {
        public string type;
        public string requestId;
        public bool ok;
        public string error;
        public string protocol = "nv1";
        public string clientVersion = "1.0.0";

        public string playerId;
        public string token;
        public string name;
        public string mode;
        public string roomCode;
        public string hostId;
        public int seed;
        public long startAtMs;
        public long serverTimeMs;
        public long countdownAtMs;
        public float raceDurationSeconds;
        public string phase;
        public bool ready;
        public bool privateRoom;
        public bool botsEnabled;
        public int maxPlayers;
        public int botCount;

        public float distance;
        public float speed;
        public int score;
        public int coins;
        public int lane;
        public string runnerState;
        public int lives;
        public int rank;
        public bool finished;
        public bool connected = true;
        public long finishTimeMs;
        public float survivalTime;
        public bool rewardGranted;
        public int rewardsCredits;
        public int rewardsXp;
        public string raceId;
        public int nearMisses;
        public int powerUpsUsed;
        public int place;

        public List<WirePlayer> players = new List<WirePlayer>();
        public List<WireLeaderboardEntry> leaderboard = new List<WireLeaderboardEntry>();
        public string profileJson;
        public string cloudSaveJson;
        public string message;
    }

    [Serializable]
    public class WirePlayer
    {
        public string playerId;
        public string name;
        public bool ready;
        public bool host;
        public bool bot;
        public bool connected = true;
        public bool finished;
        public float distance;
        public float speed;
        public int score;
        public int coins;
        public int lane;
        public string runnerState = "run";
        public int placement;
        public long lastSeenMs;
        public float survivalTime;
        public int lives = 1;
        public int rewardsCredits;
        public int rewardsXp;
    }

    [Serializable]
    public class WireLeaderboardEntry
    {
        public int rank;
        public string name;
        public float distance;
        public int score;
        public int coins;
        public string mode;
        public string achievedUtc;
        public bool verified;
    }

    public static class RaceRules
    {
        public const int MaxPlayers = 5;
        public const float RaceDurationSeconds = 120f;
        public const float ReconnectGraceSeconds = 45f;
        public const float SnapshotIntervalSeconds = 0.1f;
        public const float ValidationSlackMetres = 35f;
        public const float MaxReportedSpeed = 60f;

        /// <summary>
        /// Server-side sanity envelope. It is intentionally generous enough for prediction
        /// jitter, but rejects impossible distances and speeds.
        /// </summary>
        public static bool IsPlausibleDelta(float previousDistance, float reportedDistance,
            float deltaSeconds, float maxSpeed = 45f, string runnerState = "run")
        {
            if (float.IsNaN(reportedDistance) || float.IsNaN(previousDistance)) return false;
            if (reportedDistance + 8f < previousDistance) return false; // allow tiny float jitter, not rewinds
            float maxAdvance = maxSpeed * Math.Max(0f, deltaSeconds) + ValidationSlackMetres;
            if (runnerState == "dead" || runnerState == "finished") maxAdvance = ValidationSlackMetres;
            return reportedDistance - previousDistance <= maxAdvance;
        }

        public static bool IsPlausibleFinish(float distance, float survivalSeconds, float maxSpeed = 45f)
        {
            if (float.IsNaN(distance) || float.IsNaN(survivalSeconds)) return false;
            if (distance < 0f || survivalSeconds < 0f) return false;
            if (distance > 30000f) return false;
            return distance <= maxSpeed * Math.Max(0f, survivalSeconds) + 80f;
        }

        public static int CompareRank(WirePlayer a, WirePlayer b)
        {
            // The online circuit is a timed distance race. Final distance is the primary rank;
            // finishing is a tie-breaker ahead of score, coins and survival time.
            int byDistance = b.distance.CompareTo(a.distance);
            if (byDistance != 0) return byDistance;
            if (a.finished != b.finished) return a.finished ? -1 : 1;
            int byScore = b.score.CompareTo(a.score);
            if (byScore != 0) return byScore;
            int bySurvival = b.survivalTime.CompareTo(a.survivalTime);
            if (bySurvival != 0) return bySurvival;
            int byCoins = b.coins.CompareTo(a.coins);
            if (byCoins != 0) return byCoins;
            return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
        }

        public static void SortAndAssignRanks(List<WirePlayer> players)
        {
            if (players == null) return;
            players.Sort(CompareRank);
            for (int i = 0; i < players.Count; i++) players[i].placement = i + 1;
        }

        public static int PlacementRewardCredits(int placement, int totalPlayers)
        {
            if (totalPlayers <= 1) return 120;
            return placement switch
            {
                1 => 500,
                2 => 320,
                3 => 220,
                4 => 150,
                _ => 100
            };
        }

        public static string SanitiseName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "RUNNER";
            string trimmed = value.Trim();
            if (trimmed.Length > 20) trimmed = trimmed.Substring(0, 20);
            var chars = new char[trimmed.Length];
            int count = 0;
            foreach (char c in trimmed)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ') chars[count++] = c;
            }
            string result = new string(chars, 0, count).Trim();
            return string.IsNullOrEmpty(result) ? "RUNNER" : result;
        }
    }
}
