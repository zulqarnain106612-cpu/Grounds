using System;
using UnityEngine;
using JetFighter.Analytics;

namespace JetFighter.Economy
{
    /// <summary>
    /// Rewarded and interstitial ads, behind one seam.
    ///
    /// The criterion has a precise verb in it: the rewarded continue revives
    /// the run "only after ad completion (not on ad *start* or skip)". That is
    /// the difference between an ad network paying and not paying, and between
    /// a player who watched and one who did not -- and it is exactly the bug
    /// that ships, because on a fast test device the reward callback and the
    /// close callback arrive close enough together to look identical.
    ///
    /// So completion is tracked explicitly rather than inferred from the ad
    /// closing.
    /// </summary>
    public class AdsManager : MonoBehaviour
    {
        /// <summary>How a rewarded ad ended.</summary>
        public enum AdOutcome
        {
            Completed = 0,
            Skipped = 1,
            Failed = 2,
            NotAvailable = 3,
        }

        /// <summary>
        /// The narrow slice of an ad SDK this needs.
        ///
        /// The roadmap deliberately deferred choosing an SDK to this phase.
        /// Behind this interface the choice stays deferred: swapping networks
        /// -- which happens for revenue reasons, not technical ones -- is one
        /// implementation.
        /// </summary>
        public interface IAdBackend
        {
            bool IsRewardedReady { get; }

            bool IsInterstitialReady { get; }

            /// <summary>Shows a rewarded ad. The callback reports how it ended.</summary>
            void ShowRewarded(Action<AdOutcome> onFinished);

            void ShowInterstitial(Action onClosed);
        }

        private IAdBackend backend;

        /// <summary>Rewarded ads that ran to completion.</summary>
        public int RewardedCompleted { get; private set; }

        /// <summary>Rewarded ads started but not completed.</summary>
        public int RewardedAbandoned { get; private set; }

        /// <summary>Interstitials suppressed by the remove-ads entitlement.</summary>
        public int InterstitialsSuppressed { get; private set; }

        public int InterstitialsShown { get; private set; }

        public bool IsRewardedAvailable => backend != null && backend.IsRewardedReady;

        public void Bind(IAdBackend adBackend)
        {
            backend = adBackend;
        }

        /// <summary>
        /// Shows a rewarded ad and reports the outcome.
        ///
        /// The callback receives the outcome rather than a bare "done", so a
        /// caller physically cannot treat a skip as a completion -- which is
        /// the mistake the criterion is written against.
        /// </summary>
        public void ShowRewardedAd(Action<AdOutcome> onFinished)
        {
            if (backend == null || !backend.IsRewardedReady)
            {
                // Not available is distinct from failed: the UI offers a
                // different fallback for "try again" than for "no ad to show".
                onFinished?.Invoke(AdOutcome.NotAvailable);
                return;
            }

            bool answered = false;
            backend.ShowRewarded(outcome =>
            {
                // An SDK that calls back twice -- which several do on an
                // orientation change mid-ad -- must not grant two rewards.
                if (answered)
                {
                    return;
                }
                answered = true;

                if (outcome == AdOutcome.Completed)
                {
                    RewardedCompleted++;
                }
                else
                {
                    RewardedAbandoned++;
                }

                // Every outcome is logged, not just completions. The ratio is
                // the number worth having: completions alone cannot tell a
                // placement nobody accepts from one nobody is offered.
                AnalyticsService.LogEvent(AnalyticsEvents.AdWatched,
                    AnalyticsEvents.ParamAdOutcome, outcome.ToString());

                onFinished?.Invoke(outcome);
            });
        }

        /// <summary>
        /// Shows an interstitial unless ads were removed.
        ///
        /// Returns false when suppressed, so a caller can continue
        /// immediately rather than waiting for a callback that will not come.
        /// </summary>
        public bool ShowInterstitial(Action onClosed = null)
        {
            if (RemoveAdsFlag.IsActive)
            {
                // The entitlement is checked here, once, rather than at each
                // call site. A call site that forgot is an ad shown to someone
                // who paid not to see one -- a refund request and a review.
                InterstitialsSuppressed++;
                onClosed?.Invoke();
                return false;
            }
            if (backend == null || !backend.IsInterstitialReady)
            {
                onClosed?.Invoke();
                return false;
            }

            InterstitialsShown++;
            backend.ShowInterstitial(onClosed);
            return true;
        }
    }
}
