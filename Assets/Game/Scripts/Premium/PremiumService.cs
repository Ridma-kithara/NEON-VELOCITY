using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NeonVelocity.Core;
using NeonVelocity.Persistence;
using UnityEngine;
using UnityEngine.Networking;

namespace NeonVelocity.Premium
{
    // Wire models mirror the dependency-free Python service exactly. Currency amount fields
    // are LKR integers; GEM balances are always server-authoritative and only cached locally.
    [Serializable]
    public sealed class GemPackage
    {
        public string id;
        public string name;
        public string description;
        public string currency;
        public int gems;
        public int bonusGems;
        public int totalGems;
        public int priceLkr;
        public bool enabled;
    }

    [Serializable]
    public sealed class PremiumProduct
    {
        public string id;
        public string category;
        public string name;
        public string description;
        public int priceGems;
        public string characterId;
        public string upgradeId;
        public int maxLevel;
        public bool enabled;
    }

    [Serializable]
    public sealed class PremiumCatalog
    {
        public GemPackage[] packages = Array.Empty<GemPackage>();
        public PremiumProduct[] products = Array.Empty<PremiumProduct>();
        public int reviveCostGems = PremiumService.DefaultReviveCostGems;

        public PremiumProduct FindProduct(string id)
        {
            if (products == null) return null;
            for (int i = 0; i < products.Length; i++)
                if (products[i] != null && products[i].id == id) return products[i];
            return null;
        }

        public GemPackage FindPackage(string id)
        {
            if (packages == null) return null;
            for (int i = 0; i < packages.Length; i++)
                if (packages[i] != null && packages[i].id == id) return packages[i];
            return null;
        }
    }

    [Serializable]
    public sealed class StoreTransaction
    {
        public int id;
        public string provider;
        public string eventId;
        public string accountId;
        public string packageId;
        public int gems;
        public int amountMinor;
        public string currency;
        public string status;
        public string receiptEmail;
        public string providerRef;
        public string detail;
        public string createdUtc;

        public string StatusLabel
        {
            get
            {
                switch (status)
                {
                    case "SUCCEEDED": return "PAID";
                    case "REFUNDED": return "REFUNDED";
                    case "PARTIALLY_REFUNDED": return "PARTIALLY REFUNDED";
                    case "FAILED": return "FAILED";
                    case "PENDING": return "PENDING";
                    default: return string.IsNullOrEmpty(status) ? "UNKNOWN" : status.Replace('_', ' ');
                }
            }
        }

        public string AmountLabel
        {
            get
            {
                if (amountMinor <= 0) return "\u2014";
                return string.Format("{0:0.00} {1}", amountMinor / 100.0, currency ?? "");
            }
        }

        public string ProviderLabel
        {
            get
            {
                switch (provider)
                {
                    case "stripe": return "CARD";
                    case "apple": return "APP STORE";
                    case "google_play": return "GOOGLE PLAY";
                    default: return string.IsNullOrEmpty(provider) ? "STORE" : provider.ToUpperInvariant();
                }
            }
        }
    }

    [Serializable]
    public sealed class PremiumWallet
    {
        public int gems;
        public int pendingOrders;
    }

    [Serializable]
    public sealed class PremiumOwnershipItem
    {
        public string productId;
        public string category;
        public int level;
        public string acquiredUtc;
    }

    [Serializable]
    public sealed class PremiumOwnershipResponse
    {
        public PremiumOwnershipItem[] items = Array.Empty<PremiumOwnershipItem>();
        public string[] characters = Array.Empty<string>();
    }

    [Serializable]
    public sealed class PremiumPurchaseResult
    {
        public bool ok;
        public int gems;
        public string productId;
        public bool owned;
        public int level;
        public int credited;
        public bool processed;
        public string reason;
        public bool receiptEmailSent;
        public string receiptEmailReason;
        public StoreTransaction transaction;
    }

    [Serializable]
    public sealed class PremiumReviveResult
    {
        public bool ok;
        public int gems;
        public string reviveToken;
        public int costGems;
    }

    [Serializable]
    public sealed class PremiumAuthResult
    {
        public bool ok;
        public string token;
        public string accountId;
        public string displayName;
        public int gems;
    }

    [Serializable]
    public sealed class PremiumError
    {
        public string code;
        public string message;
        public int status;
        public bool offline;

