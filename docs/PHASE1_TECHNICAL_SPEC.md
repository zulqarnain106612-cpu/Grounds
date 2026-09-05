# JetFighter — Phase 1 Technical Spec

Scope: risk-first target from the roadmap — constrained-physics flight feel, primary gun stub, iOS scaffold. Locked-axis decision applied: **world-Z (depth) is locked; X (left-right) and Y (up-down) are free and physics-driven.**

---

## 1. Folder Layout

```
Assets/_Game/Scripts/
  Player/
    JetController.cs
    JetFlightConfig.cs
  Physics/
    PlaneConstraint.cs
  UI/Input/
    JoystickInput.cs
    PlayerInputRouter.cs
  Weapon/
    WeaponBase.cs
    PrimaryGunController.cs
  Shared/
    ObjectPool.cs
  Build/
    QualityTierManager.cs
```

This matches `config/agent.config.json` → `source_globs: ["Assets/_Game/Scripts/**/*.cs"]`, so the symbol scanner picks all of it up automatically on commit.

---

## 2. Class List

### `Player/JetFlightConfig.cs` — ScriptableObject

| field | type | notes |
|---|---|---|
| `maxSpeed` | float | free-axis (X/Y) speed cap |
| `acceleration` | float | force applied toward target velocity |
| `linearDrag` | float | Rigidbody drag on free axes |
| `angularDrag` | float | Rigidbody angular drag |
| `bankAngleMax` | float (deg) | max visual roll under lateral (X) velocity |
| `bankResponsiveness` | float | lerp speed of bank angle toward target |

### `Player/JetController.cs` — MonoBehaviour

