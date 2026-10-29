using System;
using System.Collections.Generic;

namespace NeonVelocity.Core
{
    /// <summary>
    /// One authored environment in the infinite grid. A new environment is entered every
    /// 3,000 m. The catalogue is engine-free so it can be shared by the runtime, the save
    /// migration and the headless tests. Every entry carries its own architecture family,
    /// road identity, landmark signature, palette, weather, time-of-day preference, ambience
    /// and procedural prefab keys so the runtime never has to infer a theme from colour alone.
    /// </summary>
    public sealed class LocationDefinition
    {
        public string id;
        public string displayName;
        public string subtitle;
        public string themeName;
        public string ambientLabel;
        /// <summary>Architecture family consumed by TrackManager's procedural builders.</summary>
        public int style;
        /// <summary>Unique landmark signature consumed by BuildSignature.</summary>
        public int signature;
        public int hazardBias;
        /// <summary>0 clear, 1 rain, 2 snow, 3 dust, 4 embers, 5 fog.</summary>
        public int weather;
        /// <summary>0 Early Morning, 1 Morning, 2 Afternoon, 3 Evening, 4 Sunset, 5 Night, 6 Late Night.</summary>
        public int timeOfDay;
        public bool rain;
        public string accentHex;
        public string skyTopHex;
        public string skyBottomHex;
        public string fogHex;
        public string groundHex;
        public string buildingAHex;
        public string buildingBHex;
        /// <summary>Stable key for the authored environment prefab; procedural fallback is style.</summary>
        public string environmentPrefab;
        /// <summary>Stable key for the authored road-section prefab; procedural fallback is roadStyle.</summary>
        public string roadPrefab;
        public string roadStyle;
        public string ambienceId;
        public string musicId;

        public TimeOfDayPhase PreferredTimeOfDay => (TimeOfDayPhase)TimeOfDayCatalog.ClampPhase(timeOfDay);
    }

    /// <summary>
    /// The twenty requested environment themes and no filler entries. Environments are a pure
    /// function of distance, so live revives, seeks and multiplayer peers resolve the same
    /// place at the same metre. The list contains no immediate repeats; wrapping from the
    /// skybridge back to Japan is also a clean theme change.
    /// </summary>
    public static class LocationCatalog
    {
        public const float ZoneLength = 3_000f;
        public const int Count = 20;

