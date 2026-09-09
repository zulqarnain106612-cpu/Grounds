from __future__ import annotations
import json
import os
import shlex
import subprocess
from pathlib import Path

from . import enforcement, kb_store, log_guard, context_store, symbol_scanner
from .schema_guard import validate as schema_validate_instance, RequestValidationError

ROOT = Path(__file__).resolve().parent.parent


def _ok(result=None, **extra):
    out = {"status": "ok"}
    if result is not None:
        out["result"] = result
    out.update(extra)
    return out


def _error(code: str, message: str, fatal: bool = False):
    return {"status": "error", "errors": [{"code": code, "message": message, "fatal": fatal}]}


# ---- individual action handlers -------------------------------------------------

def h_symbol_lookup(req: dict) -> dict:
    target = req["payload"].get("context", {}).get("target")
    if not target:
        return _error("missing_target", "context.target required for symbol_lookup")
    return _ok(symbols=kb_store.symbol_lookup(target))


def h_log_query(req: dict) -> dict:
    log_op = req["payload"].get("log_op", {})
    if log_op.get("op") not in ("query_failure", "tail"):
        return _error("unsupported_op", "only query_failure/tail are implemented")
    lines = log_guard.query_failures(log_op.get("filter_level"), log_op.get("tail_lines"))
    return _ok(result=lines)


def h_command_exec(req: dict) -> dict:
    cmd_spec = req["payload"].get("command")
    if not cmd_spec:
        return _error("missing_command", "payload.command required")
    cmd, args = cmd_spec["cmd"], cmd_spec.get("args", [])
    try:
        enforcement.assert_command_allowed(cmd, args)
    except enforcement.ForbiddenCommandError as e:
        return _error("forbidden_command", str(e), fatal=True)

    timeout_s = cmd_spec.get("timeout_ms", 5000) / 1000
    try:
        proc = subprocess.run(
            shlex.split(cmd) + list(args),
            cwd=cmd_spec.get("working_dir") or ROOT,
            capture_output=True, text=True, timeout=timeout_s,
        )
        out = (proc.stdout or "") + (proc.stderr or "")
    except subprocess.TimeoutExpired:
        return _error("timeout", f"command exceeded {timeout_s}s", fatal=True)
    except (OSError, ValueError) as e:
        return _error("exec_failed", str(e))

    capped = enforcement.cap_output_lines(cmd_spec.get("output_lines"))
    truncated, was_truncated = enforcement.truncate_lines(out, capped)
    return _ok(result={"output": truncated, "truncated": was_truncated, "exit_code": proc.returncode})


def h_tool_call(req: dict) -> dict:
    tool = req["payload"].get("tool", {})
    max_out = enforcement.cap_tool_output_tokens(tool.get("max_output"))
    # Extensible dispatch point -- register real tools in TOOL_REGISTRY below.
    fn = TOOL_REGISTRY.get(tool.get("tool_name"))
    if not fn:
        return _error("unknown_tool", f"no handler registered for '{tool.get('tool_name')}'")
    result = fn(tool.get("parameters", {}))
    return _ok(result=str(result)[: max_out * 4])


def h_retrieve(req: dict) -> dict:
    ctx = req["payload"].get("context")
    if not ctx:
        return _error("missing_context", "payload.context required for retrieve/query")
    chunks = kb_store.retrieve(
        ctx["strategy"], ctx.get("target"),
        ctx.get("top_k", 5), ctx.get("max_tokens", 1500),
    )
    return _ok(retrieved=chunks)


def h_write(req: dict) -> dict:
    file_op = req["payload"].get("file_op")
    if file_op:
        path = ROOT / file_op["path"]
        path.parent.mkdir(parents=True, exist_ok=True)
        content = file_op.get("content", "")
        mode = "a" if file_op["op"] == "append" else "w"
        with open(path, mode, encoding=file_op.get("encoding", "utf-8")) as f:
            f.write(content)
        return _ok(mutations=[str(file_op["path"])])
    data = req["payload"].get("data")
    if isinstance(data, dict) and "id" in data:
        kb_store.add_kb_chunk(data)
        return _ok(mutations=[f"kb:{data['id']}"])
    return _error("nothing_to_write", "provide payload.file_op or payload.data (kb chunk)")