| member | signature | notes |
|---|---|---|
| `body` | `Rigidbody` | cached in `Awake` |
| `config` | `JetFlightConfig` | assigned in inspector |
| `inputVector` | `Vector2` | set externally by `PlayerInputRouter`, X=left/right, Y=up/down |
| `FixedUpdate()` | `void` | calls `ApplyMovementForces`, then `ApplyBanking` |
| `ApplyMovementForces(Vector2 input)` | `void` | `AddForce` toward `input * config.maxSpeed`, respecting `acceleration`/`linearDrag` |
| `ApplyBanking()` | `void` | rotates visual mesh (not the Rigidbody's collision rotation) based on current X velocity, lerped by `bankResponsiveness` |

**Important:** `ApplyBanking` must rotate a child visual transform, not the Rigidbody itself — the physics body should stay axis-aligned for collision predictability; only the model tilts for feel.

### `Physics/PlaneConstraint.cs` — MonoBehaviour

| member | signature | notes |
|---|---|---|
| `lockedAxis` | `enum Axis { X, Y, Z }` | set to `Z` for Phase 1 |
| `lockedValue` | `float` | captured from initial world position in `Awake` |
| `FixedUpdate()` | `void` | runs in `Update` order **after** `JetController`'s physics step (`[DefaultExecutionOrder]` or Script Execution Order setting) — zeroes the locked axis's position delta and its Rigidbody velocity component |

This is a post-solve correction, not a `ConfigurableJoint` — deliberate, per the roadmap's rationale (joints introduce solver jitter that fights arcade feel).

### `UI/Input/JoystickInput.cs` — MonoBehaviour, implements `IDragHandler, IPointerUpHandler, IPointerDownHandler`

| member | signature | notes |
|---|---|---|
| `handle`, `background` | `RectTransform` | visual elements |
| `radius` | `float` | max handle displacement |
| `GetNormalizedVector()` | `Vector2` | returns current stick offset / radius, clamped to unit circle |

Bound to the **left half of the screen only** (input region check in `OnPointerDown`) — this is the structural guarantee (not just convention) that this control can never touch jet movement from the right side.

### `UI/Input/PlayerInputRouter.cs` — MonoBehaviour

| member | signature | notes |
|---|---|---|
| `joystick` | `JoystickInput` | left-hand source |
| `jet` | `JetController` | target |
| `Update()` | `void` | `jet.inputVector = joystick.GetNormalizedVector()` |

Right-hand input (target reticle + missile trigger) is **explicitly out of scope for Phase 1** — reserved as `ITargetInput` interface stub only, so Phase 2 has a clean seam and Phase 1 doesn't risk scope creep into weapon-lock logic.

### `Weapon/WeaponBase.cs` — ScriptableObject

| field | type | Phase 1 value |
|---|---|---|
| `fireRatePerSecond` | float | `1.0` |
| `projectilePrefab` | GameObject | bullet prefab |
| `poolTag` | string | `"primary_bullet"` |
| `damage` | float | placeholder value; damage variety is Phase 2 |

### `Weapon/PrimaryGunController.cs` — MonoBehaviour

| member | signature | notes |
|---|---|---|
| `weaponDef` | `WeaponBase` | assigned in inspector |
| `muzzleTransform` | `Transform` | front-center mount point |
| `cooldownTimer` | float | counts down each `Update`, **time-based not frame-based** so fire rate is frame-rate independent |
| `Fire()` | `void` | pulls from `ObjectPool`, positions at `muzzleTransform`, resets `cooldownTimer = 1f / weaponDef.fireRatePerSecond` |

### `Shared/ObjectPool.cs` — generic utility

| member | signature | notes |
|---|---|---|
| `Get()` | `GameObject` | reuses inactive instance or instantiates new if pool empty |
| `Release(GameObject)` | `void` | deactivates and returns to pool |

Built now (Phase 1), not deferred — continuous 1/sec+ fire over long endless runs makes `Instantiate`/`Destroy` churn a guaranteed perf problem later; cheaper to build once than retrofit.

### `Build/QualityTierManager.cs` — MonoBehaviour

| member | signature | notes |
|---|---|---|
| `enum Tier { Low, Medium, High }` | | |
| `DetectTier()` | `Tier` | based on `SystemInfo.systemMemorySize` / GPU family, run once at first launch |
| `ApplySettings(Tier)` | `void` | sets particle density / shadow quality / enemy-on-screen cap; also the entry point for the manual override required by spec item 2c |

---

## 3. Branch Sequence (exact order)

Trunk-based, one branch per cell, squash-merged to `main`, each ending in a `knowledge_update` call (payloads below) and a green `gateway/validate_repo.py` + test run.

1. `phase1/build-ios-scaffold` — Unity project init, iOS Player Settings (Bundle ID, min iOS version, Metal), folder layout above, `QualityTierManager` stub. Done first — everything else lives inside this scaffold.
2. `phase1/player-flight-rigidbody` — `JetFlightConfig`, `JetController`, tested with free (unconstrained) 3D movement first so inertia/banking feel can be validated in isolation before the constraint layer is added.
3. `phase1/physics-plane-constraint` — `PlaneConstraint` locked to `Z`, wired onto the jet's Rigidbody.
4. `phase1/input-joystick-mapping` — `JoystickInput` + `PlayerInputRouter`, on-device touch test.
5. `phase1/weapon-gun-stub` — `ObjectPool`, `WeaponBase` asset, `PrimaryGunController` firing at 1/sec.

---

## 4. Acceptance Test per Branch

| Branch | Acceptance criteria |
|---|---|
| build-ios-scaffold | Project builds and deploys to a physical iOS device via Xcode with zero errors; `QualityTierManager` logs a detected tier on launch. |
| player-flight-rigidbody | Jet exhibits visible inertia/drag/banking under test input in **unconstrained** 3D space (constraint not yet applied) — proves the physics feel independent of the plane lock. |
| physics-plane-constraint | World-Z position never changes under any input combination, including diagonal force; X/Y respond with full physics; no visible jitter over a 60-second soak test. |
| input-joystick-mapping | On-device touch in the left screen half produces a correctly normalized vector; touches in the right screen half produce **zero** effect on `jet.inputVector` (proves the region guarantee, not just the mapping). |
| weapon-gun-stub | Bullets fire at 1.0s ± 0.02s intervals regardless of frame rate; pool size stays bounded (no unbounded growth) over a 5-minute continuous-fire soak test. |

---

## 5. `knowledge_update` Payloads (run after each branch merges)

```json
{
  "meta": {"schema_version":"1.1.0","session_id":"<uuid>","tick":<n>,"phase":"1","timestamp_utc":"<iso>"},
  "intent": {"action":"knowledge_update","domain":"player","priority":5},
  "payload": {"data": null, "knowledge_op": {
    "op":"add_node","id":"phase1_jet_flight_rigidbody","type":"system",
    "label":"Jet Flight Rigidbody Controller","tags":["player","physics","phase1"],
    "phase":["1"],"symbols":["JetController","JetFlightConfig"]
  }}
}
```
```json
{
  "meta": {"schema_version":"1.1.0","session_id":"<uuid>","tick":<n>,"phase":"1","timestamp_utc":"<iso>"},
  "intent": {"action":"knowledge_update","domain":"physics","priority":5},
  "payload": {"data": null, "knowledge_op": {
    "op":"add_node","id":"phase1_plane_constraint","type":"system",
    "label":"2D-Plane Physics Constraint (Z-locked)","tags":["physics","phase1"],
    "phase":["1"],"symbols":["PlaneConstraint"]
  }}
}
```
```json
{
  "meta": {"schema_version":"1.1.0","session_id":"<uuid>","tick":<n>,"phase":"1","timestamp_utc":"<iso>"},
  "intent": {"action":"knowledge_update","domain":"ui","priority":4},
  "payload": {"data": null, "knowledge_op": {
    "op":"add_node","id":"phase1_joystick_input","type":"system",
    "label":"Left-Hand Virtual Joystick + Router","tags":["ui","input","phase1"],
    "phase":["1"],"symbols":["JoystickInput","PlayerInputRouter"]
  }}
}
```
```json
{
  "meta": {"schema_version":"1.1.0","session_id":"<uuid>","tick":<n>,"phase":"1","timestamp_utc":"<iso>"},
  "intent": {"action":"knowledge_update","domain":"weapon","priority":5},
  "payload": {"data": null, "knowledge_op": {
    "op":"add_node","id":"phase1_primary_gun_stub","type":"system",
    "label":"Primary Gun (1/sec, pooled)","tags":["weapon","phase1"],
    "phase":["1"],"symbols":["WeaponBase","PrimaryGunController","ObjectPool"]
  }}
}
```

Each also gets an edge back to a `phase1` parent node (`edge_kind: "part_of"`) so `retrieve`/`symbol_lookup` can scope to "everything in Phase 1" as a single query later.

---

## Next Step

Say go and I'll start branch 1 (`phase1/build-ios-scaffold`) — or flag anything in the class list/spec above you want changed before code starts.
