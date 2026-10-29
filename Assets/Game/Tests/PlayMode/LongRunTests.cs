using System.Collections;
using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Persistence;
using NeonVelocity.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVelocity.Tests
{
    /// <summary>
    /// Long-distance streaming coverage. The pure EditMode suites prove the deterministic
    /// catalogue; this test drives the real TrackManager through 30 km so segment release,
    /// pooling, district transitions and junction windows are exercised together.
    /// </summary>
    public sealed class LongRunTests
    {
        private static IEnumerator WaitForApp()
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (GameApp.Instance == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsNotNull(GameApp.Instance, "GameApp did not bootstrap");
            yield return null;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator TrackStreamsThirtyKilometresWithinBoundedPoolsAndZones()
        {
            yield return WaitForApp();
            var app = GameApp.Instance;
            app.ShowMainMenu();
            yield return null;
            app.StartMode(GameModeId.Practice);
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((!app.IsInRun || app.Session == null || app.Session.Track == null) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(app.IsInRun, "Practice session did not start");

            var session = app.Session;
            var track = session.Track;
            session.SetPaused(true);
            yield return null;

            var zones = new List<int>();
            track.LocationChanged += zones.Add;
            var junctionSegments = new HashSet<int>();
            int maxActive = 0;
            int maxObstacles = 0;
            int maxPickups = 0;
            int lastDifficulty = 0;
            int checkpoints = 0;
            int[] checkpointDistances = { 3_000, 6_000, 9_000, 15_000, 30_000 };
            const float step = TrackManager.SegmentLength;
            const int steps = 715; // 30,030 m, one segment past the 30 km mark.

            for (int i = 0; i <= steps; i++)
            {
                float distance = i * step;
                track.Tick(distance);
                maxActive = Mathf.Max(maxActive, track.ActiveSegmentCount);
                maxObstacles = Mathf.Max(maxObstacles, track.PooledObstacleCount);
                maxPickups = Mathf.Max(maxPickups, track.PooledPickupCount);

                Assert.That(track.ActiveSegmentCount, Is.LessThanOrEqualTo(14),
                    "Streaming must release old segments instead of growing without bound");
                Assert.That(track.ActiveSegmentCount, Is.GreaterThanOrEqualTo(6),
                    "Streaming must keep enough segments visible at " + distance + " m");
                Assert.AreEqual(track.ActiveSegmentCount * 6, track.PooledObstacleCount,
                    "Hazard budget must stay proportional to the active window");
                Assert.AreEqual(track.ActiveSegmentCount * 24, track.PooledPickupCount,
                    "Pickup budget must stay proportional to the active window");

                if (track.GetJunctionState(distance, out var state) && state.Plan != null)
                    junctionSegments.Add(state.Plan.SegmentIndex);

                int difficulty = TrackDirector.DifficultyForIndex(i);
                Assert.That(difficulty, Is.GreaterThanOrEqualTo(lastDifficulty),
                    "Difficulty may never regress during a run");
                lastDifficulty = difficulty;

                if (checkpoints < checkpointDistances.Length &&
                    distance >= checkpointDistances[checkpoints])
                {
                    int expectedZone = LocationCatalog.ZoneIndexForDistance(distance);
                    Assert.AreEqual(expectedZone, track.CurrentLocation,
                        "District must follow the 3 km zone at " + distance + " m");
                    Assert.IsNotEmpty(track.CurrentLocationName);
                    checkpoints++;
                }

                if (i % 25 == 0) yield return null;
            }

            Assert.AreEqual(checkpointDistances.Length, checkpoints,
                "The stream did not reach every long-run checkpoint");
            Assert.AreEqual(10, LocationCatalog.ZoneIndexForDistance(30_000f));
            Assert.AreEqual(10, track.CurrentLocation, "30 km must resolve to zone 10");
            Assert.That(zones.Count, Is.GreaterThanOrEqualTo(10),
                "A 30 km stream must announce every district transition");
            Assert.AreEqual(10, zones[zones.Count - 1]);
            Assert.That(junctionSegments.Count, Is.GreaterThanOrEqualTo(40),
                "A 30 km stream must expose dozens of connected junctions");
            Assert.That(maxActive, Is.LessThanOrEqualTo(14), "Active segment window grew unbounded");
            Assert.That(maxObstacles, Is.LessThanOrEqualTo(14 * 6));
            Assert.That(maxPickups, Is.LessThanOrEqualTo(14 * 24));
            Assert.AreEqual(8, lastDifficulty, "30 km must reach the maximum difficulty tier");

            // Exercise the same save path used at the end of a real run without changing the
            // player's persisted total: a round-trip must accept the live profile at 30 km.
            app.SaveProfile();
            var reloaded = new SaveService();
            Assert.IsNotNull(reloaded.Data);
            Assert.AreEqual(app.Profile.settings.timeOfDayMode, reloaded.Data.settings.timeOfDayMode);
            Assert.AreEqual(app.Profile.settings.timeOfDayPhase, reloaded.Data.settings.timeOfDayPhase);
            Assert.AreEqual(app.Profile.settings.timeOfDayCycleMetres, reloaded.Data.settings.timeOfDayCycleMetres);

            session.SetPaused(false);
            app.EndSession(false);
            yield return null;
        }
    }
}
