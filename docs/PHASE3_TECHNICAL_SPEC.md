# JetFighter — Phase 3 Technical Spec (Endless Loop + Scaling)

Builds on Phase 2's combat pipeline. Adds: power-ups, drop tables, bounded difficulty scaling, coin economy.

---

## 1. Folder Additions

```
Assets/_Game/Scripts/
  PowerUp/
    PowerUpDef.cs
    PowerUpPickup.cs
    PowerUpController.cs
  Player/
    PlayerStatsRuntime.cs   (retrofit target from Phase 1 note, finalized here)
  Enemy/
    DropTable.cs
    DifficultyCurve.cs
    DifficultyManager.cs
    EnemySpawner.cs
  Economy/
    CurrencyType.cs
    Wallet.cs
    SaveService.cs
    CoinEarnController.cs
```

## 2. Class List

### `Player/PlayerStatsRuntime.cs` — MonoBehaviour
Holds **effective** runtime values (speed, fire rate, damage), seeded once from the `JetFlightConfig`/`WeaponBase` assets on spawn. `JetController` and `PrimaryGunController` read from this, never from the SO directly. `PowerUpController` writes to this. This is the retrofit flagged before Phase 1 — if it was built then, this phase just adds the write side.

### `PowerUp/PowerUpDef.cs` — ScriptableObject
`type` (enum `Speed, FireRate`), `magnitude`, `duration` (0 = permanent-for-run, per your confirmed assumption).

### `PowerUp/PowerUpPickup.cs` — MonoBehaviour
World object; `OnTriggerEnter` (player only) calls `PowerUpController.Apply(powerUpDef)`, then deactivates/pools itself.

### `PowerUp/PowerUpController.cs` — MonoBehaviour (on player)
| member | notes |
|---|---|
| `activeStacks` | tracks currently-applied power-ups and remaining duration |
| `PlayerPowerLevel` | `float`, derived from active stack count/magnitude — read by `DifficultyManager` |
| `Apply(PowerUpDef)` | applies stacking rule: multiplicative, **hard-capped** (e.g., fire rate can't exceed 3× base) so the 1/sec baseline stays meaningful and DPS can't trivialize the game |
| `Update()` | ticks down temporary stacks, reverts `PlayerStatsRuntime` values on expiry |

### `Enemy/DropTable.cs` — serializable struct/list, added as a field on `EnemyDef`
Weighted list of `(PowerUpDef, dropChance)`. Balancing lives entirely in data — no code changes needed to retune drop rates.

### `Enemy/DifficultyCurve.cs` — ScriptableObject
`k` (scaling constant), `timeToKillCeilingSeconds` (defeatability floor), archetype-unlock power-level thresholds.

### `Enemy/DifficultyManager.cs` — MonoBehaviour (singleton/service)
| member | notes |
|---|---|
| `ComputeStatMultiplier(float playerPowerLevel)` | `1 + k * playerPowerLevel`, then **clamped** so `enemyEffectiveHealth / playerCurrentDPS ≤ timeToKillCeilingSeconds` — the bounded-DDA guarantee from the roadmap |
| `GetUnlockedArchetypes(float playerPowerLevel)` | returns which `EnemyDef`s are currently spawnable, driving variety-based difficulty rather than pure stat inflation |

### `Enemy/EnemySpawner.cs` — MonoBehaviour
Spawns from `DifficultyManager.GetUnlockedArchetypes()` on an interval/wave pattern, applies the current stat multiplier to each spawned instance.

### `Economy/CurrencyType.cs` — enum
`Coins, Gems`. Only `Coins` is active this phase; `Gems` exists now so Phase 5 doesn't need to touch this enum or refactor `Wallet` — the "architect once" principle from the roadmap's monetization section applied literally.

### `Economy/Wallet.cs` — plain C# class (not a MonoBehaviour)
`Dictionary<CurrencyType, int>` balances. `Add(type, amount)`, `Spend(type, amount) -> bool`, `GetBalance(type)`. No UI, no persistence logic inside it — single responsibility.

### `Economy/SaveService.cs` — static/service class
Persists wallet + progress to a JSON file in `Application.persistentDataPath` (**not** `PlayerPrefs** — `PlayerPrefs` is fine for trivial flags but is the wrong tool for structured, economy-critical data; a real save file is the production-grade choice here and avoids a rework when save data grows in Phase 5/6).

### `Economy/CoinEarnController.cs` — MonoBehaviour
Subscribes to a survival-timer tick and to `EnemyHealth`'s death event; calls `Wallet.Add(CurrencyType.Coins, amount)` on each.

---

## 3. Branch Sequence

1. `phase3/player-stats-runtime` — finalize `PlayerStatsRuntime` (or confirm it already exists from the Phase 1 retrofit).
2. `phase3/powerup-core` — `PowerUpDef`, `PowerUpPickup`, `PowerUpController` with stacking + hard cap.
3. `phase3/enemy-drop-tables` — `DropTable` field on `EnemyDef`, drop logic on `EnemyHealth.Die()`.
4. `phase3/difficulty-scaling` — `DifficultyCurve`, `DifficultyManager`, bounded formula, archetype thresholds.
5. `phase3/enemy-spawner-waves` — `EnemySpawner` consuming `DifficultyManager` output.
6. `phase3/economy-coins` — `CurrencyType`, `Wallet`, `SaveService`, `CoinEarnController`.

## 4. Acceptance Criteria

| Branch | Criteria |
|---|---|
| player-stats-runtime | `JetController`/`PrimaryGunController` provably read only from `PlayerStatsRuntime`; the source `ScriptableObject` assets are never mutated at runtime (verify by asset diff after a play session). |
| powerup-core | Collecting a fire-rate power-up visibly changes fire interval within one frame; stacking respects the hard cap under repeated pickups. |
| enemy-drop-tables | Drop rates match configured weights within statistical tolerance over N kills (spot-check, not exhaustive). |
| difficulty-scaling | At every tested power level, time-to-kill for the current enemy tier stays at or under `timeToKillCeilingSeconds` — i.e., never becomes mathematically unwinnable. |
| enemy-spawner-waves | New archetypes appear only after their power-level threshold is crossed, not before. |
| economy-coins | Coins accumulate correctly from both survival time and kills; balance survives an app relaunch (proves `SaveService` round-trips correctly). |

## 5. `knowledge_update` Pattern

`phase: ["3"]`, e.g.:
```json
{"op":"add_node","id":"phase3_difficulty_scaling","type":"system","label":"Bounded DDA Difficulty Scaling","tags":["enemy","balance","phase3"],"phase":["3"],"symbols":["DifficultyCurve","DifficultyManager","EnemySpawner"]}
```
