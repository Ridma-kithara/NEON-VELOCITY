using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;

namespace NeonVelocity.World
{
    public enum ObstacleKind
    {
        JumpBarrier,
        OverheadLaser,
        CargoCrate,
        HoverDrone,
        ElectricPad,
        MovingBlocker,
        RotatingBar,
        LaserWall
    }

    /// <summary>
    /// Pure kinematic hazard with an explicit axis-aligned hitbox. No rigidbodies and no
    /// frame-dependent physics; the runner performs swept checks against these bounds.
    /// </summary>
    public sealed class Obstacle : MonoBehaviour
    {
        private static int _runtimeCounter;
        public int RuntimeId { get; private set; }
        public ObstacleKind Kind { get; private set; }
        public float Width { get; private set; }
        public float HalfWidth => Width * 0.5f;
        public float Depth { get; private set; }
        public float MinY { get; private set; }
        public float MaxY { get; private set; }
        public bool Active { get; private set; }
        public int Lane { get; private set; }

        private float _baseX;
        private float _baseY;
        private float _moveAmplitude;
        private float _moveSpeed;
        private float _phase;
        private Transform _spinner;
        private bool _moving;
        private float _spinSpeed;
        private Transform _warningA;
        private Transform _warningB;

        public void Bind(Transform spinner, Transform warningA, Transform warningB)
        {
            _spinner = spinner;
            _warningA = warningA;
            _warningB = warningB;
        }

        public void Configure(ObstacleKind kind, int lane, Vector3 localPosition, float width, float depth,
            float minY, float maxY, float moveAmplitude = 0f, float moveSpeed = 0f, float phase = 0f,
            float spinSpeed = 0f)
        {
            Kind = kind;
            Lane = lane;
            Width = width;
            Depth = depth;
            MinY = minY;
            MaxY = maxY;
            _baseX = localPosition.x;
            _baseY = localPosition.y;
            _moveAmplitude = moveAmplitude;
            _moveSpeed = moveSpeed;
            _phase = phase;
            _spinSpeed = spinSpeed;
            _moving = moveAmplitude > 0.001f || moveSpeed > 0.001f;
            transform.localPosition = localPosition;
            RuntimeId = ++_runtimeCounter;
            Active = true;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (!Active) return;
            if (_moving)
            {
                var p = transform.localPosition;
                float t = Time.time * _moveSpeed + _phase;
                p.x = _baseX + Mathf.Sin(t) * _moveAmplitude;
                p.y = _baseY + (Kind == ObstacleKind.HoverDrone ? Mathf.Cos(t * 0.9f) * 0.16f : 0f);
                transform.localPosition = p;
            }
            if (_spinner != null && _spinSpeed > 0f) _spinner.Rotate(0f, _spinSpeed * Time.deltaTime, 0f, Space.Self);
            if (_warningA != null)
            {
                float pulse = 0.6f + Mathf.PingPong(Time.time * 2.5f, 0.4f);
                var s = _warningA.localScale;
                s.y = pulse;
                _warningA.localScale = s;
            }
        }

        public bool Blocks(float x, float playerHalfWidth, float playerMinY, float playerMaxY,
            float zMin, float zMax, float radiusPadding = 0f)
        {
            if (!Active) return false;
            Vector3 p = transform.position;
            float halfW = Width * 0.5f + playerHalfWidth + radiusPadding;
            if (Mathf.Abs(x - p.x) > halfW) return false;
            float halfD = Depth * 0.5f + radiusPadding;
            if (zMax < p.z - halfD || zMin > p.z + halfD) return false;
            float obsMinY = p.y + MinY;
            float obsMaxY = p.y + MaxY;
            return playerMaxY > obsMinY + 0.02f && playerMinY < obsMaxY - 0.02f;
        }

        public bool IsNearMiss(float x, float playerHalfWidth, float zMin, float zMax)
        {
            if (!Active) return false;
            Vector3 p = transform.position;
            float xGap = Mathf.Abs(x - p.x) - (Width * 0.5f + playerHalfWidth);
            bool zOverlap = zMax >= p.z - Depth * 0.5f && zMin <= p.z + Depth * 0.5f;
            return zOverlap && xGap > 0f && xGap < 0.8f;
        }

