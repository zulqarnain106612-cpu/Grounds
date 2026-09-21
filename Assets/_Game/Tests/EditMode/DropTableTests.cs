using NUnit.Framework;
using UnityEngine;
using JetFighter.Enemy;
using JetFighter.PowerUp;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion is statistical: drop rates match configured weights over
    /// N kills, within tolerance.
    ///
    /// Rolled from an injected sequence rather than UnityEngine.Random, so the
    /// distribution is checked exactly instead of sampled and hoped for. A
    /// test that draws real random numbers and allows a wide tolerance passes
    /// on a table that is subtly wrong, and fails occasionally on one that is
    /// right.
    /// </summary>
    public class DropTableTests
    {
        private PowerUpDef common;
        private PowerUpDef rare;
        private DropTable table;

        [SetUp]
        public void SetUp()
        {
            common = ScriptableObject.CreateInstance<PowerUpDef>();
            common.name = "Common";
            rare = ScriptableObject.CreateInstance<PowerUpDef>();
            rare.name = "Rare";

            table = new DropTable { dropChance = 1f };
            table.entries.Add(new DropTable.Entry { powerUp = common, weight = 3f });
            table.entries.Add(new DropTable.Entry { powerUp = rare, weight = 1f });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(common);
            Object.DestroyImmediate(rare);
        }

        [Test]
        public void WeightsDetermineTheShareOfDrops()
        {
            // 3:1 means the common entry owns the first three quarters of the
            // roll space. Swept, not sampled.
            int commonHits = 0, rareHits = 0;
            for (int i = 0; i < 10000; i++)
            {
                PowerUpDef dropped = table.Select(i / 10000f);
                if (dropped == common) commonHits++;
                else if (dropped == rare) rareHits++;
            }
            Assert.AreEqual(7500, commonHits, 2);
            Assert.AreEqual(2500, rareHits, 2);
        }

        [Test]
        public void RetuningAWeightChangesTheShareWithNoCodeChange()
        {
            // The cell's criterion, stated as the thing a designer does.
            table.entries[1] = new DropTable.Entry { powerUp = rare, weight = 3f };
            int rareHits = 0;
            for (int i = 0; i < 10000; i++)
            {
                if (table.Select(i / 10000f) == rare) rareHits++;
            }
            Assert.AreEqual(5000, rareHits, 2);
        }

        [Test]
        public void TheDropChanceGatesTheTable()
        {
            table.dropChance = 0.25f;
            Assert.IsNotNull(table.Roll(0.24f, 0.5f));
            Assert.IsNull(table.Roll(0.25f, 0.5f), "the boundary drops when it should not");
            Assert.IsNull(table.Roll(0.9f, 0.5f));
        }

        [Test]
        public void ADropChanceOfZeroNeverDrops()
        {
            table.dropChance = 0f;
            for (int i = 0; i < 1000; i++)
            {
                Assert.IsNull(table.Roll(i / 1000f, 0.5f));
            }
        }

        [Test]
        public void ADropChanceOfOneAlwaysDrops()
        {
            table.dropChance = 1f;
            for (int i = 0; i < 1000; i++)
            {
                Assert.IsNotNull(table.Roll(i / 1000f, 0.5f));
            }
        }

        [Test]
        public void AnEmptyTableDropsNothingRatherThanThrowing()
        {
            // An enemy that drops nothing is a legitimate design, not an error.
            var empty = new DropTable { dropChance = 1f };
            Assert.IsNull(empty.Roll(0f, 0.5f));
            Assert.AreEqual(0f, empty.TotalWeight);
        }

        [Test]
        public void ZeroWeightEntriesAreSkippedRatherThanEatingRolls()
        {
            table.entries.Insert(0, new DropTable.Entry { powerUp = common, weight = 0f });
            Assert.AreEqual(4f, table.TotalWeight);
            Assert.AreEqual(rare, table.Select(0.99f));
        }

        [Test]
        public void NullEntriesAreSkipped()
        {
            // A designer clearing a slot leaves a null behind; it must not
            // become a hole in the distribution.
            table.entries.Insert(0, new DropTable.Entry { powerUp = null, weight = 5f });
            Assert.AreEqual(4f, table.TotalWeight);
            for (int i = 0; i < 1000; i++)
            {
                Assert.IsNotNull(table.Select(i / 1000f));
            }
        }

        [Test]
        public void ATableOfOnlyNullsDropsNothing()
        {
            var broken = new DropTable { dropChance = 1f };
            broken.entries.Add(new DropTable.Entry { powerUp = null, weight = 5f });
            Assert.IsNull(broken.Roll(0f, 0.5f));
        }

        [Test]
        public void TheTopOfTheRollRangeStillDrops()
        {
            // `target < cursor` is false for the last entry at exactly 1.0.
            // Left unhandled this silently drops nothing once in a few million
            // kills -- rare enough to never be reproduced, common enough to be
            // reported.
            Assert.AreEqual(rare, table.Select(1f));
        }

        [Test]
        public void OutOfRangeRollsAreClampedRatherThanExtrapolated()
        {
            Assert.AreEqual(common, table.Select(-5f));
            Assert.AreEqual(rare, table.Select(5f));
        }

        // --- the enemy's side -----------------------------------------------

        [Test]
        public void KillingAnEnemyRollsItsTable()
        {
            var def = ScriptableObject.CreateInstance<EnemyDef>();
            def.maxHealth = 10f;
            def.dropTable = table;

            var enemyObject = new GameObject("Enemy");
            var health = enemyObject.AddComponent<EnemyHealth>();
            health.Def = def;
            health.RollSource = () => 0f;

            PowerUpDef dropped = null;
            health.OnDropped.AddListener(d => dropped = d);
            health.ApplyDamage(100f);

            Assert.AreEqual(common, dropped);
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(def);
        }

        [Test]
        public void ADropIsAnnouncedOnceAndNeverAsNull()
        {
            var def = ScriptableObject.CreateInstance<EnemyDef>();
            def.maxHealth = 10f;
            def.dropTable = new DropTable { dropChance = 0f };

            var enemyObject = new GameObject("Enemy");
            var health = enemyObject.AddComponent<EnemyHealth>();
            health.Def = def;
            health.RollSource = () => 0.5f;

            int announcements = 0;
            health.OnDropped.AddListener(_ => announcements++);
            health.ApplyDamage(100f);
            health.ApplyDamage(100f);

            Assert.AreEqual(0, announcements, "a listener would have to null-check every drop");
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(def);
        }

        [Test]
        public void TheDropIsRolledBeforeDeathIsAnnounced()
        {
            // A listener that deactivates or pools the enemy on OnDied must
            // not be able to cancel the drop.
            var def = ScriptableObject.CreateInstance<EnemyDef>();
            def.maxHealth = 10f;
            def.dropTable = table;

            var enemyObject = new GameObject("Enemy");
            var health = enemyObject.AddComponent<EnemyHealth>();
            health.Def = def;
            health.RollSource = () => 0f;

            bool droppedFirst = false;
            health.OnDropped.AddListener(_ => droppedFirst = true);
            health.OnDied.AddListener(() => Assert.IsTrue(droppedFirst, "death was announced before the drop"));
            health.ApplyDamage(100f);

            Assert.IsTrue(droppedFirst);
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(def);
        }

        [Test]
        public void AnEnemyWithNoTableStillDies()
        {
            var def = ScriptableObject.CreateInstance<EnemyDef>();
            def.maxHealth = 10f;
            def.dropTable = null;

            var enemyObject = new GameObject("Enemy");
            var health = enemyObject.AddComponent<EnemyHealth>();
            health.Def = def;

            Assert.DoesNotThrow(() => health.ApplyDamage(100f));
            Assert.IsTrue(health.IsDead);
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(def);
        }
    }
}
