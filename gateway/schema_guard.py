from __future__ import annotations
import json
from pathlib import Path
from functools import lru_cache
from jsonschema import Draft7Validator, exceptions as js_exceptions

ROOT = Path(__file__).resolve().parent.parent
SCHEMA_PATH = ROOT / "schema" / "agent.schema.json"


class SchemaLoadError(Exception):
    pass


class RequestValidationError(Exception):
    def __init__(self, errors):
        self.errors = errors
        super().__init__("; ".join(errors))


@lru_cache(maxsize=1)
def load_schema() -> dict:
    if not SCHEMA_PATH.exists():
        raise SchemaLoadError(f"schema not found at {SCHEMA_PATH}")
    schema = json.loads(SCHEMA_PATH.read_text())
    try:
        Draft7Validator.check_schema(schema)
    except js_exceptions.SchemaError as e:
        raise SchemaLoadError(f"agent.schema.json is not a valid Draft-07 schema: {e.message}") from e
    return schema


@lru_cache(maxsize=1)
def validator() -> Draft7Validator:
    return Draft7Validator(load_schema())


def validate(instance: dict) -> None:
    """Raise RequestValidationError if instance does not conform to the schema."""
    errs = sorted(validator().iter_errors(instance), key=lambda e: list(e.path))
    if errs:
        raise RequestValidationError(
            [f"{'/'.join(str(p) for p in e.path) or '<root>'}: {e.message}" for e in errs]
        )


def is_valid(instance: dict) -> bool:
    try:
        validate(instance)
        return True
    except RequestValidationError:
        return False
