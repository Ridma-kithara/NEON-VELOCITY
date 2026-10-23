using System.Collections.Generic;
using UnityEngine;

namespace NeonVelocity.Utilities
{
    /// <summary>
    /// Runtime material factory. All assets are original and generated from Unity primitives;
    /// replace with authored materials by assigning the same named fields in the configurator.
    /// </summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static Shader _litShader;
        private static Shader _unlitShader;
        private static Shader _particleShader;
        private static Shader _trailShader;

        public static Shader LitShader
        {
            get
            {
                if (_litShader == null)
                {
                    _litShader = Shader.Find("Universal Render Pipeline/Lit");
                    if (_litShader == null) _litShader = Shader.Find("Standard");
                    if (_litShader == null) _litShader = Shader.Find("Universal Render Pipeline/Unlit");
                }
                return _litShader;
            }
        }

        public static Shader UnlitShader
        {
            get
            {
                if (_unlitShader == null)
                {
                    _unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (_unlitShader == null) _unlitShader = Shader.Find("Unlit/Color");
                    if (_unlitShader == null) _unlitShader = Shader.Find("Sprites/Default");
                }
                return _unlitShader;
            }
        }

        public static Shader ParticleShader
        {
            get
            {
                if (_particleShader == null)
                {
                    _particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                    if (_particleShader == null) _particleShader = Shader.Find("Particles/Standard Unlit");
                    if (_particleShader == null) _particleShader = UnlitShader;
                }
                return _particleShader;
            }
        }

        public static Shader TrailShader
        {
            get
            {
                if (_trailShader == null)
                {
                    _trailShader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (_trailShader == null) _trailShader = Shader.Find("Sprites/Default");
                }
                return _trailShader;
            }
        }

        public static Material Lit(Color color, float metallic = 0.25f, float smoothness = 0.65f,
            Color? emission = null, float emissionIntensity = 1.6f)
        {
            string key = $"lit:{ColorUtility.ToHtmlStringRGBA(color)}:{metallic:F2}:{smoothness:F2}:" +
                         (emission.HasValue ? ColorUtility.ToHtmlStringRGBA(emission.Value) : "-") + $":{emissionIntensity:F2}";
            if (Cache.TryGetValue(key, out Material existing) && existing != null) return existing;

            var mat = FromTemplate("NV_Lit_Opaque", LitShader);
            mat.name = "NV_Lit_" + key.GetHashCode();
            SafeSetColor(mat, "_BaseColor", color);
            SafeSetColor(mat, "_Color", color);
            SafeSetFloat(mat, "_Metallic", metallic);
            SafeSetFloat(mat, "_Smoothness", smoothness);
            SafeSetFloat(mat, "_Glossiness", smoothness);
            if (emission.HasValue)
            {
                Color emissionColor = Palette.Hdr(emission.Value, emissionIntensity);
                mat.EnableKeyword("_EMISSION");
                SafeSetColor(mat, "_EmissionColor", emissionColor);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                SafeSetColor(mat, "_EmissionColor", Color.black);
            }
            Cache[key] = mat;
            return mat;
        }

