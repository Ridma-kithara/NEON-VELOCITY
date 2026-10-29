using System;
using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Premium;
using UnityEngine;

namespace NeonVelocity.Account
{
    [Serializable]
    public sealed class AccountFailure
    {
        public string code = "";
        public string message = "";
        public long status;
        public bool offline;

        public string Friendly
        {
            get
            {
                switch (code)
                {
                    case "STORE_OFFLINE": return "ACCOUNT SERVICE OFFLINE - PLAYING LOCALLY";
                    case "INVALID_CREDENTIALS": return "EMAIL OR PASSWORD IS INCORRECT";
                    case "EMAIL_IN_USE": return "THAT EMAIL ALREADY HAS AN ACCOUNT";
                    case "INVALID_EMAIL": return "ENTER A VALID EMAIL ADDRESS";
                    case "INVALID_PASSWORD": return "PASSWORD MUST BE 10-128 CHARACTERS WITH A LETTER AND NUMBER";
                    case "INVALID_PLAYER_NAME": return "ENTER A VALID RUNNER NAME";
                    case "INVALID_OTP": return "THE CODE IS INCORRECT OR EXPIRED";
                    case "OTP_ATTEMPTS_EXCEEDED": return "TOO MANY ATTEMPTS - REQUEST A NEW CODE";
                    case "INVALID_TOKEN": return "THE LINK IS INVALID OR EXPIRED";
                    case "RATE_LIMITED": return "TOO MANY REQUESTS - WAIT A MOMENT";
                    case "PROVIDER_NOT_CONFIGURED": return "THIS SIGN-IN PROVIDER IS NOT CONFIGURED";
                    case "PRICE_NOT_CONFIGURED": return "CARD PRICE IS NOT CONFIGURED FOR THIS PACKAGE";
                    case "REDIRECT_NOT_CONFIGURED": return "CHECKOUT RETURN URLS ARE NOT CONFIGURED";
                    case "PAYMENT_PROVIDER_ERROR": return "THE PAYMENT PROVIDER REJECTED THE CHECKOUT";
                    case "SYNC_REVISION_AHEAD": return "CLOUD SAVE IS NEWER - PULLED THE LATEST COPY";
                    case "UNAUTHORIZED": return "SIGN-IN EXPIRED - SIGN IN AGAIN";
                    case "INVALID_RECEIPT":
                    case "RECEIPT_EMAIL_REQUIRED": return "ADD AND VERIFY A RECEIPT EMAIL";
                    default:
                        if (!string.IsNullOrEmpty(message) && message != code)
                            return message.Replace('_', ' ');
                        return string.IsNullOrEmpty(code) ? "ACCOUNT REQUEST FAILED" : code.Replace('_', ' ');
                }
            }
        }
    }

    [Serializable]
    public sealed class AccountSession
    {
        public bool ok;
        public string token = "";
        public string accountId = "";
        public string displayName = "";
        public string email = "";
        public string phone = "";
        public string provider = "";
        public string receiptEmail = "";
        public bool emailVerified;
        public bool phoneVerified;
        public bool verificationRequired;
        public int gems;
        public int syncRevision;
        public AccountDelivery delivery;
    }

    [Serializable]
    public sealed class AccountProfile
    {
        public bool ok;
        public string accountId = "";
        public string displayName = "";
        public string email = "";
        public string phone = "";
        public string provider = "";
        public string receiptEmail = "";
        public bool emailVerified;
        public bool phoneVerified;
        public int gems;
        public int pendingOrders;
        public int syncRevision;
        public string createdUtc = "";
        public string updatedUtc = "";
        public AccountDelivery delivery;
    }

    [Serializable]
    public sealed class AccountDelivery
    {
        public string channel = "";
        public bool delivered;
        public string reason = "";
        public string devVerificationToken = "";
        public string devResetToken = "";
        public string devCode = "";
    }

    [Serializable]
    public sealed class PhoneChallenge
    {
        public string challengeId = "";
        public string phone = "";
        public int expiresInSeconds;
        public AccountDelivery delivery;
    }

    [Serializable]
    public sealed class PaymentCapabilities
    {
        public bool stripe;
        public bool apple;
        public bool googlePlay;
        public bool manualBankTransfer;
        public string stripeCurrency = "USD";

        public bool HasAnyRealMoney => stripe || apple || googlePlay;
    }

    [Serializable]
    public sealed class SyncConflictSummary
    {
        public bool ok;
        public int revision;
        public string[] conflicts = Array.Empty<string>();
        public string updatedUtc = "";
    }

    /// <summary>
    /// Owns the registered-account session, profile and offline-to-online merge.
    ///
    /// GEMS are never written or read as authority here: the service adopts a bearer token and
    /// lets PremiumService fetch the wallet. Sync snapshots intentionally omit premium currency,
    /// tokens, entitlements and the server URL; the server ignores them even if a tampered client
    /// sends them.
    /// </summary>
    public sealed class AccountService : MonoBehaviour
    {
        private const int SnapshotSchema = 1;
        private const int MaxSyncRetries = 1;

