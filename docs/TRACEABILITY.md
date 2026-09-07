# Traceability matrix

The repo has requirements (roadmap §2–§7), a phase×domain plan (roadmap §8),
per-phase designs with branch sequences and acceptance criteria (the six
`PHASE*_TECHNICAL_SPEC.md` files), a decision log (`docs/DECISIONS.md`), a
gateway that can index knowledge, and five CI workflows that can prove
things.

Nothing joined them. Given a branch you could not say which requirement it
served; given a requirement you could not say which check proves it; and the
`knowledge_update` node ids scattered across the phase specs had no single
list, so nobody could tell which ones had actually been created.

This file is that join. One row per cell. It is the checklist that
`docs/SDLC_SPIRAL.md` exit gate #5 reads.

## How to use it

- **Opening a cell:** find your row. It names the ADR you must not violate
  and the criterion you must satisfy. That is your session's scope.
- **Closing a cell:** tick `Done`, and confirm the node id in the last column
  now exists in `knowledge/seeds.json`. A ticked row with no seed node means
  the phase-scoped retrieval for that cell will return nothing.
- **Node id convention:** the branch name with `/` and `-` replaced by `_`.
  `phase1/physics-plane-constraint` → `phase1_physics_plane_constraint`.

  **Where a phase spec's `knowledge_update` example disagrees, the `Seed node`
  column here wins,** and the cell's PR corrects the spec. All four Phase 1
  examples predate this convention and name something shorter
  (`phase1_plane_constraint` for the row above). The ids have to agree with
  something derivable from the branch name, or the check in "Closing a cell"
  is a lookup nobody can perform. `phase1/player-flight-rigidbody` corrected
  its own example; the remaining three are each their own cell's to fix.

Cycle 0's first row is ticked; every other row is `[ ]`. That is the accurate
state, not an oversight — the scaffold exists, the gameplay code does not.

---

## Cycle 0 — Bootstrap  *(spec: `docs/CYCLE0_BOOTSTRAP_SPEC.md`)*

| Done | Branch | Domain | Requirement | ADR | Verified by | Seed node |
|---|---|---|---|---|---|---|
| [x] | `phase1/build-ios-scaffold` | build_pipeline | Roadmap §8 Phase 1 build_pipeline — iOS project scaffold | ADR-009 | `symbols/index.json` non-empty after ingest | `phase1_build_ios_scaffold` |
| [x] | `phase0/knowledge_base-seeds-layer` | knowledge_base | *Gap-fill* — durable phase nodes survive ingest | ADR-008 | `enforce.yml` green with seeds committed; seeded node present after a fresh ingest | `phase0_knowledge_base_seeds_layer` |
| [x] | `phase0/ci-unity-test-workflow` | ci | *Gap-fill* — C# tests have somewhere to run | ADR-010 | Unity test workflow green on a trivial passing test | `phase0_ci_unity_test_workflow` |

## Cycle 1 — Constrained-physics flight feel  *(risk R1)*

| Done | Branch | Domain | Requirement | ADR | Verified by | Seed node |
|---|---|---|---|---|---|---|
| [x] | `phase1/player-flight-rigidbody` | player | Roadmap §2 `player`, §3.1 — real inertia/drag/banking | — | Inertia and banking validated unconstrained, in isolation | `phase1_player_flight_rigidbody` |
| [x] | `phase1/physics-plane-constraint` | physics | Roadmap §3.2 — post-solve axis clamp | **ADR-001** | Locked-axis deviation stays within epsilon over a long run | `phase1_physics_plane_constraint` |
| [x] | `phase1/input-joystick-mapping` | ui | Roadmap §2 `ui` — left-region joystick, no bleed | — | Left-region-only guarantee test; on-device touch test | `phase1_input_joystick_mapping` |
| [x] | `phase1/weapon-gun-stub` | weapon | Roadmap §2 `weapon` — pooled 1/sec auto-fire | — | Pool stays bounded; no `Instantiate`/`Destroy` in the fire path | `phase1_weapon_gun_stub` |

