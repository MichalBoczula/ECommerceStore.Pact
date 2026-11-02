#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

stack="pact-smoke-${GITHUB_RUN_ID:-local}-$$"
export PACT_HTTP_PORT=0
export PACT_MONGO_PORT=0
mkdir -p artifacts/container

cleanup() {
  local result=$?
  docker compose -p "$stack" logs --no-color > artifacts/container/compose.log 2>&1 || true
  docker compose -p "$stack" down --volumes --remove-orphans || true
  return "$result"
}
trap cleanup EXIT

docker compose -p "$stack" config --quiet
docker compose -p "$stack" up --build -d
address="$(docker compose -p "$stack" port api 8080)"
base_url="http://$address"
curl --fail --silent --show-error --retry 30 --retry-delay 1 --retry-all-errors \
  --retry-max-time 60 --max-time 5 "$base_url/health/ready" > artifacts/container/ready.json
curl --fail --silent --show-error --max-time 10 "$base_url/health/live" > artifacts/container/live.json
curl --fail --silent --show-error --max-time 10 "$base_url/openapi/v1.json" > artifacts/container/openapi.json
python3 - <<'PY'
import json
from pathlib import Path

folder = Path('artifacts/container')
ready = json.loads((folder / 'ready.json').read_text())
live = json.loads((folder / 'live.json').read_text())
document = json.loads((folder / 'openapi.json').read_text())
assert ready['checks']['mongodb'] == 'Healthy', ready
assert live['status'] == 'Healthy', live
assert document['info']['title'] == 'ECommerceStore Pact Registry', document
print('Container smoke passed: liveness, MongoDB readiness and generated OpenAPI.')
PY