        public static readonly LocationDefinition[] All =
        {
            Loc("sakura-circuit", "SAKURA CIRCUIT", "NEON PETAL EXPRESSWAY", "FUTURISTIC JAPAN", "PETAL WIND",
                10, 15, 2, 0, 4, false, "#FF7BAC", "#120A1E", "#3A1730", "#1F1024", "#141018", "#2A1A2C", "#3D213B",
                "sakura", "petal-deck", "ambience_city", "music_run"),
            Loc("seoul-nexus", "SEOUL NEXUS", "HOLOGRAPHIC RIVER SPINE", "FUTURISTIC SOUTH KOREA", "RIVER SIGNAL",
                13, 1, 1, 0, 5, false, "#4DDFFF", "#050B18", "#123A5A", "#0A2034", "#0B1220", "#142A40", "#1D3550",
                "seoul", "hologrid", "ambience_city", "music_run_alt"),
            Loc("pacific-verge", "PACIFIC VERGE", "CLIFFSIDE SOLAR HIGHWAY", "CALIFORNIA COASTAL HIGHWAY", "PACIFIC WIND",
                6, 12, 1, 0, 2, false, "#FFB45C", "#0A1B2E", "#FFD3A0", "#2A3A44", "#1A2630", "#24343E", "#30434C",
                "pacific", "coastal-deck", "ambience_coast", "music_run"),
            Loc("maple-frost", "MAPLE FROST", "ROCKIES AVALANCHE BYPASS", "SNOWY CANADIAN MOUNTAINS", "ALPINE GALE",
                4, 6, 2, 2, 0, false, "#BFE9FF", "#081420", "#2A4A66", "#122A3A", "#101A22", "#223845", "#2E4654",
                "maple", "snow-deck", "ambience_snow", "music_run_alt"),
            Loc("cryo-harbor", "CRYO HARBOR", "ARCTIC FABRICATION TERMINAL", "FROZEN INDUSTRIAL REGION", "CRYO VENT",
                11, 2, 3, 2, 0, false, "#9FE8FF", "#04101C", "#0B2A46", "#082033", "#0A121A", "#17303F", "#1B3A4A",
                "cryo", "frozen-deck", "ambience_reactor", "music_run"),
            Loc("solar-arcology", "SOLAR ARCOLOGY", "ORBITAL MIRROR FARM", "FUTURISTIC DUBAI-INSPIRED CITY", "MIRROR WIND",
                5, 5, 1, 0, 2, false, "#FFB347", "#30101A", "#FF9A3D", "#5A2418", "#2A1710", "#452819", "#5E311B",
                "arcology", "desert-deck", "ambience_city", "music_run_alt"),
            Loc("eurodyne-spire", "EURODYNE SPIRE", "CONTINENTAL DATA PROMENADE", "EUROPEAN CYBERPUNK METROPOLIS", "PLAZA WIND",
                8, 11, 2, 1, 3, true, "#FF4FD8", "#0B1226", "#2C3E66", "#18243B", "#10141E", "#2A2A33", "#3A3540",
                "eurodyne", "euro-deck", "ambience_rain", "music_run"),
            Loc("coral-expanse", "CORAL EXPANSE", "TIDAL GARDEN CAUSEWAY", "TROPICAL BEACH AND ISLAND", "REEF SURF",
                6, 19, 1, 0, 1, false, "#3BFFD1", "#031A28", "#0B4A5C", "#07303C", "#0A1A1F", "#14343A", "#1E4650",
                "coral", "island-deck", "ambience_coast", "music_run_alt"),
            Loc("dune-meridian", "DUNE MERIDIAN", "SUNKEN ARCOLOGY HIGHWAY", "DESERT MEGACITY", "DESERT GALE",
                5, 16, 3, 3, 2, false, "#FFC65C", "#2B1638", "#FF9457", "#4A2E2A", "#3A281C", "#4A3524", "#5E432C",
                "dune", "sand-deck", "ambience_city", "music_run"),
            Loc("obsidian-line", "OBSIDIAN LINE", "END-OF-LINE SIGNAL VAULT", "UNDERGROUND FUTURISTIC RAILWAY", "TUNNEL ECHO",
                3, 3, 4, 5, 5, false, "#A78BFA", "#08040F", "#1C0E33", "#120A22", "#100C1B", "#1C1430", "#291B42",
                "obsidian", "rail-deck", "ambience_tunnel", "music_run_alt"),
            Loc("neon-tokyo", "NEON TOKYO", "SHIBUYA COURIER LAYER", "NEON TOKYO STREETS", "CROWD STATIC",
                10, 0, 2, 1, 5, true, "#FF2E9A", "#071227", "#241650", "#0A1830", "#0B1424", "#12243A", "#1B1638",
                "tokyo", "neon-deck", "ambience_rain", "music_run"),
            Loc("alpine-haven", "ALPINE HAVEN", "CLOUDLINE VILLAGE TERRACE", "FUTURISTIC MOUNTAIN VILLAGE", "HARBOR BELLS",
                12, 9, 1, 0, 0, false, "#FFD166", "#0B1726", "#334E63", "#1B2E3A", "#151E24", "#26343D", "#33454E",
                "alpine", "village-deck", "ambience_snow", "music_run_alt"),
            Loc("monsoon-quay", "MONSOON QUAY", "FLOODED CARGO TERMINAL", "RAINY COASTAL CITY", "MONSOON RAIN",
                6, 4, 2, 1, 3, true, "#41EAD4", "#061820", "#123B46", "#0A202A", "#0A1518", "#143039", "#1C3B44",
                "monsoon", "rain-deck", "ambience_rain", "music_run"),
            Loc("verdant-grid", "VERDANT GRID", "HYDROPONIC OVERPASS", "FUTURISTIC FOREST RESEARCH ZONE", "CANOPY DRIP",
                7, 8, 0, 1, 1, true, "#7CFFB2", "#06140C", "#173A22", "#0B2214", "#0A160F", "#183022", "#1E3A29",
                "verdant", "forest-deck", "ambience_forest", "music_run_alt"),
            Loc("kessler-reach", "KESSLER REACH", "ORBITAL LAUNCH APPROACH", "SPACEPORT CITY", "LAUNCH RUMBLE",
                15, 17, 2, 0, 0, false, "#7DE8FF", "#02040E", "#0A1A3C", "#071026", "#0A0E18", "#16213A", "#1E2C4C",
                "kessler", "launch-deck", "ambience_skyway", "music_run"),
            Loc("reactor-belt", "REACTOR BELT", "HOT CORE SERVICE RING", "INDUSTRIAL ROBOT DISTRICT", "REACTOR HUM",
                2, 7, 4, 0, 2, false, "#B6FF3B", "#06110E", "#112B1A", "#0C2018", "#111914", "#1C2A20", "#24321B",
                "reactor", "assembly-deck", "ambience_reactor", "music_run_alt"),
            Loc("aurora-pass", "AURORA PASS", "CONTINENTAL DIVIDE VIADUCT", "FUTURISTIC EUROPEAN MOUNTAIN HIGHWAY", "AURORA HUM",
                8, 13, 2, 0, 4, false, "#FF77C8", "#0B0720", "#3C1B52", "#21122F", "#171221", "#2A1D38", "#3A2948",
                "aurora", "viaduct-deck", "ambience_snow", "music_run"),
            Loc("halcyon-spire", "HALCYON SPIRE", "GOLDEN ALTITUDE CLUB", "LUXURY TECHNOLOGY DISTRICT", "SPIRE WINDS",
                1, 18, 0, 0, 4, false, "#FFE08A", "#131027", "#3A2A5C", "#241B3A", "#1A1622", "#2A2336", "#3B2C46",
                "halcyon", "luxury-deck", "ambience_skyway", "music_run_alt"),
            Loc("null-cathedral", "NULL CATHEDRAL", "SILENT DATA SANCTUM", "ABANDONED CYBER CITY", "CHOIR STATIC",
                16, 14, 3, 5, 6, true, "#B8C7FF", "#04050C", "#141A33", "#0A0D1C", "#0A0C14", "#161C30", "#1F2740",
                "null", "ruin-deck", "ambience_tunnel", "music_run"),
            Loc("skyway-09", "SKYWAY 09", "ELEVATED TRANSIT SPINE", "GIANT SKYBRIDGE MEGACITY", "HIGH ALTITUDE WIND",
                14, 10, 2, 5, 6, false, "#FFD166", "#2A1438", "#FF7A4D", "#52243B", "#241B26", "#3B2539", "#58293D",
                "skyway", "skybridge-deck", "ambience_skyway", "music_run_alt")
        };

