using System;
using System.IO;
using UnityEngine;
using JetFighter.Build;

namespace JetFighter.Settings
{
    /// <summary>
    /// The adjustable performance settings spec item 2c requires, on top of
    /// QualityTierManager's auto-detected tier.
    ///
    /// The criterion is that a manual override persists across relaunch and
    /// visibly changes rendering. Both halves have a specific failure: an
    /// override that does not persist makes the setting look broken, and one
    /// that persists without applying makes it look like a lie -- the toggle
    /// stays where the player put it and nothing changes.
    ///
    /// The detected tier is never overwritten by an override. A player who
    /// picks High on a device that cannot sustain it must be able to get back
    /// to "whatever this device can do", and that is impossible if the
    /// detection result was replaced rather than layered over.
    /// </summary>
    public class SettingsUI : MonoBehaviour
    {
        [Serializable]
        private class Data
        {
            public bool hasOverride;
            public int tier;
            public int targetFrameRate;
        }

        public const string FileName = "settings.json";

        /// <summary>Overridable so tests never touch the real settings file.</summary>
        public static string SaveDirectory { get; set; }

        public static string SavePath =>
            Path.Combine(SaveDirectory ?? Application.persistentDataPath, FileName);

        private bool hasOverride;
        private QualityTierManager.Tier overrideTier;
        private int overrideFrameRate;

        /// <summary>The tier detected for this device. Never changed by an override.</summary>
        public QualityTierManager.Tier DetectedTier { get; private set; } = QualityTierManager.Tier.Medium;

        /// <summary>Whether the player has chosen a tier themselves.</summary>
        public bool HasOverride => hasOverride;

        /// <summary>The tier actually in force.</summary>
        public QualityTierManager.Tier EffectiveTier => hasOverride ? overrideTier : DetectedTier;

        /// <summary>Frame-rate cap in force, or 0 for the tier's default.</summary>
        public int EffectiveFrameRate => hasOverride && overrideFrameRate > 0
            ? overrideFrameRate
            : DefaultFrameRateFor(EffectiveTier);

        /// <summary>Raised whenever the effective settings change, so a HUD can refresh.</summary>
        public event Action<QualityTierManager.Tier, int> OnSettingsChanged;

        private void Awake()
        {
            DetectedTier = QualityTierManager.DetectTier();
            Load();
            Apply();
        }

        /// <summary>
        /// Frame-rate cap for a tier.
        ///
        /// Static and pure so the mapping is a unit test rather than something
        /// checked by watching a counter -- and 30 on low is the number the
        /// gun's cooldown and the banking convergence were both written
        /// against.
        /// </summary>
        public static int DefaultFrameRateFor(QualityTierManager.Tier tier)
        {
            return tier == QualityTierManager.Tier.Low ? 30 : 60;
        }

        /// <summary>
        /// Sets a manual tier. Applies immediately and persists.
        ///
        /// Applied before saving: a player who changes a setting and force-
        /// quits should still see the change they made, and a save that
        /// succeeded while the apply failed is the worse ordering.
        /// </summary>
        public void SetTierOverride(QualityTierManager.Tier tier, int frameRate = 0)
        {
            hasOverride = true;
            overrideTier = tier;
            overrideFrameRate = Mathf.Max(0, frameRate);
            Apply();
            Save();
        }

        /// <summary>
        /// Drops the override and returns to the detected tier.
        ///
        /// The reason detection is kept rather than overwritten: a player who
        /// picked High on a device that cannot sustain it needs a way back to
        /// "whatever this device can do".
        /// </summary>
        public void ClearOverride()
        {
            hasOverride = false;
            overrideFrameRate = 0;
            Apply();
            Save();
        }

        /// <summary>
        /// Pushes the effective settings into the engine.
        ///
        /// Goes through QualityTierManager.ApplySettings rather than touching
        /// QualitySettings directly, so the enemy budget and the frame-rate
        /// cap stay consistent with the tier -- two places applying a tier is
        /// how a device ends up on low-tier visuals with a high-tier enemy
        /// count.
        /// </summary>
        public void Apply()
        {
            QualityTierManager.ApplySettings(EffectiveTier);
            Application.targetFrameRate = EffectiveFrameRate;
            OnSettingsChanged?.Invoke(EffectiveTier, EffectiveFrameRate);
        }

        /// <summary>Reads the persisted override. A missing file is a first launch.</summary>
        public bool Load()
        {
            try
            {
                if (!File.Exists(SavePath))
                {
                    return false;
                }
                Data data = JsonUtility.FromJson<Data>(File.ReadAllText(SavePath));
                if (data == null || !data.hasOverride)
                {
                    return false;
                }
                // Clamped rather than trusted: a settings file from a newer
                // build, or a hand-edited one, must not put the game on a tier
                // that does not exist.
                hasOverride = true;
                overrideTier = (QualityTierManager.Tier)Mathf.Clamp(
                    data.tier, (int)QualityTierManager.Tier.Low, (int)QualityTierManager.Tier.High);
                overrideFrameRate = Mathf.Clamp(data.targetFrameRate, 0, 240);
                return true;
            }
            catch (Exception e)
            {
                // A corrupt settings file falls back to the detected tier,
                // which is always a playable state. Refusing to start over a
                // preference would be absurd.
                Debug.LogWarning($"[SettingsUI] unreadable settings, using the detected tier: {e.Message}");
                hasOverride = false;
                return false;
            }
        }

        /// <summary>Persists the override. Atomic, for the same reason the wallet's save is.</summary>
        public bool Save()
        {
            try
            {
                string path = SavePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(new Data
                {
                    hasOverride = hasOverride,
                    tier = (int)overrideTier,
                    targetFrameRate = overrideFrameRate,
                }, true));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(temporary, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SettingsUI] could not persist settings: {e.Message}");
                return false;
            }
        }
    }
}
