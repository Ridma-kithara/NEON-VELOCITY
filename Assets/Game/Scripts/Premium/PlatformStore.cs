using System;
using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Premium;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

namespace NeonVelocity.Premium
{
    /// <summary>
    /// Unity IAP adapter for Apple App Store and Google Play. The client never credits GEMS:
    /// it forwards the store receipt/token to the backend, which verifies it with the provider
    /// and performs the atomic wallet credit. Purchases stay pending until that verification
    /// succeeds, so a network failure can be retried rather than losing the transaction.
    ///
    /// Store product IDs must match the backend package IDs (for example ``gems_80``). The
    /// server maps those IDs through NV_APPLE_PRODUCT_MAP / NV_GOOGLE_PLAY_PRODUCT_MAP.
    /// </summary>
    public static class PlatformStore
    {
        public const string ProviderApple = "apple";
        public const string ProviderGoogle = "google_play";

        private static PlatformStoreHost _host;

        public static bool IsSupported
        {
            get
            {
#if UNITY_IOS
                return true;
#elif UNITY_ANDROID
                return true;
#else
                return false;
#endif
            }
        }

        public static string Provider
        {
            get
            {
#if UNITY_IOS
                return ProviderApple;
#elif UNITY_ANDROID
                return ProviderGoogle;
#else
                return "";
#endif
            }
        }

        public static void Initialize(GameApp app, Action<bool, string> done)
        {
            if (!IsSupported)
            {
                done?.Invoke(false, "PLATFORM STORE UNAVAILABLE");
                return;
            }
            Host.Initialize(app, done);
        }

        public static void Purchase(GameApp app, GemPackage package, string receiptEmail,
            Action<PremiumPurchaseResult> success, Action<PremiumError> failure)
        {
            if (!IsSupported)
            {
                failure?.Invoke(new PremiumError
                {
                    code = "PLATFORM_STORE_UNAVAILABLE",
                    message = "PLATFORM STORE UNAVAILABLE"
                });
                return;
            }
            if (app == null || app.Premium == null || package == null)
            {
                failure?.Invoke(new PremiumError
                {
                    code = "STORE_OFFLINE",
                    message = "STORE OFFLINE"
                });
                return;
            }
            Host.Purchase(app, package, receiptEmail, success, failure);
        }

        private static PlatformStoreHost Host
        {
            get
            {
                if (_host != null) return _host;
                var go = new GameObject("Platform Store");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<PlatformStoreHost>();
                return _host;
            }
        }
    }

    public sealed class PlatformStoreHost : MonoBehaviour, IDetailedStoreListener
    {
        private IStoreController _controller;
        private IExtensionProvider _extensions;
        private bool _initialising;
        private bool _initialised;
        private string _error = "";
        private readonly List<Action<bool, string>> _initCallbacks =
            new List<Action<bool, string>>();
        private readonly HashSet<string> _productIds = new HashSet<string>();
        private GameApp _app;
        private string _receiptEmail = "";
        private Action<PremiumPurchaseResult> _onSuccess;
        private Action<PremiumError> _onFailure;

        public void Initialize(GameApp app, Action<bool, string> done)
        {
            if (app != null) _app = app;
            if (_initialised)
            {
                done?.Invoke(true, "");
                return;
            }
            if (!string.IsNullOrEmpty(_error))
            {
                done?.Invoke(false, _error);
                return;
            }
            if (done != null) _initCallbacks.Add(done);
            if (_initialising) return;
            _initialising = true;
            try
            {
                var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
                if (_app != null && _app.Premium != null && _app.Premium.Catalog != null
                    && _app.Premium.Catalog.packages != null)
                {
                    for (int i = 0; i < _app.Premium.Catalog.packages.Length; i++)
                    {
                        var package = _app.Premium.Catalog.packages[i];
                        if (package == null || !package.enabled || string.IsNullOrEmpty(package.id)) continue;
                        if (!_productIds.Add(package.id)) continue;
                        builder.AddProduct(package.id, ProductType.Consumable);
                    }
                }
                UnityPurchasing.Initialize(this, builder);
            }
            catch (Exception exception)
            {
                FailInitialisation("STORE INITIALISATION FAILED: " + exception.Message);
            }
        }

        private void FailInitialisation(string message)
        {
            _initialising = false;
            _initialised = false;
            _error = string.IsNullOrEmpty(message) ? "STORE INITIALISATION FAILED" : message;
            var callbacks = _initCallbacks.ToArray();
            _initCallbacks.Clear();
            for (int i = 0; i < callbacks.Length; i++) callbacks[i]?.Invoke(false, _error);
        }

        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _controller = controller;
            _extensions = extensions;
            _initialising = false;
            _initialised = true;
            _error = "";
            var callbacks = _initCallbacks.ToArray();
            _initCallbacks.Clear();
            for (int i = 0; i < callbacks.Length; i++) callbacks[i]?.Invoke(true, "");
        }

