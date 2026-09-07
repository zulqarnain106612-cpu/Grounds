# Decision log (ADRs)

Roadmap §1 lists six assumptions that must be confirmed before they are
"silently baked into implementation." Four of them were later confirmed —
but the confirmation lives only as a passing phrase inside a phase spec
("per your confirmed assumption"), and two were resolved only *by
implication* from a design choice, never recorded as a decision at all.

That is the gap this file fills: a single place where the status of every
load-bearing decision is explicit, so `docs/SDLC_SPIRAL.md`'s quadrant I gate
("no cycle enters implementation depending on a `Proposed` ADR") has
something to check against.

**Status values:** `Proposed` — not yet confirmed by the project owner.
`Accepted` — confirmed, with the source recorded. `Accepted (implied)` —
a spec depends on it and treats it as settled, but no explicit confirmation
is on record. `Superseded` — replaced by a later ADR.

This file does not change any existing decision. It records what the repo
already decided, plus where the record is thin.

---

## ADR-001 — Locked movement axis is world-Z (depth)

- **Status:** Accepted
- **Cycle:** 1 · **Roadmap ref:** §1 assumption #6, §3
- **Source:** `docs/PHASE1_TECHNICAL_SPEC.md`, header — "Locked-axis decision
  applied: world-Z (depth) is locked; X (left-right) and Y (up-down) are free
  and physics-driven."
- **Decision:** The jet simulates full physics on X and Y. Z is clamped to a
  fixed value after every `FixedUpdate` solve by `PlaneConstraint`.
- **Consequence:** The player has on-screen vertical movement. Roadmap §1
  called this "the single highest-impact clarification" and it blocked all of
  Phase 1; it is now settled and every Phase 1 class depends on it.

## ADR-002 — Missiles are ground-only; the primary gun handles air

- **Status:** **Accepted (implied)** — confirm before Cycle 2 closes
- **Cycle:** 2 · **Roadmap ref:** §1 assumption #2
- **Source:** `docs/PHASE2_TECHNICAL_SPEC.md` §2 — `TargetReticleInput`
  "raycasts from touch position to a `Ground` layer"; the acceptance criterion
  reads "missile locks onto and destroys the targeted **ground** enemy."
- **Decision:** Two independent weapon systems. Primary gun: always-on
  auto-fire, air targets. Secondary: manually triggered missiles, restricted
  to ground targets by the reticle's raycast layer mask.
- **Why this is only implied:** the design encodes the split, but roadmap §1
  asked whether missiles may *also* hit air targets and no answer was
  recorded. Nothing in the repo says "no" — the `Ground` layer mask just
  makes it so.
- **Consequence if wrong:** a layer-mask change plus a targeting-priority
  rule. Contained, but it invalidates the Cycle 2 acceptance criteria.

## ADR-003 — Power-ups are temporary, with `duration = 0` meaning permanent-for-run

- **Status:** Accepted
- **Cycle:** 3 · **Roadmap ref:** §1 assumption #3
- **Source:** `docs/PHASE3_TECHNICAL_SPEC.md` §2 — `duration` "(0 =
  permanent-for-run, per your confirmed assumption)".
- **Decision:** `PowerUpDef.duration` is a float; zero is the sentinel for
  run-scoped-permanent. `PowerUpController` tracks remaining duration per
  active stack.
- **Consequence:** `PlayerPowerLevel` — the input to the difficulty curve —
  is derived from *currently active* stacks, so difficulty falls again as
  power-ups expire. That is intended, and it is what makes the scaling
  power-relative rather than time-relative.

## ADR-004 — Coins are the only in-run earn; gems are IAP-primary

- **Status:** **Accepted (implied)** — confirm before Cycle 5 closes
- **Cycle:** 3 (wallet), 5 (IAP) · **Roadmap ref:** §1 assumption #4, §5
- **Source:** `docs/PHASE3_TECHNICAL_SPEC.md` §2 — "Only `Coins` is active
  this phase"; `docs/PHASE5_TECHNICAL_SPEC.md` §2 — IAP products grant
  `Gems`.
- **Decision:** `CurrencyType { Coins, Gems }` from Phase 3. Coins earned by
  surviving and killing; gems purchased.
- **Why this is only implied:** roadmap §5 also proposes "a small earnable
  trickle via achievement milestones." No phase spec implements achievements
  and no decision records whether that trickle ships. Treat gems as
  IAP-only until this ADR is confirmed either way.
- **Consequence:** deferring achievements is cheap — `Wallet` is already
  currency-generic, so adding an earn source later touches no enum and no
  service.

## ADR-005 — 2-player mode is co-op survival, not PvP