**Cycle gate (roadmap §8):** jet flies under physics, feels right, never
leaves the locked plane, responsive on-device.

## Cycle 2 — Combat core

| Done | Branch | Domain | Requirement | ADR | Verified by | Seed node |
|---|---|---|---|---|---|---|
| [x] | *(retrofit)* gun reads `PlayerStatsRuntime` | weapon | Phase 2 spec, cross-phase note | — | Runs **before** the cells below. Landed as the `IPlayerStats` seam — `PlayerStatsRuntime` itself is Cycle 3, so the interface ships here and the implementation ships in its own cell, with no weapon code changing then | — |
| [x] | `phase2/enemy-core` | enemy | Roadmap §2 `enemy` — data-driven `EnemyDef`, damage events | — | Damage pipeline unit-tested through `IDamageable` | `phase2_enemy_core` |
| [x] | `phase2/enemy-healthbar-ui` | ui | Roadmap §2 `enemy` — event-driven world-space bar | — | Bar updates on event, not per-frame polling | `phase2_enemy_healthbar_ui` |
| [x] | `phase2/weapon-bullet-damage` | weapon | Roadmap §2 `weapon` — bullet damage pipeline | — | Bullet applies damage via `IDamageable` | `phase2_weapon_bullet_damage` |
| [x] | `phase2/right-hand-targeting` | ui | Roadmap §2 `ui` — independent right-hand surface | **ADR-002** | Right-half touches move the reticle; left-half touches have zero targeting effect | `phase2_right_hand_targeting` |
| [x] | `phase2/missile-system` | weapon | Roadmap §2 `weapon` — ground-locked missiles | **ADR-002** | Missile destroys targeted ground enemy; cooldown holds; pool bounded under soak | `phase2_missile_system` |
| [x] | `phase2/intro-sequence` | player | Roadmap §2 `player` — spawn/countdown state machine | — | Control hands off to physics only at `Go` | `phase2_intro_sequence` |

**Cycle gate:** one full engagement loop — gun kills air enemy, health bar
animates and colours correctly, missile locks and destroys a ground target.

## Cycle 3 — Endless loop and scaling  *(risk R5)*

| Done | Branch | Domain | Requirement | ADR | Verified by | Seed node |
|---|---|---|---|---|---|---|
| [x] | `phase3/player-stats-runtime` | player | Roadmap §2 `asset` — power-ups mutate player stats | — | Stat changes observable at runtime | `phase3_player_stats_runtime` |
| [x] | `phase3/powerup-core` | asset | Roadmap §2 `asset` — stacking with a hard cap | **ADR-003** | Multiplicative stacking never exceeds the cap | `phase3_powerup_core` |
| [x] | `phase3/enemy-drop-tables` | enemy | Roadmap §2 `asset` — weighted drops, data-only balancing | — | Drop weights are retunable with no code change | `phase3_enemy_drop_tables` |
| [x] | `phase3/difficulty-scaling` | enemy | Roadmap §4 — bounded DDA | — | Time-to-kill never exceeds the ceiling across a `PlayerPowerLevel` sweep | `phase3_difficulty_scaling` |
| [x] | `phase3/enemy-spawner-waves` | enemy | Roadmap §4 — archetype variety by threshold | — | Archetypes unlock at the configured thresholds | `phase3_enemy_spawner_waves` |
| [x] | `phase3/economy-coins` | scene | Roadmap §5 — currency-generic `Wallet` | **ADR-004** | Adding a third currency requires no `Wallet` change | `phase3_economy_coins` |

**Cycle gate:** a 5+ minute solo run; power-ups visibly change behaviour;
difficulty escalates and stays beatable.

## Cycle 4 — 2-player P2P  *(risk R4)*

