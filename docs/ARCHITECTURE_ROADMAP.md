# JetFighter — Architecture Roadmap & Blueprint v1.0

Structure used: **Sample C (phase × domain matrix) + Sample D (risk-first ordering)**, per your confirmation. Every cell in the matrix maps 1:1 to a `phase`/`domain` pair in `schema/agent.schema.json`, so each dev session can retrieve exactly one cell's worth of context.

---

## 0. Locked Decisions (with rationale)

| Decision | Choice | Why |
|---|---|---|
| Engine | **Unity 6 LTS** (not 2022 LTS) | Longer forward support window than 2022 LTS; you're starting fresh, so there's no reason to begin on a track closer to end-of-support. Directly answers "won't corner us while upgrading." |
| Physics | **Unity built-in PhysX (Rigidbody)**, not DOTS/ECS | 2-player cap, mobile enemy counts in the tens-not-thousands range. ECS buys you nothing at this scale and costs you solo-dev velocity. Object pooling (not ECS) is the actual answer to mobile perf here. |
| Movement model | Rigidbody physics **constrained to a 2D plane** in 3D space | Full physics feel (banking, drag, inertia) while movement never leaves the X/Y screen plane. Detailed in §3. |
| Networking (P2P, 2P max) | **Apple GameKit `GKMatch` real-time P2P** | iOS-only, 2-player cap, no dedicated server costs. Zero infra spend improves margin directly (ties into your "most profit" requirement). Documented upgrade path to Photon/PlayFab in §6 if you ever go cross-platform. |
| Monetization | **Hybrid F2P**: soft currency (coins) + hard currency (gems) + rewarded video ads + tiered IAP + cosmetic-only premium items | This is the pattern with the best-documented long-term LTV in the endless-arcade genre (Subway Surfers / Alto's Odyssey style) — not a guarantee of profit, but the industry-standard structure for this genre. Full breakdown in §5. I'm not a financial advisor; treat this as an architecture recommendation, not investment advice. |
| Analytics | **Firebase Analytics** (free tier, iOS-native, integrates with Unity) | Confirmed requirement (4a). Free at your scale, no separate backend to run. |
| Branch strategy | **Trunk-based, cell-scoped short-lived branches** | Detailed in §9. Chosen specifically to minimize retrieval/context-loading overhead per your stated priority. |
| Input | Unity's new **Input System** + two independent custom touch surfaces (left: virtual joystick: right: target-reticle + fire buttons) | Matches your dual-control-scheme spec exactly (§3). |

---

## 1. Open Assumptions Requiring Your Confirmation

I'm proceeding with these as working assumptions (allowed for risk analysis, not silently baked into implementation without your sign-off). Flag any that are wrong before Phase 1 code starts:

1. **2-player mode is co-op survival** (both players in the same endless run, shared or split score), not PvP. Your spec describes a single continuous survival loop — nothing suggests competitive combat between the two jets. **Confirm or correct.**
2. **"Ground target set + trigger launchers/missiles"** = a secondary weapon system (missiles/rockets) distinct from the always-on primary gun, aimed via the right-hand touch surface at ground-based targets specifically (as opposed to the primary gun's air targets). **Confirm this split (air auto-fire vs. ground-locked missiles) is correct**, or clarify if missiles can also hit air enemies.
3. **Power-ups are consumable/temporary** (speed, fire-rate boosts) unless you intend some to be permanent for the run. Assumed temporary-with-decay or temporary-until-death.
4. **Coins are the only earn mechanic pre-store**, i.e., no secondary currency earned in-run (gems are IAP-only, possibly with a small earnable trickle via achievements — standard pattern, confirm if you want gems earnable in-run at all).
5. **Loot-box/crate mechanics are cosmetic-only if used at all.** Apple App Store Guideline 3.1.1 requires odds disclosure for any randomized paid content, and pay-to-win randomization tends to tank retention in skill-based genres. Flagging this now so it's a decision, not a surprise during App Store review.
6. **"Never move jet in vertical axis"** — read as: player has no direct vertical *world-space altitude* control; the jet's screen-Y movement is lateral-only within a fixed depth/altitude band. If you actually meant the jet can move up/down on screen (2D screen Y) but not toward/away from camera (world-space depth/Z), that changes which axis gets locked in §3 — **please confirm which axis you mean** (this is the single highest-impact clarification for the movement system).

---

## 2. Game Systems Blueprint (by schema domain)

### `player` — Jet & Flight
- Rigidbody-driven flight body with arcade tuning curves (max speed, acceleration, drag, bank angle vs. lateral velocity).
- Intro sequence state machine: `Spawn → CountdownFive..One → Go → PlayerControl`. Jet enters from bottom of screen along a scripted path (non-physics, tween/animation-driven) until `Go`, then control hands off to physics.
- Primary weapon: auto-fire from front-center mount, fixed base rate (1 round/sec), modifiable by fire-rate power-ups (multiplicative stacking, capped).
- Secondary weapon: lock-on missile/launcher, manually triggered, targets acquired via right-hand reticle placed on ground targets.

### `weapon` — Primary Gun + Missiles
- `WeaponBase` ScriptableObject-driven: fireRate, damage, projectileType, poolTag.
- Bullet pool (object pooling, not Instantiate/Destroy — mandatory for mobile perf at continuous 1/sec+ fire rates over long endless runs).
- Missile system: separate pool, homing/locked-trajectory toward the reticle-selected ground target, distinct damage profile (likely higher, area-effect viable for ground targets).

### `enemy` — Air/Ground Enemies
- Data-driven enemy types via ScriptableObject (`EnemyDef`: maxHealth, damagePerHit, moveSpeed, dropTable, threatWeight).
- Each enemy instance carries a runtime `EnemyHealth` component exposing an event (`OnDamaged(float pctRemaining)`), consumed by:
  - World-space UI health slider above the enemy, color-lerped green→yellow→red on `pctRemaining`.
  - Difficulty/analytics telemetry.
- Event-driven health bar updates (not per-frame polling) to protect mobile perf with many enemies on screen.

### `asset` (power-ups)
- `PowerUpDef` ScriptableObject: type (speed/fireRate/etc.), magnitude, duration (0 = permanent-for-run), visual/pickup prefab.
- Enemies reference a `dropTable` (weighted list of `PowerUpDef` + drop chance) rather than hardcoding per-enemy drops — keeps balancing data-only, no code changes needed to retune.
- Player-side `PowerUpController` applies stacking rules (e.g., fire-rate stacks multiplicatively up to a hard cap to keep the 1/sec baseline meaningful and prevent runaway DPS trivializing the game).

### `physics` — Movement Constraint Layer
See §3 — this is the one system with real technical risk, broken out separately.

### `network` — 2-Player P2P
See §6.

### `ui` — Controls & HUD
- Left-hand virtual joystick: screen-space overlay, fixed or floating anchor (your call, floating is generally better UX on iOS), outputs a normalized 2D vector consumed by the flight controller for lateral/screen-plane movement only.
- Right-hand surface: independent touch zone — drag/tap to place ground-target reticle, separate button(s) to trigger missile launch at the currently locked target.
- HUD: health, coins-this-run, power-up status icons + remaining duration, enemy health sliders (world-space, per §2 enemy).
- These are two **independent** input regions — architected as separate `InputActionMap`s so left-hand input can never bleed into right-hand aiming/firing and vice versa (this is what guarantees "never move the jet" from the right-hand controls, structurally, not just by convention).

### `build_pipeline` — iOS Build/Perf
- Device tiering: since target is "all non-legacy iOS," build a quality-tier system (particle density, shadow quality, enemy on-screen cap) keyed off `SystemInfo` at first launch, with the adjustable-settings requirement (2c) exposed as a manual override on top of the auto-detected tier.
- IL2CPP + Metal (standard for iOS Unity builds); Burst is worth enabling for the physics/bullet-pool hot paths given "full physics" is a stated requirement, but only after Phase 1 profiling shows it's needed — don't pre-optimize.

### `scene` — Economy/Store
See §5.

---

## 3. Movement & Control Architecture — the one system with real technical risk

**The problem:** "full physics" (real inertia, drag, banking) is normally a 3-axis affair, but the spec requires movement locked to a 2D plane. Getting this wrong either kills the physics feel (feels like a 2D sprite, not a jet) or breaks the "never move vertically" constraint (feels physically correct but ignores the design brief).

**The approach:**
1. Jet is a full `Rigidbody` in 3D world space, with real drag/angular drag/torque-based banking — physics forces are computed on all axes so the *feel* (inertia, overshoot, banking lean into turns) is authentic.
2. Immediately after each `FixedUpdate` physics step, a `PlaneConstraint` component **zeroes the locked axis's position delta and velocity component** — not via a `ConfigurableJoint` (joints introduce solver jitter and fight the arcade feel), but via direct post-solve correction. This is a standard technique for "2.5D" physics games.
3. Concretely (pending your answer to open question #6): if the locked axis is world-Z (depth), the jet physically simulates X (left-right) and Y (up-down, i.e., screen-vertical) with full force/drag/banking response, and Z is clamped to a fixed value every physics tick. If instead you meant the jet cannot move on screen-Y at all (only left-right), then X and Z (depth-into-screen, giving a subtle 3D parallax) get the physics treatment and Y is clamped.
4. Left-hand joystick output maps directly to a target-velocity on the two free axes; the physics controller applies force toward that target velocity rather than setting velocity directly — this preserves inertia/drag feel instead of making movement feel snapped/digital.

**Risk:** this is the item most likely to eat unplanned time. Recommend it as the **first thing built and playtested** (matches Sample D risk-first ordering) — if the constrained-physics feel isn't right, everything else (enemy pacing, weapon balance) is being tuned against a moving target.

---

## 4. Enemy & Difficulty Scaling Model

Requirement: enemies get stronger as the player collects power-ups, but must remain defeatable.

**Approach — power-relative scaling, not time-relative scaling:**
- Track a `PlayerPowerLevel` derived from currently-active power-up stacks (not raw survival time — this directly implements "as jet consumes more power-ups, enemy gets stronger").
- Enemy stat multiplier: `statMultiplier = 1 + k * PlayerPowerLevel`, where `k` is a tunable balance constant (data-driven, not hardcoded — put it in a `DifficultyCurve` ScriptableObject so you can retune without code changes).
- **Defeatability floor:** clamp `statMultiplier` so that `enemyEffectiveHealth / playerCurrentDPS` (time-to-kill) never exceeds a fixed ceiling, e.g. 8 seconds. This guarantees the fight is always winnable within a bounded engagement window even at high power levels — the classic bounded-DDA (dynamic difficulty adjustment) pattern.
- Enemy *variety* (not just stat scaling) should also scale with power level — new enemy archetypes unlock at power-level thresholds, which reads to the player as "getting stronger" without pure numeric bullet-sponge inflation (better game feel, same mechanism).

---

## 5. Economy & Monetization Architecture

**Currencies:**
- **Coins (soft):** earned continuously during survival + bonus per kill. Spent on: cosmetic skins, minor permanent upgrades (e.g., raise the fire-rate stacking cap), consumables (extra life / in-run revive).
- **Gems (hard):** IAP-primary, small earnable trickle via achievement milestones (not gameplay loops) to give F2P players a taste without undermining the purchase incentive.

**Store:**
- Coin-priced items: cosmetics, minor upgrades, consumables.
- Gem-priced items: premium cosmetics, "remove ads" (one-time), starter/booster bundles.
- **Avoid pay-to-win** on core combat stats (fire rate cap, damage) beyond a modest ceiling — skill-based endless genres retain better and monetize better long-term when power stays earnable, and it keeps you clean on App Store review for anything randomized.

**IAP ladder (coins/gems bought with money — your explicit requirement):**
Standard escalating-value tiers, e.g. $0.99 / $4.99 / $9.99 / $19.99 / $49.99 / $99.99, with bonus-% increasing at higher tiers to incentivize larger purchases. Implement via **Unity IAP** (wraps StoreKit).

**Ads:**
- Rewarded video: continue-after-death (highest-converting placement in this genre), double-coins-this-run, daily bonus crate.
- Interstitial: sparing, between runs only, toggle-off via a one-time "remove ads" IAP.

**Architect from day 1 for extensibility**, even though season-pass/live-ops is a `post`-phase item: use a single `CurrencyType` enum + `Wallet` service rather than hardcoding "coins" and "gems" as separate systems, so adding a third currency later (event tokens, etc.) doesn't require a rewrite.

**Compliance flag:** if any randomized purchase (crate/gacha) ships, Apple requires odds disclosure (Guideline 3.1.1) — build the store UI with an odds-display slot from the start even if unused initially, cheaper than retrofitting.

---

## 6. Multiplayer (2-Player P2P) Architecture

- **Primary:** Apple GameKit `GKMatch` (`GKMatchmakerViewController` or programmatic matchmaking) for real-time P2P between exactly 2 players. No server infrastructure, no hosting cost, built into iOS/Game Center.
- Co-op survival (per assumption #1): both jets share the same enemy spawn stream; state that must sync: enemy spawns/positions/health, power-up spawns/pickups, each player's position/fire events. Given P2P (no authoritative dedicated server), designate **one peer as the authority** for enemy spawn/health state (host-authoritative P2P, common pattern) to avoid desync on enemy HP.
- **Upgrade path (documented so we don't corner ourselves, per your engine question):** if you ever go cross-platform or want dedicated-server-quality sync, GameKit's match layer can be swapped for Photon Fusion or PlayFab Multiplayer behind the same internal networking interface — build a thin `INetworkTransport` abstraction now so GameKit is an implementation, not baked into gameplay code.

---

## 7. Analytics Architecture

- Firebase Analytics (confirmed requirement 4a), instrumented on: run start/end, survival duration, cause of death, power-ups collected, coins earned/spent, IAP conversion events, ad-watch events, difficulty-tier reached.
- These events double as your monetization tuning data — survival-duration and death-cause distributions are what you'll use to tune the difficulty curve in §4 post-launch.

---

## 8. Roadmap — Risk-Ordered Phase × Domain Matrix

Ordered by technical risk (Sample D), structured as a matrix (Sample C) so each cell is one retrieval-scoped unit of work.

| Phase | Focus (risk-ordered) | player | physics | weapon | enemy | network | scene/economy | build_pipeline |
|---|---|---|---|---|---|---|---|---|
| **1** | Constrained-physics flight feel | Rigidbody flight body, joystick input mapping | Plane-constraint layer (§3), tuning pass | primary gun stub (1/sec, no damage variety yet) | — | — | — | iOS project scaffold, device-tier stub |
| **2** | Combat core | intro sequence (spawn + countdown) | — | full primary gun + missile/launcher system, right-hand targeting | first enemy type, health slider UI, damage events | — | — | — |
| **3** | Endless loop + scaling | power-up consumption effects on player stats | — | fire-rate/speed power-up integration | enemy variety, drop tables, DDA scaling (§4) | — | coins earn-on-survive/kill | — |
| **4** | 2-Player P2P | — | — | — | host-authoritative enemy sync | GameKit `GKMatch` integration, `INetworkTransport` abstraction | — | — |
| **5** | Economy & Monetization | — | — | — | — | — | store UI, Wallet/CurrencyType service, Unity IAP ladder, ad SDK integration | — |
| **6** | iOS Hardening & Analytics | accessibility/settings (2c: adjustable perf/resolution) | perf profiling pass, Burst if warranted | — | — | — | Firebase Analytics instrumentation | quality-tier system, cert checklist |
| **Post** | Live-ops | — | — | balance patches from analytics | new enemy archetypes | — | season pass / new store rotations | — |
| **Upgrade** | Engine/SDK maintenance | — | — | — | — | networking abstraction swap if needed | — | Unity LTS version bumps |

**Definition of "playable" per phase** (vertical-slice acceptance, so you always know if a phase is actually done):
- **Phase 1 done when:** jet flies under physics, feels right, movement never leaves the locked plane, joystick control is responsive on-device (not just in-editor).
- **Phase 2 done when:** one full engagement loop works — primary gun kills air enemy, health bar animates/colors correctly, missile system locks and destroys a ground target.
- **Phase 3 done when:** a 5+ minute endless run is playable solo, power-ups visibly change jet behavior, difficulty escalates and remains beatable at increasing power levels.
- **Phase 4 done when:** two physical iOS devices can complete a co-op run together with no visible enemy-health desync.
- **Phase 5 done when:** a real IAP purchase (sandbox) completes end-to-end and coins/gems correctly unlock a store item.
- **Phase 6 done when:** builds run acceptably on your lowest supported non-legacy device at the "low" quality tier, and analytics events are visible in the Firebase dashboard.

---

## 9. Branching & Agent-Gateway Workflow Integration

**Trunk-based, cell-scoped branches** — chosen specifically to minimize retrieval/context overhead:

- `main` is always buildable and always passes `gateway/validate_repo.py` + the full test suite (enforced by the hooks/CI already built).
- One short-lived branch per **matrix cell** from §8 (e.g., `phase1/physics-plane-constraint`, `phase2/weapon-missile-lock`). A cell is small enough that an agent session working on it only needs to retrieve that cell's symbols/KB chunks — this is the direct mechanic behind "minimum context loading/retrieval tool calls" you asked for.
- Each branch, on completion, triggers (via the existing `knowledge_update` action) a graph node tagged with its phase+domain, so future retrieval (`retrieve`/`symbol_lookup`) can be scoped by phase or domain instead of searching the whole KB.
- No long-lived feature branches, no GitFlow `develop`/`release` overhead — solo dev doesn't benefit from that ceremony, and it would only add tokens/steps to every session without adding safety you need at this team size.
- Merge to `main` = squash merge, one commit per cell, so `git log` itself becomes a phase-ordered changelog (matches your "use git history instead of verbose chat changelogs" preference).

---

## 10. Tech Stack Summary

| Layer | Choice |
|---|---|
| Engine | Unity 6 LTS |
| Physics | Built-in PhysX (Rigidbody) + custom plane-constraint layer |
| Input | Unity Input System, dual independent touch action maps |
| Networking | Apple GameKit `GKMatch`, behind an `INetworkTransport` abstraction |
| IAP | Unity IAP (StoreKit) |
| Ads | Rewarded + interstitial SDK (AdMob or Unity Ads — pick at Phase 5, not before) |
| Analytics | Firebase Analytics |
| Data | ScriptableObject-driven (`EnemyDef`, `WeaponBase`, `PowerUpDef`, `DifficultyCurve`) |
| Perf | IL2CPP + Metal; Burst opt-in after Phase 1 profiling |

---

## 11. Risk Register

| Risk | Phase | Mitigation |
|---|---|---|
| Constrained-physics flight doesn't feel right | 1 | Built and playtested first, before any other system depends on it |
| P2P enemy-state desync (no authoritative server) | 4 | Host-authoritative pattern; abstraction layer allows swapping to a real backend later without a rewrite |
| App Store rejection over randomized purchases | 5 | Cosmetic-only randomization if used at all; odds-disclosure UI built in from the start |
| "ASAP" timeline vs. "full physics" + "full release" scope | all | Risk-first ordering means the scariest unknown (physics feel) is resolved before content investment; scope for full release should still be reviewed against your actual timeline once Phase 1 velocity is known |
| Difficulty curve either trivial or unfair | 3 | Bounded DDA (§4) with a hard time-to-kill ceiling, tuned against real analytics post-launch |

---

## Next Step

Confirm or correct the 6 open items in §1 — item **#6 (which axis is locked)** is the one blocking Phase 1 architecture specifics; everything else can proceed in parallel. Once confirmed, I'll produce the Phase 1 technical spec (concrete class list, ScriptableObject schemas, and the exact `knowledge_update`/branch sequence) as the next deliverable.
