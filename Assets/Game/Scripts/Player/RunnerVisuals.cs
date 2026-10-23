using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;

namespace NeonVelocity.Player
{
    public enum RunnerPose
    {
        Idle,
        Run,
        Jump,
        Slide,
        Dead,
        Victory
    }

    /// <summary>
    /// Original procedural runner rig built from Unity primitives. A shared hierarchy and
    /// animation driver is used for every character and every remote player.
    /// </summary>
    public sealed class RunnerVisuals : MonoBehaviour
    {
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
        private GameObject _hoverboard;
        private GameObject _shield;
        private GameObject _aura;
        private TrailRenderer _trail;
        private TextMesh _nameTag;
        private RunnerPose _pose = RunnerPose.Idle;
        private float _phase;
        private float _bob;
        private float _lateralLean;
        private bool _built;
        private bool _dead;
        private bool _victory;
        private float _deathSpin;

        public CharacterDefinition Definition => _definition;

        public void Build(CharacterDefinition character, TrailDefinition trail, bool remote, string displayName)
        {
            _definition = character;
            Clear();
            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);
            Color suitDark = Color.Lerp(character.secondary, Palette.Void, 0.74f);
            Color accent = character.primary;
            Color pearl = Color.Lerp(character.secondary, Color.white, 0.58f);
            Material suit = MaterialLibrary.Lit(suitDark, 0.48f, 0.78f);
            Material armor = MaterialLibrary.Lit(pearl, 0.4f, 0.82f);
            Material armorDark = MaterialLibrary.Lit(Color.Lerp(character.primary, Palette.Void, 0.48f), 0.58f, 0.8f);
            Material chrome = MaterialLibrary.Lit(new Color(0.62f, 0.72f, 0.84f), 0.98f, 0.97f);
            Material joint = MaterialLibrary.Lit(new Color(0.025f, 0.038f, 0.06f), 0.72f, 0.76f);
            Material neon = MaterialLibrary.Lit(accent, 0.12f, 0.72f, accent, 4.2f);
            Material dark = MaterialLibrary.Lit(Palette.Void, 0.35f, 0.58f);
            Material visor = MaterialLibrary.Lit(Color.Lerp(accent, Palette.Void, 0.68f), 0.92f, 0.99f,
                Color.Lerp(accent, Color.white, 0.18f), 1.05f);
            Material trailMat = MaterialLibrary.Trail(trail != null ? trail.color : character.trail);

            // Layered pelvis: dark undersuit, bright hip armour, chrome belt and a narrow
            // emissive seam. The extra silhouette break makes the legs readable at speed.
            _hips = Pivot("Hips", _visual, new Vector3(0f, 0.94f, 0f));
            PrimitiveMeshes.Box(_hips, "PelvisCore", new Vector3(0f, 0.035f, 0f), new Vector3(0.32f, 0.2f, 0.25f), suit);
            PrimitiveMeshes.Box(_hips, "PelvisPlate", new Vector3(0f, 0.045f, 0.115f), new Vector3(0.29f, 0.16f, 0.09f), armor);
            PrimitiveMeshes.Box(_hips, "Belt", new Vector3(0f, 0.155f, 0f), new Vector3(0.43f, 0.052f, 0.27f), chrome);
            PrimitiveMeshes.Box(_hips, "BeltGlow", new Vector3(0f, 0.158f, 0.142f), new Vector3(0.23f, 0.018f, 0.012f), neon);
            PrimitiveMeshes.Box(_hips, "HipGuardL", new Vector3(-0.245f, 0.055f, 0f), new Vector3(0.11f, 0.24f, 0.29f),
                armor, Quaternion.Euler(0f, 0f, 9f));
            PrimitiveMeshes.Box(_hips, "HipGuardR", new Vector3(0.245f, 0.055f, 0f), new Vector3(0.11f, 0.24f, 0.29f),
                armor, Quaternion.Euler(0f, 0f, -9f));
            PrimitiveMeshes.SphereObject(_hips, "HipJointL", new Vector3(-0.17f, -0.02f, 0f), 0.15f, joint);
            PrimitiveMeshes.SphereObject(_hips, "HipJointR", new Vector3(0.17f, -0.02f, 0f), 0.15f, joint);
            PrimitiveMeshes.Box(_hips, "RearPelvis", new Vector3(0f, 0.06f, -0.15f), new Vector3(0.27f, 0.18f, 0.08f), armorDark);

