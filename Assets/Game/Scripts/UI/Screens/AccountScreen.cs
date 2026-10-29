using System;
using NeonVelocity.Account;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Registered-account console: email/password sign-in, guest linking, phone OTP, recovery,
    /// profile and receipt email management, cloud sync and provider capability status.
    ///
    /// The screen never edits GEMS. It adopts the bearer token issued by the backend, while
    /// PremiumService remains the only owner of the server-authoritative wallet. Platform sign-in
    /// buttons report the backend/provider boundary explicitly until native plugins and provider
    /// credentials are supplied; they never fabricate a local identity.
    /// </summary>
    public sealed class AccountScreen : MonoBehaviour
    {
        public const int TabSignIn = 0;
        public const int TabCreate = 1;
        public const int TabRecovery = 2;
        public const int TabProfile = 3;

        private GameApp _app;
        private UIRoot _ui;
        private int _tab;

        private Text _sessionStatus;
        private Text _serviceStatus;
        private Text _deliveryInfo;
        private Text _revisionInfo;
        private Text _syncInfo;
        private Text _phoneStatus;
        private readonly System.Collections.Generic.List<Button> _tabButtons = new System.Collections.Generic.List<Button>(4);
        private readonly System.Collections.Generic.List<Text> _tabLabels = new System.Collections.Generic.List<Text>(4);

        private InputField _signInEmail;
        private InputField _signInPassword;
        private InputField _createName;
        private InputField _createEmail;
        private InputField _createPassword;
        private InputField _phoneNumber;
        private InputField _phoneCode;
        private string _challengeId = "";
        private InputField _resetEmail;
        private InputField _resetToken;
        private InputField _resetPassword;
        private InputField _verifyToken;
        private InputField _profileName;
        private InputField _receiptEmail;
        private InputField _changeEmail;
        private InputField _profileVerifyToken;

        private AccountService Account => _app != null ? _app.Account : null;

        public static GameObject Build(GameApp app, UIRoot ui, int tab = 0)
        {
            var root = ScreenKit.Root("Account");
            var screen = root.gameObject.AddComponent<AccountScreen>();
            screen._app = app;
            screen._ui = ui;
            screen._tab = Mathf.Clamp(tab, 0, 3);
            screen.BuildLayout();
            return root.gameObject;
        }

        private void OnDestroy()
        {
            if (_app != null && _app.Account != null) _app.Account.Changed -= RefreshStatus;
        }

        private void BuildLayout()
        {
            var root = (RectTransform)transform;
            ScreenKit.Backdrop(root, UITheme.Cyan);
            ScreenKit.Header(root, _app, "ACCOUNT", "RUNNER IDENTITY",
                "Email, phone, provider sign-in, cloud sync and verified receipts.",
                UITheme.Cyan, _app.NavigateBack);

            BuildTabs(root);
            BuildStatusStrip(root);

            UITheme.Scroll(root, "Account Scroll", out RectTransform content);
            var scroll = root.Find("Account Scroll");
            UITheme.Anchor((RectTransform)scroll, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(36f, 44f), new Vector2(-36f, -270f));
            BuildTab(content);
            ScreenKit.Footer(root, "OFFLINE PLAY STAYS AVAILABLE  ·  GEMS ARE ALWAYS SERVER-AUTHORITATIVE");

            if (Account != null)
            {
                Account.Changed -= RefreshStatus;
                Account.Changed += RefreshStatus;
            }
            RefreshStatus();
            RefreshPaymentCapabilities(false);
        }

        private void BuildTabs(RectTransform root)
        {
            var bar = UITheme.Img(root, "Tabs", new Color(0.02f, 0.04f, 0.075f, 0.5f));
            UITheme.Anchor((RectTransform)bar.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(36f, -216f), new Vector2(-36f, -158f));
            string[] labels = { "SIGN IN", "CREATE ACCOUNT", "RECOVERY", "PROFILE" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var button = UITheme.Button(bar.transform, "Tab " + labels[i], labels[i], UITheme.Cyan,
                    () => SelectTab(index), UITheme.BodySize);
                UITheme.Anchor((RectTransform)button.transform,
                    new Vector2(i / 4f, 0f), new Vector2((i + 1) / 4f, 1f),
                    new Vector2(6f, 8f), new Vector2(-6f, 8f));
                _tabButtons.Add(button);
                _tabLabels.Add(button.GetComponentInChildren<Text>());
            }
            ApplyTabColors();
        }

        private void BuildStatusStrip(RectTransform root)
        {
            var strip = UITheme.Img(root, "Status Strip", new Color(0.035f, 0.065f, 0.11f, 0.72f));
            UITheme.Anchor((RectTransform)strip.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(36f, -260f), new Vector2(-36f, -222f));

            _sessionStatus = UITheme.Label(strip.transform, "Session", "", UITheme.SmallSize,
                UITheme.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_sessionStatus.transform, new Vector2(0f, 0f), new Vector2(0.65f, 1f),
                new Vector2(18f, 0f), new Vector2(-6f, 0f));

            _serviceStatus = UITheme.Label(strip.transform, "Service", "", UITheme.SmallSize,
                UITheme.Dim, TextAnchor.MiddleRight);
            UITheme.Anchor((RectTransform)_serviceStatus.transform, new Vector2(0.55f, 0f), new Vector2(1f, 1f),
                new Vector2(6f, 0f), new Vector2(-18f, 0f));
        }

        private void SelectTab(int tab)
        {
            _app.Audio.PlayUiClick();
            if (_tab == tab) return;
            _tab = Mathf.Clamp(tab, 0, 3);
            ApplyTabColors();
            var root = (RectTransform)transform;
            var scroll = root.Find("Account Scroll");
            if (scroll == null) return;
            var content = scroll.Find("Viewport") != null
                ? scroll.Find("Viewport").Find("Content") as RectTransform : null;
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);
            BuildTab(content);
            RefreshStatus();
        }

        private void ApplyTabColors()
        {
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                bool active = i == _tab;
                var image = _tabButtons[i].targetGraphic as Image;
                if (image != null)
                    image.color = active ? UITheme.Cyan.Alpha(0.85f) : UITheme.PanelBright;
                if (i < _tabLabels.Count && _tabLabels[i] != null)
                    _tabLabels[i].color = active ? UITheme.White : UITheme.Dim;
            }
        }

        private void BuildTab(RectTransform content)
        {
            ClearInputs();
            switch (_tab)
            {
                case TabCreate:
                    BuildCreate(content);
                    break;
                case TabRecovery:
                    BuildRecovery(content);
                    break;
                case TabProfile:
                    BuildProfile(content);
                    break;
                default:
                    BuildSignIn(content);
                    break;
            }
        }

        private void ClearInputs()
        {
            _signInEmail = null;
            _signInPassword = null;
            _createName = null;
            _createEmail = null;
            _createPassword = null;
            _phoneNumber = null;
            _phoneCode = null;
            _phoneStatus = null;
            _resetEmail = null;
            _resetToken = null;
            _resetPassword = null;
            _verifyToken = null;
            _profileName = null;
            _receiptEmail = null;
            _changeEmail = null;
            _profileVerifyToken = null;
            _deliveryInfo = null;
            _revisionInfo = null;
            _syncInfo = null;
        }

        // ---- Sign-in tab ------------------------------------------------------------------

        private void BuildSignIn(RectTransform content)
        {
            var card = AddCard(content, "Sign In Card", 400f, new Color(0.035f, 0.065f, 0.115f, 0.96f));
            float top = 14f;
            AddCardTitle(card, "EMAIL & PASSWORD", UITheme.Cyan, ref top);
            AddCardText(card, "Sign in to keep progression, discoveries and premium ownership synced. " +
                              "Offline play never requires an account.", UITheme.Dim, ref top, 40f);
            _signInEmail = AddField(card, "Sign In Email", "EMAIL", "runner@example.com", ref top,
                false, 128);
            _signInEmail.contentType = InputField.ContentType.EmailAddress;
            _signInPassword = AddField(card, "Sign In Password", "PASSWORD", "Your password", ref top,
                true, 128);

            AddBottomButton(card, "Sign In", "SIGN IN", UITheme.Cyan, SignIn, UITheme.BodySize,
                96f, 144f);
            AddBottomButton(card, "Forgot", "FORGOT PASSWORD?", UITheme.Muted,
                () => SelectTab(TabRecovery), UITheme.SmallSize, 44f, 84f, 0f, 0.48f);
            AddBottomButton(card, "Create Instead", "CREATE ACCOUNT", UITheme.Cyan,
                () => SelectTab(TabCreate), UITheme.SmallSize, 44f, 84f, 0.52f, 1f);

            var platform = AddCard(content, "Platform Card", 214f, new Color(0.035f, 0.05f, 0.09f, 0.96f));
            top = 14f;
            AddCardTitle(platform, "PLATFORM SIGN-IN", UITheme.Violet, ref top);
            AddCardText(platform,
                "Google identity verification is implemented on the server. Native Google/Apple " +
                "sign-in plugins and provider credentials are not configured in this build, so no " +
                "token can be minted locally.", UITheme.Dim, ref top, 64f);
            AddBottomButton(platform, "Google", "GOOGLE", UITheme.Cyan,
                () => ExplainProvider("GOOGLE"), UITheme.BodySize, 24f, 72f, 0f, 0.48f);
            AddBottomButton(platform, "Apple", "APPLE", UITheme.White,
                () => ExplainProvider("APPLE"), UITheme.BodySize, 24f, 72f, 0.52f, 1f);

            var guest = AddCard(content, "Guest Card", 214f, new Color(0.035f, 0.05f, 0.085f, 0.96f));
            top = 14f;
            AddCardTitle(guest, "GUEST SESSION", UITheme.Amber, ref top);
            string guestCopy = Account != null && Account.SignedIn
                ? "You are signed in. Guest mode is only for local play and can be linked from the " +
                  "CREATE ACCOUNT tab."
                : "Guest play stores progression on this device. Register later to keep it across " +
                  "devices; GEMS are never included in a local save.";
            AddCardText(guest, guestCopy, UITheme.Dim, ref top, 58f);
            AddBottomButton(guest, "Guest Action",
                Account != null && Account.SignedIn ? "VIEW PROFILE" : "CONTINUE OFFLINE",
                UITheme.Amber,
                Account != null && Account.SignedIn ? (Action)(() => SelectTab(TabProfile)) : ContinueOffline,
                UITheme.BodySize, 24f, 72f);
        }

        private void ContinueOffline()
        {
            _app.Audio.PlayUiClick();
            if (Account != null && Account.Guest)
            {
                _ui.Toast("GUEST SESSION ACTIVE - PLAY OFFLINE OR LINK AN ACCOUNT", UITheme.Amber, 3f);
                return;
            }
            _ui.Toast("OFFLINE PLAY IS ALWAYS AVAILABLE - SIGN IN ANY TIME", UITheme.Cyan, 3f);
            SelectTab(TabCreate);
        }

        private void ExplainProvider(string provider)
        {
            _app.Audio.PlayUiClick();
            _ui.ShowChoiceModal(provider + " SIGN-IN",
                "The server verifies Google ID tokens when NV_GOOGLE_CLIENT_ID is configured. " +
                "Apple token verification remains an explicit disabled boundary until an Apple " +
                "developer account supplies a service id, key id and private key.\n\n" +
                "This build has no native provider plugin, so the game will not fabricate an " +
                "identity or accept an unverified token. Use email, phone OTP or guest mode now.",
                "CREATE ACCOUNT", () => SelectTab(TabCreate),
                "PHONE OTP", () => SelectTab(TabCreate),
                "CLOSE", null);
        }

        // ---- Create tab -------------------------------------------------------------------

        private void BuildCreate(RectTransform content)
        {
            var card = AddCard(content, "Register Card", 520f, new Color(0.035f, 0.065f, 0.115f, 0.96f));
            float top = 14f;
            AddCardTitle(card, "CREATE ACCOUNT", UITheme.Lime, ref top);
            AddCardText(card, "Use 10-128 characters with at least one letter and one number. " +
                              "Registering links this device's local progress to the account.", UITheme.Dim, ref top, 44f);
            _createName = AddField(card, "Runner Name", "RUNNER NAME", "RUNNER-01", ref top, false, 20);
            _createEmail = AddField(card, "Create Email", "EMAIL", "runner@example.com", ref top, false, 128);
            _createEmail.contentType = InputField.ContentType.EmailAddress;
            _createPassword = AddField(card, "Create Password", "PASSWORD", "Minimum 10 characters", ref top,
                true, 128);
            AddCardText(card, "A verification message is sent only when SMTP is configured. The server " +
                              "never claims an email was delivered when it was not.", UITheme.Muted, ref top, 46f);
            AddBottomButton(card, "Create", "CREATE ACCOUNT", UITheme.Lime, CreateAccount,
                UITheme.BodySize, 76f, 124f, 0f, 0.55f);
            AddBottomButton(card, "Link Guest", "LINK GUEST", UITheme.Cyan, LinkGuest,
                UITheme.BodySize, 76f, 124f, 0.57f, 1f);
            AddBottomButton(card, "Sign In Instead", "ALREADY HAVE AN ACCOUNT? SIGN IN", UITheme.Muted,
                () => SelectTab(TabSignIn), UITheme.SmallSize, 24f, 64f);

            var phone = AddCard(content, "Phone Card", 470f, new Color(0.035f, 0.055f, 0.095f, 0.96f));
            top = 14f;
            AddCardTitle(phone, "PHONE OTP", UITheme.Amber, ref top);
            AddCardText(phone,
                "Phone sign-in uses a one-time code and is available when NV_SMS_WEBHOOK is configured. " +
                "No SMS is sent otherwise.", UITheme.Dim, ref top, 44f);
            _phoneNumber = AddField(phone, "Phone Number", "PHONE", "+94 77 123 4567", ref top, false, 32);
            _phoneNumber.contentType = InputField.ContentType.Standard;
            AddTopButton(phone, "Send Code", "SEND CODE", UITheme.Amber, SendPhoneCode, ref top, 190f);
            _phoneCode = AddField(phone, "OTP Code", "ONE-TIME CODE", "6 digit code", ref top, false, 12);
            AddTopButton(phone, "Verify Code", "VERIFY & SIGN IN", UITheme.Lime, VerifyPhoneCode, ref top, 330f);
            _phoneStatus = AddCardText(phone, "SMS delivery status appears here after a request.",
                UITheme.Muted, ref top, 58f);
        }

        // ---- Recovery tab -----------------------------------------------------------------

        private void BuildRecovery(RectTransform content)
        {
            var request = AddCard(content, "Reset Request Card", 250f,
                new Color(0.035f, 0.055f, 0.095f, 0.96f));
            float top = 14f;
            AddCardTitle(request, "PASSWORD RESET", UITheme.Amber, ref top);
            AddCardText(request, "Request a reset code. The same response is returned for unknown " +
                                 "addresses so the endpoint cannot enumerate accounts.",
                UITheme.Dim, ref top, 44f);
            _resetEmail = AddField(request, "Reset Email", "ACCOUNT EMAIL", "runner@example.com",
                ref top, false, 128);
            _resetEmail.contentType = InputField.ContentType.EmailAddress;
            AddBottomButton(request, "Request Reset", "SEND RESET CODE", UITheme.Amber,
                RequestReset, UITheme.BodySize, 24f, 72f);

            var reset = AddCard(content, "Reset Card", 340f, new Color(0.035f, 0.065f, 0.115f, 0.96f));
            top = 14f;
            AddCardTitle(reset, "SET A NEW PASSWORD", UITheme.Cyan, ref top);
            AddCardText(reset, "Paste the reset code from the email. A successful reset revokes every " +
                               "existing session for the account.", UITheme.Dim, ref top, 40f);
            _resetToken = AddField(reset, "Reset Token", "RESET CODE", "Code from the email", ref top,
                false, 512);
            _resetPassword = AddField(reset, "Reset Password", "NEW PASSWORD", "Minimum 10 characters",
                ref top, true, 128);
            AddBottomButton(reset, "Reset", "RESET PASSWORD", UITheme.Cyan, ResetPassword,
                UITheme.BodySize, 24f, 72f);

            var verify = AddCard(content, "Verify Card", 280f, new Color(0.035f, 0.06f, 0.11f, 0.96f));
            top = 14f;
            AddCardTitle(verify, "VERIFY EMAIL", UITheme.Lime, ref top);
            AddCardText(verify, "Verification is required before a receipt can be emailed. Enter the " +
                                "code from the verification message.", UITheme.Dim, ref top, 40f);
            _verifyToken = AddField(verify, "Verify Token", "VERIFICATION CODE", "Code from the email",
                ref top, false, 512);
            AddBottomButton(verify, "Verify", "VERIFY EMAIL", UITheme.Lime, VerifyEmail,
                UITheme.BodySize, 24f, 72f);

            BuildDeliveryCard(content);
        }

        private void BuildDeliveryCard(RectTransform content)
        {
            var card = AddCard(content, "Delivery Card", 330f, new Color(0.035f, 0.055f, 0.09f, 0.96f));
            float top = 14f;
            AddCardTitle(card, "LAST SECURITY MESSAGE", UITheme.Violet, ref top);
            var delivery = Account != null ? Account.LastDelivery : null;
            string channel = delivery != null && !string.IsNullOrEmpty(delivery.channel)
                ? delivery.channel.ToUpperInvariant() : "NONE";
            string state = delivery == null
                ? "No message has been requested in this session."
                : (delivery.delivered
                    ? "Delivered through " + channel + "."
                    : "Not delivered: " + (string.IsNullOrEmpty(delivery.reason) ? "UNKNOWN" : delivery.reason));
            _deliveryInfo = AddCardText(card, state + "\nEmail requires SMTP_HOST/FROM; SMS requires " +
                "NV_SMS_WEBHOOK. Development tokens are never shown in production.", UITheme.Dim, ref top, 62f);
            float buttonTop = top + 4f;
            if (delivery != null && !string.IsNullOrEmpty(delivery.devVerificationToken))
            {
                string token = delivery.devVerificationToken;
                AddTopButton(card, "Copy Verify", "COPY DEV VERIFICATION CODE", UITheme.Lime,
                    () => Copy(token, "VERIFICATION CODE COPIED"), ref top, buttonTop, 0f, 0.56f);
                buttonTop = top + 6f;
            }
            if (delivery != null && !string.IsNullOrEmpty(delivery.devResetToken))
            {
                string token = delivery.devResetToken;
                AddTopButton(card, "Copy Reset", "COPY DEV RESET CODE", UITheme.Amber,
                    () => Copy(token, "RESET CODE COPIED"), ref top, buttonTop, 0.58f, 1f);
                buttonTop = top + 6f;
            }
            if (delivery != null && !string.IsNullOrEmpty(delivery.devCode))
            {
                string code = delivery.devCode;
                AddTopButton(card, "Copy OTP", "COPY DEV SMS CODE", UITheme.Cyan,
                    () => Copy(code, "SMS CODE COPIED"), ref top, buttonTop, 0f, 0.56f);
            }
        }

        // ---- Profile tab ------------------------------------------------------------------

        private void BuildProfile(RectTransform content)
        {
            var account = Account;
            if (account == null)
            {
                AddEmptyCard(content, "ACCOUNT SERVICE UNAVAILABLE");
                return;
            }

            bool signedIn = account.SignedIn;
            var identity = AddCard(content, "Identity Card", 318f, new Color(0.035f, 0.065f, 0.115f, 0.96f));
            float top = 14f;
            AddCardTitle(identity, signedIn ? "ACCOUNT IDENTITY" : "LOCAL PROFILE", UITheme.Cyan, ref top);
            AddInfoLine(identity, "Status", signedIn ? "SIGNED IN" : (account.Guest ? "GUEST" : "LOCAL ONLY"),
                signedIn ? UITheme.Lime : UITheme.Amber, ref top);
            AddInfoLine(identity, "Email",
                string.IsNullOrEmpty(account.Email) ? "NOT SET" : account.Email +
                (account.EmailVerified ? "  ·  VERIFIED" : "  ·  UNVERIFIED"),
                account.EmailVerified ? UITheme.Lime : UITheme.Dim, ref top);
            AddInfoLine(identity, "Phone",
                string.IsNullOrEmpty(account.Phone) ? "NOT SET" : account.Phone,
                account.PhoneVerified ? UITheme.Lime : UITheme.Dim, ref top);
            AddInfoLine(identity, "Provider",
                string.IsNullOrEmpty(account.Provider) ? "EMAIL / GUEST" : account.Provider.ToUpperInvariant(),
                UITheme.White, ref top);
            AddInfoLine(identity, "Receipt email",
                string.IsNullOrEmpty(account.ReceiptEmail) ? "NOT SET" : account.ReceiptEmail,
                account.EmailVerified ? UITheme.Amber : UITheme.Muted, ref top);
            AddInfoLine(identity, "Account ID",
                string.IsNullOrEmpty(account.AccountId) ? "LOCAL" : account.AccountId,
                UITheme.Muted, ref top);

            var edit = AddCard(content, "Profile Edit Card", 330f, new Color(0.035f, 0.055f, 0.095f, 0.96f));
            top = 14f;
            AddCardTitle(edit, "PROFILE & RECEIPT ADDRESS", UITheme.Amber, ref top);
            AddCardText(edit, "The authenticated email is the default receipt address. A changed " +
                              "address is saved immediately but receipts are emailed only to a " +
                              "verified account email.", UITheme.Dim, ref top, 44f);
            _profileName = AddField(edit, "Profile Name", "RUNNER NAME", "RUNNER-01", ref top, false, 20);
            _profileName.text = account.DisplayName ?? "";
            _receiptEmail = AddField(edit, "Receipt Email", "RECEIPT EMAIL", "receipts@example.com",
                ref top, false, 128);
            _receiptEmail.contentType = InputField.ContentType.EmailAddress;
            _receiptEmail.text = account.ReceiptEmail ?? "";
            AddTopButton(edit, "Save Profile", "SAVE PROFILE", UITheme.Amber, SaveProfile, ref top, 260f);

            var change = AddCard(content, "Change Email Card", 250f, new Color(0.035f, 0.05f, 0.09f, 0.96f));
            top = 14f;
            AddCardTitle(change, "CHANGE ACCOUNT EMAIL", UITheme.Violet, ref top);
            AddCardText(change, "Changing the email resets verification. Sign-in and receipts stay " +
                                "paused for receipts until the new address is verified.", UITheme.Dim, ref top, 40f);
            _changeEmail = AddField(change, "Change Email", "NEW EMAIL", "new@example.com", ref top,
                false, 128);
            _changeEmail.contentType = InputField.ContentType.EmailAddress;
            AddBottomButton(change, "Change Email", "CHANGE EMAIL", UITheme.Violet, ChangeEmail,
                UITheme.BodySize, 24f, 72f);

            var verify = AddCard(content, "Profile Verify Card", 262f,
                new Color(0.035f, 0.06f, 0.10f, 0.96f));
            top = 14f;
            AddCardTitle(verify, "VERIFY CONTACT", UITheme.Lime, ref top);
            AddCardText(verify, "Paste the latest verification code. Check the RECOVERY tab for " +
                                "development codes when email delivery is not configured.",
                UITheme.Dim, ref top, 40f);
            _profileVerifyToken = AddField(verify, "Profile Verify Token", "VERIFICATION CODE",
                "Code from the email", ref top, false, 512);
            AddBottomButton(verify, "Profile Verify", "VERIFY EMAIL", UITheme.Lime, VerifyEmail,
                UITheme.BodySize, 24f, 72f);

            BuildSyncCard(content);
            BuildPaymentCard(content);
            BuildSessionCard(content);
        }

        private void BuildSyncCard(RectTransform content)
        {
            var card = AddCard(content, "Sync Card", 306f, new Color(0.035f, 0.06f, 0.10f, 0.96f));
            float top = 14f;
            AddCardTitle(card, "CLOUD PROGRESS SYNC", UITheme.Lime, ref top);
            _revisionInfo = AddInfoLine(card, "Revision",
                Account != null ? Account.SyncRevision.ToString() : "0", UITheme.White, ref top);
            _syncInfo = AddInfoLine(card, "Last sync",
                Account == null || string.IsNullOrEmpty(Account.LastSyncSummary)
                    ? "NEVER" : Account.LastSyncSummary, UITheme.Muted, ref top);
            string error = Account != null ? Account.LastSyncError : "";
            AddCardText(card, string.IsNullOrEmpty(error)
                ? "Local and cloud progress merge by maximum scalar progress. Reward IDs are unioned " +
                  "so a replayed sync cannot duplicate a reward. GEMS and tokens are excluded."
                : "LAST SYNC ERROR  " + error,
                string.IsNullOrEmpty(error) ? UITheme.Dim : UITheme.Red, ref top, 58f);
            bool canSync = Account != null && Account.CanSync;
            AddTopButton(card, "Sync Now", canSync ? "SYNC NOW" : "SIGN IN TO SYNC", UITheme.Lime,
                SyncNow, ref top, 226f, 0f, 0.48f);
            AddTopButton(card, "Pull Cloud", "PULL CLOUD", UITheme.Cyan, PullCloud,
                ref top, 226f, 0.52f, 1f);
        }

        private void BuildPaymentCard(RectTransform content)
        {
            var card = AddCard(content, "Payment Card", 360f, new Color(0.04f, 0.055f, 0.085f, 0.96f));
            float top = 14f;
            AddCardTitle(card, "PAYMENT & RECEIPT STATUS", UITheme.Magenta, ref top);
            var payments = Account != null ? Account.Payments : null;
            AddInfoLine(card, "Stripe card", payments != null && payments.stripe ? "CONFIGURED" : "NOT CONFIGURED",
                payments != null && payments.stripe ? UITheme.Lime : UITheme.Muted, ref top);
            AddInfoLine(card, "Apple IAP", payments != null && payments.apple ? "CONFIGURED" : "DISABLED BOUNDARY",
                payments != null && payments.apple ? UITheme.Lime : UITheme.Muted, ref top);
            AddInfoLine(card, "Google Play", payments != null && payments.googlePlay ? "CONFIGURED" : "DISABLED BOUNDARY",
                payments != null && payments.googlePlay ? UITheme.Lime : UITheme.Muted, ref top);
            AddInfoLine(card, "Bank transfer",
                payments != null && payments.manualBankTransfer ? "LEGACY · ENABLED BY OPERATOR" : "DISABLED",
                payments != null && payments.manualBankTransfer ? UITheme.Amber : UITheme.Muted, ref top);
            AddCardText(card, "Mobile purchases use the official App Store or Google Play sheet; desktop " +
                              "opens the provider's hosted checkout. GEMS are credited only after the " +
                              "server verifies the purchase. Receipts go to your saved account email; " +
                              "no card data ever enters the game.", UITheme.Dim, ref top, 58f);
            AddTopButton(card, "Refresh Payments", "REFRESH CAPABILITIES", UITheme.Cyan,
                () => RefreshPaymentCapabilities(true), ref top, 282f, 0f, 0.52f);
            AddTopButton(card, "Open Gem Shop", "GEM SHOP", UITheme.Amber,
                () => _app.ShowGemShop(), ref top, 282f, 0.54f, 1f);
        }

        private void BuildSessionCard(RectTransform content)
        {
            var card = AddCard(content, "Session Card", 224f, new Color(0.04f, 0.045f, 0.07f, 0.96f));
            float top = 14f;
            AddCardTitle(card, "SESSION", UITheme.Red, ref top);
            bool signedIn = Account != null && Account.SignedIn;
            AddCardText(card, signedIn
                ? "Signing out clears the cached wallet and premium ownership on this device. Local " +
                  "progression remains and offline play continues. The token is revoked server-side."
                : "A guest session keeps local progression. Ending it creates a fresh guest identity " +
                  "on the next store sync; local progression is preserved.",
                UITheme.Dim, ref top, 58f);
            AddBottomButton(card, "Logout", signedIn ? "SIGN OUT" : "END GUEST SESSION",
                UITheme.Red, ConfirmLogout, UITheme.BodySize, 24f, 72f);
        }

        private void AddEmptyCard(RectTransform content, string text)
        {
            var card = AddCard(content, "Empty", 160f, new Color(0.035f, 0.05f, 0.09f, 0.96f));
            var label = UITheme.Label(card.transform, "Text", text, UITheme.BodySize, UITheme.Red,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Stretch((RectTransform)label.transform, 24f, 24f, 24f, 24f);
        }

        // ---- Actions ----------------------------------------------------------------------

        private void SignIn()
        {
            if (Account == null) { ToastOffline(); return; }
            string email = _signInEmail != null ? _signInEmail.text.Trim() : "";
            string password = _signInPassword != null ? _signInPassword.text : "";
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                _ui.Toast("ENTER YOUR EMAIL AND PASSWORD", UITheme.Red, 2.6f);
                return;
            }
            _ui.Toast("SIGNING IN...", UITheme.Cyan, 1.6f);
            Account.Login(email, password, ok =>
            {
                if (this == null) return;
                if (!ok) { ShowError(Account.LastError); return; }
                _app.Audio.PlaySfx("sfx_purchase", 0.7f);
                _ui.Toast("SIGNED IN - CLOUD SYNC STARTED", UITheme.Lime, 3f);
                Rebuild(TabProfile);
            });
        }

        private void CreateAccount()
        {
            if (Account == null) { ToastOffline(); return; }
            string name = _createName != null ? _createName.text.Trim() : "";
            string email = _createEmail != null ? _createEmail.text.Trim() : "";
            string password = _createPassword != null ? _createPassword.text : "";
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                _ui.Toast("ENTER AN EMAIL AND PASSWORD", UITheme.Red, 2.6f);
                return;
            }
            _ui.Toast("CREATING ACCOUNT...", UITheme.Cyan, 1.6f);
            Account.Register(email, password, name, ok =>
            {
                if (this == null) return;
                if (!ok) { ShowError(Account.LastError); return; }
                _app.Audio.PlaySfx("sfx_purchase", 0.8f);
                _ui.Toast("ACCOUNT CREATED - VERIFY YOUR EMAIL", UITheme.Lime, 3.2f);
                Rebuild(TabProfile);
            });
        }

        private void LinkGuest()
        {
            if (Account == null) { ToastOffline(); return; }
            if (Account.SignedIn)
            {
                _ui.Toast("ALREADY SIGNED IN - SIGN OUT TO LINK ANOTHER ACCOUNT", UITheme.Amber, 3f);
                return;
            }
            string name = _createName != null ? _createName.text.Trim() : "";
            string email = _createEmail != null ? _createEmail.text.Trim() : "";
            string password = _createPassword != null ? _createPassword.text : "";
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                _ui.Toast("ENTER AN EMAIL AND PASSWORD TO LINK", UITheme.Red, 2.6f);
                return;
            }
            _ui.Toast("LINKING GUEST PROGRESS...", UITheme.Cyan, 1.6f);
            Account.LinkCredentials(email, password, name, ok =>
            {
                if (this == null) return;
                if (!ok) { ShowError(Account.LastError); return; }
                _ui.Toast("GUEST PROGRESS LINKED TO YOUR ACCOUNT", UITheme.Lime, 3.2f);
                Rebuild(TabProfile);
            });
        }

        private void SendPhoneCode()
        {
            if (Account == null) { ToastOffline(); return; }
            string phone = _phoneNumber != null ? _phoneNumber.text.Trim() : "";
            if (string.IsNullOrEmpty(phone))
            {
                _ui.Toast("ENTER A PHONE NUMBER", UITheme.Red, 2.6f);
                return;
            }
            _ui.Toast("REQUESTING SMS CODE...", UITheme.Cyan, 1.8f);
            Account.StartPhone(phone, challenge =>
            {
                if (this == null) return;
                if (challenge == null)
                {
                    ShowError(Account.LastError);
                    return;
                }
                _challengeId = challenge.challengeId ?? "";
                string detail = challenge.delivery != null && challenge.delivery.delivered
                    ? "SMS CODE SENT - ENTER IT BELOW"
                    : "CODE NOT DELIVERED - " +
                      (challenge.delivery != null && !string.IsNullOrEmpty(challenge.delivery.reason)
                          ? challenge.delivery.reason : "SMS_NOT_CONFIGURED");
                if (challenge.delivery != null && !string.IsNullOrEmpty(challenge.delivery.devCode))
                    detail += "  ·  DEV CODE " + challenge.delivery.devCode;
                SetPhoneStatus(detail,
                    challenge.delivery != null && challenge.delivery.delivered ? UITheme.Lime : UITheme.Amber);
                _ui.Toast(detail, challenge.delivery != null && challenge.delivery.delivered
                    ? UITheme.Lime : UITheme.Amber, 3.6f);
            });
        }

        private void VerifyPhoneCode()
        {
            if (Account == null) { ToastOffline(); return; }
            if (string.IsNullOrEmpty(_challengeId))
            {
                _ui.Toast("REQUEST A CODE FIRST", UITheme.Red, 2.6f);
                return;
            }
            string code = _phoneCode != null ? _phoneCode.text.Trim() : "";
            if (string.IsNullOrEmpty(code))
            {
                _ui.Toast("ENTER THE ONE-TIME CODE", UITheme.Red, 2.6f);
                return;
            }
            _ui.Toast("VERIFYING CODE...", UITheme.Cyan, 1.6f);
            Account.VerifyPhone(_challengeId, code, "", ok =>
            {
                if (this == null) return;
                if (!ok) { ShowError(Account.LastError); return; }
                _challengeId = "";
                _ui.Toast("PHONE VERIFIED - SIGNED IN", UITheme.Lime, 3f);
                Rebuild(TabProfile);
            });
        }

        private void RequestReset()
        {
            if (Account == null) { ToastOffline(); return; }
            string email = _resetEmail != null ? _resetEmail.text.Trim() : "";
            if (string.IsNullOrEmpty(email))
            {
                _ui.Toast("ENTER YOUR ACCOUNT EMAIL", UITheme.Red, 2.6f);
                return;
            }
            _ui.Toast("REQUESTING RESET CODE...", UITheme.Cyan, 1.8f);
            Account.RequestPasswordReset(email, ok =>
            {
                if (this == null) return;
                if (!ok) { ShowError(Account.LastError); return; }
                var delivery = Account.LastDelivery;
                string detail = delivery != null && delivery.delivered
                    ? "RESET CODE SENT"
                    : "RESET REQUEST ACCEPTED - " +
                      (delivery != null && !string.IsNullOrEmpty(delivery.reason)
                          ? delivery.reason : "EMAIL_NOT_CONFIGURED");
                if (delivery != null && !string.IsNullOrEmpty(delivery.devResetToken))
                    detail += "  ·  DEV CODE AVAILABLE BELOW";
                _ui.Toast(detail, UITheme.Lime, 3.4f);
                Rebuild(TabRecovery);
            });
        }

        private void ResetPassword()
        {
            if (Account == null) { ToastOffline(); return; }
            string token = _resetToken != null ? _resetToken.text.Trim() : "";
            string password = _resetPassword != null ? _resetPassword.text : "";
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(password))
            {
                _ui.Toast("ENTER THE RESET CODE AND A NEW PASSWORD", UITheme.Red, 2.8f);
                return;
            }
            _ui.Toast("RESETTING PASSWORD...", UITheme.Cyan, 1.6f);
            Account.ResetPassword(token, password, ok =>
            {
                if (this == null) return;
                if (!ok) { ShowError(Account.LastError); return; }
                _ui.Toast("PASSWORD RESET - SIGN IN AGAIN", UITheme.Lime, 3f);
                Rebuild(TabSignIn);
            });
        }

        private void VerifyEmail()
        {
            if (Account == null) { ToastOffline(); return; }
            string token = _profileVerifyToken != null ? _profileVerifyToken.text.Trim() : "";
            if (string.IsNullOrEmpty(token) && _verifyToken != null) token = _verifyToken.text.Trim();
            if (string.IsNullOrEmpty(token))
            {
                _ui.Toast("ENTER THE VERIFICATION CODE", UITheme.Red, 2.6f);
                return;
            }
            _ui.Toast("VERIFYING EMAIL...", UITheme.Cyan, 1.6f);
            Account.VerifyEmail(token, ok =>
            {
                if (this == null) return;
                if (!ok) { ShowError(Account.LastError); return; }
                _ui.Toast("EMAIL VERIFIED - RECEIPTS ENABLED", UITheme.Lime, 3f);
                Rebuild(TabProfile);
            });
        }

        private void SaveProfile()
        {
            if (Account == null) { ToastOffline(); return; }
            string name = _profileName != null ? _profileName.text.Trim() : "";
            string receipt = _receiptEmail != null ? _receiptEmail.text.Trim() : "";
            _ui.Toast("SAVING PROFILE...", UITheme.Cyan, 1.4f);
            Account.UpdateProfile(name, receipt, ok =>
            {
                if (this == null) return;
                if (!ok) { ShowError(Account.LastError); return; }
                _ui.Toast("PROFILE UPDATED", UITheme.Lime, 2.4f);
                Rebuild(TabProfile);
            });
        }

        private void ChangeEmail()
        {
            if (Account == null) { ToastOffline(); return; }
            string email = _changeEmail != null ? _changeEmail.text.Trim() : "";
            if (string.IsNullOrEmpty(email))
            {
                _ui.Toast("ENTER A NEW EMAIL ADDRESS", UITheme.Red, 2.6f);
                return;
            }
            _ui.Toast("CHANGING EMAIL...", UITheme.Cyan, 1.6f);
            Account.ChangeEmail(email, ok =>
            {
                if (this == null) return;
                if (!ok) { ShowError(Account.LastError); return; }
                _ui.Toast("EMAIL CHANGED - VERIFY THE NEW ADDRESS", UITheme.Amber, 3.4f);
                Rebuild(TabRecovery);
            });
        }

        private void SyncNow()
        {
            if (Account == null) { ToastOffline(); return; }
            if (!Account.CanSync)
            {
                _ui.Toast("SIGN IN TO SYNC CLOUD PROGRESS", UITheme.Red, 2.6f);
                return;
            }
            _ui.Toast("SYNCING PROGRESS...", UITheme.Cyan, 1.4f);
            Account.SyncNow(ok =>
            {
                if (this == null) return;
                if (!ok) { ShowError(Account.LastError); return; }
                _ui.Toast("CLOUD SYNC COMPLETE", UITheme.Lime, 2.6f);
                Rebuild(TabProfile);
            });
        }

        private void PullCloud()
        {
            if (Account == null) { ToastOffline(); return; }
            if (!Account.CanSync)
            {
                _ui.Toast("SIGN IN TO PULL CLOUD PROGRESS", UITheme.Red, 2.6f);
                return;
            }
            _ui.Toast("PULLING CLOUD SAVE...", UITheme.Cyan, 1.4f);
            Account.PullSync(ok =>
            {
                if (this == null) return;
                if (!ok) { ShowError(Account.LastError); return; }
                _ui.Toast("CLOUD SAVE PULLED", UITheme.Lime, 2.6f);
                Rebuild(TabProfile);
            });
        }

        private void ConfirmLogout()
        {
            _app.Audio.PlayUiClick();
            if (Account == null) { ToastOffline(); return; }
            bool signedIn = Account.SignedIn;
            _ui.ShowModal(signedIn ? "SIGN OUT?" : "END GUEST SESSION?",
                signedIn
                    ? "This device will return to local play. Your account, progression and purchases " +
                      "remain on the server and can be restored by signing in again."
                    : "A fresh guest identity will be created on the next store sync. Local " +
                      "progression stays on this device.",
                signedIn ? "SIGN OUT" : "END SESSION", DoLogout, "CANCEL");
        }

        private void DoLogout()
        {
            if (Account == null) return;
            _ui.Toast("SIGNING OUT...", UITheme.Cyan, 1.2f);
            Account.Logout(ok =>
            {
                if (this == null) return;
                _ui.Toast(ok ? "SIGNED OUT - LOCAL PLAY ACTIVE" : "LOCAL SESSION CLEARED",
                    ok ? UITheme.Lime : UITheme.Amber, 2.8f);
                Rebuild(TabSignIn);
            });
        }

        private void RefreshPaymentCapabilities(bool notify)
        {
            if (Account == null) return;
            Account.RefreshPaymentCapabilities(ok =>
            {
                if (this == null) return;
                if (notify)
                {
                    _ui.Toast(ok ? "PAYMENT CAPABILITIES REFRESHED" : "STORE OFFLINE - CAPABILITIES UNKNOWN",
                        ok ? UITheme.Lime : UITheme.Red, 2.6f);
                    if (_tab == TabProfile) Rebuild(TabProfile);
                }
            });
        }

        // ---- Shared UI helpers ------------------------------------------------------------

        private RectTransform AddCard(RectTransform content, string name, float height, Color fill)
        {
            var border = UITheme.GlassCard(content, name, fill);
            var card = (RectTransform)border.transform.parent;
            UITheme.Layout(card.gameObject, height, -1f, height);
            var stripe = UITheme.Img(card.transform, "Accent", UITheme.Cyan.Alpha(0.8f));
            UITheme.Anchor((RectTransform)stripe.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 14f), new Vector2(4f, -14f));
            return card;
        }

        private static void AddCardTitle(RectTransform card, string title, Color color, ref float top)
        {
            var label = UITheme.Label(card.transform, "Title", title, UITheme.SmallSize, color,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)label.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -top - 30f), new Vector2(-24f, -top));
            top += 34f;
        }

        private static Text AddCardText(RectTransform card, string text, Color color, ref float top,
            float height)
        {
            var label = UITheme.Label(card.transform, "Text", text, UITheme.SmallSize, color,
                TextAnchor.UpperLeft);
            UITheme.Anchor((RectTransform)label.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -top - height), new Vector2(-24f, -top));
            top += height + 8f;
            return label;
        }

        private static InputField AddField(RectTransform card, string name, string labelText,
            string placeholder, ref float top, bool secret, int limit)
        {
            var label = UITheme.Label(card.transform, name + " Label", labelText, 12,
                UITheme.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)label.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -top - 18f), new Vector2(-24f, -top));
            top += 22f;

            var input = UITheme.Input(card.transform, name, "", placeholder, UITheme.BodySize);
            UITheme.Anchor((RectTransform)input.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -top - 44f), new Vector2(-24f, -top));
            input.characterLimit = limit;
            if (secret)
            {
                input.contentType = InputField.ContentType.Password;
                input.asteriskChar = '\u2022';
            }
            top += 52f;
            return input;
        }

        private static Text AddInfoLine(RectTransform card, string labelText, string value,
            Color valueColor, ref float top)
        {
            var label = UITheme.Label(card.transform, labelText + " Label", labelText.ToUpperInvariant(),
                12, UITheme.Muted, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)label.transform, new Vector2(0f, 1f), new Vector2(0.32f, 1f),
                new Vector2(24f, -top - 22f), new Vector2(-4f, -top));
            var text = UITheme.Label(card.transform, labelText, value, 16, valueColor,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Anchor((RectTransform)text.transform, new Vector2(0.3f, 1f), new Vector2(1f, 1f),
                new Vector2(6f, -top - 22f), new Vector2(-24f, -top));
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            top += 28f;
            return text;
        }

        private static void AddBottomButton(RectTransform card, string name, string label,
            Color accent, Action action, int size, float bottom, float top, float leftFraction = 0f,
            float rightFraction = 1f)
        {
            var button = UITheme.Button(card.transform, name, label, accent, () => action(),
                size);
            UITheme.Anchor((RectTransform)button.transform, new Vector2(leftFraction, 0f),
                new Vector2(rightFraction, 0f), new Vector2(24f, bottom), new Vector2(-24f, top));
        }

        private static void AddTopButton(RectTransform card, string name, string label, Color accent,
            Action action, ref float top, float y, float leftFraction = 0f, float rightFraction = 1f)
        {
            var button = UITheme.Button(card.transform, name, label, accent, () => action(),
                UITheme.BodySize);
            UITheme.Anchor((RectTransform)button.transform, new Vector2(leftFraction, 1f),
                new Vector2(rightFraction, 1f), new Vector2(24f, -y - 46f), new Vector2(-24f, -y));
            if (top < y + 54f) top = y + 54f;
        }

        private void SetPhoneStatus(string text, Color color)
        {
            if (_phoneStatus != null)
            {
                _phoneStatus.text = text;
                _phoneStatus.color = color;
            }
        }

        private void Copy(string value, string message)
        {
            _app.Audio.PlayUiClick();
            GUIUtility.systemCopyBuffer = value ?? "";
            _ui.Toast(message, UITheme.Lime, 2.2f);
        }

        private void Rebuild(int tab)
        {
            _tab = Mathf.Clamp(tab, 0, 3);
            _app.UI.ShowScreen(Build(_app, _ui, _tab));
        }

        private void RefreshStatus()
        {
            var account = Account;
            if (_sessionStatus != null)
            {
                if (account == null)
                {
                    _sessionStatus.text = "ACCOUNT SERVICE UNAVAILABLE";
                    _sessionStatus.color = UITheme.Red;
                }
                else if (account.SignedIn)
                {
                    string identity = !string.IsNullOrEmpty(account.Email) ? account.Email
                        : (!string.IsNullOrEmpty(account.Phone) ? account.Phone : "ACCOUNT");
                    _sessionStatus.text = "SIGNED IN  ·  " + identity +
                        (account.EmailVerified || account.PhoneVerified ? "  ·  VERIFIED" : "  ·  VERIFY CONTACT");
                    _sessionStatus.color = account.EmailVerified || account.PhoneVerified
                        ? UITheme.Lime : UITheme.Amber;
                }
                else if (account.Guest)
                {
                    _sessionStatus.text = "GUEST SESSION  ·  LOCAL PROGRESS";
                    _sessionStatus.color = UITheme.Amber;
                }
                else
                {
                    _sessionStatus.text = "LOCAL PROFILE  ·  NOT SIGNED IN";
                    _sessionStatus.color = UITheme.Dim;
                }
            }
            if (_serviceStatus != null)
            {
                _serviceStatus.text = account == null ? "SERVICE OFFLINE"
                    : account.Status + "  ·  REV " + account.SyncRevision;
                _serviceStatus.color = account != null && account.Online ? UITheme.Cyan : UITheme.Red;
            }
        }

        private void ShowError(AccountFailure failure)
        {
            _ui.Toast(failure != null ? failure.Friendly : "ACCOUNT REQUEST FAILED", UITheme.Red, 3.4f);
        }

        private void ToastOffline()
        {
            _ui.Toast("ACCOUNT SERVICE OFFLINE - LOCAL PLAY IS STILL AVAILABLE", UITheme.Red, 3f);
        }
    }
}
