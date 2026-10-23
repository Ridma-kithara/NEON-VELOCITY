using System;
using System.Collections.Generic;
using System.Globalization;
using NeonVelocity.Core;

namespace NeonVelocity.CoreTests
{
    /// <summary>
    /// Headless test runner for the engine-independent gameplay core.
    /// Runs with the .NET SDK without a Unity licence, so the deterministic logic
    /// can be verified on CI or on machines where the editor cannot launch.
    /// </summary>
    internal static class Program
    {
        private static int _passed;
        private static readonly List<string> Failures = new List<string>();

        private static int Main()
        {
            Console.WriteLine("NEON VELOCITY - core logic tests");
            Console.WriteLine("--------------------------------");

            LaneTests();
            SpeedTests();
            TrackTests();
            SaveTests();
            ProgressionTests();
            RandomTests();
            MissionTests();
            RaceTests();
            ModeTests();

            Console.WriteLine("--------------------------------");
            Console.WriteLine($"Passed: {_passed}  Failed: {Failures.Count}");
            if (Failures.Count == 0)
            {
                Console.WriteLine("CORE TESTS: PASS");
                return 0;
            }

            Console.WriteLine("CORE TESTS: FAIL");
            foreach (var failure in Failures) Console.WriteLine("  - " + failure);
            return 1;
        }

        private static void LaneTests()
        {
            Run("lane positions are centred and spaced", () =>
            {
                Eq(-2.7f, LaneMath.LaneToX(0), "left lane x");
                Eq(0f, LaneMath.LaneToX(1), "centre lane x");
                Eq(2.7f, LaneMath.LaneToX(2), "right lane x");
            });

            Run("lane clamping keeps the runner on the grid", () =>
            {
                Eq(0, LaneMath.ClampLane(-9));
                Eq(0, LaneMath.ClampLane(0));
                Eq(1, LaneMath.ClampLane(1));
                Eq(2, LaneMath.ClampLane(2));
                Eq(2, LaneMath.ClampLane(9));
            });

            Run("lane movement respects edges and zero input", () =>
            {
                Eq(1, LaneMath.MoveLane(0, 1));
                Eq(0, LaneMath.MoveLane(0, -1));
                Eq(2, LaneMath.MoveLane(2, 1));
                Eq(1, LaneMath.MoveLane(2, -1));
                Eq(1, LaneMath.MoveLane(1, 0));
                Eq(0, LaneMath.MoveLane(0, -4));
            });

            Run("lane input validity rejects blocked directions", () =>
            {
                False(LaneMath.IsLaneInputValid(0, -1));
                False(LaneMath.IsLaneInputValid(2, 1));
                False(LaneMath.IsLaneInputValid(1, 0));
                True(LaneMath.IsLaneInputValid(1, -1));
                True(LaneMath.IsLaneInputValid(1, 1));
            });

            Run("lane swing is a smooth, clamped interpolation", () =>
            {
                Eq(2.7f, LaneMath.Swing(-2.7f, 2.7f, -2.7f, 0.2f, 0.2f), "completion");
                Eq(-2.7f, LaneMath.Swing(-2.7f, 2.7f, -2.7f, 0.2f, 0f), "start");
                Near(0f, LaneMath.Swing(-2.7f, 2.7f, -2.7f, 0.2f, 0.1f), 0.0001f, "halfway");
                Eq(2.7f, LaneMath.Swing(-2.7f, 2.7f, -2.7f, 0f, 0f), "instant");
                Eq(2.7f, LaneMath.Swing(-2.7f, 2.7f, -2.7f, 0.2f, 5f), "overshoot");
            });
        }

