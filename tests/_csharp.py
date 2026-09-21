"""Reading C# sources the way an assertion should read them.

Several checks in this suite are "this file must not mention X" -- no joint,
no reference to the constraint from the flight model. Matching the raw text
makes them fail on the comment that *explains why X is absent*, which is
backwards: the explanation is the thing you most want people to write.

These strip comments so the assertions look at code only.
"""
from __future__ import annotations

import re
from pathlib import Path

_BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.DOTALL)
_LINE_COMMENT = re.compile(r"//.*$", re.MULTILINE)


def code(path: Path) -> str:
    """Source with block and line comments removed.

    String literals containing `//` would be mangled by this, which is fine
    for the absence checks it serves and worth knowing before reusing it for
    anything that parses.
    """
    return _LINE_COMMENT.sub("", _BLOCK_COMMENT.sub("", path.read_text()))


def code_lines(path: Path) -> list[str]:
    return [line for line in code(path).splitlines() if line.strip()]


def mentions(path: Path, needle: str) -> bool:
    """True when `needle` appears outside comments."""
    return needle in code(path)
