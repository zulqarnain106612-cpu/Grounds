"""Coverage for the four entrypoint modules no other test imports.

cli, docgen, validate_repo and verify_retrieval sat at 0% line coverage: they
are only ever invoked as `python -m gateway.<name>` by a workflow, so nothing
in the suite exercised them. A module at 0% is the one place a regression can
land completely unobserved, and three of these four are the modules CI itself
depends on to decide whether a build passes.
"""
from __future__ import annotations

import io
import json

import pytest

from gateway import cli, docgen, gateway, handlers, schema_guard, validate_repo, verify_retrieval

REAL_ROOT = validate_repo.ROOT


def _example(name: str = "symbol_lookup") -> dict:
    examples = json.loads((REAL_ROOT / "schema" / "examples.json").read_text())["examples"]
    return examples[name]


# --------------------------------------------------------------------------
# gateway/cli.py
# --------------------------------------------------------------------------

def test_cli_reads_a_request_from_stdin(monkeypatch, capsys):
    monkeypatch.setattr("sys.argv", ["gateway.cli"])
    monkeypatch.setattr("sys.stdin", io.StringIO(json.dumps(_example())))

    assert cli.main() == 0

    printed = json.loads(capsys.readouterr().out)
    assert printed["response"]["status"] == "ok"


def test_cli_reads_a_request_from_a_file_argument(monkeypatch, capsys, tmp_path):
    request_file = tmp_path / "request.json"
    request_file.write_text(json.dumps(_example()))
    monkeypatch.setattr("sys.argv", ["gateway.cli", str(request_file)])

    assert cli.main() == 0
    assert json.loads(capsys.readouterr().out)["response"]["status"] == "ok"


def test_cli_rejects_malformed_json_without_calling_a_handler(monkeypatch, capsys):
    monkeypatch.setattr("sys.argv", ["gateway.cli"])
    monkeypatch.setattr("sys.stdin", io.StringIO("{ not json"))

    def _explode(_request):  # pragma: no cover - must never run
        raise AssertionError("handle() was called on unparseable input")

    monkeypatch.setattr(cli, "handle", _explode)

    assert cli.main() == 1
    assert json.loads(capsys.readouterr().out)["response"]["errors"][0]["code"] == "invalid_json"


def test_cli_exit_code_is_nonzero_when_the_gateway_reports_an_error(monkeypatch, capsys):
    monkeypatch.setattr("sys.argv", ["gateway.cli"])
    monkeypatch.setattr("sys.stdin", io.StringIO(json.dumps({"not": "a valid request"})))

    assert cli.main() == 1
    assert json.loads(capsys.readouterr().out)["response"]["status"] == "error"


# --------------------------------------------------------------------------
# gateway/docgen.py
# --------------------------------------------------------------------------

def test_docgen_renders_every_action_and_domain():
    schema = schema_guard.load_schema()
    out = docgen.generate()

    assert out.startswith("<!-- AUTO-GENERATED")
    for action in schema["properties"]["intent"]["properties"]["action"]["enum"]:
        assert f"- `{action}` — handled" in out
    for domain in schema["properties"]["intent"]["properties"]["domain"]["enum"]:
        assert f"- `{domain}`" in out
    # the generated doc must keep restating the guarantee it exists to document
    assert "there is no `read`" in out


def test_docgen_flags_an_action_with_no_handler(monkeypatch):
    monkeypatch.setattr(handlers, "DISPATCH", {})
    assert "**NO HANDLER REGISTERED**" in docgen.generate()


def test_docgen_skips_definitions_that_are_not_objects(monkeypatch):
    schema = json.loads(json.dumps(schema_guard.load_schema()))
    schema["definitions"]["NotAnObject"] = {"type": "string"}
    monkeypatch.setattr(docgen.schema_guard, "load_schema", lambda: schema)

    out = docgen.generate()
    assert "### NotAnObject" not in out
    assert "### FileOp" in out


def test_docgen_fmt_props_renders_enum_const_and_requiredness():
    table = docgen._fmt_props(
        {
            "kind": {"type": "string", "enum": ["a", "b"], "description": "what it is"},
            "capped": {"const": True},
            "ref": {"$ref": "#/definitions/FileOp"},
            "loose": {},
        },
        required={"kind"},
    )
    assert "| `kind` | string | yes | what it is enum: a, b |" in table
    assert "const: True" in table
    assert "#/definitions/FileOp" in table
    assert "| `loose` | any | no |  |" in table