        private static void SpeedTests()
        {
            Run("speed is positive, monotonic and capped", () =>
            {
                var model = new SpeedModel();
                float previous = model.SpeedAt(0f, 0f);
                True(previous > 0f, "start speed is positive");
                for (int i = 1; i <= 240; i++)
                {
                    float speed = model.SpeedAt(i * 80f, i * 2f);
                    True(speed >= previous - 0.0001f, "speed should not fall at step " + i);
                    True(speed <= model.MaxSpeed + 0.0001f, "speed should respect the cap at step " + i);
                    previous = speed;
                }
                Near(model.MaxSpeed, previous, 0.2f, "late-run speed should approach the cap");
            });

            Run("speed boost multiplies output without mutating state", () =>
            {
                var model = new SpeedModel();
                float normal = model.SpeedAt(500f, 20f);
                model.SetBoost(1.5f);
                float boosted = model.SpeedAt(500f, 20f);
                Near(normal * 1.5f, boosted, 0.001f, "boosted speed");
                model.SetBoost(0.5f);
                Near(normal, model.SpeedAt(500f, 20f), 0.001f, "boost is clamped to at least 1x");
                model.SetBoost(-3f);
                Near(normal, model.SpeedAt(500f, 20f), 0.001f, "negative boost is clamped");
            });

            Run("speed model clamps nonsensical configuration", () =>
            {
                var model = new SpeedModel(-10f, -20f, -1f, -1f);
                Eq(1f, model.BaseSpeed);
                Eq(2f, model.MaxSpeed);
                Eq(0f, model.AccelerationPerSecond);
                Eq(0f, model.AccelerationPerDistance);
            });

            Run("plausible distance and difficulty stages are ordered", () =>
            {
                var model = new SpeedModel();
                Near(model.MaxSpeed * 10f + 25f, model.MaxPlausibleDistance(10f), 0.001f);
                Eq(25f, model.MaxPlausibleDistance(-5f));
                Eq(0, model.DifficultyStage(0f));
                Eq(0, model.DifficultyStage(449.9f));
                Eq(1, model.DifficultyStage(450f));
                Eq(2, model.DifficultyStage(1100f));
                Eq(3, model.DifficultyStage(2200f));
                Eq(4, model.DifficultyStage(4000f));
            });
        }

        private static void TrackTests()
        {
            Run("track rows parse the authored vocabulary", () =>
            {
                var row = TrackPattern.ParseRow(".LH");
                Eq(CellKind.Empty, row.Left);
                Eq(CellKind.LowBarrier, row.Centre);
                Eq(CellKind.OverheadGate, row.Right);
                False(row.CanPassWithoutAction(1));
                True(row.RequiresJump(1));
                True(row.RequiresSlide(2));
                var solid = TrackPattern.ParseRow("X.E");
                True(solid.IsSolid(0));
                True(solid.RequiresJump(2));
            });

            Run("invalid track rows are rejected", () =>
            {
                Throws<ArgumentNullException>(() => TrackPattern.ParseRow(null));
                Throws<FormatException>(() => TrackPattern.ParseRow(".."));
                Throws<FormatException>(() => TrackPattern.ParseRow("...."));
                Throws<FormatException>(() => TrackPattern.ParseRow("..Z"));
            });

            Run("every shipped pattern is navigable", () =>
            {
                True(TrackPatternLibrary.All.Length >= 15, "expected the authored pattern library");
                var ids = new HashSet<string>();
                foreach (var pattern in TrackPatternLibrary.All)
                {
                    True(ids.Add(pattern.Id), "duplicate pattern id " + pattern.Id);
                    var issues = TrackValidator.Validate(pattern);
                    Eq(0, issues.Count, pattern.Id + " -> " + string.Join(" | ", issues));
                    True(TrackValidator.HasLanePath(pattern), pattern.Id + " has no lane path");
                }
            });

            Run("validator catches blocked and non-navigable rows", () =>
            {
                var blocked = new TrackPattern("blocked", 0, 7f, false, "XXX", "...");
                var issues = TrackValidator.Validate(blocked);
                True(issues.Count > 0, "three solids should be rejected");
                True(issues.Exists(i => i.Contains("blocks every lane")), "expected full block issue");
                False(TrackValidator.HasLanePath(new TrackPattern("walls", 0, 7f, false, "X.X", "XXX", "...")));

                var empty = new TrackPattern("empty", 0, 7f, false);
                Eq(0, TrackValidator.Validate(empty).Count);
                True(empty.Safe, "empty pattern is marked safe");
            });

            Run("validator enforces full-width action spacing", () =>
            {
                var tooClose = new TrackPattern("too-close", 1, 7f, false, "LLL", "LLL", "...");
                var issues = TrackValidator.Validate(tooClose);
                True(issues.Exists(i => i.Contains("too close")), "two full hurdles 7m apart should be rejected");
                var spaced = new TrackPattern("spaced", 1, 7f, false, "LLL", "...", "...", "...", "...", "HHH");
                Eq(0, TrackValidator.Validate(spaced).Count, "hurdles 35m apart should pass");
            });

            Run("generated track is deterministic for a shared seed", () =>
            {
                var first = new TrackDirector(481516, 0);
                var second = new TrackDirector(481516, 0);
                var a = first.BuildRange(0, 60);
                var b = second.BuildRange(0, 60);
                Eq(a.Count, b.Count);
                for (int i = 0; i < a.Count; i++)
                {
                    Eq(a[i].PatternId, b[i].PatternId, "pattern " + i);
                    Eq(a[i].Rows.Length, b[i].Rows.Length, "row count " + i);
                    for (int r = 0; r < a[i].Rows.Length; r++)
                        for (int lane = 0; lane < 3; lane++)
                            Eq(a[i].Rows[r][lane], b[i].Rows[r][lane], $"segment {i} row {r} lane {lane}");
                }
            });

            Run("different seeds and salts produce different layouts", () =>
            {
                var baseline = new TrackDirector(100, 0).BuildRange(0, 40);
                var otherSeed = new TrackDirector(101, 0).BuildRange(0, 40);
                var otherSalt = new TrackDirector(100, GameModes.Get(GameModeId.DailyChallenge).SeedSalt).BuildRange(0, 40);
                True(HasDifference(baseline, otherSeed) || HasDifference(baseline, otherSalt), "layout should vary");
            });

            Run("generated track opens safely and is always navigable", () =>
            {
                foreach (int seed in new[] { 1, 7, 99, 424242, -551 })
                {
                    var segments = new TrackDirector(seed, 3).BuildRange(0, 80);
                    for (int i = 0; i < segments.Count; i++)
                    {
                        var s = segments[i];
                        Eq(i * TrackDirector.SegmentLength, s.WorldStartZ, "segment origin");
                        if (i < TrackDirector.SafeOpeningSegments)
                        {
                            True(s.IsSafe, "opening segment " + i + " should be safe");
                            for (int r = 0; r < s.Rows.Length; r++)
                                for (int lane = 0; lane < 3; lane++)
                                    Eq(CellKind.Empty, s.Rows[r][lane], "opening cell");
                        }
                    }
                    var issues = GeneratedTrackValidator.Validate(segments);
                    Eq(0, issues.Count, "seed " + seed + " -> " + string.Join(" | ", issues));
                }
            });

            Run("difficulty tiers follow distance thresholds", () =>
            {
                Eq(0, TrackDirector.DifficultyForIndex(0));
                Eq(0, TrackDirector.DifficultyForIndex(1));
                Eq(1, TrackDirector.DifficultyForIndex(11));
                Eq(2, TrackDirector.DifficultyForIndex(27));
                Eq(3, TrackDirector.DifficultyForIndex(53));
                Eq(4, TrackDirector.DifficultyForIndex(96));
            });

            Run("track seeds are stable and non-zero", () =>
            {
                var utc = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
                int a = TrackDirector.SeedForRun("classic", utc, "p1");
                int b = TrackDirector.SeedForRun("classic", utc, "p1");
                int c = TrackDirector.SeedForRun("classic", utc, "p2");
                Eq(a, b);
                True(a != 0, "seed must never be zero");
                True(a != c, "seed should include the player id");
            });
        }