            // Chest reads as a white composite shell over a graphite flight frame.
            _torso = Pivot("Torso", _hips, new Vector3(0f, 0.12f, 0f));
            PrimitiveMeshes.Create("Abdomen", PrimitiveMeshes.Capsule, suit, _torso,
                new Vector3(0f, 0.24f, 0f), new Vector3(0.32f, 0.25f, 0.27f));
            PrimitiveMeshes.Box(_torso, "AbdominalSeam", new Vector3(0f, 0.25f, 0.14f), new Vector3(0.23f, 0.028f, 0.012f), neon);
            PrimitiveMeshes.Create("ChestShell", PrimitiveMeshes.Capsule, suit, _torso,
                new Vector3(0f, 0.43f, 0f), new Vector3(0.45f, 0.34f, 0.32f));
            PrimitiveMeshes.Create("ChestPlate", PrimitiveMeshes.Capsule, armor, _torso,
                new Vector3(0f, 0.47f, 0.055f), new Vector3(0.43f, 0.3f, 0.26f));
            PrimitiveMeshes.Box(_torso, "ChestCenter", new Vector3(0f, 0.45f, 0.195f), new Vector3(0.31f, 0.18f, 0.07f), armor);
            PrimitiveMeshes.Box(_torso, "ChestSplit", new Vector3(0f, 0.45f, 0.235f), new Vector3(0.015f, 0.18f, 0.012f), neon);
            PrimitiveMeshes.Create("CoreRing", PrimitiveMeshes.Cylinder, neon, _torso,
                new Vector3(0f, 0.47f, 0.235f), new Vector3(0.135f, 0.012f, 0.135f), Quaternion.Euler(90f, 0f, 0f));
            PrimitiveMeshes.Create("Core", PrimitiveMeshes.Sphere, visor, _torso,
                new Vector3(0f, 0.47f, 0.245f), new Vector3(0.075f, 0.075f, 0.04f));
            PrimitiveMeshes.Box(_torso, "Collar", new Vector3(0f, 0.645f, 0f), new Vector3(0.27f, 0.07f, 0.22f), joint);
            PrimitiveMeshes.Box(_torso, "CollarGlowL", new Vector3(-0.105f, 0.645f, 0.115f), new Vector3(0.075f, 0.018f, 0.015f), neon);
            PrimitiveMeshes.Box(_torso, "CollarGlowR", new Vector3(0.105f, 0.645f, 0.115f), new Vector3(0.075f, 0.018f, 0.015f), neon);
            PrimitiveMeshes.Box(_torso, "ShoulderHarnessL", new Vector3(-0.205f, 0.57f, 0.13f), new Vector3(0.032f, 0.18f, 0.035f),
                armorDark, Quaternion.Euler(0f, 0f, -18f));
            PrimitiveMeshes.Box(_torso, "ShoulderHarnessR", new Vector3(0.205f, 0.57f, 0.13f), new Vector3(0.032f, 0.18f, 0.035f),
                armorDark, Quaternion.Euler(0f, 0f, 18f));
            PrimitiveMeshes.Box(_torso, "Backpack", new Vector3(0f, 0.42f, -0.205f), new Vector3(0.32f, 0.37f, 0.16f), armorDark);
            PrimitiveMeshes.Box(_torso, "Spine", new Vector3(0f, 0.42f, -0.295f), new Vector3(0.09f, 0.3f, 0.04f), chrome);
            for (int i = 0; i < 3; i++)
            {
                PrimitiveMeshes.Box(_torso, "SpineGlow" + i, new Vector3(0f, 0.33f + i * 0.09f, -0.318f),
                    new Vector3(0.055f, 0.018f, 0.012f), neon);
            }
            PrimitiveMeshes.Create("ThrusterL", PrimitiveMeshes.Cylinder, armor, _torso,
                new Vector3(-0.12f, 0.29f, -0.275f), new Vector3(0.09f, 0.13f, 0.09f));
            PrimitiveMeshes.Create("ThrusterR", PrimitiveMeshes.Cylinder, armor, _torso,
                new Vector3(0.12f, 0.29f, -0.275f), new Vector3(0.09f, 0.13f, 0.09f));
            PrimitiveMeshes.Create("ThrusterGlowL", PrimitiveMeshes.Cylinder, neon, _torso,
                new Vector3(-0.12f, 0.155f, -0.275f), new Vector3(0.066f, 0.012f, 0.066f));
            PrimitiveMeshes.Create("ThrusterGlowR", PrimitiveMeshes.Cylinder, neon, _torso,
                new Vector3(0.12f, 0.155f, -0.275f), new Vector3(0.066f, 0.012f, 0.066f));

