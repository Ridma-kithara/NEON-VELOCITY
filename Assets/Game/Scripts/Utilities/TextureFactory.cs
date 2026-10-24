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
            // A thin route header and abstract stroke clusters suggest a departure board
            // without drawing boxed UI glyphs.
            FillRect(pixels, w, h, 14, 22, 98, 2, paleInk);
            FillRect(pixels, w, h, 14, 27, 52, 1, ink);
            int rows = variant % 3 == 0 ? 4 : 3;
            for (int row = 0; row < rows; row++)
            {
                int y0 = 38 + row * 45;
                for (int glyph = 0; glyph < 3; glyph++)
                {
                    int gx = 16 + glyph * 36;
                    Color32 color = ((row + glyph + variant) % 5 == 0) ? paleInk : ink;
                    int style = rng.Next(0, 6);
                    FillRect(pixels, w, h, gx, y0 + 3, 20, 2, color);
                    if ((style & 1) == 0) FillRect(pixels, w, h, gx + 2, y0 + 3, 2, 16, color);
                    if ((style & 2) == 0) FillRect(pixels, w, h, gx + 16, y0 + 5, 2, 14, color);
                    FillRect(pixels, w, h, gx + 4, y0 + 11, 13, 2, color);
                    if (style > 2) FillRect(pixels, w, h, gx + 6, y0 + 18, 9, 2, color);
                    if (style == 5) FillRect(pixels, w, h, gx + 8, y0 + 5, 2, 13, color);
                }
            }
            // Route-map dots along the bottom edge.
            for (int dot = 0; dot < 5; dot++)
                FillRect(pixels, w, h, 15 + dot * 21, h - 18, 5, 5, dot == variant % 5 ? paleInk : ink);

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

        /// <summary>
        /// Deterministic facade atlas with four by four window cells. The world builder maps
        /// these UVs from real dimensions instead of stretching one texture over every tower,
        /// so a 12 m shopfront and a 36 m tower both keep believable floor and window sizes.
        /// </summary>
        public static Texture2D WindowFacade(int seed, int style = 0)
        {
            const int size = 512;
            const int cells = 4;
            const int cell = size / cells;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "NV_Facade_" + seed + "_" + style,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };
            var pixels = new Color32[size * size];
            bool masonry = style % 2 == 1;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int cellX = x / cell;
                int cellY = y / cell;
                int lx = x - cellX * cell;
                int ly = y - cellY * cell;
                float cellHash = Hash01(cellX, cellY, seed + 19);
                float detail = Hash01(x / 2, y / 3, seed + 41);
                float dirt = Hash01(x / 5, y / 7, seed + 73);

                // Matte wall base. It is kept close to neutral so the material tint can
                // separate warm residential blocks from cool office towers.
                float wall = masonry
                    ? 0.20f + detail * 0.07f + dirt * 0.04f
                    : 0.098f + detail * 0.045f + dirt * 0.026f;
                bool panelSeam = lx < 2 || ly < 2 || lx > cell - 3 || ly > cell - 3;
                if (panelSeam) wall *= 0.55f;
                if (masonry && ly % 14 < 1) wall *= 0.72f;
                if (!masonry && lx % 32 < 2) wall *= 0.82f;

                Color32 wallColor = new Color32(
                    (byte)(Mathf.Clamp01(wall * 0.82f) * 255f),
                    (byte)(Mathf.Clamp01(wall * 0.95f) * 255f),
                    (byte)(Mathf.Clamp01(wall) * 255f), 255);

                int windowLeft = masonry ? 28 : 18;
                int windowRight = masonry ? 100 : 110;
                int windowBottom = masonry ? 26 : 18;
                int windowTop = masonry ? 96 : 110;
                bool inWindow = lx >= windowLeft && lx <= windowRight &&
                                ly >= windowBottom && ly <= windowTop;
                bool cellLit = cellHash > (masonry ? 0.50f : 0.63f);
                float spillHue = Hash01(cellX + 5, cellY + 11, seed + 97);

                if (inWindow)
                {
                    int frame = masonry ? 5 : 4;
                    bool windowFrame = lx < windowLeft + frame || lx > windowRight - frame ||
                                       ly < windowBottom + frame || ly > windowTop - frame;
                    if (windowFrame)
                    {
                        pixels[y * size + x] = new Color32(12, 18, 26, 255);
                        continue;
                    }

                    bool mullion = lx >= 61 && lx <= 66;
                    if (!masonry && (ly >= 60 && ly <= 64)) mullion = true;
                    if (mullion)
                    {
                        pixels[y * size + x] = new Color32(9, 14, 22, 255);
                        continue;
                    }

                    bool lit = cellHash > (masonry ? 0.50f : 0.63f);
                    Color windowColor = new Color(0.018f, 0.035f, 0.065f);
                    if (lit)
                    {
                        float hueRoll = Hash01(cellX + 5, cellY + 11, seed + 97);
                        Color tint;
                        if (hueRoll < 0.58f) tint = new Color(0.34f, 0.55f, 0.78f);
                        else if (hueRoll < 0.85f) tint = new Color(0.72f, 0.30f, 0.55f);
                        else tint = new Color(0.78f, 0.55f, 0.30f);
                        tint = Color.Lerp(tint, Color.white, 0.12f);
                        float brightness = Mathf.Lerp(0.32f, 0.95f,
                            Hash01(cellX + 3, cellY + 23, seed + 131));
                        windowColor = tint * brightness;
                        float curtainRoll = Hash01(cellX + 9, cellY + 7, seed + 157);
                        if (curtainRoll > 0.74f && ly > windowTop - 30)
                            windowColor *= 0.38f;
                        else if (curtainRoll < 0.18f && ly < windowBottom + 34)
                            windowColor *= 0.55f;
                        // Desks, partitions and blinds give the lit panes depth at close
                        // range instead of leaving them as flat saturated rectangles.
                        float deskRoll = Hash01(cellX + 17, cellY + 29, seed + 181);
                        if (deskRoll > 0.42f)
                        {
                            int deskTop = windowBottom + 14 + (int)(deskRoll * 24f);
                            if (ly < deskTop) windowColor *= 0.5f;
                        }
                        float blindRoll = Hash01(cellX + 31, cellY + 13, seed + 211);
                        if (blindRoll > 0.58f && ly % 15 < 2) windowColor *= 0.55f;
                    }
                    float glassGradient = Mathf.Lerp(0.72f, 1.15f,
                        (ly - windowBottom) / (float)Mathf.Max(1, windowTop - windowBottom));
                    windowColor *= glassGradient;
                    // Reflected neon streaks keep the glass from reading as flat paper.
                    if ((x + cellY * 17) % 61 < 3) windowColor += new Color(0.08f, 0.02f, 0.12f);
                    pixels[y * size + x] = new Color32(
                        (byte)(Mathf.Clamp01(windowColor.r) * 255f),
                        (byte)(Mathf.Clamp01(windowColor.g) * 255f),
                        (byte)(Mathf.Clamp01(windowColor.b) * 255f), 255);
                }
                else
                {
                    // Light spilling out of lit panes softens the hard window/wall edge and
                    // makes the unlit facade read as a surface rather than a black cut-out.
                    if (cellLit)
                    {
                        int dx = Mathf.Max(windowLeft - lx, lx - windowRight);
                        int dy = Mathf.Max(windowBottom - ly, ly - windowTop);
                        int distance = Mathf.Max(dx, dy);
                        if (distance <= 12)
                        {
                            float falloff = 1f - distance / 12f;
                            falloff *= falloff;
                            Color spill = spillHue < 0.58f
                                ? new Color(0.10f, 0.22f, 0.38f)
                                : (spillHue < 0.85f
                                    ? new Color(0.38f, 0.13f, 0.28f)
                                    : new Color(0.38f, 0.26f, 0.13f));
                            Color wallWash = wallColor;
                            wallColor = To32(wallWash + spill * (falloff * 0.46f));
                        }
                    }
                    // Sills, balcony bands and occasional service boxes interrupt the grid.
                    if (masonry && ly >= windowBottom - 5 && ly <= windowBottom - 2)
                        wallColor = new Color32(31, 36, 42, 255);
                    if (!masonry && cellHash > 0.68f && lx > 96 && lx < 116 && ly > 28 && ly < 48)
                        wallColor = new Color32(24, 31, 38, 255);
                    if (masonry && cellHash > 0.58f && ly > 4 && ly < 17 &&
                        lx > 22 && lx < 106 && (lx % 18 < 2 || ly < 7))
                        wallColor = new Color32(45, 48, 52, 255);
                    pixels[y * size + x] = wallColor;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// Lit shop interior seen through a glazed frontage. Shelves, product blocks, a warm
        /// ceiling wash and diagonal glass reflections stop the city storefronts from looking
        /// like repeated UI posters.
        /// </summary>
        public static Texture2D ShopWindow(int seed, Color accent, int variant = 0)
        {
            const int w = 512, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = "NV_Shop_" + seed,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            var rng = new System.Random(seed);
            var pixels = new Color32[w * h];
            Color warm = Color.Lerp(new Color(1f, 0.61f, 0.28f), accent, 0.18f);
            Color pale = Color.Lerp(accent, Color.white, 0.58f);
            Color dark = new Color(0.010f, 0.016f, 0.028f);

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = dark;
                if (x > 10 && x < w - 10 && y > 12 && y < h - 16)
                {
                    float v = (y - 12f) / (h - 28f);
                    c = Color.Lerp(new Color(0.018f, 0.026f, 0.043f),
                        new Color(0.105f, 0.071f, 0.044f), Mathf.Pow(v, 1.75f));
                    c += accent * (0.008f + 0.022f * v);
                    if ((x % 86) < 2) c *= 0.58f;
                }
                pixels[y * w + x] = To32(c);
            }

            // Glazed frame, headers and a bright neon reveal.
            FillRect(pixels, w, h, 0, 0, w, 10, new Color32(6, 9, 14, 255));
            FillRect(pixels, w, h, 0, h - 12, w, 12, new Color32(7, 10, 16, 255));
            FillRect(pixels, w, h, 14, 12, w - 28, 3, To32(pale));
            FillRect(pixels, w, h, 14, h - 18, w - 28, 3, To32(accent * 0.8f));
            FillRect(pixels, w, h, 10, 12, 2, h - 30, new Color32(9, 12, 18, 255));

            // Warm ceiling strips near the top of the window, washing down over the
            // shelving. Texture v=0 is the pavement edge on a cube face.
            for (int light = 0; light < 4; light++)
            {
                int lx = 50 + light * 108;
                int ly = h - 30;
                FillRect(pixels, w, h, lx, ly, 70, 4, To32(warm * 1.6f));
                for (int glow = 1; glow < 10; glow++)
                    for (int gx = lx - 5; gx < lx + 75; gx++)
                        BlendPixel(pixels, w, h, gx, ly - glow, warm, 0.07f / glow);
            }

            // Stocked shelves, with varying product colours.
            Color[] productColours =
            {
                Color.Lerp(accent, Palette.White, 0.2f),
                Palette.Amber,
                Color.Lerp(Palette.Magenta, Palette.PanelBright, 0.55f),
                Color.Lerp(Palette.Cyan, Palette.PanelBright, 0.35f),
                Palette.White
            };
            for (int shelf = 0; shelf < 3; shelf++)
            {
                int sy = 71 + shelf * 49;
                FillRect(pixels, w, h, 16, sy, w - 32, 4, new Color32(29, 24, 19, 255));
                FillRect(pixels, w, h, 16, sy + 4, w - 32, 1, new Color32(75, 52, 31, 255));
                int cursor = 22;
                while (cursor < w - 40)
                {
                    int pw = rng.Next(14, 30);
                    int ph = rng.Next(13, 32);
                    if (cursor + pw > w - 26) break;
                    Color product = productColours[rng.Next(0, productColours.Length)];
                    product = Color.Lerp(product, new Color(0.22f, 0.16f, 0.11f), 0.34f) *
                              (0.55f + (float)rng.NextDouble() * 0.32f);
                    FillRect(pixels, w, h, cursor, sy + 5, pw, ph, To32(product));
                    FillRect(pixels, w, h, cursor, sy + 5 + ph - 2, pw, 2,
                        To32(Color.Lerp(product, Color.white, 0.55f)));
                    cursor += pw + rng.Next(10, 26);
                }
            }

            // Dark counter at pavement level, visible below the lowest shelf.
            FillRect(pixels, w, h, 16, 18, w - 32, 35, new Color32(11, 13, 17, 255));
            FillRect(pixels, w, h, 16, 49, w - 32, 3, To32(accent * 0.65f));

            // Diagonal reflected highlights on the glass.
            for (int streak = 0; streak < 6; streak++)
            {
                int offset = 20 + streak * 88;
                for (int y = 14; y < h - 24; y += 2)
                {
                    int x = offset + (h - y) / 2;
                    BlendPixel(pixels, w, h, x, y, new Color(0.55f, 0.72f, 0.9f), 0.055f);
                    BlendPixel(pixels, w, h, x + 1, y, new Color(0.55f, 0.72f, 0.9f), 0.025f);
                }
            }

            // Cool glass wash and fine horizontal banding from the glazing bars.
            for (int y = 14; y < h - 18; y++)
            for (int x = 12; x < w - 12; x++)
            {
                var c = pixels[y * w + x];
                Color cooled = Color.Lerp(new Color(c.r / 255f, c.g / 255f, c.b / 255f),
                    new Color(0.055f, 0.085f, 0.13f), 0.16f);
                if ((y & 3) == 0) cooled *= 0.92f;
                pixels[y * w + x] = To32(cooled);
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// Large vertical neon poster used for street media panels and blade signs. The
        /// composition is deliberately graphical rather than a grid of boxed characters,
        /// so it still reads as an advertisement at speed.
        /// </summary>
        public static Texture2D BillboardArt(int seed, Color accent, int variant = 0)
        {
            const int w = 256, h = 512;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = "NV_Ad_" + seed,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };
            var rng = new System.Random(seed);
            var pixels = new Color32[w * h];
            Color secondary = variant % 2 == 0 ? Palette.Magenta : Palette.Cyan;
            Color pale = Color.Lerp(accent, Color.white, 0.55f);
            Color dark = new Color(0.008f, 0.012f, 0.028f);

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = y / (h - 1f);
                Color c = Color.Lerp(new Color(0.012f, 0.018f, 0.04f), accent * 0.18f,
                    Mathf.Pow(1f - v, 1.8f));
                float band = Mathf.Pow(Mathf.Max(0f,
                    Mathf.Sin((x / (float)w * 1.3f + v * 0.9f) * Mathf.PI * 2f)), 7f);
                c += secondary * band * 0.028f;
                c += new Color(0.006f, 0.008f, 0.014f) * (x / (float)w);
                pixels[y * w + x] = To32(c);
            }

            // Frame and divider.
            FillRect(pixels, w, h, 5, 5, 4, h - 10, To32(accent * 0.9f));
            FillRect(pixels, w, h, 9, 5, w - 14, 3, To32(pale));
            FillRect(pixels, w, h, 9, h - 9, w - 14, 4, To32(secondary * 0.8f));

            // Primary graphic.
            if (variant % 3 == 0)
            {
                float cy = h * 0.58f;
                float cx = w * 0.55f;
                float radius = 77f;
                for (int y = (int)(cy - radius - 20f); y < cy + radius + 20f; y++)
                for (int x = (int)(cx - radius - 20f); x < cx + radius + 20f; x++)
                {
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (Mathf.Abs(dist - radius) < 4.5f) BlendPixel(pixels, w, h, x, y, pale, 0.9f);
                    else if (dist < radius + 16f && dist > radius + 3f)
                        BlendPixel(pixels, w, h, x, y, accent, 0.09f);
                    if (dist < 18f) BlendPixel(pixels, w, h, x, y, secondary, 0.28f);
                }
                FillRect(pixels, w, h, 24, (int)(h * 0.27f), w - 48, 4, To32(accent * 0.7f));
            }
            else if (variant % 3 == 1)
            {
                for (int beam = 0; beam < 3; beam++)
                {
                    int offset = 66 + beam * 70;
                    for (int y = 90; y < h - 60; y++)
                    {
                        int x = offset + (y - 90) / 3;
                        BlendPixel(pixels, w, h, x, y, beam == 1 ? secondary : accent, 0.5f);
                        BlendPixel(pixels, w, h, x + 4, y, accent, 0.12f);
                    }
                }
                FillRect(pixels, w, h, 26, h - 140, 204, 3, To32(pale));
            }
            else
            {
                for (int bar = 0; bar < 5; bar++)
                {
                    int bx = 24 + bar * 42;
                    int bh = 110 + rng.Next(0, 180);
                    FillRect(pixels, w, h, bx, 110, 22, bh,
                        To32(Color.Lerp(accent, secondary, bar * 0.18f) * 0.55f));
                }
                for (int ring = 0; ring < 3; ring++)
                {
                    int br = 24 + ring * 15;
                    for (int y = (int)(h * 0.36f) - br; y < (int)(h * 0.36f) + br; y++)
                    for (int x = (int)(w * 0.5f) - br; x < (int)(w * 0.5f) + br; x++)
                    {
                        float d = Mathf.Sqrt((x - w * 0.5f) * (x - w * 0.5f) +
                                             (y - h * 0.36f) * (y - h * 0.36f));
                        if (Mathf.Abs(d - br) < 3f) BlendPixel(pixels, w, h, x, y, pale, 0.7f);
                    }
                }
            }

            // Large abstract mark near the top.
            FillRect(pixels, w, h, 24, h - 96, 6, 54, To32(pale));
            FillRect(pixels, w, h, 91, h - 96, 6, 54, To32(pale));
            for (int step = 0; step < 52; step++)
                BlendPixel(pixels, w, h, 29 + step, h - 94 + step, secondary, 0.75f);
            FillRect(pixels, w, h, 118, h - 74, 78, 3, To32(accent * 0.8f));
            FillRect(pixels, w, h, 118, h - 62, 48, 2, To32(secondary * 0.8f));

            // Small brand marks and scanlines.
            for (int mark = 0; mark < 4; mark++)
                FillRect(pixels, w, h, 24 + mark * 23, 28, 13, 3, To32(accent * (0.5f + mark * 0.1f)));
            for (int y = 0; y < h; y += 3)
            for (int x = 12; x < w - 12; x++)
            {
                var c = pixels[y * w + x];
                pixels[y * w + x] = new Color32((byte)(c.r * 0.9f), (byte)(c.g * 0.9f),
                    (byte)(c.b * 0.9f), 255);
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Soft-edged streak texture for the stretched rain billboards.</summary>
        public static Texture2D RainStreak(int width = 16, int height = 96)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "NV_RainStreak",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float ny = y / (height - 1f);
                float ends = Mathf.SmoothStep(0f, 0.10f, ny) *
                             (1f - Mathf.SmoothStep(0.82f, 1f, ny));
                for (int x = 0; x < width; x++)
                {
                    float nx = x / (width - 1f) * 2f - 1f;
                    float edge = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(nx)), 2.4f);
                    float core = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(nx) * 1.7f), 8f);
                    float alpha = Mathf.Clamp01(edge * ends * (0.75f + core * 0.25f));
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Color32 To32(Color c) => new Color32(
            (byte)(Mathf.Clamp01(c.r) * 255f),
            (byte)(Mathf.Clamp01(c.g) * 255f),
            (byte)(Mathf.Clamp01(c.b) * 255f), 255);

        private static void BlendPixel(Color32[] pixels, int w, int h, int x, int y, Color c, float alpha)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            alpha = Mathf.Clamp01(alpha);
            int index = y * w + x;
            Color dst = pixels[index];
            pixels[index] = new Color32(
                (byte)(Mathf.Clamp01(dst.r / 255f + c.r * alpha) * 255f),
                (byte)(Mathf.Clamp01(dst.g / 255f + c.g * alpha) * 255f),
                (byte)(Mathf.Clamp01(dst.b / 255f + c.b * alpha) * 255f),
                255);
        }

        /// <summary>
        /// Soft volumetric-looking lamp cone. Used as an additive quad below street lamps:
        /// narrow at the fixture, wide and fading toward the road. No extra real lights.
        /// </summary>
        public static Texture2D LightShaft(int width = 64, int height = 256)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "NV_LightShaft",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float ny = y / (float)(height - 1);
                float halfWidth = Mathf.Lerp(0.48f, 0.055f, ny);
                float vertical = Mathf.SmoothStep(0f, 0.13f, ny) * Mathf.Pow(ny, 0.58f);
                for (int x = 0; x < width; x++)
                {
                    float nx = x / (float)(width - 1) * 2f - 1f;
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(nx) / halfWidth);
                    edge *= edge;
                    float wisps = 0.82f + 0.18f * Fbm(x / (float)width, ny * 0.45f, 9, 317);
                    float a = Mathf.Clamp01(edge * vertical * wisps);
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