def h_delete(req: dict) -> dict:
    file_op = req["payload"].get("file_op")
    if file_op and file_op["op"] == "delete":
        path = ROOT / file_op["path"]
        existed = path.exists()
        if existed:
            path.unlink()
        return _ok(mutations=[str(file_op["path"])] if existed else [])
    data = req["payload"].get("data")
    if isinstance(data, dict) and "id" in data:
        changed = kb_store.delete_kb_chunk(data["id"])
        return _ok(mutations=[f"kb:{data['id']}"] if changed else [])
    return _error("nothing_to_delete", "provide payload.file_op(op=delete) or payload.data.id")


def h_index(req: dict) -> dict:
    result = symbol_scanner.rebuild_symbol_index(ROOT)
    return _ok(result={"symbol_count": len(result["symbols"])})


def h_knowledge_update(req: dict) -> dict:
    op = req["payload"].get("knowledge_op")
    if not op:
        return _error("missing_knowledge_op", "payload.knowledge_op required")
    graph = kb_store.apply_knowledge_op(op)
    return _ok(mutations=[f"graph:{op['id']}"], result={"node_count": len(graph["graph"]["nodes"])})


def h_context_push(req: dict) -> dict:
    session_id = req["meta"]["session_id"]
    stack = context_store.push(session_id, req["payload"].get("data"))
    return _ok(result={"depth": len(stack)})


def h_context_pop(req: dict) -> dict:
    session_id = req["meta"]["session_id"]
    frame, stack = context_store.pop(session_id)
    return _ok(result={"frame": frame, "depth": len(stack)})


def h_schema_validate(req: dict) -> dict:
    candidate = req["payload"].get("data")
    if not isinstance(candidate, dict):
        return _error("invalid_candidate", "payload.data must be an object to validate")
    try:
        schema_validate_instance(candidate)
        return _ok(result={"valid": True})
    except RequestValidationError as e:
        return _ok(result={"valid": False, "errors": e.errors})


def h_daemon_status(req: dict) -> dict:
    from . import daemon_manager
    daemon_id = req["payload"].get("daemon_op", {}).get("daemon_id")
    return _ok(result=daemon_manager.status(daemon_id))


def h_daemon_start(req: dict) -> dict:
    from . import daemon_manager
    daemon_id = req["payload"]["daemon_op"]["daemon_id"]
    return _ok(result=daemon_manager.start(daemon_id))


def h_daemon_stop(req: dict) -> dict:
    from . import daemon_manager
    daemon_id = req["payload"]["daemon_op"]["daemon_id"]
    return _ok(result=daemon_manager.stop(daemon_id))


def h_build(req: dict) -> dict:
    # build is command_exec under the hood, always capped the same way.
    return h_command_exec(req)


# ---- CI handlers ---------------------------------------------------------------
# Policy: build / test / review / ingest / manifest / poll never run on the local
# machine. They run on GitHub Actions. `_in_ci()` is the single gate: in-process
# execution is permitted only when the process is itself a CI runner. Everywhere
# else these actions dispatch a workflow and return immediately -- they never block.

def _in_ci() -> bool:
    return os.environ.get("GITHUB_ACTIONS") == "true" or os.environ.get("JFA_CI") == "1"


def _gh(args: list[str], timeout_s: float = 20.0) -> tuple[int, str]:
    """Run a short, non-blocking `gh` call. Never used to wait on a run.

    JFA_NO_DISPATCH=1 short-circuits to a canned success. Verification jobs set
    it so that replaying the worked examples cannot fire a real workflow run
    (a ci_dispatch example would otherwise trigger ingest.yml from inside a CI
    job, which risks a dispatch loop). The guard is explicit here rather than
    relying on a token being absent from the job.
    """
    if os.environ.get("JFA_NO_DISPATCH") == "1":
        return 0, "[]"
    try:
        proc = subprocess.run(
            ["gh", *args], cwd=ROOT, capture_output=True, text=True, timeout=timeout_s
        )
    except FileNotFoundError:
        return 127, "gh CLI not found on PATH"
    except subprocess.TimeoutExpired:
        return 124, f"gh call exceeded {timeout_s}s"
    return proc.returncode, (proc.stdout or "") + (proc.stderr or "")


