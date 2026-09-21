using UnityEngine;

namespace JetFighter.Economy
{
    /// <summary>
    /// One thing the store sells for in-game currency.
    ///
    /// Cost and currency are data, so a price change is an asset edit rather
    /// than a build -- which the Cycle 5 gate states as "price changes touch
    /// no code" and which matters more here than anywhere: store pricing is
    /// retuned against live conversion data, weekly, by someone who is not
    /// going to open Unity.
    /// </summary>
    [CreateAssetMenu(menuName = "JetFighter/Store Item", fileName = "StoreItemDef")]
    public class StoreItemDef : ScriptableObject
    {
        public enum ItemType
        {
            Cosmetic = 0,
            Upgrade = 1,
            Consumable = 2,
        }

        [Tooltip("Stable id. Persisted in the save, so renaming it orphans everything already owned.")]
        public string itemId = "item.unnamed";

        [Tooltip("Shown in the store. Safe to change at any time -- unlike itemId.")]
        public string displayName = "Unnamed";

        [TextArea]
        public string description;

        public ItemType itemType = ItemType.Cosmetic;

        [Tooltip("Which currency buys this. Coins are earned, gems are bought (ADR-004).")]
        public CurrencyType currency = CurrencyType.Coins;

        [Min(0)]
        public int cost = 100;

        [Tooltip("Consumables can be bought repeatedly; cosmetics and upgrades cannot.")]
        public bool repeatable;

        /// <summary>
        /// Whether this item may be bought again once owned.
        ///
        /// Derived rather than trusted from the flag alone: a cosmetic marked
        /// repeatable is a mis-authored asset that charges the player twice
        /// for the same hat, and there is no design in which that is intended.
        /// </summary>
        public bool CanRepurchase => repeatable && itemType == ItemType.Consumable;
    }
}
