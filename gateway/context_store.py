from __future__ import annotations
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
STATE_DIR = ROOT / "daemons" / "state"
STATE_DIR.mkdir(parents=True, exist_ok=True)


def _stack_path(session_id: str) -> Path:
    return STATE_DIR / f"context.{session_id}.json"


def push(session_id: str, frame) -> list:
    path = _stack_path(session_id)
    stack = json.loads(path.read_text()) if path.exists() else []
    stack.append(frame)
    path.write_text(json.dumps(stack, indent=2))
    return stack


def pop(session_id: str):
    path = _stack_path(session_id)
    if not path.exists():
        return None, []
    stack = json.loads(path.read_text())
    frame = stack.pop() if stack else None
    path.write_text(json.dumps(stack, indent=2))
    return frame, stack