def h_ci_dispatch(req: dict) -> dict:
    ci_op = req["payload"].get("ci_op")
    if not ci_op:
        return _error("missing_ci_op", "payload.ci_op required for ci_dispatch")
    workflow = ci_op.get("workflow")
    if not workflow:
        return _error("missing_workflow", "ci_op.workflow required for dispatch")

    args = ["workflow", "run", workflow, "--ref", ci_op.get("ref", "main")]
    for key, value in (ci_op.get("inputs") or {}).items():
        args += ["-f", f"{key}={value}"]

    code, out = _gh(args)
    if code != 0:
        return _error("dispatch_failed", out.strip()[:500])
    # Deliberately returns without polling: CIOp.wait is const false in the schema.
    return _ok(result={"dispatched": workflow, "ref": ci_op.get("ref", "main"), "blocking": False})


_ERROR_MARKER = "##[error]"


def h_ci_status(req: dict) -> dict:
    """Check status for exactly one PR.

    A repo-wide listing is not expressible: CIOp requires `pr` when op is
    'status', and this refuses without it. `gh pr checks` is PR-scoped by
    construction, so there is no `gh run list` on this path at all.
    """
    ci_op = req["payload"].get("ci_op") or {}
    pr = ci_op.get("pr")
    if not pr:
        return _error("missing_pr", "ci_op.pr required: status is always scoped to one PR")
    limit = min(enforcement.cap_output_lines(ci_op.get("limit")), 20)

    code, out = _gh(["pr", "checks", str(pr),
                     "--json", "bucket,state,workflow,name"])
    # 8 means checks are still pending -- a state to report, not a failure.
    if code not in (0, 8):
        return _error("status_failed", out.strip()[:500])
    try:
        checks = json.loads(out)
    except json.JSONDecodeError:
        return _error("bad_gh_output", out.strip()[:500])
    failed = [c.get("name") for c in checks if c.get("bucket") == "fail"]
    return _ok(result={"pr": pr, "checks": checks[:limit],
                       "failed": failed, "total": len(checks)})


def h_ci_logs(req: dict) -> dict:
    """Lines from the newest failed run of exactly one PR.

    Omitting log_tail_lines returns the fixed probe: the last two
    `##[error]` annotations plus the position of the last one. The last
    *physical* lines of a failed job are runner teardown, not the cause, so a
    plain tail would report noise -- the annotations are the cause, and the
    position is what sizes a follow-up request. A named count returns exactly
    that many lines, ceilinged server-side by cap_ci_log_tail().
    """
    ci_op = req["payload"].get("ci_op") or {}
    pr = ci_op.get("pr")
    if not pr:
        return _error("missing_pr", "ci_op.pr required: logs are always scoped to one PR")
    requested = ci_op.get("log_tail_lines")
    count = enforcement.cap_ci_log_tail(requested)
    offset = ci_op.get("log_offset", 0)

    code, out = _gh(["pr", "view", str(pr), "--json", "headRefName,headRefOid"])
    if code != 0:
        return _error("pr_lookup_failed", out.strip()[:500])
    try:
        parsed = json.loads(out)
        branch, head = parsed["headRefName"], parsed["headRefOid"]
    except (json.JSONDecodeError, KeyError, TypeError):
        # TypeError covers gh returning a list where an object was expected --
        # which is exactly what the stubbed `gh` in the example replay returns.
        return _error("bad_gh_output", out.strip()[:500])

    run_id = ci_op.get("run_id")
    if not run_id:
        # Pinned to the PR's current head. Without --commit, a branch that has
        # since been fixed still reports its old failure, sending the caller to
        # debug a problem that no longer exists.
        code, out = _gh(["run", "list", "--branch", branch, "--commit", head,
                         "--status", "failure", "--limit", "1", "--json", "databaseId"])
        if code != 0:
            return _error("run_lookup_failed", out.strip()[:500])
        try:
            runs = json.loads(out)
        except json.JSONDecodeError:
            return _error("bad_gh_output", out.strip()[:500])
        if not runs:
            return _ok(result={"pr": pr, "branch": branch, "failed_run": None,
                               "lines": [], "probe": requested is None})
        run_id = runs[0]["databaseId"]

    code, out = _gh(["run", "view", str(run_id), "--log-failed"], timeout_s=60.0)
    lines = [_strip_runner_prefix(l) for l in out.splitlines()]

    if requested is None:
        errors = [(i, l) for i, l in enumerate(lines, start=1) if _ERROR_MARKER in l]
        if errors:
            picked = errors[-count:]
            return _ok(result={
                "pr": pr, "branch": branch, "failed_run": run_id, "probe": True,
                "lines": [l for _, l in picked],
                "error_line": picked[-1][0], "total_lines": len(lines),
            })
        return _ok(result={
            "pr": pr, "branch": branch, "failed_run": run_id, "probe": True,
            "lines": lines[-count:], "error_line": None, "total_lines": len(lines),
        })

    end = len(lines) - offset
    window = lines[max(0, end - count):max(0, end)]
    return _ok(result={"pr": pr, "branch": branch, "failed_run": run_id, "probe": False,
                       "lines": window, "total_lines": len(lines)})


