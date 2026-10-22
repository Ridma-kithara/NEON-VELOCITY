using UnityEngine;

namespace NeonVelocity.Utilities
{
    public static class Palette
    {
        public static readonly Color Void = Hex("#050712");
        public static readonly Color DeepPanel = Hex("#0A1020");
        public static readonly Color Panel = Hex("#101A31");
        public static readonly Color PanelBright = Hex("#172744");
        public static readonly Color Cyan = Hex("#28F6FF");
        public static readonly Color Magenta = Hex("#FF2E9A");
        public static readonly Color Lime = Hex("#B6FF3B");
        public static readonly Color Amber = Hex("#FFD166");
        public static readonly Color Orange = Hex("#FF8A3D");
        public static readonly Color Violet = Hex("#A78BFA");
        public static readonly Color Red = Hex("#FF3B5C");
        public static readonly Color White = Hex("#EAF6FF");
        public static readonly Color Muted = Hex("#7C93B8");
        public static readonly Color DeepBlue = Hex("#071428");

        public static Color Hex(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color color)) return color;
            return Color.white;
        }

        public static Color WithAlpha(this Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        public static Color Brighten(Color c, float amount) =>
            new Color(Mathf.Clamp01(c.r + amount), Mathf.Clamp01(c.g + amount), Mathf.Clamp01(c.b + amount), c.a);

        /// <summary>HDR colour for emission and bloom-friendly particles.</summary>
        public static Color Hdr(Color c, float intensity) =>
            new Color(c.r * intensity, c.g * intensity, c.b * intensity, c.a);

        public static Color BiomeAccent(int biome) => biome switch
        {
            0 => Cyan,
            1 => Amber,
            2 => Lime,
            3 => Violet,
            _ => Magenta
        };
    }

    public static class UiText
    {
        public const string Studio = "RK VANTAGE GAMES";
        public const string GameTitle = "NEON VELOCITY";
        public const string MenuHint = "A / D  or  \u2190 \u2192   SWITCH LANES     SPACE   JUMP     S   SLIDE";
    }
}
