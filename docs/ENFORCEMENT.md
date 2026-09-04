# Enforcement: what is actually real here

This document exists because the previous version of this bundle declared
policies in JSON (`"direct_file_read": "BLOCKED"`) with no code enforcing
them. That is fixed. This section is deliberately specific about what is
and isn't guaranteed, so nothing here is taken on faith.

## Guarantees enforced by code (not by convention)

| Guarantee | Where it's enforced | How to verify |
|---|---|---|
| No request/response bypasses the schema | `gateway/gateway.py:handle()` validates **both** the inbound request and the outbound response against `agent.schema.json` before returning anything | `tests/test_gateway.py::test_schema_violation_is_rejected_not_executed` |
| No action exists to read raw file/code content | `FileOp.op` enum = `write/append/delete/stat` only; no handler in `gateway/handlers.py` ever reads a file and returns its bytes/text | `tests/test_enforcement.py::test_fileop_write_then_delete_round_trip_no_read_op_exists` |
| `command_exec` output is always capped | `enforcement.truncate_lines()` runs on every `command_exec`/`build` response regardless of what flags were requested | `tests/test_enforcement.py::test_command_output_is_truncated_server_side_even_if_output_lines_omitted` |
| Known-dangerous commands (`cat`, `less`, huge `tail`/`head`, `grep` on source, etc.) are blocked before `subprocess.run` is ever called | `enforcement.assert_command_allowed()`, patterns in `tools/patterns.json` | `tests/test_enforcement.py::test_forbidden_cat_command_is_blocked_not_executed` |
| `log_query` never sees a raw `.log` file | `log_guard.refresh_failures_cache()` is the *only* function that opens `logs/*.log`; `log_query` reads `logs/failures.cache.json`, capped to `max_log_tail_lines` | `tests/test_enforcement.py::test_log_query_never_exceeds_hard_cap...` |
| Retrieved KB content is capped | `RetrievedChunk.content` schema `maxLength: 2000`, enforced again in `kb_store.retrieve()` | `tests/test_enforcement.py::test_retrieved_chunk_content_never_exceeds_schema_cap` |
| Symbol data never carries source text | `gateway/symbol_scanner.py` reads files only to compute `{name, kind, file, line, namespace}`; the read text is never put on a returned object | code review of `symbol_scanner.scan_file` — it has no field/branch that stores line content |
| Every action in the schema has a real handler (no silent no-ops) | `gateway/validate_repo.py` step 5 fails the build if any enum value in `intent.action` isn't a key in `handlers.DISPATCH` | CI job `agent-schema-enforcement` |
| Symbol index can't silently go stale | CI step "Rebuild symbol index and fail if stale" re-runs the scanner and diffs the committed file | `.github/workflows/enforce.yml` |

## Two enforcement layers, on purpose

1. **Local git hooks** (`.githooks/pre-commit`, `.githooks/pre-push`) — fast
   feedback, block bad commits before they happen.
2. **CI** (`.github/workflows/enforce.yml`) — the real backstop. Local
   hooks can always be skipped with `git commit --no-verify`, and hooks
   only activate after `scripts/install.sh` has been run once (git has no
   "hooks run automatically on clone" mechanism — this is a git
   limitation, stated plainly rather than glossed over). CI cannot be
   skipped by a contributor; if you want it to also block merges, turn on
   **branch protection → required status check: `agent-schema-enforcement`**
   in your repo settings (this bundle can't do that for you — it's a
   GitHub repo setting, not a file).

## What this does NOT do

- It does not sandbox an LLM's tool-calling layer (e.g. Claude Code's own
  `bash`/`view` tools) at the OS level. If an agent is given raw shell/file
  access outside this gateway, this schema can't stop it — the contract
  only binds callers that go through `python -m gateway.cli` /
  `gateway.gateway.handle()`. If your workflow is "the agent talks to my
  repo only via this CLI," the guarantee holds end-to-end. If the agent
  also has an unrelated raw shell tool available to it, that tool is
  outside this system's authority — document that boundary in your agent's
  own operating instructions.
- `semantic` retrieval currently falls back to keyword scoring (see
  `retrieval/strategies.json`) until you wire in a real embedding
  provider in `gateway/kb_store.py:retrieve()`. It is not silently
  returning raw content instead — it's a documented, honest fallback.
- The forbidden-command regex list (`tools/patterns.json`) is
  defense-in-depth, not the primary control. It can, in principle, be
  evaded by a sufficiently creative shell one-liner. The primary control
  is that raw output is *always* truncated and no "read" action exists —
  even a command that evades the blocklist can't return more than
  `max_command_output_lines`.

## Extending safely

See `docs/EXTENDING.md`.