        [Serializable] private class RegisterRequest { public string email; public string password; public string playerName; }
        [Serializable] private class LoginRequest { public string email; public string password; }
        [Serializable] private class LinkRequest { public string email; public string password; public string playerName; }
        [Serializable] private class EmailRequest { public string email; }
        [Serializable] private class TokenRequest { public string token; }
        [Serializable] private class ResetRequest { public string token; public string password; }
        [Serializable] private class PhoneStartRequest { public string phone; }
        [Serializable] private class PhoneVerifyRequest { public string challengeId; public string code; public string playerName; }
        [Serializable] private class OAuthRequest { public string provider; public string idToken; public string playerName; }
        [Serializable] private class ProfileRequest { public string displayName; public string receiptEmail; }
        [Serializable] private class CheckoutRequest
        {
            public string packageId;
            public string clientRequestId;
            public string receiptEmail;
            public string successUrl;
            public string cancelUrl;
        }

        [Serializable] private class ErrorEnvelope { public bool ok; public string error; public string code; }
        [Serializable] private class DeliveryEnvelope { public bool ok; public AccountDelivery delivery; }
        [Serializable] private class SyncRequest
        {
            public SyncSnapshot snapshot;
            public int baseRevision;
            public string clientRequestId;
            public string deviceKey;
        }
        [Serializable] private class SyncResponse
        {
            public bool ok;
            public int revision;
            public SyncSnapshot snapshot;
            public string[] conflicts = Array.Empty<string>();
            public int gems;
            public string updatedUtc = "";
            public string code = "";
            public string error = "";
        }

        [Serializable]
        public sealed class SyncSettings
        {
            public float musicVolume;
            public float sfxVolume;
            public float ambienceVolume;
            public float swipeSensitivity;
            public int graphicsPreset;
            public int targetFrameRate;
            public bool cameraShake;
            public bool reducedMotion;
            public bool vibration;
            public bool highContrast;
            public string preferredLanguage = "en";
        }

        [Serializable]
        public sealed class SyncSnapshot
        {
            public int schema = SnapshotSchema;
            public long totalXp;
            public int credits;
            public int seasonXp;
            public int seasonLevel;
            public float bestDistance;
            public int bestScore;
            public int furthestLocationIndex;
            public LifetimeStats lifetime = new LifetimeStats();
            public string[] discoveredLocations = Array.Empty<string>();
            public string[] unlockedCharacters = Array.Empty<string>();
            public string[] unlockedTrails = Array.Empty<string>();
            public string[] redeemedRewardIds = Array.Empty<string>();
            public string[] claimedAchievements = Array.Empty<string>();
            public MissionState[] missions = Array.Empty<MissionState>();
            public ModeBest[] modeBests = Array.Empty<ModeBest>();
            public LocationRecord[] locationRecords = Array.Empty<LocationRecord>();
            public bool tutorialCompleted;
            public SyncSettings settings = new SyncSettings();
            public string lastModeId = "";
            public string selectedCharacter = "kira";
            public string selectedTrail = "spark";
            public string lastPlayedUtc = "";
        }

        private GameApp _app;
        private PremiumService _premium;
        private int _syncBusy;

        public bool Busy { get; private set; }
        public string Status { get; private set; } = "LOCAL PROFILE";
        public AccountFailure LastError { get; private set; }
        public AccountDelivery LastDelivery { get; private set; }
        public bool PaymentsLoaded { get; private set; }
        public PaymentCapabilities Payments { get; private set; } = new PaymentCapabilities();
        public event Action Changed;

        private SaveData Data => _app != null ? _app.Profile : null;
        private AccountCache Cache => Data != null ? Data.account : null;

        public bool Online => _premium != null && _premium.Online;
        public bool SignedIn =>
            Data != null && Cache != null && Cache.signedIn &&
            _premium != null && _premium.HasPersistentSession;
        public bool Guest => !SignedIn && _premium != null && _premium.HasPersistentSession;
        public bool EmailVerified => Cache != null && Cache.emailVerified;
        public bool PhoneVerified => Cache != null && Cache.phoneVerified;
        public string Email => Cache != null ? Cache.email ?? "" : "";
        public string Phone => Cache != null ? Cache.phone ?? "" : "";
        public string Provider => Cache != null ? Cache.provider ?? "" : "";
        public string ReceiptEmail => Cache != null ? Cache.receiptEmail ?? "" : "";
        public string AccountId => Cache != null ? Cache.accountId ?? "" : "";
        public string DisplayName => Data != null ? Data.playerName ?? "" : "";
        public int SyncRevision => Cache != null ? Cache.syncRevision : 0;
        public string LastSyncUtc => Cache != null ? Cache.lastSyncUtc ?? "" : "";
        public string LastSyncSummary => Cache != null ? Cache.lastSyncSummary ?? "" : "";
        public string LastSyncError => Cache != null ? Cache.lastSyncError ?? "" : "";

        public void Initialise(GameApp app, PremiumService premium)
        {
            _app = app;
            _premium = premium;
            if (Data == null) return;
            if (Data.account == null) Data.account = new AccountCache();
            if (!string.IsNullOrEmpty(Data.premium.token))
            {
                // A registered session survives restarts; a legacy guest token does not.
                if (Data.account.signedIn) Data.premium.persistentSession = true;
                if (string.IsNullOrEmpty(Data.account.accountId))
                    Data.account.accountId = Data.premium.accountId ?? "";
            }
            else
            {
                Data.account.signedIn = false;
            }
            Status = SignedIn ? "SIGNED IN" : "LOCAL PROFILE";
        }

        // ---- provider capabilities -----------------------------------------------------

