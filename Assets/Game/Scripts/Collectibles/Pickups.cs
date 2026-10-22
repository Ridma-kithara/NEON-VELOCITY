using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;

namespace NeonVelocity.Collectibles
{
    public enum EnergyKind
    {
        Credit,
        Prism
    }

    public sealed class EnergyPickup : MonoBehaviour
    {
        public EnergyKind Kind { get; private set; }
        public int Value { get; private set; }
        public bool Collected { get; private set; }
        private Transform _spin;
        private float _baseY;
        private float _phase;
        private float _bobHeight = 0.12f;
        private bool _attracting;

        public void Bind(Transform spin)
        {
            _spin = spin;
        }

        public void Configure(EnergyKind kind, int value, Vector3 localPosition, float phase)
        {
            Kind = kind;
            Value = value;
            _baseY = localPosition.y;
            _phase = phase;
            _attracting = false;
            Collected = false;
            transform.localPosition = localPosition;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (Collected || _attracting) return;
            float t = Time.time + _phase;
            if (_spin != null) _spin.Rotate(0f, 110f * Time.deltaTime, 0f, Space.Self);
            var p = transform.localPosition;
            p.y = _baseY + Mathf.Sin(t * 2.2f) * _bobHeight;
            transform.localPosition = p;
        }

        /// <summary>Magnet motion. The caller supplies a world-space target.</summary>
        public void Attract(Vector3 worldTarget, float deltaTime, float speed)
        {
            if (Collected) return;
            _attracting = true;
            transform.position = Vector3.MoveTowards(transform.position, worldTarget, speed * deltaTime);
        }

        public void SetAttracting(bool value)
        {
            if (!value)
            {
                _baseY = transform.localPosition.y;
                _attracting = false;
            }
        }

        public void Collect()
        {
            Collected = true;
            gameObject.SetActive(false);
        }

        public void ResetPickup()
        {
            Collected = false;
            gameObject.SetActive(true);
        }
    }

    public sealed class PowerUpPickup : MonoBehaviour
    {
        public PowerUpType Type { get; private set; }
        public PowerUpDefinition Definition { get; private set; }
        public bool Collected { get; private set; }
        private Transform _spin;
        private float _baseY;
        private float _phase;
        private TextMesh _label;

        public void Bind(Transform spin, TextMesh label)
        {
            _spin = spin;
            _label = label;
        }

        public void Configure(PowerUpDefinition definition, Vector3 localPosition, float phase)
        {
            Definition = definition;
            Type = definition.type;
            _baseY = localPosition.y;
            _phase = phase;
            Collected = false;
            transform.localPosition = localPosition;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (Collected) return;
            float t = Time.time + _phase;
            if (_spin != null) _spin.Rotate(0f, 60f * Time.deltaTime, 0f, Space.Self);
            var p = transform.localPosition;
            p.y = _baseY + Mathf.Sin(t * 1.8f) * 0.18f;
            transform.localPosition = p;
            if (_label != null)
            {
                var cam = Camera.main;
                if (cam != null) _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.transform.position, Vector3.up);
            }
        }

        public void Collect()
        {
            Collected = true;
            gameObject.SetActive(false);
        }

