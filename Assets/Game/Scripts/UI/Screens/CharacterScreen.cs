using System;
using NeonVelocity.Core;
using NeonVelocity.Player;
using NeonVelocity.Premium;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Runner roster screen. A large live turntable renders the selected character through the
    /// GaragePreview stages, the right column lists every runner (owned locally, or premium
    /// frames entitled by the server wallet), and pose buttons drive the full animation range
    /// including the sprint blend, jump, somersault, slide, victory and the kneel-rise revive.
    /// </summary>
    public sealed class CharacterScreen : MonoBehaviour
    {
        public static GameObject Build(GameApp app, UIRoot ui)
        {
            var root = ScreenKit.Root("Character");
            var screen = root.gameObject.AddComponent<CharacterScreen>();
            screen._app = app;
            screen._ui = ui;
            screen.BuildLayout();
            return root.gameObject;
        }

        private GameApp _app;
        private UIRoot _ui;

        private void BuildLayout()
        {
            var root = (RectTransform)transform;
            ScreenKit.Backdrop(root, UITheme.Magenta);
            ScreenKit.Header(root, _app, "GARAGE", "CHARACTER",
                "Choose your runner. Every suit is machined, not painted.", UITheme.Magenta, _app.NavigateBack);

            BuildGarage(root);
            BuildRoster(root);
        }

        private void BuildGarage(RectTransform root)
        {
            var profile = _app.Profile;
            var character = _app.Config.Character(profile.selectedCharacter);
            if (character == null) return;
            var trail = _app.Config.Trail(profile.selectedTrail);

            var border = UITheme.GlassCard(root, "Garage Card", new Color(0.025f, 0.05f, 0.095f, 0.96f));
            var card = (RectTransform)border.transform.parent;
            UITheme.Anchor(card, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(36f, 44f), new Vector2(604f, -168f));

            var frame = UITheme.Img(card.transform, "Preview Frame", new Color(0.006f, 0.014f, 0.03f, 1f),
                UITheme.Rounded, false);
            UITheme.Anchor((RectTransform)frame.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-284f, -622f), new Vector2(284f, -66f));

            var rawGo = new GameObject("Garage Render", typeof(RectTransform));
            rawGo.transform.SetParent(frame.transform, false);
            var raw = rawGo.AddComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = true;
            UITheme.Stretch((RectTransform)raw.transform, 5f, 5f, 5f, 5f);
            var garage = rawGo.AddComponent<GaragePreview>();
            garage.Initialise(_app, character, trail, raw);

            var dragHint = UITheme.Label(frame.transform, "Drag Hint", "\u25C6 DRAG TO INSPECT  \u00B7  360\u00B0",
                UITheme.SmallSize, UITheme.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)dragHint.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-200f, 16f), new Vector2(200f, 42f));

            // Name plate under the turntable.
            var name = UITheme.Label(card.transform, "Name", character.displayName, 34,
                UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(28f, 214f), new Vector2(-28f, 258f));

            var callsign = UITheme.Label(card.transform, "Callsign",
                character.callsign + "  \u00B7  " + character.perkLabel, UITheme.SmallSize,
                character.primary, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)callsign.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(30f, 188f), new Vector2(-28f, 212f));

            // Pose stage: every procedural animation the rig supports.
            string[] poseLabels = { "IDLE", "SPRINT", "JUMP", "SOMERSAULT", "SLIDE", "VICTORY", "REVIVE" };
            RunnerPose[] poses = { RunnerPose.Idle, RunnerPose.Run, RunnerPose.Jump, RunnerPose.Landing,
                RunnerPose.Slide, RunnerPose.Victory, RunnerPose.Revive };
            for (int i = 0; i < poses.Length; i++)
            {
                RunnerPose pose = poses[i];
                var button = UITheme.Button(card.transform, "Pose " + poseLabels[i], poseLabels[i],
                    pose == RunnerPose.Revive ? UITheme.Magenta : (i == 1 ? UITheme.Lime : UITheme.Muted),
                    () => { _app.Audio.PlayUiClick(); garage.SetPose(pose); }, 14);
                UITheme.Anchor((RectTransform)button.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(28f + (i % 4) * 136f, 84f + (i / 4) * 52f),
                    new Vector2(136f + (i % 4) * 136f, 122f + (i / 4) * 52f));
            }

            var specLine = UITheme.Label(card.transform, "Specs",
                "TRAIL  " + (trail != null ? trail.displayName.ToUpperInvariant() : "STANDARD") +
                "    //    ARMOUR STYLE " + (character.armorStyle + 1).ToString("00") +
                "    //    SERVER-SYNCED", UITheme.SmallSize, UITheme.Muted, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)specLine.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(30f, 36f), new Vector2(-28f, 62f));
        }

        private void BuildRoster(RectTransform root)
        {
            UITheme.Scroll(root, "Roster Scroll", out RectTransform content);
            var scrollRect = root.Find("Roster Scroll");
            UITheme.Anchor((RectTransform)scrollRect, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(648f, 44f), new Vector2(-36f, -222f));

            var rosterTitle = UITheme.Label(content, "Roster Title", "ACTIVE RUNNER", UITheme.SmallSize,
                UITheme.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            rosterTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

            var profile = _app.Profile;
            var characters = _app.Config.characters;
            for (int i = 0; i < characters.Count; i++)
            {
                var character = characters[i];
                bool owned = IsOwned(character);
                bool selected = profile.selectedCharacter == character.id;
                var card = UITheme.GlassCard(content, "Runner " + character.id,
                    new Color(0.035f, 0.065f, 0.12f, 0.94f)).transform.parent as RectTransform;
                UITheme.Layout(card.gameObject, 128f, -1f, 128f);

                var dot = UITheme.Img(card.transform, "Dot", character.primary, TextureFactory.Diamond, false);
                UITheme.Anchor((RectTransform)dot.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(20f, -24f), new Vector2(68f, 24f));

                var name = UITheme.Label(card.transform, "Name", character.displayName, UITheme.HeadingSize,
                    UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(94f, -52f), new Vector2(-420f, -14f));

                var callsign = UITheme.Label(card.transform, "Callsign",
                    character.callsign + "  \u00B7  " + character.perkLabel, UITheme.SmallSize,
                    character.primary, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)callsign.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(96f, -82f), new Vector2(-420f, -54f));

                string bioCopy = character.premium && !string.IsNullOrEmpty(character.abilityDescription)
                    ? character.bio + "  " + character.abilityDescription
                    : character.bio;
                var bio = UITheme.Label(card.transform, "Bio", bioCopy, UITheme.SmallSize, UITheme.Dim,
                    TextAnchor.UpperLeft);
                UITheme.Anchor((RectTransform)bio.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                    new Vector2(96f, 14f), new Vector2(-420f, -88f));

                if (selected)
                {
                    var tag = UITheme.Badge(card.transform, "Status", "EQUIPPED", UITheme.Lime, 150, 30);
                    UITheme.Anchor((RectTransform)tag.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                        new Vector2(-236f, -15f), new Vector2(-28f, 15f));
                }
                else if (owned)
                {
                    string id = character.id;
                    var equip = UITheme.Button(card.transform, "Action", "EQUIP", UITheme.Cyan,
                        () => Equip(character), UITheme.BodySize);
                    UITheme.Anchor((RectTransform)equip.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                        new Vector2(-236f, -28f), new Vector2(-28f, 28f));
                }
                else if (character.premium)
                {
                    bool online = _app.Premium != null && _app.Premium.Online;
                    var action = UITheme.Button(card.transform, "Action",
                        online ? "\u25C6 " + UITheme.FormatNumber(character.premiumPriceGems) : "STORE OFFLINE",
                        online ? UITheme.Magenta : UITheme.Muted, () => RequestPremiumCharacter(character),
                        UITheme.BodySize);
                    action.interactable = online;
                    UITheme.Anchor((RectTransform)action.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                        new Vector2(-236f, -28f), new Vector2(-28f, 28f));
                }
                else
                {
                    string id = character.id;
                    var locked = profile.level < character.unlockLevel;
                    var action = UITheme.Button(card.transform, "Action",
                        locked ? "LVL " + character.unlockLevel : "\u25C6 " + UITheme.FormatNumber(character.unlockCost),
                        locked ? UITheme.Muted : UITheme.Magenta, () => RequestUnlock(id), UITheme.BodySize);
                    action.interactable = !locked;
                    UITheme.Anchor((RectTransform)action.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                        new Vector2(-236f, -28f), new Vector2(-28f, 28f));
                }
            }

            // Equip trail shortcut trails stay in the full shop; this rows links to it.
            var moreLink = UITheme.Button(content, "More Link", "TRAILS & GEAR  \u203A  OPEN FULL SHOP",
                UITheme.Muted, () => { _app.Audio.PlayUiClick(); _app.ShowShop(); }, UITheme.BodySize);
            UITheme.Layout(moreLink.gameObject, 64f, -1f, 64f);
        }

        private bool IsOwned(CharacterDefinition character)
        {
            if (character == null) return false;
            if (_app.Profile.unlockedCharacters.Contains(character.id)) return true;
            return character.premium && _app.Premium != null && _app.Premium.OwnsCharacter(character.id);
        }

        private void Equip(CharacterDefinition character)
        {
            _app.Audio.PlayUiClick();
            if (character == null) return;
            if (!_app.Profile.unlockedCharacters.Contains(character.id))
                _app.Profile.unlockedCharacters.Add(character.id);
            _app.Progression.SelectCharacter(character.id);
            _app.Audio.PlaySfx("sfx_select", 0.8f);
            _app.SaveProfile();
            Reload();
        }

        /// <summary>
        /// Premium frames are bought with server-authoritative GEMS. The server validates the
        /// balance and idempotency key; the local roster only changes after a confirmed purchase.
        /// </summary>
        private void RequestPremiumCharacter(CharacterDefinition character)
        {
            _app.Audio.PlayUiClick();
            if (character == null) return;
            var premium = _app.Premium;
            if (premium == null || !premium.Online)
            {
                _app.UI.ShowModal("STORE OFFLINE",
                    "Premium runners are verified by the store service. Start the service and refresh, " +
                    "then try again. No GEMS have been spent.", "RETRY", () =>
                    {
                        _app.Premium?.SyncAll(_ =>
                        {
                            if (this == null) return;
                            _app.UI.Toast(_app.Premium != null && _app.Premium.Online
                                ? "STORE ONLINE - TRY THE PURCHASE AGAIN" : "STORE STILL OFFLINE",
                                _app.Premium != null && _app.Premium.Online ? UITheme.Lime : UITheme.Red, 2.6f);
                        });
                    }, "CLOSE");
                return;
            }
            if (premium.Gems < character.premiumPriceGems)
            {
                _app.UI.Toast("NOT ENOUGH GEMS - BUY GEMS IN THE GEM SHOP", UITheme.Red, 2.8f);
                _app.UI.ShowChoiceModal("NOT ENOUGH GEMS",
                    character.displayName + " costs " + character.premiumPriceGems + " GEMS. " +
                    "Your server wallet holds " + UITheme.FormatNumber(premium.Gems) + " GEMS.",
                    "BUY GEMS", () => _app.ShowGemShop(),
                    "CLOSE", null);
                return;
            }
            _app.UI.ShowModal("CONFIRM PREMIUM RUNNER",
                character.displayName + "\n\n" + character.callsign + " \u00B7 " + character.perkLabel +
                "\n\nCOST  \u25C6 " + UITheme.FormatNumber(character.premiumPriceGems) +
                "     WALLET  \u25C6 " + UITheme.FormatNumber(premium.Gems) +
                " GEMS\nCredited only after the server confirms the debit.",
                "PURCHASE", () => BuyPremiumCharacter(character), "CANCEL");
        }

        private void BuyPremiumCharacter(CharacterDefinition character)
        {
            var premium = _app.Premium;
            if (premium == null || character == null) return;
            string requestId = PremiumService.NewRequestId();
            _app.UI.Toast("AUTHORIZING PURCHASE...", UITheme.Cyan, 2f);
            premium.Purchase(character.premiumProductId, requestId, result => OnPremiumPurchased(character, result),
                error =>
                {
                    if (this == null) return;
                    if (error != null && error.code == "INSUFFICIENT_GEMS")
                    {
                        _app.UI.ShowChoiceModal("NOT ENOUGH GEMS",
                            "The server rejected the purchase: the wallet does not hold " +
                            character.premiumPriceGems + " GEMS.", "BUY GEMS", () => _app.ShowGemShop(),
                            "CLOSE", null);
                        return;
                    }
                    _app.UI.Toast(error != null ? error.Friendly : "PURCHASE FAILED", UITheme.Red, 2.8f);
                });
        }

        private void OnPremiumPurchased(CharacterDefinition character, PremiumPurchaseResult result)
        {
            if (this == null || result == null) return;
            _app.Audio.PlaySfx("sfx_purchase", 0.9f);
            if (!_app.Profile.unlockedCharacters.Contains(character.id))
                _app.Profile.unlockedCharacters.Add(character.id);
            _app.Progression.SelectCharacter(character.id);
            _app.Audio.PlaySfx("sfx_select", 0.8f);
            _app.SaveProfile();
            _app.UI.Toast(character.displayName + " UNLOCKED", UITheme.Lime, 2.8f);
            Reload();
        }

        private void RequestUnlock(string id)
        {
            _app.Audio.PlayUiClick();
            var character = _app.Config.Character(id);
            if (character == null || _app.Profile.unlockedCharacters.Contains(id)) return;
            if (_app.Profile.level < character.unlockLevel) return;
            PurchaseKit.ConfirmCredits(_app, "RUNNER // " + character.displayName,
                character.callsign + " \u00B7 " + character.perkLabel, character.unlockCost,
                () => UnlockCharacter(id), "UNLOCK");
        }

        private void UnlockCharacter(string id)
        {
            bool ok = _app.Progression.UnlockCharacter(id, out string message);
            _app.Audio.PlaySfx(ok ? "sfx_purchase" : "sfx_hit", ok ? 0.9f : 0.4f);
            _app.UI.Toast(message, ok ? UITheme.Lime : UITheme.Red, 2.5f);
            if (ok)
            {
                _app.Progression.SelectCharacter(id);
                _app.Audio.PlaySfx("sfx_select", 0.8f);
                _app.SaveProfile();
            }
            Reload();
        }

        private void Reload()
        {
            _app.UI.ShowScreen(Build(_app, _ui));
        }
    }
}