        public void RefreshPaymentCapabilities(Action<bool> done = null)
        {
            if (_premium == null) { done?.Invoke(false); return; }
            _premium.CallApi("GET", "/api/v1/store/payments", null, false, result =>
            {
                if (!result.ok)
                {
                    done?.Invoke(false);
                    return;
                }
                try
                {
                    var envelope = JsonUtility.FromJson<PaymentEnvelope>(result.text);
                    if (envelope != null && envelope.payments != null)
                    {
                        Payments = envelope.payments;
                        PaymentsLoaded = true;
                        RaiseChanged();
                    }
                    done?.Invoke(true);
                }
                catch (Exception)
                {
                    done?.Invoke(false);
                }
            });
        }

        [Serializable] private class PaymentEnvelope { public bool ok; public PaymentCapabilities payments; }

        // ---- email/password and provider sign-in --------------------------------------

        public void Register(string email, string password, string runnerName, Action<bool> done)
        {
            var request = new RegisterRequest
            {
                email = email ?? "",
                password = password ?? "",
                playerName = string.IsNullOrWhiteSpace(runnerName) ? DisplayName : runnerName.Trim()
            };
            PostSession("/api/v1/auth/register", JsonUtility.ToJson(request), false, true, done);
        }

        public void Login(string email, string password, Action<bool> done)
        {
            var request = new LoginRequest { email = email ?? "", password = password ?? "" };
            PostSession("/api/v1/auth/login", JsonUtility.ToJson(request), false, true, done);
        }

        public void LinkCredentials(string email, string password, string runnerName, Action<bool> done)
        {
            if (!SignedIn && !Guest) { Fail(done, Offline("SIGN IN AS A GUEST OR ACCOUNT FIRST")); return; }
            var request = new LinkRequest
            {
                email = email ?? "",
                password = password ?? "",
                playerName = string.IsNullOrWhiteSpace(runnerName) ? DisplayName : runnerName.Trim()
            };
            PostSession("/api/v1/auth/link", JsonUtility.ToJson(request), true, true, done);
        }

        public void OAuth(string provider, string idToken, string runnerName, Action<bool> done)
        {
            if (string.IsNullOrEmpty(idToken))
            {
                Fail(done, new AccountFailure
                {
                    code = "PROVIDER_NOT_CONFIGURED",
                    message = "NATIVE PROVIDER TOKEN REQUIRED"
                });
                return;
            }
            var request = new OAuthRequest
            {
                provider = provider ?? "",
                idToken = idToken,
                playerName = string.IsNullOrWhiteSpace(runnerName) ? DisplayName : runnerName.Trim()
            };
            PostSession("/api/v1/auth/oauth", JsonUtility.ToJson(request), false, false, done);
        }

        public void Logout(Action<bool> done)
        {
            if (_premium == null) { done?.Invoke(false); return; }
            if (!_premium.HasPersistentSession)
            {
                _premium.ClearSession();
                ClearLocalAccountCache();
                Status = "LOCAL PROFILE";
                if (_app != null) _app.SaveProfile();
                RaiseChanged();
                done?.Invoke(true);
                return;
            }
            Busy = true;
            _premium.CallApi("POST", "/api/v1/auth/logout", "{}", true, result =>
            {
                Busy = false;
                // A failed network call still clears the local session: the player asked to
                // leave this account on this device, and the token remains server-revocable.
                _premium.ClearSession();
                ClearLocalAccountCache();
                Status = "SIGNED OUT - LOCAL PROFILE";
                LastError = result.ok ? null : ParseFailure(result);
                if (_app != null) _app.SaveProfile();
                RaiseChanged();
                done?.Invoke(result.ok);
                if (_premium.Online || result.ok) _premium.SyncAll();
            });
        }

        private void ClearLocalAccountCache()
        {
            if (Data == null || Data.account == null) return;
            Data.account.signedIn = false;
            Data.account.accountId = "";
            Data.account.email = "";
            Data.account.phone = "";
            Data.account.provider = "";
            Data.account.receiptEmail = "";
            Data.account.emailVerified = false;
            Data.account.phoneVerified = false;
            Data.account.lastSyncError = "";
        }

        private void PostSession(string path, string json, bool authenticated, bool syncAfter,
            Action<bool> done)
        {
            if (_premium == null) { Fail(done, Offline("ACCOUNT SERVICE UNAVAILABLE")); return; }
            Busy = true;
            LastError = null;
            Status = "CONTACTING ACCOUNT SERVICE...";
            RaiseChanged();
            _premium.CallApi("POST", path, json, authenticated, result =>
            {
                Busy = false;
                if (!result.ok)
                {
                    LastError = ParseFailure(result);
                    Status = SignedIn ? "SIGNED IN" : "SIGN-IN FAILED";
                    RaiseChanged();
                    Fail(done, LastError);
                    return;
                }
                AccountSession session = null;
                try { session = JsonUtility.FromJson<AccountSession>(result.text); }
                catch (Exception) { session = null; }
                if (session == null || string.IsNullOrEmpty(session.token))
                {
                    Fail(done, new AccountFailure { code = "INVALID_RESPONSE", message = "ACCOUNT RESPONSE INVALID" });
                    return;
                }
                LastDelivery = session.delivery;
                ApplySession(session);
                done?.Invoke(true);
                if (_premium != null) _premium.SyncAll();
                if (syncAfter) SyncNow(null);
            });
        }

