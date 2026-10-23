using NeonVelocity.Core;
using NeonVelocity.Player;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Small off-screen turntable used by the runner garage. It renders the same procedural
    /// RunnerVisuals used in play through a dedicated layer and camera, so the preview cannot
    /// leak into the world render and the main game keeps its normal camera stack.
    /// </summary>
    public sealed class GaragePreview : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const int PreviewLayer = 30;

        private GameObject _stage;
        private Camera _camera;
        private RenderTexture _texture;
        private Transform _turntable;
        private RunnerVisuals _runner;
        private RunnerPose _pose = RunnerPose.Idle;
        private bool _dragging;

        public void Initialise(GameApp app, CharacterDefinition character, TrailDefinition trail, RawImage target)
        {
            BuildStage();
            BuildLights();
            BuildRunner(app, character, trail);
            BuildCamera();
            if (target != null) target.texture = _texture;
        }

        public void SetPose(RunnerPose pose)
        {
            _pose = pose;
            if (_runner != null) _runner.SetPose(pose);
        }

        private void BuildStage()
        {
            // Far outside every gameplay/menu camera frustum so only the preview camera sees it.
            _stage = new GameObject("NV Garage Preview Stage");
            _stage.transform.position = new Vector3(2400f, -2400f, 2400f);

            var floorMaterial = MaterialLibrary.Lit(new Color(0.025f, 0.045f, 0.075f), 0.88f, 0.9f);
            var metalMaterial = MaterialLibrary.Lit(new Color(0.045f, 0.075f, 0.12f), 0.92f, 0.86f);
            var platformTopMaterial = MaterialLibrary.Lit(new Color(0.012f, 0.025f, 0.045f), 0.4f, 0.97f);
            var accentMaterial = MaterialLibrary.Lit(Palette.Cyan, 0.2f, 0.75f, Palette.Cyan, 3.5f);
            var magentaMaterial = MaterialLibrary.Lit(Palette.Magenta, 0.2f, 0.75f, Palette.Magenta, 2.8f);
            var backdropMaterial = MaterialLibrary.Lit(new Color(0.012f, 0.024f, 0.045f), 0.3f, 0.45f);

            PrimitiveMeshes.Box(_stage.transform, "Backdrop", new Vector3(0f, 1.7f, 2.4f),
                new Vector3(4.6f, 3.4f, 0.14f), backdropMaterial);
            for (int side = -1; side <= 1; side += 2)
            {
                PrimitiveMeshes.Box(_stage.transform, "LightBar", new Vector3(side * 1.95f, 1.7f, 2.27f),
                    new Vector3(0.035f, 2.9f, 0.02f), side < 0 ? accentMaterial : magentaMaterial);
                PrimitiveMeshes.Box(_stage.transform, "LightFrame", new Vector3(side * 1.65f, 1.7f, 2.25f),
                    new Vector3(0.02f, 2.4f, 0.02f), metalMaterial);
            }

            PrimitiveMeshes.Create("Platform", PrimitiveMeshes.Cylinder, metalMaterial, _stage.transform,
                new Vector3(0f, 0.025f, 0.12f), new Vector3(2.35f, 0.035f, 2.35f));
            PrimitiveMeshes.Create("PlatformGlow", PrimitiveMeshes.Cylinder, accentMaterial, _stage.transform,
                new Vector3(0f, 0.062f, 0.12f), new Vector3(2.08f, 0.008f, 2.08f));
            PrimitiveMeshes.Create("PlatformTop", PrimitiveMeshes.Cylinder, platformTopMaterial, _stage.transform,
                new Vector3(0f, 0.068f, 0.12f), new Vector3(1.99f, 0.006f, 1.99f));
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                PrimitiveMeshes.Box(_stage.transform, "PlatformMark" + i,
                    new Vector3(Mathf.Sin(angle) * 0.88f, 0.07f, 0.12f + Mathf.Cos(angle) * 0.88f),
                    new Vector3(0.13f, 0.008f, 0.02f), i % 2 == 0 ? accentMaterial : magentaMaterial);
            }
            PrimitiveMeshes.Create("Floor", PrimitiveMeshes.Cylinder, floorMaterial, _stage.transform,
                new Vector3(0f, -0.06f, 0.12f), new Vector3(4.4f, 0.03f, 4.4f));
            PrimitiveMeshes.Box(_stage.transform, "RearGlow", new Vector3(0f, 0.08f, 1.7f),
                new Vector3(2.2f, 0.02f, 0.025f), magentaMaterial);

            _turntable = new GameObject("Turntable").transform;
            _turntable.SetParent(_stage.transform, false);
            _turntable.localPosition = new Vector3(0f, 0.08f, 0.12f);
            // Runner art faces +Z; the preview camera sits on the -Z side, so start the
            // turntable showing the face and chest rather than the backpack.
            _turntable.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        private void BuildLights()
        {
            CreateLight("Key", new Vector3(2.4f, 3.1f, -2.1f), new Color(1f, 0.95f, 0.88f), 6.2f, 8f);
            CreateLight("Cyan Fill", new Vector3(-2.6f, 1.8f, -1.4f), Palette.Cyan, 3.0f, 6.5f);
            CreateLight("Magenta Rim", new Vector3(0.2f, 2.5f, 2.6f), Palette.Magenta, 3.8f, 7.5f);
        }

        private void CreateLight(string name, Vector3 localPosition, Color color, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_stage.transform, false);
            go.transform.localPosition = localPosition;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            // Preview layer only, so shop lighting can never contaminate a run or menu.
            light.cullingMask = 1 << PreviewLayer;
        }

        private void BuildRunner(GameApp app, CharacterDefinition character, TrailDefinition trail)
        {
            if (app == null || character == null) return;
            var runnerGo = new GameObject("Garage Runner");
            runnerGo.transform.SetParent(_turntable, false);
            _runner = runnerGo.AddComponent<RunnerVisuals>();
            _runner.Build(character, trail, false, character.callsign);
            _runner.SetPose(RunnerPose.Idle);
            PrimitiveMeshes.SetLayerRecursive(_stage, PreviewLayer);
        }

        private void BuildCamera()
        {
            int size = SystemInfo.graphicsMemorySize >= 2048 ? 768 : 512;
            _texture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                name = "NV_GaragePreview",
                filterMode = FilterMode.Bilinear,
                antiAliasing = 4
            };
            _texture.Create();

            var cameraGo = new GameObject("Garage Preview Camera");
            cameraGo.transform.SetParent(_stage.transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, 1.18f, -4.15f);
            cameraGo.transform.LookAt(_stage.transform.position + new Vector3(0f, 1.02f, 0.12f),
                Vector3.up);
            _camera = cameraGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.008f, 0.018f, 0.035f, 1f);
            _camera.cullingMask = 1 << PreviewLayer;
            _camera.fieldOfView = 34f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 18f;
            _camera.allowHDR = false;
            _camera.allowMSAA = true;
            _camera.targetTexture = _texture;
            var data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = true;
        }

        private void Update()
        {
            if (_turntable == null) return;
            if (!_dragging) _turntable.Rotate(Vector3.up, 18f * Time.unscaledDeltaTime, Space.Self);
            if (_runner != null)
                _runner.Tick(Time.unscaledDeltaTime, _pose == RunnerPose.Idle ? 0.15f : 0.55f, 0f, _pose);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_turntable != null) _turntable.Rotate(Vector3.up, -eventData.delta.x * 0.45f, Space.Self);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
        }

        private void OnDestroy()
        {
            if (_camera != null)
            {
                _camera.targetTexture = null;
                Destroy(_camera.gameObject);
                _camera = null;
            }
            if (_stage != null)
            {
                Destroy(_stage);
                _stage = null;
            }
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
                _texture = null;
            }
        }
    }
}
