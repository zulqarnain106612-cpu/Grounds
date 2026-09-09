using System;
using System.Collections.Generic;
using UnityEngine;

namespace JetFighter.Economy
{
    /// <summary>
    /// Turns a completed store purchase into gems in the wallet.
    ///
    /// Unity IAP sits behind IStoreBackend for the same reason GameKit sits
    /// behind INativeMatch: this is real-money code that CI can never
    /// exercise, and the alternative is a class only ever tested by making
    /// sandbox purchases by hand. Every failure mode below -- a duplicate
    /// callback, an unknown product, a purchase arriving before init -- is
    /// cheap to test here and expensive to reproduce in a sandbox.
    ///
    /// Crediting is idempotent per transaction. Apple redelivers unfinished
    /// transactions on the next launch, which is a feature: it is how a
    /// purchase survives the app being killed mid-flow. But it means the same
    /// transaction id arrives more than once as a matter of course, and a
    /// credit per arrival is free gems.
    /// </summary>
    public class IAPManager : MonoBehaviour
    {
        /// <summary>
        /// The narrow slice of a store SDK this needs.
        ///
        /// Purposely smaller than Unity IAP's surface: everything here is a
        /// method a fake must implement, and everything absent cannot leak the
        /// SDK's shape into the game's.
        /// </summary>
        public interface IStoreBackend
        {
            bool IsInitialized { get; }

            event Action<string, string> OnPurchaseSucceeded;

            event Action<string, string> OnPurchaseFailed;

            void Initialize(IReadOnlyList<string> productIds);

            void Purchase(string productId);

            /// <summary>Tells the store the transaction is fully handled.</summary>
            void ConfirmPendingPurchase(string transactionId);
        }

        [SerializeField] private ProductCatalog catalog;

        private IStoreBackend backend;
        private Wallet wallet;
        private readonly HashSet<string> creditedTransactions = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Purchases credited. For a receipts screen and for tests.</summary>
        public int CreditedCount { get; private set; }

        /// <summary>Redelivered transactions already credited. Expected to be non-zero in the wild.</summary>
        public int DuplicateTransactions { get; private set; }

        /// <summary>Purchases for products the catalog does not list.</summary>
        public int UnknownProductPurchases { get; private set; }

        public bool IsReady => backend != null && backend.IsInitialized && catalog != null && catalog.IsUsable;

        public ProductCatalog Catalog { get => catalog; set => catalog = value; }

        /// <summary>Raised after gems are credited, with product id and amount.</summary>
        public event Action<string, int> OnPurchaseCredited;

        /// <summary>Raised when a purchase fails, with a reason the UI can show.</summary>
        public event Action<string, string> OnPurchaseFailed;

        /// <summary>
        /// Wires the store and the wallet.
        ///
        /// The catalog is validated before initialising: sending a malformed
        /// id to the store is how a product silently never appears, and
        /// discovering that during review costs a submission cycle.
        /// </summary>
        public bool Initialize(IStoreBackend storeBackend, Wallet targetWallet)
        {
            if (storeBackend == null || targetWallet == null || catalog == null)
            {
                return false;
            }
            if (!catalog.IsUsable)
            {
                foreach (string problem in catalog.Validate())
                {
                    Debug.LogError($"[IAPManager] catalog problem: {problem}");
                }
                return false;
            }

            Detach();
            backend = storeBackend;
            wallet = targetWallet;
            backend.OnPurchaseSucceeded += HandleSucceeded;
            backend.OnPurchaseFailed += HandleFailed;

            var ids = new List<string>();
            foreach (ProductCatalog.Product product in catalog.products)
            {
                ids.Add(product.productId);
            }
            backend.Initialize(ids);
            return true;
        }

        private void Detach()
        {
            if (backend == null)
            {
                return;
            }
            backend.OnPurchaseSucceeded -= HandleSucceeded;
            backend.OnPurchaseFailed -= HandleFailed;
        }

        private void OnDestroy()
        {
            Detach();
        }

        /// <summary>
        /// Starts a purchase. Returns false when it cannot be started at all,
        /// so the UI can distinguish "not attempted" from "declined" -- the
        /// player deserves a different message for each.
        /// </summary>
        public bool PurchaseProduct(string productId)
        {
            if (!IsReady)
            {
                OnPurchaseFailed?.Invoke(productId, "store not ready");
                return false;
            }
            if (!catalog.TryGet(productId, out _))
            {
                // Never ask the store for something the catalog does not
                // know: the grant would have no defined amount.
                OnPurchaseFailed?.Invoke(productId, "unknown product");
                return false;
            }
            backend.Purchase(productId);
            return true;
        }

        private void HandleSucceeded(string productId, string transactionId)
        {
            Credit(productId, transactionId);
        }

        /// <summary>
        /// Credits one completed purchase. Public so the redelivery and
        /// duplicate paths are testable without a store.
        /// </summary>
        public bool Credit(string productId, string transactionId)
        {
            if (wallet == null || catalog == null)
            {
                return false;
            }
            if (!catalog.TryGet(productId, out ProductCatalog.Product product))
            {
                // A product removed from the catalog but still purchasable on
                // the store. Confirming it stops an endless redelivery loop;
                // crediting an amount nobody defined would be worse.
                UnknownProductPurchases++;
                backend?.ConfirmPendingPurchase(transactionId);
                OnPurchaseFailed?.Invoke(productId, "product is not in the catalog");
                return false;
            }

            if (!string.IsNullOrEmpty(transactionId) && !creditedTransactions.Add(transactionId))
            {
                // Apple redelivers unfinished transactions on every launch.
                // That is how a purchase survives the app being killed
                // mid-flow -- and it means a credit per arrival is free gems.
                DuplicateTransactions++;
                backend?.ConfirmPendingPurchase(transactionId);
                return false;
            }

            int gems = ProductCatalog.GemsFor(product);
            wallet.Add(CurrencyType.Gems, gems);
            CreditedCount++;

            // Saved before confirming. Confirm-then-save loses the gems if the
            // process dies in between, and the store will never redeliver a
            // transaction it was told was handled.
            SaveService.Save(wallet);
            backend?.ConfirmPendingPurchase(transactionId);

            OnPurchaseCredited?.Invoke(productId, gems);
            return true;
        }

        private void HandleFailed(string productId, string reason)
        {
            OnPurchaseFailed?.Invoke(productId, reason);
        }

        /// <summary>
        /// Restores the set of already-credited transactions after a
        /// relaunch, so redelivery is recognised rather than paid twice.
        /// </summary>
        public void RestoreCreditedTransactions(IEnumerable<string> transactionIds)
        {
            if (transactionIds == null)
            {
                return;
            }
            foreach (string id in transactionIds)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    creditedTransactions.Add(id);
                }
            }
        }

        /// <summary>Transaction ids credited this session, for persistence.</summary>
        public IReadOnlyCollection<string> CreditedTransactions => creditedTransactions;
    }
}
