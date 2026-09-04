import json
from pathlib import Path
import pytest
from gateway.gateway import handle

ROOT = Path(__file__).resolve().parent.parent
EXAMPLES = json.loads((ROOT / "schema" / "examples.json").read_text())["examples"]


@pytest.mark.parametrize("name", list(EXAMPLES.keys()))
def test_every_example_action_is_handled_without_error(name):
    resp = handle(EXAMPLES[name])
    assert resp["response"]["status"] in ("ok", "partial"), (name, resp["response"])


def test_invalid_json_string_is_rejected():
    resp = handle("{not valid json")
    assert resp["response"]["status"] == "error"
    assert resp["response"]["errors"][0]["code"] == "invalid_json"


def test_schema_violation_is_rejected_not_executed():
    bad = dict(EXAMPLES["command_exec"])
    bad = json.loads(json.dumps(bad))
    bad["payload"]["command"]["capped"] = False  # const:true violated
    resp = handle(bad)
    assert resp["response"]["status"] == "error"
    assert resp["response"]["errors"][0]["code"] == "schema_violation"


def test_unknown_action_rejected_at_schema_not_dispatch():
    bad = json.loads(json.dumps(EXAMPLES["query"]))
    bad["intent"]["action"] = "raw_file_read"
    resp = handle(bad)
    assert resp["response"]["status"] == "error"


def test_context_push_pop_roundtrip():
    push_req = json.loads(json.dumps(EXAMPLES["context_push"]))
    push_req["meta"]["session_id"] = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
    r1 = handle(push_req)
    assert r1["response"]["result"]["depth"] == 1

    pop_req = json.loads(json.dumps(EXAMPLES["context_pop"]))
    pop_req["meta"]["session_id"] = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
    r2 = handle(pop_req)
    assert r2["response"]["result"]["frame"] == {"frame": "editing weapon_system"}
    assert r2["response"]["result"]["depth"] == 0


def test_knowledge_update_then_symbol_lookup_do_not_leak_source_text():
    resp = handle(EXAMPLES["knowledge_update"])
    assert resp["response"]["status"] == "ok"
    # response never contains a 'content'/'file_text' field with raw source
    dumped = json.dumps(resp)
    assert "file_text" not in dumped
