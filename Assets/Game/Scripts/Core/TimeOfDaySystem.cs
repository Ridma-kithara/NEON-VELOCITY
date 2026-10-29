using System;
using System.Globalization;

namespace NeonVelocity.Core
{
    /// <summary>The seven authored lighting states requested for every environment.</summary>
    public enum TimeOfDayPhase
    {
        EarlyMorning = 0,
        Morning = 1,
        Afternoon = 2,
        Evening = 3,
        Sunset = 4,
        Night = 5,
        LateNight = 6
    }

    /// <summary>Engine-free RGB used by the shared Core assembly.</summary>
    public readonly struct TodColor
    {
        public readonly float R;
        public readonly float G;
        public readonly float B;

        public TodColor(float r, float g, float b)
        {
            R = Math.Max(0f, r);
            G = Math.Max(0f, g);
            B = Math.Max(0f, b);
        }

        public static TodColor Hex(string value)
        {
            if (string.IsNullOrEmpty(value)) return new TodColor(1f, 1f, 1f);
            string hex = value[0] == '#' ? value.Substring(1) : value;
            if (hex.Length != 6) return new TodColor(1f, 1f, 1f);
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int packed))
                return new TodColor(1f, 1f, 1f);
            return new TodColor(((packed >> 16) & 0xFF) / 255f, ((packed >> 8) & 0xFF) / 255f,
                (packed & 0xFF) / 255f);
        }

        public string ToHex() => "#" + MathfRound(R).ToString("X2") + MathfRound(G).ToString("X2") +
                                 MathfRound(B).ToString("X2");

        public static TodColor Lerp(TodColor a, TodColor b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return new TodColor(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        }

        private static int MathfRound(float v) => Math.Max(0, Math.Min(255, (int)Math.Round(v * 255f)));
    }

    /// <summary>
    /// One lighting state. Values are deliberately explicit: the environment binder reads
    /// every field instead of guessing a mood from a colour. Authored locations can override
    /// the preferred phase, while the global dynamic cycle blends these profiles continuously.
    /// </summary>
    public sealed class TimeOfDayProfile
    {
        public TimeOfDayPhase Phase;
        public string Id;
        public string Label;
        public string Description;
        public float SunElevation;
        public float SunAzimuth;
        public float SunIntensity;
        public float MoonIntensity;
        public float AmbientIntensity;
        public float ReflectionIntensity;
        public float ShadowStrength;
        public float FogDensity;
        public float NeonIntensity;
        public float StreetlightIntensity;
        public float StarVisibility;
        public float WeatherIntensity;
        public string SunColorHex;
        public string SkyTopHex;
        public string SkyBottomHex;
        public string FogHex;
        public string MoonColorHex;
        public string CloudTintHex;
    }

    public readonly struct TimeOfDayState
    {
        public readonly TimeOfDayPhase Phase;
        public readonly TimeOfDayPhase NextPhase;
        public readonly float PhaseBlend;
        public readonly string Label;
        public readonly float SunElevation;
        public readonly float SunAzimuth;
        public readonly float SunIntensity;
        public readonly float MoonIntensity;
        public readonly float AmbientIntensity;
        public readonly float ReflectionIntensity;
        public readonly float ShadowStrength;
        public readonly float FogDensity;
        public readonly float NeonIntensity;
        public readonly float StreetlightIntensity;
        public readonly float StarVisibility;
        public readonly float WeatherIntensity;
        public readonly TodColor SunColor;
        public readonly TodColor SkyTop;
        public readonly TodColor SkyBottom;
        public readonly TodColor Fog;
        public readonly TodColor MoonColor;
        public readonly TodColor CloudTint;

        public TimeOfDayState(TimeOfDayProfile a, TimeOfDayProfile b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            Phase = a.Phase;
            NextPhase = b.Phase;
            PhaseBlend = t;
            Label = t < 0.5f ? a.Label : b.Label;
            SunElevation = Lerp(a.SunElevation, b.SunElevation, t);
            SunAzimuth = Lerp(a.SunAzimuth, b.SunAzimuth, t);
            SunIntensity = Lerp(a.SunIntensity, b.SunIntensity, t);
            MoonIntensity = Lerp(a.MoonIntensity, b.MoonIntensity, t);
            AmbientIntensity = Lerp(a.AmbientIntensity, b.AmbientIntensity, t);
            ReflectionIntensity = Lerp(a.ReflectionIntensity, b.ReflectionIntensity, t);
            ShadowStrength = Lerp(a.ShadowStrength, b.ShadowStrength, t);
            FogDensity = Lerp(a.FogDensity, b.FogDensity, t);
            NeonIntensity = Lerp(a.NeonIntensity, b.NeonIntensity, t);
            StreetlightIntensity = Lerp(a.StreetlightIntensity, b.StreetlightIntensity, t);
            StarVisibility = Lerp(a.StarVisibility, b.StarVisibility, t);
            WeatherIntensity = Lerp(a.WeatherIntensity, b.WeatherIntensity, t);
            SunColor = TodColor.Lerp(TodColor.Hex(a.SunColorHex), TodColor.Hex(b.SunColorHex), t);
            SkyTop = TodColor.Lerp(TodColor.Hex(a.SkyTopHex), TodColor.Hex(b.SkyTopHex), t);
            SkyBottom = TodColor.Lerp(TodColor.Hex(a.SkyBottomHex), TodColor.Hex(b.SkyBottomHex), t);
            Fog = TodColor.Lerp(TodColor.Hex(a.FogHex), TodColor.Hex(b.FogHex), t);
            MoonColor = TodColor.Lerp(TodColor.Hex(a.MoonColorHex), TodColor.Hex(b.MoonColorHex), t);
            CloudTint = TodColor.Lerp(TodColor.Hex(a.CloudTintHex), TodColor.Hex(b.CloudTintHex), t);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }

    /// <summary>
    /// The seven state catalogue and its pure blending rules. The runtime environment binder
    /// consumes the state; tests consume the same rules, so day/night definitions cannot drift
    /// between the editor, player and headless validation.
    /// </summary>
    public static class TimeOfDayCatalog
    {
        public const int Count = 7;
        public const float DefaultCycleMetres = 21_000f;
        public const float MinimumCycleMetres = 3_000f;
        public const float MaximumCycleMetres = 105_000f;

        public static readonly TimeOfDayProfile[] All =
        {
            Profile(TimeOfDayPhase.EarlyMorning, "early-morning", "EARLY MORNING",
                "Cold blue light, low haze and the first transit lamps waking up.",
                -4f, 105f, 0.24f, 0.34f, 0.46f, 0.42f, 0.22f, 0.0105f, 0.82f, 0.62f, 0.28f, 0.72f,
                "#8598C8", "#16223D", "#6E7897", "#1A263D", "#B9C8FF", "#5A6D8F"),
            Profile(TimeOfDayPhase.Morning, "morning", "MORNING",
                "Clear directional sun, long soft shadows and thin commuter traffic.",
                18f, 88f, 0.78f, 0.12f, 0.78f, 0.68f, 0.58f, 0.0080f, 0.36f, 0.34f, 0.05f, 0.92f,
                "#FFE6C2", "#29445E", "#8CA8C0", "#24384A", "#D6E4FF", "#7D9CBA"),
            Profile(TimeOfDayPhase.Afternoon, "afternoon", "AFTERNOON",
                "High white sun, crisp reflections and the strongest shadow contrast.",
                48f, 38f, 1.15f, 0f, 1.02f, 0.92f, 0.82f, 0.0062f, 0.18f, 0.12f, 0f, 1f,
                "#FFF4DD", "#1E5C84", "#B8D8E6", "#284452", "#C9E8FF", "#A9C9DC"),
            Profile(TimeOfDayPhase.Evening, "evening", "EVENING",
                "Amber sun, blue shadow fill and the first neon signs reflecting in the road.",
                20f, 100f, 0.72f, 0.10f, 0.70f, 0.72f, 0.54f, 0.0080f, 0.52f, 0.58f, 0.10f, 0.88f,
                "#FFB36F", "#2A2147", "#B96F70", "#2A1F38", "#D7C4FF", "#7A5C86"),
            Profile(TimeOfDayPhase.Sunset, "sunset", "SUNSET",
                "Low copper sun, magenta horizon and heavy atmospheric perspective.",
                7f, 118f, 0.46f, 0.18f, 0.56f, 0.66f, 0.38f, 0.0092f, 0.72f, 0.82f, 0.24f, 0.82f,
                "#FF8A4C", "#331A46", "#E46F87", "#3B1E37", "#FFD2C4", "#9B5B86"),
            Profile(TimeOfDayPhase.Night, "night", "NIGHT",
                "Moonlit blue ambience, full neon presence and pooled street lighting.",
                -28f, 148f, 0.06f, 0.46f, 0.34f, 0.54f, 0.16f, 0.0110f, 1f, 1f, 1f, 0.78f,
                "#839BFF", "#050914", "#16233E", "#08101E", "#B9D2FF", "#26385C"),
            Profile(TimeOfDayPhase.LateNight, "late-night", "LATE NIGHT",
                "Deepest blue, maximum neon bloom, fog banks and almost no daylight.",
                -44f, 172f, 0f, 0.55f, 0.24f, 0.46f, 0.08f, 0.0130f, 1.18f, 1.12f, 1f, 0.70f,
                "#6E85E8", "#03050C", "#0B1728", "#050B14", "#AFC8FF", "#1A2942")
        };

        public static TimeOfDayProfile Get(TimeOfDayPhase phase) => All[ClampPhase((int)phase)];

        public static int ClampPhase(int phase)
        {
            if (phase < 0) return 0;
            if (phase >= Count) return Count - 1;
            return phase;
        }

        public static TimeOfDayPhase ClampPhase(int phase, bool wrap)
        {
            if (!wrap) return (TimeOfDayPhase)ClampPhase(phase);
            int wrapped = phase % Count;
            return (TimeOfDayPhase)(wrapped < 0 ? wrapped + Count : wrapped);
        }

        public static TimeOfDayState Blend(TimeOfDayPhase from, TimeOfDayPhase to, float t) =>
            new TimeOfDayState(Get(from), Get(to), t);

        public static TimeOfDayState ForPhase(TimeOfDayPhase phase) =>
            new TimeOfDayState(Get(phase), Get(phase), 0f);

        /// <summary>
        /// Continuous dynamic cycle. One state per three kilometres gives a complete seven-state
        /// day every 21 km while every 3 km boundary still lands exactly on a lighting change.
        /// </summary>
        public static TimeOfDayState ForDistance(float distance, float cycleMetres = DefaultCycleMetres,
            int phaseOffset = 3)
        {
            if (float.IsNaN(cycleMetres) || float.IsInfinity(cycleMetres) ||
                cycleMetres < MinimumCycleMetres) cycleMetres = MinimumCycleMetres;
            if (cycleMetres > MaximumCycleMetres) cycleMetres = MaximumCycleMetres;
            float progress = (Math.Max(0f, distance) / cycleMetres) * Count + phaseOffset;
            int index = (int)Math.Floor(progress);
            float blend = SmoothStep(progress - index);
            int a = index % Count;
            int b = (index + 1) % Count;
            if (a < 0) a += Count;
            if (b < 0) b += Count;
            return new TimeOfDayState(All[a], All[b], blend);
        }

        /// <summary>Resolve the requested mode. 0 authored location, 1 dynamic, 2 fixed.</summary>
        public static TimeOfDayState Resolve(int mode, int fixedPhase, float distance, int locationPhase,
            float cycleMetres = DefaultCycleMetres)
        {
            if (mode == 2) return ForPhase((TimeOfDayPhase)ClampPhase(fixedPhase));
            if (mode == 1) return ForDistance(distance, cycleMetres);
            return ForPhase((TimeOfDayPhase)ClampPhase(locationPhase));
        }

        public static string Label(int phase) => Get((TimeOfDayPhase)ClampPhase(phase)).Label;

        public static string[] Labels()
        {
            var labels = new string[Count];
            for (int i = 0; i < Count; i++) labels[i] = All[i].Label;
            return labels;
        }

        private static float SmoothStep(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return t * t * (3f - 2f * t);
        }

        private static TimeOfDayProfile Profile(TimeOfDayPhase phase, string id, string label, string description,
            float sunElevation, float sunAzimuth, float sunIntensity, float moonIntensity,
            float ambientIntensity, float reflectionIntensity, float shadowStrength, float fogDensity,
            float neonIntensity, float streetlightIntensity, float starVisibility, float weatherIntensity,
            string sunColor, string skyTop, string skyBottom, string fog, string moonColor, string cloudTint)
        {
            return new TimeOfDayProfile
            {
                Phase = phase, Id = id, Label = label, Description = description,
                SunElevation = sunElevation, SunAzimuth = sunAzimuth,
                SunIntensity = sunIntensity, MoonIntensity = moonIntensity,
                AmbientIntensity = ambientIntensity, ReflectionIntensity = reflectionIntensity,
                ShadowStrength = shadowStrength, FogDensity = fogDensity,
                NeonIntensity = neonIntensity, StreetlightIntensity = streetlightIntensity,
                StarVisibility = starVisibility, WeatherIntensity = weatherIntensity,
                SunColorHex = sunColor, SkyTopHex = skyTop, SkyBottomHex = skyBottom,
                FogHex = fog, MoonColorHex = moonColor, CloudTintHex = cloudTint
            };
        }
    }
}