            // Helmet: pearl shell, deep visor, chrome ear discs and a swept antenna fin.
            _head = Pivot("Head", _torso, new Vector3(0f, 0.71f, 0f));
            PrimitiveMeshes.Box(_head, "Neck", new Vector3(0f, -0.04f, 0f), new Vector3(0.12f, 0.13f, 0.12f), joint);
            PrimitiveMeshes.Create("HelmetShell", PrimitiveMeshes.Sphere, armor, _head,
                new Vector3(0f, 0.14f, 0f), new Vector3(0.36f, 0.33f, 0.35f));
            PrimitiveMeshes.Create("HelmetCrown", PrimitiveMeshes.Sphere, armorDark, _head,
                new Vector3(0f, 0.27f, -0.015f), new Vector3(0.3f, 0.13f, 0.3f));
            PrimitiveMeshes.Create("Visor", PrimitiveMeshes.Sphere, visor, _head,
                new Vector3(0f, 0.155f, 0.12f), new Vector3(0.29f, 0.135f, 0.16f));
            PrimitiveMeshes.Box(_head, "VisorStrip", new Vector3(0f, 0.16f, 0.205f), new Vector3(0.22f, 0.017f, 0.015f), neon);
            PrimitiveMeshes.Box(_head, "Brow", new Vector3(0f, 0.235f, 0.155f), new Vector3(0.28f, 0.045f, 0.06f), armor,
                Quaternion.Euler(-8f, 0f, 0f));
            PrimitiveMeshes.Box(_head, "Jaw", new Vector3(0f, 0.035f, 0.115f), new Vector3(0.2f, 0.085f, 0.14f), armorDark);
            PrimitiveMeshes.Box(_head, "Chin", new Vector3(0f, 0.0f, 0.16f), new Vector3(0.13f, 0.045f, 0.08f), chrome);
            PrimitiveMeshes.Create("EarL", PrimitiveMeshes.Cylinder, chrome, _head,
                new Vector3(-0.183f, 0.15f, 0f), new Vector3(0.095f, 0.02f, 0.095f), Quaternion.Euler(0f, 0f, 90f));
            PrimitiveMeshes.Create("EarR", PrimitiveMeshes.Cylinder, chrome, _head,
                new Vector3(0.183f, 0.15f, 0f), new Vector3(0.095f, 0.02f, 0.095f), Quaternion.Euler(0f, 0f, 90f));
            PrimitiveMeshes.Create("EarGlowL", PrimitiveMeshes.Cylinder, neon, _head,
                new Vector3(-0.197f, 0.15f, 0f), new Vector3(0.045f, 0.012f, 0.045f), Quaternion.Euler(0f, 0f, 90f));
            PrimitiveMeshes.Create("EarGlowR", PrimitiveMeshes.Cylinder, neon, _head,
                new Vector3(0.197f, 0.15f, 0f), new Vector3(0.045f, 0.012f, 0.045f), Quaternion.Euler(0f, 0f, 90f));
            PrimitiveMeshes.Box(_head, "HelmetFin", new Vector3(0f, 0.34f, -0.07f), new Vector3(0.028f, 0.13f, 0.2f), armorDark,
                Quaternion.Euler(18f, 0f, 0f));
            PrimitiveMeshes.Box(_head, "FinTip", new Vector3(0f, 0.4f, -0.125f), new Vector3(0.02f, 0.045f, 0.09f), neon,
                Quaternion.Euler(18f, 0f, 0f));