        private static bool HasDifference(List<SegmentPlan> a, List<SegmentPlan> b)
        {
            int count = Math.Min(a.Count, b.Count);
            for (int i = 0; i < count; i++)
            {
                if (a[i].PatternId != b[i].PatternId) return true;
                for (int lane = 0; lane < 3; lane++)
                    if (a[i].Rows[lane][lane] != b[i].Rows[lane][lane]) return true;
            }
            return false;
        }

        private static void SaveTests()
        {
            Run("save migration fills null collections", () =>
            {
                var data = new SaveData
                {
                    playerName = null,
                    unlockedCharacters = null,
                    unlockedTrails = null,
                    upgrades = null,
                    redeemedRewardIds = null,
                    modeBests = null,
                    localLeaderboard = null,
                    lifetime = null,
                    settings = null,
                    missions = null,
                    selectedCharacter = "ghost",
                    selectedTrail = "missing"
                };
                SaveMigration.Normalise(data);
                NotNull(data.unlockedCharacters);
                NotNull(data.unlockedTrails);
                NotNull(data.upgrades);
                NotNull(data.redeemedRewardIds);
                NotNull(data.modeBests);
                NotNull(data.localLeaderboard);
                NotNull(data.lifetime);
                NotNull(data.settings);
                NotNull(data.missions);
                NotNull(data.missions.missions);
                Eq("RUNNER-01", data.playerName);
                Eq("kira", data.selectedCharacter);
                Eq("spark", data.selectedTrail);
                True(data.unlockedCharacters.Contains("kira"));
                True(data.unlockedTrails.Contains("spark"));
            });

            Run("save migration clamps corrupt values", () =>
            {
                var data = new SaveData
                {
                    credits = -50,
                    xp = -100,
                    level = 0,
                    seasonXp = -1,
                    seasonLevel = 0,
                    bestDistance = -10f,
                    dailyStreak = 99,
                    playerName = "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
                    upgrades = new List<UpgradeEntry> { new UpgradeEntry { id = "magnet", level = 99 }, null }
                };
                data.settings.musicVolume = 2f;
                data.settings.sfxVolume = -2f;
                data.settings.ambienceVolume = 0.25f;
                data.settings.swipeSensitivity = 9f;
                data.settings.graphicsPreset = 9;
                data.settings.targetFrameRate = 59;
                data.settings.serverPort = 0;
                data.settings.serverHost = "   ";
                SaveMigration.Normalise(data);
                Eq(0, data.credits);
                Eq(0, data.xp);
                Eq(1, data.level);
                Eq(0, data.seasonXp);
                Eq(1, data.seasonLevel);
                Eq(0f, data.bestDistance);
                Eq(0, data.dailyStreak);
                Eq(20, data.playerName.Length);
                Eq(5, data.upgrades[0].level);
                Eq(1f, data.settings.musicVolume);
                Eq(0f, data.settings.sfxVolume);
                Eq(0.25f, data.settings.ambienceVolume);
                Eq(2.5f, data.settings.swipeSensitivity);
                Eq(4, data.settings.graphicsPreset);
                Eq(60, data.settings.targetFrameRate);
                Eq(7777, data.settings.serverPort);
                Eq("127.0.0.1", data.settings.serverHost);
            });

            Run("normalising a fresh save is a no-op", () =>
            {
                var data = new SaveData();
                SaveMigration.Normalise(data);
                Eq(SaveData.CurrentSchemaVersion, data.schemaVersion);
                Eq(500, data.credits);
                Eq("kira", data.selectedCharacter);
                Eq(0.62f, data.settings.musicVolume);
                SaveMigration.Normalise(null);
            });

            Run("upgrade lookup is safe on sparse saves", () =>
            {
                var data = new SaveData { upgrades = null };
                Eq(0, data.GetUpgradeLevel("magnet"));
                data.upgrades = new List<UpgradeEntry> { new UpgradeEntry { id = "magnet", level = 3 } };
                Eq(3, data.GetUpgradeLevel("magnet"));
                Eq(0, data.GetUpgradeLevel("shield"));
                True(data.FindModeBest("classic") == null);
            });

            Run("reward redemption is idempotent and bounded", () =>
            {
                var data = new SaveData { redeemedRewardIds = new List<string>() };
                data.MarkRedeemed("race:a");
                data.MarkRedeemed("race:a");
                Eq(1, data.redeemedRewardIds.Count);
                True(data.IsRedeemed("race:a"));
                False(data.IsRedeemed("race:b"));
                data.MarkRedeemed("");
                data.MarkRedeemed(null);
                Eq(1, data.redeemedRewardIds.Count);
                for (int i = 0; i < 10; i++) data.MarkRedeemed("race:" + i, keepAtMost: 3);
                Eq(3, data.redeemedRewardIds.Count);
                True(data.IsRedeemed("race:9"));
                False(data.IsRedeemed("race:a"));
            });
        }

