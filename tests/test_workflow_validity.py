"""Every workflow file is asserted structurally, not just asserted to exist.

`test_ci_policy.test_every_policy_workflow_exists` checks that the files are
present. Presence is not validity, and the gap between the two is not
hypothetical: `auto-merge.yml` shipped with a `workflow_run` trigger that had
no `workflows:` key, which makes the whole file invalid. Every run of it ended
as a startup failure in under a second, having executed no step, for as long as
it existed -- and the same file gated on three check names that no job in this
repository produces, so even once it ran it could never have merged anything.

Both defects are the same shape: a workflow naming something that does not
exist. Nothing read these files, so nothing objected. `unity-test.yml` was the
only workflow with a structural test, and it is the only one of the eight that
was correct.

The assertions here are deliberately about references rather than about
formatting: a workflow may be laid out however its author likes, but a name it
depends on has to resolve.
"""
import json
import re
from pathlib import Path

import pytest
import yaml

REAL_ROOT = Path(__file__).resolve().parent.parent
WORKFLOW_DIR = REAL_ROOT / ".github" / "workflows"
AUTO_MERGE = WORKFLOW_DIR / "auto-merge.yml"


def _workflows():
    return sorted(WORKFLOW_DIR.glob("*.yml"))


def _load(path):
    return yaml.safe_load(path.read_text(encoding="utf-8"))


def _triggers(doc):
    """`on:` parses as the boolean True in YAML 1.1, which is a known trap."""
    return doc.get(True, doc.get("on"))


def _check_run_names(doc):
    """The check-run names a workflow produces, which are its job names.

    A job with a single-key matrix is reported once per value as
    `job (value)` -- which is why the Unity legs appear as `test (editmode)`
    and `test (playmode)` rather than as `test`.
    """
    names = []
    for job_id, job in (doc.get("jobs") or {}).items():
        matrix = ((job.get("strategy") or {}).get("matrix") or {})
        axes = {k: v for k, v in matrix.items() if isinstance(v, list)}
        if len(axes) == 1:
            (values,) = axes.values()
            names.extend(f"{job_id} ({v})" for v in values)
        else:
            names.append(job_id)
    return names


def _all_check_run_names():
    names = set()
    for path in _workflows():
        names.update(_check_run_names(_load(path)))
    return names


def test_every_workflow_parses():
    """A file that does not parse never runs, and says so only in the run log."""
    for path in _workflows():
        try:
            doc = _load(path)
        except yaml.YAMLError as exc:  # pragma: no cover - the failure message is the point
            pytest.fail(f"{path.name} is not valid YAML: {exc}")
        assert isinstance(doc, dict), f"{path.name} did not parse to a mapping"
        assert doc.get("jobs"), f"{path.name} declares no jobs"


def test_every_workflow_declares_triggers():
    for path in _workflows():
        assert _triggers(_load(path)), f"{path.name} has no `on:` triggers"


def test_workflow_run_triggers_name_the_workflows_they_follow():
    """`workflow_run` without `workflows:` makes the file invalid.

    This is the defect that stopped auto-merge running at all: GitHub rejects
    the workflow at startup, so the run exists, lasts under a second, contains
    no jobs, and is reported only as a failure with no log to read.
    """
    for path in _workflows():
        on = _triggers(_load(path))
        if not isinstance(on, dict) or "workflow_run" not in on:
            continue
        spec = on["workflow_run"] or {}
        assert "workflows" in spec, (
            f"{path.name}: workflow_run requires a `workflows:` key; without it "
            f"the file is invalid and every run is a startup failure"
        )
        assert spec["workflows"], f"{path.name}: `workflows:` is empty"


def test_workflow_run_triggers_reference_real_workflow_names():
    """The names under `workflows:` are workflow `name:` values, not filenames."""
    declared = {_load(p).get("name") for p in _workflows()}
    for path in _workflows():
        on = _triggers(_load(path))
        if not isinstance(on, dict) or "workflow_run" not in on:
            continue
        for named in (on["workflow_run"] or {}).get("workflows", []):
            assert named in declared, (
                f"{path.name}: workflow_run follows '{named}', which is not the "
                f"`name:` of any workflow here. Known: {sorted(n for n in declared if n)}"
            )


def test_auto_merge_gates_on_check_names_that_something_actually_produces():
    """The second defect: three required checks that matched no check run.

    `checks.listForRef` returns check-run names, which are job names.
    'agent-schema-enforcement' is the `name:` of enforce.yml, whose job is
    `validate`; the Unity legs are matrix jobs. All three lookups returned
    undefined, `allPassed` was false on every evaluation, and green CI could
    never have merged anything.
    """
    source = AUTO_MERGE.read_text(encoding="utf-8")
    block = re.search(r"requiredChecks\s*=\s*\[(.*?)\]", source, re.S)
    assert block, "auto-merge.yml no longer declares requiredChecks"

    required = re.findall(r"['\"]([^'\"]+)['\"]", block.group(1))
    assert required, "requiredChecks is empty, so the gate asserts nothing"

    produced = _all_check_run_names()
    unknown = [name for name in required if name not in produced]
    assert not unknown, (
        f"auto-merge gates on {unknown}, which no job produces. "
        f"Available check-run names: {sorted(produced)}"
    )


def test_auto_merge_resolves_the_pull_request_on_every_trigger_it_declares():
    """`context.issue` is empty on check_run and workflow_run payloads.

    Those two are the only triggers that fire *after* CI has finished, which is
    the one moment a merge gate matters. Reading the number solely from
    `context.issue` means the script returns 'not a pull request context' at
    exactly the point it is supposed to act, and the PR is never merged.
    """
    source = AUTO_MERGE.read_text(encoding="utf-8")
    on = _triggers(_load(AUTO_MERGE))
    late = [t for t in ("check_run", "workflow_run") if isinstance(on, dict) and t in on]
    if not late:
        pytest.skip("auto-merge no longer listens for post-CI events")

    for trigger in late:
        assert re.search(rf"payload\.{trigger}[\s\S]{{0,120}}pull_requests", source), (
            f"auto-merge declares the {trigger} trigger but never reads "
            f"payload.{trigger}.pull_requests, so it cannot know which PR the "
            f"event belongs to -- context.issue is empty for that payload"
        )


def test_auto_merge_refuses_drafts_and_conflicts():
    """Merging a draft, or a PR GitHub has already called unmergeable."""
    source = AUTO_MERGE.read_text(encoding="utf-8")
    assert "draft" in source, "auto-merge does not check the draft flag"
    assert "mergeable" in source, "auto-merge does not check mergeability"


def test_every_policy_declared_workflow_is_one_of_the_files_here():
    """The policy names workflows by path; a rename silently unhooks the policy."""
    policy = json.loads((REAL_ROOT / "schema" / "agent.schema.json").read_text(
        encoding="utf-8"))["x-enforcement"]["execution_policy"]
    present = {f".github/workflows/{p.name}" for p in _workflows()}
    for name, path in policy["workflows"].items():
        assert path in present, f"policy workflow '{name}' points at {path}, which is not present"