        [Obsolete]
        public void OnInitializeFailed(InitializationFailureReason error)
        {
            FailInitialisation(error.ToString());
        }

        public void OnInitializeFailed(InitializationFailureReason error, string message)
        {
            FailInitialisation(string.IsNullOrEmpty(message) ? error.ToString() : message);
        }

        public void Purchase(GameApp app, GemPackage package, string receiptEmail,
            Action<PremiumPurchaseResult> success, Action<PremiumError> failure)
        {
            _app = app;
            _receiptEmail = receiptEmail ?? "";
            _onSuccess = success;
            _onFailure = failure;
            if (!_initialised || _controller == null)
            {
                Initialize(app, (ok, message) =>
                {
                    if (ok) Purchase(app, package, receiptEmail, success, failure);
                    else failure?.Invoke(new PremiumError
                    {
                        code = "PLATFORM_STORE_UNAVAILABLE",
                        message = message
                    });
                });
                return;
            }
            var product = _controller.products.WithID(package.id);
            if (product == null || !product.availableToPurchase)
            {
                failure?.Invoke(new PremiumError
                {
                    code = "PRODUCT_NOT_AVAILABLE",
                    message = "STORE PRODUCT IS NOT AVAILABLE YET"
                });
                ClearPending();
                return;
            }
            _controller.InitiatePurchase(product);
        }

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs purchaseEvent)
        {
            var product = purchaseEvent != null ? purchaseEvent.purchasedProduct : null;
            if (product == null)
            {
                ClearPending();
                return PurchaseProcessingResult.Complete;
            }
            string payload = ExtractPayload(product);
            string provider = PlatformStore.Provider;
            if (_app == null || _app.Premium == null || string.IsNullOrEmpty(payload))
            {
                _onFailure?.Invoke(new PremiumError
                {
                    code = "INVALID_RECEIPT",
                    message = "STORE RECEIPT IS MISSING"
                });
                ClearPending();
                return PurchaseProcessingResult.Pending;
            }
            _app.Premium.VerifyPlatformPurchase(
                provider,
                product.definition.id,
                payload,
                _receiptEmail,
                result =>
                {
                    if (_controller != null) _controller.ConfirmPendingPurchase(product);
                    _onSuccess?.Invoke(result);
                    ClearPending();
                },
                error =>
                {
                    // Leave the purchase pending so Unity IAP replays it on the next launch.
                    _onFailure?.Invoke(error);
                    ClearPending();
                });
            return PurchaseProcessingResult.Pending;
        }

        private static string ExtractPayload(Product product)
        {
            string receipt = product != null ? product.receipt : "";
            if (string.IsNullOrEmpty(receipt)) return "";
            try
            {
                var parsed = JsonUtility.FromJson<UnityReceipt>(receipt);
                if (parsed != null && !string.IsNullOrEmpty(parsed.Payload))
                {
                    if (PlatformStore.Provider == PlatformStore.ProviderGoogle)
                    {
                        // Google Play's Unity receipt payload is itself JSON containing the
                        // purchase token and signature. The backend needs the raw token.
                        var google = JsonUtility.FromJson<GoogleReceipt>(parsed.Payload);
                        if (google != null && !string.IsNullOrEmpty(google.json))
                        {
                            var purchase = JsonUtility.FromJson<GooglePurchase>(google.json);
                            if (purchase != null && !string.IsNullOrEmpty(purchase.purchaseToken))
                                return purchase.purchaseToken;
                        }
                    }
                    return parsed.Payload;
                }
            }
            catch (Exception)
            {
                // Fall back to the raw payload; the backend rejects anything it cannot verify.
            }
            return receipt;
        }

        [Serializable]
        private sealed class UnityReceipt
        {
            public string Store;
            public string TransactionID;
            public string Payload;
        }

        [Serializable]
        private sealed class GoogleReceipt
        {
            public string json;
            public string signature;
        }

        [Serializable]
        private sealed class GooglePurchase
        {
            public string orderId;
            public string packageName;
            public string productId;
            public string purchaseToken;
            public int purchaseState;
        }

        [Obsolete]
        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            _onFailure?.Invoke(new PremiumError
            {
                code = "PURCHASE_FAILED",
                message = failureReason.ToString().ToUpperInvariant()
            });
            ClearPending();
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
        {
            _onFailure?.Invoke(new PremiumError
            {
                code = "PURCHASE_FAILED",
                message = (failureDescription != null
                    ? failureDescription.message
                    : "PURCHASE FAILED").ToUpperInvariant()
            });
            ClearPending();
        }

        private void ClearPending()
        {
            _onSuccess = null;
            _onFailure = null;
        }
    }
}