        public void ResetPickup()
        {
            Collected = false;
            gameObject.SetActive(true);
        }
    }

    public static class PickupGeometry
    {
        private static Mesh _octahedron;

        public static Mesh Octahedron
        {
            get
            {
                if (_octahedron != null) return _octahedron;
                var mesh = new Mesh { name = "NV_Octahedron" };
                var verts = new[]
                {
                    new Vector3(0f, 0.65f, 0f),
                    new Vector3(0f, -0.65f, 0f),
                    new Vector3(0.42f, 0f, 0f),
                    new Vector3(0f, 0f, -0.42f),
                    new Vector3(-0.42f, 0f, 0f),
                    new Vector3(0f, 0f, 0.42f)
                };
                var tris = new[]
                {
                    0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 2,
                    1, 3, 2, 1, 4, 3, 1, 5, 4, 1, 2, 5
                };
                mesh.vertices = verts;
                mesh.triangles = tris;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                _octahedron = mesh;
                return _octahedron;
            }
        }
    }

    public sealed class PickupPool
    {
        private readonly Stack<EnergyPickup> _credits = new Stack<EnergyPickup>();
        private readonly Stack<EnergyPickup> _prisms = new Stack<EnergyPickup>();
        private readonly Dictionary<PowerUpType, Stack<PowerUpPickup>> _powerUps = new Dictionary<PowerUpType, Stack<PowerUpPickup>>();
        private readonly Transform _container;

        public PickupPool(Transform container)
        {
            _container = container;
        }

        public EnergyPickup SpawnCredit(Transform parent, Vector3 localPosition, float phase, bool prism = false)
        {
            var stack = prism ? _prisms : _credits;
            EnergyPickup pickup = null;
            while (stack.Count > 0 && pickup == null) pickup = stack.Pop();
            if (pickup == null) pickup = BuildEnergy(prism);
            pickup.transform.SetParent(parent, false);
            pickup.Configure(prism ? EnergyKind.Prism : EnergyKind.Credit, prism ? 25 : 1, localPosition, phase);
            return pickup;
        }

        public PowerUpPickup SpawnPowerUp(Transform parent, PowerUpDefinition definition, Vector3 localPosition, float phase)
        {
            if (!_powerUps.TryGetValue(definition.type, out var stack))
            {
                stack = new Stack<PowerUpPickup>();
                _powerUps[definition.type] = stack;
            }
            PowerUpPickup pickup = null;
            while (stack.Count > 0 && pickup == null) pickup = stack.Pop();
            if (pickup == null) pickup = BuildPowerUp(definition);
            pickup.transform.SetParent(parent, false);
            pickup.Configure(definition, localPosition, phase);
            return pickup;
        }

        public void Return(EnergyPickup pickup)
        {
            if (pickup == null) return;
            pickup.Collect();
            pickup.transform.SetParent(_container, false);
            if (pickup.Kind == EnergyKind.Prism) _prisms.Push(pickup);
            else _credits.Push(pickup);
        }

        public void Return(PowerUpPickup pickup)
        {
            if (pickup == null) return;
            pickup.Collect();
            pickup.transform.SetParent(_container, false);
            if (!_powerUps.TryGetValue(pickup.Type, out var stack))
            {
                stack = new Stack<PowerUpPickup>();
                _powerUps[pickup.Type] = stack;
            }
            stack.Push(pickup);
        }

        private EnergyPickup BuildEnergy(bool prism)
        {
            var root = new GameObject(prism ? "PrismShard" : "EnergyCredit");
            root.transform.SetParent(_container, false);
            Color color = prism ? Palette.Violet : Palette.Cyan;
            Material core = MaterialLibrary.Lit(color, 0.2f, 0.9f, color, prism ? 2.2f : 1.45f);
            Material ring = MaterialLibrary.Additive(color);
            var spin = new GameObject("Spin").transform;
            spin.SetParent(root.transform, false);
            var mesh = PrimitiveMeshes.Create("Core", PickupGeometry.Octahedron, core, spin, Vector3.zero,
                Vector3.one * (prism ? 1.4f : 1f));
            mesh.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            PrimitiveMeshes.Create("Halo", PrimitiveMeshes.Cylinder, ring, spin, Vector3.zero,
                new Vector3(prism ? 1.4f : 0.95f, 0.006f, prism ? 1.4f : 0.95f));
            PrimitiveMeshes.Create("Halo2", PrimitiveMeshes.Cylinder, ring, spin, Vector3.zero,
                new Vector3(prism ? 1.1f : 0.7f, 0.006f, prism ? 1.1f : 0.7f), Quaternion.Euler(90f, 0f, 0f));
            var pickup = root.AddComponent<EnergyPickup>();
            pickup.Bind(spin);
            return pickup;
        }

        private PowerUpPickup BuildPowerUp(PowerUpDefinition def)
        {
            var root = new GameObject("PowerUp_" + def.id);
            root.transform.SetParent(_container, false);
            Color color = def.color;
            Material core = MaterialLibrary.Lit(Color.Lerp(color, Color.white, 0.18f), 0.4f, 0.9f, color, 2.4f);
            Material ring = MaterialLibrary.Lit(color, 0.2f, 0.8f, color, 1.8f);
            Material dark = MaterialLibrary.Lit(Palette.DeepPanel, 0.8f, 0.6f);
            var spin = new GameObject("Spin").transform;
            spin.SetParent(root.transform, false);
            PrimitiveMeshes.SphereObject(spin, "Core", Vector3.zero, 0.52f, core);
            PrimitiveMeshes.Box(spin, "ShellA", Vector3.zero, new Vector3(0.86f, 0.86f, 0.86f), ring,
                Quaternion.Euler(45f, 0f, 0f));
            PrimitiveMeshes.Create("Ring", PrimitiveMeshes.Cylinder, ring, spin, Vector3.zero,
                new Vector3(1.05f, 0.018f, 1.05f), Quaternion.Euler(0f, 0f, 18f));
            PrimitiveMeshes.Create("Ring2", PrimitiveMeshes.Cylinder, dark, spin, Vector3.zero,
                new Vector3(1.3f, 0.012f, 1.3f), Quaternion.Euler(72f, 0f, 0f));

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            var label = labelGo.AddComponent<TextMesh>();
            label.text = def.shortCode;
            label.font = FontLibrary.Body;
            label.fontSize = 60;
            label.characterSize = 0.035f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = Color.white;
            var lr = labelGo.GetComponent<MeshRenderer>();
            if (label.font != null) lr.material = label.font.material;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var pickup = root.AddComponent<PowerUpPickup>();
            pickup.Bind(spin, label);
            return pickup;
        }
    }
}
