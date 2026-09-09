"""Cycle 0, branch phase0/ci-unity-test-workflow.

The workflow's own acceptance criteria are about its *failure* paths -- a
failing test turns the job red, and zero discovered tests turns the job red --
and neither is provable by watching a green run. Both live in
scripts/check_unity_results.py, which is plain Python and is tested here
directly, so the guarantees hold before a Unity licence is ever configured.

The workflow file itself is asserted structurally: a licence gate that fails
instead of skipping, and a results gate that runs even when the runner step
did not.
"""
from __future__ import annotations

import json
import subprocess
import sys
import textwrap
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "scripts"))
import check_unity_results as gate  # noqa: E402

REAL_ROOT = Path(__file__).resolve().parent.parent
WORKFLOW = REAL_ROOT / ".github" / "workflows" / "unity-test.yml"
TESTS_DIR = REAL_ROOT / "Assets" / "_Game" / "Tests"


def _results(path: Path, *, total: int, passed: int, failed: int = 0, skipped: int = 0) -> Path:
    path.write_text(textwrap.dedent(f"""\
        <?xml version="1.0" encoding="utf-8"?>
        <test-run id="2" total="{total}" passed="{passed}" failed="{failed}"
                  inconclusive="0" skipped="{skipped}" result="Passed">
          <test-suite type="Assembly" name="JetFighter.Tests.EditMode" />
        </test-run>
        """))
    return path


# --- the gate ---------------------------------------------------------------

def test_a_normal_passing_run_is_accepted(tmp_path):
    _results(tmp_path / "results.xml", total=2, passed=2)
    assert gate.check(tmp_path, min_tests=1) == []


def test_a_failing_test_is_rejected(tmp_path):
    _results(tmp_path / "results.xml", total=2, passed=1, failed=1)
    problems = gate.check(tmp_path, min_tests=1)
    assert any("1 test(s) failed" in p for p in problems)


def test_zero_discovered_tests_is_rejected(tmp_path):
    """The criterion this script exists for: the Unity runner exits 0 when it
    finds nothing, so a broken .asmdef would stay green forever."""
    _results(tmp_path / "results.xml", total=0, passed=0)
    problems = gate.check(tmp_path, min_tests=1)
    assert any("only 0 test(s) discovered" in p for p in problems)


def test_a_run_that_only_skips_is_rejected(tmp_path):
    _results(tmp_path / "results.xml", total=3, passed=0, skipped=3)
    assert "no test actually passed" in gate.check(tmp_path, min_tests=1)


def test_min_tests_is_enforced_above_one(tmp_path):
    _results(tmp_path / "results.xml", total=1, passed=1)
    assert gate.check(tmp_path, min_tests=5)
    assert gate.check(tmp_path, min_tests=1) == []


def test_totals_are_summed_across_result_files(tmp_path):
    """The matrix writes one file per mode; a green mode must not mask an
    empty one."""
    _results(tmp_path / "a.xml", total=2, passed=2)
    nested = tmp_path / "nested"
    nested.mkdir()
    _results(nested / "b.xml", total=1, passed=0, failed=1)
    problems = gate.check(tmp_path, min_tests=1)
    assert any("1 test(s) failed" in p for p in problems)


def test_a_single_file_path_is_accepted(tmp_path):
    path = _results(tmp_path / "results.xml", total=1, passed=1)
    assert gate.check(path, min_tests=1) == []


# --- the gate's own failure modes ------------------------------------------

def test_a_missing_results_path_is_an_error(tmp_path):
    with pytest.raises(gate.ResultsError, match="does not exist"):
        gate.check(tmp_path / "nope", min_tests=1)


def test_an_empty_results_directory_is_an_error(tmp_path):
    with pytest.raises(gate.ResultsError, match="no .xml results found"):
        gate.check(tmp_path, min_tests=1)


def test_unparseable_xml_is_an_error(tmp_path):
    (tmp_path / "results.xml").write_text("<test-run")
    with pytest.raises(gate.ResultsError, match="not parseable as XML"):
        gate.check(tmp_path, min_tests=1)


def test_the_wrong_root_element_is_an_error(tmp_path):
    (tmp_path / "results.xml").write_text("<coverage total='9'/>")
    with pytest.raises(gate.ResultsError, match="expected <test-run>"):
        gate.check(tmp_path, min_tests=1)


def test_a_missing_count_attribute_is_an_error(tmp_path):
    (tmp_path / "results.xml").write_text("<test-run total='1' passed='1'/>")
    with pytest.raises(gate.ResultsError, match="no 'failed' attribute"):
        gate.check(tmp_path, min_tests=1)


def test_a_non_numeric_count_is_an_error(tmp_path):
    (tmp_path / "results.xml").write_text(
        "<test-run total='many' passed='1' failed='0' skipped='0'/>")
    with pytest.raises(gate.ResultsError, match="not a number"):
        gate.check(tmp_path, min_tests=1)


# --- the CLI, which is what the workflow actually invokes -------------------