# --------------------------------------------------------------------------
# gateway/validate_repo.py
# --------------------------------------------------------------------------

@pytest.fixture
def repo(isolated_repo, monkeypatch):
    """validate_repo against a throwaway copy, so a test can break the repo."""
    monkeypatch.setattr(validate_repo, "ROOT", isolated_repo)
    return isolated_repo


def test_validate_repo_passes_on_a_sound_checkout(repo, capsys):
    assert validate_repo.main() == 0
    assert "[PASS]" in capsys.readouterr().out


def test_validate_repo_reports_a_missing_required_file(repo, capsys):
    (repo / "tools" / "patterns.json").unlink()
    assert validate_repo.main() == 1
    assert "missing required file: tools/patterns.json" in capsys.readouterr().out


def test_validate_repo_reports_unparseable_json(repo, capsys):
    (repo / "daemons" / "registry.json").write_text("{ not json")
    assert validate_repo.main() == 1
    out = capsys.readouterr().out
    assert "daemons/registry.json: invalid JSON" in out


def test_validate_repo_reports_an_unloadable_schema(repo, monkeypatch, capsys):
    def _boom():
        raise schema_guard.SchemaLoadError("schema is not Draft-07")

    monkeypatch.setattr(validate_repo.schema_guard, "load_schema", _boom)
    assert validate_repo.main() == 1
    assert "schema is not Draft-07" in capsys.readouterr().out


def test_validate_repo_catches_an_action_with_no_example(repo, capsys):
    examples_path = repo / "schema" / "examples.json"
    data = json.loads(examples_path.read_text())
    data["examples"].pop("retrieve")
    examples_path.write_text(json.dumps(data))

    assert validate_repo.main() == 1
    assert "actions missing from schema/examples.json" in capsys.readouterr().out


def test_validate_repo_catches_an_example_that_violates_the_schema(repo, capsys):
    examples_path = repo / "schema" / "examples.json"
    data = json.loads(examples_path.read_text())
    data["examples"]["retrieve"]["intent"]["priority"] = "not-a-number"
    examples_path.write_text(json.dumps(data))

    assert validate_repo.main() == 1
    assert "fails schema validation" in capsys.readouterr().out


def test_validate_repo_catches_an_action_with_no_handler(repo, monkeypatch, capsys):
    trimmed = {k: v for k, v in handlers.DISPATCH.items() if k != "retrieve"}
    monkeypatch.setattr(handlers, "DISPATCH", trimmed)

    assert validate_repo.main() == 1
    assert "actions with no handler in gateway/handlers.py" in capsys.readouterr().out


def test_validate_repo_catches_a_structurally_broken_seed(repo, capsys):
    """seeds.json is hand-authored, so it fails here -- on the PR that broke
    it -- rather than in the nightly ingest that reads it hours later."""
    seeds = repo / "knowledge" / "seeds.json"
    data = json.loads(seeds.read_text())
    data["nodes"].append({"label": "no id at all"})
    seeds.write_text(json.dumps(data))

    assert validate_repo.main() == 1
    assert "'id' must be a non-empty string" in capsys.readouterr().out


def test_validate_repo_catches_a_dangling_seed_edge(repo, capsys):
    seeds = repo / "knowledge" / "seeds.json"
    data = json.loads(seeds.read_text())
    data["edges"].append({"from": "cycle:0", "to": "domain:build-pipeline", "kind": "delivers"})
    seeds.write_text(json.dumps(data))

    assert validate_repo.main() == 1
    assert "matches no node" in capsys.readouterr().out


def test_validate_repo_accepts_edges_into_derived_nodes(repo, capsys):
    """`domain:*` and `kind:*` ids are derived by ingest, not seeded, so an
    edge into one must pass even though no seed node declares it."""
    seeds = repo / "knowledge" / "seeds.json"
    data = json.loads(seeds.read_text())
    kb = json.loads((repo / "index" / "kb.index.json").read_text())
    a_kind = sorted({c["kind"] for c in kb["chunks"]})[0]
    data["edges"].append({"from": "cycle:0", "to": f"kind:{a_kind}", "kind": "covers"})
    seeds.write_text(json.dumps(data))

    assert validate_repo.main() == 0
    assert "[PASS]" in capsys.readouterr().out


