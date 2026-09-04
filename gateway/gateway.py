from __future__ import annotations
import json
from datetime import datetime, timezone
from pathlib import Path

from . import schema_guard, handlers

ROOT = Path(__file__).resolve().parent.parent
AUDIT_LOG = ROOT / "logs" / "agent.jsonl"


def _audit(request: dict, response: dict) -> None:
    entry = {
        "ts": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "action": request.get("intent", {}).get("action"),
        "domain": request.get("intent", {}).get("domain"),
        "session_id": request.get("meta", {}).get("session_id"),
        "response_status": response.get("response", {}).get("status"),
    }
    AUDIT_LOG.parent.mkdir(parents=True, exist_ok=True)
    with open(AUDIT_LOG, "a") as f:
        f.write(json.dumps(entry) + "\n")


def handle(raw_request) -> dict:
    """The only supported way to talk to this repo's agent surface.

    1. Reject anything that is not a JSON object matching agent.schema.json.
    2. Dispatch to the handler registered for intent.action.
    3. Validate the handler's response shape before returning it.
    4. Append an audit line (never containing raw file/code content).
    """
    if isinstance(raw_request, (str, bytes)):
        try:
            request = json.loads(raw_request)
        except json.JSONDecodeError as e:
            return {"response": {"status": "error", "errors": [
                {"code": "invalid_json", "message": str(e), "fatal": True}
            ]}}
    else:
        request = raw_request

    try:
        schema_guard.validate(request)
    except schema_guard.RequestValidationError as e:
        return {"response": {"status": "error", "errors": [
            {"code": "schema_violation", "message": m, "fatal": True} for m in e.errors
        ]}}

    action = request["intent"]["action"]
    handler = handlers.DISPATCH.get(action)
    if handler is None:
        result = {"status": "error", "errors": [
            {"code": "no_handler", "message": f"action '{action}' has no registered handler", "fatal": True}
        ]}
    else:
        try:
            result = handler(request)
        except Exception as e:  # noqa: BLE001 - convert any bug into a schema-valid error response
            result = {"status": "error", "errors": [
                {"code": "handler_exception", "message": f"{type(e).__name__}: {e}", "fatal": True}
            ]}

    full_response = dict(request)
    full_response["response"] = result

    try:
        schema_guard.validate(full_response)
    except schema_guard.RequestValidationError as e:
        # A handler produced a shape the schema forbids -- surface that as
        # the failure instead of ever returning unvalidated output.
        full_response["response"] = {"status": "error", "errors": [
            {"code": "response_schema_violation", "message": m, "fatal": True} for m in e.errors
        ]}

    _audit(request, full_response)
    return full_response