        private void ApplySession(AccountSession session)
        {
            if (Data == null) return;
            if (Data.account == null) Data.account = new AccountCache();
            Data.account.accountId = session.accountId ?? "";
            Data.account.email = session.email ?? "";
            Data.account.phone = session.phone ?? "";
            Data.account.provider = session.provider ?? "";
            Data.account.receiptEmail = string.IsNullOrEmpty(session.receiptEmail)
                ? session.email ?? "" : session.receiptEmail;
            Data.account.emailVerified = session.emailVerified;
            Data.account.phoneVerified = session.phoneVerified;
            Data.account.signedIn = true;
            Data.account.syncRevision = Mathf.Max(0, session.syncRevision);
            Data.account.lastSyncError = "";
            if (!string.IsNullOrEmpty(session.displayName)) Data.playerName = session.displayName;
            _premium?.AdoptSession(session.token, session.accountId, session.displayName, session.gems);
            Status = session.emailVerified || session.phoneVerified
                ? "SIGNED IN" : "SIGNED IN - VERIFY CONTACT";
            LastError = null;
            _app.SaveProfile();
            RaiseChanged();
        }

        // ---- verification and recovery -------------------------------------------------

        public void VerifyEmail(string token, Action<bool> done)
        {
            var request = new TokenRequest { token = token ?? "" };
            Busy = true;
            _premium.CallApi("POST", "/api/v1/auth/email/verify", JsonUtility.ToJson(request), false, result =>
            {
                Busy = false;
                if (!result.ok) { Fail(done, ParseFailure(result)); return; }
                ApplyProfileJson(result.text);
                Status = "EMAIL VERIFIED";
                done?.Invoke(true);
            });
        }

        public void RequestPasswordReset(string email, Action<bool> done)
        {
            var request = new EmailRequest { email = email ?? "" };
            Busy = true;
            _premium.CallApi("POST", "/api/v1/auth/password/request", JsonUtility.ToJson(request), false, result =>
            {
                Busy = false;
                if (!result.ok) { Fail(done, ParseFailure(result)); return; }
                try
                {
                    var envelope = JsonUtility.FromJson<DeliveryEnvelope>(result.text);
                    if (envelope != null && envelope.delivery != null)
                        LastDelivery = envelope.delivery;
                }
                catch (Exception)
                {
                    LastDelivery = null;
                }
                Status = "RESET EMAIL REQUESTED";
                RaiseChanged();
                done?.Invoke(true);
            });
        }

        public void ResetPassword(string token, string password, Action<bool> done)
        {
            var request = new ResetRequest { token = token ?? "", password = password ?? "" };
            Busy = true;
            _premium.CallApi("POST", "/api/v1/auth/password/reset", JsonUtility.ToJson(request), false, result =>
            {
                Busy = false;
                if (!result.ok) { Fail(done, ParseFailure(result)); return; }
                _premium.ClearSession();
                Status = "PASSWORD RESET - SIGN IN AGAIN";
                if (_app != null) _app.SaveProfile();
                done?.Invoke(true);
            });
        }

        public void StartPhone(string phone, Action<PhoneChallenge> done)
        {
            var request = new PhoneStartRequest { phone = phone ?? "" };
            Busy = true;
            _premium.CallApi("POST", "/api/v1/auth/phone/start", JsonUtility.ToJson(request), false, result =>
            {
                Busy = false;
                if (!result.ok)
                {
                    LastError = ParseFailure(result);
                    RaiseChanged();
                    done?.Invoke(null);
                    return;
                }
                PhoneChallenge challenge = null;
                try { challenge = JsonUtility.FromJson<PhoneChallenge>(result.text); }
                catch (Exception) { challenge = null; }
                if (challenge == null || string.IsNullOrEmpty(challenge.challengeId))
                {
                    done?.Invoke(null);
                    return;
                }
                LastDelivery = challenge.delivery;
                Status = "SMS CODE REQUESTED";
                RaiseChanged();
                done?.Invoke(challenge);
            });
        }

        public void VerifyPhone(string challengeId, string code, string runnerName, Action<bool> done)
        {
            var request = new PhoneVerifyRequest
            {
                challengeId = challengeId ?? "",
                code = code ?? "",
                playerName = string.IsNullOrWhiteSpace(runnerName) ? DisplayName : runnerName.Trim()
            };
            PostSession("/api/v1/auth/phone/verify", JsonUtility.ToJson(request), false, true, done);
        }

        // ---- profile -------------------------------------------------------------------

        public void RefreshProfile(Action<bool> done = null)
        {
            if (_premium == null || string.IsNullOrEmpty(_premium.Token))
            {
                done?.Invoke(false);
                return;
            }
            _premium.CallApi("GET", "/api/v1/account/profile", null, true, result =>
            {
                if (!result.ok) { LastError = ParseFailure(result); done?.Invoke(false); return; }
                ApplyProfileJson(result.text);
                done?.Invoke(true);
            });
        }

        public void UpdateProfile(string runnerName, string receiptEmail, Action<bool> done)
        {
            if (!SignedIn && !Guest) { Fail(done, Offline("SIGN IN TO EDIT PROFILE")); return; }
            var request = new ProfileRequest
            {
                displayName = string.IsNullOrWhiteSpace(runnerName) ? DisplayName : runnerName.Trim(),
                receiptEmail = receiptEmail ?? ""
            };
            Busy = true;
            _premium.CallApi("POST", "/api/v1/account/profile", JsonUtility.ToJson(request), true, result =>
            {
                Busy = false;
                if (!result.ok) { Fail(done, ParseFailure(result)); return; }
                ApplyProfileJson(result.text);
                Status = "PROFILE UPDATED";
                done?.Invoke(true);
            });
        }

