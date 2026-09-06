# JetFighter — SDLC: Boehm Spiral, instantiated

This is the **process** document. It does not restate the architecture
(`docs/ARCHITECTURE_ROADMAP.md`) or the per-phase designs
(`docs/PHASE1..6_TECHNICAL_SPEC.md`). It defines how work moves from one of
those specs into merged, verified code, and what has to be true before a
cycle is allowed to close.

It exists because the repo previously had two disconnected halves:

- a **plan** — roadmap + six technical specs describing a game, and
- a **harness** — a schema-enforced gateway, five CI workflows and a test
  suite that can police a repo,

with nothing binding them together: no cycle definition, no entry/exit gates,
no decision log, no traceability from a requirement to the CI check that
proves it. This document plus `docs/DECISIONS.md` and
`docs/TRACEABILITY.md` are that binding layer.

---

## 1. Why spiral, and not something else

The roadmap already made the two choices that spiral formalises:

- **Risk-first ordering** (roadmap §8, "Sample D") — build the scariest
  unknown first. That is quadrant II of a spiral, not a waterfall stage.
- **Vertical-slice acceptance per phase** (roadmap §8, "Definition of
  playable") — every phase ends in something runnable. That is spiral's
  "develop and verify the next-level product", not a document handoff.

Waterfall would require the six open assumptions in roadmap §1 to be
resolved before any code, which is impossible: assumption #6 (which axis is
locked) is a *feel* question that only a prototype can answer. Pure Scrum
would drop the explicit risk-resolution step, which is the single most
valuable thing here given that "constrained-physics flight doesn't feel
right" is the project's top risk.

Spiral keeps both: each cycle is a full miniature SDLC, and each cycle is
*chosen* by which risk is currently largest.

---

## 2. The cycle template — four quadrants

Every cycle runs the same four quadrants. Nothing may be skipped; a quadrant
may be *short*, but it must produce its artifact, because the artifact is
what the next session reads instead of re-deriving context.

### Quadrant I — Objectives, alternatives, constraints

**Produces:** a cycle brief at the top of the cycle's section below, plus any
new entries in `docs/DECISIONS.md`.

- State the objective as the roadmap's "Definition of playable" line for that
  phase. Do not invent a new one.
- List the alternatives actually considered and the constraint that kills
  each. If there is only one option, say so — that is a finding, not a gap.
- Any assumption from roadmap §1 that this cycle depends on must be
  **resolved to an ADR** before quadrant III starts. An unresolved assumption
  entering implementation is the defect this process is designed to prevent.

### Quadrant II — Risk identification and resolution

**Produces:** an updated row in §4 (Risk register), and a **spike** —
throwaway code, not merged to `main`.

- Pick the top-exposure risk (§4). Build the smallest thing that resolves it.
- A spike is time-boxed and explicitly disposable. It lives on a
  `spike/<topic>` branch and is **never** merged. Its output is a decision
  recorded in `docs/DECISIONS.md`, not code.
- If the spike says the plan is wrong, the correct move is to amend the phase
  spec and re-enter quadrant I. That is the spiral working, not a failure.

### Quadrant III — Engineer and verify

**Produces:** merged PRs, one per phase×domain matrix cell.

This is the inner loop, run once per cell — see §3.

### Quadrant IV — Plan the next cycle, and review

**Produces:** the exit-gate checklist below, signed off, plus the next
cycle's brief.

**Exit gate — every box must be true before the next cycle opens:**

| # | Gate | How it is checked |
|---|---|---|
| 1 | The phase's "Definition of playable" (roadmap §8) demonstrably holds | Recorded evidence — a device capture or a test run — linked from the cycle section |
| 2 | Every acceptance criterion in that phase's technical spec is met | The spec's own "Acceptance Criteria" table, each row ticked |
| 3 | `enforce.yml`, `review.yml` and `retrieval-verify.yml` are green on `main` | `make ci-status` |
| 4 | Every new C# type appears in `symbols/index.json` | `ingest.yml` run, index PR merged |
| 5 | One durable knowledge node per merged cell exists in `knowledge/seeds.json` | `docs/TRACEABILITY.md` row filled in |
| 6 | Every ADR the cycle depended on has status `Accepted`, not `Proposed` | `docs/DECISIONS.md` |
| 7 | The risk register has been re-scored, not just re-read | §4, with a dated note |

Gate 5 is the one that is easy to skip and expensive to skip. See §6.

---

## 3. The inner loop — one matrix cell, one branch, one session

A **cell** is one (phase, domain) pair from roadmap §8. It is deliberately
small enough that an agent session working on it needs to retrieve only that
cell's context — which is the entire reason the roadmap chose cell-scoped
branches over feature branches.

