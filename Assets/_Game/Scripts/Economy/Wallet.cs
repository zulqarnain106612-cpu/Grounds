using System;
using System.Collections.Generic;

namespace JetFighter.Economy
{
    /// <summary>
    /// Currency balances. A plain C# class, not a MonoBehaviour.
    ///
    /// The criterion is that adding a third currency requires no Wallet
    /// change (ADR-004), so nothing here names a currency: balances live in a
    /// dictionary keyed by the enum, and every operation takes the type as an
    /// argument. A `coins` field and a `gems` field would each need a sibling
    /// for the third, plus a save migration.
    ///
    /// No UI and no persistence inside it. SaveService serialises a wallet;
    /// the wallet does not know it is being saved, which is what lets Phase 5
    /// swap the store without touching the arithmetic.
    /// </summary>
    public class Wallet
    {
        private readonly Dictionary<CurrencyType, int> balances = new Dictionary<CurrencyType, int>();

        /// <summary>Raised on every balance change, with the type and its new balance.</summary>
        public event Action<CurrencyType, int> OnBalanceChanged;

        /// <summary>Balance for a currency. Unknown currencies read as zero, not as an error.</summary>
        public int GetBalance(CurrencyType type)
        {
            return balances.TryGetValue(type, out int amount) ? amount : 0;
        }

        /// <summary>
        /// Credits an amount. Negative and zero amounts are ignored rather
        /// than trusted -- a refund is a deliberate Add elsewhere, and an
        /// accidental negative credit is how a currency goes missing with no
        /// spend to explain it.
        /// </summary>
        public void Add(CurrencyType type, int amount)
        {
            if (amount <= 0)
            {
                return;
            }
            // Saturating rather than wrapping. A player who somehow reaches
            // int.MaxValue should stay rich, not go bankrupt.
            int current = GetBalance(type);
            int next = amount > int.MaxValue - current ? int.MaxValue : current + amount;
            SetBalance(type, next);
        }

        /// <summary>
        /// Debits an amount if the balance covers it. Returns false and
        /// changes nothing otherwise -- all-or-nothing, because a partial
        /// spend leaves the player charged for something they did not get.
        /// </summary>
        public bool Spend(CurrencyType type, int amount)
        {
            if (amount <= 0)
            {
                return false;
            }
            int current = GetBalance(type);
            if (current < amount)
            {
                return false;
            }
            SetBalance(type, current - amount);
            return true;
        }

        /// <summary>Every currency with a recorded balance. For serialisation.</summary>
        public IReadOnlyDictionary<CurrencyType, int> Snapshot()
        {
            return balances;
        }

        /// <summary>
        /// Replaces every balance. Used by SaveService on load; it does not
        /// raise per-currency events for currencies that did not change, so a
        /// UI bound to the event does not flash on every launch.
        /// </summary>
        public void Restore(IReadOnlyDictionary<CurrencyType, int> restored)
        {
            if (restored == null)
            {
                return;
            }
            foreach (KeyValuePair<CurrencyType, int> pair in restored)
            {
                if (GetBalance(pair.Key) != pair.Value)
                {
                    SetBalance(pair.Key, Math.Max(0, pair.Value));
                }
            }
        }

        private void SetBalance(CurrencyType type, int amount)
        {
            balances[type] = amount;
            OnBalanceChanged?.Invoke(type, amount);
        }
    }
}
