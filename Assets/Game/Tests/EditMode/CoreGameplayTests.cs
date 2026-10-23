using System;
using System.Collections.Generic;
using System.Text;
using NeonVelocity.Core;
using NUnit.Framework;

namespace NeonVelocity.Tests
{
    /// <summary>
    /// Unity EditMode coverage for the deterministic gameplay core. These tests do not need a
    /// running scene and can be executed from Window > General > Test Runner > EditMode.
    /// </summary>
    public sealed class CoreGameplayTests
    {
        [Test]
        public void LaneMath_ClampsAndInterpolates()
        {
            Assert.AreEqual(0, LaneMath.ClampLane(-4));
            Assert.AreEqual(2, LaneMath.ClampLane(9));
            Assert.AreEqual(0, LaneMath.MoveLane(0, -1));
            Assert.AreEqual(2, LaneMath.MoveLane(2, 1));
            Assert.AreEqual(1, LaneMath.MoveLane(1, 0));
            Assert.AreEqual(-2.7f, LaneMath.LaneToX(0), 0.0001f);
            Assert.AreEqual(0f, LaneMath.Swing(-2.7f, 2.7f, -2.7f, 0.2f, 0.1f), 0.0001f);
            Assert.AreEqual(2.7f, LaneMath.Swing(-2.7f, 2.7f, -2.7f, 0.2f, 0.2f), 0.0001f);
        }

        [Test]
        public void AuthoredPatternLibrary_IsFullyNavigable()
        {
            Assert.That(TrackPatternLibrary.All.Length, Is.GreaterThanOrEqualTo(15));
            var ids = new HashSet<string>();
            foreach (var pattern in TrackPatternLibrary.All)
            {
                Assert.That(ids.Add(pattern.Id), Is.True, "Duplicate pattern id " + pattern.Id);
                Assert.That(TrackValidator.Validate(pattern), Is.Empty, pattern.Id + " failed validation");
                Assert.That(TrackValidator.HasLanePath(pattern), Is.True, pattern.Id + " has no route");
            }
        }

        [Test]
        public void Validator_RejectsBlockedRowsAndImpossibleSpacing()
        {
            var blocked = new TrackPattern("blocked", 0, 7f, false, "XXX", "...");
            Assert.That(TrackValidator.Validate(blocked), Is.Not.Empty);
            Assert.That(TrackValidator.HasLanePath(
                new TrackPattern("dead-end", 0, 7f, false, "X.X", "XXX", "...")), Is.False);

            var tooClose = new TrackPattern("close", 1, 7f, false, "LLL", "LLL", "...");
            Assert.That(TrackValidator.Validate(tooClose),
                Has.Some.Contains("too close"), "Full-width hurdles need at least 30m spacing");

            Assert.Throws<FormatException>(() => TrackPattern.ParseRow("..Z"));
            Assert.Throws<FormatException>(() => TrackPattern.ParseRow(".."));
        }

        [Test]
        public void GeneratedTrack_OpensSafelyAndValidates([Values(1, 77, 424242, -90)] int seed)
        {
            var director = new TrackDirector(seed, 1009);
            var segments = director.BuildRange(0, 64);
            Assert.AreEqual(64, segments.Count);
            Assert.That(GeneratedTrackValidator.Validate(segments), Is.Empty, "seed " + seed);

            for (int i = 0; i < TrackDirector.SafeOpeningSegments; i++)
            {
                Assert.That(segments[i].IsSafe, Is.True);
                Assert.AreEqual(i * TrackDirector.SegmentLength, segments[i].WorldStartZ, 0.001f);
                foreach (var row in segments[i].Rows)
                {
                    Assert.AreEqual(CellKind.Empty, row.Left);
                    Assert.AreEqual(CellKind.Empty, row.Centre);
                    Assert.AreEqual(CellKind.Empty, row.Right);
                }
            }
        }

        [Test]
        public void GeneratedTrack_IsDeterministicPerSeed()
        {
            var first = new TrackDirector(314159, 0).BuildRange(0, 60);
            var second = new TrackDirector(314159, 0).BuildRange(0, 60);
            Assert.AreEqual(Describe(first), Describe(second));
        }

        [Test]
        public void GeneratedTrack_OpensInNeonSprawlForEverySeed()
        {
            foreach (int seed in new[] { 1, 77, 424242, -90 })
            {
                var segments = new TrackDirector(seed, 1009).BuildRange(0, 12);
                for (int i = 0; i < 8; i++)
                    Assert.AreEqual(0, segments[i].BiomeIndex,
                        "seed " + seed + " segment " + i + " must open in Neon Sprawl");
            }
        }

