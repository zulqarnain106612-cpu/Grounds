using UnityEngine;

namespace JetFighter.UI.Input
{
    /// <summary>
    /// The screen split that the two hands share.
    ///
    /// Phase 1 proved the left-hand joystick ignores the right half. Phase 2
    /// has to prove the mirror for targeting. Written twice, the two halves
    /// can disagree -- and the way they disagree is either a dead column that
    /// neither hand answers, or an overlapping one where a single touch flies
    /// the jet *and* moves the reticle. Neither is visible in a screenshot.
    ///
    /// Written once, the partition is a property with a test:
    /// IsInLeft(p) XOR IsInRight(p) for every on-screen p.
    /// </summary>
    public static class ScreenRegions
    {
        /// <summary>The default divide: half the screen each.</summary>
        public const float DefaultSplit = 0.5f;

        /// <summary>
        /// Left of the divide. The boundary column itself belongs to the
        /// right hand -- an arbitrary choice, but it has to be made once and
        /// in one place, or that column belongs to both.
        /// </summary>
        public static bool IsInLeft(Vector2 screenPosition, float screenWidth, float split)
        {
            if (!IsOnScreen(screenPosition, screenWidth))
            {
                return false;
            }
            return screenPosition.x < screenWidth * Mathf.Clamp01(split);
        }

        /// <summary>At or right of the divide.</summary>
        public static bool IsInRight(Vector2 screenPosition, float screenWidth, float split)
        {
            if (!IsOnScreen(screenPosition, screenWidth))
            {
                return false;
            }
            return screenPosition.x >= screenWidth * Mathf.Clamp01(split);
        }

        /// <summary>
        /// Whether a position is on the screen at all.
        ///
        /// A zero width means the layout is mid-orientation-change and cannot
        /// be trusted, so nothing is claimed: handing the whole screen to one
        /// hand at exactly that moment is worse than a dropped touch.
        /// </summary>
        public static bool IsOnScreen(Vector2 screenPosition, float screenWidth)
        {
            return screenWidth > 0f && screenPosition.x >= 0f && screenPosition.x <= screenWidth;
        }
    }
}
