# JetFighter Agent Gateway

A schema-enforced boundary between an AI coding agent and this repo. The
agent talks JSON in, JSON out, through `gateway/gateway.py`; there is no
code path that hands it raw file or log content. See `docs/ENFORCEMENT.md`
for exactly what's guaranteed and how it's verified.

Two rules define this repo:

1. **Every interaction is a schema-validated JSON request.** Anything else is
   rejected at the boundary.
2. **Nothing heavy runs on your machine.** Build, test, review, ingest,
   manifest, index and polling all execute on GitHub Actions. Dispatching one
   never blocks.

## Setup (once, right after cloning — repo starts empty)

```bash
bash scripts/install.sh
```

This installs `jsonschema`/`pytest`, activates the git hooks
(`core.hooksPath=.githooks`) and runs one `validate_repo` check to confirm the
clone is sound.

The hooks are deliberately **fast**: `pre-commit` only parses staged JSON and
Python for syntax errors, and `pre-push` runs nothing at all. Validation, the
test suite and index rebuilds are CI's job (see *Execution policy* below), so
a commit never waits on them.

## Execution policy

`config/agent.config.json → execution_policy` marks these operations
`FORBIDDEN` locally: `build`, `test`, `review`, `ingest`, `manifest`, `poll`,
`index`. They run in GitHub Actions instead.

This is enforced by the shape of the contract, not by a policy flag:

- `CIOp.remote_only` is `const: true` — a request cannot claim a local run.
- `CIOp.wait` is `const: false` — a request cannot ask to block on a run.
- `gateway/handlers.py:_in_ci()` is the single gate. Inside a runner
  (`GITHUB_ACTIONS=true` or `JFA_CI=1`) the `ingest`/`manifest` actions do the
  real work; anywhere else they dispatch the workflow and return immediately.

| Workflow | Trigger | What it does |
| --- | --- | --- |
| `enforce.yml` | push, PR, dispatch | Validates schema/config/examples/handler coverage, runs pytest, rebuilds every index and fails if one is stale |
| `ingest.yml` | dispatch, nightly cron | Rebuilds symbols + KB + graph + manifest, verifies retrieval, then opens a PR with the refreshed indexes. The cron replaces any local polling loop |
| `manifest.yml` | dispatch, push to main | Rebuilds `index/manifest.json` and verifies every sha256 against the working tree; on dispatch it opens a PR with the result |
| `review.yml` | PR, dispatch | Structural review: action/example/handler parity, config paths resolve, no handler blocks on CI |
| `retrieval-verify.yml` | push, PR, dispatch | Live retrieval probes through the gateway, all-actions round-trip, and the no-raw-file-read guarantee |

Staleness checks compare **content keys only**. Every generated index carries a
moving `updated_at`/`built_at`, so diffing whole files would fail on every run.

`main` is protected by a ruleset requiring changes to arrive via pull request,
so the workflows that regenerate indexes **open a PR** (`chore/ingest-<run_id>`,
`chore/manifest-<run_id>`) instead of pushing to the base ref. Review and merge
that PR to publish the refreshed indexes.

## Talking to the gateway

```bash
echo '{
  "meta": {"schema_version":"1.1.0","session_id":"550e8400-e29b-41d4-a716-446655440000","tick":1,"phase":"1","timestamp_utc":"2026-01-01T00:00:00Z"},
  "intent": {"action":"symbol_lookup","domain":"player","priority":5},
  "payload": {"data": null, "context": {"strategy":"symbol","scope":"symbol_table","target":"PlayerController"}}
}' | python3 -m gateway.cli
```

All 21 actions in the schema have a worked example in `schema/examples.json`
and a handler in `gateway/handlers.py`. `gateway/validate_repo.py` fails the
build unless that stays true, so adding an action means touching the schema,
the examples and the handlers together.

## Day-to-day commands

Each target dispatches a workflow and returns straight away — it does not wait
for the run.

```bash
make validate          # dispatch enforce.yml
make test              # dispatch enforce.yml
make ingest            # dispatch ingest.yml  (rebuild + commit indexes)
make manifest          # dispatch manifest.yml
make review            # dispatch review.yml
make retrieval-verify  # dispatch retrieval-verify.yml
make ci-status         # gh run list --limit 10
make daemons-status
```

Override the branch with `make ingest REF=my-branch`.

## Retrieval

`gateway/ingest.py` builds `index/kb.index.json` from the docs, the schema
itself, the retrieval/daemon configs and the symbol index, then derives
`knowledge/graph.json` from those chunks. It is deterministic — chunks sort by
id and only the timestamp moves — so CI can diff it for staleness.

Retrieval serves bounded summaries, never file bytes. `gateway/verify_retrieval.py`
drives real probes through the full gateway and fails if a populated KB returns
nothing, so an empty index cannot pass silently.

Note: `symbols/index.json` covers `source_globs`
(`Assets/_Game/Scripts/**/*.cs`) only, so it holds game code and nothing
else — editor tooling under `Assets/_Game/Editor/` is deliberately outside
it. The Unity scaffold that first populated it landed with
`phase1/build-ios-scaffold`; the KB is populated from docs and schema
independently.

## Layout

```
schema/       agent.schema.json (source of truth), examples.json
gateway/      the enforcement engine (see docs/ENFORCEMENT.md)
  ingest.py           builds kb.index.json + graph.json
  manifest.py         builds index/manifest.json (path/size/sha256)
  verify_retrieval.py end-to-end retrieval probes, CI-facing
config/       agent.config.json — caps + execution_policy
retrieval/    strategy definitions used by gateway/kb_store.py
tools/        forbidden-command patterns used by gateway/enforcement.py
daemons/      registry + runtime state (pid files, context stacks)
knowledge/    graph.json — generated by the ingest workflow
symbols/      index.json — generated by the symbol scanner
index/        kb.index.json, manifest.json — generated by the ingest workflow
Assets/_Game/ Unity project — Scripts/ (indexed), Editor/ (tooling, not indexed)
ProjectSettings/, Packages/  Unity project files (Unity 6 LTS, ADR-009)
tests/        pytest suite backing every claim in docs/ENFORCEMENT.md
docs/         SCHEMA.md (generated), ENFORCEMENT.md, EXTENDING.md
.githooks/    pre-commit (fast syntax checks), pre-push (no-op)
.github/      the five workflows above
```

Files under `knowledge/`, `symbols/` and `index/` are generated — never
hand-edit them. Run `make ingest` and merge the PR that CI opens.

## Docs

- `docs/SCHEMA.md` — full field reference, generated from the schema
  itself (`python3 -m gateway.docgen`), so it cannot drift.
- `docs/ENFORCEMENT.md` — what's really enforced, with a test proving each
  claim, and an honest list of what this system does *not* cover.
- `docs/EXTENDING.md` — how to add an action/domain/payload type without
  breaking the guarantees.
