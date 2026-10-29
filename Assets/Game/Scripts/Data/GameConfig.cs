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
        [Header("Premium entitlement")]
        public bool premium;
        public string premiumProductId;
        public int premiumPriceGems;
        [Header("Signature ability")]
        public string abilityId;
        public string abilityName;
        public string abilityDescription;
        public float abilityCooldown = 22f;
        public float abilityDuration = 1.4f;
        [Range(0.9f, 1.1f)] public float speedBias = 1f;
        [Range(0f, 1f)] public float defenseBias;
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
            if (themes == null || themes.Count == 0) return null;
            return themes[Mathf.Abs(index) % themes.Count];
        }

        public ThemeDefinition LocationTheme(int zoneIndex)
        {
            if (themes == null || themes.Count == 0) return null;
            return themes[LocationCatalog.Wrap(zoneIndex) % themes.Count];
        }

        public LocationDefinition Location(int zoneIndex) => LocationCatalog.Get(zoneIndex);

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
            EnsurePremiumCharacters(config);
            if (config.powerUps.Count == 0) BuildPowerUps(config);
            if (config.trails.Count == 0) BuildTrails(config);
            if (config.themes.Count != LocationCatalog.Count) BuildThemes(config);
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

        /// <summary>
        /// Two server-entitled premium runners. They are sidegrades, not upgrades: Phantom X
        /// trades raw durability for a short phase dash, Nova Titan trades top speed for one
        /// emergency shield charge and a hazard-clearing burst. Neither changes rewards.
        /// </summary>
        private static void EnsurePremiumCharacters(GameConfig config)
        {
            if (config.characters == null) config.characters = new List<CharacterDefinition>();
            bool hasPhantom = false, hasNova = false, hasEcho = false, hasSable = false, hasHelios = false;
            for (int i = 0; i < config.characters.Count; i++)
            {
                if (config.characters[i] == null) continue;
                if (config.characters[i].id == "phantom") hasPhantom = true;
                if (config.characters[i].id == "nova") hasNova = true;
                if (config.characters[i].id == "echo") hasEcho = true;
                if (config.characters[i].id == "sable") hasSable = true;
                if (config.characters[i].id == "helios") hasHelios = true;
            }

            if (!hasPhantom)
            {
                var phantom = Make<CharacterDefinition>("phantom", "PHANTOM X", "STEALTH PROTOTYPE",
                    "A black-budget phase frame. Its wake bends light around the runner for a heartbeat.",
                    "Gone before the grid renders you.", Palette.Violet, Element("#090C18"), Palette.Cyan,
                    0, 1, 5, false, "#A78BFA");
                phantom.premium = true;
                phantom.premiumProductId = "char_phantom_x";
                phantom.premiumPriceGems = 450;
                phantom.abilityId = "phase_dash";
                phantom.abilityName = "PHASE DASH";
                phantom.abilityDescription = "Become untouchable for 1.5 s and surge forward. 20 s cooldown.";
                phantom.abilityCooldown = 20f;
                phantom.abilityDuration = 1.5f;
                phantom.speedBias = 1.02f;
                phantom.defenseBias = 0f;
                phantom.perkLabel = "STEALTH // PHASE DASH";
                config.characters.Add(phantom);
            }

            if (!hasNova)
            {
                var nova = Make<CharacterDefinition>("nova", "NOVA TITAN", "HEAVY ASSAULT FRAME",
                    "A siege-class courier wrapped in reactor plate. Nova does not dodge the storm; it answers.",
                    "Slow to turn. Impossible to stop.", Palette.Orange, Element("#1B1210"), Palette.Amber,
                    0, 1, 6, false, "#FF8A3D");
                nova.premium = true;
                nova.premiumProductId = "char_nova_titan";
                nova.premiumPriceGems = 700;
                nova.abilityId = "nova_burst";
                nova.abilityName = "NOVA BURST";
                nova.abilityDescription = "Detonate a shockwave that clears nearby hazards. Starts with one shield charge.";
                nova.abilityCooldown = 24f;
                nova.abilityDuration = 1.2f;
                nova.speedBias = 0.98f;
                nova.defenseBias = 1f;
                nova.perkLabel = "ARMOURED // NOVA BURST";
                config.characters.Add(nova);
            }

            if (!hasEcho)
            {
                var echo = Make<CharacterDefinition>("echo", "ECHO", "SONAR PATHFINDER",
                    "A survey frame rebuilt around a resonance array. Echo maps the grid by listening to it.",
                    "The grid speaks. Echo answers.", Palette.Cyan, Element("#0D1B22"), Palette.Lime,
                    0, 1, 7, false, "#5EF2D6");
                echo.premium = true;
                echo.premiumProductId = "char_echo";
                echo.premiumPriceGems = 520;
                echo.abilityId = "echo_pulse";
                echo.abilityName = "ECHO PULSE";
                echo.abilityDescription = "Broadcast a sonar pulse that magnetises every credit in a 16 m radius for 5 s. 18 s cooldown.";
                echo.abilityCooldown = 18f;
                echo.abilityDuration = 1f;
                echo.speedBias = 1f;
                echo.defenseBias = 0.5f;
                echo.perkLabel = "SURVEY // ECHO PULSE";
                config.characters.Add(echo);
            }

            if (!hasSable)
            {
                var sable = Make<CharacterDefinition>("sable", "SABLE", "NIGHT COURIER",
                    "A blackout specialist with a mirrored chassis. Sable is only visible when the rain catches her edge.",
                    "No signal. No shadow. No trail.", Palette.Violet, Element("#0B0712"), Palette.Magenta,
                    0, 1, 8, false, "#C084FC");
                sable.premium = true;
                sable.premiumProductId = "char_sable";
                sable.premiumPriceGems = 620;
                sable.abilityId = "night_veil";
                sable.abilityName = "NIGHT VEIL";
                sable.abilityDescription = "Collapse into a shadow state for 2.2 s, ignoring every hazard without a speed surge. 26 s cooldown.";
                sable.abilityCooldown = 26f;
                sable.abilityDuration = 2.2f;
                sable.speedBias = 1.01f;
                sable.defenseBias = 0f;
                sable.perkLabel = "STEALTH // NIGHT VEIL";
                config.characters.Add(sable);
            }

            if (!hasHelios)
            {
                var helios = Make<CharacterDefinition>("helios", "HELIOS", "SOLAR BULWARK",
                    "A rescue frame plated with photovoltaic armour. Helios carries daylight into districts that have forgotten the sun.",
                    "Stand in the light. Share it.", Palette.Amber, Element("#241505"), Palette.Orange,
                    0, 1, 9, false, "#FFB020");
                helios.premium = true;
                helios.premiumProductId = "char_helios";
                helios.premiumPriceGems = 760;
                helios.abilityId = "solar_aegis";
                helios.abilityName = "SOLAR AEGIS";
                helios.abilityDescription = "Forge a solar shield that absorbs one crash and grants 1.5 s of protection. 24 s cooldown.";
                helios.abilityCooldown = 24f;
                helios.abilityDuration = 1.5f;
                helios.speedBias = 0.99f;
                helios.defenseBias = 0.75f;
                helios.perkLabel = "AEGIS // SOLAR SHIELD";
                config.characters.Add(helios);
            }
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
            config.themes.Clear();
            for (int i = 0; i < LocationCatalog.Count; i++)
            {
                var location = LocationCatalog.All[i];
                config.themes.Add(new ThemeDefinition
                {
                    id = location.id,
                    displayName = location.displayName,
                    accent = Palette.Hex(location.accentHex),
                    skyTop = Palette.Hex(location.skyTopHex),
                    skyBottom = Palette.Hex(location.skyBottomHex),
                    fog = Palette.Hex(location.fogHex),
                    ground = Palette.Hex(location.groundHex),
                    buildingA = Palette.Hex(location.buildingAHex),
                    buildingB = Palette.Hex(location.buildingBHex),
                    rain = location.rain,
                    ambientLabel = location.ambientLabel,
                    styleIndex = location.style,
                    signature = location.signature,
                    hazardBias = location.hazardBias,
                    subtitle = location.subtitle,
                    themeName = location.themeName,
                    weather = location.weather,
                    timeOfDay = location.timeOfDay
                });
            }
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
        public int styleIndex;
        public int signature;
        public int hazardBias;
        public string subtitle;
        /// <summary>The named biome archetype, e.g. "SNOWY MOUNTAIN HIGHWAY".</summary>
        public string themeName;
        /// <summary>0 clear, 1 rain, 2 snow, 3 dust, 4 embers, 5 fog.</summary>
        public int weather;
        /// <summary>0 Early Morning, 1 Morning, 2 Afternoon, 3 Evening, 4 Sunset, 5 Night, 6 Late Night.</summary>
        public int timeOfDay;

        public bool HasPrecipitation => weather == 1 || weather == 2;
        public float WeatherIntensity => weather == 0 ? 0f : weather == 5 ? 0.55f : 1f;
    }
}
