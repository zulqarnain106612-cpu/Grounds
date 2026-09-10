using NUnit.Framework;
using UnityEngine;
using JetFighter.UI.Input;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The two hands partition the screen.
    ///
    /// Phase 1 proved the left control ignores the right half. This cell owes
    /// the mirror. The property worth asserting is stronger than either: for
    /// every on-screen x, exactly one hand claims it. A gap is a dead column
    /// no touch answers; an overlap is a column where one thumb both flies the
    /// jet and moves the reticle. Neither appears in a screenshot.
    /// </summary>
    public class ScreenRegionsTests
    {
        private const float Width = 1080f;

        [Test]
        public void EveryOnScreenColumnBelongsToExactlyOneHand()
        {
            for (float x = 0f; x <= Width; x += 0.5f)
            {
                var p = new Vector2(x, 400f);
                bool left = ScreenRegions.IsInLeft(p, Width, ScreenRegions.DefaultSplit);
                bool right = ScreenRegions.IsInRight(p, Width, ScreenRegions.DefaultSplit);
                Assert.AreNotEqual(left, right, $"x={x} is claimed by {(left ? "both" : "neither")} hand");
            }
        }

        [Test]
        public void ThePartitionHoldsAtEverySplit()
        {
            foreach (float split in new[] { 0.25f, 0.4f, 0.5f, 0.75f })
            {
                for (float x = 0f; x <= Width; x += 5f)
                {
                    var p = new Vector2(x, 0f);
                    Assert.AreNotEqual(
                        ScreenRegions.IsInLeft(p, Width, split),
                        ScreenRegions.IsInRight(p, Width, split),
                        $"split={split} leaves x={x} ambiguous");
                }
            }
        }

        [Test]
        public void TheBoundaryColumnBelongsToTheRightHand()
        {
            var boundary = new Vector2(Width * 0.5f, 0f);
            Assert.IsFalse(ScreenRegions.IsInLeft(boundary, Width, 0.5f));
            Assert.IsTrue(ScreenRegions.IsInRight(boundary, Width, 0.5f));
        }

        [Test]
        public void OffScreenBelongsToNeither()
        {
            foreach (float x in new[] { -1f, Width + 1f })
            {
                var p = new Vector2(x, 0f);
                Assert.IsFalse(ScreenRegions.IsInLeft(p, Width, 0.5f));
                Assert.IsFalse(ScreenRegions.IsInRight(p, Width, 0.5f));
            }
        }

        [Test]
        public void AZeroWidthScreenIsClaimedByNeitherHand()
        {
            // Screen.width is 0 for a frame during some orientation changes.
            // Handing the whole screen to one hand at exactly that moment is
            // worse than dropping the touch.
            Assert.IsFalse(ScreenRegions.IsInLeft(Vector2.zero, 0f, 0.5f));
            Assert.IsFalse(ScreenRegions.IsInRight(Vector2.zero, 0f, 0.5f));
        }

        [Test]
        public void TheJoystickAndTheReticleAgreeOnTheDivide()
        {
            // Both delegate here; this fails the moment one of them stops.
            for (float x = 0f; x <= Width; x += 3f)
            {
                var p = new Vector2(x, 0f);
                Assert.AreEqual(ScreenRegions.IsInLeft(p, Width, 0.5f),
                    JoystickInput.IsInRegion(p, Width, 0.5f));
                Assert.AreEqual(ScreenRegions.IsInRight(p, Width, 0.5f),
                    TargetReticleInput.IsInRegion(p, Width, 0.5f));
            }
        }

        [Test]
        public void AnOutOfRangeSplitIsClampedRatherThanTrusted()
        {
            Assert.IsTrue(ScreenRegions.IsInRight(new Vector2(1f, 0f), Width, -3f));
            Assert.IsTrue(ScreenRegions.IsInLeft(new Vector2(Width - 1f, 0f), Width, 9f));
        }
    }
}