        private static void ProgressionTests()
        {
            Run("xp thresholds grow linearly", () =>
            {
                Eq(450, ProgressionMath.XpRequiredForLevel(1));
                Eq(670, ProgressionMath.XpRequiredForLevel(2));
                Eq(450, ProgressionMath.XpRequiredForLevel(0));
                Eq(450 + 670, ProgressionMath.TotalXpForLevel(3));
                Eq(0, ProgressionMath.TotalXpForLevel(1));
            });

            Run("levels derive from cumulative xp", () =>
            {
                Eq(1, ProgressionMath.LevelFromXp(0));
                Eq(1, ProgressionMath.LevelFromXp(449));
                Eq(2, ProgressionMath.LevelFromXp(450));
                Eq(2, ProgressionMath.LevelFromXp(1119));
                Eq(3, ProgressionMath.LevelFromXp(1120));
                Eq(1, ProgressionMath.LevelFromXp(-999));
                Eq(0, ProgressionMath.XpIntoCurrentLevel(450));
                Eq(100, ProgressionMath.XpIntoCurrentLevel(550));
                Near(100f / 670f, ProgressionMath.LevelProgress(550), 0.0001f);
                Eq(1f, ProgressionMath.LevelProgress(ProgressionMath.TotalXpForLevel(100)));
            });

            Run("adding xp reports every level gained", () =>
            {
                int xp = 0;
                int gained = ProgressionMath.AddXp(ref xp, 500, out int level);
                Eq(500, xp);
                Eq(1, gained);
                Eq(2, level);
                gained = ProgressionMath.AddXp(ref xp, 620, out level);
                Eq(1120, xp);
                Eq(1, gained);
                Eq(3, level);
                gained = ProgressionMath.AddXp(ref xp, -500, out level);
                Eq(1120, xp);
                Eq(0, gained);
                Eq(3, level);
                xp = ProgressionMath.TotalXpForLevel(100);
                gained = ProgressionMath.AddXp(ref xp, 100000, out level);
                Eq(100, level);
                Eq(0, gained);
            });

            Run("run scoring combines distance, coins and bonuses", () =>
            {
                Eq(2155, ProgressionMath.ScoreForRun(100f, 10, 2f, 5));
                Eq(0, ProgressionMath.ScoreForRun(-100f, -10, 0.5f, 0));
                Eq(1500, ProgressionMath.ScoreForRun(150f, 0, 1f, 0));
                Eq(88, ProgressionMath.XpForRun(100f, 1000, 10, 1f));
                Eq(176, ProgressionMath.XpForRun(100f, 1000, 10, 2f));
                Eq(7, ProgressionMath.CreditsForRun(5, 100f, 1f));
                Eq(21, ProgressionMath.CreditsForRun(5, 100f, 3f));
            });

            Run("upgrade curves are monotonic and clamped", () =>
            {
                foreach (var id in new[] { "magnet", "shield", "multiplier", "boost", "head_start", "unknown" })
                {
                    True(ProgressionMath.UpgradeCost(id, 2) > ProgressionMath.UpgradeCost(id, 1), id + " cost should rise");
                    True(ProgressionMath.UpgradeValue(id, 5) >= ProgressionMath.UpgradeValue(id, 0), id + " value should rise");
                    Eq(ProgressionMath.UpgradeValue(id, 5), ProgressionMath.UpgradeValue(id, 50), id + " clamps above max");
                    Eq(ProgressionMath.UpgradeValue(id, 0), ProgressionMath.UpgradeValue(id, -5), id + " clamps below zero");
                }
                Eq(0f, ProgressionMath.UpgradeValue("head_start", 0));
                Eq(275f, ProgressionMath.UpgradeValue("head_start", 5));
            });

            Run("season levels cap at fifty", () =>
            {
                Eq(1, ProgressionMath.SeasonLevel(-10));
                Eq(1, ProgressionMath.SeasonLevel(0));
                Eq(1, ProgressionMath.SeasonLevel(1199));
                Eq(2, ProgressionMath.SeasonLevel(1200));
                Eq(50, ProgressionMath.SeasonLevel(1000000));
            });

            Run("daily reward streak cycles", () =>
            {
                Eq(100, DailyRewards.CreditsForStreak(0));
                Eq(100, DailyRewards.CreditsForStreak(1));
                Eq(800, DailyRewards.CreditsForStreak(7));
                Eq(100, DailyRewards.CreditsForStreak(8));
                Eq(100, DailyRewards.CreditsForStreak(-1));
            });

            Run("daily streaks respond to UTC calendar days", () =>
            {
                const string today = "2026-10-22T10:00:00Z";
                Eq(1, DailyRewards.StreakAfterClaim("", today, 3));
                Eq(0, DailyRewards.StreakAfterClaim("2026-10-22T01:00:00Z", today, 3));
                Eq(4, DailyRewards.StreakAfterClaim("2026-10-21T23:00:00Z", today, 3));
                Eq(1, DailyRewards.StreakAfterClaim("2026-10-19T23:00:00Z", today, 3));
                Eq(7, DailyRewards.StreakAfterClaim("2026-10-21T23:00:00Z", today, 7));
                Eq(1, DailyRewards.StreakAfterClaim("not-a-date", today, 3));
                True(DailyRewards.CanClaim("", today));
                False(DailyRewards.CanClaim("2026-10-22T00:01:00Z", today));
                True(DailyRewards.CanClaim("2026-10-21T23:59:00Z", today));
                True(DailyRewards.CanClaim("garbage", today));
            });
        }