def test_cli_exits_zero_on_a_good_run(tmp_path, capsys):
    _results(tmp_path / "results.xml", total=2, passed=2)
    assert gate.main(["--results", str(tmp_path), "--min-tests", "1"]) == 0
    assert "unity test gate passed" in capsys.readouterr().out


def test_cli_exits_nonzero_and_annotates_on_zero_tests(tmp_path, capsys):
    _results(tmp_path / "results.xml", total=0, passed=0)
    assert gate.main(["--results", str(tmp_path)]) == 1
    assert "::error::" in capsys.readouterr().out


def test_cli_exits_nonzero_when_the_runner_produced_nothing(tmp_path, capsys):
    assert gate.main(["--results", str(tmp_path / "missing")]) == 1
    assert "::error::" in capsys.readouterr().out


# --- the workflow file ------------------------------------------------------

def test_the_licence_gate_fails_rather_than_skips():
    """A skipped test gate is a green lie -- the spec calls this out by name."""
    text = WORKFLOW.read_text()
    assert "secrets.UNITY_LICENSE" in text
    assert "No Unity licence configured" in text
    assert "exit 1" in text
    assert "if: ${{ secrets" not in text, "a job-level secret condition would skip, not fail"


def test_the_results_gate_runs_even_when_the_runner_step_failed():
    """The gate must still run when the runner fails, so a red job says *why*.

    The original form of this asserted `continue-on-error` was absent from the
    runner step, because a step that does not fail is omitted from
    `gh run view --log-failed` and so hides the reason the job is red.

    The runner step now carries `continue-on-error: true` again, deliberately:
    game-ci/unity-test-runner fails while *posting* its check run even when the
    suites themselves ran, and letting that fail the job made a green test run
    report red. The visibility guarantee the original assertion protected is
    kept by other means instead, and that is what is asserted here:

      - the gate step is `if: always()`, so it runs regardless, and
      - an explicit `if: always() && steps.runner.outcome != 'success'` step
        emits `::error::` annotations naming the runner's outcome, which is
        what puts the cause back in front of the two-line probe.
    """
    # Comments only, stripped: the comment explaining the continue-on-error
    # trade-off otherwise trips the substring checks below -- the same trap
    # PR #16 fixed for the C# unconstrained-flight assertion.
    code = "\n".join(
        line for line in WORKFLOW.read_text().splitlines()
        if not line.lstrip().startswith("#")
    )
    assert "if: always()" in code
    assert "scripts/check_unity_results.py" in code
    assert "--min-tests" in code
    # The replacement for the old continue-on-error ban: a runner that never
    # started must still announce itself as an annotation.
    assert "steps.runner.outcome != 'success'" in code, (
        "without this step a continue-on-error runner failure is invisible to "
        "--log-failed, which is the whole reason the ban existed"
    )
    assert "::error::game-ci/unity-test-runner exited" in code


def test_both_test_modes_are_covered():
    assert "testMode: [editmode, playmode]" in WORKFLOW.read_text()


@pytest.mark.parametrize("mode", ["EditMode", "PlayMode"])
def test_each_mode_has_a_test_assembly_and_at_least_one_test(mode):
    """The gate fails a mode that discovers nothing, so every mode in the
    matrix must actually have tests -- otherwise this workflow is red by
    construction."""
    folder = TESTS_DIR / mode
    asmdefs = list(folder.glob("*.asmdef"))
    assert len(asmdefs) == 1, f"{mode} needs exactly one assembly definition"
    asmdef = json.loads(asmdefs[0].read_text())
    assert "JetFighter.Runtime" in asmdef["references"]
    assert asmdef["defineConstraints"] == ["UNITY_INCLUDE_TESTS"]

    sources = list(folder.glob("*.cs"))
    assert sources, f"{mode} has no tests; the gate would fail the job"
    assert any("[Test]" in s.read_text() or "[UnityTest]" in s.read_text() for s in sources)


def test_editmode_assembly_is_editor_only():
    asmdef = json.loads((TESTS_DIR / "EditMode" / "JetFighter.Tests.EditMode.asmdef").read_text())
    assert asmdef["includePlatforms"] == ["Editor"]


def test_the_test_tree_stays_out_of_the_symbol_index():
    """docs/CYCLE0_BOOTSTRAP_SPEC.md section 1: source_globs covers Scripts/
    only, deliberately -- tests are not project symbols."""
    globs = json.loads((REAL_ROOT / "config" / "agent.config.json").read_text())["source_globs"]
    assert globs == ["Assets/_Game/Scripts/**/*.cs"]
    matched = [p.relative_to(REAL_ROOT) for p in REAL_ROOT.glob(globs[0])]
    assert matched, "the glob matches nothing; this assertion would be vacuous"
    assert not any(str(p).startswith("Assets/_Game/Tests/") for p in matched)


def test_the_script_is_runnable_as_a_program(tmp_path):
    """The workflow calls it with `python scripts/...`, not as a module."""
    _results(tmp_path / "results.xml", total=1, passed=1)
    proc = subprocess.run(
        [sys.executable, str(REAL_ROOT / "scripts" / "check_unity_results.py"),
         "--results", str(tmp_path)],
        capture_output=True, text=True, timeout=30)
    assert proc.returncode == 0, proc.stderr
