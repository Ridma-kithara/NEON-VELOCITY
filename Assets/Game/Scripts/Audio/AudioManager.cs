using System.Collections;
using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;

namespace NeonVelocity.Audio
{
    /// <summary>
    /// Three-bus audio manager (music, ambience, SFX) with pooled one-shots, loop crossfades
    /// and anti-spam cooldowns. Clips load from Resources/Audio; missing clips fall back to
    /// generated placeholder tones so the game is always audible.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        private sealed class Bus
        {
            public AudioSource Source;
            public float TargetVolume;
            public string ClipName;
        }

        private readonly List<AudioSource> _sfxPool = new List<AudioSource>();
        private readonly List<AudioSource> _spatialPool = new List<AudioSource>();
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
        private Bus _music;
        private Bus _ambience;
        private SettingsData _settings;
        private Coroutine _musicFade;
        private Coroutine _ambienceFade;
        private int _nextSfx;
        private int _nextSpatial;
        private static readonly string[] WetFootsteps = { "sfx_step_wet_1", "sfx_step_wet_2" };

        public void Initialise(SettingsData settings)
        {
            _settings = settings;
            for (int i = 0; i < 10; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.volume = 0.8f;
                _sfxPool.Add(source);
            }
            var spatialRoot = new GameObject("Spatial One-Shots");
            spatialRoot.transform.SetParent(transform, false);
            for (int i = 0; i < 6; i++)
            {
                var source = spatialRoot.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 5f;
                source.maxDistance = 110f;
                source.dopplerLevel = 0.55f;
                _spatialPool.Add(source);
            }
            _music = new Bus { Source = gameObject.AddComponent<AudioSource>() };
            _ambience = new Bus { Source = gameObject.AddComponent<AudioSource>() };
            foreach (var bus in new[] { _music, _ambience })
            {
                bus.Source.playOnAwake = false;
                bus.Source.loop = true;
                bus.Source.spatialBlend = 0f;
            }
            LoadClips();
            ApplyVolumes();
        }

        private void LoadClips()
        {
            string[] names =
            {
                "music_menu", "music_run", "ambience_city", "ambience_skyway", "ambience_reactor",
                "ambience_tunnel", "ambience_rain", "sfx_jump", "sfx_land", "sfx_slide", "sfx_coin",
                "sfx_shard", "sfx_hit", "sfx_powerup", "sfx_click", "sfx_countdown", "sfx_go",
                "sfx_victory", "sfx_defeat", "sfx_nearmiss", "sfx_daily", "sfx_step_wet_1",
                "sfx_step_wet_2", "sfx_step_metal", "sfx_lane", "sfx_thunder", "sfx_train_pass"
            };
            foreach (string name in names)
            {
                AudioClip clip = Resources.Load<AudioClip>("Audio/" + name);
                if (clip == null) clip = GenerateFallback(name);
                _clips[name] = clip;
            }
        }

