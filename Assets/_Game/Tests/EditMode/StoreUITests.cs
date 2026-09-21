using System.IO;
using NUnit.Framework;
using UnityEngine;
using JetFighter.Economy;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion: both coin- and gem-priced items purchase correctly and
    /// deduct the right currency, and the insufficient-balance case is handled
    /// with no negative balances, ever.
    ///
    /// "Ever" is the word that makes this a sweep rather than a spot check.
    /// </summary>
    public class StoreUITests
    {
        private string saveDirectory;
        private GameObject root;
        private StoreUI store;
        private Wallet wallet;
        private StoreItemDef coinHat;
        private StoreItemDef gemSkin;
        private StoreItemDef coinPotion;

        [SetUp]
        public void SetUp()
        {
            saveDirectory = Path.Combine(Path.GetTempPath(), "jetfighter-store-" + Path.GetRandomFileName());
            Directory.CreateDirectory(saveDirectory);
            SaveService.SaveDirectory = saveDirectory;

            coinHat = Item("item.hat", CurrencyType.Coins, 100, StoreItemDef.ItemType.Cosmetic, false);
            gemSkin = Item("item.skin", CurrencyType.Gems, 40, StoreItemDef.ItemType.Cosmetic, false);
            coinPotion = Item("item.potion", CurrencyType.Coins, 25, StoreItemDef.ItemType.Consumable, true);

            root = new GameObject("Store");
            store = root.AddComponent<StoreUI>();
            store.AddItem(coinHat);
            store.AddItem(gemSkin);
            store.AddItem(coinPotion);

            wallet = new Wallet();
            wallet.Add(CurrencyType.Coins, 150);
            wallet.Add(CurrencyType.Gems, 50);
            store.Bind(wallet);
        }

        private static StoreItemDef Item(string id, CurrencyType currency, int cost,
            StoreItemDef.ItemType type, bool repeatable)
        {
            var item = ScriptableObject.CreateInstance<StoreItemDef>();
            item.itemId = id;
            item.currency = currency;
            item.cost = cost;
            item.itemType = type;
            item.repeatable = repeatable;
            return item;
        }

        [TearDown]
        public void TearDown()
        {
            SaveService.SaveDirectory = null;
            if (Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, true);
            }
            Object.DestroyImmediate(root);
            foreach (var asset in new[] { coinHat, gemSkin, coinPotion })
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ACoinItemDeductsCoins()
        {
            Assert.AreEqual(StoreUI.PurchaseResult.Purchased, store.Purchase(coinHat));
            Assert.AreEqual(50, wallet.GetBalance(CurrencyType.Coins));
            Assert.AreEqual(50, wallet.GetBalance(CurrencyType.Gems), "gems were touched by a coin purchase");
        }

        [Test]
        public void AGemItemDeductsGems()
        {
            Assert.AreEqual(StoreUI.PurchaseResult.Purchased, store.Purchase(gemSkin));
            Assert.AreEqual(10, wallet.GetBalance(CurrencyType.Gems));
            Assert.AreEqual(150, wallet.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void AnUnaffordableItemIsRefusedAndChangesNothing()
        {
            var expensive = Item("item.jet", CurrencyType.Coins, 9999, StoreItemDef.ItemType.Cosmetic, false);
            store.AddItem(expensive);

            Assert.AreEqual(StoreUI.PurchaseResult.InsufficientFunds, store.Purchase(expensive));
            Assert.AreEqual(150, wallet.GetBalance(CurrencyType.Coins));
            Assert.IsFalse(store.IsOwned(expensive), "a refused purchase granted the item anyway");

            Object.DestroyImmediate(expensive);
        }

        [Test]
        public void NoSequenceOfPurchasesEverProducesANegativeBalance()
        {
            // "No negative balances, ever" is a sweep, not a spot check.
            var everything = new[] { coinHat, gemSkin, coinPotion };
            for (int i = 0; i < 500; i++)
            {
                store.Purchase(everything[i % everything.Length]);
                Assert.GreaterOrEqual(wallet.GetBalance(CurrencyType.Coins), 0);
                Assert.GreaterOrEqual(wallet.GetBalance(CurrencyType.Gems), 0);
            }
        }

        [Test]
        public void SpendingExactlyTheBalanceIsAllowed()
        {
            var exact = Item("item.exact", CurrencyType.Gems, 50, StoreItemDef.ItemType.Cosmetic, false);
            store.AddItem(exact);

            Assert.AreEqual(StoreUI.PurchaseResult.Purchased, store.Purchase(exact));
            Assert.AreEqual(0, wallet.GetBalance(CurrencyType.Gems));

            Object.DestroyImmediate(exact);
        }

        [Test]
        public void OneCoinShortIsRefused()
        {
            var justTooMuch = Item("item.close", CurrencyType.Coins, 151, StoreItemDef.ItemType.Cosmetic, false);
            store.AddItem(justTooMuch);
            Assert.AreEqual(StoreUI.PurchaseResult.InsufficientFunds, store.Purchase(justTooMuch));
            Object.DestroyImmediate(justTooMuch);
        }

        [Test]
        public void ACosmeticCannotBeBoughtTwice()
        {
            store.Purchase(coinHat);
            Assert.AreEqual(StoreUI.PurchaseResult.AlreadyOwned, store.Purchase(coinHat));
            Assert.AreEqual(50, wallet.GetBalance(CurrencyType.Coins), "the player paid twice for one hat");
        }

        [Test]
        public void AConsumableCanBeBoughtRepeatedly()
        {
            Assert.AreEqual(StoreUI.PurchaseResult.Purchased, store.Purchase(coinPotion));
            Assert.AreEqual(StoreUI.PurchaseResult.Purchased, store.Purchase(coinPotion));
            Assert.AreEqual(100, wallet.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void AnItemNotOnTheShelfIsRefused()
        {
            // A stale UI reference after a catalog change looks exactly like
            // this.
            var stranger = Item("item.stranger", CurrencyType.Coins, 1, StoreItemDef.ItemType.Cosmetic, false);
            Assert.AreEqual(StoreUI.PurchaseResult.UnknownItem, store.Purchase(stranger));
            Assert.AreEqual(150, wallet.GetBalance(CurrencyType.Coins));
            Object.DestroyImmediate(stranger);
        }

        [Test]
        public void ANullItemIsRefusedRatherThanThrowing()
        {
            Assert.AreEqual(StoreUI.PurchaseResult.UnknownItem, store.Purchase(null));
        }

        [Test]
        public void AStoreWithNoWalletIsNotReady()
        {
            var lonelyRoot = new GameObject("Store2");
            var lonely = lonelyRoot.AddComponent<StoreUI>();
            lonely.AddItem(coinHat);
            Assert.AreEqual(StoreUI.PurchaseResult.NotReady, lonely.Purchase(coinHat));
            Object.DestroyImmediate(lonelyRoot);
        }

        [Test]
        public void TheButtonStateAgreesWithWhatPurchaseWillDo()
        {
            // Two places deciding affordability is how a button stays enabled
            // for an item the wallet refuses -- which the player reads as the
            // game taking their coins and giving nothing.
            var everything = new[] { coinHat, gemSkin, coinPotion };
            for (int i = 0; i < 200; i++)
            {
                StoreItemDef item = everything[i % everything.Length];
                bool predicted = store.CanPurchase(item);
                StoreUI.PurchaseResult actual = store.Purchase(item);
                Assert.AreEqual(predicted, actual == StoreUI.PurchaseResult.Purchased,
                    $"CanPurchase disagreed with Purchase for {item.itemId} on iteration {i}");
            }
        }

        [Test]
        public void RefusalsReportWhy()
        {
            StoreUI.PurchaseResult reason = StoreUI.PurchaseResult.Purchased;
            store.OnRefused += (_, r) => reason = r;

            store.Purchase(coinHat);
            store.Purchase(coinHat);
            Assert.AreEqual(StoreUI.PurchaseResult.AlreadyOwned, reason);
        }

        [Test]
        public void APurchaseIsAnnouncedWithItsCost()
        {
            StoreItemDef bought = null;
            int paid = 0;
            store.OnPurchased += (item, cost) => { bought = item; paid = cost; };

            store.Purchase(gemSkin);
            Assert.AreSame(gemSkin, bought);
            Assert.AreEqual(40, paid);
        }

        [Test]
        public void ThePurchaseSurvivesARelaunch()
        {
            // An item bought and lost to a crash is a support ticket -- and
            // for a gem purchase it is a refund.
            store.Purchase(coinHat);

            var reloaded = new Wallet();
            Assert.IsTrue(SaveService.Load(reloaded));
            Assert.AreEqual(50, reloaded.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void RestoredOwnershipPreventsAResale()
        {
            var freshRoot = new GameObject("Store3");
            var fresh = freshRoot.AddComponent<StoreUI>();
            fresh.AddItem(coinHat);
            fresh.Bind(wallet);
            fresh.RestoreOwned(new[] { "item.hat" });

            Assert.AreEqual(StoreUI.PurchaseResult.AlreadyOwned, fresh.Purchase(coinHat));
            Object.DestroyImmediate(freshRoot);
        }

        [Test]
        public void ItemsCanBeListedPerCurrency()
        {
            Assert.AreEqual(2, store.ItemsFor(CurrencyType.Coins).Count);
            Assert.AreEqual(1, store.ItemsFor(CurrencyType.Gems).Count);
        }
    }
}
