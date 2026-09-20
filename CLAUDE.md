# Working in this repo

## Process

Development follows `docs/SDLC_SPIRAL.md` — a Boehm spiral, one cycle per
roadmap phase, one branch per phase×domain cell. Before starting a cell,
read its row in `docs/TRACEABILITY.md`: it names the ADR you must not
violate and the criterion that closes the cell.

- Branch names: `phase<N>/<domain>-<slug>`. Squash-merge, one commit per cell.
- A cell's PR must also add its knowledge node to `knowledge/seeds.json` and
  tick its `docs/TRACEABILITY.md` row. A node added only at runtime through
  `knowledge_update` is erased by the next ingest — see ADR-008.
- Decisions go in `docs/DECISIONS.md`, not in commit messages.

## IMPORTANT: nothing heavy runs locally

`config/agent.config.json → execution_policy` forbids `build`, `test`,
`review`, `ingest`, `manifest`, `poll` and `index` locally. Dispatch the
workflow and move on; never block on a run.

```bash
make validate   # or: make test / make review / make ingest / make manifest
```

```bash
make ci-status
```

Do not run `pytest`, `python -m gateway.ingest`, `python -m gateway.manifest`
or `python -m gateway.symbol_scanner` on this machine.

## Editing docs turns CI red until you re-ingest

`gateway/ingest.py` chunks `docs/*.md` and `README.md` into
`index/kb.index.json`, and `enforce.yml` fails when that index is stale.
After pushing a branch that touches those files:

```bash
make ingest REF=<your-branch>
```

That opens `chore/ingest-<run_id>` **into your branch**. Merge it, and
`enforce.yml` goes green. Full explanation in `docs/SDLC_SPIRAL.md` §7.

## Never hand-edit generated files

`index/kb.index.json`, `index/manifest.json`, `knowledge/graph.json`,
`symbols/index.json`. `knowledge/seeds.json` is the exception — it is
hand-authored input that ingest merges in.

## Adding a schema action

Schema, `schema/examples.json` and `gateway/handlers.py` must move together
or `validate_repo` fails the build. Steps: `docs/EXTENDING.md`.

## Working agreement

- **Act, don't ask.** Finish the cell, commit, push, open the PR, dispatch
  `make ingest` when `docs/` or `README.md` changed. No "shall I push?",
  no "that's your call". Only genuinely destructive actions (force-push to
  `main`, history rewrites, deletions) are worth stopping for.
- **Own the outcome.** Whoever made the change is accountable for it. "I
  forgot" / "that wasn't me" is not a report.
- **Keep chat short.** State the action and the result. Long explanations
  belong in `docs/`, commit messages and PR bodies, not in the terminal.

## IMPORTANT: read CI from the PR's comments, never from run logs

`.github/workflows/pr-status-comment.yml` posts a comment on every PR each
time a workflow finishes, carrying the exact error annotations that made it
red. **That comment is the source of truth for CI state.**

- Always work against a specific PR number. Never a repo-wide run listing.
- Read the **last** `pr-status-comment` on the PR first. It already contains
  the failure text, so no log fetch is needed in the common case.
- Only if that comment is genuinely insufficient, fall back to
  `make ci-logs PR=<n>` - the 2-line probe first, then the *exact* number of
  lines the probe justified. Never a round number, never "to be safe", never
  a full log.
- Waiting for a merge means re-reading that comment after a sensible
  interval. No watching a run, no polling loop, no live monitoring.

```bash
gh pr view <n> --json comments --jq '.comments[-1].body'
```

## IMPORTANT: CI status and logs are always scoped to one PR

Never list runs repo-wide, and never pull a full log.

```bash
make ci-status PR=<n>
```

Logs are **probe first, then exact**. With no `LINES` you get the last 2
`##[error]` annotations plus `error_line`/`total_lines`. Those name the failure
class and its position, which is what tells you how many lines you actually
need:

```bash
make ci-logs PR=<n>
```

Then ask for precisely that many — not a round number, not "to be safe".
`OFFSET` skips lines from the end when the probe shows the cause sits above the
tail (`OFFSET = total_lines - error_line`):

```bash
make ci-logs PR=<n> LINES=12 OFFSET=17
```

`gh run list`, `gh run view` and `gh run watch` are denied in
`.claude/settings.json`, and a `PreToolUse` hook
(`.claude/hooks/guard_ci_reads.py`) blocks them again along with
`gh api .../actions/runs/...`. The hook exists because permission rules match
a command prefix and cannot constrain `gh api` arguments; a hook sees the whole
command string, so piped and command-substituted forms are caught too.

Through the gateway, `ci_status`/`ci_logs` require
`ci_op.pr`; omitting `log_tail_lines` returns the 2-line probe, and any named
count is capped at 50 server-side.

## Verification

Every cell ships something that returns pass/fail without a human looking at
it. Where acceptance is subjective (flight feel), split it: automate the
objective half and attach a device capture for the rest. Show CI evidence
rather than asserting success.
