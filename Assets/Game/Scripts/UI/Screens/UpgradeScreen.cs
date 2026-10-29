using NeonVelocity.Core;
using NeonVelocity.Premium;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Dedicated permanent-upgrade terminal. The five runner tech lines are bought with earned
    /// CREDITS; the premium section below mirrors server-owned GEM upgrades. Both paths show
    /// the current level, the next benefit and a confirmation before any currency moves.
    /// </summary>
    public sealed class UpgradeScreen : MonoBehaviour
    {
        private GameApp _app;
        private UIRoot _ui;

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
        private static readonly Color[] UpgradeAccents =
        {
            UITheme.Cyan, UITheme.Violet, UITheme.Amber, UITheme.Lime, UITheme.Magenta
        };

        public static GameObject Build(GameApp app, UIRoot ui)
        {
            var root = ScreenKit.Root("Upgrades");
            var screen = root.gameObject.AddComponent<UpgradeScreen>();
            screen._app = app;
            screen._ui = ui;
            screen.BuildLayout();
            return root.gameObject;
        }

        private void BuildLayout()
        {
            var root = (RectTransform)transform;
            ScreenKit.Backdrop(root, UITheme.Lime);
            ScreenKit.Header(root, _app, "TECH BAY", "UPGRADES",
                "Permanent tech: CREDITS for core systems, GEMS for premium abilities.", UITheme.Lime,
                _app.NavigateBack);

            var summary = UITheme.Label(root, "Summary", "", UITheme.SmallSize, UITheme.Dim,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Anchor((RectTransform)summary.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-560f, -206f), new Vector2(-36f, -166f));
            summary.text = "TOTAL LEVELS  " + TotalLevels() + " / " + UpgradeIds.Length * ProgressionMath.MaxUpgradeLevel;

            UITheme.Scroll(root, "Upgrade Scroll", out RectTransform content);
            var scrollRect = root.Find("Upgrade Scroll");
            UITheme.Anchor((RectTransform)scrollRect, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(36f, 42f), new Vector2(-36f, -222f));

            var profile = _app.Profile;
            for (int i = 0; i < UpgradeIds.Length; i++)
            {
                int index = i;
                string id = UpgradeIds[i];
                int level = profile.GetUpgradeLevel(id);
                bool max = level >= ProgressionMath.MaxUpgradeLevel;
                int cost = ProgressionMath.UpgradeCost(id, level + 1);
                var accent = UpgradeAccents[i];
                var border = UITheme.GlassCard(content, "Upgrade " + id, new Color(0.035f, 0.065f, 0.12f, 0.94f));
                var card = border.transform.parent as RectTransform;
                UITheme.Layout(card.gameObject, 152f, -1f, 152f);

                var glyph = UITheme.Label(card.transform, "Glyph", "\u25C6", 40, accent,
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                UITheme.Anchor((RectTransform)glyph.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(18f, -32f), new Vector2(82f, 32f));

                var name = UITheme.Label(card.transform, "Name", UpgradeNames[i], UITheme.HeadingSize,
                    UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(104f, -56f), new Vector2(-390f, -16f));

                var desc = UITheme.Label(card.transform, "Desc", UpgradeDescriptions[i], UITheme.SmallSize,
                    UITheme.Dim, TextAnchor.MiddleLeft);
                UITheme.Anchor((RectTransform)desc.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(104f, -84f), new Vector2(-390f, -58f));

                for (int p = 0; p < ProgressionMath.MaxUpgradeLevel; p++)
                {
                    var pip = UITheme.Img(card.transform, "Pip " + p,
                        p < level ? accent : new Color(0.15f, 0.2f, 0.3f, 0.9f), UITheme.Rounded, false);
                    UITheme.Anchor((RectTransform)pip.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                        new Vector2(104f + p * 44f, 40f), new Vector2(140f + p * 44f, 56f));
                }

                var value = UITheme.Label(card.transform, "Value",
                    "LV " + level + "/" + ProgressionMath.MaxUpgradeLevel +
                    "     VALUE  x" + ProgressionMath.UpgradeValue(id, level).ToString("0.00") +
                    (max ? "     \u00B7 FULLY CALIBRATED" : "     NEXT  \u25C6 " + UITheme.FormatNumber(cost)),
                    UITheme.SmallSize, max ? UITheme.Lime : UITheme.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)value.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(104f, 12f), new Vector2(-390f, 38f));

                var buy = UITheme.Button(card.transform, "Buy", max ? "MAXED" : "\u25C6 " + UITheme.FormatNumber(cost),
                    max ? UITheme.Muted : accent, () => RequestUpgrade(index), UITheme.BodySize);
                buy.interactable = !max;
                UITheme.Anchor((RectTransform)buy.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-300f, -30f), new Vector2(-24f, 30f));
                buy.transform.localScale = Vector3.one;
                UiFx.Hover(buy.gameObject, 1.03f);
            }

            BuildPremiumTech(content);
        }

        /// <summary>
        /// Server-owned GEM upgrades for the two premium abilities. Ownership and levels come
        /// from the store; purchasing is refused while offline and never edits the level locally.
        /// </summary>
        private void BuildPremiumTech(RectTransform content)
        {
            var divider = UITheme.Divider(content, new Color(1f, 1f, 1f, 0.08f));
            UITheme.Layout(divider, 2f, -1f, 2f);

            var header = UITheme.Label(content, "Premium Header", "PREMIUM TECH  \u00B7  GEMS",
                UITheme.SmallSize, UITheme.Magenta, TextAnchor.MiddleLeft, FontStyle.Bold);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            var intro = UITheme.Label(content, "Premium Intro",
                "Ability refinements are durable sidegrades bought with GEMS. " +
                "The wallet is server-authoritative; a purchase is confirmed before the level rises.",
                UITheme.SmallSize, UITheme.Dim, TextAnchor.MiddleLeft);
            intro.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            var catalog = _app.Premium != null ? _app.Premium.Catalog : null;
            var products = catalog != null ? catalog.products : null;
            bool any = false;
            if (products != null)
            {
                for (int i = 0; i < products.Length; i++)
                {
                    var product = products[i];
                    if (product == null || !product.enabled || product.category != "upgrade") continue;
                    BuildPremiumCard(content, product);
                    any = true;
                }
            }
            if (any) return;

            var empty = UITheme.Label(content, "No Premium Tech",
                _app.Premium != null && _app.Premium.Online
                    ? "NO PREMIUM TECH IN THE CATALOGUE"
                    : "PREMIUM TECH OFFLINE - RECONNECT TO LOAD",
                UITheme.BodySize, UITheme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 72f;
        }

        private void BuildPremiumCard(RectTransform content, PremiumProduct product)
        {
            int level = _app.Profile.PremiumUpgradeLevel(product.upgradeId);
            int maxLevel = Mathf.Max(1, product.maxLevel);
            bool max = level >= maxLevel;
            var border = UITheme.GlassCard(content, "Premium " + product.id,
                new Color(0.06f, 0.035f, 0.085f, 0.95f));
            var card = border.transform.parent as RectTransform;
            UITheme.Layout(card.gameObject, 152f, -1f, 152f);
            border.color = UITheme.Magenta.Alpha(max ? 0.3f : 0.6f);

            var glyph = UITheme.Label(card.transform, "Glyph", "\u25C8", 40, UITheme.Magenta,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)glyph.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(18f, -32f), new Vector2(82f, 32f));

            var name = UITheme.Label(card.transform, "Name", product.name, UITheme.HeadingSize,
                UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(104f, -56f), new Vector2(-390f, -16f));

            var desc = UITheme.Label(card.transform, "Desc",
                PremiumBenefit(product.upgradeId, level + 1), UITheme.SmallSize,
                UITheme.Dim, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)desc.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(104f, -84f), new Vector2(-390f, -58f));

            for (int p = 0; p < maxLevel; p++)
            {
                var pip = UITheme.Img(card.transform, "Pip " + p,
                    p < level ? UITheme.Magenta : new Color(0.16f, 0.13f, 0.24f, 0.9f),
                    UITheme.Rounded, false);
                UITheme.Anchor((RectTransform)pip.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(104f + p * 44f, 40f), new Vector2(140f + p * 44f, 56f));
            }

            var value = UITheme.Label(card.transform, "Value",
                "LV " + level + "/" + maxLevel +
                "     NEXT  " + (max ? "MAX" : "\u25C6 " + UITheme.FormatNumber(product.priceGems) + " GEMS"),
                UITheme.SmallSize, max ? UITheme.Lime : UITheme.Muted,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)value.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(104f, 12f), new Vector2(-390f, 38f));

            bool online = _app.Premium != null && _app.Premium.Online;
            string label = max ? "MAXED" : (online ? "\u25C6 " + UITheme.FormatNumber(product.priceGems) + "  \u00B7  LVL " + (level + 1)
                : "STORE OFFLINE");
            var buy = UITheme.Button(card.transform, "Buy", label,
                max ? UITheme.Muted : UITheme.Magenta, () => RequestPremiumUpgrade(product), UITheme.BodySize);
            buy.interactable = !max && online;
            UITheme.Anchor((RectTransform)buy.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-300f, -30f), new Vector2(-24f, 30f));
            UiFx.Hover(buy.gameObject, 1.03f);
        }

        private static string PremiumBenefit(string upgradeId, int nextLevel)
        {
            switch (upgradeId)
            {
                case "revive_stabilizer":
                    return "Revive stabilisation +0.4 s per level; next resets with " +
                           (2.6f + Mathf.Clamp(nextLevel, 0, 3) * 0.4f).ToString("0.0") + " s of invulnerability.";
                case "phase_effect":
                    return "PHANTOM X phase duration +0.2 s per level; next extends the dash to " +
                           (1.5f + Mathf.Clamp(nextLevel, 0, 3) * 0.2f).ToString("0.0") + " s.";
                case "nova_effect":
                    return "NOVA TITAN burst duration +0.35 s per level; next holds the field for " +
                           (1.2f + Mathf.Clamp(nextLevel, 0, 3) * 0.35f).ToString("0.0") + " s.";
                default:
                    return "Permanent premium upgrade. Levels unlock one at a time, server-verified.";
            }
        }

        private void RequestPremiumUpgrade(PremiumProduct product)
        {
            if (product == null) return;
            _app.Audio.PlayUiClick();
            int level = _app.Profile.PremiumUpgradeLevel(product.upgradeId);
            if (level >= Mathf.Max(1, product.maxLevel)) return;
            PurchaseKit.ConfirmGems(_app, "PREMIUM TECH // " + product.name,
                PremiumBenefit(product.upgradeId, level + 1) + "\nLEVEL " + level + " \u2192 " + (level + 1),
                product.priceGems, () => BuyPremiumUpgrade(product), "INSTALL");
        }

        private void BuyPremiumUpgrade(PremiumProduct product)
        {
            var premium = _app.Premium;
            if (premium == null || product == null) return;
            premium.Purchase(product.id, PremiumService.NewRequestId(), result =>
            {
                if (this == null) return;
                _app.Audio.PlaySfx("sfx_purchase", 0.9f);
                _ui.Toast(product.name + " INSTALLED \u00B7 LEVEL " + result.level, UITheme.Lime, 2.8f);
                Reload();
            }, error =>
            {
                if (this == null) return;
                _ui.Toast(error != null ? error.Friendly : "UPGRADE FAILED", UITheme.Red, 2.8f);
            });
        }

        private static int TotalLevels()
        {
            var app = GameApp.Instance;
            if (app == null) return 0;
            int total = 0;
            for (int i = 0; i < UpgradeIds.Length; i++)
                total += app.Profile.GetUpgradeLevel(UpgradeIds[i]);
            return total;
        }

        private void RequestUpgrade(int index)
        {
            _app.Audio.PlayUiClick();
            string id = UpgradeIds[index];
            int level = _app.Profile.GetUpgradeLevel(id);
            if (level >= ProgressionMath.MaxUpgradeLevel) return;
            int cost = ProgressionMath.UpgradeCost(id, level + 1);
            PurchaseKit.ConfirmCredits(_app, "TECH // " + UpgradeNames[index],
                UpgradeDescriptions[index] + "  (LEVEL " + (level + 1) + ")", cost,
                () => BuyUpgrade(id), "INSTALL");
        }

        private void BuyUpgrade(string id)
        {
            bool ok = _app.Progression.BuyUpgrade(id, out string message);
            _app.Audio.PlaySfx(ok ? "sfx_upgrade" : "sfx_hit", ok ? 0.9f : 0.4f);
            _app.UI.Toast(message, ok ? UITheme.Lime : UITheme.Red, 2.5f);
            Reload();
        }

        private void Reload()
        {
            _app.UI.ShowScreen(Build(_app, _ui));
        }
    }
}
