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
UNITY_TEST = WORKFLOW_DIR / "unity-test.yml"


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


def _yaml_list(text, key, indent):
    """`key: [a, b]` or a block sequence under `key:` -> ['a', 'b'].

    Absent, or present but empty -> None.

    Both spellings are valid YAML and both appear in this repo's workflows.
    Reading only the inline form had two costs: a populated block list was
    reported as "empty", and at the call site below that tolerates None it
    meant the names were never checked against real workflows at all.
    """
    pad = " " * indent
    inline = re.search(rf"^{pad}{re.escape(key)}:[^\S\n]*\[([^\]]*)\]", text, re.M)
    if inline is not None:
        values = [v.strip().strip("'\"") for v in inline.group(1).split(",") if v.strip()]
        return values or None

    header = re.search(rf"^{pad}{re.escape(key)}:[^\S\n]*$", text, re.M)
    if header is None:
        return None
    values = []
    for line in text[header.end():].splitlines():
        if not line.strip():
            continue
        entry = re.match(rf"^{pad}\s+-\s*(.+?)\s*$", line)
        if entry is None:
            break
        values.append(entry.group(1).strip().strip("'\""))
    return values or None


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
        assert _yaml_list(spec, "workflows", 4), f"{path.name}: `workflows:` is empty"


def test_workflow_run_triggers_reference_real_workflow_names():
    """The names under `workflows:` are workflow `name:` values, not filenames."""
    declared = {_workflow_name(_text(p)) for p in _workflows()}
    for path in _workflows():
        on_block = _block(_text(path), "on")
        if not re.search(r"^  workflow_run:", on_block, re.M):
            continue
        spec = _block(on_block, "workflow_run", indent=2)
        for named in (_yaml_list(spec, "workflows", 4) or []):
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


def test_auto_merge_requires_the_unity_legs_only_when_they_will_run():
    """The third auto-merge defect: requiring a check that will never appear.

    unity-test.yml is path-filtered. On a pull request touching none of those
    paths the workflow never runs, so `test (editmode)` / `test (playmode)`
    never exist as check runs. Requiring them unconditionally made
    `checksByName[name]` undefined and auto-merge waited forever -- on every
    ingest, docs or gateway pull request, which is most of them.
    """
    text = _text(AUTO_MERGE)
    assert "touchesUnity" in text, (
        "auto-merge must decide the Unity legs from the changed files, not "
        "require them unconditionally"
    )
    assert "listFiles" in text, "it needs the PR's file list to make that call"
    # 'validate' comes from enforce.yml, which is not path-filtered, so it is
    # always required.
    assert "const requiredChecks = ['validate'];" in text


def test_auto_merge_unity_paths_match_the_workflow_they_mirror():
    """UNITY_PATHS is a copy of unity-test.yml's `paths:` filter, and a copy
    that drifts is worse than no copy: auto-merge would either wait for a run
    that never comes, or stop requiring a run that does."""
    # unity-test.yml lists each path twice (push and pull_request); a set
    # collapses that. The filter uses globs, auto-merge matches by prefix.
    declared = {
        entry.replace("/**", "/")
        for entry in re.findall(r'^\s*-\s*"([^"]+)"\s*$', _text(UNITY_TEST), re.M)
    }
    assert declared, "unity-test.yml no longer declares a paths: filter"

    array = re.search(r"UNITY_PATHS = \[(.*?)\];", _text(AUTO_MERGE), re.S)
    assert array, "auto-merge no longer declares UNITY_PATHS"
    mirrored = set(re.findall(r"'([^']+)'", array.group(1)))
    assert declared == mirrored, (
        f"unity-test.yml paths {sorted(declared)} != auto-merge UNITY_PATHS "
        f"{sorted(mirrored)}; update both together"
    )


def test_pr_triggered_workflows_do_not_also_run_on_branch_pushes():
    """Two full runs of the same commit, which this cell exists to stop.

    A workflow with a bare `push:` and a `pull_request:` runs twice for every
    commit on a pull request branch. enforce.yml and coverage.yml scope push
    to main for that reason; unity-test.yml was missed, and there it is worse
    than slow -- each run activates a Unity seat per test mode, and a Personal
    account holds very few, so the duplicate competes with the run the pull
    request actually gates on.
    """
    for path in _workflows():
        text = _text(path)
        on_block = _block(text, "on")
        if not re.search(r"^  push:", on_block, re.M):
            continue
        if not re.search(r"^  pull_request:", on_block, re.M):
            continue  # push-only is fine; nothing duplicates it
        push = _block(on_block, "push", indent=2)
        assert re.search(r"^    branches:", push, re.M), (
            f"{path.name}: has both push: and pull_request:, so every commit "
            f"on a PR branch runs it twice. Scope push to branches: [main]."
        )


# --- action runtimes --------------------------------------------------------

# The lowest major of each first-party action that runs on Node 24. Below it,
# GitHub forces the action onto Node 24 anyway and prints a deprecation
# warning on every job; the announced end of that forcing is the action
# failing outright, which would take every workflow here down at once.
#
# These are floors, not pins: a newer major is fine, an older one is not. Each
# was read from `runs.using` in that action's own action.yml at the tag, not
# from release prose -- the manifest is what the runner obeys.
NODE24_FLOOR = {
    "actions/checkout": 5,
    "actions/setup-python": 6,
    "actions/cache": 5,
    "actions/upload-artifact": 6,
    "actions/download-artifact": 8,
    "actions/github-script": 8,
}


def test_first_party_actions_run_on_a_supported_node():
    """A copy-pasted `@v4` is the way this regresses: a new workflow borrows a
    step from an old one and reintroduces the deprecated runtime silently,
    because a warning is not a failure."""
    stale = []
    for path in _workflows():
        for line_no, line in enumerate(_text(path).splitlines(), start=1):
            match = re.search(r"uses:\s*(actions/[a-z-]+)@v(\d+)", line)
            if not match:
                continue
            action, major = match.group(1), int(match.group(2))
            floor = NODE24_FLOOR.get(action)
            if floor is not None and major < floor:
                stale.append(f"{path.name}:{line_no} {action}@v{major} < v{floor}")
    assert not stale, (
        "these steps run on a deprecated Node runtime:\n  " + "\n  ".join(stale)
    )


def test_the_floor_table_covers_every_first_party_action_in_use():
    """An action not in the table is one nothing checks. It is allowed -- a
    composite action has no Node runtime of its own -- but the omission has to
    be a decision, so this lists what is unchecked and fails on a new one.
    """
    in_use = set()
    for path in _workflows():
        in_use.update(re.findall(r"uses:\s*(actions/[a-z-]+)@", _text(path)))
    # actions/upload-code-coverage is a composite action: it runs no Node
    # entry point of its own, so no floor applies to it.
    unchecked = in_use - set(NODE24_FLOOR) - {"actions/upload-code-coverage"}
    assert not unchecked, (
        f"first-party actions with no Node floor recorded: {sorted(unchecked)}. "
        f"Read runs.using from the action's action.yml at each major tag and "
        f"add the lowest one that says node24."
    )
