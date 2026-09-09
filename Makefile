REF ?= main

.PHONY: init validate test ingest manifest review retrieval-verify ci-status \
        ci-logs daemons-start daemons-stop daemons-status

init:
	bash scripts/install.sh

# EXECUTION POLICY: validate/test/ingest/manifest/review never run locally
# (config/agent.config.json -> execution_policy). These targets dispatch the
# corresponding GitHub Actions workflow and return immediately -- they never
# block waiting for a run to finish.

validate:
	gh workflow run enforce.yml --ref $(REF)
	@echo "dispatched enforce.yml on $(REF); check with 'make ci-status'"

test:
	gh workflow run enforce.yml --ref $(REF)
	@echo "dispatched enforce.yml on $(REF); check with 'make ci-status'"

ingest:
	gh workflow run ingest.yml --ref $(REF)
	@echo "dispatched ingest.yml on $(REF); check with 'make ci-status'"

manifest:
	gh workflow run manifest.yml --ref $(REF)
	@echo "dispatched manifest.yml on $(REF); check with 'make ci-status'"

review:
	gh workflow run review.yml --ref $(REF)
	@echo "dispatched review.yml on $(REF); check with 'make ci-status'"

retrieval-verify:
	gh workflow run retrieval-verify.yml --ref $(REF)
	@echo "dispatched retrieval-verify.yml on $(REF); check with 'make ci-status'"

# PR-scoped by policy: a repo-wide run listing is not available here. The old
# target ran `gh run list` from inside make, which the permission layer never
# sees, so it was a bypass of the deny rules in .claude/settings.json.
ci-status:
	@test -n "$(PR)" || { echo "usage: make ci-status PR=<pr-number>"; exit 1; }
	./scripts/pr_status.sh $(PR)

# No LINES => the fixed 2-line error probe. With LINES => exactly that many.
ci-logs:
	@test -n "$(PR)" || { echo "usage: make ci-logs PR=<pr-number> [LINES=n] [OFFSET=n]"; exit 1; }
	./scripts/pr_failed_log_tail.sh $(PR) $(LINES) $(OFFSET)

daemons-start:
	python3 -m gateway.cli <<< '{"meta":{"schema_version":"1.1.0","session_id":"550e8400-e29b-41d4-a716-446655440000","tick":0,"phase":"1","timestamp_utc":"2026-01-01T00:00:00Z"},"intent":{"action":"daemon_start","domain":"daemon","priority":3},"payload":{"data":null,"daemon_op":{"daemon_id":"symbol_indexer"}}}'

daemons-status:
	python3 -m gateway.cli <<< '{"meta":{"schema_version":"1.1.0","session_id":"550e8400-e29b-41d4-a716-446655440000","tick":0,"phase":"1","timestamp_utc":"2026-01-01T00:00:00Z"},"intent":{"action":"daemon_status","domain":"daemon","priority":2},"payload":{"data":null,"daemon_op":{"daemon_id":"symbol_indexer"}}}'