        /// <summary>Exact zone index. 2,999.9 m is still zone 0; 3,000 m is zone 1.</summary>
        public static int ZoneIndexForDistance(float distance) =>
            Math.Max(0, (int)Math.Floor(Math.Max(0f, distance) / ZoneLength));

        public static int IndexForDistance(float distance) => Wrap(ZoneIndexForDistance(distance));

        public static int Wrap(int zoneIndex)
        {
            int wrapped = zoneIndex % Count;
            return wrapped < 0 ? wrapped + Count : wrapped;
        }

        public static float ZoneStart(int zoneIndex) => Math.Max(0, zoneIndex) * ZoneLength;

        public static LocationDefinition Get(int zoneIndex) => All[Wrap(zoneIndex)];

        public static LocationDefinition Find(string locationId)
        {
            if (string.IsNullOrEmpty(locationId)) return null;
            for (int i = 0; i < All.Length; i++)
                if (All[i] != null && All[i].id == locationId) return All[i];
            return null;
        }

        public static int IndexOf(string locationId)
        {
            if (string.IsNullOrEmpty(locationId)) return -1;
            for (int i = 0; i < All.Length; i++)
                if (All[i] != null && All[i].id == locationId) return i;
            return -1;
        }

        public static string NameForDistance(float distance) => Get(IndexForDistance(distance)).displayName;

