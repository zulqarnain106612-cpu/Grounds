# JetFighter — Phase 2 Technical Spec (Combat Core)

Builds on Phase 1's flight/input/gun foundation. Adds: intro sequence, enemy + health UI, right-hand targeting, missile system, bullet damage.

**Retrofit required from the cross-phase note:** `PrimaryGunController` should already read `damage`/`fireRatePerSecond` from `PlayerStatsRuntime`, not `WeaponBase` directly, before this phase starts.

---

## 1. Folder Additions

```
Assets/_Game/Scripts/
  Shared/
    IDamageable.cs
  Enemy/
    EnemyDef.cs
    EnemyHealth.cs
    EnemyController.cs
  UI/
    EnemyHealthBarUI.cs
  UI/Input/
    TargetReticleInput.cs
    MissileTriggerButton.cs
  Weapon/
    Bullet.cs
    MissileController.cs
    MissileLauncher.cs
  Player/
    IntroSequenceController.cs
```

## 2. Class List

### `Shared/IDamageable.cs` — interface
`void ApplyDamage(float amount)` — implemented by `EnemyHealth`. Decouples both weapon types (bullet, missile) from enemy-specific logic.

### `Enemy/EnemyDef.cs` — ScriptableObject
`maxHealth`, `damagePerHit`, `moveSpeed`. (`dropTable` field added in Phase 3 — deliberately not here, keeps this phase's data shape minimal.)

### `Enemy/EnemyHealth.cs` — MonoBehaviour, implements `IDamageable`
| member | notes |
|---|---|
| `currentHealth` | initialized from `EnemyDef.maxHealth` |
| `OnDamaged` | `UnityEvent<float>` — emits `pctRemaining`, consumed by `EnemyHealthBarUI` |
| `ApplyDamage(float)` | decrements health, fires `OnDamaged`, calls `Die()` at zero |
| `Die()` | Phase 2: just deactivate. Drop-table logic added in Phase 3. |

### `Enemy/EnemyController.cs` — MonoBehaviour
Single movement pattern (approach + loiter) for the first enemy type. Full AI/FSM variety is a Phase 3 concern — this phase proves the damage/health/UI pipeline works, not enemy sophistication.

### `UI/EnemyHealthBarUI.cs` — MonoBehaviour
World-space canvas billboard tracking the enemy transform. `Image.fillAmount` driven by `pctRemaining`; color `Lerp` green→yellow→red on fixed thresholds (e.g., >60%, 30–60%, <30%). Subscribed to `EnemyHealth.OnDamaged` — event-driven, no per-frame polling (perf requirement from the roadmap).

### `UI/Input/TargetReticleInput.cs` — MonoBehaviour
Right screen half only (mirrors `JoystickInput`'s left-region guarantee — same structural pattern, opposite half). Raycasts from touch position to a `Ground` layer, sets `CurrentTarget : Transform`, positions a reticle sprite.

### `UI/Input/MissileTriggerButton.cs` — MonoBehaviour
On press: if `TargetReticleInput.CurrentTarget != null`, calls `MissileLauncher.Fire(currentTarget)`.

### `Weapon/Bullet.cs` — MonoBehaviour (added to the Phase 1 pooled bullet prefab)
`OnTriggerEnter`: gets `IDamageable` from the hit collider, calls `ApplyDamage(weaponDef.damage)`, then `ObjectPool.Release(gameObject)`. This is what makes the Phase 1 gun stub's `damage` field meaningful for the first time.

### `Weapon/MissileController.cs` — MonoBehaviour (on pooled missile prefab)
Homes toward a locked `Transform` target (simple `Vector3.MoveTowards`/steering, not full physics-based guidance — arcade feel, not simulation). Applies damage via `IDamageable` on impact, higher `damage` value than primary bullet (data-driven via a separate `WeaponBase` asset for missiles).

### `Weapon/MissileLauncher.cs` — MonoBehaviour
`Fire(Transform target)`: pulls from a dedicated missile `ObjectPool` (reuses Phase 1's generic pool utility), sets target on the instance, applies a cooldown so missiles aren't spammable.

### `Player/IntroSequenceController.cs` — MonoBehaviour, state machine
`Spawn → CountdownFive..One → Go → PlayerControl`.
- `Spawn`: jet enters from below screen via tween/`AnimationCurve` (not physics-driven).
- During `Spawn`/`Countdown`: `Rigidbody.isKinematic = true`, `PlayerInputRouter` and `PrimaryGunController` disabled — player has no control and the gun is silent until `Go`.
- `Go`: `isKinematic = false`, input router + gun enabled, hands off to `PlayerControl`.

---

## 3. Branch Sequence

1. `phase2/enemy-core` — `IDamageable`, `EnemyDef`, `EnemyHealth`, `EnemyController`.
2. `phase2/enemy-healthbar-ui` — `EnemyHealthBarUI` wired to `EnemyHealth.OnDamaged`.
3. `phase2/weapon-bullet-damage` — `Bullet.cs` added to the pooled prefab, damage pipeline live.
4. `phase2/right-hand-targeting` — `TargetReticleInput`, region-guarantee test mirroring Phase 1's left-hand test.
5. `phase2/missile-system` — `MissileController`, `MissileLauncher`, `MissileTriggerButton`.
6. `phase2/intro-sequence` — `IntroSequenceController`, kinematic gating during spawn/countdown.

## 4. Acceptance Criteria

| Branch | Criteria |
|---|---|
| enemy-core | Enemy spawns, takes damage via `IDamageable`, dies at zero health. |
| enemy-healthbar-ui | Bar fill and color update in real time on damage, tracks enemy position correctly, no per-frame polling in profiler. |
| weapon-bullet-damage | Primary gun kills the test enemy in a predictable number of hits matching `damage × maxHealth` math. |
| right-hand-targeting | Touches in the right screen half place/move the reticle; touches in the left half have zero effect on targeting (mirrors Phase 1's proof). |
| missile-system | Missile locks onto and destroys the targeted ground enemy; cooldown prevents spam; pool stays bounded over a soak test. |
| intro-sequence | Jet cannot be moved or fire during countdown; control and firing both enable exactly at `Go`, not before. |

## 5. `knowledge_update` Pattern

Same shape as Phase 1 (see `PHASE1_TECHNICAL_SPEC.md` §5), `phase: ["2"]`, one node per branch, e.g.:

```json
{"op":"add_node","id":"phase2_enemy_core","type":"system","label":"Enemy Health + Damage Pipeline","tags":["enemy","phase2"],"phase":["2"],"symbols":["EnemyDef","EnemyHealth","EnemyController","IDamageable"]}
```
