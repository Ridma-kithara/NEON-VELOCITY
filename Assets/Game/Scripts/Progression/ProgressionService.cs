using System;
using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Persistence;
using UnityEngine;

namespace NeonVelocity.Progression
{
    public struct RunSummary
    {
        public GameModeId ModeId;
        public float Distance;
        public int Score;
        public int Coins;
        public int Jumps;
        public int Slides;
        public int PowerUpsUsed;
        public int NearMisses;
        public float SurvivalSeconds;
        public bool Crashed;
        public bool OnlineResult;
        public int RacePlacement;
        public int RaceTotal;
        public int NearMissCredits;
        public int ScoreBonus;
        public int ServerCredits;
        public int ServerXp;
        public string ServerRaceId;
    }

    public sealed class ProgressionService
    {
        private readonly SaveService _save;
        private SaveData Data => _save.Data;

        public ProgressionService(SaveService save)
        {
            _save = save;
            RefreshMissions(DateTime.UtcNow);
        }

        public SaveData Profile => _save.Data;
        public event Action ProgressChanged;

        public void RefreshMissions(DateTime utc)
        {
            string dailyKey = MissionFactory.DailyKey(utc);
            string weeklyKey = MissionFactory.WeeklyKey(utc);
            bool changed = false;
            var m = Data.missions;
            if (m.dailyDateKey != dailyKey)
            {
                m.missions.RemoveAll(x => x.scope == "daily");
                m.dailyDateKey = dailyKey;
                m.missions.AddRange(MissionFactory.GenerateDaily(dailyKey));
                changed = true;
            }
            if (m.weeklyDateKey != weeklyKey)
            {
                m.missions.RemoveAll(x => x.scope == "weekly");
                m.weeklyDateKey = weeklyKey;
                m.missions.AddRange(MissionFactory.GenerateWeekly(weeklyKey));
                changed = true;
            }
            for (int i = 0; i < m.missions.Count; i++)
            {
                if (m.missions[i].target < 1) m.missions[i].target = 1;
                m.missions[i].completed = m.missions[i].progress >= m.missions[i].target;
            }
            if (changed) _save.Save();
        }

        public List<MissionState> ActiveMissions(string scope)
        {
            RefreshMissions(DateTime.UtcNow);
            return Data.missions.missions.FindAll(x => x.scope == scope);
        }