        public void ChangeEmail(string email, Action<bool> done)
        {
            if (!SignedIn) { Fail(done, Offline("SIGN IN TO CHANGE EMAIL")); return; }
            var request = new EmailRequest { email = email ?? "" };
            Busy = true;
            _premium.CallApi("POST", "/api/v1/account/email", JsonUtility.ToJson(request), true, result =>
            {
                Busy = false;
                if (!result.ok) { Fail(done, ParseFailure(result)); return; }
                ApplyProfileJson(result.text);
                Status = "VERIFY YOUR NEW EMAIL";
                done?.Invoke(true);
            });
        }

        private void ApplyProfileJson(string text)
        {
            AccountProfile profile = null;
            try { profile = JsonUtility.FromJson<AccountProfile>(text); }
            catch (Exception) { profile = null; }
            if (profile == null || Data == null) return;
            if (profile.delivery != null) LastDelivery = profile.delivery;
            if (Data.account == null) Data.account = new AccountCache();
            Data.account.accountId = profile.accountId ?? Data.account.accountId;
            Data.account.email = profile.email ?? "";
            Data.account.phone = profile.phone ?? "";
            Data.account.provider = profile.provider ?? "";
            Data.account.receiptEmail = string.IsNullOrEmpty(profile.receiptEmail)
                ? profile.email ?? "" : profile.receiptEmail;
            Data.account.emailVerified = profile.emailVerified;
            Data.account.phoneVerified = profile.phoneVerified;
            Data.account.syncRevision = Mathf.Max(Data.account.syncRevision, profile.syncRevision);
            if (!string.IsNullOrEmpty(profile.displayName)) Data.playerName = profile.displayName;
            if (profile.gems >= 0 && Data.premium != null)
                Data.premium.gems = Mathf.Max(0, profile.gems);
            Status = profile.emailVerified || profile.phoneVerified
                ? "SIGNED IN" : "SIGNED IN - VERIFY CONTACT";
            LastError = null;
            _app.SaveProfile();
            RaiseChanged();
        }

        // ---- cloud sync ----------------------------------------------------------------

        public bool CanSync => SignedIn && _premium != null && !string.IsNullOrEmpty(_premium.Token);

        /// <summary>
        /// Merge local progression into the account cloud save. The server takes the maximum of
        /// scalar progress, unions reward IDs, ignores GEMS/tokens and returns the merged snapshot.
        /// A stale revision triggers a pull and exactly one retry.
        /// </summary>
        public void SyncNow(Action<bool> done)
        {
            if (!CanSync)
            {
                done?.Invoke(false);
                return;
            }
            if (_syncBusy > 0)
            {
                done?.Invoke(false);
                return;
            }
            StartCoroutine(SyncRoutine(MaxSyncRetries, done));
        }

        private System.Collections.IEnumerator SyncRoutine(int retriesLeft, Action<bool> done)
        {
            _syncBusy++;
            Busy = true;
            Status = "SYNCING PROGRESS...";
            RaiseChanged();
            var snapshot = BuildSnapshot(Data);
            var request = new SyncRequest
            {
                snapshot = snapshot,
                baseRevision = Mathf.Max(0, Cache.syncRevision),
                clientRequestId = "sync-" + Guid.NewGuid().ToString("N"),
                deviceKey = Data.premium != null ? Data.premium.deviceKey : ""
            };
            PremiumApiResult result = null;
            yield return null;
            _premium.CallApi("POST", "/api/v1/account/sync", JsonUtility.ToJson(request), true,
                r => result = r);
            while (result == null) yield return null;

            if (result.ok)
            {
                SyncResponse response = null;
                try { response = JsonUtility.FromJson<SyncResponse>(result.text); }
                catch (Exception) { response = null; }
                if (response == null || !response.ok)
                {
                    FinishSync(false, "SYNC RESPONSE INVALID", done);
                    yield break;
                }
                ApplySnapshot(Data, response.snapshot);
                Cache.syncRevision = Mathf.Max(0, response.revision);
                Cache.lastSyncUtc = DateTime.UtcNow.ToString("O");
                Cache.lastSyncError = "";
                int conflicts = response.conflicts != null ? response.conflicts.Length : 0;
                Cache.lastSyncSummary = "REVISION " + response.revision +
                    (conflicts > 0 ? "  •  " + conflicts + " CONFLICT" + (conflicts == 1 ? "" : "S") : "  •  CLEAN");
                if (response.gems >= 0 && Data.premium != null)
                    Data.premium.gems = Mathf.Max(0, response.gems);
                _app.SaveProfile();
                FinishSync(true, "SYNC COMPLETE", done);
                if (_premium != null) _premium.RefreshWallet();
                yield break;
            }

            var failure = ParseFailure(result);
            if (failure.code == "SYNC_REVISION_AHEAD" && retriesLeft > 0)
            {
                bool pulled = false;
                yield return PullSyncInternal(r => pulled = r);
                if (pulled)
                {
                    _syncBusy--;
                    yield return SyncRoutine(retriesLeft - 1, done);
                    yield break;
                }
            }
            Cache.lastSyncError = failure.Friendly;
            FinishSync(false, failure.Friendly, done);
        }