        public static string IdForZone(int zoneIndex) => Get(zoneIndex).id;

        public static int NextZoneIndex(int zoneIndex) => Math.Max(0, zoneIndex) + 1;

        /// <summary>
        /// True when the segment contains a zone boundary. The runtime uses this to build the
        /// next theme before the player reaches the exact metre and to show the transition sign.
        /// Segment geometry changes on the next segment boundary, while the logical zone and
        /// HUD flip at the exact 3,000 m mark.
        /// </summary>
        public static bool SegmentCrossesBoundary(float segmentStart, float segmentLength)
        {
            if (segmentLength <= 0f) return false;
            int first = ZoneIndexForDistance(segmentStart);
            int last = ZoneIndexForDistance(segmentStart + segmentLength - 1f);
            return first != last;
        }

        /// <summary>Authored environment theme that follows the supplied zone, never the same.</summary>
        public static LocationDefinition NextAfter(int zoneIndex) => Get(Math.Max(0, zoneIndex) + 1);

        /// <summary>True only for the first discovery in this profile, not every re-entry.</summary>
        public static bool IsNewDiscovery(int zoneIndex, IReadOnlyCollection<string> discovered)
        {
            if (discovered == null || discovered.Count == 0) return true;
            string id = Get(zoneIndex).id;
            foreach (string known in discovered)
                if (string.Equals(known, id, StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>All twenty required themes, in catalogue order, for validation and docs.</summary>
        public static readonly string[] RequiredThemeNames =
        {
            "FUTURISTIC JAPAN",
            "FUTURISTIC SOUTH KOREA",
            "CALIFORNIA COASTAL HIGHWAY",
            "SNOWY CANADIAN MOUNTAINS",
            "FROZEN INDUSTRIAL REGION",
            "FUTURISTIC DUBAI-INSPIRED CITY",
            "EUROPEAN CYBERPUNK METROPOLIS",
            "TROPICAL BEACH AND ISLAND",
            "DESERT MEGACITY",
            "UNDERGROUND FUTURISTIC RAILWAY",
            "NEON TOKYO STREETS",
            "FUTURISTIC MOUNTAIN VILLAGE",
            "RAINY COASTAL CITY",
            "FUTURISTIC FOREST RESEARCH ZONE",
            "SPACEPORT CITY",
            "INDUSTRIAL ROBOT DISTRICT",
            "FUTURISTIC EUROPEAN MOUNTAIN HIGHWAY",
            "LUXURY TECHNOLOGY DISTRICT",
            "ABANDONED CYBER CITY",
            "GIANT SKYBRIDGE MEGACITY"
        };

        private static LocationDefinition Loc(string id, string name, string subtitle, string themeName,
            string ambient, int style, int signature, int hazardBias, int weather, int timeOfDay,
            bool rain, string accent, string skyTop, string skyBottom, string fog, string ground,
            string buildingA, string buildingB, string roadStyle, string roadPrefab,
            string ambienceId, string musicId)
        {
            return new LocationDefinition
            {
                id = id, displayName = name, subtitle = subtitle, themeName = themeName,
                ambientLabel = ambient, style = style, signature = signature, hazardBias = hazardBias,
                weather = weather, timeOfDay = timeOfDay, rain = rain,
                accentHex = accent, skyTopHex = skyTop, skyBottomHex = skyBottom, fogHex = fog,
                groundHex = ground, buildingAHex = buildingA, buildingBHex = buildingB,
                environmentPrefab = id + "_env", roadPrefab = roadPrefab,
                roadStyle = roadStyle, ambienceId = ambienceId, musicId = musicId
            };
        }
    }
}
