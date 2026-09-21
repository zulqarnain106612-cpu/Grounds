using NUnit.Framework;
using UnityEngine;
using JetFighter.UI.Input;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The left-region guarantee and the normalization, as assertions rather
    /// than device observations.
    ///
    /// docs/PHASE1_TECHNICAL_SPEC.md section 4 asks that right-half touches
    /// produce *zero* effect. "We tried it and nothing happened" does not
    /// prove zero; a boundary sweep does.
    /// </summary>
    public class JoystickInputTests
    {
        private const float Width = 1000f;
        private const float Half = 0.5f;

        [Test]
        public void TheLeftEdgeIsInside()
        {
            Assert.IsTrue(JoystickInput.IsInRegion(new Vector2(0f, 400f), Width, Half));
        }

        [Test]
        public void TheMidpointBelongsToTheRightHand()
        {
            // Exactly half is excluded on purpose: the two halves must not
            // both claim the centre column, or one pixel of the screen fires
            // movement and targeting at once.
            Assert.IsFalse(JoystickInput.IsInRegion(new Vector2(Width * Half, 400f), Width, Half));
            Assert.IsTrue(JoystickInput.IsInRegion(new Vector2(Width * Half - 0.01f, 400f), Width, Half));
        }

        [Test]
        public void EveryPointInTheRightHalfIsRejected()
        {
            for (float x = Width * Half; x <= Width; x += 5f)
            {
                Assert.IsFalse(JoystickInput.IsInRegion(new Vector2(x, 300f), Width, Half),
                    $"x={x} was accepted by the left-hand control");
            }
        }

        [Test]
        public void OffScreenTouchesAreRejected()
        {
            Assert.IsFalse(JoystickInput.IsInRegion(new Vector2(-1f, 300f), Width, Half));
        }

        [Test]
        public void AZeroWidthScreenRejectsEverything()
        {
            // Screen.width is 0 for a frame during some orientation changes.
            // Accepting everything there would hand the whole screen to the
            // joystick at exactly the moment the layout is untrustworthy.
            Assert.IsFalse(JoystickInput.IsInRegion(Vector2.zero, 0f, Half));
        }

        [Test]
        public void TheRegionFractionIsHonoured()
        {
            Assert.IsTrue(JoystickInput.IsInRegion(new Vector2(200f, 0f), Width, 0.3f));
            Assert.IsFalse(JoystickInput.IsInRegion(new Vector2(400f, 0f), Width, 0.3f));
        }

        [Test]
        public void ACentredStickReadsZero()
        {
            Assert.AreEqual(Vector2.zero, JoystickInput.Normalize(Vector2.zero, 120f));
        }

        [Test]
        public void FullDeflectionReadsOne()
        {
            Assert.AreEqual(1f, JoystickInput.Normalize(new Vector2(120f, 0f), 120f).magnitude, 1e-4f);
        }

        [Test]
        public void ACornerPullIsNotFasterThanACardinalOne()
        {
            Vector2 corner = JoystickInput.Normalize(new Vector2(120f, 120f), 120f);
            Assert.AreEqual(1f, corner.magnitude, 1e-4f);
        }

        [Test]
        public void OverTravelIsClampedRatherThanAmplified()
        {
            Assert.AreEqual(1f, JoystickInput.Normalize(new Vector2(9000f, 0f), 120f).magnitude, 1e-4f);
        }

        [Test]
        public void AZeroRadiusReadsZeroInsteadOfDividingByIt()
        {
            Assert.AreEqual(Vector2.zero, JoystickInput.Normalize(new Vector2(5f, 5f), 0f));
        }

        [Test]
        public void HalfDeflectionReadsHalf()
        {
            Assert.AreEqual(0.5f, JoystickInput.Normalize(new Vector2(60f, 0f), 120f).magnitude, 1e-4f);
        }
    }
}