        private static void RandomTests()
        {
            Run("deterministic random repeats exactly", () =>
            {
                var a = new DeterministicRandom(12345);
                var b = new DeterministicRandom(12345);
                for (int i = 0; i < 50; i++)
                {
                    Eq(a.NextUInt(), b.NextUInt(), "value " + i);
                    Eq(a.Next01(), b.Next01(), "float " + i);
                    int value = a.Range(3, 9);
                    Eq(value, b.Range(3, 9), "range int " + i);
                    True(value >= 3 && value < 9, "Range int bounds");
                    float f = a.Range(-2f, 2f);
                    Near(f, b.Range(-2f, 2f), 0.000001f, "range float " + i);
                    True(f >= -2f && f < 2f, "Range float bounds");
                }
            });

            Run("zero seed does not stall the generator", () =>
            {
                var rng = new DeterministicRandom(0);
                uint first = rng.NextUInt();
                uint second = rng.NextUInt();
                True(first != 0, "zero seed is replaced");
                True(first != second, "generator advances");
            });

            Run("range handles empty spans", () =>
            {
                var rng = new DeterministicRandom(5);
                Eq(4, rng.Range(4, 4));
                Eq(9, rng.Range(9, 3));
                Eq(7, rng.Range(7, 7));
            });

            Run("stable hash is stable and platform independent", () =>
            {
                int a = DeterministicRandom.StableHash("neon-velocity");
                int b = DeterministicRandom.StableHash("neon-velocity");
                int c = DeterministicRandom.StableHash("neon-velocitx");
                Eq(a, b);
                True(a != c, "hash should change with input");
                True(a >= 0, "hash must be non-negative");
            });
        }