            // Shoulders, arms, forearms.
            _armL = BuildArm(_torso, new Vector3(-0.35f, 0.58f, 0f), suit, armor, armorDark, joint, chrome, neon, visor, -1f);
            _armR = BuildArm(_torso, new Vector3(0.35f, 0.58f, 0f), suit, armor, armorDark, joint, chrome, neon, visor, 1f);
            _forearmL = _armL.Find("Forearm");
            _forearmR = _armR.Find("Forearm");

            // Legs.
            _legL = BuildLeg(_hips, new Vector3(-0.17f, 0f, 0f), suit, armor, armorDark, joint, chrome, neon, dark);
            _legR = BuildLeg(_hips, new Vector3(0.17f, 0f, 0f), suit, armor, armorDark, joint, chrome, neon, dark);
            _shinL = _legL.Find("Shin");
            _shinR = _legR.Find("Shin");

            AddStyleDetails(character.armorStyle, _torso, _head, _hips, armor, armorDark, neon, visor);

            if (!remote)
            {
                var rimGo = new GameObject("Runner Rim Light");
                rimGo.transform.SetParent(_visual, false);
                rimGo.transform.localPosition = new Vector3(0.7f, 1.45f, -1.05f);
                var rim = rimGo.AddComponent<Light>();
                rim.type = LightType.Point;
                rim.color = Color.Lerp(Color.white, accent, 0.26f);
                rim.range = 4.2f;
                rim.intensity = 1.9f;
                rim.shadows = LightShadows.None;

                var fillGo = new GameObject("Runner Soft Fill");
                fillGo.transform.SetParent(_visual, false);
                fillGo.transform.localPosition = new Vector3(-0.9f, 1.9f, -0.55f);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Point;
                fill.color = new Color(0.72f, 0.86f, 1f);
                fill.range = 4.2f;
                fill.intensity = 0.9f;
                fill.shadows = LightShadows.None;
            }

            // Trail.
            var trailGo = new GameObject("EnergyTrail");
            trailGo.transform.SetParent(_visual, false);
            trailGo.transform.localPosition = new Vector3(0f, 0.85f, -0.15f);
            _trail = trailGo.AddComponent<TrailRenderer>();
            _trail.time = remote ? 0.24f : 0.36f;
            _trail.minVertexDistance = 0.24f;
            _trail.widthCurve = new AnimationCurve(new Keyframe(0f, remote ? 0.11f : 0.18f), new Keyframe(1f, 0f));
            _trail.material = trailMat;
            _trail.startColor = new Color(accent.r, accent.g, accent.b, remote ? 0.45f : 0.85f);
            _trail.endColor = new Color(accent.r, accent.g, accent.b, 0f);
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;

            // Hoverboard.
            _hoverboard = new GameObject("Hoverboard");
            _hoverboard.transform.SetParent(_visual, false);
            _hoverboard.transform.localPosition = new Vector3(0f, 0.17f, 0.03f);
            PrimitiveMeshes.Box(_hoverboard.transform, "Deck", Vector3.zero, new Vector3(1.0f, 0.07f, 0.42f), dark);
            PrimitiveMeshes.Box(_hoverboard.transform, "DeckGlow", new Vector3(0f, 0.05f, 0f), new Vector3(0.85f, 0.02f, 0.3f), neon);
            PrimitiveMeshes.Box(_hoverboard.transform, "Wake", new Vector3(0f, -0.02f, -0.26f), new Vector3(0.6f, 0.02f, 0.12f), neon);
            _hoverboard.SetActive(false);

            // Shield bubble.
            _shield = new GameObject("ShieldBubble");
            _shield.transform.SetParent(_visual, false);
            _shield.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            var bubble = PrimitiveMeshes.Create("Bubble", PrimitiveMeshes.Sphere, MaterialLibrary.Transparent(
                new Color(accent.r, accent.g, accent.b, 0.2f), 0.9f), _shield.transform, Vector3.zero, Vector3.one * 2.45f);
            bubble.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _shield.SetActive(false);

