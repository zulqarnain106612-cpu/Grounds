using NUnit.Framework;
using UnityEngine;
using JetFighter.Economy;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion: the catalog loads and validates -- no duplicate or
    /// malformed ids -- before any store UI depends on it.
    ///
    /// Every fault below reaches a customer rather than a developer if it is
    /// not caught here: a duplicate id credits the wrong amount, a whitespace
    /// id makes every purchase of that product fail, and an inverted tier
    /// makes the expensive option strictly worse.
    /// </summary>
    public class ProductCatalogTests
    {
        private ProductCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            catalog = ScriptableObject.CreateInstance<ProductCatalog>();
            catalog.products.Add(Product("com.jetfighter.gems.small", 100, 0f, 1));
            catalog.products.Add(Product("com.jetfighter.gems.medium", 500, 10f, 2));
            catalog.products.Add(Product("com.jetfighter.gems.large", 1200, 25f, 3));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(catalog);
        }

        private static ProductCatalog.Product Product(string id, int gems, float bonus, int tier)
        {
            return new ProductCatalog.Product
            {
                productId = id, displayName = id, baseGems = gems, bonusPercent = bonus, tier = tier,
            };
        }

        [Test]
        public void AWellFormedCatalogValidates()
        {
            CollectionAssert.IsEmpty(catalog.Validate());
            Assert.IsTrue(catalog.IsUsable);
        }

        [Test]
        public void ADuplicateProductIdIsRejected()
        {
            // Surfaces at runtime as a purchase crediting the wrong amount --
            // a refund conversation, discovered by a customer.
            catalog.products.Add(Product("com.jetfighter.gems.small", 9999, 0f, 4));
            Assert.IsFalse(catalog.IsUsable);
            CollectionAssert.Contains(catalog.Validate(),
                "products[3]: duplicate product id 'com.jetfighter.gems.small'");
        }

        [Test]
        public void AnEmptyProductIdIsRejected()
        {
            catalog.products.Add(Product("   ", 100, 0f, 4));
            Assert.IsFalse(catalog.IsUsable);
        }

        [Test]
        public void WhitespaceAroundAProductIdIsRejected()
        {
            // Invisible in the inspector, and it makes the id not match App
            // Store Connect -- so every purchase fails with no obvious cause.
            catalog.products.Add(Product("com.jetfighter.gems.huge ", 2000, 0f, 4));
            Assert.IsFalse(catalog.IsUsable);
        }

        [Test]
        public void AProductGrantingNothingIsRejected()
        {
            catalog.products.Add(Product("com.jetfighter.gems.empty", 0, 0f, 4));
            Assert.IsFalse(catalog.IsUsable);
        }

        [Test]
        public void AHigherTierGrantingFewerGemsIsRejected()
        {
            // Makes the expensive option strictly worse; no store UI can
            // present that in a way that is not a bug.
            catalog.products.Add(Product("com.jetfighter.gems.mega", 200, 0f, 9));
            Assert.IsFalse(catalog.IsUsable);
        }

        [Test]
        public void ValidationReportsEveryFaultNotJustTheFirst()
        {
            // A designer fixing a catalog wants all of them, not one per
            // build.
            catalog.products.Add(Product("", 0, 0f, 4));
            Assert.GreaterOrEqual(catalog.Validate().Count, 2);
        }

        [Test]
        public void AnEmptyCatalogIsNotUsable()
        {
            catalog.products.Clear();
            Assert.IsFalse(catalog.IsUsable, "an empty catalog would show an empty store");
        }

        [Test]
        public void TheBonusIsAppliedToTheGrant()
        {
            Assert.AreEqual(550, ProductCatalog.GemsFor(Product("x", 500, 10f, 1)));
        }

        [Test]
        public void TheBonusRoundsDown()
        {
            // A player who computes the advertised bonus and gets one gem
            // fewer complains; one who gets one more never does. The
            // advertised number should be the floor.
            Assert.AreEqual(103, ProductCatalog.GemsFor(Product("x", 99, 4.5f, 1)));
        }

        [Test]
        public void NoBonusGrantsTheBaseExactly()
        {
            Assert.AreEqual(100, ProductCatalog.GemsFor(Product("x", 100, 0f, 1)));
        }

        [Test]
        public void LookupIsExactAndOrdinal()
        {
            // An App Store product id is an exact byte match; culture-aware
            // comparison would accept ids the store will not.
            Assert.IsTrue(catalog.TryGet("com.jetfighter.gems.small", out _));
            Assert.IsFalse(catalog.TryGet("COM.JETFIGHTER.GEMS.SMALL", out _));
            Assert.IsFalse(catalog.TryGet("com.jetfighter.gems.small ", out _));
        }

        [Test]
        public void AnUnknownProductIdReturnsFalseRatherThanThrowing()
        {
            Assert.IsFalse(catalog.TryGet("not.a.product", out _));
            Assert.IsFalse(catalog.TryGet(null, out _));
            Assert.IsFalse(catalog.TryGet("", out _));
        }

        // --- store items ----------------------------------------------------

        [Test]
        public void OnlyConsumablesCanBeRepurchased()
        {
            // A cosmetic marked repeatable charges the player twice for the
            // same hat, and there is no design in which that is intended.
            var cosmetic = ScriptableObject.CreateInstance<StoreItemDef>();
            cosmetic.itemType = StoreItemDef.ItemType.Cosmetic;
            cosmetic.repeatable = true;
            Assert.IsFalse(cosmetic.CanRepurchase);

            var consumable = ScriptableObject.CreateInstance<StoreItemDef>();
            consumable.itemType = StoreItemDef.ItemType.Consumable;
            consumable.repeatable = true;
            Assert.IsTrue(consumable.CanRepurchase);

            Object.DestroyImmediate(cosmetic);
            Object.DestroyImmediate(consumable);
        }

        [Test]
        public void AStoreItemCarriesItsOwnCurrency()
        {
            // Both currencies price items from Phase 3's enum, so the store
            // needs no per-currency code path.
            var item = ScriptableObject.CreateInstance<StoreItemDef>();
            item.currency = CurrencyType.Gems;
            item.cost = 250;
            Assert.AreEqual(CurrencyType.Gems, item.currency);
            Assert.AreEqual(250, item.cost);
            Object.DestroyImmediate(item);
        }
    }
}