# --------------------------------------------------------------------------
# gateway/verify_retrieval.py
# --------------------------------------------------------------------------

@pytest.fixture
def probes(isolated_repo, monkeypatch):
    monkeypatch.setattr(verify_retrieval, "ROOT", isolated_repo)
    return isolated_repo


def test_verify_retrieval_passes_against_a_populated_kb(probes, monkeypatch, capsys):
    # GitHub Actions always sets GITHUB_STEP_SUMMARY, so the "not running in CI"
    # path is never taken there unless it is removed explicitly.
    monkeypatch.delenv("GITHUB_STEP_SUMMARY", raising=False)

    assert verify_retrieval.main() == 0
    assert "[PASS] retrieval verification" in capsys.readouterr().out


def test_verify_retrieval_fails_loudly_on_an_empty_kb(probes, capsys):
    kb_path = probes / "index" / "kb.index.json"
    kb_path.write_text(json.dumps({"chunk_count": 0, "chunks": []}))

    results, failures = verify_retrieval.run_probes()
    assert results == []
    assert any("0 chunks" in f for f in failures)
    assert verify_retrieval.main() == 1
    assert "[FAIL]" in capsys.readouterr().out


def test_verify_retrieval_fails_when_the_kb_file_is_absent(probes):
    (probes / "index" / "kb.index.json").unlink()
    _, failures = verify_retrieval.run_probes()
    assert any("0 chunks" in f for f in failures)


def test_verify_retrieval_reports_a_gateway_error_status(probes, monkeypatch):
    monkeypatch.setattr(gateway, "handle", lambda _raw: {
        "response": {"status": "error", "errors": [{"code": "boom"}]}
    })
    results, failures = verify_retrieval.run_probes()
    assert results == []
    assert len(failures) == len(verify_retrieval.PROBES)
    assert all("gateway returned error" in f for f in failures)


def test_verify_retrieval_reports_too_few_hits(probes, monkeypatch):
    monkeypatch.setattr(gateway, "handle", lambda _raw: {
        "response": {"status": "ok", "retrieved": []}
    })
    _, failures = verify_retrieval.run_probes()
    assert all("expected >= 1 hit(s)" in f for f in failures)


def test_verify_retrieval_rejects_malformed_chunks(probes, monkeypatch):
    monkeypatch.setattr(gateway, "handle", lambda _raw: {
        "response": {"status": "ok", "retrieved": [
            {"source": "s", "score": 42.0, "content": "x" * 2001},
        ]}
    })
    _, failures = verify_retrieval.run_probes()
    joined = "\n".join(failures)
    assert "outside [0,1]" in joined
    assert "exceeds RetrievedChunk maxLength 2000" in joined


def test_verify_retrieval_reports_a_chunk_missing_a_required_field(probes, monkeypatch):
    monkeypatch.setattr(gateway, "handle", lambda _raw: {
        "response": {"status": "ok", "retrieved": [{"source": "s", "score": 0.5}]}
    })
    _, failures = verify_retrieval.run_probes()
    assert any("missing required field 'content'" in f for f in failures)


def test_verify_retrieval_writes_a_step_summary_when_running_in_ci(probes, monkeypatch, tmp_path):
    summary = tmp_path / "summary.md"
    monkeypatch.setenv("GITHUB_STEP_SUMMARY", str(summary))

    assert verify_retrieval.main() == 0
    written = summary.read_text()
    assert "## Retrieval verification" in written
    assert "| probe | strategy | hits | top match | score |" in written


def test_verify_retrieval_step_summary_records_failures(probes, monkeypatch, tmp_path):
    summary = tmp_path / "summary.md"
    monkeypatch.setenv("GITHUB_STEP_SUMMARY", str(summary))
    monkeypatch.setattr(gateway, "handle", lambda _raw: {
        "response": {"status": "ok", "retrieved": []}
    })

    assert verify_retrieval.main() == 1
    assert "**FAILURES**" in summary.read_text()