        public void Release()
        {
            Active = false;
            gameObject.SetActive(false);
        }

        public void SetHighlight(bool on)
        {
            // Kept for tutorial hints; visuals use emissive materials, so scale a subtle pulse.
            transform.localScale = on ? Vector3.one * 1.06f : Vector3.one;
        }
    }

    /// <summary>Visual factory and pool. Every hazard is made from original primitive geometry.</summary>
    public sealed class ObstaclePool
    {
        private readonly Dictionary<ObstacleKind, Stack<Obstacle>> _pools = new Dictionary<ObstacleKind, Stack<Obstacle>>();
        private readonly Transform _container;

        public ObstaclePool(Transform container)
        {
            _container = container;
        }

        public Obstacle Spawn(ObstacleKind kind, Transform parent, Vector3 localPosition, int lane,
            float width, float depth, float minY, float maxY, float moveAmplitude = 0f,
            float moveSpeed = 0f, float phase = 0f, float spinSpeed = 0f)
        {
            if (!_pools.TryGetValue(kind, out var stack))
            {
                stack = new Stack<Obstacle>();
                _pools[kind] = stack;
            }
            Obstacle obstacle = null;
            while (stack.Count > 0 && obstacle == null) obstacle = stack.Pop();
            if (obstacle == null) obstacle = Build(kind);
            obstacle.transform.SetParent(parent, false);
            obstacle.Configure(kind, lane, localPosition, width, depth, minY, maxY, moveAmplitude, moveSpeed, phase, spinSpeed);
            return obstacle;
        }

        public void Return(Obstacle obstacle)
        {
            if (obstacle == null) return;
            obstacle.Release();
            obstacle.transform.SetParent(_container, false);
            if (!_pools.TryGetValue(obstacle.Kind, out var stack))
            {
                stack = new Stack<Obstacle>();
                _pools[obstacle.Kind] = stack;
            }
            stack.Push(obstacle);
        }

