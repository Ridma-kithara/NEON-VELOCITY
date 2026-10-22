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
        private float _fovBase = 62f;
        private Color _neon = Palette.Cyan;
        private Bloom _bloom;
        private Vignette _vignette;
        private ChromaticAberration _chroma;

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
            Camera = gameObject.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Palette.Hex("#06101F");
            Camera.nearClipPlane = 0.12f;
            Camera.farClipPlane = 420f;
            Camera.allowHDR = true;
            Camera.allowMSAA = true;
            Camera.fieldOfView = _fovBase;
            gameObject.AddComponent<AudioListener>();
            gameObject.transform.position = new Vector3(0f, 4.2f, -6.4f);

            var data = Camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = graphicsScale >= 2f ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
            data.antialiasingQuality = AntialiasingQuality.Medium;
            data.renderShadows = graphicsScale >= 1f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "NV Gameplay Post";
            _bloom = profile.Add<Bloom>(true);
            _bloom.intensity.Override(graphicsScale >= 1f ? 0.9f : 0.35f);
            _bloom.threshold.Override(0.82f);
            _bloom.scatter.Override(0.68f);
            _vignette = profile.Add<Vignette>(true);
            _vignette.intensity.Override(0.34f);
            _vignette.smoothness.Override(0.52f);
            _vignette.color.Override(Palette.Hex("#03050B"));
            _chroma = profile.Add<ChromaticAberration>(true);
            _chroma.intensity.Override(0.13f);
            var grade = profile.Add<ColorAdjustments>(true);
            grade.postExposure.Override(0.12f);
            grade.contrast.Override(14f);
            grade.saturation.Override(17f);
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
            if (_chroma != null) _chroma.intensity.Override(_reducedMotion ? 0.04f : 0.13f);
        }

        private void LateUpdate()
        {
            if (_runner == null) return;
            float dt = Time.unscaledDeltaTime;
            _time += dt;
            float speedNorm = _runner.SpeedNormalised;
            Vector3 runnerPos = _runner.transform.position;
            float z = runnerPos.z;
            float x = Mathf.Lerp(transform.position.x, runnerPos.x * 0.62f, 1f - Mathf.Exp(-dt * 8f));
            float height = 4.15f + speedNorm * 0.35f;
            float back = 6.7f + speedNorm * 1.1f;
            Vector3 desired = new Vector3(x, height, z - back);
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-dt * 7f));

            Vector3 lookAt = new Vector3(runnerPos.x * 0.25f, runnerPos.y + 1.45f, z + 15f + speedNorm * 6f);
            Quaternion rotation = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, 1f - Mathf.Exp(-dt * 6f));

            if (_shake > 0.001f)
            {
                float amplitude = _shake * 0.22f;
                transform.position += new Vector3(
                    (Mathf.PerlinNoise(_time * 28f, 0.4f) - 0.5f) * amplitude,
                    (Mathf.PerlinNoise(0.2f, _time * 31f) - 0.5f) * amplitude,
                    0f);
                _shake = Mathf.Max(0f, _shake - dt * 2.8f);
            }

            float targetFov = _fovBase + speedNorm * 10f + (_runner.IsSliding ? 2f : 0f);
            if (_reducedMotion) targetFov = _fovBase + speedNorm * 3f;
            Camera.fieldOfView = Mathf.Lerp(Camera.fieldOfView, targetFov, 1f - Mathf.Exp(-dt * 3f));
            if (_bloom != null) _bloom.intensity.Override(_reducedMotion ? 0.45f : (0.8f + speedNorm * 0.5f));
            if (_vignette != null) _vignette.intensity.Override(_runner.IsDead ? 0.6f : 0.3f + speedNorm * 0.12f);
        }
    }
}
