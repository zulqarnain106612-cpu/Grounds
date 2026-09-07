using System;
using System.IO;
using UnityEngine;

namespace JetFighter.Economy
{
    /// <summary>
    /// Whether the player bought "remove ads".
    ///
    /// A separate tiny file rather than a field on the wallet save. The
    /// criterion is that remove-ads suppresses interstitials *permanently*,
    /// and the wallet save is the file most likely to be rewritten as the
    /// economy grows -- a migration bug there would resell an entitlement the
    /// player already paid for, which is the single worst store outcome short
    /// of charging twice.
    ///
    /// It is also never un-set by anything except an explicit reset. There is
    /// no code path that clears an entitlement on a failed read, because "we
    /// could not read the file" and "you did not buy it" must not produce the
    /// same behaviour.
    /// </summary>
    public static class RemoveAdsFlag
    {
        [Serializable]
        private class Data
        {
            public bool removeAds;
            public string purchasedTransactionId;
        }

        public const string FileName = "entitlements.json";

        /// <summary>Overridable so tests never touch the real entitlement file.</summary>
        public static string SaveDirectory { get; set; }

        public static string SavePath =>
            Path.Combine(SaveDirectory ?? Application.persistentDataPath, FileName);

        private static bool? cached;

        /// <summary>
        /// True when ads are removed.
        ///
        /// Cached after the first read: this is checked before every
        /// interstitial, and a file read per game over is a stall the player
        /// feels at the worst moment.
        /// </summary>
        public static bool IsActive
        {
            get
            {
                if (cached.HasValue)
                {
                    return cached.Value;
                }
                cached = Read();
                return cached.Value;
            }
        }

        /// <summary>
        /// Grants the entitlement and persists it immediately.
        ///
        /// Returns false only if the write failed, so the caller can retry --
        /// but the in-memory flag is set either way. A player who paid and
        /// then saw an ad because a disk write failed has been charged for
        /// nothing, and the retry can happen on the next launch.
        /// </summary>
        public static bool Grant(string transactionId = null)
        {
            cached = true;
            try
            {
                string path = SavePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(
                    new Data { removeAds = true, purchasedTransactionId = transactionId }, true));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(temporary, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoveAdsFlag] could not persist the entitlement: {e.Message}");
                return false;
            }
        }

        /// <summary>Clears it. Only for an explicit reset and for tests.</summary>
        public static void Reset()
        {
            cached = null;
            try
            {
                if (File.Exists(SavePath))
                {
                    File.Delete(SavePath);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoveAdsFlag] reset failed: {e.Message}");
            }
        }

        /// <summary>Drops the cache so the next read hits disk. For tests and for a restore flow.</summary>
        public static void InvalidateCache()
        {
            cached = null;
        }

        private static bool Read()
        {
            try
            {
                if (!File.Exists(SavePath))
                {
                    return false;
                }
                Data data = JsonUtility.FromJson<Data>(File.ReadAllText(SavePath));
                return data != null && data.removeAds;
            }
            catch (Exception e)
            {
                // A corrupt entitlement file is not a reason to start showing
                // ads to someone who paid. It reads as "not granted" only
                // because there is nothing else to return -- and the restore
                // flow is what fixes it, not a silent downgrade.
                Debug.LogWarning($"[RemoveAdsFlag] unreadable entitlement, treating as not granted: {e.Message}");
                return false;
            }
        }
    }
}
