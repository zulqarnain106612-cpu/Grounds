# Verification strategy

`docs/SDLC_SPIRAL.md` requires every cell to ship something that returns
pass/fail without a human looking at it. This file says *which kind* of
check, and why more than one kind is needed.

The short version: example-based unit tests only ever catch the failures
somebody already imagined. Everything below exists to catch a class of
failure that unit tests structurally cannot.

---

## The layers

| Layer | Lives in | Catches | Cannot catch |
|---|---|---|---|
| Unit | `tests/test_gateway.py`, `test_enforcement.py`, `test_ingest.py`, `Tests/EditMode/JetFlightModelTests.cs` | The cases a person enumerated | Anything nobody enumerated |
| Contract / regression | `tests/test_schema_contract.py` + `tests/golden/schema_contract.json` | The contract silently widening — `additionalProperties` flipping, a `const` decaying, a cap disappearing | Behaviour; it never calls a handler |
| Property | `tests/test_property_invariants.py` | Invariants failing on inputs nobody wrote down | Anything not expressible as a universally-quantified property |
| Metamorphic | `tests/test_metamorphic.py`, `Tests/EditMode/JetFlightAdvancedTests.cs` | Wrong behaviour where *no correct answer can be written down* — ranking, graph shape, flight feel | Absolute correctness; it only relates runs to each other |
| Differential | `JetFlightAdvancedTests.SteppedBankingMatchesTheClosedFormOfItsDecayLaw` | The implementation drifting from an independent oracle | Anything with no independent oracle |
| Fuzz | `JetFlightAdvancedTests`, seeded | NaN, overflow, envelope breaches under hostile input | Wrong-but-plausible values |
| Soak | `Tests/PlayMode/JetFlightSoakTests.cs` | Drift, leaks and accumulation that need thousands of ticks to show | Anything visible in one frame |
| Mutation | `.github/workflows/qa.yml`, `suite=mutation` | Tests that execute a line without asserting anything about it | Code no test runs at all — coverage catches that |
| Coverage | `.coveragerc`, `scripts/check_coverage.py`, `scripts/report_unity_coverage.py` | Code no test runs | Code that runs but is never actually checked — mutation catches that |
| Architecture fitness | `tests/test_architecture.py`, `Tests/EditMode/AssemblyWiringTests.cs` | Structural decay — layering violations, import cycles, a runtime assembly growing an editor dependency | Anything behavioural |
| Integration / seam | `tests/test_pipeline_wiring.py` | Two components that each pass their own tests while disagreeing about a key, a path or a shape | Defects inside a single component |

**Architecture fitness functions** come from *Building Evolutionary
Architectures* (Ford, Parsons, Kua): an automated check on a structural
property rather than on behaviour. They exist because structural decay never
fails a unit test — every individual change that erodes a boundary works
fine, and the bill arrives years later as a codebase nobody can change
safely. `tests/test_architecture.py` derives the import graph with `ast` and
pins the layering; the C# side uses reflection over the loaded assemblies,
where the classic failure is a runtime type acquiring a `UnityEditor`
reference that compiles in the editor and dies at IL2CPP time.

**Integration tests** cover the seams. A wiring defect is invisible to unit
tests by construction: both sides pass in isolation while disagreeing about
the contract between them. `tests/test_pipeline_wiring.py` crosses each seam
with no mock in between — ingest→index→retrieval, scanner→symbol_lookup,
handler→response→schema, stdin→CLI→stdout, and the audit trail.

Coverage and mutation are the pair worth understanding together. Coverage
says a line was *executed*. Mutation says a test would have *noticed it
changing*. A suite can sit at 99% line coverage and kill almost no mutants,
which means it runs the code and asserts nothing useful about it.

---

## Why the schema carries the floors

`schema/agent.schema.json → definitions.TestOp` is not a convenience
wrapper over `pytest`. Each of its constraints removes a way of asking for
a run that cannot fail:

- `min_tests` has `minimum: 1`. Both `pytest` and Unity's test runner exit
  0 when they discover nothing, so a request that tolerates zero tests is a
  request for a green lie. It has no encoding.
- `remote_only: true` and `wait: false` are `const`. "Run the tests on my
  machine" and "block until they finish" cannot be expressed, which is
  `execution_policy` enforced by the shape of the contract rather than by a
  flag somebody could set.
- `mutation` requires `mutation_score_min`; `performance` requires
  `budget_ms`; `regression` requires `baseline`. A score with no floor and
  a timing with no budget are numbers nobody is obliged to act on.
- `property` and `soak` require `seed`. A randomised failure that cannot be
  replayed is an anecdote, not a regression — it can never become a test.

This mirrors what `docs/ENFORCEMENT.md` already does for file reads: the
guarantee comes from the capability being absent, not from a policy flag.

---

## Running one

Through the gateway — dispatches `qa.yml` and returns immediately:

```bash
python3 -m gateway.cli <<< '{"meta":{"schema_version":"1.1.0","session_id":"550e8400-e29b-41d4-a716-446655440000","tick":0,"phase":"1","timestamp_utc":"2026-01-01T00:00:00Z"},"intent":{"action":"test_run","domain":"qa","priority":8},"payload":{"data":null,"test_op":{"suite":"mutation","mutation_score_min":80,"min_tests":1,"remote_only":true,"wait":false}}}'
```

Or directly:

```bash
gh workflow run qa.yml --ref <branch> -f suite=property -f seed=1729
```

Never locally. `config/agent.config.json → execution_policy` forbids it,
and `h_test_run` has no in-process path even inside CI — pytest invoking
itself inherits the outer run's coverage context and attributes the result
to the wrong suite.

---

## Adding a technique

1. Add the suite name to `TestOp.suite`'s enum **and** to
   `handlers._TEST_SUITES` — `tests/test_test_run_handler.py` asserts the
   two lists agree, because nothing else would notice them drifting.
2. Add a `case` to the `Select the suite` step in `.github/workflows/qa.yml`.
3. If the suite produces a number, make the schema require its floor. A
   suite that reports without gating is a dashboard, not a test.
4. Regenerate the contract golden — see `docs/EXTENDING.md`.

## Known gaps

Stated here rather than left implicit, so their absence is a decision:

- **No floor on C# coverage, and until now no number either.**
  `scripts/report_unity_coverage.py` searched the artifacts directory, while
  `game-ci/unity-test-runner` writes its coverage report to a separate
  `CodeCoverage` directory at the workspace root. So the reporter printed *no
  coverage summary was produced* on every run, under a green check, where
  nobody reads it — and the figure has never once been measured. This entry
  previously described the number as reported-but-not-gated; it was not
  reported at all, and the reason it gave (that `QualityTierManager` had no
  testable seam, and that it was one of only three runtime files) was wrong
  on both counts.
  Both roots are searched now and the coverage directory is uploaded with the
  results. **The floor lands in the commit that reads the first real
  figure** — a floor set against a number nobody has seen is decoration.
- **No mutation testing on C#.** Stryker.NET would cover it. Not wired up;
  it needs its own cell.
- **Flight feel is still half-subjective.** The objective half is
  automated above. The rest is a device capture, per
  `docs/PHASE1_TECHNICAL_SPEC.md` §4.
