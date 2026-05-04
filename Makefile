SHELL := /bin/bash

.PHONY: verify-observability

## verify-observability: Start the OTel/Prometheus/Grafana stack, poll readiness, then tear down.
verify-observability:
	@set -euo pipefail; \
	trap 'docker compose -p frigaterelay-observability -f docker/observability/docker-compose.yml down -v >/dev/null 2>&1 || true' EXIT INT TERM; \
	docker compose -p frigaterelay-observability -f docker/observability/docker-compose.yml up -d; \
	echo "Waiting for Prometheus..."; \
	for i in $$(seq 1 30); do \
	  if curl -fsS http://localhost:9090/-/ready >/dev/null 2>&1; then break; fi; \
	  sleep 2; \
	  if [ $$i -eq 30 ]; then echo "ERROR: Prometheus did not become ready after 60s"; exit 1; fi; \
	done; \
	echo "Waiting for Grafana..."; \
	for i in $$(seq 1 30); do \
	  if curl -fsS http://localhost:3000/api/health >/dev/null 2>&1; then break; fi; \
	  sleep 2; \
	  if [ $$i -eq 30 ]; then echo "ERROR: Grafana did not become ready after 60s"; exit 1; fi; \
	done; \
	echo "verify-observability OK -- Prometheus on :9090, Grafana on :3000"
