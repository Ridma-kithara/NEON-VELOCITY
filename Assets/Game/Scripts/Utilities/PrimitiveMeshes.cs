using System.Collections.Generic;
using UnityEngine;

namespace NeonVelocity.Utilities
{
    /// <summary>Cached primitive meshes with deterministic pivots, no colliders, no allocations per use.</summary>
    public static class PrimitiveMeshes
    {
        private static Mesh _cube;
        private static Mesh _sphere;
        private static Mesh _capsule;
        private static Mesh _cylinder;
        private static Mesh _quad;

        public static Mesh Cube => _cube != null ? _cube : (_cube = Capture(PrimitiveType.Cube));
        public static Mesh Sphere => _sphere != null ? _sphere : (_sphere = Capture(PrimitiveType.Sphere));
        public static Mesh Capsule => _capsule != null ? _capsule : (_capsule = Capture(PrimitiveType.Capsule));
        public static Mesh Cylinder => _cylinder != null ? _cylinder : (_cylinder = Capture(PrimitiveType.Cylinder));
        public static Mesh Quad => _quad != null ? _quad : (_quad = Capture(PrimitiveType.Quad));

        private static Mesh Capture(PrimitiveType type)
        {
            var temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(temp);
            return mesh;
        }

        public static GameObject Create(string name, Mesh mesh, Material material, Transform parent = null,
            Vector3 localPosition = default, Vector3 localScale = default, Quaternion? localRotation = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale == default ? Vector3.one : localScale;
            if (localRotation.HasValue) go.transform.localRotation = localRotation.Value;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return go;
        }

        public static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale,
            Material material, Quaternion? rotation = null) =>
            Create(name, Cube, material, parent, position, scale, rotation);

        public static GameObject SphereObject(Transform parent, string name, Vector3 position, float diameter,
            Material material) => Create(name, Sphere, material, parent, position, Vector3.one * diameter);

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }
    }

    public static class MeshCombiner
    {
        /// <summary>
        /// Combines renderers beneath a root into one mesh with a submesh per material.
        /// Matrices are captured in root-local space, so the resulting object can still move.
        /// </summary>
        public static Mesh CombineLocal(Transform root, IReadOnlyList<Renderer> renderers,
            out Material[] materials)
        {
            var groups = new Dictionary<Material, List<CombineInstance>>();
            Vector3 rootPos = root.position;
            Quaternion rootRot = root.rotation;
            Vector3 rootScale = root.lossyScale;
            Vector3 invScale = new Vector3(
                Mathf.Approximately(rootScale.x, 0f) ? 0f : 1f / rootScale.x,
                Mathf.Approximately(rootScale.y, 0f) ? 0f : 1f / rootScale.y,
                Mathf.Approximately(rootScale.z, 0f) ? 0f : 1f / rootScale.z);

            for (int i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var mat = renderer.sharedMaterial;
                if (!groups.TryGetValue(mat, out var list))
                {
                    list = new List<CombineInstance>();
                    groups[mat] = list;
                }
                Vector3 worldPos = renderer.transform.position;
                Quaternion worldRot = renderer.transform.rotation;
                Vector3 worldScale = renderer.transform.lossyScale;
                Matrix4x4 localMatrix = Matrix4x4.TRS(
                    Quaternion.Inverse(rootRot) * (worldPos - rootPos),
                    Quaternion.Inverse(rootRot) * worldRot,
                    Vector3.Scale(worldScale, invScale));
                list.Add(new CombineInstance { mesh = filter.sharedMesh, transform = localMatrix });
            }

            var combined = new Mesh { name = "NV_Combined" };
            var subMeshes = new List<CombineInstance>();
            var mats = new List<Material>();
            foreach (var kv in groups)
            {
                if (kv.Value.Count == 0) continue;
                var sub = new Mesh();
                sub.CombineMeshes(kv.Value.ToArray(), true, true);
                for (int s = 0; s < sub.subMeshCount; s++)
                {
                    subMeshes.Add(new CombineInstance { mesh = sub, subMeshIndex = s });
                    mats.Add(kv.Key);
                }
            }
            combined.CombineMeshes(subMeshes.ToArray(), false, true);
            combined.RecalculateBounds();
            materials = mats.ToArray();
            return combined;
        }
    }
}