        public RunRewards ApplyRun(RunSummary run)
        {
            RefreshMissions(DateTime.UtcNow);
            var spec = GameModes.Get(run.ModeId);
            var rewards = new RunRewards { Messages = new List<string>() };
            if (run.OnlineResult && !string.IsNullOrEmpty(run.ServerRaceId) && Data.IsRedeemed("race:" + run.ServerRaceId))
            {
                rewards.Verified = true;
                rewards.Messages.Add("RESULT ALREADY VERIFIED");
                return rewards;
            }
            bool progression = spec.CountsProgression;
            int credits = 0;
            int xp = 0;

            if (run.OnlineResult)
            {
                // Competitive rewards come from the server response; this path is idempotent.
                credits = Mathf.Max(0, run.ServerCredits);
                xp = Mathf.Max(0, run.ServerXp);
                rewards.Verified = true;
            }
            else if (progression)
            {
                credits = ProgressionMath.CreditsForRun(run.Coins, run.Distance, spec.CoinMultiplier);
                credits += Mathf.Max(0, run.NearMissCredits);
                xp = ProgressionMath.XpForRun(run.Distance, run.Score, run.Coins,
                    run.ModeId == GameModeId.SeasonalEvent ? 2f : 1f);
            }

            rewards.Credits = credits;
            rewards.Xp = xp;

            if (progression)
            {
                Data.credits += credits;
                Data.totalCoins += Mathf.Max(0, run.Coins);
                Data.xp += xp;
                Data.level = ProgressionMath.LevelFromXp(Data.xp);
                Data.seasonXp += xp;
                Data.seasonLevel = ProgressionMath.SeasonLevel(Data.seasonXp);

                var lifetime = Data.lifetime;
                lifetime.totalRuns++;
                lifetime.totalDistance += Mathf.Max(0f, run.Distance);
                lifetime.totalCoins += Mathf.Max(0, run.Coins);
                lifetime.totalJumps += Mathf.Max(0, run.Jumps);
                lifetime.totalSlides += Mathf.Max(0, run.Slides);
                lifetime.totalPowerUps += Mathf.Max(0, run.PowerUpsUsed);
                lifetime.nearMisses += Mathf.Max(0, run.NearMisses);
                if (run.Crashed) lifetime.totalDeaths++;
                if (run.OnlineResult)
                {
                    lifetime.racesCompleted++;
                    if (run.RacePlacement == 1) lifetime.racesWon++;
                    if (lifetime.bestRaceRank == 0 || (run.RacePlacement > 0 && run.RacePlacement < lifetime.bestRaceRank))
                        lifetime.bestRaceRank = run.RacePlacement;
                }

                if (run.Distance > Data.bestDistance)
                {
                    Data.bestDistance = run.Distance;
                    Data.bestScore = Mathf.Max(Data.bestScore, run.Score);
                    Data.bestModeId = run.ModeId.ToString();
                }
                else if (run.Score > Data.bestScore)
                {
                    Data.bestScore = run.Score;
                }

                var modeBest = Data.FindModeBest(run.ModeId.ToString());
                if (modeBest == null)
                {
                    modeBest = new ModeBest { modeId = run.ModeId.ToString() };
                    Data.modeBests.Add(modeBest);
                }
                if (run.Distance > modeBest.distance)
                {
                    modeBest.distance = run.Distance;
                    modeBest.score = run.Score;
                    modeBest.coins = run.Coins;
                    modeBest.achievedUtc = DateTime.UtcNow.ToString("O");
                }

                ApplyMissions(run);
                CheckAchievements(ref rewards);
                UpdateLocalLeaderboard(run);

                if (run.ModeId == GameModeId.DailyChallenge && run.Distance >= 600f)
                {
                    string rewardId = "daily-challenge:" + MissionFactory.DailyKey(DateTime.UtcNow);
                    if (!Data.IsRedeemed(rewardId))
                    {
                        Data.MarkRedeemed(rewardId);
                        Data.credits += 250;
                        rewards.Credits += 250;
                        rewards.Messages.Add("DAILY SIGNAL CLEARED  +250");
                    }
                }
                else if (run.ModeId == GameModeId.WeeklyChallenge && run.Distance >= 1500f)
                {
                    string rewardId = "weekly-challenge:" + MissionFactory.WeeklyKey(DateTime.UtcNow);
                    if (!Data.IsRedeemed(rewardId))
                    {
                        Data.MarkRedeemed(rewardId);
                        Data.credits += 600;
                        rewards.Credits += 600;
                        rewards.Messages.Add("WEEKLY PROTOCOL CLEARED  +600");
                    }
                }
                else if (run.ModeId == GameModeId.SeasonalEvent && run.Distance >= 1000f)
                {
                    string rewardId = "seasonal-monsoon:" + DateTime.UtcNow.ToString("yyyyMMdd");
                    if (!Data.IsRedeemed(rewardId))
                    {
                        Data.MarkRedeemed(rewardId);
                        Data.credits += 300;
                        rewards.Credits += 300;
                        rewards.Messages.Add("MONSOON OVERDRIVE CLEARED  +300");
                    }
                }

                if (run.ModeId == GameModeId.Tutorial && !Data.tutorialCompleted)
                {
                    Data.tutorialCompleted = true;
                    Data.credits += 200;
                    rewards.Credits += 200;
                    rewards.Messages.Add("FIRST CONTACT COMPLETE  +200");
                }
            }
            else
            {
                rewards.Messages.Add("PRACTICE MODE - NO COMPETITIVE REWARDS");
            }

            if (run.OnlineResult && !string.IsNullOrEmpty(run.ServerRaceId))
                Data.MarkRedeemed("race:" + run.ServerRaceId);
            _save.Save();
            ProgressChanged?.Invoke();
            return rewards;
        }