            // Power aura geometry.
            _aura = new GameObject("PowerAura");
            _aura.transform.SetParent(_visual, false);
            _aura.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            for (int i = 0; i < 3; i++)
            {
                var ring = PrimitiveMeshes.Create("AuraRing", PrimitiveMeshes.Cylinder,
                    MaterialLibrary.Additive(new Color(accent.r, accent.g, accent.b, 0.32f)),
                    _aura.transform, new Vector3(0f, -0.35f + i * 0.4f, 0f), new Vector3(1.25f, 0.008f, 1.25f),
                    Quaternion.Euler(0f, 0f, 8f + i * 25f));
                var r = ring.GetComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            _aura.SetActive(false);

            if (remote && !string.IsNullOrEmpty(displayName))
            {
                var tagGo = new GameObject("NameTag");
                tagGo.transform.SetParent(transform, false);
                tagGo.transform.localPosition = new Vector3(0f, 2.15f, 0f);
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
                textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                textRenderer.receiveShadows = false;
            }

            _built = true;
            _dead = false;
            _victory = false;
            SetPose(RunnerPose.Idle);
        }

        private static Transform Pivot(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        private static Transform BuildArm(Transform parent, Vector3 shoulder, Material suit, Material armor,
            Material armorDark, Material joint, Material chrome, Material neon, Material visor, float side)
        {
            var pivot = Pivot(side < 0 ? "ArmL" : "ArmR", parent, shoulder);
            PrimitiveMeshes.SphereObject(pivot, "ShoulderJoint", Vector3.zero, 0.17f, joint);
            PrimitiveMeshes.Create("ShoulderPad", PrimitiveMeshes.Sphere, armor, pivot,
                new Vector3(side * 0.035f, 0.035f, 0f), new Vector3(0.26f, 0.2f, 0.28f));
            PrimitiveMeshes.Box(pivot, "ShoulderBlade", new Vector3(side * 0.115f, 0.02f, 0.01f),
                new Vector3(0.055f, 0.12f, 0.22f), neon, Quaternion.Euler(0f, 0f, side * 16f));
            PrimitiveMeshes.Box(pivot, "ShoulderFrame", new Vector3(side * 0.02f, -0.03f, -0.11f),
                new Vector3(0.2f, 0.13f, 0.055f), armorDark, Quaternion.Euler(0f, 0f, side * 10f));
            PrimitiveMeshes.Create("UpperArm", PrimitiveMeshes.Capsule, suit, pivot,
                new Vector3(0f, -0.19f, 0f), new Vector3(0.135f, 0.2f, 0.135f));
            PrimitiveMeshes.Box(pivot, "BicepPlate", new Vector3(0f, -0.16f, 0.065f),
                new Vector3(0.11f, 0.14f, 0.05f), armor);
            var forearm = Pivot("Forearm", pivot, new Vector3(0f, -0.37f, 0f));
            PrimitiveMeshes.SphereObject(forearm, "Elbow", Vector3.zero, 0.14f, joint);
            PrimitiveMeshes.Create("Forearm", PrimitiveMeshes.Capsule, armor, forearm,
                new Vector3(0f, -0.16f, 0f), new Vector3(0.145f, 0.17f, 0.145f));
            PrimitiveMeshes.Box(forearm, "Gauntlet", new Vector3(0f, -0.17f, 0.075f),
                new Vector3(0.115f, 0.2f, 0.055f), armorDark);
            PrimitiveMeshes.Box(forearm, "WristGlow", new Vector3(0f, -0.295f, 0f),
                new Vector3(0.14f, 0.022f, 0.14f), neon);
            PrimitiveMeshes.SphereObject(forearm, "Hand", new Vector3(0f, -0.36f, 0.015f), 0.115f, joint);
            PrimitiveMeshes.Box(forearm, "Knuckles", new Vector3(0f, -0.37f, 0.062f),
                new Vector3(0.095f, 0.045f, 0.035f), chrome);
            return pivot;
        }

        private static Transform BuildLeg(Transform parent, Vector3 hip, Material suit, Material armor,
            Material armorDark, Material joint, Material chrome, Material neon, Material dark)
        {
            var pivot = Pivot("Leg", parent, hip);
            PrimitiveMeshes.SphereObject(pivot, "HipJoint", Vector3.zero, 0.16f, joint);
            PrimitiveMeshes.Create("Thigh", PrimitiveMeshes.Capsule, suit, pivot,
                new Vector3(0f, -0.2f, 0f), new Vector3(0.17f, 0.23f, 0.17f));
            PrimitiveMeshes.Box(pivot, "ThighPlate", new Vector3(0f, -0.18f, 0.075f),
                new Vector3(0.17f, 0.24f, 0.075f), armor);
            PrimitiveMeshes.Box(pivot, "ThighSeam", new Vector3(0f, -0.15f, 0.116f),
                new Vector3(0.1f, 0.018f, 0.012f), neon);
            var shin = Pivot("Shin", pivot, new Vector3(0f, -0.4f, 0f));
            PrimitiveMeshes.SphereObject(shin, "KneeJoint", Vector3.zero, 0.16f, joint);
            PrimitiveMeshes.Create("KneeCap", PrimitiveMeshes.Cylinder, armor, shin,
                new Vector3(0f, 0f, 0.055f), new Vector3(0.11f, 0.022f, 0.11f), Quaternion.Euler(90f, 0f, 0f));
            PrimitiveMeshes.Create("KneeGlow", PrimitiveMeshes.Cylinder, neon, shin,
                new Vector3(0f, 0f, 0.071f), new Vector3(0.055f, 0.012f, 0.055f), Quaternion.Euler(90f, 0f, 0f));
            PrimitiveMeshes.Create("Shin", PrimitiveMeshes.Capsule, armor, shin,
                new Vector3(0f, -0.18f, 0f), new Vector3(0.145f, 0.19f, 0.145f));
            PrimitiveMeshes.Box(shin, "ShinGuard", new Vector3(0f, -0.16f, 0.07f),
                new Vector3(0.115f, 0.23f, 0.055f), armorDark);
            PrimitiveMeshes.Box(shin, "ShinGlow", new Vector3(0f, -0.17f, 0.102f),
                new Vector3(0.045f, 0.16f, 0.012f), neon);
            PrimitiveMeshes.SphereObject(shin, "Ankle", new Vector3(0f, -0.38f, 0f), 0.12f, joint);
            PrimitiveMeshes.Box(shin, "Boot", new Vector3(0f, -0.44f, 0.065f),
                new Vector3(0.215f, 0.13f, 0.35f), dark);
            PrimitiveMeshes.Box(shin, "ToeCap", new Vector3(0f, -0.435f, 0.225f),
                new Vector3(0.205f, 0.1f, 0.15f), armorDark);
            PrimitiveMeshes.Box(shin, "Heel", new Vector3(0f, -0.45f, -0.085f),
                new Vector3(0.16f, 0.1f, 0.1f), chrome);
            PrimitiveMeshes.Box(shin, "SoleGlow", new Vector3(0f, -0.508f, 0.075f),
                new Vector3(0.18f, 0.014f, 0.3f), neon);
            return pivot;
        }

        private void AddStyleDetails(int style, Transform torso, Transform head, Transform hips,
            Material armor, Material armorDark, Material neon, Material visor)
        {
            if (style == 0)
            {
                PrimitiveMeshes.Box(torso, "SpineGlow", new Vector3(0f, 0.3f, -0.31f), new Vector3(0.05f, 0.4f, 0.02f), neon);
            }
            else if (style == 1)
            {
                PrimitiveMeshes.Box(torso, "VoltFinL", new Vector3(-0.4f, 0.52f, -0.1f), new Vector3(0.05f, 0.28f, 0.3f), neon,
                    Quaternion.Euler(0f, 0f, -24f));
                PrimitiveMeshes.Box(torso, "VoltFinR", new Vector3(0.4f, 0.52f, -0.1f), new Vector3(0.05f, 0.28f, 0.3f), neon,
                    Quaternion.Euler(0f, 0f, 24f));
                PrimitiveMeshes.Box(head, "VisorStripe", new Vector3(0f, 0.16f, 0.18f), new Vector3(0.32f, 0.025f, 0.02f), neon);
            }
            else if (style == 2)
            {
                PrimitiveMeshes.Box(head, "Hood", new Vector3(0f, 0.23f, -0.11f), new Vector3(0.4f, 0.24f, 0.24f), armor);
                PrimitiveMeshes.Box(head, "Antenna", new Vector3(0.13f, 0.43f, -0.1f), new Vector3(0.018f, 0.3f, 0.018f), neon,
                    Quaternion.Euler(0f, 0f, -12f));
                PrimitiveMeshes.Box(torso, "ChestSplit", new Vector3(0f, 0.42f, 0.2f), new Vector3(0.02f, 0.3f, 0.02f), neon);
            }
            else if (style == 3)
            {
                PrimitiveMeshes.Box(torso, "HaulerPadL", new Vector3(-0.48f, 0.57f, 0f), new Vector3(0.3f, 0.16f, 0.34f), armor,
                    Quaternion.Euler(0f, 0f, -12f));
                PrimitiveMeshes.Box(torso, "HaulerPadR", new Vector3(0.48f, 0.57f, 0f), new Vector3(0.3f, 0.16f, 0.34f), armor,
                    Quaternion.Euler(0f, 0f, 12f));
                PrimitiveMeshes.Box(torso, "Tank", new Vector3(0f, 0.35f, -0.4f), new Vector3(0.24f, 0.5f, 0.16f), neon);
                PrimitiveMeshes.Box(head, "Brow", new Vector3(0f, 0.27f, 0.14f), new Vector3(0.3f, 0.04f, 0.04f), neon);
            }
            else if (style == 4)
            {
                PrimitiveMeshes.Box(torso, "ProtoCore", new Vector3(0f, 0.46f, 0.21f), new Vector3(0.16f, 0.16f, 0.02f), neon,
                    Quaternion.Euler(0f, 0f, 45f));
                PrimitiveMeshes.Box(head, "Halo", new Vector3(0f, 0.42f, 0f), new Vector3(0.5f, 0.025f, 0.5f), neon,
                    Quaternion.Euler(12f, 0f, 0f));
                for (int i = 0; i < 3; i++)
                {
                    PrimitiveMeshes.Box(hips, "DataSpike", new Vector3(-0.18f + i * 0.18f, 0.02f, -0.18f),
                        new Vector3(0.045f, 0.2f, 0.045f), neon, Quaternion.Euler(0f, 0f, 12f));
                }
            }
        }

        public void Tick(float dt, float speedNormalised, float lateralVelocity, RunnerPose pose)
        {
            if (!_built || _visual == null) return;
            if (_pose != pose) _pose = pose;
            if (_pose == RunnerPose.Dead) _dead = true;
            if (_pose == RunnerPose.Victory) _victory = true;
            _lateralLean = Mathf.Lerp(_lateralLean, Mathf.Clamp(-lateralVelocity * 0.055f, -12f, 12f), dt * 9f);
            float runSpeed = 5.5f + speedNormalised * 7.5f;
            _phase += dt * runSpeed * Mathf.PI;
            float s = Mathf.Sin(_phase);
            float c = Mathf.Cos(_phase);
            _bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.028f;

            Vector3 hipPos = new Vector3(0f, 0.94f + _bob, 0f);
            float runLean = 7f + speedNormalised * 8f;
            Quaternion torsoRot = Quaternion.Euler(runLean, 0f, _lateralLean);
            Quaternion headRot = Quaternion.Euler(-runLean * 0.34f, _lateralLean * 0.25f, 0f);
            float legLX = s * 55f, legRX = -s * 55f;
            float kneeLX = Mathf.Max(0f, -s) * 58f, kneeRX = Mathf.Max(0f, s) * 58f;
            float armLX = -s * 52f, armRX = s * 52f;
            float foreLX = -18f - Mathf.Max(0f, s) * 26f, foreRX = -18f - Mathf.Max(0f, -s) * 26f;
            Vector3 visualPos = Vector3.zero;
            Quaternion visualRot = Quaternion.identity;

            if (_pose == RunnerPose.Idle)
            {
                legLX = legRX = 0f;
                kneeLX = kneeRX = 6f;
                armLX = -8f; armRX = -8f;
                foreLX = foreRX = -10f;
                torsoRot = Quaternion.Euler(2f, Mathf.Sin(Time.unscaledTime * 0.8f) * 4f, _lateralLean * 0.4f);
                _bob = Mathf.Sin(Time.unscaledTime * 1.8f) * 0.01f;
                hipPos = new Vector3(0f, 0.94f + _bob, 0f);
            }
            else if (_pose == RunnerPose.Jump)
            {
                legLX = 38f; legRX = -18f;
                kneeLX = 62f; kneeRX = 28f;
                armLX = -72f; armRX = -35f;
                foreLX = -35f; foreRX = -28f;
                torsoRot = Quaternion.Euler(14f, 0f, _lateralLean);
            }
            else if (_pose == RunnerPose.Slide)
            {
                visualPos = new Vector3(0f, -0.36f, 0f);
                hipPos = new Vector3(0f, 0.56f, 0f);
                torsoRot = Quaternion.Euler(62f, 0f, _lateralLean * 1.3f);
                headRot = Quaternion.Euler(-48f, 0f, 0f);
                legLX = 70f; legRX = 54f;
                kneeLX = -32f; kneeRX = -48f;
                armLX = 42f; armRX = 58f;
                foreLX = -18f; foreRX = -12f;
            }
            else if (_pose == RunnerPose.Dead)
            {
                _deathSpin += dt * 240f;
                visualRot = Quaternion.Euler(68f, 0f, Mathf.Sin(_deathSpin * Mathf.Deg2Rad) * 16f);
                visualPos = new Vector3(0f, -0.2f, 0f);
                legLX = -25f; legRX = 38f;
                kneeLX = 25f; kneeRX = 60f;
                armLX = 110f; armRX = 80f;
                torsoRot = Quaternion.Euler(20f, 0f, 12f);
            }
            else if (_pose == RunnerPose.Victory)
            {
                visualPos = new Vector3(0f, Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)) * 0.08f, 0f);
                armLX = -158f; armRX = -158f;
                foreLX = -20f; foreRX = -20f;
                legLX = 12f; legRX = -12f;
                kneeLX = 8f; kneeRX = 8f;
                torsoRot = Quaternion.Euler(-6f, 0f, 0f);
                headRot = Quaternion.Euler(-8f, 0f, 0f);
            }