Branch name: `phase<N>/<domain>-<slug>`, e.g. `phase1/physics-plane-constraint`.
This is the existing convention in the phase specs; it is restated here
because the branch name is what ties a PR to a traceability row.

The loop, per cell:

1. **Retrieve, don't read.** Open the session by pulling exactly this cell's
   context through the gateway — `retrieve` scoped to the phase, and
   `symbol_lookup` for the types being touched. Do not open the whole
   roadmap. This is the practice the gateway exists to make cheap.

2. **Explore, then plan, then code.** Separate the phases explicitly. Produce
   the plan *before* the first edit; if the diff cannot be described in one
   sentence, it needs a written plan first. Anthropic's guidance on this is
   direct: letting a coding agent jump straight to implementation reliably
   produces code that solves the wrong problem.

3. **Write the check before the code where possible.** Every cell must ship
   with something that returns pass/fail without a human looking at it —
   a Unity EditMode/PlayMode test, a CI assertion, or a scripted comparison.
   Cells whose acceptance is genuinely subjective (flight *feel*) still get a
   mechanical check for the objective half: "position on the locked axis
   never deviates by more than epsilon over a 10,000-tick run" is testable
   even though "feels right" is not. Split the criterion; automate the half
   that can be automated and record the other half as a device capture.

4. **Implement against the plan.**

5. **Verify — and show the evidence.** Dispatch CI (`make validate`,
   `make test`); never run the suite locally, per the execution policy in
   `config/agent.config.json`. Paste the run result, don't assert success.

6. **Adversarial review in a fresh context.** Before the PR is marked ready,
   have a reviewer that did not write the code check the diff against the
   phase spec, looking for *missing requirements*, not style. A reviewer
   biased by having just written the code is not a reviewer. Scope it:
   "report gaps against the spec's acceptance criteria; ignore preferences"
   — an unscoped reviewer will invent findings and drive over-engineering.

7. **Close the loop.** Squash-merge (one commit per cell, so `git log` reads
   as a phase-ordered changelog), then promote the cell's knowledge node into
   `knowledge/seeds.json` and fill the `docs/TRACEABILITY.md` row **in the
   same PR**. Step 7 is part of the cell, not cleanup after it.

**Context hygiene.** Reset context between cells. A session that has finished
one cell and starts another carries the first cell's files as noise, and
agent performance degrades as the context window fills. One cell, one
session, one reset.

---

## 4. Risk register — scored and re-scored per cycle

Roadmap §11 lists the risks. It does not score them, so "which risk is
largest right now" was unanswerable — that is the input quadrant II needs.

Exposure = Probability (1–5) × Impact (1–5). Re-score at every quadrant IV.

| ID | Risk | Cycle | P | I | Exp | Resolution move (quadrant II) |
|---|---|---|---|---|---|---|
| R1 | Constrained-physics flight does not feel right | 1 | 4 | 5 | **20** | Spike the `PlaneConstraint` post-solve correction on device before any dependent system is built |
| R2 | No Unity project exists; `source_globs` resolves to nothing, so symbol retrieval is dead | 0 | 5 | 4 | **20** | Cycle 0 — scaffold the project so the harness has something to index |
| R3 | Durable phase knowledge is lost on every ingest | 0 | 5 | 3 | **15** | `knowledge/seeds.json` merge layer — see §6 |
| R4 | P2P enemy-state desync with no authoritative server | 4 | 3 | 4 | 12 | Host-authoritative spike on two physical devices before gameplay sync is written |
| R5 | Difficulty curve trivial or unfair | 3 | 3 | 3 | 9 | Bounded-DDA time-to-kill ceiling, tuned against instrumented runs |
| R6 | App Store rejection over randomized purchases | 5 | 2 | 4 | 8 | Cosmetic-only randomization; odds-disclosure slot built into store UI from the start |
| R7 | Scope ("full release", "full physics") vs. timeline | all | 4 | 3 | 12 | Re-scope at every quadrant IV once real per-cycle velocity is known |
| R8 | Unresolved roadmap §1 assumptions silently baked into code | 1–5 | 4 | 4 | **16** | ADR gate — quadrant I cannot close with a `Proposed` ADR that the cycle depends on |

R2, R3 and R8 are not in roadmap §11. They were found while auditing the
repo against its own plan; see `docs/DECISIONS.md` ADR-007..009.

---

## 5. The cycles

### Cycle 0 — Harness and bootstrap *(new; no roadmap phase)*

**Objective:** make the repo capable of hosting the work the other six cycles
describe. Today it is not: there is no Unity project, `symbols/index.json` is
empty because `Assets/_Game/Scripts/**/*.cs` matches nothing, and durable
knowledge nodes do not survive an ingest.