        [Test]
        public void Settings_ClampsHostileDisplayAndQualityValues()
        {
            var settings = new SettingsData
            {
                graphicsPreset = 99,
                targetFrameRate = 999,
                screenWidth = 9000,
                screenHeight = -4,
                fullscreenMode = 8,
                renderScale = 2.5f,
                textureQuality = -5,
                shadowQuality = 12,
                reflectionQuality = -2,
                antiAliasing = 7,
                effectsQuality = 9,
                viewDistance = -1,
                serverPort = 0,
                serverHost = "   "
            };

            SaveMigration.ClampSettings(settings);

            Assert.AreEqual(4, settings.graphicsPreset);
            Assert.AreEqual(60, settings.targetFrameRate);
            Assert.AreEqual(0, settings.screenWidth);
            Assert.AreEqual(0, settings.screenHeight);
            Assert.AreEqual(2, settings.fullscreenMode);
            Assert.AreEqual(1f, settings.renderScale, 0.0001f);
            Assert.AreEqual(0, settings.textureQuality);
            Assert.AreEqual(3, settings.shadowQuality);
            Assert.AreEqual(0, settings.reflectionQuality);
            Assert.AreEqual(4, settings.antiAliasing);
            Assert.AreEqual(2, settings.effectsQuality);
            Assert.AreEqual(0, settings.viewDistance);
            Assert.AreEqual(7777, settings.serverPort);
            Assert.AreEqual("127.0.0.1", settings.serverHost);
        }

        [Test]
        public void Settings_LowPresetIsDeterministicAndComplete()
        {
            var settings = new SettingsData { graphicsPreset = 0 };
            GameApp.ApplyPresetDefaults(settings);
            Assert.AreEqual(0.67f, settings.renderScale, 0.0001f);
            Assert.AreEqual(2, settings.textureQuality);
            Assert.AreEqual(0, settings.shadowQuality);
            Assert.AreEqual(0, settings.reflectionQuality);
            Assert.AreEqual(0, settings.antiAliasing);
            Assert.AreEqual(0, settings.effectsQuality);
            Assert.AreEqual(0, settings.viewDistance);
        }

        [Test]
        public void Settings_UltraPresetUsesFullQuality()
        {
            var settings = new SettingsData { graphicsPreset = 3 };
            GameApp.ApplyPresetDefaults(settings);
            Assert.AreEqual(1f, settings.renderScale, 0.0001f);
            Assert.AreEqual(0, settings.textureQuality);
            Assert.AreEqual(3, settings.shadowQuality);
            Assert.AreEqual(2, settings.reflectionQuality);
            Assert.AreEqual(4, settings.antiAliasing);
            Assert.AreEqual(2, settings.effectsQuality);
            Assert.AreEqual(2, settings.viewDistance);
        }

        [Test]
        public void Settings_CustomPresetPreservesChosenValues()
        {
            var settings = new SettingsData
            {
                graphicsPreset = 4,
                renderScale = 0.5f,
                textureQuality = 1,
                shadowQuality = 1,
                reflectionQuality = 0,
                antiAliasing = 8,
                effectsQuality = 1,
                viewDistance = 0
            };
            GameApp.ApplyPresetDefaults(settings);
            Assert.AreEqual(0.5f, settings.renderScale, 0.0001f);
            Assert.AreEqual(1, settings.textureQuality);
            Assert.AreEqual(1, settings.shadowQuality);
            Assert.AreEqual(0, settings.reflectionQuality);
            Assert.AreEqual(8, settings.antiAliasing);
            Assert.AreEqual(1, settings.effectsQuality);
            Assert.AreEqual(0, settings.viewDistance);
        }

        [Test]
        public void GeneratedTrack_VariesBySeedAndModeSalt()
        {
            var baseline = Describe(new TrackDirector(200, 0).BuildRange(0, 50));
            var otherSeed = Describe(new TrackDirector(201, 0).BuildRange(0, 55));
            var otherSalt = Describe(new TrackDirector(200, GameModes.Get(GameModeId.DailyChallenge).SeedSalt)
                .BuildRange(0, 50));
            Assert.AreNotEqual(baseline, otherSeed);
            Assert.AreNotEqual(baseline, otherSalt);
        }

        [Test]
        public void MissionFactory_ProducesThreeUniqueDailyMissions()
        {
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int day = 0; day < 120; day++)
            {
                var date = start.AddDays(day);
                var missions = MissionFactory.GenerateDaily(MissionFactory.DailyKey(date));
                Assert.AreEqual(3, missions.Count, "day " + day);
                var ids = new HashSet<string>();
                foreach (var mission in missions)
                {
                    Assert.That(ids.Add(mission.id), Is.True, "duplicate mission on day " + day);
                    Assert.AreEqual("daily", mission.scope);
                    Assert.Greater(mission.target, 0);
                    Assert.Greater(mission.rewardCredits, 0);
                    Assert.IsTrue(mission.description.Contains(mission.target.ToString(), StringComparison.Ordinal));
                }
            }
        }

