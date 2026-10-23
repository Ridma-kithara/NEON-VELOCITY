using System;
using NeonVelocity.Core;
using NeonVelocity.Player;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>Cosmetics and permanent upgrades. All purchases are local and persist in SaveData.</summary>
    public sealed class ShopScreen : MonoBehaviour
    {
        private GameApp _app;
        private UIRoot _ui;
        private int _tab;
        private bool _purchaseInFlight;

        private static readonly string[] UpgradeIds = { "magnet", "shield", "multiplier", "boost", "head_start" };
        private static readonly string[] UpgradeNames = { "MAGNET CORE", "AEGIS PLATING", "SCORE ENGINE", "BOOST VECTOR", "HEAD START" };
        private static readonly string[] UpgradeDescriptions =
        {
            "Widens credit attraction by 16% per level.",
            "Increases shield strength and pickup duration by 18% per level.",
            "Raises the permanent score multiplier by 3% per level.",
            "Improves boost power and duration by 10% per level.",
            "Starts every run further down the track: +55 m per level."
        };

        public static GameObject Build(GameApp app, UIRoot ui, int tab)
        {
            var root = ScreenKit.Root("Shop");
            var screen = root.gameObject.AddComponent<ShopScreen>();
            screen._app = app;
            screen._ui = ui;
            screen._tab = Mathf.Clamp(tab, 0, 2);
            screen.BuildLayout();
            return root.gameObject;
        }

        private void BuildLayout()
        {
            var root = (RectTransform)transform;
            ScreenKit.Backdrop(root, UITheme.Magenta);
            ScreenKit.Header(root, _app, "LOADOUT", "RUNNERS & GEAR", "Original cosmetics and permanent run upgrades.",
                UITheme.Magenta, _app.NavigateBack);

            BuildTabs(root);

            UITheme.Scroll(root, "Shop Scroll", out RectTransform content);
            var scrollRect = root.Find("Shop Scroll");
            UITheme.Anchor((RectTransform)scrollRect, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(36f, 42f), new Vector2(-36f, -222f));

            switch (_tab)
            {
                case 0: BuildCharacters(content); break;
                case 1: BuildTrails(content); break;
                default: BuildUpgrades(content); break;
            }
        }

        private void BuildTabs(RectTransform root)
        {
            var bar = UITheme.Img(root, "Tabs", new Color(0.02f, 0.04f, 0.075f, 0.4f));
            UITheme.Anchor((RectTransform)bar.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(36f, -216f), new Vector2(-36f, -158f));

            string[] labels = { "RUNNERS", "TRAILS", "UPGRADES" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var button = UITheme.Button(bar.transform, labels[i], labels[i],
                    i == _tab ? UITheme.Magenta : UITheme.Muted,
                    () => { _app.Audio.PlayUiClick(); _ui.ShowScreen(Build(_app, _ui, index)); },
                    UITheme.BodySize, i == _tab);
                UITheme.Anchor((RectTransform)button.transform, new Vector2(i / 3f, 0f), new Vector2((i + 1) / 3f, 1f),
                    new Vector2(6f, 8f), new Vector2(-6f, -8f));
            }

            // Currency is already present in the persistent header. Repeating it on the tab bar
            // made purchases harder to read and wasted the widest part of the row.
        }

        private void BuildCharacters(RectTransform content)
        {
            var profile = _app.Profile;
            BuildGarage(content, profile);
            var characters = _app.Config.characters;
            for (int i = 0; i < characters.Count; i++)
            {
                var character = characters[i];
                bool unlocked = profile.unlockedCharacters.Contains(character.id);
                bool selected = profile.selectedCharacter == character.id;
                var card = ScreenKit.Card(content, "Runner " + character.id, new Color(0.035f, 0.065f, 0.12f, 0.94f));
                UITheme.Layout(card.gameObject, 156f, -1f, 156f);

                var swatch = UITheme.Img(card.transform, "Swatch", character.primary, UITheme.Rounded, false);
                UITheme.Anchor((RectTransform)swatch.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(26f, -48f), new Vector2(122f, 48f));
                var swatchInner = UITheme.Img(swatch.transform, "Inner", character.secondary, TextureFactory.Diamond, false);
                UITheme.Anchor((RectTransform)swatchInner.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(-28f, -28f), new Vector2(28f, 28f));

                var name = UITheme.Label(card.transform, "Name", character.displayName, UITheme.HeadingSize,
                    UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(144f, -54f), new Vector2(-540f, -16f));

                var callsign = UITheme.Label(card.transform, "Callsign",
                    character.callsign + "  \u00B7  " + character.perkLabel, UITheme.SmallSize,
                    character.primary, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)callsign.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(146f, -86f), new Vector2(-540f, -56f));

                var bio = UITheme.Label(card.transform, "Bio", character.bio, UITheme.SmallSize, UITheme.Dim,
                    TextAnchor.UpperLeft);
                UITheme.Anchor((RectTransform)bio.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                    new Vector2(146f, 18f), new Vector2(-540f, -90f));

                string status = selected ? "EQUIPPED"
                    : unlocked ? "OWNED"
                    : profile.level < character.unlockLevel ? "LVL " + character.unlockLevel
                    : "\u25C6 " + UITheme.FormatNumber(character.unlockCost);
                var tag = UITheme.Badge(card.transform, "Status", status,
                    selected ? UITheme.Lime : unlocked ? UITheme.Cyan : UITheme.Amber, 150, 30);
                UITheme.Anchor((RectTransform)tag.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-360f, -52f), new Vector2(-210f, -18f));

                if (selected)
                {
                    var equipped = UITheme.Button(card.transform, "Equipped", "EQUIPPED", UITheme.Lime,
                        null, UITheme.BodySize, false);
                    equipped.interactable = false;
                    UITheme.Anchor((RectTransform)equipped.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                        new Vector2(-350f, 26f), new Vector2(-24f, 82f));
                }
                else if (unlocked)
                {
                    string id = character.id;
                    var select = UITheme.Button(card.transform, "Select", "EQUIP", UITheme.Cyan,
                        () => { _app.Progression.SelectCharacter(id); _app.SaveProfile(); Reload(); }, UITheme.BodySize);
                    UITheme.Anchor((RectTransform)select.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                        new Vector2(-350f, 26f), new Vector2(-24f, 82f));
                }
                else
                {
                    string id = character.id;
                    var unlock = UITheme.Button(card.transform, "Unlock", "UNLOCK", UITheme.Magenta,
                        () => RequestCharacter(id), UITheme.BodySize);
                    UITheme.Anchor((RectTransform)unlock.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                        new Vector2(-350f, 26f), new Vector2(-24f, 82f));
                }
            }
        }

        private void BuildGarage(RectTransform content, SaveData profile)
        {
            var character = _app.Config.Character(profile.selectedCharacter);
            if (character == null) return;
            var trail = _app.Config.Trail(profile.selectedTrail);

            var card = ScreenKit.Card(content, "Garage Preview", new Color(0.025f, 0.05f, 0.095f, 0.96f));
            UITheme.Layout(card.gameObject, 392f, -1f, 392f);

            var frame = UITheme.Img(card.transform, "Preview Frame", new Color(0.006f, 0.014f, 0.03f, 1f),
                UITheme.Rounded, false);
            UITheme.Anchor((RectTransform)frame.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(22f, -174f), new Vector2(366f, 170f));

            var rawGo = new GameObject("Garage Render", typeof(RectTransform));
            rawGo.transform.SetParent(frame.transform, false);
            var raw = rawGo.AddComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = true;
            UITheme.Stretch((RectTransform)raw.transform, 6f, 6f, 6f, 6f);
            var garage = rawGo.AddComponent<GaragePreview>();
            garage.Initialise(_app, character, trail, raw);

            var hint = UITheme.Label(card.transform, "Garage Hint", "DRAG TO INSPECT",
                UITheme.SmallSize, UITheme.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)hint.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(398f, -326f), new Vector2(-30f, -292f));

            var eyebrow = UITheme.Label(card.transform, "Garage Eyebrow", "GARAGE  //  360\u00B0 TURNTABLE",
                UITheme.SmallSize, UITheme.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)eyebrow.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(398f, -66f), new Vector2(-30f, -28f));

            var name = UITheme.Label(card.transform, "Garage Name", character.displayName, 44,
                UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(398f, -128f), new Vector2(-30f, -70f));

            var callsign = UITheme.Label(card.transform, "Garage Callsign",
                character.callsign + "  \u00B7  " + character.perkLabel, UITheme.BodySize,
                character.primary, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)callsign.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(400f, -164f), new Vector2(-30f, -130f));

            var bio = UITheme.Label(card.transform, "Garage Bio", character.bio, UITheme.SmallSize,
                UITheme.Dim, TextAnchor.UpperLeft);
            UITheme.Anchor((RectTransform)bio.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(400f, -242f), new Vector2(-30f, -172f));

            var trailLabel = UITheme.Label(card.transform, "Garage Trail",
                "TRAIL  " + (trail != null ? trail.displayName.ToUpperInvariant() : "STANDARD") +
                "     //     STYLE  " + (character.armorStyle + 1).ToString("00"),
                UITheme.SmallSize, UITheme.Amber, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)trailLabel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(400f, -270f), new Vector2(-30f, -242f));

            var badge = UITheme.Badge(card.transform, "Garage Active", "ACTIVE LOADOUT", UITheme.Lime, 190, 30);
            UITheme.Anchor((RectTransform)badge.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-220f, -62f), new Vector2(-30f, -32f));

            string[] poseLabels = { "IDLE", "RUN", "JUMP" };
            RunnerPose[] poses = { RunnerPose.Idle, RunnerPose.Run, RunnerPose.Jump };
            for (int i = 0; i < poses.Length; i++)
            {
                RunnerPose pose = poses[i];
                var button = UITheme.Button(card.transform, "Pose " + poseLabels[i], poseLabels[i],
                    i == 0 ? UITheme.Lime : UITheme.Muted, () => garage.SetPose(pose), UITheme.SmallSize, i == 0);
                UITheme.Anchor((RectTransform)button.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-420f + i * 130f, 24f), new Vector2(-300f + i * 130f, 72f));
            }
        }

        private void RequestCharacter(string id)
        {
            _app.Audio.PlayUiClick();
            var character = _app.Config.Character(id);
            if (character == null) return;
            if (_app.Profile.unlockedCharacters.Contains(id)) return;
            if (_app.Profile.level < character.unlockLevel)
            {
                _app.UI.Toast("REACH LEVEL " + character.unlockLevel, UITheme.Red, 2.5f);
                return;
            }
            ConfirmPurchase(character.displayName, character.unlockCost, () => UnlockCharacter(id));
        }

        private void UnlockCharacter(string id)
        {
            bool ok = _app.Progression.UnlockCharacter(id, out string message);
            _app.UI.Toast(message, ok ? UITheme.Lime : UITheme.Red, 2.5f);
            if (ok)
            {
                _app.Progression.SelectCharacter(id);
                _app.SaveProfile();
            }
            Reload();
        }

        private void BuildTrails(RectTransform content)
        {
            var profile = _app.Profile;
            var trails = _app.Config.trails;
            for (int i = 0; i < trails.Count; i++)
            {
                var trail = trails[i];
                bool unlocked = profile.unlockedTrails.Contains(trail.id);
                bool selected = profile.selectedTrail == trail.id;
                var card = ScreenKit.Card(content, "Trail " + trail.id, new Color(0.035f, 0.065f, 0.12f, 0.94f));
                UITheme.Layout(card.gameObject, 126f, -1f, 126f);

                var bar = UITheme.Img(card.transform, "Trail", trail.color, UITheme.Rounded, false);
                UITheme.Anchor((RectTransform)bar.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(26f, -32f), new Vector2(176f, 32f));
                var shine = UITheme.Img(bar.transform, "Shine", Color.white, UITheme.Glow, false);
                UITheme.Anchor((RectTransform)shine.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                    new Vector2(6f, 8f), new Vector2(-6f, -8f));

                var name = UITheme.Label(card.transform, "Name", trail.displayName, UITheme.HeadingSize,
                    UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(196f, -52f), new Vector2(-420f, -16f));

                var desc = UITheme.Label(card.transform, "Desc", trail.description, UITheme.SmallSize,
                    UITheme.Dim, TextAnchor.MiddleLeft);
                UITheme.Anchor((RectTransform)desc.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(196f, 20f), new Vector2(-420f, 52f));

                if (selected)
                {
                    var tag = UITheme.Badge(card.transform, "Status", "EQUIPPED", UITheme.Lime, 150, 30);
                    UITheme.Anchor((RectTransform)tag.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                        new Vector2(-190f, -15f), new Vector2(-40f, 15f));
                }
                else
                {
                    string id = trail.id;
                    var button = UITheme.Button(card.transform, "Action", unlocked ? "EQUIP" : "\u25C6 " +
                            UITheme.FormatNumber(trail.unlockCost), unlocked ? UITheme.Cyan : UITheme.Magenta,
                        () => SelectTrail(id), UITheme.BodySize);
                    UITheme.Anchor((RectTransform)button.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                        new Vector2(-250f, -28f), new Vector2(-26f, 28f));
                }
            }
        }

        private void SelectTrail(string id)
        {
            _app.Audio.PlayUiClick();
            if (!_app.Profile.unlockedTrails.Contains(id))
            {
                var trail = _app.Config.Trail(id);
                if (trail == null) return;
                ConfirmPurchase(trail.displayName, trail.unlockCost, () => UnlockTrail(id));
                return;
            }
            _app.Progression.SelectTrail(id);
            _app.SaveProfile();
            Reload();
        }

        private void UnlockTrail(string id)
        {
            bool ok = _app.Progression.UnlockTrail(id, out string message);
            _app.UI.Toast(message, ok ? UITheme.Lime : UITheme.Red, 2.5f);
            if (!ok) { Reload(); return; }
            _app.Progression.SelectTrail(id);
            _app.SaveProfile();
            Reload();
        }

        private void BuildUpgrades(RectTransform content)
        {
            var profile = _app.Profile;
            for (int i = 0; i < UpgradeIds.Length; i++)
            {
                string id = UpgradeIds[i];
                int level = profile.GetUpgradeLevel(id);
                bool max = level >= ProgressionMath.MaxUpgradeLevel;
                int cost = ProgressionMath.UpgradeCost(id, level + 1);
                var card = ScreenKit.Card(content, "Upgrade " + id, new Color(0.035f, 0.065f, 0.12f, 0.94f));
                UITheme.Layout(card.gameObject, 136f, -1f, 136f);

                var icon = UITheme.Img(card.transform, "Icon", UITheme.Cyan, TextureFactory.Diamond, false);
                UITheme.Anchor((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(26f, -28f), new Vector2(82f, 28f));

                var name = UITheme.Label(card.transform, "Name", UpgradeNames[i], UITheme.HeadingSize,
                    UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(100f, -50f), new Vector2(-360f, -16f));

                var desc = UITheme.Label(card.transform, "Desc", UpgradeDescriptions[i], UITheme.SmallSize,
                    UITheme.Dim, TextAnchor.MiddleLeft);
                UITheme.Anchor((RectTransform)desc.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                    new Vector2(100f, 22f), new Vector2(-460f, -54f));

                for (int p = 0; p < ProgressionMath.MaxUpgradeLevel; p++)
                {
                    var pip = UITheme.Img(card.transform, "Pip " + p,
                        p < level ? UITheme.Cyan : new Color(0.15f, 0.2f, 0.3f, 0.9f), UITheme.Rounded, false);
                    UITheme.Anchor((RectTransform)pip.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                        new Vector2(100f + p * 30f, 46f), new Vector2(122f + p * 30f, 58f));
                }

                var value = UITheme.Label(card.transform, "Value",
                    "LEVEL " + level + " / " + ProgressionMath.MaxUpgradeLevel +
                    "   x" + ProgressionMath.UpgradeValue(id, level).ToString("0.00"),
                    UITheme.SmallSize, UITheme.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)value.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(100f, 12f), new Vector2(-360f, 40f));

                var button = UITheme.Button(card.transform, "Buy", max ? "MAXED" : "\u25C6 " + UITheme.FormatNumber(cost),
                    max ? UITheme.Muted : UITheme.Amber, () => RequestUpgrade(id), UITheme.BodySize);
                button.interactable = !max;
                UITheme.Anchor((RectTransform)button.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-290f, -28f), new Vector2(-26f, 28f));
            }
        }

        private void RequestUpgrade(string id)
        {
            _app.Audio.PlayUiClick();
            int level = _app.Profile.GetUpgradeLevel(id);
            if (level >= ProgressionMath.MaxUpgradeLevel) return;
            int cost = ProgressionMath.UpgradeCost(id, level + 1);
            string label = "UPGRADE";
            for (int i = 0; i < UpgradeIds.Length; i++)
                if (UpgradeIds[i] == id) { label = UpgradeNames[i]; break; }
            ConfirmPurchase(label, cost, () => BuyUpgrade(id));
        }

        private void BuyUpgrade(string id)
        {
            bool ok = _app.Progression.BuyUpgrade(id, out string message);
            _app.UI.Toast(message, ok ? UITheme.Lime : UITheme.Red, 2.5f);
            Reload();
        }

        private void ConfirmPurchase(string itemName, int cost, Action onConfirm)
        {
            if (_purchaseInFlight || _app.UI.HasModal) return;
            if (_app.Profile.credits < cost)
            {
                _app.UI.Toast("INSUFFICIENT CREDITS", UITheme.Red, 2.5f);
                return;
            }
            string message = itemName + "\n\nCOST  \u25C6 " + UITheme.FormatNumber(cost) +
                "     BALANCE  \u25C6 " + UITheme.FormatNumber(_app.Profile.credits) +
                "\n\nThis purchase is permanent and cannot be refunded.";
            _app.UI.ShowModal("CONFIRM PURCHASE", message, "PURCHASE", () =>
            {
                if (_purchaseInFlight) return;
                _purchaseInFlight = true;
                try { onConfirm?.Invoke(); }
                finally { _purchaseInFlight = false; }
            }, "CANCEL");
        }

        private void Reload()
        {
            _app.UI.ShowScreen(Build(_app, _ui, _tab));
        }
    }
}