        private void ApplyMissions(RunSummary run)
        {
            var missions = Data.missions.missions;
            for (int i = 0; i < missions.Count; i++)
            {
                var m = missions[i];
                if (m.claimed) continue;
                int delta = 0;
                switch ((MissionMetric)m.metric)
                {
                    case MissionMetric.Distance: delta = Mathf.RoundToInt(run.Distance); break;
                    case MissionMetric.Coins: delta = Mathf.Max(0, run.Coins); break;
                    case MissionMetric.Runs: delta = 1; break;
                    case MissionMetric.Jumps: delta = Mathf.Max(0, run.Jumps); break;
                    case MissionMetric.Slides: delta = Mathf.Max(0, run.Slides); break;
                    case MissionMetric.PowerUps: delta = Mathf.Max(0, run.PowerUpsUsed); break;
                    case MissionMetric.Races: delta = run.OnlineResult ? 1 : 0; break;
                    case MissionMetric.Score: delta = Mathf.Max(0, run.Score); break;
                    case MissionMetric.NearMisses: delta = Mathf.Max(0, run.NearMisses); break;
                }
                if (delta <= 0) continue;
                if ((MissionMetric)m.metric == MissionMetric.Score)
                    m.progress = Mathf.Max(m.progress, delta);
                else
                    m.progress = Mathf.Min(m.target, m.progress + delta);
                m.completed = m.progress >= m.target;
            }
        }

        private void CheckAchievements(ref RunRewards rewards)
        {
            foreach (var achievement in MissionFactory.Achievements)
            {
                if (Data.IsRedeemed("ach:" + achievement.id)) continue;
                if (achievement.LifetimeProgress(Data.lifetime) < achievement.target) continue;
                Data.MarkRedeemed("ach:" + achievement.id);
                Data.credits += achievement.credits;
                rewards.Credits += achievement.credits;
                rewards.Messages.Add("ACHIEVEMENT  " + achievement.title + "  +" + achievement.credits);
            }
        }

        private void UpdateLocalLeaderboard(RunSummary run)
        {
            var entry = new LeaderboardEntryData
            {
                playerName = Data.playerName,
                distance = run.Distance,
                score = run.Score,
                modeId = run.ModeId.ToString(),
                verified = run.OnlineResult,
                achievedUtc = DateTime.UtcNow.ToString("O")
            };
            Data.localLeaderboard.Add(entry);
            Data.localLeaderboard.Sort((a, b) => b.distance.CompareTo(a.distance));
            if (Data.localLeaderboard.Count > 20) Data.localLeaderboard.RemoveRange(20, Data.localLeaderboard.Count - 20);
            for (int i = 0; i < Data.localLeaderboard.Count; i++) Data.localLeaderboard[i].rank = i + 1;
        }

        public bool ClaimMission(string missionId)
        {
            var mission = Data.missions.missions.Find(x => x.id == missionId);
            if (mission == null || mission.claimed || mission.progress < mission.target) return false;
            mission.claimed = true;
            Data.credits += mission.rewardCredits;
            Data.xp += mission.rewardXp;
            Data.level = ProgressionMath.LevelFromXp(Data.xp);
            Data.lifetime.missionsClaimed++;
            _save.Save();
            ProgressChanged?.Invoke();
            return true;
        }

        public DailyClaimResult ClaimDailyReward(DateTime utc)
        {
            string today = utc.ToString("O");
            if (!DailyRewards.CanClaim(Data.lastDailyClaimUtc, today))
                return new DailyClaimResult { Success = false, Message = "ALREADY CLAIMED TODAY" };
            int streak = DailyRewards.StreakAfterClaim(Data.lastDailyClaimUtc, today, Data.dailyStreak);
            if (streak <= 0) streak = Data.dailyStreak;
            int credits = DailyRewards.CreditsForStreak(streak);
            Data.dailyStreak = streak;
            Data.lastDailyClaimUtc = today;
            Data.credits += credits;
            Data.lifetime.dailyRewardsClaimed++;
            _save.Save();
            ProgressChanged?.Invoke();
            return new DailyClaimResult { Success = true, Credits = credits, Streak = streak, Message = $"DAY {streak} SUPPLY DROP  +{credits}" };
        }