        [Test]
        public void SaveMigration_NormalisesHostileData()
        {
            var data = new SaveData
            {
                playerName = null,
                credits = -100,
                xp = -50,
                level = 0,
                selectedCharacter = "unknown",
                unlockedCharacters = null,
                unlockedTrails = null,
                upgrades = null,
                redeemedRewardIds = null,
                lifetime = null,
                settings = null,
                missions = null
            };
            SaveMigration.Normalise(data);
            Assert.AreEqual("RUNNER-01", data.playerName);
            Assert.AreEqual(0, data.credits);
            Assert.AreEqual(0, data.xp);
            Assert.AreEqual(1, data.level);
            Assert.AreEqual("kira", data.selectedCharacter);
            Assert.IsNotNull(data.unlockedCharacters);
            Assert.IsNotNull(data.unlockedTrails);
            Assert.IsNotNull(data.upgrades);
            Assert.IsNotNull(data.redeemedRewardIds);
            Assert.IsNotNull(data.lifetime);
            Assert.IsNotNull(data.settings);
            Assert.IsNotNull(data.missions);
            Assert.IsTrue(data.unlockedCharacters.Contains("kira"));
        }

        [Test]
        public void ProgressionMath_MatchesLinearXpCurve()
        {
            int xp = 0;
            Assert.AreEqual(1, ProgressionMath.AddXp(ref xp, 500, out int level));
            Assert.AreEqual(2, level);
            Assert.AreEqual(500, xp);
            Assert.AreEqual(1, ProgressionMath.AddXp(ref xp, 620, out level));
            Assert.AreEqual(3, level);
            Assert.AreEqual(3, ProgressionMath.LevelFromXp(ProgressionMath.TotalXpForLevel(3)));
            Assert.AreEqual(4, ProgressionMath.LevelFromXp(ProgressionMath.TotalXpForLevel(3) + ProgressionMath.XpRequiredForLevel(3)));
            Assert.AreEqual(100, ProgressionMath.LevelFromXp(ProgressionMath.TotalXpForLevel(100) + 99999));
        }

        [Test]
        public void RaceRules_RankByDistanceThenFinishState()
        {
            var players = new List<WirePlayer>
            {
                new WirePlayer { name = "B", distance = 800f, score = 9999 },
                new WirePlayer { name = "A", distance = 900f },
                new WirePlayer { name = "C", distance = 800f, finished = true, score = 1 }
            };
            RaceRules.SortAndAssignRanks(players);
            Assert.AreEqual("A", players[0].name);
            Assert.AreEqual("C", players[1].name);
            Assert.AreEqual("B", players[2].name);
            Assert.AreEqual(1, players[0].placement);
            Assert.AreEqual(3, players[2].placement);
        }

        [Test]
        public void RaceRules_RejectImpossibleSnapshotsAndFinishes()
        {
            Assert.IsTrue(RaceRules.IsPlausibleDelta(100f, 120f, 0.5f));
            Assert.IsFalse(RaceRules.IsPlausibleDelta(100f, 1200f, 0.1f));
            Assert.IsFalse(RaceRules.IsPlausibleDelta(100f, 40f, 0.1f));
            Assert.IsFalse(RaceRules.IsPlausibleDelta(float.NaN, 10f, 0.1f));
            Assert.IsTrue(RaceRules.IsPlausibleFinish(1000f, 30f));
            Assert.IsFalse(RaceRules.IsPlausibleFinish(20000f, 30f));
            Assert.IsFalse(RaceRules.IsPlausibleFinish(-10f, 3f));
        }

        [Test]
        public void SanitiseName_IsSafeForTheScoreboard()
        {
            Assert.AreEqual("RUNNER", RaceRules.SanitiseName("   "));
            Assert.AreEqual("RUNNER", RaceRules.SanitiseName("@@@"));
            Assert.AreEqual("Kira-01", RaceRules.SanitiseName(" Kira-01 "));
            Assert.AreEqual(20, RaceRules.SanitiseName(new string('A', 40)).Length);
        }

        private static string Describe(List<SegmentPlan> segments)
        {
            var sb = new StringBuilder();
            foreach (var segment in segments)
            {
                sb.Append(segment.PatternId).Append('|');
                foreach (var row in segment.Rows)
                    sb.Append(row.Left).Append(row.Centre).Append(row.Right).Append('/');
                sb.Append(';');
            }
            return sb.ToString();
        }
    }
}
