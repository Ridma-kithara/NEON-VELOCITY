using System;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Shared purchase confirmation flows. CREDITS are earned locally inside runs; GEMS are a
    /// server-authoritative premium wallet. Keeping the two helpers separate makes it
    /// impossible for a screen to accidentally spend one currency while labelling the other.
    /// </summary>
    public static class PurchaseKit
    {
        /// <summary>Confirm a local CREDITS purchase (runner, trail, permanent tech).</summary>
        public static void ConfirmCredits(GameApp app, string itemName, string detailLines, int cost,
            Action onSpend, string confirmLabel = "UNLOCK")
        {
            if (app == null || app.UI == null) return;
            if (app.UI.HasModal) return;
            if (app.Profile.credits < cost)
            {
                app.UI.Toast("NOT ENOUGH CREDITS \u2014 EARN MORE IN RUNS", UITheme.Red, 2.5f);
                return;
            }
            string message = itemName + (string.IsNullOrEmpty(detailLines) ? "" : "\n\n" + detailLines) +
                "\n\nCOST  \u25C6 " + UITheme.FormatNumber(cost) +
                " CREDITS     BALANCE  \u25C6 " + UITheme.FormatNumber(app.Profile.credits) + " CREDITS";
            bool inFlight = false;
            app.UI.ShowModal("CONFIRM PURCHASE", message, confirmLabel, () =>
            {
                if (inFlight) return;
                inFlight = true;
                try { onSpend?.Invoke(); }
                finally { inFlight = false; }
            });
        }

        /// <summary>
        /// Confirm a premium GEMS purchase. The wallet is only mirrored locally, so this checks
        /// the server-reported balance and refuses to proceed while the store is offline. The
        /// actual debit still happens on the server through PremiumService.Purchase.
        /// </summary>
        public static void ConfirmGems(GameApp app, string itemName, string detailLines, int cost,
            Action onSpend, string confirmLabel = "PURCHASE")
        {
            if (app == null || app.UI == null) return;
            if (app.UI.HasModal) return;
            var premium = app.Premium;
            if (premium == null || !premium.Online)
            {
                app.UI.ShowModal("STORE OFFLINE",
                    "GEMS are verified by the premium service. Start the service, refresh the store " +
                    "and try again. No GEMS have been spent.", "RETRY", () =>
                    {
                        app.Premium?.SyncAll(ok => app.UI.Toast(
                            ok ? "STORE ONLINE - TRY AGAIN" : "STORE STILL OFFLINE",
                            ok ? UITheme.Lime : UITheme.Red, 2.6f));
                    }, "CLOSE");
                return;
            }
            if (premium.Gems < cost)
            {
                app.UI.ShowChoiceModal("NOT ENOUGH GEMS",
                    itemName + " costs " + UITheme.FormatNumber(cost) + " GEMS. Your server wallet holds " +
                    UITheme.FormatNumber(premium.Gems) + " GEMS.",
                    "BUY GEMS", () => app.ShowGemShop(),
                    "CLOSE", null);
                return;
            }
            string message = itemName + (string.IsNullOrEmpty(detailLines) ? "" : "\n\n" + detailLines) +
                "\n\nCOST  \u25C6 " + UITheme.FormatNumber(cost) +
                " GEMS     WALLET  \u25C6 " + UITheme.FormatNumber(premium.Gems) +
                " GEMS\nThe server debits once and confirms before anything unlocks.";
            bool inFlight = false;
            app.UI.ShowModal("CONFIRM PREMIUM PURCHASE", message, confirmLabel, () =>
            {
                if (inFlight) return;
                inFlight = true;
                try { onSpend?.Invoke(); }
                finally { inFlight = false; }
            });
        }

        /// <summary>Backwards-compatible alias for local CREDITS purchases.</summary>
        public static void ConfirmSpend(GameApp app, string itemName, string detailLines, int cost,
            Action onSpend, string confirmLabel = "UNLOCK") =>
            ConfirmCredits(app, itemName, detailLines, cost, onSpend, confirmLabel);

        /// <summary>Builds the diamond "price chip" used on every purchasable card row.</summary>
        public static Text PriceChip(Transform parent, string name, int cost, Color accent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var chip = UITheme.Label(parent, name, "\u25C6 " + UITheme.FormatNumber(cost), UITheme.BodySize,
                accent, TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Anchor((RectTransform)chip.transform, anchorMin, anchorMax, offsetMin, offsetMax);
            chip.raycastTarget = false;
            return chip;
        }
    }
}