        private void FinishSync(bool success, string message, Action<bool> done)
        {
            Busy = false;
            _syncBusy = Mathf.Max(0, _syncBusy - 1);
            Status = success ? "CLOUD SYNCED" : "SYNC FAILED";
            if (!success) LastError = new AccountFailure { code = "SYNC_FAILED", message = message };
            RaiseChanged();
            done?.Invoke(success);
        }

        /// <summary>Pull the cloud copy without pushing. Used after a revision conflict.</summary>
        public void PullSync(Action<bool> done)
        {
            if (!CanSync) { done?.Invoke(false); return; }
            StartCoroutine(PullSyncInternal(done));
        }

        private System.Collections.IEnumerator PullSyncInternal(Action<bool> done)
        {
            PremiumApiResult result = null;
            _premium.CallApi("GET", "/api/v1/account/sync", null, true, r => result = r);
            while (result == null) yield return null;
            if (!result.ok)
            {
                LastError = ParseFailure(result);
                done?.Invoke(false);
                yield break;
            }
            SyncResponse response = null;
            try { response = JsonUtility.FromJson<SyncResponse>(result.text); }
            catch (Exception) { response = null; }
            if (response == null || !response.ok)
            {
                done?.Invoke(false);
                yield break;
            }
            if (response.snapshot != null) ApplySnapshot(Data, response.snapshot);
            Cache.syncRevision = Mathf.Max(0, response.revision);
            Cache.lastSyncUtc = DateTime.UtcNow.ToString("O");
            Cache.lastSyncError = "";
            Cache.lastSyncSummary = "PULLED REVISION " + response.revision;
            if (response.gems >= 0 && Data.premium != null)
                Data.premium.gems = Mathf.Max(0, response.gems);
            _app.SaveProfile();
            RaiseChanged();
            done?.Invoke(true);
        }

        public SyncSnapshot BuildSnapshot(SaveData data)
        {
            if (data == null) return new SyncSnapshot();
            var settings = data.settings ?? new SettingsData();
            return new SyncSnapshot
            {
                schema = SnapshotSchema,
                totalXp = Math.Max(0L, data.totalXp),
                credits = Math.Max(0, data.credits),
                seasonXp = Math.Max(0, data.seasonXp),
                seasonLevel = Math.Max(1, data.seasonLevel),
                bestDistance = Math.Max(0f, data.bestDistance),
                bestScore = Math.Max(0, data.bestScore),
                furthestLocationIndex = Math.Max(-1, data.furthestLocationIndex),
                lifetime = data.lifetime ?? new LifetimeStats(),
                discoveredLocations = (data.discoveredLocations ?? new List<string>()).ToArray(),
                unlockedCharacters = (data.unlockedCharacters ?? new List<string>()).ToArray(),
                unlockedTrails = (data.unlockedTrails ?? new List<string>()).ToArray(),
                redeemedRewardIds = (data.redeemedRewardIds ?? new List<string>()).ToArray(),
                claimedAchievements = new string[0],
                missions = (data.missions != null && data.missions.missions != null
                    ? data.missions.missions : new List<MissionState>()).ToArray(),
                modeBests = (data.modeBests ?? new List<ModeBest>()).ToArray(),
                locationRecords = (data.locationRecords ?? new List<LocationRecord>()).ToArray(),
                tutorialCompleted = data.tutorialCompleted,
                settings = new SyncSettings
                {
                    musicVolume = settings.musicVolume,
                    sfxVolume = settings.sfxVolume,
                    ambienceVolume = settings.ambienceVolume,
                    swipeSensitivity = settings.swipeSensitivity,
                    graphicsPreset = settings.graphicsPreset,
                    targetFrameRate = settings.targetFrameRate,
                    cameraShake = settings.cameraShake,
                    reducedMotion = settings.reducedMotion,
                    vibration = settings.vibration,
                    highContrast = settings.highContrast,
                    preferredLanguage = settings.preferredLanguage ?? "en"
                },
                lastModeId = data.lastModeId ?? "",
                selectedCharacter = data.selectedCharacter ?? "kira",
                selectedTrail = data.selectedTrail ?? "spark",
                lastPlayedUtc = data.lastPlayedUtc ?? ""
            };
        }