- **Status:** Accepted
- **Cycle:** 4 · **Roadmap ref:** §1 assumption #1, §6
- **Source:** `docs/PHASE4_TECHNICAL_SPEC.md`, header — "Co-op survival (per
  your confirmed assumption), host-authoritative for enemy state."
- **Decision:** Both jets share one endless run and one enemy spawn stream.
  One peer is authoritative for enemy spawn and health state.
- **Consequence:** This is the highest-blast-radius ADR in the project. PvP
  would change matchmaking, the sync set, the authority model and the entire
  Cycle 4 acceptance criterion. It is settled; it should not be reopened
  casually.

## ADR-006 — Randomized purchases are cosmetic-only; odds UI ships dormant

- **Status:** Accepted
- **Cycle:** 5 · **Roadmap ref:** §1 assumption #5, §5
- **Source:** `docs/PHASE5_TECHNICAL_SPEC.md` §2 — "confirmed assumption:
  cosmetic-only, no pay-to-win loot boxes — this component stays dormant
  unless that changes."
- **Decision:** No pay-to-win randomization. The odds-disclosure component is
  built and tested in Cycle 5 even though nothing uses it at launch.
- **Consequence:** Building an unused component is deliberate. Apple
  Guideline 3.1.1 requires odds disclosure for randomized paid content, and
  the expensive time to discover a missing disclosure UI is during review.
  Re-read the current guidelines at Cycle 5 rather than trusting this note.

---

## Decisions found while auditing the repo against its own plan

The four below were not in the roadmap. They are recorded here because
`docs/SDLC_SPIRAL.md` depends on them.

## ADR-007 — Cycle 0 exists, before roadmap Phase 1

- **Status:** Accepted
- **Cycle:** 0 · **Risk:** R2
- **Context:** `config/agent.config.json` sets
  `source_globs: ["Assets/_Game/Scripts/**/*.cs"]`. There is no `Assets/`
  directory in the repo, so `symbols/index.json` contains zero symbols —
  which the README acknowledges. The roadmap treats the iOS scaffold as a
  task *inside* Phase 1.
- **Decision:** Scaffold the Unity project as a distinct cycle **before**
  Phase 1, spec'd in `docs/CYCLE0_BOOTSTRAP_SPEC.md`.
- **Rationale:** Phase 1 is the highest-risk cycle (R1, exposure 20). Running
  it while `symbol_lookup` returns nothing means every session in the riskiest
  cycle works without the retrieval mechanism the whole harness was built to
  provide. Alternatives considered: fold into Phase 1 (rejected — leaves R2
  and R3 live during the worst possible cycle); drop the symbol index until
  later (rejected — it is load-bearing for roadmap §9's context-minimisation
  argument, which is the stated reason for cell-scoped branching).
- **Consequence:** One extra cycle before gameplay work. It does not change
  any Phase 1–6 content.

## ADR-008 — Durable knowledge lives in `knowledge/seeds.json`, merged by ingest

- **Status:** Accepted
- **Cycle:** 0 · **Risk:** R3
- **Context:** Every phase spec ends with a `knowledge_update` node pattern
  tagged by phase, and roadmap §9 makes phase-scoped retrieval the
  justification for cell-scoped branches. But
  `gateway/ingest.py:build_graph()` derives the graph from domains and chunk
  kinds and writes `knowledge/graph.json` wholesale. Any node added at
  runtime is erased by the next ingest — which runs nightly on cron. While
  such a node is committed, `enforce.yml`'s staleness check also fails,
  because a fresh ingest cannot reproduce it.
- **Decision:** Add `knowledge/seeds.json` — a tracked, hand-authored node
  and edge set that `build_graph()` unions into the derived graph.
- **Rationale:** Runtime and durable knowledge have genuinely different
  lifetimes and should stay separate systems; the missing piece was a carrier
  between them, not a merge of them. Because seeds are a tracked *input*, a
  fresh ingest reproduces them exactly, so the staleness guarantee is
  preserved rather than weakened. Alternatives considered: make
  `apply_knowledge_op` write to a side file read at retrieval time (rejected —
  two sources of truth for one graph); stop regenerating the graph (rejected —
  destroys the staleness guarantee that `docs/ENFORCEMENT.md` claims).
- **Consequence:** Inner-loop step 7 — promoting a cell's node into
  `seeds.json` in the merging PR — becomes mandatory. An unpromoted node is
  gone within a day.

## ADR-009 — `config/agent.config.json` engine field corrected to Unity 6 LTS

- **Status:** Accepted
- **Cycle:** 0
- **Context:** Roadmap §0 locks **Unity 6 LTS** with explicit rationale
  ("not 2022 LTS"), and §10 repeats it. `config/agent.config.json` said
  `"engine": "Unity 2022 LTS"` — the exact version the roadmap rejected.
