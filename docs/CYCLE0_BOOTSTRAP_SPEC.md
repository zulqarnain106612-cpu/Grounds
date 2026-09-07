# JetFighter — Cycle 0 Technical Spec (Harness Bootstrap)

Scope: make the repo able to host the work Phases 1–6 describe. Same format
as the other phase specs. Decided in ADR-007.

Cycle 0 is not a new phase of the game. It **executes the roadmap's existing
`phase1/build-ios-scaffold` branch first**, then adds the two harness pieces
no phase spec covers. Nothing here changes any Phase 1–6 content.

---

## 0. Why this cycle exists

Three things are true of the repo today, all verifiable:

1. `config/agent.config.json` sets
   `source_globs: ["Assets/_Game/Scripts/**/*.cs"]`, and there is no
   `Assets/` directory. `symbols/index.json` therefore contains zero
   symbols, so `symbol_lookup` returns nothing for every query. The README
   states this plainly; the gap is that no cycle owns fixing it.
2. Every phase spec ends with a `knowledge_update` node pattern, but
   `gateway/ingest.py:build_graph()` overwrites `knowledge/graph.json`
   wholesale on every ingest — and `ingest.yml` runs nightly on cron. Those
   nodes do not survive (ADR-008).
3. The repo has a Python test suite and five workflows, none of which can
   run a C# test. Every phase spec's acceptance criteria assume C# tests
   exist. There is nowhere for them to run.

Phase 1 is the highest-risk cycle in the project (R1, exposure 20). Entering
it with a dead symbol index, disappearing knowledge nodes and no C# test
runner means running the riskiest work without any of the machinery built to
de-risk it.

---

## 1. Folder Layout

Cycle 0 creates the scaffold that `docs/PHASE1_TECHNICAL_SPEC.md` §1 assumes:

```
Assets/_Game/Scripts/
  Build/
    QualityTierManager.cs        # stub only; filled in Cycle 1
Assets/_Game/Tests/
  EditMode/
    ScaffoldSmokeTest.cs         # proves the test runner works, nothing more
knowledge/
  seeds.json                     # new: durable, tracked knowledge nodes
.github/workflows/
  unity-test.yml                 # new: runs EditMode/PlayMode tests
```

Only `Assets/_Game/Scripts/**/*.cs` is matched by `source_globs`, so the test
tree under `Assets/_Game/Tests/` is deliberately outside the symbol index —
tests are not project symbols and should not pollute retrieval.

## 2. Component List

### `knowledge/seeds.json` — tracked data

| field | type | notes |
|---|---|---|
| `version` | string | matches the schema version in use |
| `note` | string | states that this file *is* hand-authored, unlike every other file under `knowledge/` |
| `nodes` | array | objects shaped like `KnowledgeNode` minus `op` — `id`, `type`, `label`, `tags`, `phase`, `symbols` |
| `edges` | array | `{from, to, kind}` |

Sorted by `id` on write, so a fresh ingest reproduces the file
byte-identically and `enforce.yml`'s staleness check keeps working unchanged.

Validated by `gateway/validate_repo.py` on every push, using the same
`gateway/ingest.py:parse_seeds()` the ingest itself runs: a node with no
usable `id`, a duplicate id, or an edge whose endpoint names no node fails
the PR. It is the only hand-authored file under `knowledge/`, so it is the
only one that can be wrong, and a broken entry that merely disappeared would
be the exact failure ADR-008 exists to prevent.

### `gateway/ingest.py:build_graph()` — modified

Unions `knowledge/seeds.json` into the derived graph after the domain and
chunk-kind nodes, then sorts. Seed nodes win on id collision — a hand-authored
node is the more specific statement. If `seeds.json` is absent or empty the
function behaves exactly as before, so the change cannot break an existing
checkout.

**What is deliberately not changed:** `kb_store.apply_knowledge_op()` still
writes runtime nodes straight into `graph.json`, and those are still
transient. That is the intended split (`docs/SDLC_SPIRAL.md` §6) — runtime
and durable knowledge are different systems with different lifetimes, joined
by the promotion step, not merged.

### `.github/workflows/unity-test.yml` — new workflow

Runs Unity EditMode and PlayMode tests on push and PR. Two things it must do
that are easy to get wrong:

- **Fail on zero tests.** A Unity test job that finds no tests exits green,
  which is worse than red — it reports success for an unverified build. Assert
  a non-zero test count explicitly.
- **Not block on a licence it does not have.** Unity CI needs a licence
  secret. Until one is configured the job must fail loudly with a clear
  message, never skip silently.

### `Assets/_Game/Tests/EditMode/ScaffoldSmokeTest.cs`

One trivially passing test. Its only job is to prove the runner executes and
reports. It is deleted once Cycle 1 has real tests.

### `Assets/_Game/Scripts/Build/QualityTierManager.cs`

Empty stub with the type declared, per Phase 1 spec branch 1. It exists in
Cycle 0 for one reason: to give the symbol scanner a real C# type to find, so
exit criterion 1 below is mechanically checkable.

## 3. Branch Sequence

1. `phase1/build-ios-scaffold` — the roadmap's own first branch, run here:
   Unity project init on **Unity 6 LTS** (ADR-009), iOS Player Settings
   (Bundle ID, minimum iOS version, Metal, IL2CPP), the `Assets/_Game/`
   folder layout, and the `QualityTierManager` stub.
2. `phase0/knowledge_base-seeds-layer` — `knowledge/seeds.json`, the
   `build_graph()` union, and a Python test proving a seed node survives a
   rebuild.
3. `phase0/ci-unity-test-workflow` — `unity-test.yml` and the smoke test.

Order matters: branch 1 must land first, or branch 3 has no project to test
and branch 2 has no symbols to reference.

## 4. Acceptance Criteria

| Branch | Criteria |
|---|---|
| build-ios-scaffold | After dispatching `ingest.yml` and merging the index PR, `symbols/index.json` contains `QualityTierManager`. A `symbol_lookup` request for it through the gateway returns a hit. |
| knowledge_base-seeds-layer | A node present in `seeds.json` is still present in `knowledge/graph.json` after a full ingest, **and** `enforce.yml`'s staleness check passes with that node committed. Both halves are required — surviving ingest while turning CI red is not a fix. |
| ci-unity-test-workflow | The workflow runs the smoke test and reports it. Deliberately verify the failure paths too: a failing test turns the job red, and zero discovered tests turns the job red. |

**Cycle gate (`docs/SDLC_SPIRAL.md` quadrant IV):** all three criteria above,
plus `docs/TRACEABILITY.md`'s Cycle 0 rows ticked with their seed nodes
present.

## 5. `knowledge_update` Pattern

`meta.phase` is constrained by the schema to
`["1","2","3","4","5","6","post","upgrade"]` — **there is no `"0"`**. Cycle 0
sessions therefore declare `meta.phase: "1"`, which is accurate: Cycle 0's
work is roadmap Phase 1's `build_pipeline` cell plus the harness it depends
on. See ADR-011 for why the enum is left alone rather than extended.

The `phase` field on a `KnowledgeNode` is a free-form string array and is not
enum-constrained, so seed nodes may carry `"0"` for scoping:

```json
{"op":"add_node","id":"phase0_knowledge_base_seeds_layer","type":"system","label":"Durable Knowledge Seed Layer","tags":["knowledge_base","phase0","harness"],"phase":["0"],"symbols":[]}
```

Per `docs/SDLC_SPIRAL.md` §6, emitting this through the gateway is not
enough — promote it into `knowledge/seeds.json` in the same PR, or it is
gone by the next nightly ingest.