        private static void MissionTests()
        {
            Run("daily missions are unique and deterministic", () =>
            {
                var first = MissionFactory.GenerateDaily("2026-10-22");
                var second = MissionFactory.GenerateDaily("2026-10-22");
                Eq(3, first.Count);
                Eq(first.Count, second.Count);
                var ids = new HashSet<string>();
                for (int i = 0; i < first.Count; i++)
                {
                    var mission = first[i];
                    Eq(mission.id, second[i].id);
                    Eq(mission.title, second[i].title);
                    Eq(mission.target, second[i].target);
                    Eq(mission.scope, second[i].scope);
                    Eq("daily", mission.scope);
                    Eq("2026-10-22", mission.expiresDateKey);
                    True(mission.id.Contains("2026-10-22"), "mission id should include the date");
                    True(mission.target > 0, "target should be positive");
                    True(mission.rewardCredits > 0, "credit reward should be positive");
                    True(mission.rewardXp > 0, "xp reward should be positive");
                    True(mission.description.Contains(mission.target.ToString(CultureInfo.InvariantCulture)),
                        "description should include the scaled target");
                    True(ids.Add(mission.id), "mission ids must be unique");
                }
            });

            Run("weekly missions are deterministic and change by week", () =>
            {
                var weekOne = MissionFactory.GenerateWeekly("2026-W43");
                var weekTwo = MissionFactory.GenerateWeekly("2026-W44");
                Eq(2, weekOne.Count);
                foreach (var mission in weekOne) Eq("weekly", mission.scope);
                True(weekOne.Exists(m => m.id.Contains("2026-W43")));
                True(weekTwo.Exists(m => m.id.Contains("2026-W44")));
            });

            Run("date keys use invariant culture formats", () =>
            {
                var utc = new DateTime(2026, 1, 5, 23, 59, 0, DateTimeKind.Utc);
                Eq("2026-01-05", MissionFactory.DailyKey(utc));
                Eq("2026-W02", MissionFactory.WeeklyKey(utc));
            });

            Run("achievements read the matching lifetime stats", () =>
            {
                var stats = new LifetimeStats
                {
                    totalRuns = 30,
                    totalDistance = 12345.9f,
                    totalCoins = 2000,
                    totalJumps = 700,
                    totalSlides = 50,
                    totalPowerUps = 120,
                    racesCompleted = 6,
                    nearMisses = 22
                };
                var distance = FindAchievement("distance_10k");
                True(distance.LifetimeProgress(stats) >= distance.target);
                var jumps = FindAchievement("jumps_500");
                Eq(700, jumps.LifetimeProgress(stats));
                var races = FindAchievement("races_5");
                Eq(6, races.LifetimeProgress(stats));
                var runs = FindAchievement("runs_25");
                Eq(30, runs.LifetimeProgress(stats));
                Eq(2000, FindAchievement("coins_1k").LifetimeProgress(stats));
                Eq(120, FindAchievement("powerups_100").LifetimeProgress(stats));
            });
        }