- **Decision:** The config now reads `Unity 6 LTS`. The roadmap is the source
  of truth; the config was stale.
- **Rationale:** The config is machine-readable and feeds agent context. A
  config that contradicts the locked decision will eventually cause an agent
  to scaffold against the wrong LTS track. Nothing in `gateway/ingest.py`
  reads this field into the KB, so correcting it does not change any
  generated index.
- **Consequence:** Cycle 0 scaffolds on Unity 6 LTS. If the project owner
  actually wants 2022 LTS, this ADR is the thing to supersede — and roadmap
  §0 and §10 would need updating too.

## ADR-010 — Verification runs in CI, never locally

- **Status:** Accepted (restates existing policy)
- **Cycle:** all
- **Context:** `config/agent.config.json → execution_policy` marks `build`,
  `test`, `review`, `ingest`, `manifest`, `poll` and `index` `FORBIDDEN`
  locally, and `CIOp.wait` is `const: false` so a request cannot block on a
  run.
- **Decision:** Restated here so `docs/SDLC_SPIRAL.md` §3 step 5 has a
  citable source: every verification in this process is a dispatched
  workflow whose result is read back later. No process step in this repo may
  require a local build or test run.
- **Consequence:** Evidence in a cycle review is a CI run link, not a local
  terminal paste.

## ADR-011 — `meta.phase` enum is left unchanged; Cycle 0 reports phase `"1"`

- **Status:** Accepted
- **Cycle:** 0
- **Context:** `schema/agent.schema.json` constrains `meta.phase` to
  `["1","2","3","4","5","6","post","upgrade"]`. ADR-007 introduces Cycle 0,
  which has no corresponding enum value, so a Cycle 0 session literally
  cannot describe itself — the gateway would reject the request.
- **Decision:** Do not add `"0"` to the enum. Cycle 0 sessions declare
  `meta.phase: "1"`.
- **Rationale:** Cycle 0's work *is* roadmap Phase 1's `build_pipeline`
  cell — the roadmap's own `phase1/build-ios-scaffold` branch — plus the
  harness that cell depends on. Reporting phase `1` is accurate, not a
  workaround. Widening the enum would change the schema, which is the
  repo's source of truth, force a `docs/SCHEMA.md` regeneration, and add a
  phase value that no roadmap cell, phase spec or retrieval scope uses.
  Changing the source of truth to accommodate a process document is the
  wrong direction of dependency.
- **Note:** `KnowledgeNode.phase` is a free-form string array and is *not*
  enum-constrained, so seed nodes may still carry `"0"` for retrieval
  scoping. The two fields are unrelated; only `meta.phase` is restricted.
- **Consequence:** If Cycle 0 ever grows work that is genuinely not Phase 1
  build_pipeline, revisit this rather than stretching the meaning of
  phase `1`.

## ADR-012 — iOS Player Settings are held in `config/ios.build.json`, applied to Unity by script

- **Status:** Accepted
- **Cycle:** 0
- **Context:** `phase1/build-ios-scaffold` must pin the Bundle ID, minimum
  iOS version, Metal and IL2CPP. Unity's home for those is
  `ProjectSettings/ProjectSettings.asset` — generated YAML, several thousand
  lines, hand-editing it is exactly what "never hand-edit generated files"
  forbids. It also cannot be checked without booting the editor, and
  ADR-010 says every verification runs in CI, where no Unity licence exists
  yet (that licence is `phase0/ci-unity-test-workflow`'s problem, not this
  cell's).
- **Decision:** The values live in `config/ios.build.json`, hand-authored
  alongside `config/agent.config.json`.
  `Assets/_Game/Editor/IOSPlayerSettings.cs` reads that file and applies it
  to `PlayerSettings`, invoked from the `JetFighter/Apply iOS Player
  Settings` menu item and callable from a batch-mode build.
  `ProjectSettings.asset` stays Unity-owned and is never hand-edited.
- **Rationale:** It makes the settings a testable artifact.
  `tests/test_ios_scaffold.py` asserts IL2CPP, Metal, ARM64 and a sane iOS
  floor on every PR, so a dropped setting fails a pull request in seconds
  instead of an Xcode archive an hour later. It also survives a Unity
  version bump rewriting the .asset.
- **Consequence:** The JSON and the .asset can drift if someone changes
  settings through the Unity inspector and does not update the JSON. The
  fix is to re-run the menu item, which makes the JSON authoritative again.
  When the Unity licence lands, `unity-test.yml` should run `Apply` in batch
  mode so drift is corrected in CI rather than by convention.
