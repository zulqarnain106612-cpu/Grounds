"""Architecture fitness functions: tests over the shape of the system.

The technique is from *Building Evolutionary Architectures* (Ford, Parsons,
Kua). A fitness function is an automated check on a structural property --
layering, coupling, acyclicity -- rather than on behaviour. It exists because
structural decay never fails a unit test: every individual change that erodes
a boundary works fine, and the cost only shows up years later as a codebase
nobody can change safely.

This file pins the boundaries the gateway already has, so the next import
that crosses one fails a build instead of quietly becoming precedent.

Everything here is derived from the source with `ast`, never from a
hand-maintained list, so a module added tomorrow is governed the day it
lands.
"""
from __future__ import annotations

import ast
import json
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent
GATEWAY = ROOT / "gateway"
WORKFLOWS = ROOT / ".github" / "workflows"

# The layering the gateway already follows. Lower numbers are more primitive:
# a module may import from a strictly lower layer, never from its own layer's
# peers upward and never from a higher one.
#
#   0  leaves       no internal dependencies at all
#   1  guards       a leaf plus the policy it enforces
#   2  composition  wires the leaves and guards into handlers
#   3  boundary     the single validated entry point
#   4  entrypoints  what a human or CI actually invokes
LAYERS = {
    "context_store": 0, "daemon_manager": 0, "enforcement": 0, "ingest": 0,
    "kb_store": 0, "manifest": 0, "schema_guard": 0, "symbol_scanner": 0,
    "log_guard": 1,
    "handlers": 2,
    "gateway": 3,
    "cli": 4, "verify_retrieval": 4, "docgen": 4, "validate_repo": 4,
}


def _modules() -> dict[str, Path]:
    return {p.stem: p for p in GATEWAY.glob("*.py") if p.stem != "__init__"}


def _internal_imports(path: Path) -> set[str]:
    """Sibling gateway modules this file imports, however it spells it."""
    known = set(_modules())
    tree = ast.parse(path.read_text())
    found: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.ImportFrom):
            # `from . import kb_store, log_guard`
            if node.level and not node.module:
                found |= {a.name for a in node.names if a.name in known}
            # `from .kb_store import x` / `from gateway.kb_store import x`
            elif node.module:
                head = node.module.split(".")[-1]
                if head in known:
                    found.add(head)
        elif isinstance(node, ast.Import):
            for alias in node.names:
                head = alias.name.split(".")[-1]
                if alias.name.startswith("gateway") and head in known:
                    found.add(head)
    return found - {path.stem}


def _graph() -> dict[str, set[str]]:
    return {name: _internal_imports(path) for name, path in _modules().items()}


# --- layering --------------------------------------------------------------

def test_every_gateway_module_is_assigned_a_layer():
    """A new module must be placed in the hierarchy deliberately. Defaulting
    it to some layer would let it sit wherever its first import happened to
    put it, which is how a layering stops being one."""
    unassigned = sorted(set(_modules()) - set(LAYERS))
    assert not unassigned, (
        f"gateway modules with no layer: {unassigned}. Add them to LAYERS "
        f"after deciding where they belong, not before."
    )


@pytest.mark.parametrize("module", sorted(_modules()))
def test_no_module_imports_from_its_own_layer_or_above(module):
    """The rule that keeps the dependency direction readable. `handlers` may
    use `kb_store`; `kb_store` reaching back for `handlers` would make the
    two inseparable and neither testable alone."""
    if module not in LAYERS:
        pytest.skip("covered by test_every_gateway_module_is_assigned_a_layer")
    offenders = sorted(
        dep for dep in _graph()[module]
        if dep in LAYERS and LAYERS[dep] >= LAYERS[module]
    )
    assert not offenders, (
        f"{module} (layer {LAYERS[module]}) imports {offenders} at the same "
        f"layer or above"
    )


def test_the_import_graph_is_acyclic():
    """A cycle means the two modules are one module wearing two filenames."""
    graph = _graph()
    state: dict[str, int] = {}
    cycles: list[list[str]] = []

    def visit(node: str, stack: list[str]) -> None:
        if state.get(node) == 2:
            return
        if state.get(node) == 1:
            cycles.append(stack[stack.index(node):] + [node])
            return
        state[node] = 1
        for dep in sorted(graph.get(node, ())):
            visit(dep, stack + [dep])
        state[node] = 2

    for node in sorted(graph):
        visit(node, [node])
    assert not cycles, f"import cycles: {cycles}"


def test_the_leaf_layer_really_has_no_internal_dependencies():
    """Layer 0 is what makes the rest testable in isolation. If a leaf grows
    a dependency, every test that used it as a fixture now drags that in."""
    offenders = {
        name: sorted(deps)
        for name, deps in _graph().items()
        if LAYERS.get(name) == 0 and deps
    }
    assert not offenders, f"layer-0 modules with internal imports: {offenders}"