        public string Friendly
        {
            get
            {
                switch (code)
                {
                    case "STORE_OFFLINE": return "STORE OFFLINE - CHECK THE PREMIUM SERVICE";
                    case "INSUFFICIENT_GEMS": return "NOT ENOUGH GEMS!";
                    case "ALREADY_OWNED": return "ALREADY OWNED";
                    case "MAX_LEVEL_REACHED": return "UPGRADE AT MAXIMUM";
                    case "PACKAGE_NOT_FOUND":
                    case "PRODUCT_NOT_FOUND": return "ITEM NO LONGER AVAILABLE";
                    case "PACKAGE_DISABLED":
                    case "PRODUCT_DISABLED": return "ITEM IS DISABLED";
                    case "RATE_LIMITED": return "TOO MANY REQUESTS - WAIT A MOMENT";
                    case "MANUAL_TRANSFER_DISABLED": return "USE THE IN-APP STORE CHECKOUT";
                    case "PROVIDER_NOT_CONFIGURED":
                    case "PRICE_NOT_CONFIGURED": return "PAYMENT METHOD NOT AVAILABLE YET";
                    case "INVALID_RECEIPT": return "STORE COULD NOT VERIFY THIS PURCHASE";
                    case "DUPLICATE_TRANSACTION": return "THIS PURCHASE WAS ALREADY CREDITED";
                    case "PURCHASE_FAILED": return "STORE PURCHASE CANCELLED OR FAILED";
                    case "INVALID_DEVICE_KEY": return "PROFILE IDENTITY IS INVALID";
                    case "UNAUTHORIZED": return "SIGN-IN EXPIRED - RETRY";
                    default:
                        if (!string.IsNullOrEmpty(message) && message != code)
                            return message.Replace('_', ' ');
                        return string.IsNullOrEmpty(code) ? "STORE REQUEST FAILED" : code.Replace('_', ' ');
                }
            }
        }
    }

    /// <summary>
    /// Result of a raw account-layer request. The transport is shared with the wallet service
    /// so every call uses the same base URL, timeout and bearer token, but account failures are
    /// never retried as a guest identity.
    /// </summary>
    public sealed class PremiumApiResult
    {
        public bool ok;
        public long status;
        public string text = "";
        public bool networkError;
        public string code = "";
        public string message = "";
    }

    /// <summary>
    /// HTTP client for the server-authoritative GEMS wallet. The client never invents a
    /// balance, purchase, ownership record or revival. Every money movement is performed by
    /// the backend and then mirrored into the local cache for display and offline selection.
    /// </summary>
    public sealed class PremiumService : MonoBehaviour
    {
        public const int DefaultReviveCostGems = 40;

        [Serializable] private sealed class ErrorEnvelope { public bool ok; public string error; public string code; }
        [Serializable] private sealed class AuthRequest { public string deviceKey; public string playerName; }
        [Serializable] private sealed class PlatformPaymentRequest
        {
            public string productId;
            public string receiptBase64;
            public string purchaseToken;
            public string receiptEmail;
        }
        [Serializable] private sealed class PurchaseRequest { public string productId; public string clientRequestId; }
        [Serializable] private sealed class TransactionsEnvelope
        {
            public bool ok;
            public StoreTransaction[] transactions = Array.Empty<StoreTransaction>();
        }
        [Serializable] private sealed class ReviveRequest
        {
            public string sessionId;
            public float distance;
            public int locationIndex;
            public string clientRequestId;
        }
        [Serializable] private sealed class ReviveEnvelope { public bool ok; public int gems; public string reviveToken; public int costGems; }
        [Serializable] private sealed class WalletEnvelope { public bool ok; public int gems; public int pendingOrders; public string serverTime; }
        [Serializable] private sealed class CatalogEnvelope
        {
            public bool ok;
            public GemPackage[] packages = Array.Empty<GemPackage>();
            public PremiumProduct[] products = Array.Empty<PremiumProduct>();
            public int reviveCostGems = DefaultReviveCostGems;
        }
        [Serializable] private sealed class OwnershipEnvelope
        {
            public bool ok;
            public PremiumOwnershipItem[] items = Array.Empty<PremiumOwnershipItem>();
            public string[] characters = Array.Empty<string>();
        }

        private sealed class HttpResponse
        {
            public bool ok;
            public long status;
            public string text;
            public bool networkError;
        }

        private GameApp _app;
        private string _baseUrl = "http://127.0.0.1:8088";
        private string _token = "";
        private bool _syncing;
        private int _authBusy;

        public bool Online { get; private set; }
        public bool Busy { get; private set; }
        public string Status { get; private set; } = "STORE OFFLINE";
        public string Token => _token;
        public string AccountId => Cache != null ? Cache.accountId ?? "" : "";
        public bool HasPersistentSession =>
            Cache != null && Cache.persistentSession && !string.IsNullOrEmpty(_token);
        public PremiumCatalog Catalog { get; private set; } = new PremiumCatalog();
        public PremiumError LastError { get; private set; }
        public int ReviveCostGems => Catalog != null && Catalog.reviveCostGems > 0
            ? Catalog.reviveCostGems : DefaultReviveCostGems;
        public int Gems => _app != null && _app.Profile != null && _app.Profile.premium != null
            ? _app.Profile.premium.gems : 0;
        public int PendingOrders => _app != null && _app.Profile != null && _app.Profile.premium != null
            ? _app.Profile.premium.pendingOrders : 0;
        public string ServerUrl => _baseUrl;
        public event Action Changed;

