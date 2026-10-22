using System;
using System.Collections.Generic;
using NeonVelocity.Utilities;
using UnityEngine;

namespace NeonVelocity.Core
{
    public enum PowerUpType
    {
        Magnet = 0,
        Shield = 1,
        Multiplier = 2,
        SpeedBoost = 3,
        Hoverboard = 4,
        Invincibility = 5,
        AirJump = 6,
        SlowMotion = 7,
        CoinRush = 8,
        PhaseDash = 9
    }

    [CreateAssetMenu(menuName = "NEON VELOCITY/Power-Up Definition", fileName = "PowerUp")]
    public class PowerUpDefinition : ScriptableObject
    {
        public PowerUpType type;
        public string id;
        public string displayName;
        public string shortCode;
        public string description;
        public Color color = Color.cyan;
        [Tooltip("Base duration in seconds before upgrade bonuses.")]
        public float duration = 8f;
        [Range(0f, 1f)] public float spawnWeight = 0.1f;
        [Tooltip("How this behaves in a server-authoritative race.")]
        public string multiplayerRule = "Shared track, personal pickup, identical duration.";
        public bool enabledInRaces = true;
        public bool isRare;
    }

    [CreateAssetMenu(menuName = "NEON VELOCITY/Character Definition", fileName = "Character")]
    public class CharacterDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public string callsign;
        public string bio;
        public string tagline;
        public Color primary = Color.cyan;
        public Color secondary = Color.white;
        public Color trail = Color.cyan;
        public int unlockCost;
        public int unlockLevel;
        public int armorStyle;
        public bool starter;
        public string accentHex;
        public string perkLabel = "COSMETIC";
    }

    [CreateAssetMenu(menuName = "NEON VELOCITY/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [Header("Identity")]
        public string studioName = "RK VANTAGE GAMES";
        public string gameTitle = "NEON VELOCITY";
        public string buildVersion = "1.0.0";

        [Header("Runner")]
        public float baseSpeed = 9f;
        public float maxSpeed = 34f;
        public float accelerationPerSecond = 0.16f;
        public float accelerationPerDistance = 0.004f;
        public float laneWidth = 2.7f;
        public float laneSwitchDuration = 0.13f;
        public float jumpVelocity = 9.8f;
        public float gravity = 27f;
        public float slideDuration = 0.72f;
        public float slideCooldown = 0.18f;
        public float hoverboardSpeedRetention = 0.55f;

        [Header("Track")]
        public int segmentsAhead = 7;
        public int segmentsBehind = 1;
        public float worldOriginRebaseDistance = 2000f;

        [Header("Collectibles")]
        public float baseMagnetRadius = 2.4f;
        public float coinMagnetRadius = 7.5f;
        public int startingCredits = 500;

        [Header("Content")]
        public List<CharacterDefinition> characters = new List<CharacterDefinition>();
        public List<PowerUpDefinition> powerUps = new List<PowerUpDefinition>();
        public List<TrailDefinition> trails = new List<TrailDefinition>();
        public List<ThemeDefinition> themes = new List<ThemeDefinition>();

        private static GameConfig _current;

        public static GameConfig Current
        {
            get
            {
                if (_current != null) return _current;
                var loaded = Resources.Load<GameConfig>("GameConfig");
                if (loaded != null)
                {
                    _current = loaded;
                    EnsureContent(_current);
                    return _current;
                }
                _current = CreateDefault();
                return _current;
            }
        }

        public CharacterDefinition Character(string id)
        {
            for (int i = 0; i < characters.Count; i++) if (characters[i].id == id) return characters[i];
            return characters.Count > 0 ? characters[0] : null;
        }

        public PowerUpDefinition PowerUp(PowerUpType type)
        {
            for (int i = 0; i < powerUps.Count; i++) if (powerUps[i].type == type) return powerUps[i];
            return null;
        }

        public TrailDefinition Trail(string id)
        {
            for (int i = 0; i < trails.Count; i++) if (trails[i].id == id) return trails[i];
            return trails.Count > 0 ? trails[0] : null;
        }

        public ThemeDefinition Theme(int index)
        {
            if (themes.Count == 0) return null;
            return themes[Mathf.Abs(index) % themes.Count];
        }

        public SpeedModel CreateSpeedModel() =>
            new SpeedModel(baseSpeed, maxSpeed, accelerationPerSecond, accelerationPerDistance);

        private static GameConfig CreateDefault()
        {
            var config = CreateInstance<GameConfig>();
            config.name = "GameConfig (Runtime Defaults)";
            EnsureContent(config);
            return config;
        }

        public static void EnsureContent(GameConfig config)
        {
            if (config == null) return;
            if (config.characters == null) config.characters = new List<CharacterDefinition>();
            if (config.powerUps == null) config.powerUps = new List<PowerUpDefinition>();
            if (config.trails == null) config.trails = new List<TrailDefinition>();
            if (config.themes == null) config.themes = new List<ThemeDefinition>();
            if (config.characters.Count == 0) BuildCharacters(config);
            if (config.powerUps.Count == 0) BuildPowerUps(config);
            if (config.trails.Count == 0) BuildTrails(config);
            if (config.themes.Count == 0) BuildThemes(config);
        }

        private static void BuildCharacters(GameConfig config)
        {
            config.characters.Add(Make<CharacterDefinition>("kira", "KIRA-9", "VANTA COURIER",
                "A former grid medic who rewired her suit to outrun the blackout.",
                "Fast lines. Clean landings.", Palette.Cyan, Palette.White, Palette.Cyan,
                0, 1, 0, true, "#28F6FF"));
            config.characters.Add(Make<CharacterDefinition>("volt", "VOLT", "CIRCUIT BREAKER",
                "Street racer turned courier. Runs on stolen voltage and bad ideas.",
                "Loud, bright, impossible to miss.", Palette.Amber, Element("#20304A"), Palette.Amber,
                2500, 2, 1, false, "#FFD166"));
            config.characters.Add(Make<CharacterDefinition>("nyx", "NYX", "SIGNAL GHOST",
                "A ghost in the city's traffic layer. Nobody has seen the face behind the visor.",
                "Low profile. Lower latency.", Palette.Magenta, Element("#1A1236"), Palette.Magenta,
                4000, 4, 2, false, "#FF2E9A"));
            config.characters.Add(Make<CharacterDefinition>("rook", "ROOK", "HEAVY HAULER",
                "Cargo pilot built for monsoons. Rook treats every collision like a personal insult.",
                "Unshakeable. Unhurried.", Palette.Orange, Element("#2C1B16"), Palette.Orange,
                6000, 7, 3, false, "#FF8A3D"));
            config.characters.Add(Make<CharacterDefinition>("ion", "ION-7", "FRONTIER PROTOTYPE",
                "The first synthetic runner cleared for the open grid. Still deciding what speed means.",
                "Beyond spec. Beyond limits.", Palette.Lime, Element("#111F22"), Palette.Lime,
                9000, 10, 4, false, "#B6FF3B"));
        }

        private static void BuildPowerUps(GameConfig config)
        {
            config.powerUps.Add(Make<PowerUpDefinition>(PowerUpType.Magnet, "magnet", "MAGNET", "MAG",
                "Pulls nearby energy credits into your field.", Palette.Violet, 9f, 0.13f));
            config.powerUps.Add(Make<PowerUpDefinition>(PowerUpType.Shield, "shield", "AEGIS SHIELD", "AEG",
                "Absorbs one crash, then breaks.", Palette.Cyan, 0f, 0.12f));
            config.powerUps.Add(Make<PowerUpDefinition>(PowerUpType.Multiplier, "multiplier", "SCORE SURGE", "x2",
                "Doubles distance score while active.", Palette.Amber, 12f, 0.11f));
            config.powerUps.Add(Make<PowerUpDefinition>(PowerUpType.SpeedBoost, "boost", "VOLT RUSH", "SPD",
                "Adds a temporary burst of speed and score.", Palette.Orange, 6f, 0.09f));
            config.powerUps.Add(Make<PowerUpDefinition>(PowerUpType.Hoverboard, "hoverboard", "SPECTRE BOARD", "BRD",
                "Absorbs a collision and glides you clear.", Palette.Magenta, 0f, 0.08f));
            config.powerUps.Add(Make<PowerUpDefinition>(PowerUpType.Invincibility, "invincibility", "PHASE SHELL", "INV",
                "Grants full protection for a short window.", Palette.Lime, 6f, 0.07f));
            config.powerUps.Add(Make<PowerUpDefinition>(PowerUpType.AirJump, "airjump", "SKY CHARGE", "AIR",
                "Grants an additional mid-air jump charge.", Palette.Cyan, 16f, 0.08f));
            var slow = Make<PowerUpDefinition>(PowerUpType.SlowMotion, "slowmo", "TIME DILATION", "TME",
                "Slows the local simulation for intense sections.", Palette.Violet, 5f, 0.06f);
            slow.multiplayerRule = "Restricted online: converted to 300 score and speed re-normalisation; no simulation advantage.";
            slow.enabledInRaces = false;
            config.powerUps.Add(slow);
            config.powerUps.Add(Make<PowerUpDefinition>(PowerUpType.CoinRush, "coinrush", "CREDIT STORM", "CRD",
                "Triples credit value and expands collection range.", Palette.Amber, 8f, 0.06f));
            var phase = Make<PowerUpDefinition>(PowerUpType.PhaseDash, "phasedash", "PRISM DASH", "PRM",
                "Rare ability: phase through hazards, magnetise credits and surge forward.", Palette.White, 4.5f, 0.025f);
            phase.isRare = true;
            phase.multiplayerRule = "Competitive allowed. Same rarity and duration for every runner; server validates distance gain.";
            config.powerUps.Add(phase);
        }

        private static void BuildTrails(GameConfig config)
        {
            config.trails.Add(new TrailDefinition("spark", "ION SPARK", Palette.Cyan, 0, "Default trail"));
            config.trails.Add(new TrailDefinition("afterimage", "AFTERIMAGE", Palette.Magenta, 1200, "Twin-layer photon wake"));
            config.trails.Add(new TrailDefinition("solar", "SOLAR FLARE", Palette.Amber, 1800, "Hot orange ember wake"));
            config.trails.Add(new TrailDefinition("verdant", "GREENLINE", Palette.Lime, 2400, "Bio-luminescent stream"));
        }

        private static void BuildThemes(GameConfig config)
        {
            config.themes.Add(new ThemeDefinition
            {
                id = "neon-sprawl", displayName = "NEON SPRAWL", accent = Palette.Cyan,
                skyTop = Element("#071227"), skyBottom = Element("#241650"), fog = Element("#0A1830"),
                ground = Element("#0B1424"), buildingA = Element("#12243A"), buildingB = Element("#1B1638"),
                rain = true, ambientLabel = "CITY RAIN"
            });
            config.themes.Add(new ThemeDefinition
            {
                id = "skyway", displayName = "SKYWAY 09", accent = Palette.Amber,
                skyTop = Element("#2A1438"), skyBottom = Element("#FF7A4D"), fog = Element("#52243B"),
                ground = Element("#241B26"), buildingA = Element("#3B2539"), buildingB = Element("#58293D"),
                rain = false, ambientLabel = "HIGH ALTITUDE WIND"
            });
            config.themes.Add(new ThemeDefinition
            {
                id = "reactor", displayName = "REACTOR BELT", accent = Palette.Lime,
                skyTop = Element("#06110E"), skyBottom = Element("#112B1A"), fog = Element("#0C2018"),
                ground = Element("#111914"), buildingA = Element("#1C2A20"), buildingB = Element("#24321B"),
                rain = false, ambientLabel = "REACTOR HUM"
            });
            config.themes.Add(new ThemeDefinition
            {
                id = "ghost-tunnel", displayName = "GHOST TUNNEL", accent = Palette.Violet,
                skyTop = Element("#08040F"), skyBottom = Element("#1C0E33"), fog = Element("#120A22"),
                ground = Element("#100C1B"), buildingA = Element("#1C1430"), buildingB = Element("#291B42"),
                rain = false, ambientLabel = "TUNNEL ECHO"
            });
        }

        private static T Make<T>(string id, string displayName, string callsign, string bio, string tagline,
            Color primary, Color secondary, Color trail, int cost, int level, int style, bool starter, string accentHex)
            where T : ScriptableObject
        {
            var def = CreateInstance<T>();
            if (def is CharacterDefinition c)
            {
                c.id = id; c.displayName = displayName; c.callsign = callsign; c.bio = bio; c.tagline = tagline;
                c.primary = primary; c.secondary = secondary; c.trail = trail; c.unlockCost = cost;
                c.unlockLevel = level; c.armorStyle = style; c.starter = starter; c.accentHex = accentHex;
                c.name = displayName;
            }
            return def;
        }

        private static PowerUpDefinition Make<T>(PowerUpType type, string id, string displayName, string shortCode,
            string description, Color color, float duration, float weight) where T : ScriptableObject
        {
            var def = CreateInstance<PowerUpDefinition>();
            def.type = type; def.id = id; def.displayName = displayName; def.shortCode = shortCode;
            def.description = description; def.color = color; def.duration = duration; def.spawnWeight = weight;
            def.name = displayName;
            return def;
        }

        private static Color Element(string hex) => Palette.Hex(hex);
    }

    [Serializable]
    public class TrailDefinition
    {
        public string id;
        public string displayName;
        public Color color;
        public int unlockCost;
        public string description;

        public TrailDefinition(string id, string displayName, Color color, int unlockCost, string description)
        {
            this.id = id; this.displayName = displayName; this.color = color;
            this.unlockCost = unlockCost; this.description = description;
        }
    }

    [Serializable]
    public class ThemeDefinition
    {
        public string id;
        public string displayName;
        public Color accent;
        public Color skyTop;
        public Color skyBottom;
        public Color fog;
        public Color ground;
        public Color buildingA;
        public Color buildingB;
        public bool rain;
        public string ambientLabel;
    }
}
