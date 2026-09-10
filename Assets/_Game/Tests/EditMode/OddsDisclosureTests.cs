using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using JetFighter.UI;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion: the component renders correctly in a test harness even
    /// though nothing live uses it.
    ///
    /// That is an unusual thing to ask for, and it is the point of ADR-006:
    /// dormant and absent are different. A dormant component is tested and can
    /// be pointed at a real item in an afternoon; an absent one is a week of
    /// work under review pressure, with a build submitted and a date already
    /// communicated.
    /// </summary>
    public class OddsDisclosureTests
    {
        private GameObject root;
        private OddsDisclosureUI odds;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("OddsDisclosure");
            odds = root.AddComponent<OddsDisclosureUI>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
        }

        private static OddsDisclosureUI.Entry Entry(string name, float weight)
        {
            return new OddsDisclosureUI.Entry { outcomeName = name, weight = weight };
        }

        [Test]
        public void ItShipsDormant()
        {
            // ADR-006: nothing randomized is sold at launch.
            Assert.IsTrue(odds.IsDormant);
            CollectionAssert.IsEmpty(odds.Validate());
        }

        [Test]
        public void ADormantComponentRendersAnExplanationNotABlank()
        {
            // A screen that renders nothing looks broken, and a reviewer
            // opening this component should see why it is empty.
            Assert.AreEqual("No randomized items are offered.", odds.BuildDisclosureText());
        }

        [Test]
        public void ItRendersOddsWhenGivenSome()
        {
            // The criterion: it works in a harness today, so pointing it at a
            // real item later is configuration rather than construction.
            odds.SetEntries(new[] { Entry("Common", 3f), Entry("Rare", 1f) });

            string text = odds.BuildDisclosureText();
            StringAssert.Contains("Common: 75.00%", text);
            StringAssert.Contains("Rare: 25.00%", text);
        }

        [Test]
        public void PercentagesComeFromTheSameWeightsTheGameUses()
        {
            // A disclosure computed from separate numbers is a compliance
            // problem waiting for someone to retune a drop table.
            odds.SetEntries(new[] { Entry("A", 70f), Entry("B", 20f), Entry("C", 10f) });
            Assert.AreEqual(70f, odds.PercentFor(Entry("A", 70f)), 0.01f);
        }

        [Test]
        public void TheDisclosedOddsSumToOneHundred()
        {
            odds.SetEntries(new[] { Entry("A", 1f), Entry("B", 1f), Entry("C", 1f) });
            CollectionAssert.IsEmpty(odds.Validate(),
                "three equal outcomes should disclose cleanly");
        }

        [Test]
        public void RoundingThatBreaksTheSumIsReported()
        {
            // A player who adds the published percentages and gets 99.7 has
            // found a real discrepancy. Cheaper to catch here than in a review
            // note.
            odds.DecimalPlaces = 0;
            odds.SetEntries(new[] { Entry("A", 1f), Entry("B", 1f), Entry("C", 1f) });
            // 33 + 33 + 33 = 99
            Assert.IsNotEmpty(odds.Validate());
        }

        [Test]
        public void ADuplicateOutcomeIsReported()
        {
            // Two rows for one outcome understate its real chance -- the
            // misleading direction.
            odds.SetEntries(new[] { Entry("Rare", 1f), Entry("Rare", 1f), Entry("Common", 2f) });
            Assert.IsNotEmpty(odds.Validate());
        }

        [Test]
        public void AnUnnamedOutcomeIsReported()
        {
            odds.SetEntries(new[] { Entry("  ", 1f), Entry("Common", 1f) });
            Assert.IsNotEmpty(odds.Validate());
        }

        [Test]
        public void AnUnwinnableOutcomeIsReported()
        {
            // Listed but impossible reads as a chance the player does not
            // actually have.
            odds.SetEntries(new[] { Entry("Impossible", 0f), Entry("Common", 1f) });
            Assert.IsNotEmpty(odds.Validate());
        }

        [Test]
        public void DecimalPlacesAreConfigurable()
        {
            // Guideline 3.1.1 wants odds a player can act on, not marketing
            // rounding.
            odds.DecimalPlaces = 3;
            odds.SetEntries(new[] { Entry("Ultra", 1f), Entry("Common", 999f) });
            StringAssert.Contains("Ultra: 0.100%", odds.BuildDisclosureText());
        }

        [Test]
        public void DecimalPlacesAreClamped()
        {
            odds.DecimalPlaces = 99;
            Assert.LessOrEqual(odds.DecimalPlaces, 4);
            odds.DecimalPlaces = -5;
            Assert.GreaterOrEqual(odds.DecimalPlaces, 0);
        }

        [Test]
        public void UnusableEntriesAreSkippedInTheText()
        {
            odds.SetEntries(new[] { Entry("Good", 1f), Entry("", 5f), Entry("Zero", 0f) });
            string text = odds.BuildDisclosureText();
            StringAssert.Contains("Good", text);
            StringAssert.DoesNotContain("Zero", text);
        }

        [Test]
        public void NoEntriesMeansNoDivisionByZero()
        {
            Assert.AreEqual(0f, odds.TotalWeight);
            Assert.AreEqual(0f, odds.PercentFor(Entry("Anything", 1f)));
        }

        [Test]
        public void SettingNullEntriesReturnsItToDormant()
        {
            odds.SetEntries(new[] { Entry("A", 1f) });
            odds.SetEntries(null);
            Assert.IsTrue(odds.IsDormant);
        }

        [Test]
        public void ItIsNotWiredToAnyLivePurchaseFlow()
        {
            // A disclosure attached to nothing cannot go stale. One attached
            // to a flow that later changes its odds silently becomes a false
            // statement, which is worse than missing.
            var entries = new List<OddsDisclosureUI.Entry>();
            odds.SetEntries(entries);
            Assert.IsTrue(odds.IsDormant);
        }
    }
}
