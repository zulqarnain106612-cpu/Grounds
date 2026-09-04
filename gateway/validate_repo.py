from __future__ import annotations
import json
import sys
from pathlib import Path

from . import schema_guard

ROOT = Path(__file__).resolve().parent.parent

JSON_FILES = [
    "schema/agent.schema.json",
    "schema/examples.json",
    "config/agent.config.json",
    "retrieval/strategies.json",
    "daemons/registry.json",
    "tools/patterns.json",
    "knowledge/graph.json",
    "symbols/index.json",
    "index/kb.index.json",
]


def main() -> int:
    failures: list[str] = []

    # 1. every tracked JSON file must parse
    for rel in JSON_FILES:
        path = ROOT / rel
        if not path.exists():
            failures.append(f"missing required file: {rel}")
            continue
        try:
            json.loads(path.read_text())
        except json.JSONDecodeError as e:
            failures.append(f"{rel}: invalid JSON ({e})")

    if failures:
        _report(failures)
        return 1

    # 2. schema itself must be a valid Draft-07 schema
    try:
        schema = schema_guard.load_schema()
    except schema_guard.SchemaLoadError as e:
        _report([str(e)])
        return 1

    # 3. every action in the schema enum must have an example
    examples = json.loads((ROOT / "schema" / "examples.json").read_text())["examples"]
    actions = set(schema["properties"]["intent"]["properties"]["action"]["enum"])
    covered = {ex["intent"]["action"] for ex in examples.values()}
    missing = actions - covered
    if missing:
        failures.append(f"actions missing from schema/examples.json: {sorted(missing)}")

    # 4. every example must validate against the schema
    for name, ex in examples.items():
        try:
            schema_guard.validate(ex)
        except schema_guard.RequestValidationError as e:
            failures.append(f"example '{name}' fails schema validation: {e.errors}")

    # 5. every action in the schema must have a registered handler
    from . import handlers
    unhandled = actions - set(handlers.DISPATCH.keys())
    if unhandled:
        failures.append(f"actions with no handler in gateway/handlers.py: {sorted(unhandled)}")

    _report(failures)
    return 1 if failures else 0


def _report(failures: list[str]) -> None:
    if not failures:
        print("[PASS] repo validation: schema valid, all actions covered and handled")
        return
    print("[FAIL] repo validation:")
    for f in failures:
        print(f"  - {f}")


if __name__ == "__main__":
    sys.exit(main())