        private SaveData Profile => _app != null ? _app.Profile : null;
        private PremiumCache Cache => Profile != null ? Profile.premium : null;

        public void Initialise(GameApp app)
        {
            _app = app;
            if (app == null || app.Profile == null) return;
            _baseUrl = NormaliseUrl(app.Profile.settings.premiumServerUrl);
            _token = app.Profile.premium.token ?? "";
            if (!string.IsNullOrEmpty(app.Profile.premium.catalogJson))
            {
                try
                {
                    var cached = JsonUtility.FromJson<PremiumCatalog>(app.Profile.premium.catalogJson);
                    if (cached != null && cached.packages != null && cached.products != null)
                        Catalog = cached;
                }
                catch (Exception)
                {
                    Catalog = new PremiumCatalog();
                }
            }
            Online = false;
            Status = string.IsNullOrEmpty(_token) ? "STORE OFFLINE" : "STORE OFFLINE - CACHED";
        }

        /// <summary>
        /// Adopt a token issued by the account service. GEMS still come only from the server;
        /// the value here is a display cache refreshed by the next wallet call.
        /// </summary>
        public void AdoptSession(string token, string accountId, string displayName, int gems)
        {
            if (_app == null || Profile == null || Cache == null || string.IsNullOrEmpty(token)) return;
            _token = token;
            Cache.token = token;
            Cache.tokenIssuedUtc = DateTime.UtcNow.ToString("O");
            Cache.accountId = accountId ?? "";
            Cache.persistentSession = true;
            Cache.gems = Mathf.Max(0, gems);
            if (!string.IsNullOrEmpty(displayName)) Profile.playerName = displayName;
            if (Profile.account == null) Profile.account = new AccountCache();
            Profile.account.signedIn = true;
            Profile.account.accountId = accountId ?? "";
            _app.SaveProfile();
            Online = true;
            Status = "STORE ONLINE";
            RaiseChanged();
        }

        /// <summary>
        /// Drop an account session and the entitlement cache that belongs to it. Local
        /// progression stays intact so offline play continues; premium characters and GEMS
        /// are re-evaluated by the next server sync.
        /// </summary>
        public void ClearSession()
        {
            _token = "";
            Online = false;
            Status = "STORE OFFLINE";
            if (Cache != null)
            {
                Cache.token = "";
                Cache.accountId = "";
                Cache.persistentSession = false;
                Cache.gems = 0;
                Cache.pendingOrders = 0;
                Cache.ownedItems?.Clear();
                Cache.characters?.Clear();
                Cache.upgrades?.Clear();
            }
            if (Profile != null && Profile.account != null)
            {
                Profile.account.signedIn = false;
                Profile.account.accountId = "";
            }
            if (_app != null) _app.SaveProfile();
            RaiseChanged();
        }

        public bool IsValidEndpoint => IsValidServerUrl(_baseUrl);

        /// <summary>
        /// Send one account-layer request without the guest re-authentication fallback used by
        /// wallet mutations. Used for login, logout, sync and provider callbacks where silently
        /// changing identity would corrupt the session.
        /// </summary>
        public void CallApi(string method, string path, string json, bool authenticated,
            Action<PremiumApiResult> done)
        {
            if (!isActiveAndEnabled)
            {
                done?.Invoke(new PremiumApiResult
                {
                    ok = false,
                    status = 0,
                    networkError = true,
                    code = "STORE_OFFLINE",
                    message = "STORE OFFLINE"
                });
                return;
            }
            StartCoroutine(CallApiRoutine(method, path, json, authenticated, done));
        }

        private IEnumerator CallApiRoutine(string method, string path, string json,
            bool authenticated, Action<PremiumApiResult> done)
        {
            HttpResponse response = null;
            yield return SendRaw(method, path, json, authenticated, r => response = r);
            done?.Invoke(DecodeApiResult(response));
        }

        private static PremiumApiResult DecodeApiResult(HttpResponse response)
        {
            if (response == null)
                return new PremiumApiResult
                {
                    ok = false,
                    status = 0,
                    networkError = true,
                    code = "STORE_OFFLINE",
                    message = "STORE OFFLINE"
                };
            var result = new PremiumApiResult
            {
                ok = response.ok,
                status = response.status,
                text = response.text ?? "",
                networkError = response.networkError
            };
            if (!response.ok && !string.IsNullOrEmpty(result.text))
            {
                try
                {
                    var envelope = JsonUtility.FromJson<ErrorEnvelope>(result.text);
                    if (envelope != null)
                    {
                        result.code = envelope.code ?? "";
                        result.message = envelope.error ?? "";
                    }
                }
                catch (Exception)
                {
                    // Non-JSON failures keep the transport status as their only diagnosis.
                }
            }
            if (string.IsNullOrEmpty(result.code) && !result.ok)
                result.code = result.networkError ? "STORE_OFFLINE" : "REQUEST_FAILED";
            if (string.IsNullOrEmpty(result.message)) result.message = result.code;
            return result;
        }