        public static Material Transparent(Color color, float smoothness = 0.7f)
        {
            string key = $"trans:{ColorUtility.ToHtmlStringRGBA(color)}:{smoothness:F2}";
            if (Cache.TryGetValue(key, out Material existing) && existing != null) return existing;
            var mat = FromTemplate("NV_Lit_Transparent", LitShader);
            mat.name = "NV_Transparent";
            SafeSetColor(mat, "_BaseColor", color);
            SafeSetColor(mat, "_Color", color);
            SafeSetFloat(mat, "_Smoothness", smoothness);
            SafeSetFloat(mat, "_Surface", 1f);
            SafeSetFloat(mat, "_Blend", 0f);
            SafeSetFloat(mat, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SafeSetFloat(mat, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SafeSetFloat(mat, "_ZWrite", 0f);
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            Cache[key] = mat;
            return mat;
        }

        /// <summary>Additive particle material with a soft generated sprite (mist, splashes).</summary>
        public static Material ParticleTextured(string key, Texture texture, Color tint, Vector2 tiling)
        {
            if (Cache.TryGetValue(key, out Material existing) && existing != null) return existing;
            var mat = FromTemplate("NV_Particles_Additive", ParticleShader);
            mat.name = key;
            mat.mainTexture = texture;
            mat.mainTextureScale = tiling;
            SafeSetColor(mat, "_BaseColor", tint);
            SafeSetColor(mat, "_Color", tint);
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", texture);
                mat.SetTextureScale("_BaseMap", tiling);
            }
            Cache[key] = mat;
            return mat;
        }

        public static Material Additive(Color color)
        {
            string key = $"add:{ColorUtility.ToHtmlStringRGBA(color)}";
            if (Cache.TryGetValue(key, out Material existing) && existing != null) return existing;
            var mat = FromTemplate("NV_Particles_Additive", ParticleShader);
            mat.name = "NV_Additive";
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Palette.Hdr(color, 2.2f));
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Palette.Hdr(color, 2.2f));
            SafeSetFloat(mat, "_Surface", 1f);
            SafeSetFloat(mat, "_Blend", 2f);
            SafeSetFloat(mat, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SafeSetFloat(mat, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            SafeSetFloat(mat, "_ZWrite", 0f);
            SafeSetFloat(mat, "_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            Cache[key] = mat;
            return mat;
        }

        public static Material Unlit(Color color, bool transparent = false)
        {
            string key = $"unlit:{ColorUtility.ToHtmlStringRGBA(color)}:{transparent}";
            if (Cache.TryGetValue(key, out Material existing) && existing != null) return existing;
            var mat = FromTemplate(transparent ? "NV_Unlit_Transparent" : "NV_Unlit_Opaque", UnlitShader);
            mat.name = "NV_Unlit";
            SafeSetColor(mat, "_BaseColor", color);
            SafeSetColor(mat, "_Color", color);
            if (transparent)
            {
                SafeSetFloat(mat, "_Surface", 1f);
                SafeSetFloat(mat, "_Blend", 0f);
                SafeSetFloat(mat, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                SafeSetFloat(mat, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                SafeSetFloat(mat, "_ZWrite", 0f);
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            Cache[key] = mat;
            return mat;
        }

        public static Material UnlitTextured(string key, Texture texture, Color tint, Vector2 tiling)
        {
            if (Cache.TryGetValue(key, out Material existing) && existing != null) return existing;
            var mat = FromTemplate("NV_Unlit_Opaque", UnlitShader);
            mat.name = key;
            mat.mainTexture = texture;
            SafeSetColor(mat, "_BaseColor", tint);
            SafeSetColor(mat, "_Color", tint);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_BaseMap")) mat.SetTextureScale("_BaseMap", tiling);
            mat.mainTextureScale = tiling;
            Cache[key] = mat;
            return mat;
        }

        /// <summary>
        /// Unlit, alpha-blended texture used by the soft contact shadow under runners. The
        /// material is deliberately unlit and cull-off so it remains stable on every quality
        /// level and over both wet asphalt and metal station floors.
        /// </summary>
        public static Material UnlitTransparentTextured(string key, Texture texture, Color tint, Vector2 tiling)
        {
            if (Cache.TryGetValue(key, out Material existing) && existing != null) return existing;
            var mat = FromTemplate("NV_Unlit_Transparent", UnlitShader);
            mat.name = key;
            mat.mainTexture = texture;
            mat.mainTextureScale = tiling;
            SafeSetColor(mat, "_BaseColor", tint);
            SafeSetColor(mat, "_Color", tint);
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", texture);
                mat.SetTextureScale("_BaseMap", tiling);
            }
            SafeSetFloat(mat, "_Surface", 1f);
            SafeSetFloat(mat, "_Blend", 0f);
            SafeSetFloat(mat, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SafeSetFloat(mat, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SafeSetFloat(mat, "_ZWrite", 0f);
            SafeSetFloat(mat, "_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 20;
            Cache[key] = mat;
            return mat;
        }

        /// <summary>
        /// Lit material with a generated albedo/normal map. The normal-map template is created
        /// by BuildTools so the _NORMALMAP variant survives URP shader stripping in a player.
        /// </summary>
        public static Material LitTextured(string key, Texture texture, Color tint, float metallic,
            float smoothness, Vector2 tiling, Texture normal = null, Vector2? normalTiling = null,
            float normalScale = 1f)
        {
            if (Cache.TryGetValue(key, out Material existing) && existing != null) return existing;
            var mat = FromTemplate(normal != null ? "NV_Lit_Normal" : "NV_Lit_Opaque", LitShader);
            mat.name = key;
            mat.mainTexture = texture;
            mat.mainTextureScale = tiling;
            SafeSetColor(mat, "_BaseColor", tint);
            SafeSetColor(mat, "_Color", tint);
            SafeSetFloat(mat, "_Metallic", metallic);
            SafeSetFloat(mat, "_Smoothness", smoothness);
            SafeSetFloat(mat, "_Glossiness", smoothness);
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", texture);
                mat.SetTextureScale("_BaseMap", tiling);
            }
            if (normal != null)
            {
                mat.EnableKeyword("_NORMALMAP");
                if (mat.HasProperty("_BumpMap"))
                {
                    mat.SetTexture("_BumpMap", normal);
                    mat.SetTextureScale("_BumpMap", normalTiling ?? tiling);
                }
                SafeSetFloat(mat, "_BumpScale", normalScale);
            }
            else
            {
                mat.DisableKeyword("_NORMALMAP");
            }
            Cache[key] = mat;
            return mat;
        }

        /// <summary>Additive unlit texture used for cloud shells and mist/light cards.</summary>
        public static Material UnlitAdditiveTextured(string key, Texture texture, Color tint, Vector2 tiling,
            bool insideDome = false, int queueOffset = 0)
        {
            if (Cache.TryGetValue(key, out Material existing) && existing != null) return existing;
            var mat = FromTemplate("NV_Unlit_Additive", UnlitShader);
            mat.name = key;
            mat.mainTexture = texture;
            mat.mainTextureScale = tiling;
            SafeSetColor(mat, "_BaseColor", tint);
            SafeSetColor(mat, "_Color", tint);
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", texture);
                mat.SetTextureScale("_BaseMap", tiling);
            }
            SafeSetFloat(mat, "_Cull", insideDome ? 1f : 0f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10 + queueOffset;
            Cache[key] = mat;
            return mat;
        }

        public static Material Trail(Color color)
        {
            string key = $"trail:{ColorUtility.ToHtmlStringRGBA(color)}";
            if (Cache.TryGetValue(key, out Material existing) && existing != null) return existing;
            var mat = FromTemplate("NV_Unlit_Additive", TrailShader);
            mat.name = "NV_Trail";
            SafeSetColor(mat, "_BaseColor", Palette.Hdr(color, 2f));
            SafeSetColor(mat, "_Color", Palette.Hdr(color, 2f));
            SafeSetFloat(mat, "_Surface", 1f);
            SafeSetFloat(mat, "_Blend", 2f);
            SafeSetFloat(mat, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SafeSetFloat(mat, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            SafeSetFloat(mat, "_ZWrite", 0f);
            SafeSetFloat(mat, "_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            Cache[key] = mat;
            return mat;
        }

        private static Material FromTemplate(string templateName, Shader fallback)
        {
            var template = Resources.Load<Material>("Materials/" + templateName);
            if (template != null) return new Material(template);
            return new Material(fallback);
        }

        private static void SafeSetColor(Material m, string property, Color c)
        {
            if (m.HasProperty(property)) m.SetColor(property, c);
        }

        private static void SafeSetFloat(Material m, string property, float value)
        {
            if (m.HasProperty(property)) m.SetFloat(property, value);
        }

        public static void ClearCache()
        {
            foreach (var kv in Cache) if (kv.Value != null) Object.Destroy(kv.Value);
            Cache.Clear();
        }
    }
}