This cycle is not in the roadmap. It is the missing predecessor to Phase 1 —
the roadmap's Phase 1 assumes an iOS project scaffold exists as a *task
within* the phase, but the harness needs it to exist *before* the phase, or
every Phase 1 session starts with a dead symbol index. Full spec:
`docs/CYCLE0_BOOTSTRAP_SPEC.md`.

| Quadrant | Content |
|---|---|
| I | Objective above. Alternative considered: fold this into Phase 1. Rejected — it would leave R2/R3 unresolved during the highest-risk cycle, which is precisely backwards. |
| II | R2, R3. Resolution: scaffold + seeds layer. |
| III | Cells: `phase0/build_pipeline-unity-scaffold`, `phase0/knowledge_base-seeds-layer`, `phase0/ci-unity-test-workflow`. |
| IV | Gate: a trivial C# type under `Assets/_Game/Scripts/` appears in `symbols/index.json` after an ingest, and a seeded phase node survives that same ingest. |

**Exit criterion (mechanical):** commit one placeholder script, dispatch
`ingest.yml`, merge the index PR, and confirm both the symbol and the seed
node are present. If either is absent, Cycle 0 is not done.

### Cycle 1 — Constrained-physics flight feel *(roadmap Phase 1)*

| Quadrant | Content |
|---|---|
| I | "Jet flies under physics, feels right, movement never leaves the locked plane, joystick control responsive on-device." ADR-001 (locked axis = world-Z) must be `Accepted` before quadrant III. |
| II | **R1, exposure 20 — the highest in the project.** Spike `PlaneConstraint` first, on device, with nothing else built. If post-solve correction fights the arcade feel, the alternative (`ConfigurableJoint`) is evaluated here and the outcome recorded as an ADR. |
| III | Cells per `docs/PHASE1_TECHNICAL_SPEC.md` §1: player, physics, weapon (gun stub), build_pipeline. |
| IV | Automated half: locked-axis deviation test over a long run. Subjective half: on-device capture attached to the cycle review. Both required. |

Nothing downstream may start before this gate closes. Every later cycle tunes
against flight feel; tuning against a moving target is the most expensive
mistake available here.

### Cycle 2 — Combat core *(roadmap Phase 2)*

| Quadrant | Content |
|---|---|
| I | "One full engagement loop: gun kills air enemy, health bar animates, missile locks and destroys a ground target." ADR-002 (air auto-fire vs. ground-locked missiles) must be `Accepted`. |
| II | Low residual risk. Watch item: object-pool churn under sustained fire — measure, do not assume. |
| III | Cells per Phase 2 spec, including the stated retrofit (`PrimaryGunController` reads from `PlayerStatsRuntime`) **before** the cycle's other cells. A retrofit scheduled after dependent work is a merge conflict with extra steps. |
| IV | Damage/health/lock-on are all mechanically testable — this cycle should be close to fully automated in its verification. |

### Cycle 3 — Endless loop and scaling *(roadmap Phase 3)*

| Quadrant | Content |
|---|---|
| I | "A 5+ minute run is playable, power-ups visibly change behaviour, difficulty escalates and stays beatable." ADR-003 (power-up duration) and ADR-004 (coins-only earn) must be `Accepted`. |
| II | R5. Spike the bounded-DDA ceiling in isolation: simulate `PlayerPowerLevel` sweeps against `statMultiplier` and assert time-to-kill never exceeds the ceiling. This is a pure-function test — write it before the gameplay wiring. |
| III | Cells per Phase 3 spec. `CurrencyType`/`Wallet` is built here as a general service, per roadmap §5, so Cycle 5 does not require a rewrite. |
| IV | Re-score R7 with three cycles of real velocity data. This is the first honest point to check "full release ASAP" against reality. |

### Cycle 4 — 2-player P2P *(roadmap Phase 4)*

| Quadrant | Content |
|---|---|
| I | "Two physical iOS devices complete a co-op run with no visible enemy-health desync." ADR-005 (co-op, not PvP) must be `Accepted` — this one changes the whole cycle if wrong. |
| II | R4. Spike host-authoritative enemy state on two devices **behind `INetworkTransport`** before any gameplay code depends on GameKit. The abstraction is what makes the spike disposable. |
| III | Cells per Phase 4 spec. |
| IV | Desync is the gate. Two devices, one run, no divergence. |

### Cycle 5 — Economy and monetization *(roadmap Phase 5)*

| Quadrant | Content |
|---|---|
| I | "A sandbox IAP completes end-to-end and unlocks a store item." ADR-006 (cosmetic-only randomization) must be `Accepted`. |
| II | R6. Resolution is a design constraint, not code: odds-disclosure slot exists in the store UI from the first commit, used or not. Retrofitting it during App Store review is the expensive path. |
| III | Cells per Phase 5 spec. |
| IV | Sandbox purchase evidence attached. Compliance checklist reviewed against current App Store guidelines — re-read them at this point rather than trusting the roadmap's snapshot. |