        /// <summary>
        /// Applies an edited premium endpoint without restarting the app. A different host
        /// invalidates the old server's cached wallet and entitlements before re-syncing; they
        /// are never carried across authorities.
        /// </summary>
        public void Reconfigure(bool sync = true)
        {
            if (_app == null || _app.Profile == null) return;
            string next = NormaliseUrl(_app.Profile.settings.premiumServerUrl);
            if (string.Equals(next, _baseUrl, StringComparison.Ordinal)) return;

            _baseUrl = next;
            _token = "";
            Online = false;
            Busy = false;
            Status = "STORE OFFLINE";
            LastError = null;
            Catalog = new PremiumCatalog();
            if (Cache != null)
            {
                Cache.token = "";
                Cache.catalogJson = "";
                Cache.gems = 0;
                Cache.pendingOrders = 0;
                Cache.lastSyncUtc = "";
                Cache.ownedItems?.Clear();
                Cache.characters?.Clear();
                Cache.upgrades?.Clear();
            }
            RaiseChanged();
            if (sync && !_syncing) SyncAll();
        }

        public static bool IsValidServerUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        public void SyncAll(Action<bool> done = null)
        {
            if (!isActiveAndEnabled) { done?.Invoke(false); return; }
            if (_syncing) { done?.Invoke(Online); return; }
            StartCoroutine(SyncRoutine(done));
        }

        private IEnumerator SyncRoutine(Action<bool> done)
        {
            _syncing = true;
            Busy = true;
            Online = false;
            Status = "CONNECTING TO STORE...";
            LastError = null;
            RaiseChanged();

            HttpResponse health = null;
            yield return SendRaw("GET", "/api/v1/health", null, false, r => health = r);
            if (health == null || !health.ok)
            {
                FinishOffline("STORE OFFLINE", MakeOfflineError());
                done?.Invoke(false);
                yield break;
            }

            bool authenticated = false;
            if (!string.IsNullOrEmpty(_token))
            {
                // Reuse the stored bearer token. A registered account must never be silently
                // downgraded to a guest identity when its session expires.
                HttpResponse probe = null;
                yield return SendRaw("GET", "/api/v1/wallet", null, true, r => probe = r);
                if (probe != null && probe.ok)
                {
                    authenticated = true;
                    ParseWallet(probe.text);
                }
                else if (probe != null && probe.status == 401)
                {
                    if (Cache != null && Cache.persistentSession)
                    {
                        FinishOffline("SIGN-IN EXPIRED", new PremiumError
                        {
                            code = "UNAUTHORIZED",
                            message = "SIGN-IN EXPIRED",
                            status = 401
                        });
                        done?.Invoke(false);
                        yield break;
                    }
                    _token = "";
                    Cache.token = "";
                    Cache.accountId = "";
                }
            }
            if (!authenticated)
            {
                bool guest = false;
                yield return AuthenticateRoutine(result => guest = result);
                authenticated = guest;
            }
            if (!authenticated)
            {
                FinishOffline("STORE OFFLINE", LastError ?? MakeOfflineError());
                done?.Invoke(false);
                yield break;
            }

            bool catalogOk = false;
            yield return FetchCatalogRoutine(r => catalogOk = r);
            bool ownershipOk = false;
            yield return FetchOwnershipRoutine(r => ownershipOk = r);
            bool walletOk = false;
            yield return FetchWalletRoutine(r => walletOk = r);

            Online = ownershipOk || walletOk || catalogOk;
            Busy = false;
            _syncing = false;
            Status = Online ? "STORE ONLINE" : "STORE OFFLINE";
            if (Online)
            {
                Cache.lastSyncUtc = DateTime.UtcNow.ToString("O");
                _app.SaveProfile();
            }
            RaiseChanged();
            done?.Invoke(Online);
        }

        private IEnumerator AuthenticateRoutine(Action<bool> done)
        {
            if (Profile == null) { done(false); yield break; }
            if (string.IsNullOrEmpty(Cache.deviceKey))
                Cache.deviceKey = "nv-" + Guid.NewGuid().ToString("N") + "-" +
                                  DeterministicRandom.StableHash(Profile.playerId).ToString("x");
            var payload = new AuthRequest
            {
                deviceKey = Cache.deviceKey,
                playerName = string.IsNullOrEmpty(Profile.playerName) ? "RUNNER-01" : Profile.playerName
            };
            HttpResponse response = null;
            yield return SendRaw("POST", "/api/v1/auth/guest", JsonUtility.ToJson(payload), false,
                r => response = r);
            if (response == null || !response.ok)
            {
                LastError = ParseError(response);
                done(false);
                yield break;
            }
            PremiumAuthResult auth = null;
            try { auth = JsonUtility.FromJson<PremiumAuthResult>(response.text); }
            catch (Exception) { auth = null; }
            if (auth == null || string.IsNullOrEmpty(auth.token))
            {
                LastError = new PremiumError { code = "AUTH_FAILED", message = "SIGN-IN FAILED" };
                done(false);
                yield break;
            }
            _token = auth.token;
            Cache.token = auth.token;
            Cache.tokenIssuedUtc = DateTime.UtcNow.ToString("O");
            Cache.accountId = auth.accountId ?? "";
            Cache.gems = Mathf.Max(0, auth.gems);
            if (!string.IsNullOrEmpty(auth.displayName)) Profile.playerName = auth.displayName;
            done(true);
        }

