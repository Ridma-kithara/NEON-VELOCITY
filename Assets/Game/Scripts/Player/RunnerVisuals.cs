using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonVelocity.Player
{
    public enum RunnerPose
    {
        Idle,
        Run,
        Jump,
        Landing,
        Slide,
        Dead,
        Victory,
        Revive
    }

    /// <summary>
    /// Original procedural runner rig — the NEON VELOCITY protagonist.
    /// The silhouette is machined from chamfered armour plates over a graphite flight frame:
    /// a layered chest carapace with a live reactor core, twin-shell pauldrons, articulated
    /// gauntlets with fingers, actuator-ring legs with calf pistons and heel exhaust jets.
    /// Materials follow the studio reference: glossy obsidian armour, titanium silver plating,
    /// machined graphite internals, cyan energy light and magenta trim. Animation is a
    /// speed-reactive procedural run/sprint cycle with banking, jump tucks, a double-jump
    /// somersault, landing compression, a slide brace, a collapse death and a kneel-rise
    /// revival. The public API is unchanged: Build, Tick, SetPose and the toggles still drive
    /// local play, remote runners, the garage turntable and the title stage.
    /// </summary>
    public sealed class RunnerVisuals : MonoBehaviour
    {
        private sealed class Paint
        {
            /// <summary>Glossy black mirage plating — the main armour surface.</summary>
            public Material Suit;
            /// <summary>Woven carbon-fibre under-suit — studio matte, visible at the flex joints.</summary>
            public Material Carbon;
            /// <summary>Titanium silver plating for the outer shells.</summary>
            public Material Armor;
            /// <summary>Graphite dark metal for sub-frame armour.</summary>
            public Material ArmorDark;
            /// <summary>Deep intake housing metal.</summary>
            public Material Deep;
            /// <summary>Polished chrome details.</summary>
            public Material Chrome;
            /// <summary>Dark rubberised joint seals.</summary>
            public Material Joint;
            /// <summary>Cyan energy light.</summary>
            public Material Neon;
            /// <summary>Magenta accent light.</summary>
            public Material Magenta;
            /// <summary>Void-black composite soles and deck.</summary>
            public Material Dark;
            /// <summary>Glowing visor glass used at the chest core.</summary>
            public Material Visor;
        }

        private const float LandingDuration = 0.22f;
        private const float FlipDuration = 0.44f;
        private static Texture2D _contactShadowTexture;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private CharacterDefinition _definition;
        private Transform _visual;
        private Transform _hips;
        private Transform _torso;
        private Transform _head;
        private Transform _armL;
        private Transform _armR;
        private Transform _forearmL;
        private Transform _forearmR;
        private Transform _legL;
        private Transform _legR;
        private Transform _shinL;
        private Transform _shinR;
        private Transform _footL;
        private Transform _footR;
        private GameObject _hoverboard;
        private GameObject _shield;
        private GameObject _aura;
        private TrailRenderer _trail;
        private readonly List<TrailRenderer> _trails = new List<TrailRenderer>();
        private TextMesh _nameTag;
        private RunnerPose _pose = RunnerPose.Idle;
        private float _phase;
        private float _bob;
        private float _lateralLean;
        private float _sprintBlend;
        private float _landingTimer;
        private float _landingImpact;
        private float _flipTimer;
        private bool _built;
        private bool _dead;
        private bool _victory;
        private bool _remote;
        private float _deathSpin;
        private float _reviveProgress;
        private Transform _shadow;
        private Material _shadowMaterial;
        private Color _accent = Palette.Cyan;
        private readonly List<Renderer> _pulseRenderers = new List<Renderer>(4);
        private readonly List<Transform> _thrusterJets = new List<Transform>(2);
        private Transform _speedWave;
        // MaterialPropertyBlock cannot be constructed in a MonoBehaviour constructor (Unity
        // forbids CreateImpl there), so the buffer is filled on first use inside Build.
        private MaterialBlockBuffer _fx;

        /// <summary>Lazy per-renderer emission buffer so live lights pulse without cloning materials.</summary>
        private MaterialBlockBuffer Fx => _fx ??= new MaterialBlockBuffer();

        /// <summary>Holds the single MaterialPropertyBlock used for emission pulses.</summary>
        private sealed class MaterialBlockBuffer
        {
            public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        }

        public CharacterDefinition Definition => _definition;

        public void Build(CharacterDefinition character, TrailDefinition trail, bool remote, string displayName)
        {
            _definition = character;
            _remote = remote;
            _accent = character.primary;
            Clear();

            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);

            Color obsidian = Color.Lerp(Palette.Hex("#07080D"), character.secondary, 0.16f);
            Color titanium = Color.Lerp(new Color(0.79f, 0.84f, 0.91f), character.secondary, 0.2f);
            Color graphite = Color.Lerp(new Color(0.085f, 0.10f, 0.125f), character.primary, 0.1f);
            Color carbon = Color.Lerp(new Color(0.062f, 0.072f, 0.088f), character.secondary, 0.12f);
            Color accent = character.primary;
            var paint = new Paint
            {
                Suit = MaterialLibrary.LitTextured("runner_obsidian:" + character.id,
                    TextureFactory.ArmorSurface(), obsidian, 0.88f, 0.9f, new Vector2(2.1f, 2.1f),
                    TextureFactory.ArmorNormal(), new Vector2(2.1f, 2.1f), 0.5f),
                Carbon = MaterialLibrary.LitTextured("runner_carbon:" + character.id,
                    TextureFactory.CarbonSurface(), carbon, 0.58f, 0.58f, new Vector2(3.4f, 3.4f),
                    TextureFactory.CarbonNormal(), new Vector2(3.4f, 3.4f), 1.25f),
                Armor = MaterialLibrary.LitTextured("runner_titanium:" + character.id,
                    TextureFactory.ArmorSurface(), titanium, 0.98f, 0.86f, new Vector2(1.6f, 1.6f),
                    TextureFactory.ArmorNormal(), new Vector2(1.6f, 1.6f), 0.42f),
                ArmorDark = MaterialLibrary.LitTextured("runner_graphite:" + character.id,
                    TextureFactory.ArmorSurface(), graphite, 0.84f, 0.74f, new Vector2(1.9f, 1.9f),
                    TextureFactory.ArmorNormal(), new Vector2(1.9f, 1.9f), 0.55f),
                Deep = MaterialLibrary.Lit(Color.Lerp(Palette.Void, character.primary, 0.05f), 0.66f, 0.62f),
                Chrome = MaterialLibrary.Lit(new Color(0.72f, 0.78f, 0.86f), 1f, 0.95f),
                Joint = MaterialLibrary.Lit(new Color(0.025f, 0.032f, 0.05f), 0.72f, 0.66f),
                Neon = MaterialLibrary.Lit(accent, 0.08f, 0.7f, accent, 4.2f),
                Magenta = MaterialLibrary.Lit(Palette.Magenta, 0.1f, 0.62f, Palette.Magenta, 3.1f),
                Dark = MaterialLibrary.Lit(Palette.Void, 0.34f, 0.6f),
                Visor = MaterialLibrary.Lit(accent, 0.94f, 0.99f,
                    Color.Lerp(accent, Color.white, 0.22f), 1.4f)
            };
            DoubleSided(paint.Suit);
            DoubleSided(paint.Armor);
            DoubleSided(paint.ArmorDark);
            DoubleSided(paint.Carbon);
            DoubleSided(paint.Joint);
            DoubleSided(paint.Dark);

            BuildBody(paint);
            AddStyleDetails(character.armorStyle, paint);
            BuildMechanicalDetail(paint, remote);
            AddLights(accent, remote);
            AddTrails(trail, accent, remote);
            AddHoverboard(paint);
            AddShieldBubble(accent);
            AddAura(accent);
            AddNameTag(displayName, remote, accent);
            AddContactShadow();

            _built = true;
            _dead = false;
            _victory = false;
            _landingTimer = 0f;
            SetPose(RunnerPose.Idle);
        }

        private void BuildBody(Paint paint)
        {
            // Pelvis: graphite flight frame under a sharp composite hip shell.
            _hips = Pivot("Hips", _visual, new Vector3(0f, 0.94f, 0f));
            PrimitiveMeshes.Beveled(_hips, "PelvisCage", new Vector3(0f, 0.035f, 0f),
                new Vector3(0.35f, 0.23f, 0.29f), paint.Carbon);
            PrimitiveMeshes.Beveled(_hips, "PelvisPlate", new Vector3(0f, 0.045f, 0.14f),
                new Vector3(0.31f, 0.18f, 0.075f), paint.Armor);
            PrimitiveMeshes.Box(_hips, "Belt", new Vector3(0f, 0.17f, 0f), new Vector3(0.44f, 0.055f, 0.3f), paint.Chrome);
            PrimitiveMeshes.Box(_hips, "BeltGlow", new Vector3(0f, 0.18f, 0.155f), new Vector3(0.23f, 0.016f, 0.012f), paint.Neon);
            PrimitiveMeshes.Beveled(_hips, "HipGuardL", new Vector3(-0.245f, 0.045f, 0f),
                new Vector3(0.105f, 0.245f, 0.29f), paint.Armor, Quaternion.Euler(0f, 0f, 9f));
            PrimitiveMeshes.Beveled(_hips, "HipGuardR", new Vector3(0.245f, 0.045f, 0f),
                new Vector3(0.105f, 0.245f, 0.29f), paint.Armor, Quaternion.Euler(0f, 0f, -9f));
            PrimitiveMeshes.Box(_hips, "HipEdgeL", new Vector3(-0.3f, 0.045f, 0.01f),
                new Vector3(0.014f, 0.18f, 0.22f), paint.Magenta);
            PrimitiveMeshes.Box(_hips, "HipEdgeR", new Vector3(0.3f, 0.045f, 0.01f),
                new Vector3(0.014f, 0.18f, 0.22f), paint.Magenta);
            PrimitiveMeshes.SphereObject(_hips, "HipJointL", new Vector3(-0.17f, -0.02f, 0f), 0.15f, paint.Joint);
            PrimitiveMeshes.SphereObject(_hips, "HipJointR", new Vector3(0.17f, -0.02f, 0f), 0.15f, paint.Joint);
            PrimitiveMeshes.Beveled(_hips, "RearPelvis", new Vector3(0f, 0.06f, -0.16f),
                new Vector3(0.28f, 0.18f, 0.075f), paint.ArmorDark);
            PrimitiveMeshes.Box(_hips, "RearGlow", new Vector3(0f, 0.06f, -0.205f),
                new Vector3(0.16f, 0.022f, 0.012f), paint.Neon);

            // Chest: dark pressure shell, layered titanium carapace and a visible reactor core.
            _torso = Pivot("Torso", _hips, new Vector3(0f, 0.12f, 0f));
            PrimitiveMeshes.Create("Abdomen", PrimitiveMeshes.Capsule, paint.Carbon, _torso,
                new Vector3(0f, 0.23f, 0f), new Vector3(0.34f, 0.26f, 0.28f));
            PrimitiveMeshes.Beveled(_torso, "AbdominalPlate", new Vector3(0f, 0.245f, 0.14f),
                new Vector3(0.24f, 0.13f, 0.055f), paint.ArmorDark);
            PrimitiveMeshes.Box(_torso, "AbdominalSeam", new Vector3(0f, 0.235f, 0.17f),
                new Vector3(0.16f, 0.012f, 0.012f), paint.Neon);
            PrimitiveMeshes.Create("ChestShell", PrimitiveMeshes.Capsule, paint.Suit, _torso,
                new Vector3(0f, 0.45f, 0f), new Vector3(0.47f, 0.34f, 0.33f));
            PrimitiveMeshes.Create("ChestPlate", PrimitiveMeshes.Capsule, paint.Armor, _torso,
                new Vector3(0f, 0.47f, 0.045f), new Vector3(0.45f, 0.3f, 0.27f));
            // Under-shell carbon barrier visible in the collar notch and under the pec plates.
            PrimitiveMeshes.Beveled(_torso, "ChestCarbon", new Vector3(0f, 0.62f, 0.05f),
                new Vector3(0.25f, 0.09f, 0.2f), paint.Carbon);
            PrimitiveMeshes.Beveled(_torso, "Sternum", new Vector3(0f, 0.46f, 0.205f),
                new Vector3(0.14f, 0.27f, 0.06f), paint.ArmorDark);
            PrimitiveMeshes.Beveled(_torso, "PectoralL", new Vector3(-0.145f, 0.5f, 0.165f),
                new Vector3(0.18f, 0.2f, 0.075f), paint.Armor, Quaternion.Euler(-4f, -8f, 8f));
            PrimitiveMeshes.Beveled(_torso, "PectoralR", new Vector3(0.145f, 0.5f, 0.165f),
                new Vector3(0.18f, 0.2f, 0.075f), paint.Armor, Quaternion.Euler(-4f, 8f, -8f));
            PrimitiveMeshes.Box(_torso, "ChestSplit", new Vector3(0f, 0.44f, 0.255f),
                new Vector3(0.014f, 0.2f, 0.012f), paint.Neon);
            PrimitiveMeshes.Create("CoreRing", PrimitiveMeshes.Cylinder, paint.Neon, _torso,
                new Vector3(0f, 0.47f, 0.255f), new Vector3(0.155f, 0.012f, 0.155f), Quaternion.Euler(90f, 0f, 0f));
            PrimitiveMeshes.Create("Core", PrimitiveMeshes.Sphere, paint.Visor, _torso,
                new Vector3(0f, 0.47f, 0.265f), new Vector3(0.088f, 0.088f, 0.045f));
            RegisterPulse(_torso.Find("Core"), 3.4f);
            PrimitiveMeshes.Box(_torso, "MagentaChestMark", new Vector3(-0.19f, 0.37f, 0.235f),
                new Vector3(0.06f, 0.014f, 0.012f), paint.Magenta, Quaternion.Euler(0f, 0f, 18f));
            PrimitiveMeshes.Box(_torso, "Collar", new Vector3(0f, 0.665f, 0f),
                new Vector3(0.28f, 0.07f, 0.23f), paint.Joint);
            PrimitiveMeshes.Box(_torso, "CollarGlowL", new Vector3(-0.105f, 0.667f, 0.12f),
                new Vector3(0.075f, 0.018f, 0.015f), paint.Neon);
            PrimitiveMeshes.Box(_torso, "CollarGlowR", new Vector3(0.105f, 0.667f, 0.12f),
                new Vector3(0.075f, 0.018f, 0.015f), paint.Neon);
            PrimitiveMeshes.Beveled(_torso, "ShoulderHarnessL", new Vector3(-0.205f, 0.585f, 0.135f),
                new Vector3(0.035f, 0.21f, 0.04f), paint.ArmorDark, Quaternion.Euler(0f, 0f, -18f));
            PrimitiveMeshes.Beveled(_torso, "ShoulderHarnessR", new Vector3(0.205f, 0.585f, 0.135f),
                new Vector3(0.035f, 0.21f, 0.04f), paint.ArmorDark, Quaternion.Euler(0f, 0f, 18f));

            // Backpack, spine and twin thrusters.
            PrimitiveMeshes.Beveled(_torso, "Backpack", new Vector3(0f, 0.42f, -0.205f),
                new Vector3(0.34f, 0.38f, 0.17f), paint.ArmorDark);
            PrimitiveMeshes.Box(_torso, "Spine", new Vector3(0f, 0.42f, -0.305f),
                new Vector3(0.08f, 0.31f, 0.045f), paint.Chrome);
            for (int i = 0; i < 3; i++)
            {
                PrimitiveMeshes.Box(_torso, "SpineGlow" + i, new Vector3(0f, 0.32f + i * 0.09f, -0.335f),
                    new Vector3(0.05f, 0.018f, 0.012f), paint.Neon);
            }
            PrimitiveMeshes.Box(_torso, "SpineMagenta", new Vector3(0f, 0.49f, -0.342f),
                new Vector3(0.014f, 0.22f, 0.012f), paint.Magenta);
            for (int side = -1; side <= 1; side += 2)
            {
                PrimitiveMeshes.Box(_torso, "PackRail" + side, new Vector3(side * 0.155f, 0.41f, -0.304f),
                    new Vector3(0.016f, 0.28f, 0.012f), paint.Neon, Quaternion.Euler(0f, 0f, side * 5f));
                PrimitiveMeshes.Box(_torso, "PackShoulder" + side, new Vector3(side * 0.085f, 0.605f, -0.245f),
                    new Vector3(0.11f, 0.016f, 0.012f), paint.Neon, Quaternion.Euler(0f, 0f, side * 8f));
                PrimitiveMeshes.Box(_torso, "PackVent" + side, new Vector3(side * 0.125f, 0.34f, -0.307f),
                    new Vector3(0.05f, 0.11f, 0.012f), paint.Magenta);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                PrimitiveMeshes.Box(_torso, "BackVent" + side, new Vector3(side * 0.145f, 0.43f, -0.305f),
                    new Vector3(0.035f, 0.18f, 0.025f), paint.Chrome, Quaternion.Euler(0f, 0f, side * 8f));
                PrimitiveMeshes.Create("Thruster" + side, PrimitiveMeshes.Cylinder, paint.Armor, _torso,
                    new Vector3(side * 0.12f, 0.29f, -0.285f), new Vector3(0.09f, 0.145f, 0.09f));
                PrimitiveMeshes.Create("ThrusterGlow" + side, PrimitiveMeshes.Cylinder, paint.Neon, _torso,
                    new Vector3(side * 0.12f, 0.148f, -0.285f), new Vector3(0.063f, 0.012f, 0.063f));
            }

            // Helmet: titanium crown, angled jaw, deep visor and luminous side optics.
            _head = Pivot("Head", _torso, new Vector3(0f, 0.71f, 0f));
            PrimitiveMeshes.Create("Neck", PrimitiveMeshes.Cylinder, paint.Joint, _head,
                new Vector3(0f, -0.045f, 0f), new Vector3(0.11f, 0.085f, 0.11f));
            PrimitiveMeshes.Create("NeckRing", PrimitiveMeshes.Cylinder, paint.Chrome, _head,
                new Vector3(0f, -0.005f, 0f), new Vector3(0.125f, 0.012f, 0.125f));
            PrimitiveMeshes.Create("HelmetShell", PrimitiveMeshes.Sphere, paint.Armor, _head,
                new Vector3(0f, 0.15f, 0f), new Vector3(0.36f, 0.34f, 0.36f));
            // Rear helmet weave: the carbon substrate that seals into the backpack spine.
            PrimitiveMeshes.Beveled(_head, "HelmetWeave", new Vector3(0f, 0.15f, -0.115f),
                new Vector3(0.27f, 0.24f, 0.09f), paint.Carbon);
            PrimitiveMeshes.Beveled(_head, "HelmetCrown", new Vector3(0f, 0.285f, -0.025f),
                new Vector3(0.28f, 0.11f, 0.29f), paint.ArmorDark);
            PrimitiveMeshes.Beveled(_head, "HelmetRear", new Vector3(0f, 0.13f, -0.17f),
                new Vector3(0.26f, 0.21f, 0.08f), paint.ArmorDark);
            PrimitiveMeshes.Box(_head, "HelmetRearGlow", new Vector3(0f, 0.16f, -0.213f),
                new Vector3(0.12f, 0.018f, 0.012f), paint.Neon);
            PrimitiveMeshes.Create("Visor", PrimitiveMeshes.Sphere, paint.Visor, _head,
                new Vector3(0f, 0.165f, 0.135f), new Vector3(0.29f, 0.135f, 0.16f));
            RegisterPulse(_head.Find("Visor"), 1.9f);
            PrimitiveMeshes.Box(_head, "VisorStrip", new Vector3(0f, 0.17f, 0.225f),
                new Vector3(0.22f, 0.016f, 0.014f), paint.Neon);
            PrimitiveMeshes.Box(_head, "VisorPixelL", new Vector3(-0.16f, 0.17f, 0.215f),
                new Vector3(0.014f, 0.014f, 0.012f), paint.Magenta);
            PrimitiveMeshes.Box(_head, "VisorPixelR", new Vector3(0.16f, 0.17f, 0.215f),
                new Vector3(0.014f, 0.014f, 0.012f), paint.Magenta);
            PrimitiveMeshes.Beveled(_head, "Brow", new Vector3(0f, 0.248f, 0.165f),
                new Vector3(0.3f, 0.055f, 0.075f), paint.Armor, Quaternion.Euler(-8f, 0f, 0f));
            PrimitiveMeshes.Beveled(_head, "Jaw", new Vector3(0f, 0.035f, 0.13f),
                new Vector3(0.22f, 0.09f, 0.15f), paint.ArmorDark);
            PrimitiveMeshes.Beveled(_head, "Chin", new Vector3(0f, -0.005f, 0.17f),
                new Vector3(0.13f, 0.05f, 0.09f), paint.Chrome);
            PrimitiveMeshes.Box(_head, "MouthVent", new Vector3(0f, 0.08f, 0.207f),
                new Vector3(0.1f, 0.018f, 0.012f), paint.Neon);
            for (int side = -1; side <= 1; side += 2)
            {
                PrimitiveMeshes.Create("Ear" + side, PrimitiveMeshes.Cylinder, paint.Chrome, _head,
                    new Vector3(side * 0.185f, 0.155f, 0f), new Vector3(0.1f, 0.022f, 0.1f),
                    Quaternion.Euler(0f, 0f, 90f));
                PrimitiveMeshes.Create("EarGlow" + side, PrimitiveMeshes.Cylinder, paint.Neon, _head,
                    new Vector3(side * 0.2f, 0.155f, 0f), new Vector3(0.047f, 0.012f, 0.047f),
                    Quaternion.Euler(0f, 0f, 90f));
                PrimitiveMeshes.Beveled(_head, "Cheek" + side, new Vector3(side * 0.14f, 0.06f, 0.155f),
                    new Vector3(0.08f, 0.11f, 0.08f), paint.Armor, Quaternion.Euler(0f, 0f, side * 7f));
            }
            PrimitiveMeshes.Box(_head, "HelmetFin", new Vector3(0f, 0.355f, -0.075f),
                new Vector3(0.03f, 0.14f, 0.21f), paint.ArmorDark, Quaternion.Euler(18f, 0f, 0f));
            PrimitiveMeshes.Box(_head, "FinTip", new Vector3(0f, 0.425f, -0.14f),
                new Vector3(0.02f, 0.045f, 0.09f), paint.Magenta, Quaternion.Euler(18f, 0f, 0f));

            _armL = BuildArm(_torso, new Vector3(-0.35f, 0.58f, 0f), -1f, paint);
            _armR = BuildArm(_torso, new Vector3(0.35f, 0.58f, 0f), 1f, paint);
            _forearmL = _armL.Find("Forearm");
            _forearmR = _armR.Find("Forearm");

            _legL = BuildLeg(_hips, new Vector3(-0.17f, 0f, 0f), paint);
            _legR = BuildLeg(_hips, new Vector3(0.17f, 0f, 0f), paint);
            _shinL = _legL.Find("Shin");
            _shinR = _legR.Find("Shin");
            _footL = _shinL != null ? _shinL.Find("Foot") : null;
            _footR = _shinR != null ? _shinR.Find("Foot") : null;
        }

        private static Transform BuildArm(Transform parent, Vector3 shoulder, float side, Paint paint)
        {
            var pivot = Pivot(side < 0 ? "ArmL" : "ArmR", parent, shoulder);
            PrimitiveMeshes.SphereObject(pivot, "ShoulderJoint", Vector3.zero, 0.17f, paint.Joint);
            PrimitiveMeshes.Beveled(pivot, "Pauldron", new Vector3(side * 0.035f, 0.045f, 0f),
                new Vector3(0.29f, 0.2f, 0.31f), paint.Armor, Quaternion.Euler(0f, 0f, side * 8f));
            PrimitiveMeshes.Beveled(pivot, "PauldronUnder", new Vector3(side * 0.02f, -0.035f, -0.11f),
                new Vector3(0.2f, 0.13f, 0.065f), paint.ArmorDark, Quaternion.Euler(0f, 0f, side * 10f));
            PrimitiveMeshes.Box(pivot, "PauldronGlow", new Vector3(side * 0.165f, 0.03f, 0.01f),
                new Vector3(0.016f, 0.12f, 0.22f), paint.Neon, Quaternion.Euler(0f, 0f, side * 10f));
            PrimitiveMeshes.Box(pivot, "PauldronMagenta", new Vector3(side * 0.105f, 0.13f, 0.09f),
                new Vector3(0.14f, 0.014f, 0.012f), paint.Magenta, Quaternion.Euler(0f, 0f, side * 12f));
            PrimitiveMeshes.Box(pivot, "PauldronRearGlow", new Vector3(side * 0.085f, 0.04f, -0.162f),
                new Vector3(0.16f, 0.018f, 0.012f), paint.Neon, Quaternion.Euler(0f, 0f, side * 10f));
            PrimitiveMeshes.Box(pivot, "PauldronRearMagenta", new Vector3(side * 0.02f, 0.12f, -0.155f),
                new Vector3(0.07f, 0.012f, 0.01f), paint.Magenta, Quaternion.Euler(0f, 0f, side * 8f));
            PrimitiveMeshes.Create("UpperArm", PrimitiveMeshes.Capsule, paint.Carbon, pivot,
                new Vector3(0f, -0.19f, 0f), new Vector3(0.135f, 0.2f, 0.135f));
            PrimitiveMeshes.Beveled(pivot, "BicepPlate", new Vector3(0f, -0.16f, 0.065f),
                new Vector3(0.115f, 0.16f, 0.055f), paint.Armor);
            PrimitiveMeshes.Box(pivot, "BicepGlow", new Vector3(side * 0.07f, -0.17f, 0.0f),
                new Vector3(0.012f, 0.09f, 0.01f), paint.Neon);
            var forearm = Pivot("Forearm", pivot, new Vector3(0f, -0.37f, 0f));
            PrimitiveMeshes.SphereObject(forearm, "Elbow", Vector3.zero, 0.14f, paint.Joint);
            PrimitiveMeshes.Create("ElbowRing", PrimitiveMeshes.Cylinder, paint.Chrome, forearm,
                Vector3.zero, new Vector3(0.12f, 0.016f, 0.12f));
            PrimitiveMeshes.Create("Forearm", PrimitiveMeshes.Capsule, paint.Armor, forearm,
                new Vector3(0f, -0.16f, 0f), new Vector3(0.145f, 0.17f, 0.145f));
            // Woven carbon guard over the flex side of the forearm.
            PrimitiveMeshes.Beveled(forearm, "ForeCarbon", new Vector3(side * 0.02f, -0.15f, -0.08f),
                new Vector3(0.11f, 0.15f, 0.05f), paint.Carbon, Quaternion.Euler(0f, 0f, side * 6f));
            PrimitiveMeshes.Beveled(forearm, "Gauntlet", new Vector3(0f, -0.17f, 0.075f),
                new Vector3(0.12f, 0.2f, 0.055f), paint.ArmorDark);
            PrimitiveMeshes.Box(forearm, "GauntletGlow", new Vector3(side * 0.072f, -0.18f, 0f),
                new Vector3(0.012f, 0.13f, 0.012f), paint.Neon);
            PrimitiveMeshes.Create("WristRing", PrimitiveMeshes.Cylinder, paint.Chrome, forearm,
                new Vector3(0f, -0.295f, 0f), new Vector3(0.15f, 0.022f, 0.15f));
            PrimitiveMeshes.Box(forearm, "WristGlow", new Vector3(0f, -0.32f, 0.075f),
                new Vector3(0.08f, 0.015f, 0.012f), paint.Neon);
            PrimitiveMeshes.Beveled(forearm, "Palm", new Vector3(0f, -0.36f, 0.015f),
                new Vector3(0.11f, 0.095f, 0.1f), paint.Joint);
            PrimitiveMeshes.Box(forearm, "Knuckles", new Vector3(0f, -0.365f, 0.075f),
                new Vector3(0.095f, 0.035f, 0.035f), paint.Chrome);
            for (int i = 0; i < 4; i++)
            {
                float fx = -0.045f + i * 0.03f;
                PrimitiveMeshes.Beveled(forearm, "Finger" + i, new Vector3(fx, -0.425f, 0.045f),
                    new Vector3(0.021f, 0.085f, 0.025f), paint.ArmorDark,
                    Quaternion.Euler(8f, 0f, (i - 1.5f) * 2f));
                PrimitiveMeshes.Box(forearm, "FingerTip" + i, new Vector3(fx, -0.475f, 0.055f),
                    new Vector3(0.018f, 0.045f, 0.02f), paint.Joint,
                    Quaternion.Euler(15f, 0f, (i - 1.5f) * 2f));
            }
            PrimitiveMeshes.Beveled(forearm, "Thumb", new Vector3(side * -0.075f, -0.395f, 0.035f),
                new Vector3(0.026f, 0.075f, 0.03f), paint.ArmorDark, Quaternion.Euler(14f, 0f, side * 24f));
            return pivot;
        }

        private static Transform BuildLeg(Transform parent, Vector3 hip, Paint paint)
        {
            var pivot = Pivot("Leg", parent, hip);
            PrimitiveMeshes.SphereObject(pivot, "HipJoint", Vector3.zero, 0.16f, paint.Joint);
            PrimitiveMeshes.Create("Thigh", PrimitiveMeshes.Capsule, paint.Carbon, pivot,
                new Vector3(0f, -0.2f, 0f), new Vector3(0.18f, 0.24f, 0.18f));
            PrimitiveMeshes.Beveled(pivot, "ThighPlate", new Vector3(0f, -0.18f, 0.08f),
                new Vector3(0.18f, 0.25f, 0.075f), paint.Armor);
            PrimitiveMeshes.Beveled(pivot, "ThighSidePlate", new Vector3(0f, -0.19f, -0.075f),
                new Vector3(0.16f, 0.21f, 0.055f), paint.ArmorDark);
            PrimitiveMeshes.Box(pivot, "ThighSeam", new Vector3(0f, -0.15f, 0.124f),
                new Vector3(0.1f, 0.018f, 0.012f), paint.Neon);
            PrimitiveMeshes.Box(pivot, "ThighMagenta", new Vector3(0.075f, -0.22f, 0.12f),
                new Vector3(0.012f, 0.12f, 0.01f), paint.Magenta);

            var shin = Pivot("Shin", pivot, new Vector3(0f, -0.42f, 0f));
            PrimitiveMeshes.SphereObject(shin, "KneeJoint", Vector3.zero, 0.16f, paint.Joint);
            PrimitiveMeshes.Create("KneeCap", PrimitiveMeshes.Cylinder, paint.Armor, shin,
                new Vector3(0f, 0f, 0.065f), new Vector3(0.12f, 0.025f, 0.12f), Quaternion.Euler(90f, 0f, 0f));
            PrimitiveMeshes.Create("KneeGlow", PrimitiveMeshes.Cylinder, paint.Neon, shin,
                new Vector3(0f, 0f, 0.082f), new Vector3(0.06f, 0.012f, 0.06f), Quaternion.Euler(90f, 0f, 0f));
            PrimitiveMeshes.Create("Shin", PrimitiveMeshes.Capsule, paint.Armor, shin,
                new Vector3(0f, -0.18f, 0f), new Vector3(0.15f, 0.2f, 0.15f));
            // Knee servo cluster: two chrome pins riding the transverse hinge.
            for (int pin = -1; pin <= 1; pin += 2)
                PrimitiveMeshes.SphereObject(shin, "KneeServo" + pin,
                    new Vector3(pin * 0.06f, 0.045f, -0.03f), 0.05f, paint.Chrome);
            PrimitiveMeshes.Beveled(shin, "ShinGuard", new Vector3(0f, -0.16f, 0.075f),
                new Vector3(0.12f, 0.24f, 0.06f), paint.ArmorDark);
            PrimitiveMeshes.Box(shin, "ShinGlow", new Vector3(0f, -0.17f, 0.112f),
                new Vector3(0.045f, 0.16f, 0.012f), paint.Neon);
            PrimitiveMeshes.Beveled(shin, "CalfPlate", new Vector3(0f, -0.16f, -0.075f),
                new Vector3(0.11f, 0.2f, 0.055f), paint.ArmorDark);
            PrimitiveMeshes.Box(shin, "CalfGlow", new Vector3(0f, -0.17f, -0.106f),
                new Vector3(0.04f, 0.14f, 0.012f), paint.Magenta);
            PrimitiveMeshes.Create("CalfPiston", PrimitiveMeshes.Cylinder, paint.Chrome, shin,
                new Vector3(0f, -0.2f, -0.12f), new Vector3(0.024f, 0.13f, 0.024f),
                Quaternion.Euler(8f, 0f, 0f));
            PrimitiveMeshes.SphereObject(shin, "Ankle", new Vector3(0f, -0.38f, 0f), 0.12f, paint.Joint);

            var foot = Pivot("Foot", shin, new Vector3(0f, -0.4f, 0.02f));
            PrimitiveMeshes.Create("AnkleCollar", PrimitiveMeshes.Cylinder, paint.Chrome, foot,
                new Vector3(0f, 0.005f, 0f), new Vector3(0.1f, 0.03f, 0.1f));
            PrimitiveMeshes.Beveled(foot, "Sole", new Vector3(0f, -0.07f, 0.07f),
                new Vector3(0.225f, 0.055f, 0.36f), paint.Dark);
            PrimitiveMeshes.Beveled(foot, "Heel", new Vector3(0f, -0.025f, -0.085f),
                new Vector3(0.19f, 0.13f, 0.14f), paint.ArmorDark);
            PrimitiveMeshes.Beveled(foot, "Instep", new Vector3(0f, -0.045f, 0.105f),
                new Vector3(0.205f, 0.12f, 0.18f), paint.Armor);
            PrimitiveMeshes.Beveled(foot, "ToeCap", new Vector3(0f, -0.055f, 0.255f),
                new Vector3(0.2f, 0.095f, 0.15f), paint.ArmorDark,
                Quaternion.Euler(4f, 0f, 0f));
            PrimitiveMeshes.Box(foot, "ToeSplit", new Vector3(0f, -0.11f, 0.27f),
                new Vector3(0.15f, 0.012f, 0.1f), paint.Neon);
            PrimitiveMeshes.Box(foot, "SoleGlow", new Vector3(0f, -0.105f, 0.07f),
                new Vector3(0.17f, 0.012f, 0.3f), paint.Neon);
            PrimitiveMeshes.Box(foot, "HeelGlow", new Vector3(0f, -0.04f, -0.16f),
                new Vector3(0.11f, 0.016f, 0.012f), paint.Magenta);
            PrimitiveMeshes.Create("HeelJet", PrimitiveMeshes.Cylinder, paint.ArmorDark, foot,
                new Vector3(0f, -0.02f, -0.175f), new Vector3(0.05f, 0.05f, 0.05f),
                Quaternion.Euler(90f, 0f, 0f));
            var jet = PrimitiveMeshes.Create("HeelJetGlow", PrimitiveMeshes.Cylinder, paint.Neon, foot,
                new Vector3(0f, -0.02f, -0.208f), new Vector3(0.036f, 0.014f, 0.036f),
                Quaternion.Euler(90f, 0f, 0f));
            jet.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return pivot;
        }

        /// <summary>
        /// Second-pass mechanical detail: actuators, cable runs, vents, housings and micro
        /// fasteners. Hardware clusters are skipped on remote ghosts to keep five-player races
        /// cheap; everything else is a fixed-cost one-time build.
        /// </summary>
        private void BuildMechanicalDetail(Paint paint, bool remote)
        {
            var rng = remote ? new DeterministicRandom(0x51)
                : new DeterministicRandom(0x5100 + AverageSeed(_definition != null ? _definition.id : ""));

            // Knee actuators: twin chrome cylinders hinged around each knee joint.
            foreach (var shin in new[] { _shinL, _shinR })
            {
                if (shin == null) continue;
                PrimitiveMeshes.Create("KneeActuator", PrimitiveMeshes.Cylinder, paint.Chrome, shin,
                    new Vector3(0.055f, 0.02f, 0.03f), new Vector3(0.028f, 0.11f, 0.028f),
                    Quaternion.Euler(0f, 0f, 8f));
                PrimitiveMeshes.Create("KneeActuatorInner", PrimitiveMeshes.Cylinder, paint.ArmorDark, shin,
                    new Vector3(0.075f, -0.045f, 0.03f), new Vector3(0.018f, 0.09f, 0.018f),
                    Quaternion.Euler(0f, 0f, 8f));
                PrimitiveMeshes.Box(shin, "CalfHousing", new Vector3(0f, -0.28f, -0.075f),
                    new Vector3(0.075f, 0.14f, 0.05f), paint.Deep);
                PrimitiveMeshes.Box(shin, "CalfHousingVent", new Vector3(0f, -0.28f, -0.108f),
                    new Vector3(0.045f, 0.09f, 0.012f), paint.Neon);
            }

            // Elbow pistons behind each upper arm.
            foreach (var arm in new[] { _armL, _armR })
            {
                if (arm == null) continue;
                PrimitiveMeshes.Create("BicepPiston", PrimitiveMeshes.Cylinder, paint.Chrome, arm,
                    new Vector3(0f, -0.14f, -0.1f), new Vector3(0.02f, 0.14f, 0.02f),
                    Quaternion.Euler(6f, 0f, 0f));
                PrimitiveMeshes.Beveled(arm, "ShoulderMount", new Vector3(0f, 0.005f, -0.05f),
                    new Vector3(0.13f, 0.09f, 0.1f), paint.ArmorDark);
            }

            // Chest intake shrouds and micro vents.
            PrimitiveMeshes.Beveled(_torso, "IntakeL", new Vector3(0.21f, 0.42f, 0.16f),
                new Vector3(0.055f, 0.17f, 0.09f), paint.Deep, Quaternion.Euler(0f, 0f, 10f));
            PrimitiveMeshes.Beveled(_torso, "IntakeR", new Vector3(-0.21f, 0.42f, 0.16f),
                new Vector3(0.055f, 0.17f, 0.09f), paint.Deep, Quaternion.Euler(0f, 0f, -10f));
            PrimitiveMeshes.Box(_torso, "IntakeGlowL", new Vector3(0.235f, 0.38f, 0.148f),
                new Vector3(0.016f, 0.07f, 0.014f), paint.Neon);
            PrimitiveMeshes.Box(_torso, "IntakeGlowR", new Vector3(-0.235f, 0.38f, 0.148f),
                new Vector3(0.016f, 0.07f, 0.014f), paint.Neon);
            for (int i = 0; i < 3; i++)
            {
                float y = 0.3f - i * 0.052f;
                PrimitiveMeshes.Box(_torso, "VentSlot" + i, new Vector3(0f, y, -0.222f),
                    new Vector3(0.21f - i * 0.045f, 0.014f, 0.014f), paint.Joint);
            }

            // Cable harness: hip to backpack on both sides plus a neck chute.
            AddCable(_hips, new Vector3(0.14f, 0.16f, -0.13f), 3, paint);
            AddCable(_hips, new Vector3(-0.14f, 0.16f, -0.13f), 3, paint);
            AddCable(_head, new Vector3(0.1f, -0.02f, -0.12f), 2, paint);
            AddCable(_head, new Vector3(-0.1f, -0.02f, -0.12f), 2, paint);

            if (remote) return;

            // Twin backpack exhaust bells with additive exhaust glow plates.
            for (int side = -1; side <= 1; side += 2)
            {
                var bell = PrimitiveMeshes.Create("ExhaustBell" + side, PrimitiveMeshes.Cylinder, paint.ArmorDark,
                    _torso, new Vector3(side * 0.12f, 0.235f, -0.325f), new Vector3(0.075f, 0.07f, 0.075f));
                PrimitiveMeshes.Create("ExhaustLip" + side, PrimitiveMeshes.Cylinder, paint.Chrome, bell.transform,
                    new Vector3(0f, 0.035f, 0f), new Vector3(0.085f, 0.014f, 0.085f));
                var flare = PrimitiveMeshes.Create("ExhaustFlare" + side, PrimitiveMeshes.Quad,
                    MaterialLibrary.Additive(_accent), _torso,
                    new Vector3(side * 0.12f, 0.16f, -0.33f), new Vector3(0.13f, 0.3f, 1f),
                    Quaternion.Euler(90f, 0f, 0f));
                var flareRenderer = flare.GetComponent<Renderer>();
                flareRenderer.shadowCastingMode = ShadowCastingMode.Off;
                flareRenderer.receiveShadows = false;
                _thrusterJets.Add(flare.transform);
            }

            // Fastener studs across chest plate, pauldrons and hip guards.
            var boltMaterial = paint.Chrome;
            for (int i = 0; i < 9; i++)
            {
                Transform host;
                Vector3 anchor;
                switch (i % 3)
                {
                    case 0:
                        host = _torso;
                        anchor = new Vector3(rng.Range(-0.16f, 0.16f), rng.Range(0.36f, 0.56f), 0.24f);
                        break;
                    case 1:
                        host = i % 2 == 0 ? _armL : _armR;
                        anchor = new Vector3(rng.Range(0f, 0.14f), rng.Range(0.03f, 0.11f), 0.16f);
                        break;
                    default:
                        host = _hips;
                        anchor = new Vector3(rng.Range(-0.22f, 0.22f), 0.14f, 0.13f);
                        break;
                }
                if (host == null) continue;
                PrimitiveMeshes.SphereObject(host, "Fastener" + i, anchor, 0.022f, boltMaterial);
            }

            // Sprint energy wake behind the boots.
            if (_speedWave != null) return;
            var waveGo = new GameObject("SprintWake");
            waveGo.transform.SetParent(_visual, false);
            waveGo.transform.localPosition = new Vector3(0f, 0.06f, -0.5f);
            var filter = waveGo.AddComponent<MeshFilter>();
            filter.sharedMesh = PrimitiveMeshes.Quad;
            var wakeRenderer = waveGo.AddComponent<MeshRenderer>();
            wakeRenderer.sharedMaterial = MaterialLibrary.Additive(
                Color.Lerp(_accent, Palette.Magenta, 0.35f));
            wakeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            wakeRenderer.receiveShadows = false;
            waveGo.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            waveGo.SetActive(false);
            _speedWave = waveGo.transform;
        }

        private static void AddCable(Transform parent, Vector3 anchor, int segments, Paint paint)
        {
            if (parent == null) return;
            var previous = anchor;
            float sway = anchor.x > 0f ? 1f : -1f;
            for (int i = 0; i < segments; i++)
            {
                Vector3 next = anchor + new Vector3(sway * (0.02f + i * 0.012f), -0.075f - i * 0.02f,
                    -0.05f - i * 0.035f);
                Vector3 mid = (previous + next) * 0.5f;
                Vector3 delta = next - previous;
                PrimitiveMeshes.Box(parent, "Cable" + (i < segments ? i : 0) + (sway > 0 ? "R" : "L") + i,
                    mid, new Vector3(0.018f, delta.magnitude, 0.018f), paint.Joint,
                    Quaternion.LookRotation(delta.normalized, Vector3.up) * Quaternion.Euler(90f, 0f, 0f));
                previous = next;
            }
        }

        private static int AverageSeed(string id)
        {
            int seed = 0;
            for (int i = 0; i < id.Length; i++) seed += id[i] * (i + 1);
            return seed & 0xFFFF;
        }

        private void AddStyleDetails(int style, Paint paint)
        {
            PrimitiveMeshes.Beveled(_torso, "Badge", new Vector3(0.19f, 0.35f, 0.19f),
                new Vector3(0.07f, 0.045f, 0.02f), paint.ArmorDark, Quaternion.Euler(0f, 0f, -18f));
            PrimitiveMeshes.Box(_torso, "BadgeGlow", new Vector3(0.19f, 0.35f, 0.205f),
                new Vector3(0.045f, 0.012f, 0.01f), paint.Magenta, Quaternion.Euler(0f, 0f, -18f));

            if (style == 0)
            {
                PrimitiveMeshes.Beveled(_head, "AeroFinL", new Vector3(-0.17f, 0.32f, -0.02f),
                    new Vector3(0.035f, 0.06f, 0.22f), paint.Armor, Quaternion.Euler(12f, 0f, -18f));
                PrimitiveMeshes.Beveled(_head, "AeroFinR", new Vector3(0.17f, 0.32f, -0.02f),
                    new Vector3(0.035f, 0.06f, 0.22f), paint.Armor, Quaternion.Euler(12f, 0f, 18f));
                PrimitiveMeshes.Box(_torso, "SpineBlade", new Vector3(0f, 0.31f, -0.36f),
                    new Vector3(0.04f, 0.32f, 0.018f), paint.Neon);
            }
            else if (style == 1)
            {
                PrimitiveMeshes.Box(_torso, "VoltFinL", new Vector3(-0.41f, 0.54f, -0.1f),
                    new Vector3(0.045f, 0.28f, 0.3f), paint.Neon, Quaternion.Euler(0f, 0f, -24f));
                PrimitiveMeshes.Box(_torso, "VoltFinR", new Vector3(0.41f, 0.54f, -0.1f),
                    new Vector3(0.045f, 0.28f, 0.3f), paint.Neon, Quaternion.Euler(0f, 0f, 24f));
                PrimitiveMeshes.Box(_torso, "VoltSpikeL", new Vector3(-0.3f, 0.69f, -0.06f),
                    new Vector3(0.05f, 0.12f, 0.05f), paint.ArmorDark, Quaternion.Euler(0f, 0f, -18f));
                PrimitiveMeshes.Box(_torso, "VoltSpikeR", new Vector3(0.3f, 0.69f, -0.06f),
                    new Vector3(0.05f, 0.12f, 0.05f), paint.ArmorDark, Quaternion.Euler(0f, 0f, 18f));
                PrimitiveMeshes.Box(_head, "VisorStripe", new Vector3(0f, 0.17f, 0.225f),
                    new Vector3(0.31f, 0.022f, 0.018f), paint.Magenta);
            }
            else if (style == 2)
            {
                PrimitiveMeshes.Beveled(_head, "Hood", new Vector3(0f, 0.235f, -0.13f),
                    new Vector3(0.4f, 0.25f, 0.24f), paint.ArmorDark);
                PrimitiveMeshes.Box(_head, "Antenna", new Vector3(0.13f, 0.45f, -0.1f),
                    new Vector3(0.018f, 0.31f, 0.018f), paint.Neon, Quaternion.Euler(0f, 0f, -12f));
                PrimitiveMeshes.Box(_head, "AntennaTip", new Vector3(0.163f, 0.6f, -0.1f),
                    new Vector3(0.028f, 0.03f, 0.028f), paint.Magenta);
                PrimitiveMeshes.Box(_torso, "ChestSplitExtra", new Vector3(0f, 0.42f, 0.26f),
                    new Vector3(0.016f, 0.28f, 0.012f), paint.Neon);
            }
            else if (style == 3)
            {
                PrimitiveMeshes.Beveled(_torso, "HaulerPadL", new Vector3(-0.46f, 0.56f, 0f),
                    new Vector3(0.31f, 0.17f, 0.35f), paint.Armor, Quaternion.Euler(0f, 0f, -12f));
                PrimitiveMeshes.Beveled(_torso, "HaulerPadR", new Vector3(0.46f, 0.56f, 0f),
                    new Vector3(0.31f, 0.17f, 0.35f), paint.Armor, Quaternion.Euler(0f, 0f, 12f));
                PrimitiveMeshes.Beveled(_torso, "Tank", new Vector3(0f, 0.35f, -0.39f),
                    new Vector3(0.24f, 0.5f, 0.17f), paint.ArmorDark);
                PrimitiveMeshes.Box(_torso, "TankGlow", new Vector3(0f, 0.35f, -0.485f),
                    new Vector3(0.05f, 0.36f, 0.012f), paint.Neon);
                PrimitiveMeshes.Box(_head, "BrowGlow", new Vector3(0f, 0.28f, 0.17f),
                    new Vector3(0.28f, 0.018f, 0.018f), paint.Magenta);
            }
            else if (style == 4)
            {
                PrimitiveMeshes.Beveled(_torso, "ProtoCore", new Vector3(0f, 0.46f, 0.235f),
                    new Vector3(0.16f, 0.16f, 0.025f), paint.Magenta, Quaternion.Euler(0f, 0f, 45f));
                PrimitiveMeshes.Box(_head, "Halo", new Vector3(0f, 0.44f, 0f),
                    new Vector3(0.5f, 0.022f, 0.5f), paint.Neon, Quaternion.Euler(12f, 0f, 0f));
                for (int i = 0; i < 3; i++)
                {
                    PrimitiveMeshes.Box(_hips, "DataSpike" + i,
                        new Vector3(-0.18f + i * 0.18f, 0.02f, -0.18f),
                        new Vector3(0.045f, 0.2f, 0.045f), paint.Neon, Quaternion.Euler(0f, 0f, 12f));
                }
            }
            else if (style == 5)
            {
                // PHANTOM X: black-budget phase frame. Mirrored slash canards, a split tail
                // radiator pair and hip phase splitters read as a distinct stealth silhouette.
                PrimitiveMeshes.Beveled(_head, "PhaseCanardL", new Vector3(-0.19f, 0.3f, 0.1f),
                    new Vector3(0.05f, 0.05f, 0.24f), paint.ArmorDark, Quaternion.Euler(0f, 0f, -20f));
                PrimitiveMeshes.Beveled(_head, "PhaseCanardR", new Vector3(0.19f, 0.3f, 0.1f),
                    new Vector3(0.05f, 0.05f, 0.24f), paint.ArmorDark, Quaternion.Euler(0f, 0f, 20f));
                PrimitiveMeshes.Box(_head, "CanardEdgeL", new Vector3(-0.23f, 0.265f, 0.2f),
                    new Vector3(0.016f, 0.024f, 0.14f), paint.Neon, Quaternion.Euler(0f, 0f, -20f));
                PrimitiveMeshes.Box(_head, "CanardEdgeR", new Vector3(0.23f, 0.265f, 0.2f),
                    new Vector3(0.016f, 0.024f, 0.14f), paint.Neon, Quaternion.Euler(0f, 0f, 20f));
                for (int side = -1; side <= 1; side += 2)
                {
                    // Twin tail radiators that split behind the backpack.
                    PrimitiveMeshes.Box(_torso, "PhaseRadiator" + side,
                        new Vector3(side * 0.26f, 0.56f, -0.34f),
                        new Vector3(0.06f, 0.42f, 0.16f), paint.ArmorDark,
                        Quaternion.Euler(0f, 0f, side * 14f));
                    PrimitiveMeshes.Box(_torso, "PhaseRadiatorGlow" + side,
                        new Vector3(side * 0.27f, 0.47f, -0.42f),
                        new Vector3(0.04f, 0.3f, 0.014f), paint.Neon, Quaternion.Euler(0f, 0f, side * 14f));
                    // Hip spline fins trace the phase wake.
                    PrimitiveMeshes.Box(_hips, "PhaseSpline" + side,
                        new Vector3(side * 0.3f, 0.02f, -0.2f),
                        new Vector3(0.05f, 0.3f, 0.2f), paint.Armor, Quaternion.Euler(0f, 0f, side * 10f));
                    PrimitiveMeshes.Box(_hips, "PhaseSplineGlow" + side,
                        new Vector3(side * 0.3f, -0.08f, -0.3f),
                        new Vector3(0.03f, 0.12f, 0.03f), paint.Neon);
                    // Mirrored shoulder lip: low-observable edge with a cyan underline.
                    PrimitiveMeshes.Box(_torso, "StealthLip" + side,
                        new Vector3(side * 0.315f, 0.6f, -0.05f),
                        new Vector3(0.05f, 0.035f, 0.3f), paint.ArmorDark);
                    PrimitiveMeshes.Box(_torso, "StealthLipGlow" + side,
                        new Vector3(side * 0.315f, 0.575f, 0.05f),
                        new Vector3(0.045f, 0.014f, 0.2f), paint.Neon);
                }
                // Split chest chevron: the coolant band across the sternum.
                PrimitiveMeshes.Box(_torso, "CoolantBandL", new Vector3(-0.075f, 0.4f, 0.245f),
                    new Vector3(0.13f, 0.022f, 0.012f), paint.Neon, Quaternion.Euler(0f, 0f, -16f));
                PrimitiveMeshes.Box(_torso, "CoolantBandR", new Vector3(0.075f, 0.4f, 0.245f),
                    new Vector3(0.13f, 0.022f, 0.012f), paint.Neon, Quaternion.Euler(0f, 0f, 16f));
            }
            else if (style == 6)
            {
                // NOVA TITAN: siege-class frame. Blast-loaded pauldrons, a reactor plate
                // keel and reinforced leg bracing read as an armoured heavy courier.
                for (int side = -1; side <= 1; side += 2)
                {
                    PrimitiveMeshes.Beveled(_torso, "SiegePauldron" + side,
                        new Vector3(side * 0.4f, 0.6f, 0f),
                        new Vector3(0.16f, 0.22f, 0.36f), paint.Armor, Quaternion.Euler(0f, 0f, side * -12f));
                    PrimitiveMeshes.Box(_torso, "SiegeVent" + side,
                        new Vector3(side * 0.4f, 0.55f, -0.19f),
                        new Vector3(0.1f, 0.05f, 0.02f), paint.Neon);
                    PrimitiveMeshes.Box(_torso, "BlastPlate" + side,
                        new Vector3(side * 0.26f, 0.44f, 0.22f),
                        new Vector3(0.14f, 0.2f, 0.05f), paint.ArmorDark, Quaternion.Euler(0f, 0f, side * 7f));
                    PrimitiveMeshes.Box(_hips, "LegBrace" + side,
                        new Vector3(side * 0.26f, -0.04f, 0.02f),
                        new Vector3(0.06f, 0.26f, 0.1f), paint.Armor);
                    PrimitiveMeshes.Box(_hips, "LegBraceGlow" + side,
                        new Vector3(side * 0.28f, -0.14f, 0.04f),
                        new Vector3(0.03f, 0.1f, 0.014f), paint.Neon);
                    PrimitiveMeshes.Beveled(_torso, "HipReinforce" + side,
                        new Vector3(side * 0.33f, 0.1f, -0.05f),
                        new Vector3(0.1f, 0.14f, 0.18f), paint.ArmorDark);
                }
                // Chest keel: the big reactor plate with stacked heat walls.
                PrimitiveMeshes.Beveled(_torso, "ReactorKeel", new Vector3(0f, 0.42f, 0.24f),
                    new Vector3(0.36f, 0.3f, 0.06f), paint.Armor);
                for (int wall = 0; wall < 3; wall++)
                    PrimitiveMeshes.Box(_torso, "KeelWall" + wall,
                        new Vector3(0f, 0.335f + wall * 0.085f, 0.272f),
                        new Vector3(0.3f - wall * 0.055f, 0.022f, 0.016f), paint.Neon);
                PrimitiveMeshes.Box(_torso, "KeelCore", new Vector3(0f, 0.44f, 0.285f),
                    new Vector3(0.075f, 0.075f, 0.02f), paint.Magenta);
                // Heavy holster: a cargo turbine on the right hip and a winch drum on the left.
                PrimitiveMeshes.Create("HelmDrum", PrimitiveMeshes.Cylinder, paint.ArmorDark, _hips,
                    new Vector3(-0.3f, 0.06f, -0.14f), new Vector3(0.16f, 0.24f, 0.16f),
                    Quaternion.Euler(0f, 0f, 84f));
                PrimitiveMeshes.Create("CargoTurbine", PrimitiveMeshes.Cylinder, paint.Chrome, _hips,
                    new Vector3(0.31f, 0.03f, -0.1f), new Vector3(0.14f, 0.2f, 0.14f),
                    Quaternion.Euler(0f, 0f, -84f));
                PrimitiveMeshes.Create("TurbineGlow", PrimitiveMeshes.Cylinder, paint.Neon, _hips,
                    new Vector3(0.31f, -0.08f, -0.1f), new Vector3(0.1f, 0.02f, 0.1f),
                    Quaternion.Euler(0f, 0f, -84f));
                // Shoulder ram horns used to brace hazards.
                for (int side = -1; side <= 1; side += 2)
                    PrimitiveMeshes.Box(_head, "RamHorn" + side,
                        new Vector3(side * 0.17f, 0.36f, 0.02f),
                        new Vector3(0.045f, 0.12f, 0.045f), paint.Armor,
                        Quaternion.Euler(0f, 0f, side * 18f));
            }
            else if (style == 7)
            {
                // ECHO: survey frame with a shoulder sonar dish and receiver rings.
                PrimitiveMeshes.Beveled(_torso, "SonarBack", new Vector3(0f, 0.42f, -0.4f),
                    new Vector3(0.42f, 0.34f, 0.1f), paint.ArmorDark);
                PrimitiveMeshes.Create("Dish", PrimitiveMeshes.Cylinder, paint.Neon, _torso,
                    new Vector3(-0.44f, 0.68f, -0.18f), new Vector3(0.13f, 0.05f, 0.13f),
                    Quaternion.Euler(24f, 0f, -34f));
                PrimitiveMeshes.Create("DishCore", PrimitiveMeshes.Cylinder, paint.Magenta, _torso,
                    new Vector3(-0.47f, 0.73f, -0.15f), new Vector3(0.045f, 0.07f, 0.045f),
                    Quaternion.Euler(24f, 0f, -34f));
                for (int i = 0; i < 3; i++)
                    PrimitiveMeshes.Box(_head, "ReceiverRing" + i, new Vector3(0f, 0.16f + i * 0.075f, 0.2f),
                        new Vector3(0.3f - i * 0.06f, 0.014f, 0.014f), paint.Neon);
                PrimitiveMeshes.Box(_torso, "Waveform", new Vector3(0f, 0.27f, 0.255f),
                    new Vector3(0.21f, 0.018f, 0.01f), paint.Neon);
            }
            else if (style == 8)
            {
                // SABLE: mirrored night frame with a deep cowl and blade shoulders.
                PrimitiveMeshes.Beveled(_head, "Cowl", new Vector3(0f, 0.3f, -0.08f),
                    new Vector3(0.46f, 0.2f, 0.34f), paint.ArmorDark);
                PrimitiveMeshes.Box(_head, "CowlEdge", new Vector3(0f, 0.23f, 0.16f),
                    new Vector3(0.42f, 0.025f, 0.03f), paint.Neon);
                for (int side = -1; side <= 1; side += 2)
                {
                    PrimitiveMeshes.Box(_torso, "MirrorShard" + side, new Vector3(side * 0.45f, 0.62f, -0.12f),
                        new Vector3(0.06f, 0.34f, 0.16f), paint.Neon, Quaternion.Euler(0f, 0f, side * 28f));
                    PrimitiveMeshes.Box(_hips, "VeilPanel" + side, new Vector3(side * 0.24f, 0.02f, -0.2f),
                        new Vector3(0.12f, 0.46f, 0.03f), paint.ArmorDark, Quaternion.Euler(0f, 0f, side * 6f));
                }
                PrimitiveMeshes.Beveled(_torso, "VoidCore", new Vector3(0f, 0.5f, 0.235f),
                    new Vector3(0.11f, 0.11f, 0.02f), paint.Magenta, Quaternion.Euler(0f, 0f, 45f));
            }
            else if (style == 9)
            {
                // HELIOS: solar bulwark with photovoltaic shoulder wings.
                PrimitiveMeshes.Beveled(_torso, "SolarPlate", new Vector3(0f, 0.5f, 0.24f),
                    new Vector3(0.3f, 0.3f, 0.03f), paint.ArmorDark);
                PrimitiveMeshes.Box(_torso, "SolarCellA", new Vector3(0f, 0.58f, 0.265f),
                    new Vector3(0.24f, 0.1f, 0.012f), paint.Neon);
                PrimitiveMeshes.Box(_torso, "SolarCellB", new Vector3(0f, 0.44f, 0.265f),
                    new Vector3(0.24f, 0.1f, 0.012f), paint.Neon);
                for (int side = -1; side <= 1; side += 2)
                {
                    PrimitiveMeshes.Box(_torso, "SolarWing" + side, new Vector3(side * 0.53f, 0.66f, -0.04f),
                        new Vector3(0.3f, 0.05f, 0.34f), paint.Neon, Quaternion.Euler(0f, 0f, side * -22f));
                    PrimitiveMeshes.Box(_torso, "SolarWingFrame" + side, new Vector3(side * 0.53f, 0.66f, -0.04f),
                        new Vector3(0.32f, 0.02f, 0.36f), paint.ArmorDark, Quaternion.Euler(0f, 0f, side * -22f));
                }
                PrimitiveMeshes.Create("SunDisc", PrimitiveMeshes.Cylinder, paint.Neon, _head,
                    new Vector3(0f, 0.4f, -0.06f), new Vector3(0.21f, 0.02f, 0.21f),
                    Quaternion.Euler(90f, 0f, 0f));
                PrimitiveMeshes.Create("SunCore", PrimitiveMeshes.Cylinder, paint.Magenta, _head,
                    new Vector3(0f, 0.4f, -0.06f), new Vector3(0.1f, 0.03f, 0.1f),
                    Quaternion.Euler(90f, 0f, 0f));
            }
        }

        private void AddLights(Color accent, bool remote)
        {
            if (remote) return;

            var rimGo = new GameObject("Runner Rim Light");
            rimGo.transform.SetParent(_visual, false);
            rimGo.transform.localPosition = new Vector3(0.65f, 1.5f, -0.95f);
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Point;
            rim.color = Color.Lerp(Color.white, accent, 0.2f);
            rim.range = 4.2f;
            rim.intensity = 4f;
            rim.shadows = LightShadows.None;

            var fillGo = new GameObject("Runner Soft Fill");
            fillGo.transform.SetParent(_visual, false);
            fillGo.transform.localPosition = new Vector3(-1f, 1.8f, -0.45f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.72f, 0.86f, 1f);
            fill.range = 4f;
            fill.intensity = 2f;
            fill.shadows = LightShadows.None;

            var opticGo = new GameObject("Runner Optic Fill");
            opticGo.transform.SetParent(_head, false);
            opticGo.transform.localPosition = new Vector3(0f, 0.18f, 0.4f);
            var optic = opticGo.AddComponent<Light>();
            optic.type = LightType.Point;
            optic.color = accent;
            optic.range = 1.7f;
            optic.intensity = 0.9f;
            optic.shadows = LightShadows.None;
        }

        private void AddTrails(TrailDefinition trail, Color accent, bool remote)
        {
            Color trailColor = trail != null ? trail.color : accent;
            _trail = CreateTrail("EnergyTrail", new Vector3(0f, 0.87f, -0.15f), trailColor,
                remote ? 0.11f : 0.18f, remote ? 0.24f : 0.36f, remote);
            CreateTrail("RibbonCyan", new Vector3(-0.21f, 0.98f, -0.26f), accent,
                remote ? 0.04f : 0.065f, remote ? 0.2f : 0.28f, remote);
            CreateTrail("RibbonMagenta", new Vector3(0.21f, 0.98f, -0.26f), Palette.Magenta,
                remote ? 0.035f : 0.055f, remote ? 0.18f : 0.25f, remote);
        }

        private TrailRenderer CreateTrail(string name, Vector3 position, Color color, float width,
            float lifetime, bool remote)
        {
            var trailGo = new GameObject(name);
            trailGo.transform.SetParent(_visual, false);
            trailGo.transform.localPosition = position;
            var trail = trailGo.AddComponent<TrailRenderer>();
            trail.time = lifetime;
            trail.minVertexDistance = remote ? 0.4f : 0.26f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, width), new Keyframe(1f, 0f));
            trail.material = MaterialLibrary.Trail(color);
            trail.startColor = new Color(color.r, color.g, color.b, remote ? 0.35f : 0.78f);
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;
            _trails.Add(trail);
            return trail;
        }

        private void AddHoverboard(Paint paint)
        {
            _hoverboard = new GameObject("Hoverboard");
            _hoverboard.transform.SetParent(_visual, false);
            _hoverboard.transform.localPosition = new Vector3(0f, 0.17f, 0.03f);
            PrimitiveMeshes.Beveled(_hoverboard.transform, "Deck", Vector3.zero,
                new Vector3(1.02f, 0.075f, 0.44f), paint.Dark);
            PrimitiveMeshes.Box(_hoverboard.transform, "DeckPlate", new Vector3(0f, 0.055f, 0f),
                new Vector3(0.82f, 0.018f, 0.29f), paint.ArmorDark);
            PrimitiveMeshes.Box(_hoverboard.transform, "DeckGlow", new Vector3(0f, 0.065f, 0f),
                new Vector3(0.72f, 0.012f, 0.23f), paint.Neon);
            PrimitiveMeshes.Box(_hoverboard.transform, "Wake", new Vector3(0f, -0.02f, -0.28f),
                new Vector3(0.62f, 0.018f, 0.13f), paint.Magenta);
            _hoverboard.SetActive(false);
        }

        private void AddShieldBubble(Color accent)
        {
            _shield = new GameObject("ShieldBubble");
            _shield.transform.SetParent(_visual, false);
            _shield.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            var bubble = PrimitiveMeshes.Create("Bubble", PrimitiveMeshes.Sphere,
                MaterialLibrary.Transparent(new Color(accent.r, accent.g, accent.b, 0.2f), 0.9f),
                _shield.transform, Vector3.zero, Vector3.one * 2.45f);
            bubble.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            _shield.SetActive(false);
        }

        private void AddAura(Color accent)
        {
            _aura = new GameObject("PowerAura");
            _aura.transform.SetParent(_visual, false);
            _aura.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            for (int i = 0; i < 3; i++)
            {
                var ring = PrimitiveMeshes.Create("AuraRing", PrimitiveMeshes.Cylinder,
                    MaterialLibrary.Additive(new Color(accent.r, accent.g, accent.b, 0.32f)),
                    _aura.transform, new Vector3(0f, -0.35f + i * 0.4f, 0f),
                    new Vector3(1.25f, 0.008f, 1.25f), Quaternion.Euler(0f, 0f, 8f + i * 25f));
                var r = ring.GetComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            _aura.SetActive(false);
        }

        private void AddNameTag(string displayName, bool remote, Color accent)
        {
            if (!remote || string.IsNullOrEmpty(displayName)) return;
            var tagGo = new GameObject("NameTag");
            tagGo.transform.SetParent(transform, false);
            tagGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            _nameTag = tagGo.AddComponent<TextMesh>();
            _nameTag.text = displayName;
            _nameTag.font = FontLibrary.Body;
            _nameTag.fontSize = 52;
            _nameTag.characterSize = 0.055f;
            _nameTag.anchor = TextAnchor.MiddleCenter;
            _nameTag.alignment = TextAlignment.Center;
            _nameTag.color = accent;
            var textRenderer = tagGo.GetComponent<MeshRenderer>();
            if (_nameTag.font != null) textRenderer.material = _nameTag.font.material;
            textRenderer.shadowCastingMode = ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
        }

        private void AddContactShadow()
        {
            if (_contactShadowTexture == null) _contactShadowTexture = TextureFactory.RadialGlow(64);
            var baseMaterial = MaterialLibrary.UnlitTransparentTextured("NV_ContactShadow_Base",
                _contactShadowTexture, new Color(0f, 0f, 0f, 0.52f), Vector2.one);
            _shadowMaterial = new Material(baseMaterial) { name = "NV_ContactShadow_Instance" };
            var shadowGo = new GameObject("ContactShadow");
            shadowGo.transform.SetParent(transform, false);
            shadowGo.transform.localPosition = new Vector3(0f, 0.025f, 0.055f);
            shadowGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shadowGo.transform.localScale = new Vector3(1.25f, 1.72f, 1f);
            var filter = shadowGo.AddComponent<MeshFilter>();
            filter.sharedMesh = PrimitiveMeshes.Quad;
            var renderer = shadowGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _shadowMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _shadow = shadowGo.transform;
        }

        private static Transform Pivot(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        private static void DoubleSided(Material material)
        {
            if (material != null && material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Off);
        }

        private void RegisterPulse(Transform target, float baseIntensity)
        {
            if (target == null) return;
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;
            var entry = renderer;
            entry.GetPropertyBlock(Fx.Block);
            _pulseRenderers.Add(entry);
        }

        /// <summary>Called when the runner consumes an air-jump charge mid-flight.</summary>
        public void NotifyDoubleJump() => _flipTimer = FlipDuration;

        public void Tick(float dt, float speedNormalised, float lateralVelocity, RunnerPose pose,
            float verticalVelocity = 0f, float groundHeight = 0f)
        {
            if (!_built || _visual == null) return;
            if (_pose != pose) _pose = pose;
            if (_pose == RunnerPose.Dead) _dead = true;
            if (_pose == RunnerPose.Victory) _victory = true;
            if (_pose == RunnerPose.Revive && _reviveProgress <= 0.01f) _reviveProgress = 0.01f;
            _landingTimer = Mathf.Max(0f, _landingTimer - dt);
            _flipTimer = Mathf.Max(0f, _flipTimer - dt);

            float speed = Mathf.Clamp01(speedNormalised);
            bool sprinting = speed > 0.62f;
            _sprintBlend = Mathf.Lerp(_sprintBlend, sprinting ? 1f : 0f, dt * (sprinting ? 3.6f : 5.2f));
            float sprint = _sprintBlend;
            _lateralLean = Mathf.Lerp(_lateralLean, Mathf.Clamp(-lateralVelocity * 0.06f, -13f, 13f), dt * 9f);
            float runSpeed = 5.4f + speed * 7.4f + sprint * 1.2f;
            _phase += dt * runSpeed * Mathf.PI;
            float s = Mathf.Sin(_phase);
            float stride = 58f + sprint * 9f;

            float runLean = 6.5f + speed * 9.5f + sprint * 5.0f;
            float torsoYaw = s * 4.5f;
            float hipsYaw = -s * 3.6f;
            float hipY = 0.94f + Mathf.Abs(s) * (0.028f + sprint * 0.008f);
            float legLX = s * stride;
            float legRX = -s * stride;
            float kneeLX = Mathf.Max(0f, -s) * (62f + sprint * 8f);
            float kneeRX = Mathf.Max(0f, s) * (62f + sprint * 8f);
            float armLX = -s * (56f + sprint * 10f);
            float armRX = s * (56f + sprint * 10f);
            float foreLX = -20f - Mathf.Max(0f, s) * (28f + sprint * 10f);
            float foreRX = -20f - Mathf.Max(0f, -s) * (28f + sprint * 10f);
            float torsoRoll = _lateralLean;
            float headYaw = -torsoYaw * 0.55f;
            float headPitch = -runLean * 0.28f;
            float visualY = 0f;
            Vector3 visualPos = Vector3.zero;
            Quaternion visualRot = Quaternion.Euler(0f, 0f, -_lateralLean * 0.18f);
            Quaternion torsoRot = Quaternion.Euler(runLean, torsoYaw, torsoRoll);
            Quaternion headRot = Quaternion.Euler(headPitch, headYaw, -_lateralLean * 0.3f);
            Quaternion hipsRot = Quaternion.Euler(0f, hipsYaw, _lateralLean * 0.3f);
            _bob = Mathf.Abs(s) * 0.028f;

            if (_pose == RunnerPose.Idle)
            {
                float idle = Mathf.Sin(Time.unscaledTime * 0.85f);
                legLX = legRX = 0f;
                kneeLX = kneeRX = 5f;
                armLX = -7f + idle * 2f;
                armRX = -7f - idle * 2f;
                foreLX = foreRX = -12f;
                torsoRot = Quaternion.Euler(1.7f, idle * 4f, idle * 1.3f);
                headRot = Quaternion.Euler(-2.2f, idle * 5f, 0f);
                hipsRot = Quaternion.Euler(0f, idle * 1.2f, 0f);
                hipY = 0.94f + Mathf.Sin(Time.unscaledTime * 1.7f) * 0.009f;
                visualRot = Quaternion.identity;
            }
            else if (_pose == RunnerPose.Jump)
            {
                bool rising = verticalVelocity > 0.5f;
                if (rising)
                {
                    legLX = 38f; legRX = -18f;
                    kneeLX = 68f; kneeRX = 26f;
                    armLX = -82f; armRX = -42f;
                    foreLX = -42f; foreRX = -30f;
                    torsoRot = Quaternion.Euler(13f, 0f, _lateralLean);
                }
                else
                {
                    // Descending: football-carry sweep, torso into the wind, ready to brace.
                    legLX = 22f; legRX = 30f;
                    kneeLX = 34f; kneeRX = 50f;
                    armLX = -52f; armRX = -36f;
                    foreLX = -24f; foreRX = -20f;
                    torsoRot = Quaternion.Euler(15f, 0f, _lateralLean);
                    hipsRot = Quaternion.Euler(-2f, 0f, 0f);
                }
                headRot = Quaternion.Euler(-5f, 0f, 0f);
                hipsRot = Quaternion.Euler(-4f, 0f, 0f);
            }
            else if (_pose == RunnerPose.Slide)
            {
                visualPos = new Vector3(0f, -0.35f, 0.02f);
                hipY = 0.55f;
                torsoRot = Quaternion.Euler(64f, 0f, _lateralLean * 1.25f);
                headRot = Quaternion.Euler(-48f, 0f, -_lateralLean * 0.4f);
                hipsRot = Quaternion.Euler(-8f, 0f, 0f);
                legLX = 74f; legRX = -12f;
                kneeLX = -22f; kneeRX = 76f;
                armLX = 48f; armRX = 8f;
                foreLX = -34f; foreRX = -52f;
                visualRot = Quaternion.Euler(0f, 0f, -_lateralLean * 0.16f);
            }
            else if (_pose == RunnerPose.Dead)
            {
                _deathSpin += dt;
                float fall = Mathf.Clamp01(_deathSpin * 2.2f);
                visualPos = new Vector3(0f, -0.12f * fall, 0.18f * fall);
                visualRot = Quaternion.Euler(-10f - fall * 62f, fall * 18f, Mathf.Sin(_deathSpin * 7f) * 11f);
                legLX = -22f; legRX = 36f;
                kneeLX = 26f; kneeRX = 58f;
                armLX = 108f; armRX = 76f;
                foreLX = -22f; foreRX = -14f;
                torsoRot = Quaternion.Euler(18f, 0f, 13f);
                headRot = Quaternion.Euler(12f, 0f, 0f);
                hipsRot = Quaternion.identity;
            }
            else if (_pose == RunnerPose.Victory)
            {
                visualPos = new Vector3(0f, Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5.4f)) * 0.075f, 0f);
                armLX = -154f; armRX = -154f;
                foreLX = -20f; foreRX = -20f;
                legLX = 11f; legRX = -11f;
                kneeLX = 8f; kneeRX = 8f;
                torsoRot = Quaternion.Euler(-5f, 0f, 0f);
                headRot = Quaternion.Euler(-8f, 0f, 0f);
                hipsRot = Quaternion.identity;
            }
            else if (_pose == RunnerPose.Revive)
            {
                _reviveProgress = Mathf.Min(1.2f, _reviveProgress + dt * 1.05f);
                float rise = Mathf.Clamp01(_reviveProgress);
                float kneel = Mathf.Clamp01(_reviveProgress * 2f);
                hipY = 0.94f - 0.34f * (1f - rise);
                legLX = legRX = -34f * Mathf.Lerp(1f, 0f, Mathf.Clamp01((rise - 0.85f) / 0.35f));
                kneeLX = kneeRX = Mathf.Lerp(96f, 5f, Mathf.Clamp01((rise - 0.7f) / 0.3f));
                armLX = armRX = -14f + kneel * 6f;
                foreLX = foreRX = -36f + rise * 26f;
                torsoRot = Quaternion.Euler(30f * (1f - rise), Mathf.Sin(Time.unscaledTime * 1.1f) * 2.4f, 0f);
                headRot = Quaternion.Euler(10f * (1f - rise) - 2f, 0f, 0f);
                hipsRot = Quaternion.Euler(-6f * (1f - rise), 0f, 0f);
                visualRot = Quaternion.identity;
            }

            // Double-jump somersault: a full forward roll layered over the jump tuck.
            if (_flipTimer > 0f && _pose == RunnerPose.Jump)
            {
                float flip = Mathf.Clamp01(1f - _flipTimer / FlipDuration);
                float roll = Mathf.SmoothStep(0f, 1f, flip);
                float tuck = Mathf.Clamp01((flip - 0.06f) / 0.2f);
                float untuck = Mathf.Clamp01((flip - 0.68f) / 0.26f);
                float tuckAmount = tuck * (1f - untuck);
                visualRot = Quaternion.Euler(-360f * roll, 0f, -_lateralLean * 0.18f);
                legLX = Mathf.Lerp(legLX, 96f, tuckAmount);
                legRX = Mathf.Lerp(legRX, 88f, tuckAmount);
                kneeLX = Mathf.Lerp(kneeLX, 118f, tuckAmount);
                kneeRX = Mathf.Lerp(kneeRX, 112f, tuckAmount);
                armLX = Mathf.Lerp(armLX, -64f, tuckAmount);
                armRX = Mathf.Lerp(armRX, -64f, tuckAmount);
                foreLX = Mathf.Lerp(foreLX, -84f, tuckAmount);
                foreRX = Mathf.Lerp(foreRX, -84f, tuckAmount);
                torsoRot = Quaternion.Euler(24f, 0f, _lateralLean * 0.5f);
                headRot = Quaternion.Euler(-14f, 0f, 0f);
            }

            // Landing compression is layered over the run cycle, never replacing a jump or slide.
            if (_landingTimer > 0f && (_pose == RunnerPose.Run || _pose == RunnerPose.Idle))
            {
                float blend = _landingTimer / LandingDuration;
                float impact = Mathf.Clamp01(_landingImpact);
                hipY -= 0.13f * impact * blend;
                visualY -= 0.035f * impact * blend;
                legLX = Mathf.Lerp(legLX, 30f, blend * 0.8f);
                legRX = Mathf.Lerp(legRX, -9f, blend * 0.8f);
                kneeLX = Mathf.Lerp(kneeLX, 58f, blend * 0.9f);
                kneeRX = Mathf.Lerp(kneeRX, 22f, blend * 0.9f);
                torsoRot = Quaternion.Euler(Mathf.Lerp(runLean, 26f, blend * 0.8f), torsoYaw,
                    torsoRoll);
                headRot = Quaternion.Euler(-6f, headYaw, -_lateralLean * 0.3f);
            }

            visualPos.y += visualY;
            float k = 1f - Mathf.Exp(-dt * (sprint > 0.5f ? 17f : 15f));
            _visual.localPosition = Vector3.Lerp(_visual.localPosition, visualPos, k);
            _visual.localRotation = Quaternion.Slerp(_visual.localRotation, visualRot, k);
            _hips.localPosition = Vector3.Lerp(_hips.localPosition, new Vector3(0f, hipY, 0f), k);
            _hips.localRotation = Quaternion.Slerp(_hips.localRotation, hipsRot, k);
            _torso.localRotation = Quaternion.Slerp(_torso.localRotation, torsoRot, k);
            _head.localRotation = Quaternion.Slerp(_head.localRotation, headRot, k);
            _legL.localRotation = Quaternion.Slerp(_legL.localRotation, Quaternion.Euler(legLX, 0f, 0f), k);
            _legR.localRotation = Quaternion.Slerp(_legR.localRotation, Quaternion.Euler(legRX, 0f, 0f), k);
            _shinL.localRotation = Quaternion.Slerp(_shinL.localRotation, Quaternion.Euler(kneeLX, 0f, 0f), k);
            _shinR.localRotation = Quaternion.Slerp(_shinR.localRotation, Quaternion.Euler(kneeRX, 0f, 0f), k);
            _armL.localRotation = Quaternion.Slerp(_armL.localRotation,
                Quaternion.Euler(armLX, 0f, -8f + _lateralLean * 0.18f), k);
            _armR.localRotation = Quaternion.Slerp(_armR.localRotation,
                Quaternion.Euler(armRX, 0f, 8f + _lateralLean * 0.18f), k);
            _forearmL.localRotation = Quaternion.Slerp(_forearmL.localRotation, Quaternion.Euler(foreLX, 0f, 0f), k);
            _forearmR.localRotation = Quaternion.Slerp(_forearmR.localRotation, Quaternion.Euler(foreRX, 0f, 0f), k);

            if (_footL != null)
            {
                float footLX = Mathf.Clamp(-(legLX + kneeLX) * 0.7f, -55f, 40f);
                _footL.localRotation = Quaternion.Slerp(_footL.localRotation, Quaternion.Euler(footLX, 0f, 0f), k);
            }
            if (_footR != null)
            {
                float footRX = Mathf.Clamp(-(legRX + kneeRX) * 0.7f, -55f, 40f);
                _footR.localRotation = Quaternion.Slerp(_footR.localRotation, Quaternion.Euler(footRX, 0f, 0f), k);
            }

            UpdateTrails(speed);
            UpdateShadow(groundHeight);
            UpdateEnergy(dt, speed);

            if (_aura != null && _aura.activeSelf)
                _aura.transform.Rotate(Vector3.up, dt * 120f, Space.Self);

            if (_nameTag != null)
            {
                var cam = Camera.main;
                if (cam != null)
                    _nameTag.transform.rotation = Quaternion.LookRotation(
                        _nameTag.transform.position - cam.transform.position, Vector3.up);
            }
        }

        private void UpdateEnergy(float dt, float speed)
        {
            float time = Time.unscaledTime;
            float flicker = _dead ? Mathf.Max(0f, 1f - _deathSpin * 1.6f) : 1f;
            float pulse = 0.62f + Mathf.Sin(time * (5.4f + speed * 9.5f)) * 0.24f
                         + Mathf.Sin(time * 13.7f) * 0.08f;
            float intensity = (2.9f + speed * 2.8f + pulse) * flicker;
            var emission = new Color(_accent.r * intensity, _accent.g * intensity,
                _accent.b * intensity, 1f);
            Fx.Block.SetColor(EmissionColorId, emission);
            for (int i = 0; i < _pulseRenderers.Count; i++)
            {
                if (_pulseRenderers[i] == null) continue;
                _pulseRenderers[i].SetPropertyBlock(Fx.Block);
            }

            float jet = Mathf.Clamp01(speed * 1.35f + (_flipTimer > 0f ? 0.7f : 0f)) * flicker;
            for (int i = 0; i < _thrusterJets.Count; i++)
            {
                var flame = _thrusterJets[i];
                if (flame == null) continue;
                float wave = 0.55f + Mathf.Sin(time * 21f + i * 2.4f) * 0.45f;
                flame.localScale = new Vector3(0.13f, (0.12f + wave * 0.3f) * (1f + jet), 1f);
            }

            if (_speedWave != null)
            {
                bool sprintWake = (_pose == RunnerPose.Run || _pose == RunnerPose.Slide ||
                                   _pose == RunnerPose.Landing) && speed > 0.5f && !_dead;
                float wakeScale = sprintWake ? speed * (0.62f + Mathf.Sin(time * 18f) * 0.1f) : 0f;
                _speedWave.localScale = new Vector3(wakeScale * 1.6f, Mathf.Max(0.001f, wakeScale), 1f);
                _speedWave.gameObject.SetActive(wakeScale > 0.01f);
            }
        }

        private void UpdateTrails(float speed)
        {
            bool active = _pose != RunnerPose.Idle && _pose != RunnerPose.Dead &&
                          _pose != RunnerPose.Revive && speed > 0.04f;
            for (int i = 0; i < _trails.Count; i++)
            {
                var trail = _trails[i];
                if (trail == null) continue;
                trail.emitting = active;
                trail.widthMultiplier = active ? (0.72f + speed * 0.8f) : 0.2f;
            }
        }

        private void UpdateShadow(float groundHeight)
        {
            if (_shadow == null) return;
            float height = Mathf.Max(0f, groundHeight);
            float fade = Mathf.Clamp01(height / 2.4f);
            _shadow.localPosition = new Vector3(0f, -height + 0.025f, 0.055f);
            float grow = 1f + fade * 0.55f;
            _shadow.localScale = new Vector3(1.25f * grow, 1.72f * grow, 1f);
            if (_shadowMaterial != null)
            {
                var color = new Color(0f, 0f, 0f, Mathf.Lerp(0.52f, 0.08f, fade));
                if (_shadowMaterial.HasProperty("_BaseColor")) _shadowMaterial.SetColor("_BaseColor", color);
                if (_shadowMaterial.HasProperty("_Color")) _shadowMaterial.SetColor("_Color", color);
            }
        }

        public void NotifyLanding(float impact)
        {
            if (!_built) return;
            _landingImpact = Mathf.Clamp01(impact / 13f);
            _landingTimer = LandingDuration;
        }

        public void SetPose(RunnerPose pose)
        {
            _pose = pose;
            _dead = pose == RunnerPose.Dead;
            _victory = pose == RunnerPose.Victory;
            if (pose == RunnerPose.Dead) _deathSpin = 0f;
            if (pose == RunnerPose.Revive) _reviveProgress = 0.01f;
            if (pose == RunnerPose.Jump || pose == RunnerPose.Slide) _landingTimer = 0f;
        }

        public void SetShield(bool active) { if (_shield != null) _shield.SetActive(active); }
        public void SetHoverboard(bool active) { if (_hoverboard != null) _hoverboard.SetActive(active); }
        public void SetAura(bool active) { if (_aura != null) _aura.SetActive(active); }

        public void SetTrailColor(Color color)
        {
            if (_trail == null) return;
            _trail.startColor = new Color(color.r, color.g, color.b, 0.8f);
            _trail.endColor = new Color(color.r, color.g, color.b, 0f);
        }

        private void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            if (_shadowMaterial != null)
            {
                if (Application.isPlaying) Destroy(_shadowMaterial);
                else DestroyImmediate(_shadowMaterial);
                _shadowMaterial = null;
            }
            _visual = null;
            _hips = null;
            _torso = null;
            _head = null;
            _armL = _armR = _forearmL = _forearmR = null;
            _legL = _legR = _shinL = _shinR = _footL = _footR = null;
            _hoverboard = null;
            _shield = null;
            _aura = null;
            _trail = null;
            _trails.Clear();
            _pulseRenderers.Clear();
            _thrusterJets.Clear();
            _speedWave = null;
            _nameTag = null;
            _shadow = null;
            _built = false;
        }

        private void OnDestroy()
        {
            if (_shadowMaterial != null)
            {
                if (Application.isPlaying) Destroy(_shadowMaterial);
                else DestroyImmediate(_shadowMaterial);
                _shadowMaterial = null;
            }
        }
    }

    internal static class TransformExtensions
    {
        public static bool TryGetChild(this Transform parent, int index, out Transform child)
        {
            if (parent != null && index < parent.childCount)
            {
                child = parent.GetChild(index);
                return true;
            }
            child = null;
            return false;
        }
    }
}