        public bool BuyUpgrade(string id, out string message)
        {
            if (string.IsNullOrEmpty(id)) { message = "UNKNOWN UPGRADE"; return false; }
            int current = Data.GetUpgradeLevel(id);
            if (current >= ProgressionMath.MaxUpgradeLevel) { message = "UPGRADE AT MAXIMUM"; return false; }
            int cost = ProgressionMath.UpgradeCost(id, current + 1);
            if (Data.credits < cost) { message = "INSUFFICIENT CREDITS"; return false; }
            Data.credits -= cost;
            var entry = Data.FindUpgrade(id);
            if (entry == null)
            {
                entry = new UpgradeEntry { id = id, level = 0 };
                Data.upgrades.Add(entry);
            }
            entry.level++;
            _save.Save();
            ProgressChanged?.Invoke();
            message = "UPGRADE INSTALLED";
            return true;
        }

        public bool UnlockCharacter(string id, out string message)
        {
            var character = GameConfig.Current.Character(id);
            if (character == null) { message = "UNKNOWN RUNNER"; return false; }
            if (Data.unlockedCharacters.Contains(id)) { message = "ALREADY UNLOCKED"; return false; }
            if (Data.level < character.unlockLevel) { message = "REACH LEVEL " + character.unlockLevel; return false; }
            if (Data.credits < character.unlockCost) { message = "INSUFFICIENT CREDITS"; return false; }
            Data.credits -= character.unlockCost;
            Data.unlockedCharacters.Add(id);
            _save.Save();
            ProgressChanged?.Invoke();
            message = character.displayName + " UNLOCKED";
            return true;
        }

        public bool UnlockTrail(string id, out string message)
        {
            var trail = GameConfig.Current.Trail(id);
            if (trail == null) { message = "UNKNOWN TRAIL"; return false; }
            if (Data.unlockedTrails.Contains(id)) { message = "ALREADY UNLOCKED"; return false; }
            if (Data.credits < trail.unlockCost) { message = "INSUFFICIENT CREDITS"; return false; }
            Data.credits -= trail.unlockCost;
            Data.unlockedTrails.Add(id);
            _save.Save();
            ProgressChanged?.Invoke();
            message = trail.displayName + " UNLOCKED";
            return true;
        }

        public void SelectCharacter(string id)
        {
            if (!Data.unlockedCharacters.Contains(id)) return;
            Data.selectedCharacter = id;
            _save.Save();
            ProgressChanged?.Invoke();
        }

        public void SelectTrail(string id)
        {
            if (!Data.unlockedTrails.Contains(id)) return;
            Data.selectedTrail = id;
            _save.Save();
            ProgressChanged?.Invoke();
        }

        public void SetPlayerName(string name)
        {
            Data.playerName = RaceRules.SanitiseName(name);
            _save.Save();
            ProgressChanged?.Invoke();
        }

        public void AddCredits(int amount, string reason)
        {
            if (amount <= 0) return;
            Data.credits += amount;
            _save.Save();
            ProgressChanged?.Invoke();
            Debug.Log($"[Progression] +{amount} credits ({reason})");
        }

        public void RecordVerifiedRace(int credits, int xp, string raceId)
        {
            if (!string.IsNullOrEmpty(raceId)) Data.MarkRedeemed("race:" + raceId);
            AddCredits(credits, "verified race " + raceId);
            Data.xp += Mathf.Max(0, xp);
            Data.level = ProgressionMath.LevelFromXp(Data.xp);
            _save.Save();
            ProgressChanged?.Invoke();
        }

        public bool HasRaceReward(string raceId) => !string.IsNullOrEmpty(raceId) && Data.IsRedeemed("race:" + raceId);
    }

    public struct RunRewards
    {
        public int Credits;
        public int Xp;
        public bool Verified;
        public List<string> Messages;
        public static RunRewards Empty => new RunRewards { Messages = new List<string>() };
    }

    public struct DailyClaimResult
    {
        public bool Success;
        public int Credits;
        public int Streak;
        public string Message;
    }
}
