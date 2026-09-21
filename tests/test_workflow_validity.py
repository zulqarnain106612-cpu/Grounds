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

The assertions are about references rather than formatting: a workflow may be
laid out however its author likes, but a name it depends on has to resolve.

Read as text, with a deliberately small amount of structure recovered by line
indentation, because that is what `test_unity_test_workflow.py` already does
and because `requirements.txt` is three packages long. Pulling in a YAML parser
to assert four things about eight files would be a worse trade than the modest
strictness this costs: the helpers below assume the ordinary two-space layout
these files use, and a wholesale reformat would need them updated.
"""
import json
import re
from pathlib import Path

import pytest

REAL_ROOT = Path(__file__).resolve().parent.parent
WORKFLOW_DIR = REAL_ROOT / ".github" / "workflows"
AUTO_MERGE = WORKFLOW_DIR / "auto-merge.yml"


def _workflows():
    return sorted(WORKFLOW_DIR.glob("*.yml"))


def _text(path):
    return path.read_text(encoding="utf-8")


def _block(text, header, indent=0):
    """The lines under a `header:` key, up to the next key at the same indent."""
    pad = " " * indent
    start = re.search(rf"^{pad}{re.escape(header)}:[^\S\n]*$", text, re.M)
    if start is None:
        return ""
    rest = text[start.end():].lstrip("\n").splitlines()
    out = []
    for line in rest:
        if line.strip() and not line.startswith(pad + " "):
            break
        out.append(line)
    return "\n".join(out)


def _workflow_name(text):
    m = re.search(r"^name:[^\S\n]*(.+?)[^\S\n]*$", text, re.M)
    return m.group(1).strip("'\"") if m else None


def _inline_list(text, key, indent):
    """`key: [a, b]` -> ['a', 'b']; absent or not inline -> None."""
    m = re.search(rf"^{' ' * indent}{re.escape(key)}:[^\S\n]*\[([^\]]*)\]", text, re.M)
    if m is None:
        return None
    return [v.strip().strip("'\"") for v in m.group(1).split(",") if v.strip()]


def _check_run_names(text):
    """The check-run names a workflow produces, which are its job names.

    A job with a single inline matrix axis is reported once per value as
    `job (value)` -- which is why the Unity legs appear as `test (editmode)`
    and `test (playmode)` rather than as `test`.
    """
    jobs_block = _block(text, "jobs")
    if not jobs_block:
        return []
    # Split the jobs block into one chunk per job id at two-space indent.
    bounds = [(m.group(1), m.start()) for m in
              re.finditer(r"^  ([A-Za-z0-9_-]+):[^\S\n]*$", jobs_block, re.M)]
    names = []
    for i, (job_id, start) in enumerate(bounds):
        end = bounds[i + 1][1] if i + 1 < len(bounds) else len(jobs_block)
        chunk = jobs_block[start:end]
        axes = re.findall(r"^        ([A-Za-z0-9_-]+):[^\S\n]*\[([^\]]*)\]", chunk, re.M)
        if len(axes) == 1:
            values = [v.strip().strip("'\"") for v in axes[0][1].split(",") if v.strip()]
            names.extend(f"{job_id} ({v})" for v in values)
        else:
            names.append(job_id)
    return names


def _all_check_run_names():
    names = set()
    for path in _workflows():
        names.update(_check_run_names(_text(path)))
    return names


def test_the_workflow_directory_is_not_empty():
    """A helper that silently matched nothing would make every test below vacuous."""
    assert _workflows(), "no workflows found; the assertions below would pass on nothing"


def test_every_workflow_declares_a_name_and_jobs():
    for path in _workflows():
        text = _text(path)
        assert _workflow_name(text), f"{path.name} declares no `name:`"
        assert _block(text, "jobs"), f"{path.name} declares no jobs"
        assert _check_run_names(text), f"{path.name}: no job ids parsed out of its jobs block"


def test_workflow_run_triggers_name_the_workflows_they_follow():
    """`workflow_run` without `workflows:` makes the file invalid.

    This is the defect that stopped auto-merge running at all: GitHub rejects
    the workflow at startup, so the run exists, lasts under a second, contains
    no jobs, and is reported only as a failure with no log to read.
    """
    for path in _workflows():
        on_block = _block(_text(path), "on")
        if not re.search(r"^  workflow_run:", on_block, re.M):
            continue
        spec = _block(on_block, "workflow_run", indent=2)
        assert re.search(r"^    workflows:", spec, re.M), (
            f"{path.name}: workflow_run requires a `workflows:` key; without it "
            f"the file is invalid and every run is a startup failure"
        )
        assert _inline_list(spec, "workflows", 4), f"{path.name}: `workflows:` is empty"


def test_workflow_run_triggers_reference_real_workflow_names():
    """The names under `workflows:` are workflow `name:` values, not filenames."""
    declared = {_workflow_name(_text(p)) for p in _workflows()}
    for path in _workflows():
        on_block = _block(_text(path), "on")
        if not re.search(r"^  workflow_run:", on_block, re.M):
            continue
        spec = _block(on_block, "workflow_run", indent=2)
        for named in (_inline_list(spec, "workflows", 4) or []):
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
    source = _text(AUTO_MERGE)
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
    source = _text(AUTO_MERGE)
    on_block = _block(source, "on")
    late = [t for t in ("check_run", "workflow_run")
            if re.search(rf"^  {t}:", on_block, re.M)]
    if not late:
        pytest.skip("auto-merge no longer listens for post-CI events")

    for trigger in late:
        assert re.search(rf"payload\.{trigger}[\s\S]{{0,160}}pull_requests", source), (
            f"auto-merge declares the {trigger} trigger but never reads "
            f"payload.{trigger}.pull_requests, so it cannot know which PR the "
            f"event belongs to -- context.issue is empty for that payload"
        )


def test_auto_merge_refuses_drafts_and_conflicts():
    """Merging a draft, or a PR GitHub has already called unmergeable."""
    source = _text(AUTO_MERGE)
    assert "draft" in source, "auto-merge does not check the draft flag"
    assert "mergeable" in source, "auto-merge does not check mergeability"


def test_every_policy_declared_workflow_is_one_of_the_files_here():
    """The policy names workflows by path; a rename silently unhooks the policy."""
    policy = json.loads((REAL_ROOT / "schema" / "agent.schema.json").read_text(
        encoding="utf-8"))["x-enforcement"]["execution_policy"]
    present = {f".github/workflows/{p.name}" for p in _workflows()}
    for name, path in policy["workflows"].items():
        assert path in present, f"policy workflow '{name}' points at {path}, which is not present"
