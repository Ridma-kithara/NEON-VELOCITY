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

        /// <summary>
        /// Original tileable asphalt albedo. Kept grayscale so a single generated texture can
        /// be tinted by each biome material without losing the aggregate and crack detail.
        /// </summary>
        public static Texture2D Asphalt(int seed, int size = 256)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "NV_Asphalt_" + seed,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 8
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)size;
                float ny = y / (float)size;
                float macro = Fbm(nx, ny, 5, seed + 3);
                float fine = Fbm(nx, ny, 29, seed + 17);
                float grain = Hash01(x, y, seed + 31);
                float value = 0.25f + macro * 0.22f + fine * 0.18f + (grain - 0.5f) * 0.06f;

                // Hairline cracks and darker repaired patches stop the surface reading as plastic.
                float ridge = 1f - Mathf.Abs(fine * 2f - 1f);
                float crack = Mathf.Pow(Mathf.Clamp01(ridge), 22f) * Mathf.Lerp(0.35f, 1f, macro);
                value = Mathf.Lerp(value, 0.055f, crack * 0.82f);
                float patch = Fbm(nx, ny, 2, seed + 91);
                if (patch > 0.58f) value *= Mathf.Lerp(1f, 0.74f, (patch - 0.58f) * 3f);

                // Sparse pale aggregate chips catch the neon light and sell the scale.
                if (Hash01(x + 71, y + 19, seed + 57) > 0.982f) value = Mathf.Lerp(value, 0.66f, 0.8f);
                byte v = (byte)(Mathf.Clamp01(value) * 255f);
                pixels[y * size + x] = new Color32(v, v, (byte)Mathf.Min(255, v + 3), 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Tileable tangent-space normal map derived from the asphalt height field.</summary>
        public static Texture2D AsphaltNormal(int seed, int size = 256, float strength = 1.8f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "NV_AsphaltNormal_" + seed,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float height = AsphaltHeight(x, y, size, seed);
                float dx = AsphaltHeight((x + 1) % size, y, size, seed) - AsphaltHeight((x - 1 + size) % size, y, size, seed);
                float dy = AsphaltHeight(x, (y + 1) % size, size, seed) - AsphaltHeight(x, (y - 1 + size) % size, size, seed);
                Vector3 n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                pixels[y * size + x] = new Color32(
                    (byte)((n.x * 0.5f + 0.5f) * 255f),
                    (byte)((n.y * 0.5f + 0.5f) * 255f),
                    (byte)((n.z * 0.5f + 0.5f) * 255f), 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        private static float AsphaltHeight(int x, int y, int size, int seed)
        {
            float nx = x / (float)size;
            float ny = y / (float)size;
            float macro = Fbm(nx, ny, 5, seed + 3);
            float fine = Fbm(nx, ny, 29, seed + 17);
            float ridge = 1f - Mathf.Abs(fine * 2f - 1f);
            return macro * 0.6f + fine * 0.4f - Mathf.Pow(Mathf.Clamp01(ridge), 22f) * 0.35f;
        }

        private static Texture2D _armorSurface;
        private static Texture2D _armorNormal;

        /// <summary>
        /// Original, tileable armour surface. It is intentionally close to neutral grey so each
        /// runner's paint colour can be applied as a material tint while the micro-scratches,
        /// brushed grain and recessed panel seams remain readable under rim lighting.
        /// </summary>
        public static Texture2D ArmorSurface()
        {
            if (_armorSurface != null) return _armorSurface;
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "NV_ArmorSurface",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)size;
                float ny = y / (float)size;
                float grain = Fbm(nx, ny, 26, 771) * 0.18f + (Hash01(x, y, 913) - 0.5f) * 0.03f;
                float brushed = (Mathf.Sin((ny * 420f) + Fbm(nx, ny, 7, 319) * 8f) * 0.5f + 0.5f) * 0.035f;
                float broad = Fbm(nx, ny, 4, 117) * 0.10f;
                float scratch = Mathf.Pow(1f - Mathf.Abs(Fbm(nx * 1.4f, ny * 0.22f, 18, 557) * 2f - 1f), 22f);
                float seamX = Mathf.Min(x % 128, 128 - x % 128);
                float seamY = Mathf.Min(y % 128, 128 - y % 128);
                float seam = Mathf.Min(Mathf.Clamp01(seamX / 1.5f), Mathf.Clamp01(seamY / 1.5f));
                float value = 0.60f + broad + grain + brushed - scratch * 0.16f;
                value *= Mathf.Lerp(0.74f, 1f, seam);
                byte v = (byte)(Mathf.Clamp01(value) * 255f);
                pixels[y * size + x] = new Color32(v, v, (byte)Mathf.Min(255, v + 2), 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            _armorSurface = tex;
            return tex;
        }

        /// <summary>Tangent-space normal detail for the armour milled surface and edge wear.</summary>
        public static Texture2D ArmorNormal()
        {
            if (_armorNormal != null) return _armorNormal;
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "NV_ArmorNormal",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float center = ArmorHeight(x, y, size);
                float dx = ArmorHeight((x + 1) % size, y, size) - ArmorHeight((x - 1 + size) % size, y, size);
                float dy = ArmorHeight(x, (y + 1) % size, size) - ArmorHeight(x, (y - 1 + size) % size, size);
                Vector3 n = new Vector3(-dx * 2.4f, -dy * 2.4f, 1f).normalized;
                pixels[y * size + x] = new Color32(
                    (byte)((n.x * 0.5f + 0.5f) * 255f),
                    (byte)((n.y * 0.5f + 0.5f) * 255f),
                    (byte)((n.z * 0.5f + 0.5f) * 255f), 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            _armorNormal = tex;
            return tex;
        }

        private static float ArmorHeight(int x, int y, int size)
        {
            float nx = x / (float)size;
            float ny = y / (float)size;
            float brushed = Mathf.Sin((ny * 420f) + Fbm(nx, ny, 7, 319) * 8f) * 0.007f;
            float grain = Fbm(nx, ny, 26, 771) * 0.035f;
            float scratch = Mathf.Pow(1f - Mathf.Abs(Fbm(nx * 1.4f, ny * 0.22f, 18, 557) * 2f - 1f), 22f);
            float seamX = Mathf.Min(x % 128, 128 - x % 128);
            float seamY = Mathf.Min(y % 128, 128 - y % 128);
            float seam = Mathf.Min(Mathf.Clamp01(seamX / 1.5f), Mathf.Clamp01(seamY / 1.5f));
            return grain + brushed - scratch * 0.05f + (1f - seam) * 0.008f;
        }

        /// <summary>Tileable concrete/metal-deck albedo with panel seams and damp stains.</summary>
        public static Texture2D Concrete(int seed, int size = 256)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "NV_Concrete_" + seed,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 8
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)size;
                float ny = y / (float)size;
                float value = 0.43f + Fbm(nx, ny, 8, seed + 5) * 0.22f
                                     + Fbm(nx, ny, 41, seed + 29) * 0.08f;
                float seamX = Mathf.Min(x % 64, 64 - x % 64);
                float seamY = Mathf.Min(y % 64, 64 - y % 64);
                float seam = Mathf.Min(Mathf.Clamp01(seamX / 2f), Mathf.Clamp01(seamY / 2f));
                value *= Mathf.Lerp(0.62f, 1f, seam);
                float stain = Fbm(nx, ny, 4, seed + 71);
                if (stain > 0.55f) value *= Mathf.Lerp(1f, 0.72f, (stain - 0.55f) * 2.2f);
                byte v = (byte)(Mathf.Clamp01(value) * 255f);
                pixels[y * size + x] = new Color32(v, v, (byte)Mathf.Min(255, v + 4), 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Soft tileable cloud/nebula alpha used by the additive sky layers.</summary>
        public static Texture2D CloudLayer(int seed, int size = 256)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "NV_Clouds_" + seed,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)size;
                float ny = y / (float)size;
                float low = Fbm(nx, ny, 3, seed + 11);
                float detail = Fbm(nx, ny, 13, seed + 47);
                float value = low * 0.66f + detail * 0.34f;
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.78f, value));
                float wisp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.54f, 0.72f, detail));
                alpha = Mathf.Max(alpha * 0.72f, wisp * 0.26f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Original abstract neon signage, including a frame, glyph rows and scanlines.</summary>
        public static Texture2D SignBoard(int seed, Color accent, int variant = 0)
        {
            int w = 128, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "NV_Sign_" + seed,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var rng = new System.Random(seed);
            var pixels = new Color32[w * h];
            Color32 dark = new Color32(5, 9, 18, 255);
            Color32 ink = new Color32((byte)(accent.r * 255f), (byte)(accent.g * 255f),
                (byte)(accent.b * 255f), 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = dark;

            // Glowing border and a few structural bars.
            for (int x = 0; x < w; x++)
            {
                for (int band = 0; band < 3; band++)
                {
                    SetPixelSafe(pixels, w, h, x, 5 + band, ink);
                    SetPixelSafe(pixels, w, h, x, h - 6 - band, ink);
                }
            }
            for (int y = 0; y < h; y++)
            for (int band = 0; band < 3; band++)
            {
                SetPixelSafe(pixels, w, h, 5 + band, y, ink);
                SetPixelSafe(pixels, w, h, w - 6 - band, y, ink);
            }

            Color32 paleInk = new Color32(
                (byte)Mathf.Clamp(Mathf.Lerp(accent.r, 1f, 0.55f) * 255f, 0f, 255f),
                (byte)Mathf.Clamp(Mathf.Lerp(accent.g, 1f, 0.55f) * 255f, 0f, 255f),
                (byte)Mathf.Clamp(Mathf.Lerp(accent.b, 1f, 0.55f) * 255f, 0f, 255f), 255);
            // Blocky ideogram-style glyphs read as signage at speed without using any real
            // language or copyrighted mark. Strokes vary per panel so districts do not repeat.
            int rows = variant % 3 == 0 ? 4 : 3;
            for (int row = 0; row < rows; row++)
            {
                int y0 = 32 + row * 48;
                for (int glyph = 0; glyph < 3; glyph++)
                {
                    int gx = 16 + glyph * 36;
                    Color32 color = ((row + glyph + variant) % 5 == 0) ? paleInk : ink;
                    int style = rng.Next(0, 4);
                    FillRect(pixels, w, h, gx, y0, 22, 3, color);
                    FillRect(pixels, w, h, gx, y0 + 19, 22, 3, color);
                    FillRect(pixels, w, h, gx, y0, 3, 22, color);
                    FillRect(pixels, w, h, gx + 19, y0, 3, 22, color);
                    if (style != 1) FillRect(pixels, w, h, gx + 6, y0 + 8, 10, 3, color);
                    if (style != 2) FillRect(pixels, w, h, gx + 9, y0 + 4, 3, 14, color);
                    if (style == 0) FillRect(pixels, w, h, gx + 4, y0 + 15, 14, 3, color);
                }
            }

            // Sparse scanlines create the faint banding of a real LED panel.
            for (int y = 0; y < h; y += 3)
            for (int x = 7; x < w - 7; x++)
            {
                var c = pixels[y * w + x];
                c = new Color32((byte)(c.r * 0.82f), (byte)(c.g * 0.82f), (byte)(c.b * 0.82f), 255);
                pixels[y * w + x] = c;
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Radial soft-alpha texture for mist, splash sprites and light pools.</summary>
        public static Texture2D RadialGlow(int size = 64)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "NV_RadialGlow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - size * 0.5f) / (size * 0.5f);
                float dy = (y - size * 0.5f) / (size * 0.5f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.4f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>
        /// Generated environment cubemap used as the global reflection probe. It contains a
        /// horizon glow and vertical neon bands so wet asphalt and armour have something
        /// believable to reflect without an imported HDRI.
        /// </summary>
        public static Cubemap ReflectionCubemap(string name, Color zenith, Color horizon, Color accent, int size = 64)
        {
            var cube = new Cubemap(size, TextureFormat.RGBA32, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear
            };
            var pixels = new Color[size * size];
            for (int face = 0; face < 6; face++)
            {
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    Vector3 dir;
                    switch (face)
                    {
                        case 0: dir = new Vector3(1f, v, -u); break;
                        case 1: dir = new Vector3(-1f, v, u); break;
                        case 2: dir = new Vector3(u, 1f, -v); break;
                        case 3: dir = new Vector3(u, -1f, v); break;
                        case 4: dir = new Vector3(u, v, 1f); break;
                        default: dir = new Vector3(-u, v, -1f); break;
                    }
                    dir.Normalize();
                    float up = Mathf.Clamp01(dir.y * 0.5f + 0.5f);
                    Color c = Color.Lerp(horizon, zenith, Mathf.Pow(up, 0.75f));
                    float band = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(dir.y)), 10f);
                    float azimuth = Mathf.Atan2(dir.x, dir.z);
                    float signGlow = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(azimuth * 6f + 0.7f)), 18f);
                    c += accent * band * (0.35f + signGlow * 0.9f);
                    if (up < 0.24f) c = Color.Lerp(c, horizon * 0.35f, 1f - up / 0.24f);
                    pixels[y * size + x] = c;
                }
                cube.SetPixels(pixels, (CubemapFace)face);
            }
            cube.Apply(true, false);
            return cube;
        }

        private static void FillRect(Color32[] pixels, int w, int h, int x, int y, int width, int height,
            Color32 color)
        {
            for (int yy = y; yy < y + height; yy++)
            for (int xx = x; xx < x + width; xx++)
                SetPixelSafe(pixels, w, h, xx, yy, color);
        }

        private static void SetPixelSafe(Color32[] pixels, int w, int h, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            pixels[y * w + x] = c;
        }

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint n = (uint)(x * 374761393 + y * 668265263 + seed * 69069);
                n = (n ^ (n >> 13)) * 1274126177u;
                n ^= n >> 16;
                return (n & 0x00FFFFFFu) / 16777215f;
            }
        }

        private static float TileNoise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float tx = x - xi;
            float ty = y - yi;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            int x0 = Mod(xi, period);
            int y0 = Mod(yi, period);
            int x1 = Mod(xi + 1, period);
            int y1 = Mod(yi + 1, period);
            float a = Mathf.Lerp(Hash01(x0, y0, seed), Hash01(x1, y0, seed), tx);
            float b = Mathf.Lerp(Hash01(x0, y1, seed), Hash01(x1, y1, seed), tx);
            return Mathf.Lerp(a, b, ty);
        }

        private static float Fbm(float x, float y, int period, int seed)
        {
            float sum = 0f, amplitude = 0.54f, normalisation = 0f;
            int p = Mathf.Max(1, period);
            for (int octave = 0; octave < 4; octave++)
            {
                sum += TileNoise(x * p, y * p, p, seed + octave * 101) * amplitude;
                normalisation += amplitude;
                amplitude *= 0.5f;
                p = Mathf.Min(256, p * 2);
            }
            return sum / Mathf.Max(0.0001f, normalisation);
        }

        private static int Mod(int value, int period)
        {
            if (period <= 0) return 0;
            int m = value % period;
            return m < 0 ? m + period : m;
        }

        public static Texture2D WindowFacade(int seed)
        {
            const int size = 512;
            var rng = new System.Random(seed);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "NV_Facade", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            Color32 glass = new Color32(9, 16, 31, 255);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int wx = x % 64;
                int wy = y % 88;
                bool window = wx > 14 && wx < 50 && wy > 18 && wy < 62;
                if (!window) { pixels[y * size + x] = glass; continue; }
                double litRoll = rng.NextDouble();
                if (litRoll > 0.62)
                {
                    float hue;
                    float roll = (float)rng.NextDouble();
                    if (roll < 0.58f) hue = 0.51f + (float)rng.NextDouble() * 0.11f;       // cool offices
                    else if (roll < 0.86f) hue = 0.82f + (float)rng.NextDouble() * 0.11f;   // magenta residences
                    else hue = 0.07f + (float)rng.NextDouble() * 0.07f;                     // warm interiors
                    Color windowColor = Color.HSVToRGB(hue, 0.4f + (float)rng.NextDouble() * 0.4f,
                        0.7f + (float)rng.NextDouble() * 0.3f);
                    // A brighter top edge reads as a ceiling light when the facade is far away.
                    if (wy < 24) windowColor = Color.Lerp(windowColor, Color.white, 0.32f);
                    pixels[y * size + x] = windowColor;
                }
                else
                {
                    pixels[y * size + x] = new Color32(17, 29, 53, 255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