        /// <summary>
        /// Apply a server-merged snapshot. Every scalar is max-combined with local progress so a
        /// run completed while the request was in flight can never be rolled back. Premium fields
        /// are never read from the snapshot.
        /// </summary>
        public void ApplySnapshot(SaveData data, SyncSnapshot snapshot)
        {
            if (data == null || snapshot == null) return;
            data.totalXp = Math.Max(data.totalXp, Math.Max(0L, snapshot.totalXp));
            data.credits = Math.Max(data.credits, Math.Max(0, snapshot.credits));
            data.seasonXp = Math.Max(data.seasonXp, Math.Max(0, snapshot.seasonXp));
            data.seasonLevel = Math.Max(Math.Max(1, data.seasonLevel), snapshot.seasonLevel);
            data.bestDistance = Math.Max(data.bestDistance, snapshot.bestDistance);
            data.bestScore = Math.Max(data.bestScore, snapshot.bestScore);
            data.furthestLocationIndex = Math.Max(data.furthestLocationIndex, snapshot.furthestLocationIndex);
            data.level = LevelProgression.LevelForXp(data.totalXp);

            var lifetime = snapshot.lifetime ?? new LifetimeStats();
            data.lifetime.totalRuns = Math.Max(data.lifetime.totalRuns, lifetime.totalRuns);
            data.lifetime.totalDistance = Math.Max(data.lifetime.totalDistance, lifetime.totalDistance);
            data.lifetime.totalCoins = Math.Max(data.lifetime.totalCoins, lifetime.totalCoins);
            data.lifetime.totalJumps = Math.Max(data.lifetime.totalJumps, lifetime.totalJumps);
            data.lifetime.totalSlides = Math.Max(data.lifetime.totalSlides, lifetime.totalSlides);
            data.lifetime.totalPowerUps = Math.Max(data.lifetime.totalPowerUps, lifetime.totalPowerUps);
            data.lifetime.totalDeaths = Math.Max(data.lifetime.totalDeaths, lifetime.totalDeaths);
            data.lifetime.racesCompleted = Math.Max(data.lifetime.racesCompleted, lifetime.racesCompleted);
            data.lifetime.racesWon = Math.Max(data.lifetime.racesWon, lifetime.racesWon);
            data.lifetime.bestRaceRank = Math.Max(data.lifetime.bestRaceRank, lifetime.bestRaceRank);
            data.lifetime.missionsClaimed = Math.Max(data.lifetime.missionsClaimed, lifetime.missionsClaimed);
            data.lifetime.dailyRewardsClaimed = Math.Max(data.lifetime.dailyRewardsClaimed, lifetime.dailyRewardsClaimed);
            data.lifetime.nearMisses = Math.Max(data.lifetime.nearMisses, lifetime.nearMisses);
            data.lifetime.levelsGained = Math.Max(data.lifetime.levelsGained, lifetime.levelsGained);
            data.lifetime.locationsDiscovered = Math.Max(data.lifetime.locationsDiscovered, lifetime.locationsDiscovered);
            data.lifetime.premiumPurchases = Math.Max(data.lifetime.premiumPurchases, lifetime.premiumPurchases);

            UnionInto(data.discoveredLocations, snapshot.discoveredLocations);
            UnionInto(data.unlockedCharacters, snapshot.unlockedCharacters);
            UnionInto(data.unlockedTrails, snapshot.unlockedTrails);
            UnionInto(data.redeemedRewardIds, snapshot.redeemedRewardIds);

            MergeMissions(data, snapshot.missions);
            MergeModeBests(data, snapshot.modeBests);
            MergeLocationRecords(data, snapshot.locationRecords);

            data.tutorialCompleted = data.tutorialCompleted || snapshot.tutorialCompleted;
            if (snapshot.settings != null)
            {
                var s = snapshot.settings;
                var target = data.settings;
                target.musicVolume = Mathf.Clamp01(s.musicVolume);
                target.sfxVolume = Mathf.Clamp01(s.sfxVolume);
                target.ambienceVolume = Mathf.Clamp01(s.ambienceVolume);
                target.swipeSensitivity = Mathf.Clamp(s.swipeSensitivity, 0.4f, 2.5f);
                if (s.targetFrameRate >= 30 && s.targetFrameRate <= 240) target.targetFrameRate = s.targetFrameRate;
                target.cameraShake = s.cameraShake;
                target.reducedMotion = s.reducedMotion;
                target.vibration = s.vibration;
                target.highContrast = s.highContrast;
                if (!string.IsNullOrEmpty(s.preferredLanguage)) target.preferredLanguage = s.preferredLanguage;
            }
            if (!string.IsNullOrEmpty(snapshot.lastModeId)) data.lastModeId = snapshot.lastModeId;
            if (!string.IsNullOrEmpty(snapshot.selectedCharacter) &&
                data.unlockedCharacters.Contains(snapshot.selectedCharacter))
                data.selectedCharacter = snapshot.selectedCharacter;
            if (!string.IsNullOrEmpty(snapshot.selectedTrail) &&
                data.unlockedTrails.Contains(snapshot.selectedTrail))
                data.selectedTrail = snapshot.selectedTrail;
            if (!string.IsNullOrEmpty(snapshot.lastPlayedUtc)) data.lastPlayedUtc = snapshot.lastPlayedUtc;
        }

        private static void UnionInto(List<string> target, string[] source)
        {
            if (target == null || source == null) return;
            for (int i = 0; i < source.Length; i++)
            {
                string value = source[i];
                if (string.IsNullOrEmpty(value) || target.Contains(value)) continue;
                target.Add(value);
            }
        }

        private static void MergeMissions(SaveData data, MissionState[] remote)
        {
            if (data.missions == null) data.missions = new MissionSave();
            if (data.missions.missions == null) data.missions.missions = new List<MissionState>();
            if (remote == null) return;
            for (int i = 0; i < remote.Length; i++)
            {
                var incoming = remote[i];
                if (incoming == null || string.IsNullOrEmpty(incoming.id)) continue;
                var local = data.missions.missions.Find(m => m != null && m.id == incoming.id);
                if (local == null)
                {
                    data.missions.missions.Add(incoming);
                    continue;
                }
                local.progress = Math.Max(local.progress, incoming.progress);
                local.target = Math.Max(local.target, incoming.target);
                local.claimed = local.claimed || incoming.claimed;
                local.completed = local.completed || incoming.completed || local.progress >= local.target;
                if (string.IsNullOrEmpty(local.title)) local.title = incoming.title;
                if (string.IsNullOrEmpty(local.description)) local.description = incoming.description;
                if (string.IsNullOrEmpty(local.scope)) local.scope = incoming.scope;
                if (string.IsNullOrEmpty(local.expiresDateKey)) local.expiresDateKey = incoming.expiresDateKey;
            }
        }