        private static AchievementDefinition FindAchievement(string id)
        {
            foreach (var achievement in MissionFactory.Achievements)
                if (achievement.id == id) return achievement;
            throw new InvalidOperationException("Missing achievement " + id);
        }

        private static void RaceTests()
        {
            Run("snapshot validation accepts plausible movement", () =>
            {
                True(RaceRules.IsPlausibleDelta(100f, 120f, 0.5f));
                True(RaceRules.IsPlausibleDelta(0f, 30f, 0.1f));
                True(RaceRules.IsPlausibleDelta(100f, 100f, 0f));
                True(RaceRules.IsPlausibleDelta(100f, 95f, 0.1f), "small float jitter is tolerated");
                False(RaceRules.IsPlausibleDelta(100f, 80f, 0.1f), "rewinds are rejected");
                False(RaceRules.IsPlausibleDelta(100f, 1000f, 0.1f), "teleports are rejected");
                False(RaceRules.IsPlausibleDelta(float.NaN, 10f, 0.1f));
                False(RaceRules.IsPlausibleDelta(10f, float.NaN, 0.1f));
                False(RaceRules.IsPlausibleDelta(100f, 200f, 0.1f, 45f, "dead"));
                True(RaceRules.IsPlausibleDelta(100f, 120f, 0.1f, 45f, "dead"));
            });

            Run("finish claims are sanity checked", () =>
            {
                True(RaceRules.IsPlausibleFinish(1000f, 30f));
                True(RaceRules.IsPlausibleFinish(0f, 0f));
                False(RaceRules.IsPlausibleFinish(4000f, 30f), "impossible average speed");
                False(RaceRules.IsPlausibleFinish(-1f, 30f));
                False(RaceRules.IsPlausibleFinish(10f, -1f));
                False(RaceRules.IsPlausibleFinish(30001f, 10000f));
                False(RaceRules.IsPlausibleFinish(float.NaN, 10f));
                False(RaceRules.IsPlausibleFinish(10f, float.NaN));
            });

            Run("ranking prefers distance, then finish, score and coins", () =>
            {
                var leader = new WirePlayer { name = "A", distance = 900f, score = 100, coins = 5 };
                var slower = new WirePlayer { name = "B", distance = 800f, score = 9999, coins = 999 };
                True(RaceRules.CompareRank(leader, slower) < 0, "distance is primary");

                var finished = new WirePlayer { name = "C", distance = 800f, finished = true };
                var notFinished = new WirePlayer { name = "D", distance = 800f, finished = false };
                True(RaceRules.CompareRank(finished, notFinished) < 0, "finish is the first tiebreak");

                var highScore = new WirePlayer { name = "E", distance = 800f, score = 500 };
                True(RaceRules.CompareRank(highScore, notFinished) < 0, "score breaks equal distance");

                var highCoins = new WirePlayer { name = "F", distance = 800f, score = 500, coins = 80 };
                var moreCoins = new WirePlayer { name = "G", distance = 800f, score = 500, coins = 90 };
                True(RaceRules.CompareRank(moreCoins, highCoins) < 0, "coins break score ties");

                var alpha = new WirePlayer { name = "Alpha", distance = 800f, score = 500, coins = 90 };
                var beta = new WirePlayer { name = "Beta", distance = 800f, score = 500, coins = 90 };
                True(RaceRules.CompareRank(alpha, beta) < 0, "name is the final stable tiebreak");
            });

            Run("sorting assigns contiguous placements", () =>
            {
                var players = new List<WirePlayer>
                {
                    new WirePlayer { name = "C", distance = 10f },
                    new WirePlayer { name = "A", distance = 30f },
                    new WirePlayer { name = "B", distance = 20f }
                };
                RaceRules.SortAndAssignRanks(players);
                Eq("A", players[0].name);
                Eq(1, players[0].placement);
                Eq(2, players[1].placement);
                Eq(3, players[2].placement);
                RaceRules.SortAndAssignRanks(null);
            });

            Run("placement rewards are ordered", () =>
            {
                Eq(500, RaceRules.PlacementRewardCredits(1, 5));
                Eq(320, RaceRules.PlacementRewardCredits(2, 5));
                Eq(220, RaceRules.PlacementRewardCredits(3, 5));
                Eq(150, RaceRules.PlacementRewardCredits(4, 5));
                Eq(100, RaceRules.PlacementRewardCredits(5, 5));
                Eq(120, RaceRules.PlacementRewardCredits(1, 1));
            });

            Run("display names are sanitised", () =>
            {
                Eq("RUNNER", RaceRules.SanitiseName(null));
                Eq("RUNNER", RaceRules.SanitiseName("   "));
                Eq("RUNNER", RaceRules.SanitiseName("!!!"));
                Eq("Kira-01", RaceRules.SanitiseName(" Kira-01 "));
                Eq("A B", RaceRules.SanitiseName("A B"));
                Eq(20, RaceRules.SanitiseName("ABCDEFGHIJKLMNOPQRSTUVWXYZ").Length);
                Eq("AB", RaceRules.SanitiseName("A@#$%^B"));
            });
        }

