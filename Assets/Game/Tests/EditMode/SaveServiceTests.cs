using System;
using System.IO;
using NeonVelocity.Core;
using NeonVelocity.Persistence;
using NUnit.Framework;

namespace NeonVelocity.Tests
{
    /// <summary>
    /// EditMode coverage for atomic save writes and backup recovery, isolated in a temp folder so
    /// tests never touch the player's real profile.
    /// </summary>
    public sealed class SaveServiceTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "NeonVelocityTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            }
            catch (IOException)
            {
                // A failed cleanup must not mask a test assertion.
            }
        }

        [Test]
        public void SaveAndReload_RoundTripsProgression()
        {
            var first = new SaveService(_directory);
            first.Data.credits = 4321;
            first.Data.playerName = "TEST-RUNNER";
            first.Data.xp = 1500;
            first.Data.level = ProgressionMath.LevelFromXp(1500);
            first.Data.unlockedCharacters.Add("nova");
            first.Data.selectedCharacter = "nova";
            first.Data.settings.musicVolume = 0.25f;
            first.Save();

            Assert.That(File.Exists(first.SavePath), Is.True);
            var second = new SaveService(_directory);
            Assert.AreEqual(4321, second.Data.credits);
            Assert.AreEqual("TEST-RUNNER", second.Data.playerName);
            Assert.AreEqual(1500, second.Data.xp);
            Assert.AreEqual(ProgressionMath.LevelFromXp(1500), second.Data.level);
            Assert.AreEqual("nova", second.Data.selectedCharacter);
            Assert.That(second.Data.unlockedCharacters, Does.Contain("nova"));
            Assert.AreEqual(0.25f, second.Data.settings.musicVolume, 0.0001f);
        }

        [Test]
        public void CorruptPrimary_RecoversBackup()
        {
            var service = new SaveService(_directory);
            service.Data.credits = 1111;
            service.Save();
            service.Data.credits = 2222;
            service.Save();
            Assert.AreEqual(2222, service.Data.credits);

            File.WriteAllText(service.SavePath, "{ this is not valid json");
            var recovered = new SaveService(_directory);
            Assert.AreEqual(1111, recovered.Data.credits);
            Assert.That(File.Exists(recovered.SavePath), Is.True);
        }

        [Test]
        public void ResetToDefault_ProducesPlayableProfile()
        {
            var service = new SaveService(_directory);
            service.Data.credits = 99;
            service.Data.playerName = "TEMP";
            service.ResetToDefault();
            Assert.AreNotEqual("TEMP", service.Data.playerName);
            Assert.That(service.Data.credits, Is.GreaterThanOrEqualTo(0));
            Assert.AreEqual("kira", service.Data.selectedCharacter);
            Assert.IsFalse(string.IsNullOrEmpty(service.Data.playerId));
        }

        [Test]
        public void ExportJson_CanBeImportedByCloudRestore()
        {
            var source = new SaveService(_directory);
            source.Data.credits = 777;
            source.Data.playerName = "CLOUD-RUNNER";
            source.Data.xp = 2000;
            string json = source.ExportJson();

            string otherDirectory = Path.Combine(_directory, "cloud");
            Directory.CreateDirectory(otherDirectory);
            var target = new SaveService(otherDirectory);
            target.ImportCloudJson(json);
            Assert.AreEqual(777, target.Data.credits);
            Assert.AreEqual("CLOUD-RUNNER", target.Data.playerName);
            Assert.AreEqual(2000, target.Data.xp);
        }
    }
}
