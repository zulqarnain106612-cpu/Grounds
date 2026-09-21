using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using JetFighter.Economy;
using JetFighter.Enemy;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion is two claims: coins accumulate from both survival and
    /// kills, and the balance survives a relaunch -- which is really a claim
    /// about SaveService round-tripping.
    ///
    /// The relaunch is simulated by saving, building a fresh Wallet, and
    /// loading into it. That is exactly what a relaunch does, and unlike an
    /// actual relaunch it can be asserted.
    /// </summary>
    public class EconomyTests
    {
        private string saveDirectory;

        [SetUp]
        public void SetUp()
        {
            saveDirectory = Path.Combine(Path.GetTempPath(), "jetfighter-economy-tests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(saveDirectory);
            SaveService.SaveDirectory = saveDirectory;
        }

        [TearDown]
        public void TearDown()
        {
            SaveService.SaveDirectory = null;
            if (Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, true);
            }
        }

        // --- the wallet -----------------------------------------------------

        [Test]
        public void AnUnknownCurrencyReadsZeroRatherThanThrowing()
        {
            Assert.AreEqual(0, new Wallet().GetBalance(CurrencyType.Gems));
        }

        [Test]
        public void AddingCredits()
        {
            var wallet = new Wallet();
            wallet.Add(CurrencyType.Coins, 40);
            wallet.Add(CurrencyType.Coins, 2);
            Assert.AreEqual(42, wallet.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void CurrenciesAreIndependent()
        {
            var wallet = new Wallet();
            wallet.Add(CurrencyType.Coins, 10);
            Assert.AreEqual(0, wallet.GetBalance(CurrencyType.Gems));
        }

        [Test]
        public void SpendingIsAllOrNothing()
        {
            // A partial spend leaves the player charged for something they
            // did not get.
            var wallet = new Wallet();
            wallet.Add(CurrencyType.Coins, 10);

            Assert.IsFalse(wallet.Spend(CurrencyType.Coins, 11));
            Assert.AreEqual(10, wallet.GetBalance(CurrencyType.Coins));
            Assert.IsTrue(wallet.Spend(CurrencyType.Coins, 10));
            Assert.AreEqual(0, wallet.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void SpendingExactlyTheBalanceIsAllowed()
        {
            var wallet = new Wallet();
            wallet.Add(CurrencyType.Coins, 7);
            Assert.IsTrue(wallet.Spend(CurrencyType.Coins, 7));
        }

        [Test]
        public void NonPositiveAmountsAreIgnored()
        {
            // An accidental negative credit is how a currency goes missing
            // with no spend to explain it.
            var wallet = new Wallet();
            wallet.Add(CurrencyType.Coins, 10);
            wallet.Add(CurrencyType.Coins, -5);
            wallet.Add(CurrencyType.Coins, 0);
            Assert.AreEqual(10, wallet.GetBalance(CurrencyType.Coins));
            Assert.IsFalse(wallet.Spend(CurrencyType.Coins, -5));
            Assert.AreEqual(10, wallet.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void BalancesSaturateRatherThanOverflow()
        {
            // A player who somehow reaches the ceiling should stay rich, not
            // go bankrupt.
            var wallet = new Wallet();
            wallet.Add(CurrencyType.Coins, int.MaxValue);
            wallet.Add(CurrencyType.Coins, 1000);
            Assert.AreEqual(int.MaxValue, wallet.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void BalanceChangesAreAnnounced()
        {
            var wallet = new Wallet();
            var seen = new List<(CurrencyType, int)>();
            wallet.OnBalanceChanged += (type, amount) => seen.Add((type, amount));

            wallet.Add(CurrencyType.Coins, 5);
            wallet.Spend(CurrencyType.Coins, 2);

            CollectionAssert.AreEqual(
                new[] { (CurrencyType.Coins, 5), (CurrencyType.Coins, 3) }, seen);
        }

        [Test]
        public void AddingAThirdCurrencyWouldNeedNoWalletChange()
        {
            // ADR-004's criterion, as close as C# allows without adding one:
            // every operation is keyed by the enum, so the only edit is the
            // enum itself.
            var wallet = new Wallet();
            foreach (CurrencyType type in System.Enum.GetValues(typeof(CurrencyType)))
            {
                wallet.Add(type, 3);
                Assert.AreEqual(3, wallet.GetBalance(type));
                Assert.IsTrue(wallet.Spend(type, 3));
            }
        }

        // --- persistence ----------------------------------------------------

        [Test]
        public void ABalanceSurvivesARelaunch()
        {
            // The criterion. A relaunch is exactly save, fresh wallet, load.
            var before = new Wallet();
            before.Add(CurrencyType.Coins, 250);
            before.Add(CurrencyType.Gems, 4);
            Assert.IsTrue(SaveService.Save(before));

            var after = new Wallet();
            Assert.IsTrue(SaveService.Load(after));
            Assert.AreEqual(250, after.GetBalance(CurrencyType.Coins));
            Assert.AreEqual(4, after.GetBalance(CurrencyType.Gems));
        }

        [Test]
        public void SavingIsIdempotent()
        {
            var wallet = new Wallet();
            wallet.Add(CurrencyType.Coins, 9);
            SaveService.Save(wallet);
            SaveService.Save(wallet);

            var loaded = new Wallet();
            SaveService.Load(loaded);
            Assert.AreEqual(9, loaded.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void AFirstLaunchIsNotAnError()
        {
            var wallet = new Wallet();
            Assert.IsFalse(SaveService.Load(wallet));
            Assert.AreEqual(0, wallet.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void ACorruptSaveIsTreatedAsEmptyRatherThanFatal()
        {
            // Refusing to start is a worse outcome than a lost balance.
            File.WriteAllText(SaveService.SavePath, "{ not json at all");
            var wallet = new Wallet();
            Assert.DoesNotThrow(() => SaveService.Load(wallet));
            Assert.AreEqual(0, wallet.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void AnUnknownCurrencyInTheSaveDoesNotDiscardTheRest()
        {
            // A save from a newer build, or a hand-edited file.
            File.WriteAllText(SaveService.SavePath,
                "{\"version\":1,\"balances\":[{\"currency\":\"Coins\",\"amount\":12}," +
                "{\"currency\":\"Doubloons\",\"amount\":99}]}");
            var wallet = new Wallet();
            SaveService.Load(wallet);
            Assert.AreEqual(12, wallet.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void ANegativeBalanceInTheSaveIsClampedRatherThanTrusted()
        {
            File.WriteAllText(SaveService.SavePath,
                "{\"version\":1,\"balances\":[{\"currency\":\"Coins\",\"amount\":-500}]}");
            var wallet = new Wallet();
            SaveService.Load(wallet);
            Assert.AreEqual(0, wallet.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void AnInterruptedWriteLeavesNoTemporaryFileBehind()
        {
            var wallet = new Wallet();
            wallet.Add(CurrencyType.Coins, 3);
            SaveService.Save(wallet);
            Assert.IsFalse(File.Exists(SaveService.SavePath + ".tmp"));
        }

        [Test]
        public void DeletingResetsProgress()
        {
            var wallet = new Wallet();
            wallet.Add(CurrencyType.Coins, 3);
            SaveService.Save(wallet);
            SaveService.Delete();

            var fresh = new Wallet();
            Assert.IsFalse(SaveService.Load(fresh));
        }

        // --- earning --------------------------------------------------------

        [Test]
        public void CoinsAccumulateFromSurvivalTime()
        {
            var earner = new GameObject("Earner").AddComponent<CoinEarnController>();
            earner.CoinsPerSecondSurvived = 2;

            for (int i = 0; i < 300; i++)
            {
                earner.Tick(1f / 60f);
            }
            Assert.AreEqual(10, earner.EarnedFromSurvival);
            Assert.AreEqual(10, earner.Wallet.GetBalance(CurrencyType.Coins));

            Object.DestroyImmediate(earner.gameObject);
        }

        [Test]
        public void SurvivalEarningsAreFrameRateIndependent()
        {
            // A fractional second dropped every frame costs a visible amount
            // over a five-minute run.
            var slow = new GameObject("Slow").AddComponent<CoinEarnController>();
            var fast = new GameObject("Fast").AddComponent<CoinEarnController>();
            slow.CoinsPerSecondSurvived = 1;
            fast.CoinsPerSecondSurvived = 1;

            for (int i = 0; i < 1800; i++) fast.Tick(1f / 60f);
            for (int i = 0; i < 900; i++) slow.Tick(1f / 30f);

            Assert.AreEqual(fast.EarnedFromSurvival, slow.EarnedFromSurvival);
            Assert.AreEqual(30, fast.EarnedFromSurvival);

            Object.DestroyImmediate(slow.gameObject);
            Object.DestroyImmediate(fast.gameObject);
        }

        [Test]
        public void CoinsAccumulateFromKills()
        {
            var earner = new GameObject("Earner").AddComponent<CoinEarnController>();
            earner.CoinsPerKill = 5;

            var def = ScriptableObject.CreateInstance<EnemyDef>();
            def.maxHealth = 10f;
            var enemyObject = new GameObject("Enemy");
            var enemy = enemyObject.AddComponent<EnemyHealth>();
            enemy.Def = def;

            earner.Track(enemy);
            enemy.ApplyDamage(100f);

            Assert.AreEqual(5, earner.EarnedFromKills);
            Assert.AreEqual(5, earner.Wallet.GetBalance(CurrencyType.Coins));

            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(earner.gameObject);
        }

        [Test]
        public void ARetrackedEnemyDoesNotPayTwice()
        {
            // Pooled enemies are tracked again on every spawn.
            var earner = new GameObject("Earner").AddComponent<CoinEarnController>();
            earner.CoinsPerKill = 5;

            var def = ScriptableObject.CreateInstance<EnemyDef>();
            def.maxHealth = 10f;
            var enemyObject = new GameObject("Enemy");
            var enemy = enemyObject.AddComponent<EnemyHealth>();
            enemy.Def = def;

            earner.Track(enemy);
            earner.Track(enemy);
            enemy.ApplyDamage(100f);

            Assert.AreEqual(5, earner.EarnedFromKills, "the kill bounty was paid twice");

            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(earner.gameObject);
        }

        [Test]
        public void BothSourcesCreditTheSameWallet()
        {
            var earner = new GameObject("Earner").AddComponent<CoinEarnController>();
            earner.CoinsPerSecondSurvived = 1;
            earner.CoinsPerKill = 3;

            for (int i = 0; i < 120; i++) earner.Tick(1f / 60f);
            earner.AwardKill();

            Assert.AreEqual(2, earner.EarnedFromSurvival);
            Assert.AreEqual(3, earner.EarnedFromKills);
            Assert.AreEqual(5, earner.Wallet.GetBalance(CurrencyType.Coins));

            Object.DestroyImmediate(earner.gameObject);
        }

        [Test]
        public void EarningsSurviveARelaunch()
        {
            // The criterion end to end.
            var earner = new GameObject("Earner").AddComponent<CoinEarnController>();
            earner.CoinsPerKill = 7;
            earner.AwardKill();
            earner.Flush();

            var reloaded = new Wallet();
            Assert.IsTrue(SaveService.Load(reloaded));
            Assert.AreEqual(7, reloaded.GetBalance(CurrencyType.Coins));

            Object.DestroyImmediate(earner.gameObject);
        }

        [Test]
        public void AZeroRateEarnsNothingRatherThanCrashing()
        {
            var earner = new GameObject("Earner").AddComponent<CoinEarnController>();
            earner.CoinsPerSecondSurvived = 0;
            earner.CoinsPerKill = 0;

            for (int i = 0; i < 600; i++) earner.Tick(1f / 60f);
            earner.AwardKill();

            Assert.AreEqual(0, earner.Wallet.GetBalance(CurrencyType.Coins));
            Object.DestroyImmediate(earner.gameObject);
        }
    }
}
