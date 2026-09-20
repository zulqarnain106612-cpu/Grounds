"""The schema's observable surface, extracted without importing pytest.

Split out of tests/test_schema_contract.py for one practical reason: the
golden file has to be regeneratable on a machine that has no test runner
installed. config/agent.config.json forbids running the suite locally, so a
`--update-golden` path that needed pytest on PATH would be unusable exactly
where it is wanted.

    python -m tests.contract_surface --update-golden
"""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SCHEMA_PATH = ROOT / "schema" / "agent.schema.json"
GOLDEN_PATH = Path(__file__).resolve().parent / "golden" / "schema_contract.json"

# Keys that describe what a field accepts. Descriptions are deliberately not
# among them: prose is not contract, and including it would churn the golden
# file on every doc edit until nobody read the diff any more.
_CONTRACT_KEYS = (
    "type", "enum", "const", "required", "additionalProperties",
    "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum",
    "minLength", "maxLength", "minItems", "maxItems", "format",
    "pattern", "default", "$ref",
)


def load_schema() -> dict:
    return json.loads(SCHEMA_PATH.read_text())


def object_nodes(node, path="$"):
    """Every object-typed subschema in the document, with its JSON path."""
    if isinstance(node, dict):
        if node.get("type") == "object" and "properties" in node:
            yield path, node
        for key, value in node.items():
            yield from object_nodes(value, f"{path}.{key}")
    elif isinstance(node, list):
        for i, value in enumerate(node):
            yield from object_nodes(value, f"{path}[{i}]")


def _summarise(node: dict) -> dict:
    out = {k: node[k] for k in _CONTRACT_KEYS if k in node}
    if "properties" in node:
        out["properties"] = {k: _summarise(v) for k, v in sorted(node["properties"].items())}
    if isinstance(node.get("items"), dict):
        out["items"] = _summarise(node["items"])
    if "allOf" in node:
        out["allOf"] = node["allOf"]
    return out


def surface(schema: dict | None = None) -> dict:
    schema = schema or load_schema()
    intent = schema["properties"]["intent"]["properties"]
    return {
        "actions": sorted(intent["action"]["enum"]),
        "domains": sorted(intent["domain"]["enum"]),
        "definitions": {n: _summarise(b) for n, b in sorted(schema["definitions"].items())},
        "payload_fields": {
            k: _summarise(v)
            for k, v in sorted(schema["properties"]["payload"]["properties"].items())
        },
        "top_level": {
            "required": schema["required"],
            "additionalProperties": schema["additionalProperties"],
        },
    }


def uncapped_strings(schema: dict | None = None) -> set[str]:
    """Free-form string fields with no length bound. Enum/const/pattern/format
    fields are exempt -- their length is bounded by what they accept."""
    schema = schema or load_schema()
    found = set()
    for path, node in object_nodes(schema):
        for field, spec in node.get("properties", {}).items():
            if spec.get("type") != "string":
                continue
            if any(k in spec for k in ("enum", "const", "maxLength", "format", "pattern")):
                continue
            found.add(f"{path}.{field}")
    return found


def update_golden() -> Path:
    GOLDEN_PATH.parent.mkdir(parents=True, exist_ok=True)
    GOLDEN_PATH.write_text(json.dumps(surface(), indent=2, sort_keys=True) + "\n")
    return GOLDEN_PATH


if __name__ == "__main__":  # pragma: no cover
    import sys
    if "--update-golden" in sys.argv:
        print(f"wrote {update_golden()}")
    else:
        print(json.dumps(surface(), indent=2, sort_keys=True))