### Cycle 6 — iOS hardening and analytics *(roadmap Phase 6)*

| Quadrant | Content |
|---|---|
| I | "Acceptable frame rate on the lowest supported device at low tier; analytics visible in Firebase." |
| II | Residual risk is now performance and submission mechanics. Profile before optimizing — the Burst decision is made from profiler data in this cycle, not speculatively, per the roadmap. |
| III | Cells per Phase 6 spec, including Privacy Manifests for every third-party SDK. |
| IV | The fps floor must be written down here as a number. "Acceptable" is not a gate. |

### Cycle 7+ — Live-ops *(roadmap Post / Upgrade)*

The spiral does not stop at ship; it changes input. Quadrant I objectives now
come from Cycle 6's analytics rather than from the roadmap, and quadrant II
risks are retention and balance rather than technical unknowns. Each live-ops
cycle gets its own technical spec in the existing format, written when it is
actually approached — speccing them now would be guessing.

---

## 6. Durable knowledge: the promotion step

**The gap this closes.** Every phase spec ends with a `knowledge_update`
pattern — a node tagged with its phase and symbols, which is what makes
later retrieval scopeable by phase instead of searching the whole KB. That
mechanic is the stated justification for cell-scoped branching in roadmap §9.

But `gateway/ingest.py:build_graph()` rebuilds `knowledge/graph.json`
wholesale from domains and chunk kinds and overwrites the file. A node added
at runtime through `knowledge_update` therefore survives only until the next
ingest — and `ingest.yml` runs on a nightly cron. Worse, while such a node is
committed, `enforce.yml`'s staleness check sees a graph a fresh ingest cannot
reproduce and fails, and the obvious way to make CI green again is to delete
the phase nodes.

So the two halves were connected by a mechanism that quietly destroys the
thing it was supposed to carry.

**The fix is a layer between them, not a merge of them.** Runtime and durable
knowledge stay separate systems with different lifetimes:

| | Runtime nodes | Durable nodes |
|---|---|---|
| Written by | `knowledge_update` through the gateway | Hand-authored in `knowledge/seeds.json` |
| Lifetime | Until the next ingest | Permanent — reproduced by every ingest |
| Purpose | Working state within a session | Phase/cell map that later retrieval scopes against |

`build_graph()` unions `knowledge/seeds.json` into the derived graph. Because
seeds are a *tracked input*, a fresh ingest reproduces them byte-identically,
so the staleness check keeps working exactly as designed — the guarantee is
preserved rather than loosened.

**The promotion step** is what makes this real, and it is inner-loop step 7:
when a cell merges, its runtime node is written into `knowledge/seeds.json`
in the same PR. A node that is never promoted is a node that will be gone by
tomorrow morning's cron.

---

## 7. Changing documentation without turning CI red

`gateway/ingest.py:_ingest_markdown()` chunks every file matching
`docs/*.md` plus `README.md`. Adding or editing one of those changes
`index/kb.index.json`, which `enforce.yml` then correctly reports as stale.
Rebuilding the index locally is forbidden (`ingest` and `index` are both in
`execution_policy.forbidden_locally`).

The supported sequence — worth stating explicitly, because getting it wrong
produces a red PR with no obvious remedy:

```bash
git push -u origin phase1/physics-plane-constraint
```

```bash
make ingest REF=phase1/physics-plane-constraint
```

`ingest.yml` rebuilds the KB index, graph, symbols and manifest, verifies
retrieval against the rebuilt KB, and opens `chore/ingest-<run_id>` **into
your branch** (its base is the dispatched ref, not `main`). Merge that PR
into your branch, and `enforce.yml` goes green. Then open the cell PR to
`main`.

Doc-only cycles follow the same path; there is no shortcut, and the nightly
cron is not a substitute — it runs against `main`, not your branch.

---

## 8. What this process does not cover

Stated plainly, in the spirit of `docs/ENFORCEMENT.md`:

- **It does not verify game feel.** Gate 1 of the exit checklist requires
  human-reviewed evidence. No CI check substitutes for playing the build.
- **It does not gate merges by itself.** The exit gates are a checklist, not
  a status check. Making `enforce.yml` a required check is a GitHub branch
  protection setting, not a file in this repo.
- **It does not schedule.** There are no dates here, deliberately: the
  roadmap's own risk register flags "ASAP vs. full scope" as unresolved, and
  inventing dates before Cycle 1 velocity is known would be fiction. Dates
  belong in quadrant IV of Cycle 1, once one cycle of real data exists.
- **It assumes the agent talks to the repo through the gateway.** If an agent
  also holds a raw shell, the retrieval discipline in §3 is a convention for
  that agent, not an enforced property — the same boundary
  `docs/ENFORCEMENT.md` already draws.
