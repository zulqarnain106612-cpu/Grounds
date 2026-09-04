from __future__ import annotations
import json
import re
from functools import lru_cache
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CONFIG_PATH = ROOT / "config" / "agent.config.json"
PATTERNS_PATH = ROOT / "tools" / "patterns.json"


class ForbiddenCommandError(Exception):
    pass


class CapExceededError(Exception):
    pass


@lru_cache(maxsize=1)
def config() -> dict:
    return json.loads(CONFIG_PATH.read_text())


@lru_cache(maxsize=1)
def forbidden_patterns() -> list[re.Pattern]:
    data = json.loads(PATTERNS_PATH.read_text())
    return [re.compile(p, re.IGNORECASE) for p in data.get("forbidden_cmd_regex", [])]


def assert_command_allowed(cmd: str, args: list[str] | None = None) -> None:
    """Raise ForbiddenCommandError if cmd (+args) matches a blocked pattern.

    Defense-in-depth only. The primary guarantee is that command output is
    always truncated (see truncate_lines) regardless of what a command
    would otherwise print.
    """
    full = cmd + " " + " ".join(args or [])
    for pattern in forbidden_patterns():
        if pattern.search(full):
            raise ForbiddenCommandError(
                f"command matches forbidden pattern '{pattern.pattern}': {full.strip()}"
            )


def truncate_lines(text: str, max_lines: int) -> tuple[str, bool]:
    """Return (truncated_text, was_truncated). Always enforced server-side."""
    lines = text.splitlines()
    if len(lines) <= max_lines:
        return text, False
    kept = lines[-max_lines:]
    return "\n".join(kept), True


def cap_output_lines(requested: int | None) -> int:
    hard_cap = config()["enforcement"]["max_command_output_lines"]
    if requested is None:
        return hard_cap
    return min(requested, hard_cap)


def cap_log_tail(requested: int | None) -> int:
    hard_cap = config()["enforcement"]["max_log_tail_lines"]
    if requested is None:
        return hard_cap
    return min(requested, hard_cap)


def cap_tool_output_tokens(requested: int | None) -> int:
    hard_cap = config()["enforcement"]["max_tool_output_tokens"]
    if requested is None:
        return hard_cap
    return min(requested, hard_cap)