def _strip_runner_prefix(line: str) -> str:
    """Drop the constant '<job>\\t<step>\\t<timestamp>Z ' prefix.

    It is identical on every line and would otherwise consume most of a
    two-line probe budget.
    """
    parts = line.split("\t", 2)
    if len(parts) == 3:
        tail = parts[2]
        head, sep, rest = tail.partition("Z ")
        if sep and head[:4].isdigit():
            return rest
        return tail
    return line


def _remote_only(action: str, workflow: str, req: dict, run_local, describe) -> dict:
    """Run in-process only inside CI; otherwise dispatch the workflow and return."""
    if _in_ci():
        return _ok(result=describe(run_local()))
    ci_op = dict(req["payload"].get("ci_op") or {})
    ci_op.setdefault("workflow", workflow)
    dispatch_req = {"payload": {"ci_op": {**ci_op, "op": "dispatch", "remote_only": True}}}
    result = h_ci_dispatch(dispatch_req)
    if result.get("status") == "ok":
        result["result"]["delegated_action"] = action
        result["result"]["reason"] = "local execution forbidden by execution_policy; dispatched to GitHub Actions"
    return result


def h_ingest(req: dict) -> dict:
    from . import ingest
    return _remote_only(
        "ingest", "ingest.yml", req,
        run_local=lambda: (ingest.build_kb_index(ROOT), ingest.build_graph(ROOT)),
        describe=lambda r: {
            "chunk_count": r[0]["chunk_count"],
            "graph_nodes": len(r[1]["graph"]["nodes"]),
            "graph_edges": len(r[1]["graph"]["edges"]),
        },
    )


def h_manifest(req: dict) -> dict:
    from . import manifest
    return _remote_only(
        "manifest", "manifest.yml", req,
        run_local=lambda: manifest.build_manifest(ROOT),
        describe=lambda m: m["counts"],
    )


def _tool_noop(params: dict):
    """Reference/example tool registration. Replace or add entries as real
    tools (e.g. a Unity batchmode build) are wired in."""
    return {"echo": params}


TOOL_REGISTRY = {
    "noop": _tool_noop,
    # register additional real tool callables here, e.g.:
    # "unity_batch": run_unity_batchmode,
}

DISPATCH = {
    "symbol_lookup": h_symbol_lookup,
    "log_query": h_log_query,
    "command_exec": h_command_exec,
    "tool_call": h_tool_call,
    "retrieve": h_retrieve,
    "query": h_retrieve,
    "write": h_write,
    "delete": h_delete,
    "index": h_index,
    "knowledge_update": h_knowledge_update,
    "context_push": h_context_push,
    "context_pop": h_context_pop,
    "schema_validate": h_schema_validate,
    "daemon_status": h_daemon_status,
    "daemon_start": h_daemon_start,
    "daemon_stop": h_daemon_stop,
    "build": h_build,
    "ci_dispatch": h_ci_dispatch,
    "ci_status": h_ci_status,
    "ci_logs": h_ci_logs,
    "ingest": h_ingest,
    "manifest": h_manifest,
}
