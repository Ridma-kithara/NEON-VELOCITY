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
        private static Mesh _beveledBox;
        private static Mesh _foliage;

        public static Mesh Cube => _cube != null ? _cube : (_cube = Capture(PrimitiveType.Cube));
        public static Mesh Sphere => _sphere != null ? _sphere : (_sphere = Capture(PrimitiveType.Sphere));
        public static Mesh Capsule => _capsule != null ? _capsule : (_capsule = Capture(PrimitiveType.Capsule));
        public static Mesh Cylinder => _cylinder != null ? _cylinder : (_cylinder = Capture(PrimitiveType.Cylinder));
        public static Mesh Quad => _quad != null ? _quad : (_quad = Capture(PrimitiveType.Quad));

        /// <summary>
        /// Faceted low-poly canopy used by the street trees. A full Unity sphere costs hundreds
        /// of vertices per crown; this 48-triangle version keeps six trees per segment cheap
        /// while retaining an irregular silhouette at runner speed.
        /// </summary>
        public static Mesh Foliage => _foliage != null ? _foliage : (_foliage = BuildLowPolySphere(8, 4));

        /// <summary>
        /// Unit cube with chamfered edges and corners. The 26 crisp facets catch rim light and
        /// read as machined armour instead of the soft, toy-like silhouette of a scaled cube.
        /// The mesh is generated once, has explicit outward normals and uses flat 0..1 UVs.
        /// </summary>
        public static Mesh BeveledBox => _beveledBox != null
            ? _beveledBox
            : (_beveledBox = BuildBeveledBox(0.155f));

        private static Mesh BuildBeveledBox(float bevel)
        {
            float outer = 0.5f;
            float inner = outer - Mathf.Clamp(bevel, 0.02f, 0.3f);
            var vertices = new List<Vector3>(104);
            var normals = new List<Vector3>(104);
            var uvs = new List<Vector2>(104);
            var triangles = new List<int>(132);

            // Six inset face plates.
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 axisVector = Axis(axis);
                Vector3 u = Axis((axis + 1) % 3);
                Vector3 v = Axis((axis + 2) % 3);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 center = axisVector * (side * outer);
                    Vector3 normal = axisVector * side;
                    Vector3 p0 = center - u * inner - v * inner;
                    Vector3 p1 = center + u * inner - v * inner;
                    Vector3 p2 = center + u * inner + v * inner;
                    Vector3 p3 = center - u * inner + v * inner;
                    AddQuad(vertices, normals, uvs, triangles, p0, p1, p2, p3, normal);
                }
            }

            // Twelve chamfer strips along the cube edges.
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 a = Axis(axis);
                int bIndex = (axis + 1) % 3;
                int cIndex = (axis + 2) % 3;
                Vector3 b = Axis(bIndex);
                Vector3 c = Axis(cIndex);
                for (int sb = -1; sb <= 1; sb += 2)
                for (int sc = -1; sc <= 1; sc += 2)
                {
                    Vector3 normal = (b * sb + c * sc).normalized;
                    Vector3 p0 = -a * inner + b * (sb * outer) + c * (sc * inner);
                    Vector3 p1 = -a * inner + b * (sb * inner) + c * (sc * outer);
                    Vector3 p2 = a * inner + b * (sb * inner) + c * (sc * outer);
                    Vector3 p3 = a * inner + b * (sb * outer) + c * (sc * inner);
                    AddQuad(vertices, normals, uvs, triangles, p0, p1, p2, p3, normal);
                }
            }

            // Eight triangular corner facets.
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 p0 = new Vector3(sx * outer, sy * inner, sz * inner);
                Vector3 p1 = new Vector3(sx * inner, sy * outer, sz * inner);
                Vector3 p2 = new Vector3(sx * inner, sy * inner, sz * outer);
                AddTriangle(vertices, normals, uvs, triangles, p0, p1, p2,
                    new Vector3(sx, sy, sz).normalized);
            }

            var mesh = new Mesh { name = "NV_BeveledBox" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildLowPolySphere(int longitudeSegments, int latitudeSegments)
        {
            int lon = Mathf.Max(5, longitudeSegments);
            int lat = Mathf.Max(2, latitudeSegments);
            var vertices = new List<Vector3>(2 + (lat - 1) * (lon + 1));
            var normals = new List<Vector3>(vertices.Capacity);
            var uvs = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(lon * lat * 6);

            vertices.Add(Vector3.up);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 1f));
            for (int ring = 1; ring < lat; ring++)
            {
                float phi = Mathf.PI * ring / lat;
                float y = Mathf.Cos(phi);
                float radius = Mathf.Sin(phi);
                for (int segment = 0; segment <= lon; segment++)
                {
                    float theta = Mathf.PI * 2f * segment / lon;
                    var direction = new Vector3(Mathf.Cos(theta) * radius, y, Mathf.Sin(theta) * radius);
                    vertices.Add(direction);
                    normals.Add(direction.normalized);
                    uvs.Add(new Vector2(segment / (float)lon, 1f - ring / (float)lat));
                }
            }
            int bottom = vertices.Count;
            vertices.Add(Vector3.down);
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0f));

            int firstRing = 1;
            for (int segment = 0; segment < lon; segment++)
                AddOutwardTriangle(vertices, triangles, 0, firstRing + segment, firstRing + segment + 1);

            for (int ring = 0; ring < lat - 2; ring++)
            {
                int rowA = firstRing + ring * (lon + 1);
                int rowB = rowA + lon + 1;
                for (int segment = 0; segment < lon; segment++)
                {
                    int a = rowA + segment;
                    int b = rowA + segment + 1;
                    int c = rowB + segment;
                    int d = rowB + segment + 1;
                    AddOutwardTriangle(vertices, triangles, a, c, b);
                    AddOutwardTriangle(vertices, triangles, b, c, d);
                }
            }

            int lastRing = firstRing + (lat - 2) * (lon + 1);
            for (int segment = 0; segment < lon; segment++)
                AddOutwardTriangle(vertices, triangles, bottom, lastRing + segment + 1, lastRing + segment);

            var mesh = new Mesh { name = "NV_Foliage" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddOutwardTriangle(List<Vector3> vertices, List<int> triangles,
            int a, int b, int c)
        {
            Vector3 normal = (vertices[a] + vertices[b] + vertices[c]).normalized;
            bool flip = Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), normal) < 0f;
            triangles.Add(a);
            triangles.Add(flip ? c : b);
            triangles.Add(flip ? b : c);
        }

        private static Vector3 Axis(int index) => index switch
        {
            0 => Vector3.right,
            1 => Vector3.up,
            _ => Vector3.forward
        };

        private static void AddQuad(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
            List<int> triangles, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal)
        {
            int start = vertices.Count;
            vertices.Add(p0); vertices.Add(p1); vertices.Add(p2); vertices.Add(p3);
            for (int i = 0; i < 4; i++) normals.Add(normal);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(0f, 1f));

            bool flip = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normal) < 0f;
            if (!flip)
            {
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }
            else
            {
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
            }
        }

        private static void AddTriangle(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
            List<int> triangles, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 normal)
        {
            int start = vertices.Count;
            vertices.Add(p0); vertices.Add(p1); vertices.Add(p2);
            for (int i = 0; i < 3; i++) normals.Add(normal);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(0.5f, 1f));

            bool flip = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normal) < 0f;
            if (!flip)
            {
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }
            else
            {
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
            }
        }

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

        public static GameObject Beveled(Transform parent, string name, Vector3 position, Vector3 scale,
            Material material, Quaternion? rotation = null) =>
            Create(name, BeveledBox, material, parent, position, scale, rotation);

        public static GameObject SphereObject(Transform parent, string name, Vector3 position, float diameter,
            Material material) => Create(name, Sphere, material, parent, position, Vector3.one * diameter);

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }
    }
}
