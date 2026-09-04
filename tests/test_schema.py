import json
from pathlib import Path
from gateway import schema_guard

ROOT = Path(__file__).resolve().parent.parent


def test_schema_is_valid_draft7():
    schema_guard.load_schema()  # raises on invalid


def test_all_examples_validate():
    examples = json.loads((ROOT / "schema" / "examples.json").read_text())["examples"]
    for name, ex in examples.items():
        schema_guard.validate(ex)  # raises with details on failure


def test_all_actions_have_an_example():
    schema = schema_guard.load_schema()
    actions = set(schema["properties"]["intent"]["properties"]["action"]["enum"])
    examples = json.loads((ROOT / "schema" / "examples.json").read_text())["examples"]
    covered = {ex["intent"]["action"] for ex in examples.values()}
    assert actions == covered


def test_fileop_has_no_read_capability():
    schema = schema_guard.load_schema()
    ops = schema["definitions"]["FileOp"]["properties"]["op"]["enum"]
    assert "read" not in ops


def test_additional_properties_false_everywhere_top_level():
    schema = schema_guard.load_schema()
    assert schema["additionalProperties"] is False
    for key in ("meta", "intent", "payload", "response"):
        assert schema["properties"][key]["additionalProperties"] is False


def test_rejects_unknown_top_level_field():
    bad = {
        "meta": {"schema_version": "1.1.0", "session_id": "550e8400-e29b-41d4-a716-446655440000",
                  "tick": 0, "phase": "1", "timestamp_utc": "2026-01-01T00:00:00Z"},
        "intent": {"action": "query", "domain": "retrieval", "priority": 1},
        "payload": {"data": None},
        "hacker_field": "should not be allowed",
    }
    assert not schema_guard.is_valid(bad)


def test_rejects_bad_action_enum():
    bad = {
        "meta": {"schema_version": "1.1.0", "session_id": "550e8400-e29b-41d4-a716-446655440000",
                  "tick": 0, "phase": "1", "timestamp_utc": "2026-01-01T00:00:00Z"},
        "intent": {"action": "read_raw_file", "domain": "asset", "priority": 1},
        "payload": {"data": None},
    }
    assert not schema_guard.is_valid(bad)
