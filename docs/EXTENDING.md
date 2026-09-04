# Extending the schema/gateway safely

`gateway/validate_repo.py` fails the build if any of these steps is
skipped, so it's hard to add an unenforced action by accident.

## Add a new action

1. Add the string to `schema/agent.schema.json` →
   `properties.intent.properties.action.enum`.
2. Add one example under `schema/examples.json` → `examples.<action_name>`
   that validates against the schema.
3. Implement a handler `h_<action_name>(req) -> dict` in
   `gateway/handlers.py` and register it in `DISPATCH`.
4. Add a test in `tests/test_gateway.py` (the parametrized
   `test_every_example_action_is_handled_without_error` picks new examples
   up automatically — but add a dedicated test if the action needs
   specific enforcement checks, following the pattern in
   `tests/test_enforcement.py`).
5. Run `make validate && make test`.
6. Regenerate docs: `python3 -m gateway.docgen` (also run automatically by
   CI as a diff check — add it to `.githooks/pre-commit` if you want local
   enforcement too).

## Add a new domain

Add the string to `schema/agent.schema.json` →
`properties.intent.properties.domain.enum`. No handler changes required —
domain is informational/routing metadata, not a dispatch key.

## Add a new payload sub-object (like `FileOp`, `LogOp`, ...)

1. Add a definition under `schema.definitions.<Name>`, with
   `"additionalProperties": false` and an explicit `required` list — this
   is what makes it possible to reason about what an action *cannot* do,
   not just what it can.
2. Reference it from `payload.properties.<field>` with `$ref`.
3. If it introduces a new capability that could leak raw content (a new
   "read"-like op, for example), it must ship with:
   - a hard cap (maxLength/maxItems/max_tokens) in the schema itself, and
   - a test in `tests/test_enforcement.py` proving the cap is enforced
     server-side even when the caller omits/inflates the requested limit.

## Do NOT

- Add a `"read"` value to `FileOp.op`, or any handler that returns full
  file/log content, without updating `docs/ENFORCEMENT.md`'s guarantee
  table and getting a second reviewer — that table is the thing this
  whole bundle exists to keep true.
- Loosen `additionalProperties` to `true` anywhere. It is what makes
  "unknown field → rejected" a guarantee instead of a hope.
