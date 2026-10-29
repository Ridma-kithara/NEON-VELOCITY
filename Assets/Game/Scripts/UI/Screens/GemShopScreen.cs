using System;
using System.Collections.Generic;
using NeonVelocity.Account;
using NeonVelocity.Core;
using NeonVelocity.Premium;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Server-authoritative premium store.
    ///
    /// GEMS are never minted locally. Mobile purchases go through the official App Store or
    /// Google Play billing sheet; desktop/web purchases use the server-created hosted card
    /// checkout. In both cases the client only forwards store evidence and the backend
    /// verifies it with the provider before crediting the wallet. No card number or CVV is
    /// ever collected by the game. Premium runners and upgrades are billed in GEMS through
    /// the same wallet.
    /// </summary>
    public sealed class GemShopScreen : MonoBehaviour
    {
        public enum GemShopTab
        {
            Packages,
            Store,
            Orders
        }

        private GameApp _app;
        private UIRoot _ui;
        private GemShopTab _tab;

        private Text _balance;
        private Text _walletUnit;
        private Text _status;
        private Text _pending;
        private Text _serverUrl;
        private Text _receiptEmailLabel;
        private Text _receiptStateLabel;

        private RectTransform _packagesContent;
        private RectTransform _storeContent;
        private RectTransform _ordersContent;
        private GameObject _packagesRoot;
        private GameObject _storeRoot;
        private GameObject _ordersRoot;
        private readonly List<Button> _tabButtons = new List<Button>(3);
        private readonly List<Text> _tabLabels = new List<Text>(3);

        private List<StoreTransaction> _transactions = new List<StoreTransaction>();
        private bool _transactionsLoaded;
        private bool _transactionsLoading;
        private bool _actionBusy;
        private bool _purchaseBusy;
        private bool _awaitingCheckout;

        public static GameObject Build(GameApp app, UIRoot ui, GemShopTab startTab = GemShopTab.Packages)
        {
            var root = ScreenKit.Root("Gem Shop");
            var screen = root.gameObject.AddComponent<GemShopScreen>();
            screen._app = app;
            screen._ui = ui;
            screen._tab = startTab;
            screen.BuildLayout();
            return root.gameObject;
        }

        private void BuildLayout()
        {
            var root = (RectTransform)transform;
            ScreenKit.Backdrop(root, UITheme.Magenta);
            ScreenKit.Header(root, _app, "PREMIUM SUPPLY", "GEM SHOP",
                "Official App Store, Google Play or hosted card checkout. " +
                "GEMS are credited only after the server verifies the payment.",
                UITheme.Magenta, _app.NavigateBack);

            BuildWallet(root);
            BuildTabs(root);
            SelectTab(_tab);
            RefreshWalletLabels();
            if (_app.Account != null && !_app.Account.PaymentsLoaded)
            {
                _app.Account.RefreshPaymentCapabilities(ok =>
                {
                    if (this == null) return;
                    BuildPackages();
                    RefreshWalletLabels();
                });
            }
        }

        // ---- Wallet rail ------------------------------------------------------------------

        private void BuildWallet(RectTransform root)
        {
            var border = UITheme.GlassCard(root, "Wallet Card", new Color(0.05f, 0.03f, 0.075f, 0.96f));
            var panel = border.transform.parent as RectTransform;
            border.color = UITheme.Amber.Alpha(0.65f);
            UITheme.Anchor(panel, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(36f, 44f), new Vector2(420f, -168f));

            var title = UITheme.Label(panel.transform, "Wallet Title", "SERVER WALLET", UITheme.SmallSize,
                UITheme.Amber, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -44f), new Vector2(-26f, -16f));

            _balance = UITheme.Label(panel.transform, "Balance", "\u25C6 0", 46, UITheme.Amber,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_balance.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -102f), new Vector2(-26f, -48f));

            _walletUnit = UITheme.Label(panel.transform, "Unit", "GEMS \u00B7 PREMIUM CURRENCY", UITheme.SmallSize,
                UITheme.Dim, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)_walletUnit.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -126f), new Vector2(-26f, -104f));

            _status = UITheme.Label(panel.transform, "Status", "STORE OFFLINE", UITheme.SmallSize,
                UITheme.Red, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_status.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -154f), new Vector2(-26f, -130f));

            _pending = UITheme.Label(panel.transform, "Pending", "", UITheme.SmallSize,
                UITheme.Muted, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)_pending.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -178f), new Vector2(-26f, -156f));

            var divider = UITheme.Divider(panel.transform, new Color(1f, 1f, 1f, 0.09f));
            UITheme.Anchor((RectTransform)divider.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -192f), new Vector2(-26f, -190f));

            var how = UITheme.Label(panel.transform, "How Header", "HOW PURCHASES WORK", UITheme.SmallSize,
                UITheme.Magenta, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)how.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -224f), new Vector2(-26f, -196f));

            string[] steps =
            {
                "1  CHOOSE A GEM PACKAGE",
                "2  PAY IN THE OFFICIAL STORE OR BROWSER",
                "3  THE SERVER VERIFIES THE RECEIPT",
                "4  GEMS ARRIVE AFTER VERIFICATION"
            };
            for (int i = 0; i < steps.Length; i++)
            {
                var row = UITheme.Label(panel.transform, "Step " + i, steps[i], 15, UITheme.Dim,
                    TextAnchor.MiddleLeft);
                UITheme.Anchor((RectTransform)row.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(26f, -258f - i * 28f), new Vector2(-26f, -232f - i * 28f));
            }

            var note = UITheme.Label(panel.transform, "Note",
                "Gameplay awards CREDITS only.\n" +
                "GEMS are purchased and cannot be\n" +
                "earned by running, near misses or missions.\n" +
                "The game never sees or stores card data.",
                14, UITheme.Muted, TextAnchor.UpperLeft);
            UITheme.Anchor((RectTransform)note.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -424f), new Vector2(-26f, -366f));

            var receiptHeader = UITheme.Label(panel.transform, "Receipt Header", "RECEIPT EMAIL",
                UITheme.SmallSize, UITheme.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)receiptHeader.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -468f), new Vector2(-26f, -442f));
            _receiptEmailLabel = UITheme.Label(panel.transform, "Receipt Email", "NOT SET",
                UITheme.BodySize, UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_receiptEmailLabel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -498f), new Vector2(-26f, -470f));
            _receiptEmailLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _receiptStateLabel = UITheme.Label(panel.transform, "Receipt State", "",
                UITheme.SmallSize, UITheme.Muted, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)_receiptStateLabel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -522f), new Vector2(-26f, -500f));

            var accountButton = UITheme.Button(panel.transform, "Account Receipts", "ACCOUNT & RECEIPTS",
                UITheme.Cyan, OpenAccount, UITheme.SmallSize, false);
            UITheme.Anchor((RectTransform)accountButton.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -566f), new Vector2(-26f, -522f));

            var history = UITheme.Button(panel.transform, "History", "VIEW TRANSACTION HISTORY",
                UITheme.Violet, () => { _app.Audio.PlayUiClick(); SelectTab(GemShopTab.Orders); },
                UITheme.SmallSize, false);
            UITheme.Anchor((RectTransform)history.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -616f), new Vector2(-26f, -572f));

            var refresh = UITheme.Button(panel.transform, "Refresh", "REFRESH WALLET", UITheme.Magenta,
                RefreshWallet, UITheme.BodySize);
            UITheme.Anchor((RectTransform)refresh.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(26f, 44f), new Vector2(-26f, 94f));

            _serverUrl = UITheme.Label(panel.transform, "Server", "", 12, UITheme.Muted,
                TextAnchor.MiddleCenter);
            UITheme.Anchor((RectTransform)_serverUrl.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(26f, 16f), new Vector2(-26f, 40f));
        }

        private void OpenAccount()
        {
            _app.Audio.PlayUiClick();
            _app.ShowAccount(AccountScreen.TabProfile);
        }

        private void RefreshWallet()
        {
            _app.Audio.PlayUiClick();
            var premium = _app.Premium;
            if (premium == null)
            {
                _ui.Toast("STORE OFFLINE", UITheme.Red, 2.4f);
                return;
            }
            _ui.Toast("CONTACTING PREMIUM SERVICE...", UITheme.Cyan, 1.6f);
            premium.SyncAll(ok =>
            {
                if (this == null) return;
                _ui.Toast(ok ? "WALLET SYNCED" : "STORE OFFLINE - CACHED WALLET ONLY",
                    ok ? UITheme.Lime : UITheme.Red, 2.6f);
                if (_app.Account != null) _app.Account.RefreshProfile();
                _transactionsLoaded = false;
                Reload(_tab);
            });
        }

        private void RefreshWalletLabels()
        {
            var premium = _app.Premium;
            int gems = premium != null ? premium.Gems : 0;
            if (_balance != null) _balance.text = "\u25C6 " + UITheme.FormatNumber(gems);
            if (_walletUnit != null)
                _walletUnit.text = "GEMS \u00B7 SERVER WALLET  \u00B7  " +
                                   UITheme.FormatNumber(_app.Profile.credits) + " CREDITS";
            if (_status != null)
            {
                bool online = premium != null && premium.Online;
                _status.text = premium == null ? "STORE OFFLINE"
                    : (premium.Busy ? "CONNECTING TO STORE..." : (online ? "STORE ONLINE" : "STORE OFFLINE"));
                _status.color = online ? UITheme.Lime : UITheme.Red;
            }
            if (_pending != null)
            {
                string provider = PreferredRealMoneyProvider();
                bool configured = IsProviderConfigured(provider);
                _pending.text = provider == ""
                    ? "NO REAL-MONEY STORE ON THIS PLATFORM"
                    : configured
                        ? "SECURE CHECKOUT  \u00B7  " + ProviderLabel(provider) + "  \u00B7  VERIFIED SERVER CREDIT"
                        : ProviderLabel(provider) + " NOT CONFIGURED";
                _pending.color = configured ? UITheme.Lime : UITheme.Amber;
            }
            if (_serverUrl != null)
                _serverUrl.text = "AUTHORITY  " + (premium != null ? premium.ServerUrl : "NOT CONFIGURED");
            RefreshReceiptLabels();
        }

        private void RefreshReceiptLabels()
        {
            var account = _app.Account;
            string email = account != null ? account.ReceiptEmail : "";
            bool verified = account != null && account.EmailVerified && !string.IsNullOrEmpty(account.Email) &&
                            string.Equals(email, account.Email, StringComparison.OrdinalIgnoreCase);
            if (_receiptEmailLabel != null)
                _receiptEmailLabel.text = string.IsNullOrEmpty(email) ? "NOT SET" : email;
            if (_receiptStateLabel != null)
            {
                if (account == null || !account.SignedIn)
                {
                    _receiptStateLabel.text = "SIGN IN TO BUY GEMS";
                    _receiptStateLabel.color = UITheme.Amber;
                }
                else if (string.IsNullOrEmpty(email))
                {
                    _receiptStateLabel.text = "NO RECEIPT EMAIL SET";
                    _receiptStateLabel.color = UITheme.Red;
                }
                else if (verified)
                {
                    _receiptStateLabel.text = "VERIFIED ACCOUNT EMAIL  \u00B7  RECEIPTS ENABLED";
                    _receiptStateLabel.color = UITheme.Lime;
                }
                else
                {
                    _receiptStateLabel.text = "SAVED RECEIPT EMAIL  \u00B7  NOT THE VERIFIED ACCOUNT EMAIL";
                    _receiptStateLabel.color = UITheme.Amber;
                }
            }
        }

        // ---- Tabs and content -------------------------------------------------------------

        private void BuildTabs(RectTransform root)
        {
            var row = UITheme.NewRect(root, "Tabs");
            UITheme.Anchor(row, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(448f, -204f), new Vector2(-36f, -152f));
            string[] labels = { "BUY GEMS", "PREMIUM STORE", "TRANSACTION HISTORY" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var button = UITheme.Button(row, "Tab " + labels[i], labels[i], UITheme.Magenta,
                    () => SelectTab((GemShopTab)index), UITheme.BodySize);
                UITheme.Anchor((RectTransform)button.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(i * 284f, 0f), new Vector2(i * 284f + 268f, 0f));
                _tabButtons.Add(button);
                _tabLabels.Add(button.GetComponentInChildren<Text>());
            }

            var host = UITheme.NewRect(root, "Tab Host");
            UITheme.Anchor(host, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(448f, 44f), new Vector2(-36f, -216f));

            _packagesRoot = CreateTabScroll(host, "Packages", out _packagesContent);
            _storeRoot = CreateTabScroll(host, "Store", out _storeContent);
            _ordersRoot = CreateTabScroll(host, "Orders", out _ordersContent);

            BuildPackages();
            BuildProducts();
            BuildTransactionList();
        }

        private static GameObject CreateTabScroll(RectTransform host, string name, out RectTransform content)
        {
            UITheme.Scroll(host, name + " Scroll", out content);
            var scroll = host.Find(name + " Scroll");
            UITheme.Stretch((RectTransform)scroll);
            return scroll.gameObject;
        }

        private void SelectTab(GemShopTab tab)
        {
            _tab = tab;
            if (_packagesRoot != null) _packagesRoot.SetActive(tab == GemShopTab.Packages);
            if (_storeRoot != null) _storeRoot.SetActive(tab == GemShopTab.Store);
            if (_ordersRoot != null) _ordersRoot.SetActive(tab == GemShopTab.Orders);
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                bool active = i == (int)tab;
                var image = _tabButtons[i].targetGraphic as Image;
                if (image != null) image.color = active ? UITheme.Magenta.Alpha(0.85f) : UITheme.PanelBright;
                if (_tabLabels.Count > i && _tabLabels[i] != null)
                    _tabLabels[i].color = active ? UITheme.White : UITheme.Dim;
            }
            if (tab == GemShopTab.Orders && !_transactionsLoaded && !_transactionsLoading)
                LoadTransactions();
        }

        // ---- Real-money provider selection -------------------------------------------------

        private static string PreferredRealMoneyProvider()
        {
            if (PlatformStore.IsSupported) return PlatformStore.Provider;
            return "stripe";
        }

        private bool IsProviderConfigured(string provider)
        {
            var account = _app != null ? _app.Account : null;
            var payments = account != null ? account.Payments : null;
            if (payments == null) return false;
            switch (provider)
            {
                case PlatformStore.ProviderApple: return payments.apple;
                case PlatformStore.ProviderGoogle: return payments.googlePlay;
                case "stripe": return payments.stripe;
                default: return false;
            }
        }

        private static string ProviderLabel(string provider)
        {
            switch (provider)
            {
                case PlatformStore.ProviderApple: return "APP STORE";
                case PlatformStore.ProviderGoogle: return "GOOGLE PLAY";
                case "stripe": return "CARD CHECKOUT";
                default: return "STORE";
            }
        }

        private static Color ProviderColor(string provider)
        {
            switch (provider)
            {
                case PlatformStore.ProviderApple: return UITheme.White;
                case PlatformStore.ProviderGoogle: return UITheme.Lime;
                case "stripe": return UITheme.Cyan;
                default: return UITheme.Muted;
            }
        }

        // ---- Packages ---------------------------------------------------------------------

        private void BuildPackages()
        {
            Clear(_packagesContent);
            var heading = UITheme.Label(_packagesContent, "Packages Title", "GEM PACKAGES", UITheme.SmallSize,
                UITheme.Amber, TextAnchor.MiddleLeft, FontStyle.Bold);
            heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            string provider = PreferredRealMoneyProvider();
            bool configured = IsProviderConfigured(provider);
            string introText = configured
                ? "Pay through " + ProviderLabel(provider) + ". The provider shows the final price and " +
                  "currency; GEMS arrive only after the server verifies the purchase."
                : "The " + ProviderLabel(provider) + " is not configured on this server yet. " +
                  "No charge can be started and the game will not fake a purchase.";
            var intro = UITheme.Label(_packagesContent, "Packages Intro", introText, UITheme.SmallSize,
                configured ? UITheme.Dim : UITheme.Amber, TextAnchor.MiddleLeft);
            intro.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            var catalog = _app.Premium != null ? _app.Premium.Catalog : null;
            var packages = catalog != null ? catalog.packages : null;
            if (packages == null || packages.Length == 0)
            {
                var empty = UITheme.Label(_packagesContent, "No Packages",
                    "PACKAGE CATALOGUE UNAVAILABLE\nReconnect to the premium service to load live packages.",
                    UITheme.BodySize, UITheme.Red, TextAnchor.MiddleCenter, FontStyle.Bold);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
                return;
            }

            for (int i = 0; i < packages.Length; i++)
            {
                var package = packages[i];
                if (package == null || !package.enabled) continue;
                var card = UITheme.GlassCard(_packagesContent, "Package " + package.id,
                    new Color(0.04f, 0.055f, 0.1f, 0.94f)).transform.parent as RectTransform;
                UITheme.Layout(card.gameObject, 176f, -1f, 176f);

                var accent = UITheme.Img(card.transform, "Accent", UITheme.Amber);
                UITheme.Anchor((RectTransform)accent.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(0f, 14f), new Vector2(4f, -14f));

                var name = UITheme.Label(card.transform, "Name", package.name, UITheme.HeadingSize,
                    UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(24f, -46f), new Vector2(-344f, -12f));

                string gemLine = UITheme.FormatNumber(package.gems) + " GEMS" +
                                 (package.bonusGems > 0
                                     ? "  +  " + UITheme.FormatNumber(package.bonusGems) + " BONUS"
                                     : "");
                var gems = UITheme.Label(card.transform, "Gems", gemLine, UITheme.BodySize, UITheme.Amber,
                    TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)gems.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(24f, -80f), new Vector2(-344f, -50f));

                var desc = UITheme.Label(card.transform, "Desc", package.description, UITheme.SmallSize,
                    UITheme.Dim, TextAnchor.MiddleLeft);
                UITheme.Anchor((RectTransform)desc.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(24f, 48f), new Vector2(-344f, 72f));

                string priceHint;
                if (provider == "stripe")
                    priceHint = package.priceLkr > 0
                        ? "LKR " + UITheme.FormatNumber(package.priceLkr) +
                          " REFERENCE  \u00B7  FINAL PRICE SHOWN AT CHECKOUT"
                        : "FINAL PRICE AND CURRENCY SHOWN AT CHECKOUT";
                else
                    priceHint = "PRICE SHOWN BY " + ProviderLabel(provider) +
                                "  \u00B7  SERVER VERIFIES THE PURCHASE";
                var hint = UITheme.Label(card.transform, "Price Hint", priceHint,
                    UITheme.SmallSize, UITheme.Muted, TextAnchor.MiddleLeft);
                UITheme.Anchor((RectTransform)hint.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(24f, 18f), new Vector2(-344f, 44f));

                string buttonLabel = configured
                    ? (provider == "stripe" ? "PAY BY CARD" : "BUY ON " + ProviderLabel(provider))
                    : "STORE NOT CONFIGURED";
                var buy = UITheme.Button(card.transform, "Buy", buttonLabel,
                    configured ? ProviderColor(provider) : UITheme.Muted,
                    () => RequestPackage(package), UITheme.BodySize);
                UITheme.Anchor((RectTransform)buy.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-320f, -30f), new Vector2(-24f, 30f));
            }
        }

        private void RequestPackage(GemPackage package)
        {
            if (_actionBusy || package == null) return;
            _app.Audio.PlayUiClick();
            var account = _app.Account;
            if (account == null)
            {
                _ui.Toast("ACCOUNT SERVICE UNAVAILABLE", UITheme.Red, 3f);
                return;
            }
            if (!account.SignedIn)
            {
                _ui.ShowChoiceModal("SIGN IN TO BUY GEMS",
                    "Purchases must be tied to an account so the server wallet and receipt belong " +
                    "to you. Guest progress stays local and can be linked without losing anything.",
                    "OPEN ACCOUNT", () => _app.ShowAccount(AccountScreen.TabSignIn),
                    "CANCEL", null);
                return;
            }
            if (!account.PaymentsLoaded)
            {
                _ui.Toast("CHECKING PAYMENT PROVIDER...", UITheme.Cyan, 1.6f);
                account.RefreshPaymentCapabilities(ok =>
                {
                    if (this == null) return;
                    BuildPackages();
                    RefreshWalletLabels();
                    if (ok) RequestPackage(package);
                    else _ui.Toast("PAYMENT PROVIDER STATUS UNAVAILABLE", UITheme.Red, 2.8f);
                });
                return;
            }

            string provider = PreferredRealMoneyProvider();
            if (!IsProviderConfigured(provider))
            {
                ShowProviderUnavailable(provider, package);
                return;
            }

            string receipt = account.ReceiptEmail;
            if (string.IsNullOrEmpty(receipt)) receipt = account.Email;
            if (string.IsNullOrEmpty(receipt))
            {
                _ui.ShowChoiceModal("RECEIPT EMAIL REQUIRED",
                    "Add a receipt email in your account before buying GEMS. The verified account " +
                    "email is selected automatically; you can change it and the choice is remembered " +
                    "on your account.",
                    "OPEN ACCOUNT", () => _app.ShowAccount(AccountScreen.TabProfile),
                    "CANCEL", null);
                return;
            }

            bool verified = account.EmailVerified && !string.IsNullOrEmpty(account.Email) &&
                            string.Equals(receipt, account.Email, StringComparison.OrdinalIgnoreCase);
            if (!verified)
            {
                bool canUseVerified = account.EmailVerified && !string.IsNullOrEmpty(account.Email);
                if (canUseVerified)
                {
                    _ui.ShowChoiceModal("RECEIPT ADDRESS NOT VERIFIED",
                        "The saved receipt address is " + receipt + ".\n\nUse the verified account " +
                        "email " + account.Email + " so the server can deliver the receipt, or " +
                        "continue with the saved address.",
                        "USE VERIFIED EMAIL", () => UseVerifiedReceipt(package),
                        "CONTINUE ANYWAY", () => ConfirmPurchase(package, receipt, provider),
                        "CANCEL", null);
                }
                else
                {
                    _ui.ShowChoiceModal("RECEIPT EMAIL UNVERIFIED",
                        "The receipt address is " + receipt + ".\n\nThe purchase can still be " +
                        "completed and appears in transaction history, but the server may not be " +
                        "able to email a receipt until the address is verified.",
                        "CONTINUE", () => ConfirmPurchase(package, receipt, provider),
                        "OPEN ACCOUNT", () => _app.ShowAccount(AccountScreen.TabProfile),
                        "CANCEL", null);
                }
                return;
            }

            ConfirmPurchase(package, receipt, provider);
        }

        private void UseVerifiedReceipt(GemPackage package)
        {
            var account = _app.Account;
            if (account == null || package == null) return;
            string email = account.Email;
            if (string.IsNullOrEmpty(email)) return;
            _ui.Toast("UPDATING RECEIPT ADDRESS...", UITheme.Cyan, 1.4f);
            account.UpdateProfile(account.DisplayName, email, ok =>
            {
                if (this == null) return;
                if (!ok)
                {
                    _ui.Toast(account.LastError != null ? account.LastError.Friendly : "PROFILE UPDATE FAILED",
                        UITheme.Red, 2.8f);
                    return;
                }
                RefreshReceiptLabels();
                ConfirmPurchase(package, email, PreferredRealMoneyProvider());
            });
        }

        private void ConfirmPurchase(GemPackage package, string receiptEmail, string provider)
        {
            if (package == null) return;
            int total = package.gems + package.bonusGems;
            string providerName = ProviderLabel(provider);
            _ui.ShowChoiceModal(providerName + " PURCHASE",
                package.name + "  \u00B7  " + UITheme.FormatNumber(total) + " GEMS\n" +
                "RECEIPT EMAIL  " + receiptEmail + "\n\n" +
                (provider == "stripe"
                    ? "A secure provider page opens in your browser. The final price and currency " +
                      "are shown there. No card number or CVV is entered in the game.\n\n"
                    : "The official " + providerName + " payment sheet opens. Payment details are " +
                      "handled entirely by the platform.\n\n") +
                "GEMS are credited only after the server verifies the purchase with the provider.",
                "CONTINUE", () => StartPurchase(package, receiptEmail, provider),
                "CANCEL", null);
        }

        private void StartPurchase(GemPackage package, string receiptEmail, string provider)
        {
            if (_actionBusy) return;
            if (provider == "stripe")
            {
                StartCardCheckout(package, receiptEmail);
                return;
            }
            StartPlatformPurchase(package, receiptEmail);
        }

        private void StartPlatformPurchase(GemPackage package, string receiptEmail)
        {
            _actionBusy = true;
            _app.Audio.PlaySfx("sfx_purchase", 0.8f);
            _ui.Toast("OPENING " + ProviderLabel(PlatformStore.Provider) + "...",
                UITheme.Cyan, 2.4f);
            PlatformStore.Purchase(_app, package, receiptEmail, result =>
            {
                _actionBusy = false;
                if (this == null) return;
                _app.Audio.PlaySfx("sfx_purchase", 0.9f);
                string credited = result != null && result.credited > 0
                    ? "  +" + UITheme.FormatNumber(result.credited) + " GEMS"
                    : "";
                if (result != null && result.processed == false)
                    _ui.Toast("PURCHASE ALREADY CREDITED \u00B7 WALLET REFRESHED", UITheme.Lime, 3.4f);
                else
                    _ui.Toast("PURCHASE VERIFIED" + credited, UITheme.Lime, 3.4f);
                RefreshAfterPurchase();
            }, error =>
            {
                _actionBusy = false;
                if (this == null) return;
                string message = error != null ? error.Friendly : "PURCHASE FAILED";
                if (error != null && error.code == "PURCHASE_FAILED")
                    message += " \u00B7 IF YOU COMPLETED PAYMENT, IT WILL BE VERIFIED ON NEXT LAUNCH";
                _ui.Toast(message, UITheme.Red, 3.6f);
            });
        }

        private void RefreshAfterPurchase()
        {
            var premium = _app.Premium;
            if (premium == null)
            {
                Reload(GemShopTab.Packages);
                return;
            }
            premium.SyncAll(ok =>
            {
                if (this == null) return;
                if (_app.Account != null) _app.Account.RefreshProfile();
                _transactionsLoaded = false;
                Reload(_tab);
            });
        }

        private void StartCardCheckout(GemPackage package, string receiptEmail)
        {
            var account = _app.Account;
            if (account == null || package == null) return;
            _actionBusy = true;
            _ui.Toast("CREATING SECURE CHECKOUT...", UITheme.Cyan, 1.8f);
            account.StartCardCheckout(package.id, receiptEmail, url =>
            {
                _actionBusy = false;
                if (this == null) return;
                if (string.IsNullOrEmpty(url) ||
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    _ui.Toast("PROVIDER RETURNED AN INVALID CHECKOUT LINK", UITheme.Red, 3f);
                    return;
                }
                _awaitingCheckout = true;
                _app.Audio.PlaySfx("sfx_purchase", 0.8f);
                Application.OpenURL(url);
                _ui.Toast("CHECKOUT OPENED IN YOUR BROWSER \u00B7 GEMS APPEAR AFTER VERIFIED PAYMENT",
                    UITheme.Cyan, 4.2f);
            }, failure =>
            {
                _actionBusy = false;
                if (this == null) return;
                if (failure != null && (failure.code == "PROVIDER_NOT_CONFIGURED" ||
                                        failure.code == "PRICE_NOT_CONFIGURED" ||
                                        failure.code == "REDIRECT_NOT_CONFIGURED"))
                {
                    ShowProviderUnavailable("stripe", package);
                    return;
                }
                _ui.Toast(failure != null ? failure.Friendly : "CARD CHECKOUT FAILED",
                    UITheme.Red, 3.2f);
            });
        }

        private void ShowProviderUnavailable(string provider, GemPackage package)
        {
            string title = ProviderLabel(provider) + " NOT CONFIGURED";
            string options;
            switch (provider)
            {
                case PlatformStore.ProviderApple:
                    options = "Configure NV_APPLE_SHARED_SECRET, NV_APPLE_PRODUCT_MAP and the App " +
                              "Store products, then run on an iOS device signed into the sandbox " +
                              "tester account.";
                    break;
                case PlatformStore.ProviderGoogle:
                    options = "Configure NV_GOOGLE_PLAY_SERVICE_ACCOUNT, NV_GOOGLE_PLAY_PACKAGE_NAME, " +
                              "NV_GOOGLE_PLAY_PRODUCT_MAP and the Play Console products, then run on " +
                              "an Android device with a licence-testing account.";
                    break;
                default:
                    options = "Configure NV_STRIPE_SECRET_KEY, NV_STRIPE_WEBHOOK_SECRET and " +
                              "NV_STRIPE_PRICE_MAP on the server.";
                    break;
            }
            _ui.ShowChoiceModal(title,
                "This server cannot start a " + ProviderLabel(provider) + " purchase for " +
                (package != null ? package.name : "this package") + ". No charge has been " +
                "started and the game will not fake a successful purchase.\n\n" + options,
                "CHECK AGAIN", () => RetryProvider(provider, package),
                "OPEN ACCOUNT", () => _app.ShowAccount(AccountScreen.TabProfile),
                "CLOSE", null);
        }

        private void RetryProvider(string provider, GemPackage package)
        {
            var account = _app.Account;
            if (account == null) return;
            _ui.Toast("CHECKING PAYMENT PROVIDER...", UITheme.Cyan, 1.6f);
            account.RefreshPaymentCapabilities(ok =>
            {
                if (this == null) return;
                BuildPackages();
                RefreshWalletLabels();
                if (ok && IsProviderConfigured(provider)) RequestPackage(package);
                else _ui.Toast(ProviderLabel(provider) + " IS STILL NOT CONFIGURED", UITheme.Red, 2.8f);
            });
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused || !_awaitingCheckout) return;
            _awaitingCheckout = false;
            var premium = _app.Premium;
            if (premium == null) return;
            _ui.Toast("CHECKING PAYMENT STATUS...", UITheme.Cyan, 1.6f);
            premium.SyncAll(ok =>
            {
                if (this == null) return;
                _ui.Toast(ok ? "WALLET REFRESHED - VERIFIED PURCHASES ARE VISIBLE"
                    : "WALLET STILL OFFLINE - REFRESH MANUALLY", ok ? UITheme.Lime : UITheme.Red, 3.2f);
                if (_app.Account != null) _app.Account.RefreshProfile();
                _transactionsLoaded = false;
                Reload(_tab);
            });
        }

        // ---- Premium products -------------------------------------------------------------

        private static readonly string[] CategoryOrder =
        {
            "character", "skin", "armor", "helmet", "effect", "animation", "ability", "upgrade"
        };

        private void BuildProducts()
        {
            Clear(_storeContent);
            var heading = UITheme.Label(_storeContent, "Store Title", "PREMIUM STORE", UITheme.SmallSize,
                UITheme.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            var intro = UITheme.Label(_storeContent, "Store Intro",
                "Premium frames and tech are durable sidegrades, never power sold on top of skill. " +
                "All prices are GEMS and all ownership is confirmed by the server.", UITheme.SmallSize,
                UITheme.Dim, TextAnchor.MiddleLeft);
            intro.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            var catalog = _app.Premium != null ? _app.Premium.Catalog : null;
            var products = catalog != null ? catalog.products : null;
            if (products == null || products.Length == 0)
            {
                var empty = UITheme.Label(_storeContent, "No Products",
                    "PREMIUM CATALOGUE UNAVAILABLE\nReconnect to the premium service to load products.",
                    UITheme.BodySize, UITheme.Red, TextAnchor.MiddleCenter, FontStyle.Bold);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
                return;
            }

            for (int c = 0; c < CategoryOrder.Length; c++)
            {
                string category = CategoryOrder[c];
                bool wroteHeader = false;
                for (int i = 0; i < products.Length; i++)
                {
                    var product = products[i];
                    if (product == null || !product.enabled || product.category != category) continue;
                    if (!wroteHeader)
                    {
                        var header = UITheme.Label(_storeContent, "Category " + category,
                            CategoryTitle(category), UITheme.SmallSize,
                            category == "character" ? UITheme.Magenta : UITheme.Cyan,
                            TextAnchor.MiddleLeft, FontStyle.Bold);
                        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
                        wroteHeader = true;
                    }
                    BuildProductCard(product);
                }
            }
        }

        private static string CategoryTitle(string category)
        {
            switch (category)
            {
                case "character": return "PREMIUM RUNNERS";
                case "skin": return "SUIT SKINS";
                case "armor": return "ARMOUR PLATES";
                case "helmet": return "HELMETS";
                case "effect": return "TRAIL EFFECTS";
                case "animation": return "ANIMATION SETS";
                case "ability": return "ABILITY FX";
                case "upgrade": return "PERMANENT TECH UPGRADES";
                default: return category.ToUpperInvariant();
            }
        }

        private void BuildProductCard(PremiumProduct product)
        {
            var card = UITheme.GlassCard(_storeContent, "Product " + product.id,
                new Color(0.035f, 0.06f, 0.11f, 0.94f)).transform.parent as RectTransform;
            UITheme.Layout(card.gameObject, 112f, -1f, 112f);

            var dot = UITheme.Img(card.transform, "Dot",
                product.category == "character" ? UITheme.Magenta : UITheme.Cyan,
                TextureFactory.Diamond, false);
            UITheme.Anchor((RectTransform)dot.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(20f, -22f), new Vector2(64f, 22f));

            var name = UITheme.Label(card.transform, "Name", product.name, UITheme.HeadingSize,
                UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(86f, -44f), new Vector2(-336f, -12f));

            var desc = UITheme.Label(card.transform, "Desc", product.description, UITheme.SmallSize,
                UITheme.Dim, TextAnchor.UpperLeft);
            UITheme.Anchor((RectTransform)desc.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(88f, 10f), new Vector2(-336f, -48f));

            bool owned = IsProductOwned(product);
            int level = product.category == "upgrade"
                ? _app.Profile.PremiumUpgradeLevel(product.upgradeId)
                : 0;
            bool maxed = product.category == "upgrade" && product.maxLevel > 0 && level >= product.maxLevel;
            string label;
            bool interactable = true;
            if (maxed)
            {
                label = "MAX LEVEL";
                interactable = false;
            }
            else if (owned && product.category != "upgrade")
            {
                label = "OWNED";
                interactable = false;
            }
            else
            {
                label = "\u25C6 " + UITheme.FormatNumber(product.priceGems) +
                        (product.category == "upgrade" ? "  \u00B7  LVL " + (level + 1) : "");
                if (_app.Premium == null || !_app.Premium.Online)
                {
                    label = "STORE OFFLINE";
                    interactable = false;
                }
            }
            var buy = UITheme.Button(card.transform, "Buy", label,
                maxed || (owned && product.category != "upgrade") ? UITheme.Muted : UITheme.Magenta,
                () => RequestProduct(product), UITheme.BodySize);
            buy.interactable = interactable;
            UITheme.Anchor((RectTransform)buy.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-316f, -28f), new Vector2(-24f, 28f));
        }

        private bool IsProductOwned(PremiumProduct product)
        {
            if (product == null) return false;
            if (product.category == "character" && !string.IsNullOrEmpty(product.characterId))
                return _app.Premium != null && _app.Premium.OwnsCharacter(product.characterId);
            var owned = _app.Profile.FindPremiumOwnership(product.id);
            return owned != null && (product.category != "upgrade" || owned.level > 0);
        }

        private void RequestProduct(PremiumProduct product)
        {
            if (_purchaseBusy || product == null) return;
            _app.Audio.PlayUiClick();
            int level = product.category == "upgrade"
                ? _app.Profile.PremiumUpgradeLevel(product.upgradeId)
                : 0;
            string detail = product.description;
            if (product.category == "upgrade")
                detail += "\nCurrent level " + level + " / " + product.maxLevel +
                          "  \u2192  next level " + (level + 1);
            PurchaseKit.ConfirmGems(_app, product.name, detail, product.priceGems,
                () => BuyProduct(product), "PURCHASE");
        }

        private void BuyProduct(PremiumProduct product)
        {
            if (_purchaseBusy) return;
            var premium = _app.Premium;
            if (premium == null || product == null) return;
            _purchaseBusy = true;
            _ui.Toast("AUTHORIZING PURCHASE...", UITheme.Cyan, 1.8f);
            premium.Purchase(product.id, PremiumService.NewRequestId(), result =>
            {
                _purchaseBusy = false;
                if (this == null) return;
                _app.Audio.PlaySfx("sfx_purchase", 0.9f);
                string suffix = product.category == "upgrade" ? " \u00B7 LVL " + result.level : "";
                _ui.Toast(product.name + " UNLOCKED" + suffix, UITheme.Lime, 3f);
                Reload(GemShopTab.Store);
            }, error =>
            {
                _purchaseBusy = false;
                if (this == null) return;
                _ui.Toast(error != null ? error.Friendly : "PURCHASE FAILED", UITheme.Red, 2.8f);
            });
        }

        // ---- Transaction history ----------------------------------------------------------

        private void LoadTransactions()
        {
            var premium = _app.Premium;
            if (premium == null || !premium.Online)
            {
                _transactionsLoaded = true;
                _transactionsLoading = false;
                BuildTransactionList();
                return;
            }
            _transactionsLoading = true;
            BuildTransactionList();
            premium.FetchTransactions(list =>
            {
                _transactionsLoading = false;
                if (this == null) return;
                _transactionsLoaded = true;
                _transactions = list ?? new List<StoreTransaction>();
                BuildTransactionList();
            }, error =>
            {
                _transactionsLoading = false;
                if (this == null) return;
                _transactionsLoaded = true;
                BuildTransactionList();
                _ui.Toast(error != null ? error.Friendly : "TRANSACTION HISTORY UNAVAILABLE",
                    UITheme.Red, 2.6f);
            });
        }

        private void BuildTransactionList()
        {
            Clear(_ordersContent);
            var heading = UITheme.Label(_ordersContent, "Transactions Title", "TRANSACTION HISTORY",
                UITheme.SmallSize, UITheme.Violet, TextAnchor.MiddleLeft, FontStyle.Bold);
            heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            var actions = UITheme.NewRect(_ordersContent, "Transaction Actions");
            actions.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            var refresh = UITheme.Button(actions, "Refresh Transactions", "REFRESH", UITheme.Violet,
                () => { _transactionsLoaded = false; LoadTransactions(); }, UITheme.SmallSize);
            UITheme.Anchor((RectTransform)refresh.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(180f, 0f));
            var buyMore = UITheme.Button(actions, "Buy More", "BUY GEMS", UITheme.Amber,
                () => { _app.Audio.PlayUiClick(); SelectTab(GemShopTab.Packages); }, UITheme.SmallSize);
            UITheme.Anchor((RectTransform)buyMore.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(192f, 0f), new Vector2(372f, 0f));

            var premium = _app.Premium;
            if (premium == null || !premium.Online)
            {
                var offline = UITheme.Label(_ordersContent, "Offline",
                    "STORE OFFLINE\nTransaction history needs the premium service. The cached " +
                    "wallet is shown for convenience only and is never treated as authority.",
                    UITheme.BodySize, UITheme.Red, TextAnchor.MiddleCenter, FontStyle.Bold);
                offline.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
                return;
            }

            if (_transactionsLoading)
            {
                var loading = UITheme.Label(_ordersContent, "Loading", "LOADING TRANSACTIONS...",
                    UITheme.BodySize, UITheme.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                loading.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
                return;
            }

            if (_transactions.Count == 0)
            {
                var empty = UITheme.Label(_ordersContent, "No Transactions",
                    "NO TRANSACTIONS YET\nGEM purchases appear here after the payment provider " +
                    "verifies them. Gameplay awards CREDITS, never GEMS.",
                    UITheme.BodySize, UITheme.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
                return;
            }

            for (int i = 0; i < _transactions.Count; i++)
            {
                var transaction = _transactions[i];
                if (transaction == null) continue;
                var card = UITheme.GlassCard(_ordersContent, "Transaction " + transaction.id,
                    new Color(0.035f, 0.05f, 0.09f, 0.94f)).transform.parent as RectTransform;
                UITheme.Layout(card.gameObject, 116f, -1f, 116f);

                Color statusColor = transaction.status == "SUCCEEDED" ? UITheme.Lime
                    : transaction.status == "REFUNDED" || transaction.status == "PARTIALLY_REFUNDED"
                        ? UITheme.Amber
                        : transaction.status == "FAILED" ? UITheme.Red
                        : UITheme.Amber;
                var status = UITheme.Label(card.transform, "Status", transaction.StatusLabel,
                    UITheme.SmallSize, statusColor, TextAnchor.MiddleRight, FontStyle.Bold);
                UITheme.Anchor((RectTransform)status.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-280f, -42f), new Vector2(-24f, -12f));

                var name = UITheme.Label(card.transform, "Name", PackageName(transaction.packageId),
                    UITheme.HeadingSize, UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(24f, -46f), new Vector2(-292f, -12f));

                string detail = transaction.ProviderLabel + "  \u00B7  " +
                                UITheme.FormatNumber(transaction.gems) + " GEMS  \u00B7  " +
                                transaction.AmountLabel;
                var meta = UITheme.Label(card.transform, "Meta", detail, UITheme.SmallSize, UITheme.Dim,
                    TextAnchor.MiddleLeft);
                UITheme.Anchor((RectTransform)meta.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(24f, -76f), new Vector2(-292f, -50f));

                string reference = !string.IsNullOrEmpty(transaction.providerRef)
                    ? transaction.providerRef
                    : transaction.eventId;
                string receipt = string.IsNullOrEmpty(transaction.receiptEmail)
                    ? "NO RECEIPT EMAIL"
                    : "RECEIPT " + transaction.receiptEmail;
                var note = UITheme.Label(card.transform, "Note",
                    FormatTransactionDate(transaction.createdUtc) + "  \u00B7  " + reference +
                    "  \u00B7  " + receipt,
                    UITheme.SmallSize, UITheme.Muted, TextAnchor.MiddleLeft);
                UITheme.Anchor((RectTransform)note.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(24f, 12f), new Vector2(-292f, 36f));
            }
        }

        private string PackageName(string packageId)
        {
            var catalog = _app.Premium != null ? _app.Premium.Catalog : null;
            var packages = catalog != null ? catalog.packages : null;
            if (packages != null)
            {
                for (int i = 0; i < packages.Length; i++)
                {
                    if (packages[i] != null && packages[i].id == packageId)
                        return packages[i].name;
                }
            }
            return string.IsNullOrEmpty(packageId) ? "GEM PACKAGE" : packageId;
        }

        private static string FormatTransactionDate(string utc)
        {
            if (string.IsNullOrEmpty(utc)) return "DATE UNKNOWN";
            DateTime parsed;
            if (!DateTime.TryParse(utc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out parsed))
                return utc;
            return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        // ---- Utility ----------------------------------------------------------------------

        private void Reload(GemShopTab tab)
        {
            _app.UI.ShowScreen(Build(_app, _ui, tab));
        }

        private static void Clear(RectTransform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);
        }
    }
}
