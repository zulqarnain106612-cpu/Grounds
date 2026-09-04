from __future__ import annotations
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
}
