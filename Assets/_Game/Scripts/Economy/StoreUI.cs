using System;
using System.Collections.Generic;
using UnityEngine;

namespace JetFighter.Economy
{
    /// <summary>
    /// The browse-and-buy flow for both currencies.
    ///
    /// The criterion names the case that matters: insufficient balance is
    /// handled, and there are never negative balances. Phase 3's Wallet
    /// already refuses to overspend, so the job here is not to re-implement
    /// that check -- it is to never let the two diverge. A UI that decides
    /// affordability itself and then calls Spend has two sources of truth, and
    /// the one the player sees is the one that is wrong.
    ///
    /// Ownership is tracked here rather than in Wallet: Wallet is currency
    /// arithmetic and nothing else (ADR-004's shape), and an owned-items set
    /// is a different concern with a different lifetime.
    /// </summary>
    public class StoreUI : MonoBehaviour
    {
        /// <summary>Why a purchase did not happen. The UI shows a different message for each.</summary>
        public enum PurchaseResult
        {
            Purchased = 0,
            InsufficientFunds = 1,
            AlreadyOwned = 2,
            UnknownItem = 3,
            NotReady = 4,
        }

        [SerializeField] private List<StoreItemDef> items = new List<StoreItemDef>();

        private Wallet wallet;
        private readonly HashSet<string> owned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Raised after a successful purchase, with the item and what it cost.</summary>
        public event Action<StoreItemDef, int> OnPurchased;

        /// <summary>Raised when a purchase is refused, with the reason.</summary>
        public event Action<StoreItemDef, PurchaseResult> OnRefused;

        public IReadOnlyList<StoreItemDef> Items => items;

        /// <summary>Items already bought. Restored from the save on launch.</summary>
        public IReadOnlyCollection<string> OwnedItemIds => owned;

        public void Bind(Wallet targetWallet)
        {
            wallet = targetWallet;
        }

        public void AddItem(StoreItemDef item)
        {
            if (item != null)
            {
                items.Add(item);
            }
        }

        /// <summary>Restores ownership after a relaunch, so cosmetics are not resold.</summary>
        public void RestoreOwned(IEnumerable<string> itemIds)
        {
            if (itemIds == null)
            {
                return;
            }
            foreach (string id in itemIds)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    owned.Add(id);
                }
            }
        }

        public bool IsOwned(StoreItemDef item)
        {
            return item != null && owned.Contains(item.itemId);
        }

        /// <summary>
        /// Whether the player can afford this item right now.
        ///
        /// Asks the wallet rather than comparing numbers itself. Two places
        /// deciding affordability is how a button stays enabled for an item
        /// the wallet will refuse -- and the player reads that as the game
        /// taking their coins and giving nothing.
        /// </summary>
        public bool CanAfford(StoreItemDef item)
        {
            return wallet != null && item != null && wallet.GetBalance(item.currency) >= item.cost;
        }

        /// <summary>Whether the buy button should be interactive.</summary>
        public bool CanPurchase(StoreItemDef item)
        {
            if (item == null || wallet == null)
            {
                return false;
            }
            if (IsOwned(item) && !item.CanRepurchase)
            {
                return false;
            }
            return CanAfford(item);
        }

        /// <summary>
        /// Attempts a purchase. The single path -- there is no second one that
        /// skips a check, which is how a free item eventually ships.
        /// </summary>
        public PurchaseResult Purchase(StoreItemDef item)
        {
            if (wallet == null)
            {
                return Refuse(item, PurchaseResult.NotReady);
            }
            if (item == null || !items.Contains(item))
            {
                // Buying something not on the shelf. A stale UI reference
                // after a catalog change looks exactly like this.
                return Refuse(item, PurchaseResult.UnknownItem);
            }
            if (IsOwned(item) && !item.CanRepurchase)
            {
                return Refuse(item, PurchaseResult.AlreadyOwned);
            }

            // The wallet is the only thing that decides whether the money
            // moves. Spend is all-or-nothing, so a refusal leaves the balance
            // exactly as it was.
            if (!wallet.Spend(item.currency, item.cost))
            {
                return Refuse(item, PurchaseResult.InsufficientFunds);
            }

            owned.Add(item.itemId);
            // Saved immediately: an item bought and lost to a crash is a
            // support ticket, and for a gem purchase it is a refund.
            SaveService.Save(wallet);

            OnPurchased?.Invoke(item, item.cost);
            return PurchaseResult.Purchased;
        }

        private PurchaseResult Refuse(StoreItemDef item, PurchaseResult reason)
        {
            OnRefused?.Invoke(item, reason);
            return reason;
        }

        /// <summary>Items priced in one currency, for a per-currency tab.</summary>
        public IReadOnlyList<StoreItemDef> ItemsFor(CurrencyType currency)
        {
            var matching = new List<StoreItemDef>();
            foreach (StoreItemDef item in items)
            {
                if (item != null && item.currency == currency)
                {
                    matching.Add(item);
                }
            }
            return matching;
        }
    }
}
