.PHONY: init validate test daemons-start daemons-stop daemons-status

init:
	bash scripts/install.sh

validate:
	python3 -m gateway.validate_repo

test:
	python3 -m pytest tests/ -q

daemons-start:
	python3 -m gateway.cli <<< '{"meta":{"schema_version":"1.1.0","session_id":"550e8400-e29b-41d4-a716-446655440000","tick":0,"phase":"1","timestamp_utc":"2026-01-01T00:00:00Z"},"intent":{"action":"daemon_start","domain":"daemon","priority":3},"payload":{"data":null,"daemon_op":{"daemon_id":"symbol_indexer"}}}'

daemons-status:
	python3 -m gateway.cli <<< '{"meta":{"schema_version":"1.1.0","session_id":"550e8400-e29b-41d4-a716-446655440000","tick":0,"phase":"1","timestamp_utc":"2026-01-01T00:00:00Z"},"intent":{"action":"daemon_status","domain":"daemon","priority":2},"payload":{"data":null,"daemon_op":{"daemon_id":"symbol_indexer"}}}'