        private IEnumerator FetchCatalogRoutine(Action<bool> done)
        {
            HttpResponse response = null;
            yield return SendRaw("GET", "/api/v1/store/catalog", null, false, r => response = r);
            if (response == null || !response.ok) { done(false); yield break; }
            CatalogEnvelope envelope = null;
            try { envelope = JsonUtility.FromJson<CatalogEnvelope>(response.text); }
            catch (Exception) { envelope = null; }
            if (envelope == null || !envelope.ok) { done(false); yield break; }
            Catalog = new PremiumCatalog
            {
                packages = envelope.packages ?? Array.Empty<GemPackage>(),
                products = envelope.products ?? Array.Empty<PremiumProduct>(),
                reviveCostGems = envelope.reviveCostGems > 0 ? envelope.reviveCostGems : DefaultReviveCostGems
            };
            Cache.catalogJson = JsonUtility.ToJson(Catalog);
            Cache.lastCatalogUtc = DateTime.UtcNow.ToString("O");
            done(true);
        }

        private IEnumerator FetchOwnershipRoutine(Action<bool> done)
        {
            HttpResponse response = null;
            yield return SendAuth("GET", "/api/v1/ownership", null, r => response = r);
            if (response == null || !response.ok) { done(false); yield break; }
            OwnershipEnvelope envelope = null;
            try { envelope = JsonUtility.FromJson<OwnershipEnvelope>(response.text); }
            catch (Exception) { envelope = null; }
            if (envelope == null || !envelope.ok) { done(false); yield break; }
            ApplyOwnership(envelope.items, envelope.characters);
            done(true);
        }

        private IEnumerator FetchWalletRoutine(Action<bool> done)
        {
            HttpResponse response = null;
            yield return SendAuth("GET", "/api/v1/wallet", null, r => response = r);
            if (response == null || !response.ok) { done(false); yield break; }
            if (!ParseWallet(response.text)) { done(false); yield break; }
            done(true);
        }

        public void RefreshWallet(Action<bool> done = null)
        {
            if (!isActiveAndEnabled || string.IsNullOrEmpty(_token)) { done?.Invoke(false); return; }
            StartCoroutine(RefreshWalletRoutine(done));
        }

        private IEnumerator RefreshWalletRoutine(Action<bool> done)
        {
            bool ok = false;
            yield return FetchWalletRoutine(r => ok = r);
            if (ok) _app.SaveProfile();
            RaiseChanged();
            done?.Invoke(ok);
        }

        public void RefreshOwnership(Action<bool> done = null)
        {
            if (!isActiveAndEnabled || string.IsNullOrEmpty(_token)) { done?.Invoke(false); return; }
            StartCoroutine(RefreshOwnershipPublicRoutine(done));
        }

        private IEnumerator RefreshOwnershipPublicRoutine(Action<bool> done)
        {
            bool ok = false;
            yield return FetchOwnershipRoutine(r => ok = r);
            if (ok) _app.SaveProfile();
            RaiseChanged();
            done?.Invoke(ok);
        }

        /// <summary>
        /// Verified provider purchases. The server verifies the App Store / Google Play
        /// receipt and credits GEMS atomically; the client only forwards store evidence.
        /// </summary>
        public void VerifyPlatformPurchase(string provider, string productId, string payload,
            string receiptEmail, Action<PremiumPurchaseResult> success, Action<PremiumError> failure)
        {
            if (string.IsNullOrEmpty(_token)) { failure?.Invoke(MakeOfflineError()); return; }
            string path;
            var request = new PlatformPaymentRequest
            {
                productId = productId ?? "",
                receiptEmail = receiptEmail ?? ""
            };
            if (provider == "apple")
            {
                path = "/api/v1/payments/apple";
                request.receiptBase64 = payload ?? "";
            }
            else if (provider == "google_play")
            {
                path = "/api/v1/payments/google";
                request.purchaseToken = payload ?? "";
            }
            else
            {
                failure?.Invoke(new PremiumError
                {
                    code = "UNSUPPORTED_PROVIDER",
                    message = "UNSUPPORTED STORE PROVIDER"
                });
                return;
            }
            StartCoroutine(VerifyPlatformPurchaseRoutine(path, request, success, failure));
        }