        private Obstacle Build(ObstacleKind kind)
        {
            var root = new GameObject("Hazard_" + kind);
            root.transform.SetParent(_container, false);
            var obstacle = root.AddComponent<Obstacle>();
            Transform spinner = null, warningA = null, warningB = null;
            Color accent = kind switch
            {
                ObstacleKind.JumpBarrier => Palette.Orange,
                ObstacleKind.OverheadLaser => Palette.Red,
                ObstacleKind.CargoCrate => Palette.Amber,
                ObstacleKind.HoverDrone => Palette.Magenta,
                ObstacleKind.ElectricPad => Palette.Cyan,
                ObstacleKind.MovingBlocker => Palette.Violet,
                ObstacleKind.RotatingBar => Palette.Orange,
                ObstacleKind.LaserWall => Palette.Red,
                _ => Palette.Cyan
            };
            Material dark = MaterialLibrary.Lit(Palette.DeepPanel, 0.6f, 0.35f);
            Material metal = MaterialLibrary.Lit(Palette.PanelBright, 0.8f, 0.7f);
            Material neon = MaterialLibrary.Lit(accent, 0.2f, 0.75f, accent, 3.2f);
            Material white = MaterialLibrary.Lit(Palette.White, 0.2f, 0.8f, Palette.White, 1.4f);
            Material warning = MaterialLibrary.Lit(Palette.White, 0.1f, 0.9f, Palette.White, 3.6f);

            switch (kind)
            {
                case ObstacleKind.JumpBarrier:
                    PrimitiveMeshes.Box(root.transform, "PostL", new Vector3(-0.82f, 0.5f, 0f), new Vector3(0.12f, 1.0f, 0.18f), metal);
                    PrimitiveMeshes.Box(root.transform, "PostR", new Vector3(0.82f, 0.5f, 0f), new Vector3(0.12f, 1.0f, 0.18f), metal);
                    PrimitiveMeshes.Box(root.transform, "Bar", new Vector3(0f, 0.72f, 0f), new Vector3(1.72f, 0.16f, 0.22f), neon);
                    PrimitiveMeshes.Box(root.transform, "BarDark", new Vector3(0f, 0.58f, 0.02f), new Vector3(1.5f, 0.14f, 0.16f), dark);
                    warningA = PrimitiveMeshes.Box(root.transform, "Warning", new Vector3(0f, 0.93f, 0f), new Vector3(0.34f, 0.12f, 0.06f), warning).transform;
                    break;
                case ObstacleKind.OverheadLaser:
                    PrimitiveMeshes.Box(root.transform, "PostL", new Vector3(-0.96f, 1.45f, 0f), new Vector3(0.13f, 2.9f, 0.2f), metal);
                    PrimitiveMeshes.Box(root.transform, "PostR", new Vector3(0.96f, 1.45f, 0f), new Vector3(0.13f, 2.9f, 0.2f), metal);
                    PrimitiveMeshes.Box(root.transform, "Brace", new Vector3(0f, 2.82f, 0f), new Vector3(2.05f, 0.16f, 0.24f), dark);
                    for (int i = 0; i < 4; i++)
                    {
                        PrimitiveMeshes.Box(root.transform, "Laser" + i, new Vector3(-0.66f + i * 0.44f, 2.2f, 0f),
                            new Vector3(0.035f, 1.4f, 0.035f), neon);
                    }
                    warningA = PrimitiveMeshes.Box(root.transform, "LaserHeader", new Vector3(0f, 2.8f, 0.13f),
                        new Vector3(1.4f, 0.07f, 0.03f), warning).transform;
                    break;
                case ObstacleKind.CargoCrate:
                    PrimitiveMeshes.Box(root.transform, "Crate", new Vector3(0f, 0.82f, 0f), new Vector3(1.85f, 1.64f, 1.7f), dark);
                    PrimitiveMeshes.Box(root.transform, "CrateFace", new Vector3(0f, 0.82f, -0.87f), new Vector3(1.65f, 1.36f, 0.07f), metal);
                    PrimitiveMeshes.Box(root.transform, "StripeH", new Vector3(0f, 0.96f, -0.92f), new Vector3(1.6f, 0.14f, 0.03f), neon);
                    PrimitiveMeshes.Box(root.transform, "StripeV", new Vector3(-0.55f, 0.82f, -0.92f), new Vector3(0.12f, 1.3f, 0.03f), neon);
                    PrimitiveMeshes.Box(root.transform, "Corner", new Vector3(0.7f, 0.82f, -0.92f), new Vector3(0.16f, 0.3f, 0.03f), warning);
                    break;
                case ObstacleKind.HoverDrone:
                    PrimitiveMeshes.SphereObject(root.transform, "Body", new Vector3(0f, 1.65f, 0f), 0.7f, dark);
                    PrimitiveMeshes.SphereObject(root.transform, "Eye", new Vector3(0f, 1.65f, -0.36f), 0.22f, neon);
                    PrimitiveMeshes.Box(root.transform, "WingL", new Vector3(-0.55f, 1.65f, 0f), new Vector3(0.5f, 0.06f, 0.18f), metal,
                        Quaternion.Euler(0f, 0f, -12f));
                    PrimitiveMeshes.Box(root.transform, "WingR", new Vector3(0.55f, 1.65f, 0f), new Vector3(0.5f, 0.06f, 0.18f), metal,
                        Quaternion.Euler(0f, 0f, 12f));
                    spinner = PrimitiveMeshes.Create("Rotor", PrimitiveMeshes.Cylinder, neon, root.transform,
                        new Vector3(0f, 2.0f, 0f), new Vector3(1.15f, 0.015f, 1.15f)).transform;
                    PrimitiveMeshes.Box(root.transform, "Scanner", new Vector3(0f, 1.26f, -0.44f), new Vector3(0.1f, 0.4f, 0.04f), neon,
                        Quaternion.Euler(28f, 0f, 0f));
                    warningA = PrimitiveMeshes.SphereObject(root.transform, "Warning", new Vector3(0f, 1.98f, 0f), 0.12f, warning).transform;
                    break;
                case ObstacleKind.ElectricPad:
                    PrimitiveMeshes.Box(root.transform, "Pad", new Vector3(0f, 0.06f, 0f), new Vector3(2.05f, 0.12f, 2.2f), dark);
                    PrimitiveMeshes.Box(root.transform, "PadGlow", new Vector3(0f, 0.13f, 0f), new Vector3(1.8f, 0.03f, 1.95f), neon);
                    PrimitiveMeshes.Box(root.transform, "EdgeL", new Vector3(-1.0f, 0.2f, 0f), new Vector3(0.08f, 0.14f, 2.2f), metal);
                    PrimitiveMeshes.Box(root.transform, "EdgeR", new Vector3(1.0f, 0.2f, 0f), new Vector3(0.08f, 0.14f, 2.2f), metal);
                    warningA = PrimitiveMeshes.Box(root.transform, "Arc", new Vector3(0f, 0.25f, 0f), new Vector3(1.2f, 0.02f, 0.25f), warning,
                        Quaternion.Euler(0f, 25f, 0f)).transform;
                    break;
                case ObstacleKind.MovingBlocker:
                    PrimitiveMeshes.Box(root.transform, "Rail", new Vector3(0f, 0.05f, 0f), new Vector3(2.3f, 0.09f, 0.3f), metal);
                    PrimitiveMeshes.Box(root.transform, "Body", new Vector3(0f, 0.9f, 0f), new Vector3(1.3f, 1.8f, 0.65f), dark);
                    PrimitiveMeshes.Box(root.transform, "Face", new Vector3(0f, 0.95f, -0.35f), new Vector3(1.0f, 1.2f, 0.06f), metal);
                    PrimitiveMeshes.Box(root.transform, "Stripe", new Vector3(0f, 1.1f, -0.4f), new Vector3(0.9f, 0.14f, 0.03f), neon);
                    warningA = PrimitiveMeshes.Box(root.transform, "Eye", new Vector3(0f, 1.55f, -0.4f), new Vector3(0.4f, 0.12f, 0.04f), warning).transform;
                    break;
                case ObstacleKind.RotatingBar:
                    PrimitiveMeshes.Create("Hub", PrimitiveMeshes.Cylinder, metal, root.transform, new Vector3(0f, 1.1f, 0f),
                        new Vector3(0.32f, 1.1f, 0.32f));
                    spinner = new GameObject("Spinner").transform;
                    spinner.SetParent(root.transform, false);
                    spinner.localPosition = new Vector3(0f, 1.28f, 0f);
                    PrimitiveMeshes.Box(spinner, "Bar", Vector3.zero, new Vector3(2.1f, 0.16f, 0.22f), neon);
                    PrimitiveMeshes.Box(spinner, "TipL", new Vector3(-1.0f, 0f, 0f), new Vector3(0.2f, 0.24f, 0.3f), warning);
                    PrimitiveMeshes.Box(spinner, "TipR", new Vector3(1.0f, 0f, 0f), new Vector3(0.2f, 0.24f, 0.3f), warning);
                    break;
                case ObstacleKind.LaserWall:
                    PrimitiveMeshes.Box(root.transform, "FrameL", new Vector3(-1.0f, 1.2f, 0f), new Vector3(0.1f, 2.4f, 0.12f), metal);
                    PrimitiveMeshes.Box(root.transform, "FrameR", new Vector3(1.0f, 1.2f, 0f), new Vector3(0.1f, 2.4f, 0.12f), metal);
                    for (int i = 0; i < 5; i++)
                    {
                        PrimitiveMeshes.Box(root.transform, "Grid" + i, new Vector3(-0.8f + i * 0.4f, 1.2f, 0f),
                            new Vector3(0.025f, 2.3f, 0.025f), neon);
                    }
                    PrimitiveMeshes.Box(root.transform, "Header", new Vector3(0f, 2.45f, 0f), new Vector3(2.1f, 0.16f, 0.16f), dark);
                    warningA = PrimitiveMeshes.Box(root.transform, "HeaderGlow", new Vector3(0f, 2.45f, -0.09f),
                        new Vector3(1.5f, 0.05f, 0.02f), warning).transform;
                    break;
            }
            obstacle.Bind(spinner, warningA, warningB);
            return obstacle;
        }
    }
}
