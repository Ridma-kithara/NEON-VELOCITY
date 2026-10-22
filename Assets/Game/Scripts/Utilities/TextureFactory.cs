using UnityEngine;

namespace NeonVelocity.Utilities
{
    public static class FontLibrary
    {
        private static Font _body;

        public static Font Body
        {
            get
            {
                if (_body != null) return _body;
                try { _body = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                catch { _body = null; }
                if (_body == null)
                {
                    try { _body = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                    catch { _body = null; }
                }
                if (_body == null)
                {
                    var all = Resources.FindObjectsOfTypeAll<Font>();
                    if (all.Length > 0) _body = all[0];
                }
                return _body;
            }
        }

        public static Font Display => Body;
    }

    public static class TextureFactory
    {
        private static Sprite _roundedSprite;
        private static Sprite _glowSprite;
        private static Sprite _lineSprite;
        private static Sprite _diamondSprite;
        private static Sprite _chevronSprite;
        private static Sprite _coinSprite;
        private static Sprite _powerSprite;

        public static Sprite RoundedPanel
        {
            get
            {
                if (_roundedSprite != null) return _roundedSprite;
                int size = 96;
                int radius = 18;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "NV_RoundedPanel",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float px = x + 0.5f;
                        float py = y + 0.5f;
                        float cx = Mathf.Clamp(px, radius, size - radius);
                        float cy = Mathf.Clamp(py, radius, size - radius);
                        float dx = px - cx;
                        float dy = py - cy;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                        // White RGB so the sprite can be tinted to any brand colour.
                        byte v = (byte)(alpha * 255f);
                        pixels[y * size + x] = new Color32(255, 255, 255, v);
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                    100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                _roundedSprite.name = "NV_RoundedPanel";
                return _roundedSprite;
            }
        }

        public static Sprite Glow
        {
            get
            {
                if (_glowSprite != null) return _glowSprite;
                int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "NV_Glow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
                };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - size * 0.5f) / (size * 0.5f);
                    float dy = (y - size * 0.5f) / (size * 0.5f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * a;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _glowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                return _glowSprite;
            }
        }

        public static Sprite Line
        {
            get
            {
                if (_lineSprite != null) return _lineSprite;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "NV_Line" };
                var pixels = new Color32[16];
                for (int i = 0; i < 16; i++) pixels[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(pixels);
                tex.Apply();
                _lineSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                return _lineSprite;
            }
        }

        public static Sprite Diamond
        {
            get
            {
                if (_diamondSprite != null) return _diamondSprite;
                int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "NV_Diamond", filterMode = FilterMode.Bilinear };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float nx = Mathf.Abs((x - size * 0.5f) / (size * 0.5f));
                    float ny = Mathf.Abs((y - size * 0.5f) / (size * 0.5f));
                    float d = nx + ny;
                    float a = Mathf.Clamp01((1f - d) * 8f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _diamondSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                return _diamondSprite;
            }
        }

        public static Sprite Chevron
        {
            get
            {
                if (_chevronSprite != null) return _chevronSprite;
                int w = 128, h = 64;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "NV_Chevron", filterMode = FilterMode.Bilinear };
                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float t = x / (float)w;
                    float centre = 0.5f - Mathf.Abs(t - 0.5f) * 1.9f;
                    float d = Mathf.Abs(y / (float)h - 0.5f);
                    float line = Mathf.Clamp01(1f - Mathf.Abs(centre - d) * 14f);
                    pixels[y * w + x] = new Color32(255, 255, 255, (byte)(line * 255f));
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _chevronSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
                return _chevronSprite;
            }
        }

        public static Sprite Coin
        {
            get
            {
                if (_coinSprite != null) return _coinSprite;
                int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "NV_Coin", filterMode = FilterMode.Bilinear };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - size * 0.5f) / (size * 0.5f);
                    float dy = (y - size * 0.5f) / (size * 0.5f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = Mathf.Clamp01((0.84f - d) * 20f) * Mathf.Clamp01((d - 0.25f) * 20f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(ring * 255f));
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _coinSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                return _coinSprite;
            }
        }

        public static Sprite PowerOrb
        {
            get
            {
                if (_powerSprite != null) return _powerSprite;
                int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "NV_Power", filterMode = FilterMode.Bilinear };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - size * 0.5f) / (size * 0.5f);
                    float dy = (y - size * 0.5f) / (size * 0.5f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((0.9f - d) * 16f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _powerSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                return _powerSprite;
            }
        }

        public static Texture2D VerticalGradient(string name, Color bottom, Color top, int height = 256)
        {
            var tex = new Texture2D(4, height, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[4 * height];
            for (int y = 0; y < height; y++)
            {
                Color c = Color.Lerp(bottom, top, y / (float)(height - 1));
                for (int x = 0; x < 4; x++) pixels[y * 4 + x] = c;
            }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        public static Texture2D Grid(int size = 512, int cell = 64, float lineWidth = 2f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "NV_Grid", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool line = (x % cell) < lineWidth || (y % cell) < lineWidth;
                pixels[y * size + x] = line
                    ? new Color32(80, 200, 255, 28)
                    : new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        public static Texture2D WindowFacade(int seed)
        {
            const int size = 128;
            var rng = new System.Random(seed);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "NV_Facade", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool window = (x % 16 > 3 && x % 16 < 12) && (y % 22 > 4 && y % 22 < 15);
                if (!window) { pixels[y * size + x] = new Color32(10, 16, 30, 255); continue; }
                bool lit = rng.NextDouble() > 0.36;
                pixels[y * size + x] = lit
                    ? new Color32((byte)rng.Next(80, 255), (byte)rng.Next(120, 255), 255, 255)
                    : new Color32(18, 30, 52, 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