def test_only_the_boundary_and_its_tooling_know_about_dispatch():
    """`DISPATCH` is the routing table. Code that reaches into it is routing
    around the validated entry point in `gateway.handle`, which is where the
    schema check lives -- so anything dispatching directly is unvalidated by
    construction."""
    allowed = {"handlers", "gateway", "docgen", "validate_repo"}

    def touches_dispatch(path: Path) -> bool:
        # Parsed, not grepped: gateway/ingest.py mentions DISPATCH inside a
        # docstring it builds for the KB, and a substring match would read
        # that prose as a dependency.
        for node in ast.walk(ast.parse(path.read_text())):
            if isinstance(node, ast.Attribute) and node.attr == "DISPATCH":
                return True
            if isinstance(node, ast.ImportFrom) and any(a.name == "DISPATCH" for a in node.names):
                return True
            if isinstance(node, ast.Name) and node.id == "DISPATCH":
                return True
        return False

    offenders = [
        name for name, path in _modules().items()
        if name not in allowed and touches_dispatch(path)
    ]
    assert not offenders, f"modules touching DISPATCH outside the boundary: {offenders}"


def test_production_code_never_imports_the_test_suite():
    offenders = [
        name for name, path in _modules().items()
        if any(
            isinstance(n, (ast.Import, ast.ImportFrom))
            and "tests" in (getattr(n, "module", "") or "").split(".")
            for n in ast.walk(ast.parse(path.read_text()))
        )
    ]
    assert not offenders, f"gateway modules importing tests: {offenders}"


# --- wiring between the repo's moving parts --------------------------------

def _workflow_names() -> set[str]:
    return {p.name for p in WORKFLOWS.glob("*.yml")}


def test_every_workflow_a_handler_dispatches_actually_exists():
    """The seam that breaks silently. `h_test_run` dispatches `qa.yml` by
    name; a rename leaves valid Python that fails only at runtime, on CI,
    as an opaque `gh` error."""
    source = (GATEWAY / "handlers.py").read_text()
    referenced = {
        token.strip("\"'")
        for token in source.replace('"', " ").replace("'", " ").split()
        if token.endswith(".yml")
    }
    missing = sorted(referenced - _workflow_names())
    assert referenced, "no workflow is referenced from handlers.py at all"
    assert not missing, f"handlers.py dispatches workflows that do not exist: {missing}"


def test_every_script_a_workflow_invokes_actually_exists():
    """Same seam, other direction: a workflow naming a script that was moved
    or renamed is green until the day that step runs."""
    missing = []
    for workflow in sorted(WORKFLOWS.glob("*.yml")):
        for token in workflow.read_text().replace('"', " ").split():
            candidate = token.strip("'\"`")
            if candidate.startswith("scripts/") and candidate.endswith(".py"):
                if not (ROOT / candidate).exists():
                    missing.append(f"{workflow.name} -> {candidate}")
    assert not missing, f"workflows invoking missing scripts: {missing}"


def test_the_makefile_only_dispatches_workflows_that_exist():
    missing = [
        token for token in (ROOT / "Makefile").read_text().split()
        if token.endswith(".yml") and token not in _workflow_names()
    ]
    assert not missing, f"Makefile dispatches missing workflows: {missing}"


def test_every_configured_source_glob_matches_something():
    """`source_globs` drives the symbol index. A glob that matches nothing
    makes `symbol_lookup` silently return nothing for every query -- the
    exact failure docs/CYCLE0_BOOTSTRAP_SPEC.md section 0 was written about."""
    config = json.loads((ROOT / "config" / "agent.config.json").read_text())
    for glob in config["source_globs"]:
        assert list(ROOT.glob(glob)), f"source glob matches nothing: {glob}"


def test_every_daemon_action_resolves_to_a_real_callable():
    """`registry.json` addresses each daemon as "module:function". Both halves
    are resolved here: a typo in either is a daemon that registers fine and
    fails only when something tries to start it."""
    import importlib

    registry = json.loads((ROOT / "daemons" / "registry.json").read_text())
    daemons = registry["daemons"]
    assert daemons, "the registry is empty; this assertion would be vacuous"

    for daemon_id, entry in daemons.items():
        action = entry.get("action", "")
        assert ":" in action, f"daemon '{daemon_id}' has no module:function action"
        module_path, _, function_name = action.partition(":")
        module = importlib.import_module(module_path)
        assert hasattr(module, function_name), (
            f"daemon '{daemon_id}' names {action}, but {module_path} has no {function_name}"
        )


def test_every_retrieval_strategy_is_one_the_schema_allows():
    """strategies.json and the schema enum are two lists describing the same
    set. Nothing but this notices them drifting apart."""
    schema = json.loads((ROOT / "schema" / "agent.schema.json").read_text())
    allowed = set(schema["definitions"]["RetrievalContext"]["properties"]["strategy"]["enum"])
    declared = set(json.loads((ROOT / "retrieval" / "strategies.json").read_text())["strategies"])
    assert declared, "strategies.json is empty; this assertion would be vacuous"

    unknown = sorted(declared - allowed)
    assert not unknown, f"retrieval/strategies.json declares strategies the schema rejects: {unknown}"

    undeclared = sorted(allowed - declared)
    assert not undeclared, (
        f"the schema accepts strategies strategies.json never configures: {undeclared} -- "
        f"a caller can request one and get whatever the fallback happens to be"
    )