        private static void MergeModeBests(SaveData data, ModeBest[] remote)
        {
            if (data.modeBests == null) data.modeBests = new List<ModeBest>();
            if (remote == null) return;
            for (int i = 0; i < remote.Length; i++)
            {
                var incoming = remote[i];
                if (incoming == null || string.IsNullOrEmpty(incoming.modeId)) continue;
                var local = data.FindModeBest(incoming.modeId);
                if (local == null)
                {
                    data.modeBests.Add(incoming);
                    continue;
                }
                local.distance = Math.Max(local.distance, incoming.distance);
                local.score = Math.Max(local.score, incoming.score);
                local.coins = Math.Max(local.coins, incoming.coins);
                if (string.IsNullOrEmpty(local.achievedUtc)) local.achievedUtc = incoming.achievedUtc;
            }
        }

        private static void MergeLocationRecords(SaveData data, LocationRecord[] remote)
        {
            if (data.locationRecords == null) data.locationRecords = new List<LocationRecord>();
            if (remote == null) return;
            for (int i = 0; i < remote.Length; i++)
            {
                var incoming = remote[i];
                if (incoming == null || string.IsNullOrEmpty(incoming.locationId)) continue;
                var local = data.FindLocationRecord(incoming.locationId);
                if (local == null)
                {
                    // zoneIndex is not part of the server merge; resolve it from the catalogue.
                    incoming.zoneIndex = Mathf.Max(0, LocationCatalog.IndexOf(incoming.locationId));
                    data.locationRecords.Add(incoming);
                    continue;
                }
                local.bestDistance = Math.Max(local.bestDistance, incoming.bestDistance);
                local.visits = Math.Max(local.visits, incoming.visits);
            }
        }

        // ---- checkout ------------------------------------------------------------------

        public void StartCardCheckout(string packageId, string receiptEmail,
            Action<string> success, Action<AccountFailure> failure)
        {
            if (!SignedIn)
            {
                failure?.Invoke(Offline("SIGN IN BEFORE A CARD PURCHASE"));
                return;
            }
            if (string.IsNullOrEmpty(receiptEmail) || !receiptEmail.Contains("@"))
            {
                failure?.Invoke(new AccountFailure
                {
                    code = "RECEIPT_EMAIL_REQUIRED",
                    message = "ADD A RECEIPT EMAIL"
                });
                return;
            }
            var request = new CheckoutRequest
            {
                packageId = packageId ?? "",
                clientRequestId = "checkout-" + Guid.NewGuid().ToString("N"),
                receiptEmail = receiptEmail,
                // Empty redirects let the server use its configured HTTPS return pages. A custom
                // scheme supplied by the client would be rejected by the redirect validator.
                successUrl = "",
                cancelUrl = ""
            };
            Busy = true;
            _premium.CallApi("POST", "/api/v1/payments/checkout", JsonUtility.ToJson(request), true, result =>
            {
                Busy = false;
                if (!result.ok)
                {
                    failure?.Invoke(ParseFailure(result));
                    return;
                }
                CheckoutResponse envelope = null;
                try { envelope = JsonUtility.FromJson<CheckoutResponse>(result.text); }
                catch (Exception) { envelope = null; }
                if (envelope == null || string.IsNullOrEmpty(envelope.url))
                {
                    failure?.Invoke(new AccountFailure { code = "INVALID_RESPONSE", message = "CHECKOUT RESPONSE INVALID" });
                    return;
                }
                success?.Invoke(envelope.url);
            });
        }

        [Serializable] private class CheckoutResponse
        {
            public bool ok;
            public string url = "";
            public string sessionId = "";
            public string packageId = "";
            public int gems;
            public string receiptEmail = "";
        }

        // ---- error helpers -------------------------------------------------------------

        private AccountFailure ParseFailure(PremiumApiResult result)
        {
            if (result == null)
                return new AccountFailure { code = "STORE_OFFLINE", message = "ACCOUNT SERVICE OFFLINE", offline = true };
            string code = result.code;
            string message = result.message;
            if (!string.IsNullOrEmpty(result.text) && (string.IsNullOrEmpty(code) || code == "REQUEST_FAILED"))
            {
                try
                {
                    var envelope = JsonUtility.FromJson<ErrorEnvelope>(result.text);
                    if (envelope != null)
                    {
                        code = envelope.code ?? code;
                        message = envelope.error ?? message;
                    }
                }
                catch (Exception)
                {
                    // Keep the transport-level diagnosis.
                }
            }
            return new AccountFailure
            {
                code = code ?? "",
                message = message ?? "",
                status = result.status,
                offline = result.networkError || result.status == 0
            };
        }

        private static AccountFailure Offline(string message) => new AccountFailure
        {
            code = "STORE_OFFLINE",
            message = message,
            offline = true
        };

        private void Fail(Action<bool> done, AccountFailure failure)
        {
            LastError = failure;
            Status = "ACCOUNT REQUEST FAILED";
            RaiseChanged();
            done?.Invoke(false);
        }

        private void RaiseChanged() => Changed?.Invoke();
    }
}
