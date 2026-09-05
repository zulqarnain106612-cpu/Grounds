# JetFighter — Phase 5 Technical Spec (Economy & Monetization)

Extends Phase 3's `CurrencyType`/`Wallet` (already dual-currency-shaped) with real IAP, store UI, and ads. No refactor of the Phase 3 economy core needed if that phase was built as specified.

---

## 1. Folder Additions

```
Assets/_Game/Scripts/Economy/
  ProductCatalog.cs
  StoreItemDef.cs
  IAPManager.cs
  StoreUI.cs
  AdsManager.cs
  RemoveAdsFlag.cs
Assets/_Game/Scripts/UI/
  ContinueOnDeathUI.cs
  OddsDisclosureUI.cs
```

## 2. Class List

### `Economy/ProductCatalog.cs` — ScriptableObject
List of IAP products: id, price tier, currency granted (`Gems` amount), bonus % at higher tiers. Data-only — pricing changes never touch code.

### `Economy/StoreItemDef.cs` — ScriptableObject
`cost`, `currencyType` (`Coins`/`Gems`), `itemType` (`Cosmetic`/`Upgrade`/`Consumable`), reference to the effect/asset it unlocks.

### `Economy/IAPManager.cs` — MonoBehaviour, wraps Unity IAP
`Initialize()`, `PurchaseProduct(productId)`, `OnPurchaseComplete` → `Wallet.Add(CurrencyType.Gems, product.amount)`. Sandbox-testable before App Store review.

### `Economy/StoreUI.cs` — MonoBehaviour
Browse/purchase flow for both `Coins`- and `Gems`-priced `StoreItemDef`s. Calls `Wallet.Spend` on purchase, `IAPManager.PurchaseProduct` for real-money entries.

### `Economy/AdsManager.cs` — MonoBehaviour, wraps chosen ad SDK (pick at this phase, not before — roadmap explicitly deferred this decision)
`ShowRewardedAd(Action onComplete)`, `ShowInterstitial()`. Interstitial calls check `RemoveAdsFlag` first.

### `Economy/RemoveAdsFlag.cs` — thin wrapper over `SaveService` (Phase 3)
Persisted boolean, set true on the one-time "remove ads" IAP purchase.

### `UI/ContinueOnDeathUI.cs` — MonoBehaviour
On player death: offers a rewarded-ad-gated continue. Highest-converting placement in this genre per the roadmap — build this one carefully, it's disproportionately important to monetization outcomes.

### `UI/OddsDisclosureUI.cs` — MonoBehaviour
Built now, before it's needed — if any randomized purchase ever ships, Apple Guideline 3.1.1 requires odds disclosure. Cheaper to have the slot than retrofit it under App Store review pressure. Unused/hidden if no randomized items exist at launch (confirmed assumption: cosmetic-only, no pay-to-win loot boxes — this component stays dormant unless that changes).

---

## 3. Branch Sequence

1. `phase5/product-catalog` — `ProductCatalog`, `StoreItemDef` data assets.
2. `phase5/iap-integration` — `IAPManager`, Unity IAP setup, sandbox purchase test.
3. `phase5/store-ui` — `StoreUI` full browse/purchase flow.
4. `phase5/ads-integration` — `AdsManager`, `RemoveAdsFlag`, `ContinueOnDeathUI`.
5. `phase5/compliance-odds-ui` — `OddsDisclosureUI` slot (dormant, per current design).

## 4. Acceptance Criteria

| Branch | Criteria |
|---|---|
| product-catalog | Catalog data loads and validates (no duplicate/malformed product ids) before any store UI depends on it. |
| iap-integration | A sandbox purchase completes end-to-end and correctly credits `Gems` via the existing Phase 3 `Wallet`. |
| store-ui | Both coin- and gem-priced items purchase correctly and deduct the right currency; insufficient-balance case is handled (no negative balances, ever). |
| ads-integration | Rewarded continue actually revives the run only after ad completion (not on ad *start* or skip); interstitial respects `RemoveAdsFlag`. |
| compliance-odds-ui | Component exists and renders correctly in a test harness even though no live randomized item currently uses it. |

## 5. `knowledge_update` Pattern

`phase: ["5"]`, e.g.:
```json
{"op":"add_node","id":"phase5_iap_integration","type":"system","label":"Unity IAP + Gem Crediting","tags":["economy","monetization","phase5"],"phase":["5"],"symbols":["IAPManager","ProductCatalog"]}
```