        private static AudioClip GenerateFallback(string name)
        {
            int sampleRate = 22050;
            float duration = name.StartsWith("music") ? 8f : (name.StartsWith("ambience") ? 6f : 0.24f);
            int samples = Mathf.CeilToInt(sampleRate * duration);
            var data = new float[samples];
            float frequency = name switch
            {
                "sfx_jump" => 540f,
                "sfx_land" => 180f,
                "sfx_slide" => 240f,
                "sfx_coin" => 1040f,
                "sfx_shard" => 1500f,
                "sfx_hit" => 110f,
                "sfx_powerup" => 680f,
                "sfx_click" => 880f,
                "sfx_countdown" => 440f,
                "sfx_go" => 880f,
                "sfx_victory" => 760f,
                "sfx_defeat" => 140f,
                "sfx_nearmiss" => 1200f,
                "sfx_step_wet_1" => 150f,
                "sfx_step_wet_2" => 130f,
                "sfx_step_metal" => 190f,
                "sfx_lane" => 420f,
                "sfx_thunder" => 72f,
                "sfx_train_pass" => 105f,
                _ => 320f
            };
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sampleRate;
                float env = Mathf.Exp(-t * (name.StartsWith("music") ? 0.02f : 9f));
                float value = Mathf.Sin(2f * Mathf.PI * frequency * t) * env * 0.25f;
                if (name.StartsWith("music")) value += Mathf.Sin(2f * Mathf.PI * frequency * 0.5f * t) * env * 0.12f;
                data[i] = value;
            }
            var clip = AudioClip.Create("fallback_" + name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public void ApplyVolumes()
        {
            if (_settings == null) return;
            if (_music != null) _music.Source.volume = _settings.musicVolume * _music.TargetVolume;
            if (_ambience != null) _ambience.Source.volume = _settings.ambienceVolume * _ambience.TargetVolume;
        }

        public void SetSettings(SettingsData settings)
        {
            _settings = settings;
            ApplyVolumes();
        }

        public void PlayMusic(string clipName, float volume = 0.55f, float fadeSeconds = 1.2f)
        {
            if (_music == null || _music.ClipName == clipName) return;
            if (!_clips.TryGetValue(clipName, out var clip) || clip == null) return;
            _music.ClipName = clipName;
            _music.TargetVolume = volume;
            if (_musicFade != null) StopCoroutine(_musicFade);
            _music.Source.clip = clip;
            _music.Source.loop = true;
            _music.Source.Play();
            _musicFade = StartCoroutine(FadeBus(_music, volume * _settings.musicVolume, fadeSeconds));
        }

        public void PlayAmbience(string clipName, float volume = 0.5f, float fadeSeconds = 1.5f)
        {
            if (_ambience == null || _ambience.ClipName == clipName) return;
            if (!_clips.TryGetValue(clipName, out var clip) || clip == null) return;
            _ambience.ClipName = clipName;
            _ambience.TargetVolume = volume;
            if (_ambienceFade != null) StopCoroutine(_ambienceFade);
            _ambience.Source.clip = clip;
            _ambience.Source.loop = true;
            _ambience.Source.Play();
            _ambienceFade = StartCoroutine(FadeBus(_ambience, volume * _settings.ambienceVolume, fadeSeconds));
        }

        public void StopMusic(float fadeSeconds = 0.8f)
        {
            if (_music == null) return;
            _music.ClipName = null;
            if (_musicFade != null) StopCoroutine(_musicFade);
            _musicFade = StartCoroutine(FadeBus(_music, 0f, fadeSeconds, stop: true));
        }

        private IEnumerator FadeBus(Bus bus, float target, float duration, bool stop = false)
        {
            float start = bus.Source.volume;
            if (duration <= 0.01f) { bus.Source.volume = target; yield break; }
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                bus.Source.volume = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            bus.Source.volume = target;
            if (stop) bus.Source.Stop();
        }

        public void PlaySfx(string clipName, float volumeScale = 1f, float minInterval = 0.04f)
        {
            if (_settings == null || _clips == null) return;
            if (!_clips.TryGetValue(clipName, out var clip) || clip == null) return;
            float now = Time.unscaledTime;
            if (_lastPlayed.TryGetValue(clipName, out float last) && now - last < minInterval) return;
            _lastPlayed[clipName] = now;
            var source = _sfxPool[_nextSfx++ % _sfxPool.Count];
            source.pitch = Random.Range(0.97f, 1.03f);
            source.PlayOneShot(clip, Mathf.Clamp01(_settings.sfxVolume * volumeScale));
        }

        /// <summary>
        /// Spatial one-shot for thunder, train pass-bys and other world events. Sources live in
        /// a small pool so repeated effects never allocate GameObjects during a run.
        /// </summary>
        public void PlaySfxAt(string clipName, Vector3 position, float volumeScale = 1f,
            float minDistance = 5f, float maxDistance = 110f)
        {
            if (_settings == null || _spatialPool.Count == 0) return;
            if (!_clips.TryGetValue(clipName, out var clip) || clip == null) return;
            var source = _spatialPool[_nextSpatial++ % _spatialPool.Count];
            source.transform.position = position;
            source.minDistance = Mathf.Max(0.5f, minDistance);
            source.maxDistance = Mathf.Max(source.minDistance, maxDistance);
            source.pitch = Random.Range(0.96f, 1.04f);
            source.PlayOneShot(clip, Mathf.Clamp01(_settings.sfxVolume * volumeScale));
        }

        public void PlayFootstep(bool metal, float volumeScale = 0.34f, int stepIndex = 0)
        {
            string clip = metal
                ? "sfx_step_metal"
                : WetFootsteps[Mathf.Abs(stepIndex) % WetFootsteps.Length];
            PlaySfx(clip, volumeScale, 0.055f);
        }

        public void PlayUiClick() => PlaySfx("sfx_click", 0.55f, 0.03f);
        public void PlayCountdown(bool go) => PlaySfx(go ? "sfx_go" : "sfx_countdown", 0.8f, 0f);

        public void SetMusicVolume(float value)
        {
            _settings.musicVolume = Mathf.Clamp01(value);
            ApplyVolumes();
        }

        public void SetSfxVolume(float value) => _settings.sfxVolume = Mathf.Clamp01(value);
        public void SetAmbienceVolume(float value)
        {
            _settings.ambienceVolume = Mathf.Clamp01(value);
            ApplyVolumes();
        }

        public void PauseForMenu()
        {
            if (_music != null) _music.Source.volume = _settings.musicVolume * _music.TargetVolume * 0.55f;
        }

        public void ResumeFromMenu() => ApplyVolumes();
    }
}
