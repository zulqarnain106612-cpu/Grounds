using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;
using JetFighter.Economy;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion: a sandbox purchase completes end to end and credits
    /// gems via the existing Phase 3 Wallet.
    ///
    /// A real sandbox purchase is a manual step. What is testable -- and what
    /// a sandbox run would almost never surface -- is everything around it: a
    /// redelivered transaction, a product pulled from the catalog, a purchase
    /// arriving before initialisation. Each of those is free gems or lost
    /// gems, and each is expensive to reproduce by hand.
    /// </summary>
    public class IAPManagerTests
    {
        private sealed class FakeStore : IAPManager.IStoreBackend
        {
            public bool IsInitialized { get; private set; }

            public readonly List<string> Confirmed = new List<string>();

            public IReadOnlyList<string> InitializedWith { get; private set; }

            public event Action<string, string> OnPurchaseSucceeded;

            public event Action<string, string> OnPurchaseFailed;

            public void Initialize(IReadOnlyList<string> productIds)
            {
                InitializedWith = productIds;
                IsInitialized = true;
            }

            public void Purchase(string productId) => Succeed(productId, Guid.NewGuid().ToString());

            public void ConfirmPendingPurchase(string transactionId) => Confirmed.Add(transactionId);

            public void Succeed(string productId, string transactionId) =>
                OnPurchaseSucceeded?.Invoke(productId, transactionId);

            public void Fail(string productId, string reason) =>
                OnPurchaseFailed?.Invoke(productId, reason);
        }

        private const string SmallPack = "com.jetfighter.gems.small";

        private string saveDirectory;
        private GameObject root;
        private IAPManager iap;
        private ProductCatalog catalog;
        private FakeStore store;
        private Wallet wallet;

        [SetUp]
        public void SetUp()
        {
            saveDirectory = Path.Combine(Path.GetTempPath(), "jetfighter-iap-" + Path.GetRandomFileName());
            Directory.CreateDirectory(saveDirectory);
            SaveService.SaveDirectory = saveDirectory;

            catalog = ScriptableObject.CreateInstance<ProductCatalog>();
            catalog.products.Add(new ProductCatalog.Product
            {
                productId = SmallPack, displayName = "Small", baseGems = 100, bonusPercent = 0f, tier = 1,
            });
            catalog.products.Add(new ProductCatalog.Product
            {
                productId = "com.jetfighter.gems.large", displayName = "Large",
                baseGems = 1000, bonusPercent = 20f, tier = 2,
            });

            root = new GameObject("IAP");
            iap = root.AddComponent<IAPManager>();
            iap.Catalog = catalog;

            store = new FakeStore();
            wallet = new Wallet();
            Assert.IsTrue(iap.Initialize(store, wallet));
        }

        [TearDown]
        public void TearDown()
        {
            SaveService.SaveDirectory = null;
            if (Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, true);
            }
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(catalog);
        }

        [Test]
        public void APurchaseCreditsGemsThroughThePhase3Wallet()
        {
            // The criterion, minus the sandbox.
            Assert.IsTrue(iap.PurchaseProduct(SmallPack));
            Assert.AreEqual(100, wallet.GetBalance(CurrencyType.Gems));
            Assert.AreEqual(1, iap.CreditedCount);
        }

        [Test]
        public void TheTierBonusIsCredited()
        {
            iap.PurchaseProduct("com.jetfighter.gems.large");
            Assert.AreEqual(1200, wallet.GetBalance(CurrencyType.Gems));
        }

        [Test]
        public void CoinsAreUntouchedByAPurchase()
        {
            // ADR-004: gems are bought, coins are earned.
            wallet.Add(CurrencyType.Coins, 50);
            iap.PurchaseProduct(SmallPack);
            Assert.AreEqual(50, wallet.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void ARedeliveredTransactionIsNotCreditedTwice()
        {
            // Apple redelivers unfinished transactions on every launch -- that
            // is how a purchase survives the app being killed mid-flow. A
            // credit per arrival is free gems.
            iap.Credit(SmallPack, "txn-1");
            iap.Credit(SmallPack, "txn-1");
            iap.Credit(SmallPack, "txn-1");

            Assert.AreEqual(100, wallet.GetBalance(CurrencyType.Gems));
            Assert.AreEqual(2, iap.DuplicateTransactions);
        }

        [Test]
        public void ARedeliveredTransactionIsStillConfirmed()
        {
            // Otherwise the store redelivers it forever, on every launch.
            iap.Credit(SmallPack, "txn-1");
            store.Confirmed.Clear();
            iap.Credit(SmallPack, "txn-1");
            CollectionAssert.Contains(store.Confirmed, "txn-1");
        }

        [Test]
        public void DistinctTransactionsAreBothCredited()
        {
            iap.Credit(SmallPack, "txn-1");
            iap.Credit(SmallPack, "txn-2");
            Assert.AreEqual(200, wallet.GetBalance(CurrencyType.Gems));
        }

        [Test]
        public void RedeliveryAfterARelaunchIsRecognised()
        {
            // The set lives in memory, so a relaunch has to be told what was
            // already paid or the first redelivery is free gems.
            iap.Credit(SmallPack, "txn-1");

            var freshRoot = new GameObject("IAP2");
            var fresh = freshRoot.AddComponent<IAPManager>();
            fresh.Catalog = catalog;
            var freshStore = new FakeStore();
            fresh.Initialize(freshStore, wallet);
            fresh.RestoreCreditedTransactions(new[] { "txn-1" });

            fresh.Credit(SmallPack, "txn-1");
            Assert.AreEqual(100, wallet.GetBalance(CurrencyType.Gems));
            Assert.AreEqual(1, fresh.DuplicateTransactions);

            UnityEngine.Object.DestroyImmediate(freshRoot);
        }

        [Test]
        public void APurchaseIsSavedBeforeItIsConfirmed()
        {
            // Confirm-then-save loses the gems if the process dies in
            // between, and the store never redelivers a transaction it was
            // told was handled.
            iap.Credit(SmallPack, "txn-1");

            var reloaded = new Wallet();
            Assert.IsTrue(SaveService.Load(reloaded));
            Assert.AreEqual(100, reloaded.GetBalance(CurrencyType.Gems));
            CollectionAssert.Contains(store.Confirmed, "txn-1");
        }

        [Test]
        public void APurchaseForAProductNotInTheCatalogIsNotCredited()
        {
            // A product pulled from the catalog but still purchasable on the
            // store. Crediting an amount nobody defined would be worse than
            // refusing.
            Assert.IsFalse(iap.Credit("com.jetfighter.gems.retired", "txn-9"));
            Assert.AreEqual(0, wallet.GetBalance(CurrencyType.Gems));
            Assert.AreEqual(1, iap.UnknownProductPurchases);
        }

        [Test]
        public void AnUnknownProductIsStillConfirmedToStopRedeliveryLooping()
        {
            iap.Credit("com.jetfighter.gems.retired", "txn-9");
            CollectionAssert.Contains(store.Confirmed, "txn-9");
        }

        [Test]
        public void BuyingAProductTheCatalogDoesNotListIsRefusedBeforeTheStore()
        {
            // Never ask the store for something whose grant has no defined
            // amount.
            string failedProduct = null;
            iap.OnPurchaseFailed += (id, _) => failedProduct = id;

            Assert.IsFalse(iap.PurchaseProduct("com.jetfighter.gems.nope"));
            Assert.AreEqual("com.jetfighter.gems.nope", failedProduct);
            Assert.AreEqual(0, iap.CreditedCount);
        }

        [Test]
        public void AFailedPurchaseReportsAReasonForTheUI()
        {
            string reason = null;
            iap.OnPurchaseFailed += (_, r) => reason = r;
            store.Fail(SmallPack, "user cancelled");
            Assert.AreEqual("user cancelled", reason);
            Assert.AreEqual(0, wallet.GetBalance(CurrencyType.Gems));
        }

        [Test]
        public void NotAttemptedIsDistinguishableFromDeclined()
        {
            // The player deserves a different message for each.
            var lonelyRoot = new GameObject("IAP3");
            var lonely = lonelyRoot.AddComponent<IAPManager>();
            lonely.Catalog = catalog;

            string reason = null;
            lonely.OnPurchaseFailed += (_, r) => reason = r;
            Assert.IsFalse(lonely.PurchaseProduct(SmallPack));
            Assert.AreEqual("store not ready", reason);

            UnityEngine.Object.DestroyImmediate(lonelyRoot);
        }

        [Test]
        public void AMalformedCatalogRefusesToInitialise()
        {
            // Sending a malformed id to the store is how a product silently
            // never appears, and discovering that during review costs a
            // submission cycle.
            catalog.products.Add(new ProductCatalog.Product
            {
                productId = SmallPack, baseGems = 5, tier = 3,
            });

            var freshRoot = new GameObject("IAP4");
            var fresh = freshRoot.AddComponent<IAPManager>();
            fresh.Catalog = catalog;

            // The refusal is meant to be loud -- a silent one is the bug this
            // guards. Expected rather than muted, so the message keeps being
            // asserted instead of being allowed to disappear.
            // Both problems the catalog reports, in the order Validate walks
            // them: the per-product faults first, then the cross-tier one. A
            // duplicated id at a higher tier is also a tier that grants fewer
            // gems than the one below it.
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[IAPManager\] catalog problem: .*duplicate product id"));
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[IAPManager\] catalog problem: tier .* grants fewer gems than"));
            Assert.IsFalse(fresh.Initialize(new FakeStore(), new Wallet()));

            UnityEngine.Object.DestroyImmediate(freshRoot);
        }

        [Test]
        public void TheStoreIsInitialisedWithEveryCatalogProduct()
        {
            CollectionAssert.AreEquivalent(
                new[] { SmallPack, "com.jetfighter.gems.large" }, store.InitializedWith);
        }

        [Test]
        public void ANullBackendOrWalletIsRefused()
        {
            var freshRoot = new GameObject("IAP5");
            var fresh = freshRoot.AddComponent<IAPManager>();
            fresh.Catalog = catalog;
            Assert.IsFalse(fresh.Initialize(null, new Wallet()));
            Assert.IsFalse(fresh.Initialize(new FakeStore(), null));
            UnityEngine.Object.DestroyImmediate(freshRoot);
        }

        [Test]
        public void CreditingAnnouncesTheProductAndAmount()
        {
            string product = null;
            int amount = 0;
            iap.OnPurchaseCredited += (id, gems) => { product = id; amount = gems; };

            iap.Credit(SmallPack, "txn-1");
            Assert.AreEqual(SmallPack, product);
            Assert.AreEqual(100, amount);
        }
    }
}
