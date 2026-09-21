using System;
using UnityEngine;
using JetFighter.Economy;

namespace JetFighter.UI
{
    /// <summary>
    /// The rewarded-ad continue offered on death.
    ///
    /// The roadmap calls this the highest-converting placement in the genre
    /// and says to build it carefully. The care that matters is not the
    /// visuals: it is that the run revives on ad *completion* and on nothing
    /// else. Reviving on ad start is fraud against the ad network; reviving on
    /// skip trains players to skip.
    ///
    /// One continue per run by default. Unlimited continues make death
    /// meaningless, and the endless run's difficulty curve is calibrated
    /// against a player who eventually dies.
    /// </summary>
    public class ContinueOnDeathUI : MonoBehaviour
    {
        [SerializeField] private AdsManager ads;

        [Tooltip("Continues allowed per run. Zero disables the offer entirely.")]
        [Min(0)]
        [SerializeField] private int continuesPerRun = 1;

        private int continuesUsed;

        /// <summary>Raised when the run should actually resume.</summary>
        public event Action OnContinueGranted;

        /// <summary>Raised when the offer ends without a continue, with why.</summary>
        public event Action<AdsManager.AdOutcome> OnContinueDeclined;

        /// <summary>Continues taken this run.</summary>
        public int ContinuesUsed => continuesUsed;

        public AdsManager Ads { get => ads; set => ads = value; }

        public int ContinuesPerRun
        {
            get => continuesPerRun;
            set => continuesPerRun = Mathf.Max(0, value);
        }

        /// <summary>Whether the offer should be shown at all.</summary>
        public bool CanOffer =>
            ads != null && continuesUsed < continuesPerRun && ads.IsRewardedAvailable;

        /// <summary>Resets for a new run.</summary>
        public void BeginRun()
        {
            continuesUsed = 0;
        }

        /// <summary>
        /// The player accepted the offer. Grants a continue only if the ad
        /// completes.
        /// </summary>
        public void AcceptOffer()
        {
            if (!CanOffer)
            {
                OnContinueDeclined?.Invoke(AdsManager.AdOutcome.NotAvailable);
                return;
            }

            // Counted when the ad completes, not when it starts. Counting at
            // start would burn the player's one continue on an ad that failed
            // to load halfway through.
            ads.ShowRewardedAd(outcome =>
            {
                if (outcome != AdsManager.AdOutcome.Completed)
                {
                    OnContinueDeclined?.Invoke(outcome);
                    return;
                }
                continuesUsed++;
                OnContinueGranted?.Invoke();
            });
        }

        /// <summary>The player declined. Separate from a failed ad, for analytics.</summary>
        public void DeclineOffer()
        {
            OnContinueDeclined?.Invoke(AdsManager.AdOutcome.Skipped);
        }
    }
}