            float k = 1f - Mathf.Exp(-dt * 14f);
            _visual.localPosition = Vector3.Lerp(_visual.localPosition, visualPos, k);
            _visual.localRotation = Quaternion.Slerp(_visual.localRotation, visualRot, k);
            _hips.localPosition = Vector3.Lerp(_hips.localPosition, hipPos, k);
            _torso.localRotation = Quaternion.Slerp(_torso.localRotation, torsoRot, k);
            _head.localRotation = Quaternion.Slerp(_head.localRotation, headRot, k);
            _legL.localRotation = Quaternion.Slerp(_legL.localRotation, Quaternion.Euler(legLX, 0f, 0f), k);
            _legR.localRotation = Quaternion.Slerp(_legR.localRotation, Quaternion.Euler(legRX, 0f, 0f), k);
            _shinL.localRotation = Quaternion.Slerp(_shinL.localRotation, Quaternion.Euler(kneeLX, 0f, 0f), k);
            _shinR.localRotation = Quaternion.Slerp(_shinR.localRotation, Quaternion.Euler(kneeRX, 0f, 0f), k);
            _armL.localRotation = Quaternion.Slerp(_armL.localRotation, Quaternion.Euler(armLX, 0f, -8f), k);
            _armR.localRotation = Quaternion.Slerp(_armR.localRotation, Quaternion.Euler(armRX, 0f, 8f), k);
            _forearmL.localRotation = Quaternion.Slerp(_forearmL.localRotation, Quaternion.Euler(foreLX, 0f, 0f), k);
            _forearmR.localRotation = Quaternion.Slerp(_forearmR.localRotation, Quaternion.Euler(foreRX, 0f, 0f), k);

            if (_aura != null && _aura.activeSelf)
                _aura.transform.Rotate(Vector3.up, dt * 120f, Space.Self);

            if (_nameTag != null)
            {
                var cam = Camera.main;
                if (cam != null)
                    _nameTag.transform.rotation = Quaternion.LookRotation(_nameTag.transform.position - cam.transform.position, Vector3.up);
            }
        }

        public void SetPose(RunnerPose pose)
        {
            _pose = pose;
            _dead = pose == RunnerPose.Dead;
            _victory = pose == RunnerPose.Victory;
            if (pose == RunnerPose.Dead) _deathSpin = 0f;
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
            _built = false;
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
