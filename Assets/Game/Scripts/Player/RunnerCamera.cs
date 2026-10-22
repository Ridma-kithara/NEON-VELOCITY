using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonVelocity.Player
{
    /// <summary>
    /// Third-person chase camera with speed-reactive framing, smoothed lane tracking and
    /// accessibility-aware shake. Also owns the game's post-processing profile.
    /// </summary>
    public sealed class RunnerCamera : MonoBehaviour
    {
        public Camera Camera { get; private set; }
        public Volume Volume { get; private set; }
        private Transform _target;
        private RunnerController _runner;
        private float _shake;
        private float _time;
        private bool _reducedMotion;
        private bool _shakeEnabled;
        private float _fovBase = 59f;
        private float _graphicsScale = 2f;
        private Color _neon = Palette.Cyan;
        private Bloom _bloom;
        private Vignette _vignette;
        private ChromaticAberration _chroma;
        private MotionBlur _motionBlur;
        private FilmGrain _filmGrain;

        public static RunnerCamera Create(Transform parent, RunnerController runner, bool reducedMotion, bool shakeEnabled,
            float graphicsScale)
        {
            var go = new GameObject("Gameplay Camera");
            go.transform.SetParent(parent, false);
            go.tag = "MainCamera";
            var rig = go.AddComponent<RunnerCamera>();
            rig.Build(runner, reducedMotion, shakeEnabled, graphicsScale);
            return rig;
        }

        private void Build(RunnerController runner, bool reducedMotion, bool shakeEnabled, float graphicsScale)
        {
            _runner = runner;
            _reducedMotion = reducedMotion;
            _shakeEnabled = shakeEnabled;
            _graphicsScale = graphicsScale;
            Camera = gameObject.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Palette.Hex("#06101F");
            Camera.nearClipPlane = 0.09f;
            Camera.farClipPlane = 620f;
            Camera.allowHDR = true;
            Camera.allowMSAA = true;
            Camera.fieldOfView = _fovBase;
            gameObject.AddComponent<AudioListener>();
            gameObject.transform.position = new Vector3(0f, 3.8f, -7.2f);

            var data = Camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = graphicsScale >= 2f
                ? AntialiasingMode.SubpixelMorphologicalAntiAliasing
                : (graphicsScale >= 1f ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.None);
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = graphicsScale >= 1f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "NV Gameplay Post";
            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);
            _bloom = profile.Add<Bloom>(true);
            _bloom.intensity.Override(graphicsScale >= 1f ? 0.56f : 0.3f);
            _bloom.threshold.Override(0.96f);
            _bloom.scatter.Override(0.66f);
            _bloom.highQualityFiltering.Override(graphicsScale >= 2f);
            _vignette = profile.Add<Vignette>(true);
            _vignette.intensity.Override(0.37f);
            _vignette.smoothness.Override(0.56f);
            _vignette.color.Override(Palette.Hex("#03050B"));
            _chroma = profile.Add<ChromaticAberration>(true);
            _chroma.intensity.Override(reducedMotion ? 0.025f : 0.085f);
            var grade = profile.Add<ColorAdjustments>(true);
            grade.postExposure.Override(0.18f);
            grade.contrast.Override(8f);
            grade.saturation.Override(5f);
            grade.colorFilter.Override(new Color(0.94f, 0.98f, 1f));
            _filmGrain = profile.Add<FilmGrain>(true);
            _filmGrain.type.Override(FilmGrainLookup.Thin1);
            _filmGrain.intensity.Override(reducedMotion ? 0f : (graphicsScale >= 2f ? 0.16f : 0.08f));
            _filmGrain.response.Override(0.74f);
            _motionBlur = profile.Add<MotionBlur>(true);
            _motionBlur.mode.Override(MotionBlurMode.CameraOnly);
            _motionBlur.quality.Override(MotionBlurQuality.Medium);
            _motionBlur.intensity.Override(reducedMotion || graphicsScale < 2f ? 0f : 0.16f);
            _motionBlur.clamp.Override(0.045f);
            var volumeGo = new GameObject("Gameplay Volume");
            volumeGo.transform.SetParent(transform, false);
            Volume = volumeGo.AddComponent<Volume>();
            Volume.isGlobal = true;
            Volume.priority = 5f;
            Volume.profile = profile;
        }

        public void SetTarget(Transform target) => _target = target;

        public void Shake(float amount)
        {
            if (!_shakeEnabled || _reducedMotion) return;
            _shake = Mathf.Min(0.7f, _shake + amount);
        }

        public void SetBiomeColor(Color color)
        {
            _neon = color;
            if (_chroma != null) _chroma.intensity.Override(_reducedMotion ? 0.025f : 0.085f);
        }

        private void LateUpdate()
        {
            if (_runner == null) return;
            float dt = Time.unscaledDeltaTime;
            _time += dt;
            float speedNorm = _runner.SpeedNormalised;
            Vector3 runnerPos = _runner.transform.position;
            float z = runnerPos.z;

            // Lower, closer chase framing with a slight shoulder offset reads more like a film
            // camera and lets the wet road and rain pass through the foreground.
            float shoulder = 0.45f + Mathf.Sin(_time * 0.17f) * 0.12f;
            float x = Mathf.Lerp(transform.position.x, runnerPos.x * 0.74f + shoulder, 1f - Mathf.Exp(-dt * 8f));
            float height = 3.58f + speedNorm * 0.32f;
            float back = 7.35f + speedNorm * 1.25f;
            Vector3 desired = new Vector3(x, height, z - back);
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-dt * 7f));

            Vector3 lookAt = new Vector3(runnerPos.x * 0.24f, runnerPos.y + 1.36f, z + 17f + speedNorm * 6f);
            Quaternion rotation = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
            float roll = _reducedMotion ? 0f : Mathf.Clamp((runnerPos.x - x) * -2.1f, -2.8f, 2.8f);
            rotation *= Quaternion.Euler(0f, 0f, roll);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, 1f - Mathf.Exp(-dt * 6f));

            if (_shake > 0.001f)
            {
                float amplitude = _shake * 0.2f;
                transform.position += new Vector3(
                    (Mathf.PerlinNoise(_time * 28f, 0.4f) - 0.5f) * amplitude,
                    (Mathf.PerlinNoise(0.2f, _time * 31f) - 0.5f) * amplitude,
                    0f);
                _shake = Mathf.Max(0f, _shake - dt * 2.8f);
            }

            float targetFov = _fovBase + speedNorm * 9f + (_runner.IsSliding ? 2f : 0f);
            if (_reducedMotion) targetFov = _fovBase + speedNorm * 3f;
            Camera.fieldOfView = Mathf.Lerp(Camera.fieldOfView, targetFov, 1f - Mathf.Exp(-dt * 3f));
            if (_bloom != null) _bloom.intensity.Override(_reducedMotion ? 0.32f : (0.46f + speedNorm * 0.26f));
            if (_vignette != null) _vignette.intensity.Override(_runner.IsDead ? 0.64f : 0.32f + speedNorm * 0.11f);
            if (_motionBlur != null && !_reducedMotion && _graphicsScale >= 2f)
                _motionBlur.intensity.Override(0.1f + speedNorm * 0.22f);
        }
    }
}