        private IEnumerator VerifyPlatformPurchaseRoutine(string path, PlatformPaymentRequest payload,
            Action<PremiumPurchaseResult> success, Action<PremiumError> failure)
        {
            HttpResponse response = null;
            yield return SendAuth("POST", path, JsonUtility.ToJson(payload), r => response = r);
            if (response == null || !response.ok)
            {
                failure?.Invoke(ParseError(response));
                yield break;
            }
            PremiumPurchaseResult result = null;
            try { result = JsonUtility.FromJson<PremiumPurchaseResult>(response.text); }
            catch (Exception) { result = null; }
            if (result == null)
            {
                failure?.Invoke(new PremiumError
                {
                    code = "INVALID_RESPONSE",
                    message = "PURCHASE VERIFICATION INVALID"
                });
                yield break;
            }
            if (result.gems < 0) result.gems = 0;
            // A duplicate receipt response carries no balance. Never let that clear the
            // cached wallet: the next wallet sync will refresh it from the server.
            if (Cache != null && result.gems > 0)
            {
                Cache.gems = result.gems;
                Cache.lastSyncUtc = DateTime.UtcNow.ToString("O");
                _app.SaveProfile();
            }
            Online = true;
            Status = "STORE ONLINE";
            RaiseChanged();
            success?.Invoke(result);
        }

        /// <summary>Provider transaction history for the signed-in account or guest wallet.</summary>
        public void FetchTransactions(Action<List<StoreTransaction>> success, Action<PremiumError> failure)
        {
            if (string.IsNullOrEmpty(_token)) { failure?.Invoke(MakeOfflineError()); return; }
            StartCoroutine(FetchTransactionsRoutine(success, failure));
        }

        private IEnumerator FetchTransactionsRoutine(
            Action<List<StoreTransaction>> success, Action<PremiumError> failure)
        {
            HttpResponse response = null;
            yield return SendAuth("GET", "/api/v1/account/transactions?limit=100", null,
                r => response = r);
            if (response == null || !response.ok)
            {
                failure?.Invoke(ParseError(response));
                yield break;
            }
            TransactionsEnvelope envelope = null;
            try { envelope = JsonUtility.FromJson<TransactionsEnvelope>(response.text); }
            catch (Exception) { envelope = null; }
            if (envelope == null)
            {
                failure?.Invoke(new PremiumError
                {
                    code = "INVALID_RESPONSE",
                    message = "TRANSACTION HISTORY INVALID"
                });
                yield break;
            }
            success?.Invoke(new List<StoreTransaction>(
                envelope.transactions ?? Array.Empty<StoreTransaction>()));
        }

        public void Purchase(string productId, string clientRequestId,
            Action<PremiumPurchaseResult> success, Action<PremiumError> failure)
        {
            if (string.IsNullOrEmpty(_token)) { failure?.Invoke(MakeOfflineError()); return; }
            if (string.IsNullOrEmpty(clientRequestId)) clientRequestId = NewRequestId();
            var payload = new PurchaseRequest { productId = productId, clientRequestId = clientRequestId };
            StartCoroutine(PurchaseRoutine(payload, success, failure));
        }

        private IEnumerator PurchaseRoutine(PurchaseRequest payload,
            Action<PremiumPurchaseResult> success, Action<PremiumError> failure)
        {
            HttpResponse response = null;
            yield return SendAuth("POST", "/api/v1/purchases", JsonUtility.ToJson(payload), r => response = r);
            if (response == null || !response.ok) { failure?.Invoke(ParseError(response)); yield break; }
            PremiumPurchaseResult result = null;
            try { result = JsonUtility.FromJson<PremiumPurchaseResult>(response.text); }
            catch (Exception) { result = null; }
            if (result == null || !result.ok)
            {
                failure?.Invoke(new PremiumError { code = "PURCHASE_FAILED", message = "PURCHASE FAILED" });
                yield break;
            }
            Cache.gems = Mathf.Max(0, result.gems);
            ApplyOwnedProduct(result.productId, result.level);
            Profile.lifetime.premiumPurchases++;
            _app.SaveProfile();
            RaiseChanged();
            success?.Invoke(result);
        }

        public void Revive(string sessionId, float distance, int locationIndex,
            string clientRequestId, Action<PremiumReviveResult> success, Action<PremiumError> failure)
        {
            if (string.IsNullOrEmpty(_token)) { failure?.Invoke(MakeOfflineError()); return; }
            if (string.IsNullOrEmpty(clientRequestId)) clientRequestId = NewRequestId();
            var payload = new ReviveRequest
            {
                sessionId = sessionId,
                distance = Mathf.Max(0f, distance),
                locationIndex = Mathf.Max(0, locationIndex),
                clientRequestId = clientRequestId
            };
            StartCoroutine(ReviveRoutine(payload, success, failure));
        }