        private static void ModeTests()
        {
            Run("every game mode is present and uniquely defined", () =>
            {
                var ids = new HashSet<GameModeId>();
                Eq(11, GameModes.All.Count, "mode count");
                foreach (var mode in GameModes.All)
                {
                    True(ids.Add(mode.Id), "duplicate mode " + mode.Id);
                    False(string.IsNullOrEmpty(mode.Title));
                    False(string.IsNullOrEmpty(mode.Objective));
                    False(string.IsNullOrEmpty(mode.Rules));
                    Eq(true, !string.IsNullOrEmpty(mode.AccentHex));
                    True(mode.MaxPlayers >= 1 && mode.MaxPlayers <= 5);
                    if (mode.IsOnline)
                    {
                        Eq(5, mode.MaxPlayers, mode.Id + " online player cap");
                        True(mode.IsTimed, mode.Id + " must be timed");
                    }
                    else
                    {
                        Eq(1, mode.MaxPlayers, mode.Id + " offline player cap");
                    }
                }
            });

            Run("mode lookup is case-insensitive with a safe fallback", () =>
            {
                Eq(GameModeId.OnlineRace, GameModes.Get("onlinerace").Id);
                Eq(GameModeId.OnlineRace, GameModes.Get("OnlineRace").Id);
                Eq(GameModeId.Classic, GameModes.Get("nonsense").Id);
                Eq(GameModeId.DailyChallenge, GameModes.Get(GameModeId.DailyChallenge).Id);
                True(GameModes.IsOnline(GameModeId.OnlineRace));
                True(GameModes.IsOnline(GameModeId.PrivateRace));
                False(GameModes.IsOnline(GameModeId.Classic));
            });

            Run("offline practice and tutorial never end the run on a crash", () =>
            {
                var practice = GameModes.Get(GameModeId.Practice);
                False(practice.AllowsCrash);
                False(practice.CountsProgression);
                var tutorial = GameModes.Get(GameModeId.Tutorial);
                False(tutorial.AllowsCrash);
                False(tutorial.HasPowerUps);
                Eq(3, GameModes.Get(GameModeId.Survival).StartingLives);
            });

            Run("timed modes expose positive durations", () =>
            {
                foreach (var mode in GameModes.All)
                {
                    if (!mode.IsTimed) continue;
                    True(mode.DurationSeconds > 0f, mode.Id + " duration");
                }
                Eq(120f, GameModes.Get(GameModeId.OnlineRace).DurationSeconds);
                Eq(0f, GameModes.Get(GameModeId.Classic).DurationSeconds);
            });
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("PASS  " + name);
            }
            catch (Exception ex)
            {
                Failures.Add(name + ": " + ex.Message);
                Console.WriteLine("FAIL  " + name + " -> " + ex.Message);
            }
        }

        private static void Eq<T>(T expected, T actual, string context = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{context ?? "values differ"}: expected <{expected}>, got <{actual}>");
        }

        private static void Near(float expected, float actual, float tolerance, string context = null)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new Exception($"{context ?? "values differ"}: expected <{expected}> +/- {tolerance}, got <{actual}>");
        }

        private static void True(bool condition, string message = null)
        {
            if (!condition) throw new Exception(message ?? "expected true");
        }

        private static void False(bool condition, string message = null)
        {
            if (condition) throw new Exception(message ?? "expected false");
        }

        private static void NotNull(object value, string message = null)
        {
            if (value == null) throw new Exception(message ?? "expected non-null");
        }

        private static void Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new Exception($"expected {typeof(TException).Name}, got {ex.GetType().Name}");
            }
            throw new Exception($"expected {typeof(TException).Name}, no exception was thrown");
        }
    }
}
