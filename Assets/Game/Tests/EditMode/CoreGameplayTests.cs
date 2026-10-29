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
        public void ProgressionMath_MatchesAuthoredXpCurve()
        {
            long xp = 0;
            Assert.AreEqual(1, ProgressionMath.AddXp(ref xp, 500, out int level));
            Assert.AreEqual(2, level);
            Assert.AreEqual(500L, xp);
            Assert.AreEqual(1, ProgressionMath.AddXp(ref xp, 620, out level));
            Assert.AreEqual(3, level);
            Assert.AreEqual(1120L, xp);
            Assert.AreEqual(3, ProgressionMath.LevelFromXp(ProgressionMath.TotalXpForLevel(3)));
            Assert.AreEqual(4, ProgressionMath.LevelFromXp(ProgressionMath.TotalXpForLevel(3) + ProgressionMath.XpRequiredForLevel(3)));
            Assert.AreEqual(100, ProgressionMath.LevelFromXp(ProgressionMath.TotalXpForLevel(100) + 99999));
        }

        [Test]
        public void LevelProgression_UsesExactAuthoredThresholdsAndNeverLosesXp()
        {
            long[] expected = { 0L, 450L, 900L, 1_800L, 3_000L, 4_500L, 6_500L, 9_000L, 12_000L, 16_000L };
            Assert.AreEqual(expected.Length, LevelProgression.Thresholds.Length);
            for (int level = 1; level <= expected.Length; level++)
            {
                Assert.AreEqual(expected[level - 1], LevelProgression.TotalXpForLevel(level),
                    "Cumulative XP for level " + level);
                Assert.AreEqual(level, LevelProgression.LevelForXp(expected[level - 1]),
                    "Level at the exact threshold for " + level);
                if (level > 1)
                {
                    Assert.AreEqual(level - 1, LevelProgression.LevelForXp(expected[level - 1] - 1L),
                        "One XP below the level " + level + " threshold must stay at " + (level - 1));
                }
            }

            long total = 0L;
            int gained = LevelProgression.AddXp(ref total, 449L, out int reached);
            Assert.AreEqual(0, gained);
            Assert.AreEqual(1, reached);
            gained = LevelProgression.AddXp(ref total, 1L, out reached);
            Assert.AreEqual(1, gained, "The 450th XP must cross into level 2 exactly once");
            Assert.AreEqual(2, reached);
            Assert.AreEqual(450L, total, "No XP may be lost or double-counted at a threshold");
        }

        [Test]
        public void LevelProgression_ScalesBeyondTenAndCapsRequirements()
        {
            Assert.AreEqual(4_480L, LevelProgression.RequirementForLevel(10));
            Assert.AreEqual(16_000L + 4_480L, LevelProgression.TotalXpForLevel(11));
            Assert.AreEqual(11, LevelProgression.LevelForXp(LevelProgression.TotalXpForLevel(11)));
            Assert.AreEqual(10, LevelProgression.LevelForXp(LevelProgression.TotalXpForLevel(11) - 1L));
            Assert.That(LevelProgression.RequirementForLevel(12),
                Is.GreaterThan(LevelProgression.RequirementForLevel(11)),
                "Post-10 requirements must grow monotonically");
            Assert.AreEqual(LevelProgression.RequirementCap, LevelProgression.RequirementForLevel(1_000));
            Assert.AreEqual(500, LevelProgression.LevelForXp(LevelProgression.TotalXpForLevel(500)));
            Assert.AreEqual(1, LevelProgression.LevelForXp(-999L));
            Assert.AreEqual(LevelProgression.MaxLevel, LevelProgression.LevelForXp(long.MaxValue));
        }

        [Test]
        public void LocationCatalog_HasTwentyUniqueThemesOnThreeKilometreZones()
        {
            Assert.AreEqual(20, LocationCatalog.Count);
            Assert.AreEqual(20, LocationCatalog.All.Length);
            Assert.AreEqual(3_000f, LocationCatalog.ZoneLength);
            var ids = new HashSet<string>();
            var names = new HashSet<string>();
            for (int i = 0; i < LocationCatalog.All.Length; i++)
            {
                var location = LocationCatalog.All[i];
                Assert.IsTrue(ids.Add(location.id), "Duplicate location id: " + location.id);
                Assert.IsTrue(names.Add(location.displayName), "Duplicate location name: " + location.displayName);
                Assert.That(location.accentHex, Does.StartWith("#"), location.id + " accent colour");
                Assert.That(location.accentHex.Length, Is.EqualTo(7), location.id + " accent colour");
                Assert.That(location.skyTopHex, Does.StartWith("#"), location.id + " sky colour");
                Assert.That(location.skyTopHex.Length, Is.EqualTo(7), location.id + " sky colour");
                Assert.That(location.buildingBHex, Does.StartWith("#"), location.id + " building colour");
                Assert.That(location.buildingBHex.Length, Is.EqualTo(7), location.id + " building colour");
                Assert.That(location.style, Is.InRange(0, 20), location.id + " structural style");
                Assert.That(location.weather, Is.InRange(0, 5), location.id + " weather code");
                Assert.That(location.timeOfDay, Is.InRange(0, 6), location.id + " time of day");
                Assert.That(location.signature, Is.InRange(0, 19), location.id + " landmark signature");
                Assert.That(location.ambienceId, Is.Not.Empty, location.id + " ambience");
                Assert.That(location.roadPrefab, Is.Not.Empty, location.id + " road prefab key");
                Assert.That(location.environmentPrefab, Is.Not.Empty, location.id + " environment prefab key");
            }

            var themes = new HashSet<string>();
            var signatures = new HashSet<int>();
            foreach (var location in LocationCatalog.All)
            {
                Assert.IsTrue(themes.Add(location.themeName), "Duplicate theme: " + location.themeName);
                Assert.IsTrue(signatures.Add(location.signature),
                    "Two districts cannot share landmark signature " + location.signature);
            }
            Assert.AreEqual(20, signatures.Count, "Every district needs its own wired landmark builder");
            foreach (string required in LocationCatalog.RequiredThemeNames)
            {
                Assert.IsTrue(themes.Contains(required), "Missing required theme: " + required);
            }
            Assert.AreEqual(LocationCatalog.RequiredThemeNames.Length, themes.Count,
                "The library must contain exactly the twenty requested themes");

            Assert.AreEqual(0, LocationCatalog.ZoneIndexForDistance(0f));
            Assert.AreEqual(0, LocationCatalog.ZoneIndexForDistance(2_999.9f));
            Assert.AreEqual(1, LocationCatalog.ZoneIndexForDistance(3_000f));
            Assert.AreEqual(2, LocationCatalog.ZoneIndexForDistance(6_000f));
            Assert.AreEqual(5, LocationCatalog.ZoneIndexForDistance(15_000f));
            Assert.AreEqual(10, LocationCatalog.ZoneIndexForDistance(30_000f));
            Assert.AreEqual(0, LocationCatalog.IndexForDistance(60_000f),
                "Zone 20 must wrap back to the first location");
            Assert.AreEqual(0, LocationCatalog.IndexForDistance(-400f));
            Assert.AreEqual(LocationCatalog.All[0].id, LocationCatalog.Get(20).id);
            Assert.AreEqual(LocationCatalog.ZoneLength * 20, LocationCatalog.ZoneStart(20), 0.001f);
            Assert.IsTrue(LocationCatalog.SegmentCrossesBoundary(2_982f, 42f),
                "The 42 m segment that contains 3,000 m must be marked as a boundary");

            for (int zone = 0; zone < LocationCatalog.Count * 3; zone++)
            {
                Assert.AreNotEqual(LocationCatalog.Get(zone).id, LocationCatalog.Get(zone + 1).id,
                    "Locations must never repeat back-to-back, including across the wrap");
            }
        }

        [Test]
        public void TimeOfDayCatalog_HasSevenStatesAndSmoothDistanceCycle()
        {
            Assert.AreEqual(7, TimeOfDayCatalog.Count);
            Assert.AreEqual(7, TimeOfDayCatalog.All.Length);
            var ids = new HashSet<string>();
            var labels = new HashSet<string>();
            for (int i = 0; i < TimeOfDayCatalog.Count; i++)
            {
                var profile = TimeOfDayCatalog.Get((TimeOfDayPhase)i);
                Assert.AreEqual((TimeOfDayPhase)i, profile.Phase);
                Assert.IsTrue(ids.Add(profile.Id), "Duplicate time-of-day id: " + profile.Id);
                Assert.IsTrue(labels.Add(profile.Label), "Duplicate time-of-day label: " + profile.Label);
                Assert.That(profile.FogDensity, Is.GreaterThan(0f));
                Assert.That(profile.AmbientIntensity, Is.GreaterThan(0f));
            }

            Assert.AreEqual(TimeOfDayPhase.Evening, TimeOfDayCatalog.ForDistance(0f).Phase);
            Assert.AreEqual(TimeOfDayPhase.Sunset, TimeOfDayCatalog.ForDistance(3_000f).Phase);
            Assert.AreEqual(TimeOfDayPhase.Night, TimeOfDayCatalog.ForDistance(6_000f).Phase);
            Assert.AreEqual(TimeOfDayPhase.LateNight, TimeOfDayCatalog.ForDistance(9_000f).Phase);
            Assert.AreEqual(TimeOfDayPhase.EarlyMorning, TimeOfDayCatalog.ForDistance(12_000f).Phase);
            Assert.AreEqual(TimeOfDayPhase.Morning, TimeOfDayCatalog.ForDistance(15_000f).Phase);
            Assert.AreEqual(TimeOfDayPhase.Afternoon, TimeOfDayCatalog.ForDistance(18_000f).Phase);
            Assert.AreEqual(TimeOfDayPhase.Evening, TimeOfDayCatalog.ForDistance(21_000f).Phase);
            Assert.AreEqual(TimeOfDayPhase.LateNight, TimeOfDayCatalog.ForDistance(30_000f).Phase,
                "A 30 km run must advance the dynamic cycle through the full authored ring");

            var midnight = TimeOfDayCatalog.ForDistance(6_000f + 1_500f);
            Assert.AreEqual(TimeOfDayPhase.Night, midnight.Phase);
            Assert.AreEqual(TimeOfDayPhase.LateNight, midnight.NextPhase);
            Assert.That(midnight.PhaseBlend, Is.GreaterThan(0.4f).And.LessThan(0.6f),
                "The cycle must blend progressively rather than snap at the mid-point");

            Assert.AreEqual(TimeOfDayPhase.Sunset,
                TimeOfDayCatalog.Resolve(0, 2, 6_000f, (int)TimeOfDayPhase.Sunset).Phase);
            Assert.AreEqual(TimeOfDayPhase.Night,
                TimeOfDayCatalog.Resolve(1, 2, 6_000f, (int)TimeOfDayPhase.Sunset).Phase);
            Assert.AreEqual(TimeOfDayPhase.Afternoon,
                TimeOfDayCatalog.Resolve(2, 2, 6_000f, (int)TimeOfDayPhase.Sunset).Phase);
            Assert.AreEqual(TimeOfDayPhase.EarlyMorning,
                TimeOfDayCatalog.Resolve(2, -9, 6_000f, (int)TimeOfDayPhase.Sunset).Phase);
            var clamped = TimeOfDayCatalog.ForDistance(10_000f, 999_999f);
            Assert.That((int)clamped.Phase, Is.InRange(0, TimeOfDayCatalog.Count - 1),
                "An oversized cycle must clamp to a valid state");
            var shortestCycle = TimeOfDayCatalog.ForDistance(9_000f, 1f);
            Assert.That((int)shortestCycle.Phase, Is.InRange(0, TimeOfDayCatalog.Count - 1),
                "A cycle shorter than one zone must clamp to the minimum cycle");
        }

        [Test]
        public void JunctionScheduler_CyclesAllNineShapesWithFairSpacing()
        {
            const int seed = 20261028;
            int first = JunctionScheduler.FirstSegmentForSeed(seed);
            Assert.That(first, Is.GreaterThanOrEqualTo(JunctionRules.FirstJunctionSegment));
            var kinds = new HashSet<JunctionKind>();
            var plans = new List<JunctionPlan>();
            for (int slot = 0; slot < 9; slot++)
            {
                int segment = first + slot * JunctionRules.SpacingSegments;
                var plan = JunctionScheduler.ForSegment(segment, seed);
                Assert.IsNotNull(plan, "Missing junction at slot " + slot);
                Assert.AreEqual(segment, plan.SegmentIndex);
                Assert.That(kinds.Add(plan.Kind), Is.True, "Shape repeated before all eight were used");
                Assert.That(plan.DisplayName, Is.Not.Empty);
                Assert.That(plan.MergeRow, Is.GreaterThan(plan.BranchStartRow));
                Assert.That(plan.WorldStartZ, Is.EqualTo(segment * TrackDirector.SegmentLength).Within(0.001f));
                if (plan.HasCurvedRoute)
                {
                    Assert.That(plan.CurveMetres, Is.GreaterThan(0f), plan.Kind + " needs a curve");
                    Assert.That(Math.Abs(plan.CurveSign), Is.EqualTo(1), plan.Kind + " curve direction");
                }
                else Assert.AreEqual(0f, plan.CurveMetres, 0.0001f, plan.Kind + " must stay straight");
                plans.Add(plan);
            }
            Assert.AreEqual(9, kinds.Count, "The scheduler must expose all nine junction shapes");

            foreach (var plan in plans)
            {
                var rows = new TrackRow[TrackDirector.RowsPerSegment];
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = new TrackRow(CellKind.Empty, CellKind.Empty, CellKind.Empty);
                JunctionScheduler.ApplyRows(plan, rows);
                for (int row = 0; row < rows.Length; row++)
                {
                    int passable = 0;
                    for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                        if (!rows[row].IsSolid(lane)) passable++;
                    Assert.That(passable, Is.GreaterThan(0),
                        plan.Kind + " row " + row + " is a dead end");
                }
                if (plan.IsBrokenRoad)
                    for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                        Assert.AreEqual(CellKind.LowBarrier, rows[2][lane]);
                if (plan.RequiresSideChoice)
                {
                    for (int row = plan.BranchStartRow; row < plan.MergeRow; row++)
                    {
                        Assert.IsFalse(rows[row].IsSolid(0), plan.Kind + " left branch blocked");
                        Assert.IsFalse(rows[row].IsSolid(2), plan.Kind + " right branch blocked");
                        Assert.IsTrue(rows[row].IsSolid(1), plan.Kind + " median must be physical");
                    }
                }
            }
        }

        [Test]
        public void JunctionRoutes_StayOnTheDeckAndReturnToCentre()
        {
            var curved = new JunctionPlan
            {
                SegmentIndex = 10,
                Kind = JunctionKind.CurvedRoad,
                BranchStartRow = 1,
                MergeRow = 5,
                CurveSign = -1,
                CurveMetres = 1.25f
            };
            Assert.AreEqual(0f, JunctionRules.RouteOffsetAt(curved, 6.9f, 1), 0.0001f);
            Assert.AreEqual(0f, JunctionRules.RouteOffsetAt(curved, 35.1f, 1), 0.0001f);
            float peak = 0f;
            for (float z = 7f; z <= 35f; z += 0.5f)
            {
                float left = JunctionRules.RouteOffsetAt(curved, z, 0);
                float right = JunctionRules.RouteOffsetAt(curved, z, 2);
                Assert.That(Math.Abs(left), Is.LessThanOrEqualTo(1.2501f), "Curve left the deck at " + z);
                Assert.That(Math.Abs(right), Is.LessThanOrEqualTo(1.2501f), "Curve left the deck at " + z);
                Assert.AreEqual(left, right, 0.0001f, "A curved road bends every lane together");
                peak = Math.Max(peak, Math.Abs(left));
                Assert.IsFalse(float.IsNaN(JunctionRules.RouteSlopeAt(curved, z, 1)));
            }
            Assert.That(peak, Is.GreaterThan(1.1f), "The bend must actually displace the road");

            var tee = new JunctionPlan
            {
                SegmentIndex = 10,
                Kind = JunctionKind.TJunction,
                BranchStartRow = 2,
                MergeRow = 5,
                CurveMetres = 1.15f
            };
            Assert.That(JunctionRules.RouteOffsetAt(tee, 21f, 0), Is.LessThan(-0.8f));
            Assert.That(JunctionRules.RouteOffsetAt(tee, 21f, 2), Is.GreaterThan(0.8f));
            Assert.AreEqual(0f, JunctionRules.RouteOffsetAt(tee, 21f, 1), 0.0001f);

            var shortcut = new JunctionPlan
            {
                SegmentIndex = 10,
                Kind = JunctionKind.Shortcut,
                BranchStartRow = 2,
                MergeRow = 5,
                RewardOnLeft = false,
                CurveMetres = 1.45f
            };
            Assert.That(JunctionRules.RouteOffsetAt(shortcut, 21f, 2), Is.GreaterThan(1.0f));
            Assert.AreEqual(0f, JunctionRules.RouteOffsetAt(shortcut, 21f, 0), 0.0001f);
            Assert.AreEqual(0f, JunctionRules.RouteOffsetAt(shortcut, 21f, 1), 0.0001f);
        }

        [Test]
        public void JunctionCommit_NeverTrapsTheCentreLaneAndResolvesPhases()
        {
            var tee = new JunctionPlan
            {
                SegmentIndex = 10,
                Kind = JunctionKind.TJunction,
                CommitRow = 1,
                BranchStartRow = 2,
                BranchEndRow = 4,
                MergeRow = 5,
                RewardOnLeft = true
            };
            Assert.AreEqual(JunctionBranch.Left, JunctionRules.CommitForLane(0, 0, tee));
            Assert.AreEqual(JunctionBranch.Right, JunctionRules.CommitForLane(2, 0, tee));
            Assert.AreEqual(JunctionBranch.Left, JunctionRules.CommitForLane(1, -1, tee));
            Assert.AreEqual(JunctionBranch.Right, JunctionRules.CommitForLane(1, 1, tee));
            Assert.AreEqual(JunctionBranch.Left, JunctionRules.CommitForLane(1, 0, tee),
                "A centre runner with no steering history must still get a safe branch");
            Assert.AreEqual(0, JunctionRules.ClampLane(2, tee, JunctionPhase.Branch, JunctionBranch.Left));
            Assert.AreEqual(0, JunctionRules.ClampLane(1, tee, JunctionPhase.Branch, JunctionBranch.Left));
            Assert.AreEqual(2, JunctionRules.ClampLane(0, tee, JunctionPhase.Branch, JunctionBranch.Right));

            Assert.AreEqual(126f, JunctionRules.WarningDistanceMetres, 0.001f);
            float warningStart = tee.WorldCommitZ - JunctionRules.WarningDistanceMetres;
            Assert.AreEqual(JunctionPhase.None, JunctionRules.Resolve(tee, warningStart - 1f).Phase);
            Assert.AreEqual(JunctionPhase.Warning, JunctionRules.Resolve(tee, warningStart).Phase);
            Assert.AreEqual(JunctionPhase.Commit, JunctionRules.Resolve(tee, tee.WorldCommitZ).Phase);
            Assert.AreEqual(JunctionPhase.Branch, JunctionRules.Resolve(tee, tee.WorldBranchStartZ).Phase);
            Assert.AreEqual(JunctionPhase.Merge, JunctionRules.Resolve(tee, tee.WorldMergeZ).Phase);
            Assert.AreEqual(JunctionPhase.None, JunctionRules.Resolve(tee, tee.WorldEndZ).Phase);

            var curved = new JunctionPlan { Kind = JunctionKind.CurvedRoad };
            Assert.AreEqual(JunctionBranch.None, JunctionRules.CommitForLane(1, 1, curved));
            Assert.IsTrue(JunctionRules.LaneAllowed(0, curved, JunctionPhase.Branch, JunctionBranch.None));
            Assert.IsTrue(JunctionRules.LaneAllowed(2, curved, JunctionPhase.Branch, JunctionBranch.None));
        }

        [Test]
        public void GeneratedTrack_JunctionsAreConnectedAcrossLongRuns([Values(1, 77, 424242)] int seed)
        {
            var segments = new TrackDirector(seed, 1009).BuildRange(0, 300);
            int junctions = 0;
            var kinds = new HashSet<JunctionKind>();
            int lastIndex = int.MinValue;
            foreach (var segment in segments)
            {
                var junction = segment.Junction;
                if (junction == null) continue;
                junctions++;
                kinds.Add(junction.Kind);
                if (lastIndex != int.MinValue)
                    Assert.That(segment.Index - lastIndex, Is.GreaterThanOrEqualTo(JunctionRules.SpacingSegments),
                        "Junctions must keep at least 630 m of fair spacing");
                lastIndex = segment.Index;
                for (int row = 0; row < segment.Rows.Length; row++)
                {
                    int passable = 0;
                    for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                        if (!segment.Rows[row].IsSolid(lane)) passable++;
                    Assert.That(passable, Is.GreaterThan(0),
                        "seed " + seed + " junction " + segment.Index + " row " + row + " is blocked");
                }
            }
            Assert.That(junctions, Is.GreaterThanOrEqualTo(16),
                "A 12.6 km run must contain enough junctions to exercise every shape");
            Assert.AreEqual(9, kinds.Count, "All junction shapes must appear in a long run");
        }

        [Test]
        public void LocationCatalog_DiscoveryIsRememberedPerLocation()
        {
            var discovered = new List<string>();
            Assert.IsTrue(LocationCatalog.IsNewDiscovery(3, discovered));
            discovered.Add(LocationCatalog.Get(3).id);
            Assert.IsFalse(LocationCatalog.IsNewDiscovery(3, discovered));
            Assert.IsFalse(LocationCatalog.IsNewDiscovery(3 + LocationCatalog.Count, discovered),
                "Re-entering the same district is not a new discovery");
            Assert.IsTrue(LocationCatalog.IsNewDiscovery(4, discovered));
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

        [Test]
        public void HazardRules_OnlyUseFullWidthEmptyRows()
        {
            var blocked = new TrackRow[TrackDirector.RowsPerSegment];
            for (int i = 0; i < blocked.Length; i++)
                blocked[i] = new TrackRow(CellKind.Empty, CellKind.Empty, CellKind.Empty);
            blocked[3] = new TrackRow(CellKind.Solid, CellKind.Empty, CellKind.Empty);
            Assert.IsFalse(HazardRules.CanHostSignatureHazard(blocked, 2),
                "A hazard may not sit next to a blocked row");
            Assert.IsFalse(HazardRules.CanHostSignatureHazard(blocked, 3));
            Assert.IsFalse(HazardRules.CanHostSignatureHazard(blocked, 4));
            Assert.AreEqual(1, HazardRules.FindSignatureRow(blocked),
                "Only the row before the blocked trio remains fair");

            int hosts = 0;
            var segments = new TrackDirector(77, 1009).BuildRange(0, 715);
            foreach (var segment in segments)
            {
                if (!segment.IsEvent || segment.Junction != null) continue;
                int row = HazardRules.FindSignatureRow(segment.Rows);
                if (row < 0) continue;
                hosts++;
                Assert.That(row, Is.InRange(HazardRules.FirstCandidateRow, HazardRules.LastCandidateRow));
                Assert.IsTrue(HazardRules.CanHostSignatureHazard(segment.Rows, row),
                    "Segment " + segment.Index + " selected an unfair signature row");
                for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                {
                    Assert.AreEqual(CellKind.Empty, segment.Rows[row - 1][lane]);
                    Assert.AreEqual(CellKind.Empty, segment.Rows[row][lane]);
                    Assert.AreEqual(CellKind.Empty, segment.Rows[row + 1][lane]);
                }
            }
            Assert.That(hosts, Is.GreaterThanOrEqualTo(20),
                "A 30 km run must expose enough fair signature-hazard slots to exercise every event kind");
        }

        [Test]
        public void GeneratedTrack_ThirtyKilometresKeepsLocationAndJunctionContracts()
        {
            foreach (int metres in new[] { 3_000, 6_000, 9_000, 15_000, 30_000 })
            {
                int count = (int)Math.Ceiling(metres / (double)TrackDirector.SegmentLength) + 2;
                var segments = new TrackDirector(505, 0).BuildRange(0, count);
                Assert.AreEqual(count, segments.Count);
                Assert.That(GeneratedTrackValidator.Validate(segments), Is.Empty, metres + " m");

                int junctions = 0;
                int lastJunction = int.MinValue;
                var kinds = new HashSet<JunctionKind>();
                for (int i = 0; i < segments.Count; i++)
                {
                    var segment = segments[i];
                    Assert.AreEqual(LocationCatalog.IndexForDistance(segment.WorldStartZ),
                        segment.BiomeIndex, "Biome must follow the zone at " + segment.WorldStartZ + " m");
                    var junction = segment.Junction;
                    if (junction == null) continue;
                    junctions++;
                    kinds.Add(junction.Kind);
                    if (lastJunction != int.MinValue)
                        Assert.That(segment.Index - lastJunction, Is.GreaterThanOrEqualTo(JunctionRules.SpacingSegments),
                            "Junctions must stay at least 630 m apart on a " + metres + " m run");
                    lastJunction = segment.Index;
                    for (int row = 0; row < segment.Rows.Length; row++)
                    {
                        int passable = 0;
                        for (int lane = 0; lane < LaneMath.LaneCount; lane++)
                            if (!segment.Rows[row].IsSolid(lane)) passable++;
                        Assert.That(passable, Is.GreaterThan(0),
                            metres + " m junction " + segment.Index + " row " + row + " is blocked");
                    }
                }

                int zones = metres / (int)LocationCatalog.ZoneLength;
                for (int zone = 1; zone <= zones; zone++)
                {
                    int boundary = zone * (int)LocationCatalog.ZoneLength;
                    int first = (int)Math.Ceiling(boundary / (double)TrackDirector.SegmentLength);
                    Assert.AreEqual(LocationCatalog.Get(zone).id, LocationCatalog.Get(segments[first].BiomeIndex).id,
                        "Zone " + zone + " must begin on the first segment at or after " + boundary + " m");
                    Assert.AreEqual(LocationCatalog.Get(zone - 1).id, LocationCatalog.Get(segments[first - 1].BiomeIndex).id,
                        "The segment before zone " + zone + " must still belong to the previous location");
                }

                int minimumJunctions = Math.Max(1, count / 20);
                Assert.That(junctions, Is.GreaterThanOrEqualTo(minimumJunctions),
                    metres + " m must contain connected junctions");
                if (metres >= 15_000)
                    Assert.AreEqual(9, kinds.Count, "A long run must exercise every junction shape");
            }
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
