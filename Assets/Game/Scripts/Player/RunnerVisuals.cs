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
        Victory
    }

    /// <summary>
    /// Original procedural runner rig. The silhouette is built from chamfered armour plates,
    /// tapered composite shells, articulated hands/feet and emissive seams, then animated with
    /// a speed-reactive procedural run cycle. It deliberately keeps the same public API as the
    /// previous box rig so local play, remote runners and the garage preview stay compatible.
    /// </summary>
    public sealed class RunnerVisuals : MonoBehaviour
    {
        private sealed class Paint
        {
            public Material Suit;
            public Material Armor;
            public Material ArmorDark;
            public Material Chrome;
            public Material Joint;
            public Material Neon;
            public Material Magenta;
            public Material Dark;
            public Material Visor;
        }

        private const float LandingDuration = 0.22f;
        private static Texture2D _contactShadowTexture;

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
        private float _landingTimer;
        private float _landingImpact;
        private bool _built;
        private bool _dead;
        private bool _victory;
        private float _deathSpin;
        private Transform _shadow;
        private Material _shadowMaterial;

        public CharacterDefinition Definition => _definition;

        public void Build(CharacterDefinition character, TrailDefinition trail, bool remote, string displayName)
        {
            _definition = character;
            Clear();

            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);

            Color suitDark = Color.Lerp(character.secondary, Palette.Void, 0.70f);
            Color accent = character.primary;
            Color pearl = Color.Lerp(character.secondary, Color.white, 0.62f);
            var paint = new Paint
            {
                Suit = MaterialLibrary.LitTextured("runner_suit:" + character.id + ":" + character.armorStyle,
                    TextureFactory.ArmorSurface(), suitDark, 0.22f, 0.62f, new Vector2(1.7f, 1.7f),
                    TextureFactory.ArmorNormal(), new Vector2(1.7f, 1.7f), 0.42f),
                Armor = MaterialLibrary.LitTextured("runner_armor:" + character.id + ":" + character.armorStyle,
                    TextureFactory.ArmorSurface(), pearl, 0.62f, 0.84f, new Vector2(1.15f, 1.15f),
                    TextureFactory.ArmorNormal(), new Vector2(1.15f, 1.15f), 0.58f),
                ArmorDark = MaterialLibrary.LitTextured("runner_armor_dark:" + character.id + ":" + character.armorStyle,
                    TextureFactory.ArmorSurface(), Color.Lerp(character.primary, Palette.Void, 0.45f),
                    0.72f, 0.76f, new Vector2(1.35f, 1.35f), TextureFactory.ArmorNormal(),
                    new Vector2(1.35f, 1.35f), 0.5f),
                Chrome = MaterialLibrary.Lit(new Color(0.62f, 0.72f, 0.84f), 0.98f, 0.97f),
                Joint = MaterialLibrary.Lit(new Color(0.025f, 0.038f, 0.06f), 0.72f, 0.7f),
                Neon = MaterialLibrary.Lit(accent, 0.08f, 0.64f, accent, 4.1f),
                Magenta = MaterialLibrary.Lit(Palette.Magenta, 0.1f, 0.58f, Palette.Magenta, 2.9f),
                Dark = MaterialLibrary.Lit(Palette.Void, 0.32f, 0.55f),
                Visor = MaterialLibrary.Lit(Color.Lerp(accent, Palette.Void, 0.7f), 0.94f, 0.99f,
                    Color.Lerp(accent, Color.white, 0.16f), 1.15f)
            };
            DoubleSided(paint.Suit);
            DoubleSided(paint.Armor);
            DoubleSided(paint.ArmorDark);
            DoubleSided(paint.Joint);
            DoubleSided(paint.Dark);

            BuildBody(paint);
            AddStyleDetails(character.armorStyle, paint);
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
                new Vector3(0.35f, 0.23f, 0.29f), paint.Suit);
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

            // Chest: dark pressure shell, layered pearl carapace and a visible reactor core.
            _torso = Pivot("Torso", _hips, new Vector3(0f, 0.12f, 0f));
            PrimitiveMeshes.Create("Abdomen", PrimitiveMeshes.Capsule, paint.Suit, _torso,
                new Vector3(0f, 0.23f, 0f), new Vector3(0.34f, 0.26f, 0.28f));
            PrimitiveMeshes.Beveled(_torso, "AbdominalPlate", new Vector3(0f, 0.245f, 0.14f),
                new Vector3(0.24f, 0.13f, 0.055f), paint.ArmorDark);
            PrimitiveMeshes.Box(_torso, "AbdominalSeam", new Vector3(0f, 0.235f, 0.17f),
                new Vector3(0.16f, 0.012f, 0.012f), paint.Neon);
            PrimitiveMeshes.Create("ChestShell", PrimitiveMeshes.Capsule, paint.Suit, _torso,
                new Vector3(0f, 0.45f, 0f), new Vector3(0.47f, 0.34f, 0.33f));
            PrimitiveMeshes.Create("ChestPlate", PrimitiveMeshes.Capsule, paint.Armor, _torso,
                new Vector3(0f, 0.47f, 0.045f), new Vector3(0.45f, 0.3f, 0.27f));
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

            // Helmet: pearl crown, angled jaw, deep visor and luminous side optics.
            _head = Pivot("Head", _torso, new Vector3(0f, 0.71f, 0f));
            PrimitiveMeshes.Create("Neck", PrimitiveMeshes.Cylinder, paint.Joint, _head,
                new Vector3(0f, -0.045f, 0f), new Vector3(0.11f, 0.085f, 0.11f));
            PrimitiveMeshes.Create("NeckRing", PrimitiveMeshes.Cylinder, paint.Chrome, _head,
                new Vector3(0f, -0.005f, 0f), new Vector3(0.125f, 0.012f, 0.125f));
            PrimitiveMeshes.Create("HelmetShell", PrimitiveMeshes.Sphere, paint.Armor, _head,
                new Vector3(0f, 0.15f, 0f), new Vector3(0.36f, 0.34f, 0.36f));
            PrimitiveMeshes.Beveled(_head, "HelmetCrown", new Vector3(0f, 0.285f, -0.025f),
                new Vector3(0.28f, 0.11f, 0.29f), paint.ArmorDark);
            PrimitiveMeshes.Beveled(_head, "HelmetRear", new Vector3(0f, 0.13f, -0.17f),
                new Vector3(0.26f, 0.21f, 0.08f), paint.ArmorDark);
            PrimitiveMeshes.Box(_head, "HelmetRearGlow", new Vector3(0f, 0.16f, -0.213f),
                new Vector3(0.12f, 0.018f, 0.012f), paint.Neon);
            PrimitiveMeshes.Create("Visor", PrimitiveMeshes.Sphere, paint.Visor, _head,
                new Vector3(0f, 0.165f, 0.135f), new Vector3(0.29f, 0.135f, 0.16f));
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
            PrimitiveMeshes.Create("UpperArm", PrimitiveMeshes.Capsule, paint.Suit, pivot,
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
            PrimitiveMeshes.Create("Thigh", PrimitiveMeshes.Capsule, paint.Suit, pivot,
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
            return pivot;
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

        public void Tick(float dt, float speedNormalised, float lateralVelocity, RunnerPose pose,
            float verticalVelocity = 0f, float groundHeight = 0f)
        {
            if (!_built || _visual == null) return;
            if (_pose != pose) _pose = pose;
            if (_pose == RunnerPose.Dead) _dead = true;
            if (_pose == RunnerPose.Victory) _victory = true;
            _landingTimer = Mathf.Max(0f, _landingTimer - dt);

            float speed = Mathf.Clamp01(speedNormalised);
            _lateralLean = Mathf.Lerp(_lateralLean, Mathf.Clamp(-lateralVelocity * 0.06f, -13f, 13f), dt * 9f);
            float runSpeed = 5.4f + speed * 7.2f;
            _phase += dt * runSpeed * Mathf.PI;
            float s = Mathf.Sin(_phase);

            float runLean = 6.5f + speed * 9.5f;
            float torsoYaw = s * 4.5f;
            float hipsYaw = -s * 3.6f;
            float hipY = 0.94f + Mathf.Abs(s) * 0.028f;
            float legLX = s * 58f;
            float legRX = -s * 58f;
            float kneeLX = Mathf.Max(0f, -s) * 62f;
            float kneeRX = Mathf.Max(0f, s) * 62f;
            float armLX = -s * 56f;
            float armRX = s * 56f;
            float foreLX = -20f - Mathf.Max(0f, s) * 28f;
            float foreRX = -20f - Mathf.Max(0f, -s) * 28f;
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
                    legLX = 22f; legRX = 30f;
                    kneeLX = 34f; kneeRX = 50f;
                    armLX = -38f; armRX = -28f;
                    foreLX = -18f; foreRX = -16f;
                    torsoRot = Quaternion.Euler(9f, 0f, _lateralLean);
                }
                headRot = Quaternion.Euler(-5f, 0f, 0f);
                hipsRot = Quaternion.Euler(-4f, 0f, 0f);
            }
            else if (_pose == RunnerPose.Slide)
            {
                visualPos = new Vector3(0f, -0.35f, 0f);
                hipY = 0.55f;
                torsoRot = Quaternion.Euler(64f, 0f, _lateralLean * 1.25f);
                headRot = Quaternion.Euler(-48f, 0f, -_lateralLean * 0.4f);
                hipsRot = Quaternion.Euler(-8f, 0f, 0f);
                legLX = 74f; legRX = -12f;
                kneeLX = -22f; kneeRX = 76f;
                armLX = 48f; armRX = 20f;
                foreLX = -14f; foreRX = -8f;
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
            float k = 1f - Mathf.Exp(-dt * 15f);
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

        private void UpdateTrails(float speed)
        {
            bool active = _pose != RunnerPose.Idle && _pose != RunnerPose.Dead && speed > 0.04f;
            for (int i = 0; i < _trails.Count; i++)
            {
                var trail = _trails[i];
                if (trail == null) continue;
                trail.emitting = active;
                trail.widthMultiplier = active ? 0.72f + speed * 0.72f : 0.2f;
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
