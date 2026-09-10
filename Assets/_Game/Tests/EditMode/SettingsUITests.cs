using System.IO;
using NUnit.Framework;
using UnityEngine;
using JetFighter.Build;
using JetFighter.Settings;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion: a manual override persists across relaunch and visibly
    /// changes render settings.
    ///
    /// Both halves have a specific failure. An override that does not persist
    /// makes the setting look broken. One that persists without applying makes
    /// it look like a lie -- the toggle stays where the player put it and
    /// nothing changes -- which is the harder of the two to notice in a
    /// playtest.
    /// </summary>
    public class SettingsUITests
    {
        private string saveDirectory;
        private GameObject root;
        private SettingsUI settings;

        [SetUp]
        public void SetUp()
        {
            saveDirectory = Path.Combine(Path.GetTempPath(), "jetfighter-settings-" + Path.GetRandomFileName());
            Directory.CreateDirectory(saveDirectory);
            SettingsUI.SaveDirectory = saveDirectory;

            root = new GameObject("Settings");
            settings = root.AddComponent<SettingsUI>();
        }

        [TearDown]
        public void TearDown()
        {
            SettingsUI.SaveDirectory = null;
            if (Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, true);
            }
            Object.DestroyImmediate(root);
        }

        /// <summary>A relaunch: a fresh component reading the same file.</summary>
        private SettingsUI Relaunch()
        {
            var freshRoot = new GameObject("Settings2");
            var fresh = freshRoot.AddComponent<SettingsUI>();
            fresh.Load();
            fresh.Apply();
            return fresh;
        }

        [Test]
        public void WithNoOverrideTheDetectedTierIsUsed()
        {
            Assert.IsFalse(settings.HasOverride);
            Assert.AreEqual(settings.DetectedTier, settings.EffectiveTier);
        }

        [Test]
        public void AnOverrideTakesEffectImmediately()
        {
            settings.SetTierOverride(QualityTierManager.Tier.Low);
            Assert.IsTrue(settings.HasOverride);
            Assert.AreEqual(QualityTierManager.Tier.Low, settings.EffectiveTier);
        }

        [Test]
        public void AnOverrideVisiblyChangesTheRenderBudget()
        {
            // Persisting without applying makes the setting look like a lie,
            // and it is the harder half to notice in a playtest.
            settings.SetTierOverride(QualityTierManager.Tier.High);
            int highBudget = QualityTierManager.EnemyBudget;

            settings.SetTierOverride(QualityTierManager.Tier.Low);
            Assert.Less(QualityTierManager.EnemyBudget, highBudget,
                "the tier changed but nothing about rendering did");
            Assert.AreEqual(QualityTierManager.Tier.Low, QualityTierManager.Current);
        }

        [Test]
        public void TheOverrideSurvivesARelaunch()
        {
            settings.SetTierOverride(QualityTierManager.Tier.Low);

            SettingsUI fresh = Relaunch();
            Assert.IsTrue(fresh.HasOverride);
            Assert.AreEqual(QualityTierManager.Tier.Low, fresh.EffectiveTier);
            Object.DestroyImmediate(fresh.gameObject);
        }

        [Test]
        public void TheOverrideIsAppliedOnRelaunchNotJustRemembered()
        {
            settings.SetTierOverride(QualityTierManager.Tier.Low);
            QualityTierManager.ApplySettings(QualityTierManager.Tier.High);

            SettingsUI fresh = Relaunch();
            Assert.AreEqual(QualityTierManager.Tier.Low, QualityTierManager.Current,
                "the setting was remembered but never applied");
            Object.DestroyImmediate(fresh.gameObject);
        }

        [Test]
        public void ClearingReturnsToTheDetectedTier()
        {
            // A player who picked High on a device that cannot sustain it
            // needs a way back to "whatever this device can do".
            settings.SetTierOverride(QualityTierManager.Tier.High);
            settings.ClearOverride();

            Assert.IsFalse(settings.HasOverride);
            Assert.AreEqual(settings.DetectedTier, settings.EffectiveTier);
        }

        [Test]
        public void ClearingSurvivesARelaunch()
        {
            settings.SetTierOverride(QualityTierManager.Tier.Low);
            settings.ClearOverride();

            SettingsUI fresh = Relaunch();
            Assert.IsFalse(fresh.HasOverride);
            Object.DestroyImmediate(fresh.gameObject);
        }

        [Test]
        public void DetectionIsNotOverwrittenByAnOverride()
        {
            QualityTierManager.Tier detected = settings.DetectedTier;
            settings.SetTierOverride(QualityTierManager.Tier.High);
            Assert.AreEqual(detected, settings.DetectedTier);
        }

        [Test]
        public void TheLowTierCapsAtThirty()
        {
            // The number the gun's cooldown and the banking convergence were
            // both written against.
            Assert.AreEqual(30, SettingsUI.DefaultFrameRateFor(QualityTierManager.Tier.Low));
            Assert.AreEqual(60, SettingsUI.DefaultFrameRateFor(QualityTierManager.Tier.Medium));
            Assert.AreEqual(60, SettingsUI.DefaultFrameRateFor(QualityTierManager.Tier.High));
        }

        [Test]
        public void AnExplicitFrameRateOverridesTheTierDefault()
        {
            settings.SetTierOverride(QualityTierManager.Tier.Low, 60);
            Assert.AreEqual(60, settings.EffectiveFrameRate);
        }

        [Test]
        public void AZeroFrameRateMeansTheTierDefault()
        {
            settings.SetTierOverride(QualityTierManager.Tier.Low, 0);
            Assert.AreEqual(30, settings.EffectiveFrameRate);
        }

        [Test]
        public void ChangesAreAnnouncedSoAHudCanRefresh()
        {
            QualityTierManager.Tier seenTier = QualityTierManager.Tier.Medium;
            int seenRate = 0;
            settings.OnSettingsChanged += (tier, rate) => { seenTier = tier; seenRate = rate; };

            settings.SetTierOverride(QualityTierManager.Tier.Low);
            Assert.AreEqual(QualityTierManager.Tier.Low, seenTier);
            Assert.AreEqual(30, seenRate);
        }

        [Test]
        public void ACorruptSettingsFileFallsBackToDetection()
        {
            // Always a playable state. Refusing to start over a preference
            // would be absurd.
            File.WriteAllText(SettingsUI.SavePath, "{ not json");

            SettingsUI fresh = Relaunch();
            Assert.IsFalse(fresh.HasOverride);
            Assert.AreEqual(fresh.DetectedTier, fresh.EffectiveTier);
            Object.DestroyImmediate(fresh.gameObject);
        }

        [Test]
        public void AnOutOfRangeTierInTheFileIsClamped()
        {
            // A settings file from a newer build, or a hand-edited one, must
            // not put the game on a tier that does not exist.
            File.WriteAllText(SettingsUI.SavePath,
                "{\"hasOverride\":true,\"tier\":99,\"targetFrameRate\":100000}");

            SettingsUI fresh = Relaunch();
            Assert.AreEqual(QualityTierManager.Tier.High, fresh.EffectiveTier);
            Assert.LessOrEqual(fresh.EffectiveFrameRate, 240);
            Object.DestroyImmediate(fresh.gameObject);
        }

        [Test]
        public void AFirstLaunchIsNotAnError()
        {
            Assert.IsFalse(settings.Load());
        }

        [Test]
        public void TheWriteLeavesNoTemporaryFile()
        {
            settings.SetTierOverride(QualityTierManager.Tier.Low);
            Assert.IsFalse(File.Exists(SettingsUI.SavePath + ".tmp"));
        }

        [Test]
        public void SettingsAreNotStoredInTheWalletSave()
        {
            settings.SetTierOverride(QualityTierManager.Tier.Low);
            Assert.AreNotEqual(Path.GetFileName(JetFighter.Economy.SaveService.SavePath),
                Path.GetFileName(SettingsUI.SavePath));
        }
    }
}