        private IEnumerator ReviveRoutine(ReviveRequest payload,
            Action<PremiumReviveResult> success, Action<PremiumError> failure)
        {
            HttpResponse response = null;
            yield return SendAuth("POST", "/api/v1/revive", JsonUtility.ToJson(payload), r => response = r);
            if (response == null || !response.ok) { failure?.Invoke(ParseError(response)); yield break; }
            ReviveEnvelope envelope = null;
            try { envelope = JsonUtility.FromJson<ReviveEnvelope>(response.text); }
            catch (Exception) { envelope = null; }
            if (envelope == null || !envelope.ok || string.IsNullOrEmpty(envelope.reviveToken))
            {
                failure?.Invoke(new PremiumError { code = "REVIVE_FAILED", message = "REVIVE FAILED" });
                yield break;
            }
            Cache.gems = Mathf.Max(0, envelope.gems);
            _app.SaveProfile();
            RaiseChanged();
            success?.Invoke(new PremiumReviveResult
            {
                ok = true,
                gems = envelope.gems,
                reviveToken = envelope.reviveToken,
                costGems = envelope.costGems > 0 ? envelope.costGems : ReviveCostGems
            });
        }

        public static string NewRequestId() => Guid.NewGuid().ToString("N");

        private IEnumerator SendAuth(string method, string path, string json, Action<HttpResponse> done)
        {
            HttpResponse response = null;
            yield return SendRaw(method, path, json, true, r => response = r);
            if (response != null && response.status == 401 && _authBusy == 0)
            {
                _authBusy++;
                bool authenticated = false;
                yield return AuthenticateRoutine(r => authenticated = r);
                _authBusy--;
                if (authenticated)
                {
                    yield return SendRaw(method, path, json, true, r => response = r);
                }
            }
            done?.Invoke(response);
        }

        private IEnumerator SendRaw(string method, string path, string json, bool authenticated,
            Action<HttpResponse> done)
        {
            string url = _baseUrl + path;
            using (var request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 20;
                if (!string.IsNullOrEmpty(json))
                {
                    byte[] body = Encoding.UTF8.GetBytes(json);
                    request.uploadHandler = new UploadHandlerRaw(body);
                    request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                }
                if (authenticated && !string.IsNullOrEmpty(_token))
                    request.SetRequestHeader("Authorization", "Bearer " + _token);
                yield return request.SendWebRequest();
                var response = BuildResponse(request);
                if (response.status == 401)
                    LastError = new PremiumError { code = "UNAUTHORIZED", message = "UNAUTHORIZED", status = 401 };
                done?.Invoke(response);
            }
        }

        private static HttpResponse BuildResponse(UnityWebRequest request)
        {
            bool networkError = request.result == UnityWebRequest.Result.ConnectionError ||
                                request.result == UnityWebRequest.Result.DataProcessingError;
            long status = request.responseCode;
            string text = request.downloadHandler != null ? request.downloadHandler.text : "";
            return new HttpResponse
            {
                ok = request.result == UnityWebRequest.Result.Success && status >= 200 && status < 300,
                status = status,
                text = text,
                networkError = networkError || status == 0
            };
        }

        private PremiumError ParseError(HttpResponse response)
        {
            if (response == null)
                return new PremiumError { code = "STORE_OFFLINE", message = "STORE OFFLINE", offline = true };
            string code = "";
            string message = "";
            if (!string.IsNullOrEmpty(response.text))
            {
                try
                {
                    var envelope = JsonUtility.FromJson<ErrorEnvelope>(response.text);
                    if (envelope != null)
                    {
                        code = envelope.code ?? "";
                        message = envelope.error ?? "";
                    }
                }
                catch (Exception)
                {
                    code = "";
                }
            }
            if (string.IsNullOrEmpty(code))
                code = response.networkError ? "STORE_OFFLINE" : "HTTP_" + response.status;
            if (string.IsNullOrEmpty(message)) message = code;
            var error = new PremiumError
            {
                code = code,
                message = message,
                status = (int)response.status,
                offline = response.networkError || code == "STORE_OFFLINE"
            };
            LastError = error;
            return error;
        }

