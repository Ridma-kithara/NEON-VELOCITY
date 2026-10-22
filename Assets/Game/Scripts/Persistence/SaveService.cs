using System;
using System.IO;
using NeonVelocity.Core;
using UnityEngine;

namespace NeonVelocity.Persistence
{
    /// <summary>
    /// Versioned local save with atomic writes, backup recovery and explicit migration.
    /// Local data is never considered authoritative for online competition.
    /// </summary>
    public sealed class SaveService
    {
        private const string FileName = "neon_velocity_save.json";
        private readonly string _path;
        private readonly string _backupPath;
        public SaveData Data { get; private set; }
        public string SavePath => _path;
        public event Action Saved;

        public SaveService(string storageDirectory = null)
        {
            _path = Path.Combine(
                string.IsNullOrEmpty(storageDirectory) ? Application.persistentDataPath : storageDirectory,
                FileName);
            _backupPath = _path + ".bak";
            Load();
        }

        public void Load()
        {
            SaveData loaded = TryRead(_path);
            if (loaded == null) loaded = TryRead(_backupPath);
            if (loaded == null && File.Exists(_path))
            {
                string quarantine = _path + ".corrupt." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                try { File.Move(_path, quarantine); } catch (Exception e) { Debug.LogWarning("[Save] Could not quarantine corrupt save: " + e.Message); }
                Debug.LogWarning("[Save] Corrupt save quarantined. A fresh profile was created.");
            }
            Data = loaded ?? CreateDefault();
            SaveMigration.Normalise(Data);
            if (Data.schemaVersion != SaveData.CurrentSchemaVersion)
            {
                Debug.Log($"[Save] Migrating schema {Data.schemaVersion} -> {SaveData.CurrentSchemaVersion}");
                Data.schemaVersion = SaveData.CurrentSchemaVersion;
            }
            Save();
        }

        private SaveData CreateDefault()
        {
            var data = new SaveData
            {
                credits = Mathf.Max(0, GameConfig.Current.startingCredits),
                playerId = Guid.NewGuid().ToString("N"),
                playerName = "RUNNER-" + UnityEngine.Random.Range(100, 999),
                lastPlayedUtc = DateTime.UtcNow.ToString("O")
            };
            return data;
        }

        private SaveData TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var parsed = JsonUtility.FromJson<SaveData>(json);
                if (parsed == null) return null;
                if (parsed.schemaVersion > SaveData.CurrentSchemaVersion)
                {
                    Debug.LogWarning("[Save] Save was created by a newer build; attempting forward-compatible load.");
                }
                return parsed;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] Failed reading " + path + ": " + e.Message);
                return null;
            }
        }

        public void Save()
        {
            try
            {
                SaveMigration.Normalise(Data);
                Data.lastPlayedUtc = DateTime.UtcNow.ToString("O");
                string json = JsonUtility.ToJson(Data, true);
                string temp = _path + ".tmp";
                File.WriteAllText(temp, json);
                if (File.Exists(_path))
                {
                    try { File.Replace(temp, _path, _backupPath); }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(_path, _backupPath, true);
                        File.Copy(temp, _path, true);
                        File.Delete(temp);
                    }
                    catch (IOException)
                    {
                        File.Copy(_path, _backupPath, true);
                        File.Copy(temp, _path, true);
                        File.Delete(temp);
                    }
                }
                else
                {
                    File.Move(temp, _path);
                }
                Saved?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError("[Save] Write failed: " + e.Message);
            }
        }

        public void ResetToDefault()
        {
            Data = CreateDefault();
            Save();
        }

        public void ImportCloudJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                var cloud = JsonUtility.FromJson<SaveData>(json);
                if (cloud == null) return;
                SaveMigration.Normalise(cloud);
                // Never overwrite a newer local progression with an older cloud copy.
                if (cloud.xp + cloud.credits * 0.2f >= Data.xp + Data.credits * 0.2f)
                {
                    int localCredits = Data.credits;
                    Data = cloud;
                    Save();
                    Debug.Log($"[Save] Cloud profile imported (local credits were {localCredits}).");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] Cloud import failed: " + e.Message);
            }
        }

        public string ExportJson() => JsonUtility.ToJson(Data, false);
    }
}