| Done | Branch | Domain | Requirement | ADR | Verified by | Seed node |
|---|---|---|---|---|---|---|
| [x] | `phase4/network-abstraction` | network | Roadmap §6 — `INetworkTransport` seam | **ADR-005** | Loopback transport drives gameplay with no GameKit present | `phase4_network_abstraction` |
| [x] | `phase4/player-state-sync` | network | Roadmap §6 — position/fire sync | ADR-005 | Passes over loopback before any device is involved | `phase4_player_state_sync` |
| [x] | `phase4/host-authoritative-enemies` | enemy | Roadmap §6 — one authority for enemy state | ADR-005 | No enemy-HP divergence over loopback | `phase4_host_authoritative_enemies` |
| [ ] | `phase4/gamekit-transport` | network | Roadmap §6 — GameKit as an implementation, not a dependency | ADR-005 | **Zero gameplay-code changes** when swapping transport — this is the abstraction's own test | `phase4_gamekit_transport` |
| [ ] | `phase4/network-soak-test` | network | Roadmap §11 R4 | ADR-005 | Two physical devices, full co-op run, no visible desync; brief interruption crashes neither client | `phase4_network_soak_test` |

**Cycle gate:** two physical iOS devices complete a co-op run with no visible
enemy-health desync.

## Cycle 5 — Economy and monetization  *(risk R6)*

| Done | Branch | Domain | Requirement | ADR | Verified by | Seed node |
|---|---|---|---|---|---|---|
| [ ] | `phase5/product-catalog` | scene | Roadmap §5 — data-only pricing | ADR-004 | Price changes touch no code | `phase5_product_catalog` |
| [ ] | `phase5/iap-integration` | scene | Roadmap §5 — IAP ladder | ADR-004 | Sandbox purchase credits gems via the Phase 3 `Wallet` | `phase5_iap_integration` |
| [ ] | `phase5/store-ui` | ui | Roadmap §5 — dual-currency store | ADR-004 | Both currencies purchase and deduct correctly | `phase5_store_ui` |
| [ ] | `phase5/ads-integration` | scene | Roadmap §5 — rewarded + interstitial, remove-ads IAP | — | Remove-ads suppresses interstitials permanently | `phase5_ads_integration` |
| [ ] | `phase5/compliance-odds-ui` | ui | Roadmap §5 — Guideline 3.1.1 slot | **ADR-006** | Renders correctly in a test harness while dormant | `phase5_compliance_odds_ui` |

**Cycle gate:** a sandbox IAP completes end-to-end and unlocks a store item.

## Cycle 6 — iOS hardening and analytics

| Done | Branch | Domain | Requirement | ADR | Verified by | Seed node |
|---|---|---|---|---|---|---|
| [ ] | `phase6/analytics-instrumentation` | scene | Roadmap §7 — Firebase event set | — | Every listed event appears in the Firebase dashboard on a real run | `phase6_analytics_instrumentation` |
| [ ] | `phase6/settings-ui` | ui | Spec item 2c — adjustable settings | — | Manual override persists across relaunch and visibly changes rendering | `phase6_settings_ui` |
| [ ] | `phase6/perf-profiling-pass` | physics | Roadmap §2 `build_pipeline` — Burst decided from data | — | Sustained fps floor at low tier on the lowest supported device | `phase6_perf_profiling_pass` |
| [ ] | `phase6/appstore-cert-checklist` | build_pipeline | Roadmap §8 Phase 6 | ADR-006 | Archives cleanly with all required Privacy Manifests | `phase6_appstore_cert_checklist` |

**Cycle gate:** acceptable frame rate at low tier on the lowest supported
device, and analytics visible in Firebase.

---

## Coverage check

Every roadmap §8 matrix cell with content maps to at least one row above.
Deliberate exclusions, so their absence is a decision rather than an
oversight:

- **`audio`** — a schema domain with no roadmap cell and no phase spec. Not
  planned in any cycle. If audio ships, it needs a spec and rows here first.
- **Post / Upgrade** — no rows. Roadmap §8 and the Phase 6 spec both state
  these are deliberately unspecified until real analytics and real SDK
  release timing exist. Adding speculative rows would be inventing
  requirements.