        private void ApplyOwnership(PremiumOwnershipItem[] items, string[] characters)
        {
            var owned = new List<PremiumOwnership>();
            var upgrades = new List<PremiumUpgradeEntry>();
            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    if (item == null || string.IsNullOrEmpty(item.productId)) continue;
                    owned.Add(new PremiumOwnership
                    {
                        productId = item.productId,
                        category = item.category,
                        level = item.level,
                        acquiredUtc = item.acquiredUtc
                    });
                    if (item.category == "upgrade")
                        AddUpgradeLevel(upgrades, item.productId, item.level);
                }
            }
            Cache.ownedItems = owned;
            Cache.upgrades = upgrades;
            Cache.characters = new List<string>(characters ?? Array.Empty<string>());
            // Mirror server entitlements into the local roster so equipping an owned premium
            // frame still works when the store is temporarily unreachable.
            for (int i = 0; i < Cache.characters.Count; i++)
            {
                string id = Cache.characters[i];
                if (!string.IsNullOrEmpty(id) && !Profile.unlockedCharacters.Contains(id))
                    Profile.unlockedCharacters.Add(id);
            }
            EnsureSelectedCharacterOwned();
        }

        private void ApplyOwnedProduct(string productId, int level)
        {
            if (string.IsNullOrEmpty(productId)) return;
            var existing = Profile.FindPremiumOwnership(productId);
            if (existing == null)
            {
                var product = Catalog != null ? Catalog.FindProduct(productId) : null;
                existing = new PremiumOwnership
                {
                    productId = productId,
                    category = product != null ? product.category : "",
                    level = level,
                    acquiredUtc = DateTime.UtcNow.ToString("O")
                };
                Cache.ownedItems.Add(existing);
            }
            else existing.level = level;

            var catalogProduct = Catalog != null ? Catalog.FindProduct(productId) : null;
            if (catalogProduct != null && catalogProduct.category == "character" &&
                !string.IsNullOrEmpty(catalogProduct.characterId))
            {
                if (!Cache.characters.Contains(catalogProduct.characterId))
                    Cache.characters.Add(catalogProduct.characterId);
                if (!Profile.unlockedCharacters.Contains(catalogProduct.characterId))
                    Profile.unlockedCharacters.Add(catalogProduct.characterId);
            }
            if (catalogProduct != null && catalogProduct.category == "upgrade")
                AddOrReplaceUpgrade(catalogProduct.upgradeId, level);
            else if (productId.StartsWith("upgrade_", StringComparison.Ordinal))
                AddOrReplaceUpgrade(productId.Substring("upgrade_".Length), level);
        }

        private void AddUpgradeLevel(List<PremiumUpgradeEntry> upgrades, string productId, int level)
        {
            var product = Catalog != null ? Catalog.FindProduct(productId) : null;
            string id = product != null && !string.IsNullOrEmpty(product.upgradeId)
                ? product.upgradeId
                : (productId.StartsWith("upgrade_", StringComparison.Ordinal)
                    ? productId.Substring("upgrade_".Length) : productId);
            upgrades.Add(new PremiumUpgradeEntry { upgradeId = id, level = Mathf.Clamp(level, 0, 3) });
        }

        private void AddOrReplaceUpgrade(string upgradeId, int level)
        {
            if (string.IsNullOrEmpty(upgradeId)) return;
            for (int i = 0; i < Cache.upgrades.Count; i++)
            {
                if (Cache.upgrades[i].upgradeId == upgradeId)
                {
                    Cache.upgrades[i].level = Mathf.Clamp(level, 0, 3);
                    return;
                }
            }
            Cache.upgrades.Add(new PremiumUpgradeEntry { upgradeId = upgradeId, level = Mathf.Clamp(level, 0, 3) });
        }

        public bool OwnsCharacter(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return false;
            if (Profile == null) return false;
            if (Profile.unlockedCharacters.Contains(characterId)) return true;
            var character = GameConfig.Current.Character(characterId);
            if (character != null && !character.premium) return true;
            return Cache != null && Cache.characters != null && Cache.characters.Contains(characterId);
        }

        private void EnsureSelectedCharacterOwned()
        {
            if (Profile == null) return;
            var selected = GameConfig.Current.Character(Profile.selectedCharacter);
            if (selected == null || !selected.premium) return;
            if (!OwnsCharacter(selected.id))
            {
                Profile.selectedCharacter = "kira";
                Cache.selectedPremiumAbility = "";
            }
        }

        private bool ParseWallet(string text)
        {
            try
            {
                var wallet = JsonUtility.FromJson<WalletEnvelope>(text);
                if (wallet == null || !wallet.ok) return false;
                Cache.gems = Mathf.Max(0, wallet.gems);
                Cache.pendingOrders = Mathf.Max(0, wallet.pendingOrders);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void FinishOffline(string status, PremiumError error)
        {
            Busy = false;
            _syncing = false;
            Online = false;
            Status = status;
            LastError = error;
            RaiseChanged();
        }

        private static PremiumError MakeOfflineError() => new PremiumError
        {
            code = "STORE_OFFLINE",
            message = "STORE OFFLINE",
            offline = true
        };

        private static string NormaliseUrl(string url)
        {
            if (!IsValidServerUrl(url)) return "http://127.0.0.1:8088";
            url = url.Trim();
            while (url.EndsWith("/", StringComparison.Ordinal)) url = url.Substring(0, url.Length - 1);
            return url;
        }

        private void RaiseChanged() => Changed?.Invoke();
    }
}
