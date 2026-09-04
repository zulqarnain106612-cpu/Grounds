"""JetFighter Agent Gateway.

Real enforcement layer. Every agent request/response is a JSON object
validated against schema/agent.schema.json. There is no code path in this
package that returns raw file/code text to a caller: FileOp has no 'read'
op, command output is always truncated, and log output is always
pre-filtered. See docs/ENFORCEMENT.md for the exact guarantees and their
limits.
"""
__version__ = "1.1.0"
