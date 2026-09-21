using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace JetFighter.Economy
{
    /// <summary>
    /// Persists the wallet to a JSON file in Application.persistentDataPath.
    ///
    /// Not PlayerPrefs. PlayerPrefs is fine for a trivial flag, but this is
    /// structured, economy-critical data that Phase 5 grows with purchase
    /// receipts -- and PlayerPrefs has no atomicity, so a kill mid-write
    /// leaves a half-updated set of keys with no way to tell.
    ///
    /// The save is written to a temporary file and moved into place, so an
    /// interrupted write leaves the previous save intact rather than a
    /// truncated one. On a phone the process is killed at the OS's
    /// convenience, so this is the normal case rather than the rare one.
    ///
    /// Currency-generic throughout: the file stores a list of (type, amount)
    /// pairs rather than named fields, so adding a currency needs no
    /// migration -- ADR-004's stated pay-off.
    /// </summary>
    public static class SaveService
    {
        [Serializable]
        private struct Entry
        {
            public string currency;
            public int amount;
        }

        [Serializable]
        private class SaveData
        {
            public int version = 1;
            public List<Entry> balances = new List<Entry>();
        }

        public const string FileName = "wallet.json";

        /// <summary>Overridable so tests write to a temp directory, never the real save.</summary>
        public static string SaveDirectory { get; set; }

        public static string SavePath =>
            Path.Combine(SaveDirectory ?? Application.persistentDataPath, FileName);

        /// <summary>
        /// Writes the wallet. Returns false rather than throwing: a failed
        /// save must not take the run down with it, and the caller decides
        /// whether that is worth telling the player about.
        /// </summary>
        public static bool Save(Wallet wallet)
        {
            if (wallet == null)
            {
                return false;
            }

            var data = new SaveData();
            foreach (KeyValuePair<CurrencyType, int> pair in wallet.Snapshot())
            {
                data.balances.Add(new Entry { currency = pair.Key.ToString(), amount = pair.Value });
            }

            try
            {
                string path = SavePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
                // Move, not copy-then-delete: the previous save survives a
                // kill at any point before this line.
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(temporary, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveService] save failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Loads into a wallet. A missing file is a first launch, not an
        /// error; a corrupt one is reported and treated as empty, because
        /// refusing to start is a worse outcome than a lost balance.
        /// </summary>
        public static bool Load(Wallet wallet)
        {
            if (wallet == null || !File.Exists(SavePath))
            {
                return false;
            }
            try
            {
                SaveData data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
                if (data?.balances == null)
                {
                    return false;
                }

                var restored = new Dictionary<CurrencyType, int>();
                foreach (Entry entry in data.balances)
                {
                    // An unknown currency name is a save from a newer build,
                    // or a hand-edited file. Skipping it keeps every currency
                    // this build understands rather than discarding the lot.
                    if (Enum.TryParse(entry.currency, out CurrencyType type))
                    {
                        restored[type] = Mathf.Max(0, entry.amount);
                    }
                }
                wallet.Restore(restored);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveService] load failed, treating as empty: {e.Message}");
                return false;
            }
        }

        /// <summary>Removes the save. For a "reset progress" action and for tests.</summary>
        public static void Delete()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    File.Delete(SavePath);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveService] delete failed: {e.Message}");
            }
        }
    }
}
