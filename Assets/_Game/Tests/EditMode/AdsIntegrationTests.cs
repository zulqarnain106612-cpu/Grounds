using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using JetFighter.Economy;
using JetFighter.UI;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion has two halves, both of them precise: the rewarded
    /// continue revives the run only after ad *completion* -- not on start,
    /// not on skip -- and the interstitial respects the remove-ads flag.
    ///
    /// The first is the bug that ships, because on a fast test device the
    /// reward and close callbacks arrive close enough together to look
    /// identical. Here they are separate outcomes, so the distinction is
    /// mechanical rather than a matter of timing.
    /// </summary>
    public class AdsIntegrationTests
    {
        private sealed class FakeAds : AdsManager.IAdBackend
        {
            public bool IsRewardedReady { get; set; } = true;

            public bool IsInterstitialReady { get; set; } = true;

            public int RewardedShown;
            public int InterstitialShown;

            private Action<AdsManager.AdOutcome> pending;

            public void ShowRewarded(Action<AdsManager.AdOutcome> onFinished)
            {
                RewardedShown++;
                pending = onFinished;
            }

            public void ShowInterstitial(Action onClosed)
            {
                InterstitialShown++;
                onClosed?.Invoke();
            }

            /// <summary>The ad ends. Nothing happens until a test says so.</summary>
            public void Finish(AdsManager.AdOutcome outcome) => pending?.Invoke(outcome);

            /// <summary>Some SDKs call back twice on an orientation change mid-ad.</summary>
            public void FinishTwice(AdsManager.AdOutcome outcome)
            {
                pending?.Invoke(outcome);
                pending?.Invoke(outcome);
            }
        }

        private string saveDirectory;
        private GameObject root;
        private AdsManager ads;
        private ContinueOnDeathUI continueUI;
        private FakeAds backend;

        [SetUp]
        public void SetUp()
        {
            saveDirectory = Path.Combine(Path.GetTempPath(), "jetfighter-ads-" + Path.GetRandomFileName());
            Directory.CreateDirectory(saveDirectory);
            RemoveAdsFlag.SaveDirectory = saveDirectory;
            RemoveAdsFlag.InvalidateCache();

            root = new GameObject("Ads");
            ads = root.AddComponent<AdsManager>();
            backend = new FakeAds();
            ads.Bind(backend);

            continueUI = root.AddComponent<ContinueOnDeathUI>();
            continueUI.Ads = ads;
            continueUI.ContinuesPerRun = 1;
            continueUI.BeginRun();
        }

        [TearDown]
        public void TearDown()
        {
            RemoveAdsFlag.Reset();
            RemoveAdsFlag.SaveDirectory = null;
            RemoveAdsFlag.InvalidateCache();
            if (Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, true);
            }
            UnityEngine.Object.DestroyImmediate(root);
        }

        // --- the continue ---------------------------------------------------

        [Test]
        public void TheRunIsNotRevivedWhenTheAdStarts()
        {
            // The bug that ships: on a fast device, start and finish look the
            // same to a human watching.
            bool granted = false;
            continueUI.OnContinueGranted += () => granted = true;

            continueUI.AcceptOffer();
            Assert.AreEqual(1, backend.RewardedShown, "the ad never started");
            Assert.IsFalse(granted, "the run revived on ad start");
        }

        [Test]
        public void TheRunIsRevivedOnlyOnCompletion()
        {
            bool granted = false;
            continueUI.OnContinueGranted += () => granted = true;

            continueUI.AcceptOffer();
            backend.Finish(AdsManager.AdOutcome.Completed);

            Assert.IsTrue(granted);
            Assert.AreEqual(1, continueUI.ContinuesUsed);
        }

        [Test]
        public void ASkippedAdDoesNotRevive()
        {
            // Reviving on skip trains players to skip, and the ad network
            // does not pay for it.
            bool granted = false;
            AdsManager.AdOutcome declined = AdsManager.AdOutcome.Completed;
            continueUI.OnContinueGranted += () => granted = true;
            continueUI.OnContinueDeclined += o => declined = o;

            continueUI.AcceptOffer();
            backend.Finish(AdsManager.AdOutcome.Skipped);

            Assert.IsFalse(granted);
            Assert.AreEqual(AdsManager.AdOutcome.Skipped, declined);
        }

        [Test]
        public void AFailedAdDoesNotRevive()
        {
            bool granted = false;
            continueUI.OnContinueGranted += () => granted = true;
            continueUI.AcceptOffer();
            backend.Finish(AdsManager.AdOutcome.Failed);
            Assert.IsFalse(granted);
        }

        [Test]
        public void AnAbandonedAdDoesNotConsumeTheContinue()
        {
            // Counting at start would burn the player's one continue on an ad
            // that failed halfway through.
            continueUI.AcceptOffer();
            backend.Finish(AdsManager.AdOutcome.Skipped);

            Assert.AreEqual(0, continueUI.ContinuesUsed);
            Assert.IsTrue(continueUI.CanOffer, "the player lost their continue to a skipped ad");
        }

        [Test]
        public void ADoubleCallbackGrantsOneRewardNotTwo()
        {
            // Several SDKs call back twice on an orientation change mid-ad.
            int grants = 0;
            continueUI.OnContinueGranted += () => grants++;

            continueUI.AcceptOffer();
            backend.FinishTwice(AdsManager.AdOutcome.Completed);

            Assert.AreEqual(1, grants);
            Assert.AreEqual(1, ads.RewardedCompleted);
        }

        [Test]
        public void OnlyOneContinuePerRun()
        {
            // Unlimited continues make death meaningless, and the difficulty
            // curve is calibrated against a player who eventually dies.
            continueUI.AcceptOffer();
            backend.Finish(AdsManager.AdOutcome.Completed);
            Assert.IsFalse(continueUI.CanOffer);

            continueUI.AcceptOffer();
            Assert.AreEqual(1, continueUI.ContinuesUsed);
        }

        [Test]
        public void ANewRunRestoresTheOffer()
        {
            continueUI.AcceptOffer();
            backend.Finish(AdsManager.AdOutcome.Completed);
            continueUI.BeginRun();
            Assert.IsTrue(continueUI.CanOffer);
        }

        [Test]
        public void NoOfferWhenNoAdIsAvailable()
        {
            backend.IsRewardedReady = false;
            Assert.IsFalse(continueUI.CanOffer);

            AdsManager.AdOutcome declined = AdsManager.AdOutcome.Completed;
            continueUI.OnContinueDeclined += o => declined = o;
            continueUI.AcceptOffer();
            Assert.AreEqual(AdsManager.AdOutcome.NotAvailable, declined);
        }

        [Test]
        public void ZeroContinuesDisablesTheOffer()
        {
            continueUI.ContinuesPerRun = 0;
            Assert.IsFalse(continueUI.CanOffer);
        }

        // --- interstitials --------------------------------------------------

        [Test]
        public void AnInterstitialShowsWhenAdsAreNotRemoved()
        {
            Assert.IsTrue(ads.ShowInterstitial());
            Assert.AreEqual(1, backend.InterstitialShown);
        }

        [Test]
        public void RemoveAdsSuppressesInterstitials()
        {
            RemoveAdsFlag.Grant("txn-remove-ads");
            Assert.IsFalse(ads.ShowInterstitial());
            Assert.AreEqual(0, backend.InterstitialShown);
            Assert.AreEqual(1, ads.InterstitialsSuppressed);
        }

        [Test]
        public void SuppressionStillRunsTheClosedCallback()
        {
            // A caller waiting for a callback that never comes hangs on the
            // game-over screen.
            RemoveAdsFlag.Grant();
            bool continued = false;
            ads.ShowInterstitial(() => continued = true);
            Assert.IsTrue(continued);
        }

        [Test]
        public void SuppressionIsPermanentAcrossRelaunches()
        {
            // The criterion's word is "permanently".
            RemoveAdsFlag.Grant("txn-1");
            RemoveAdsFlag.InvalidateCache();
            Assert.IsTrue(RemoveAdsFlag.IsActive);
            Assert.IsFalse(ads.ShowInterstitial());
        }

        [Test]
        public void TheEntitlementIsNotStoredInTheWalletSave()
        {
            // The wallet save is the file most likely to be rewritten as the
            // economy grows, and a migration bug there would resell an
            // entitlement the player already paid for.
            RemoveAdsFlag.Grant();
            Assert.AreNotEqual(Path.GetFileName(SaveService.SavePath),
                Path.GetFileName(RemoveAdsFlag.SavePath));
        }

        [Test]
        public void RewardedAdsAreNotSuppressedByRemoveAds()
        {
            // Remove-ads buys freedom from interruption, not from an ad the
            // player deliberately chose to watch for a reward.
            RemoveAdsFlag.Grant();
            continueUI.AcceptOffer();
            Assert.AreEqual(1, backend.RewardedShown);
        }

        [Test]
        public void AnUnreadableEntitlementDoesNotThrow()
        {
            File.WriteAllText(RemoveAdsFlag.SavePath, "{ not json");
            RemoveAdsFlag.InvalidateCache();
            Assert.DoesNotThrow(() => { bool _ = RemoveAdsFlag.IsActive; });
        }

        [Test]
        public void AnInterstitialWithNoBackendDoesNotHangTheCaller()
        {
            var lonelyRoot = new GameObject("Ads2");
            var lonely = lonelyRoot.AddComponent<AdsManager>();
            bool continued = false;
            Assert.IsFalse(lonely.ShowInterstitial(() => continued = true));
            Assert.IsTrue(continued);
            UnityEngine.Object.DestroyImmediate(lonelyRoot);
        }
    }
}
