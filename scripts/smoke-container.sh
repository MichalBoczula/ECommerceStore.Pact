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
PACT_SMOKE_URL="$base_url" python3 - <<'PY'
import json
import os
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen

folder = Path('artifacts/container')
ready = json.loads((folder / 'ready.json').read_text())
live = json.loads((folder / 'live.json').read_text())
document = json.loads((folder / 'openapi.json').read_text())
assert ready['checks']['mongodb'] == 'Healthy', ready
assert live['status'] == 'Healthy', live
assert document['info']['title'] == 'ECommerceStore Pact Registry', document
base_url = os.environ['PACT_SMOKE_URL']

def call(path, payload=None):
    request = Request(base_url + path,
                      data=json.dumps(payload).encode() if payload is not None else None,
                      headers={'Content-Type': 'application/json'})
    try:
        response = urlopen(request, timeout=10)
    except HTTPError as error:
        response = error
    with response:
        return response.status, response.headers, json.load(response)

path = '/api/applications/payments/versions'
payload = {'commitSha': 'c' * 40, 'repositoryUrl': 'https://github.com/MichalBoczula/ECommerceStorePayments'}
status, headers, version = call(path, payload)
assert status == 201, (status, version)
read_status, _, read = call(headers['Location'])
assert read_status == 200 and read == version, (read_status, read)
retry_status, _, retry = call(path, payload)
assert retry_status == 200 and retry == version, (retry_status, retry)
conflict_status, _, conflict = call(path, {**payload, 'repositoryUrl': 'https://github.com/other/repo'})
assert conflict_status == 409 and conflict['code'] == 'application_version_metadata_conflict', conflict
status, headers, integration = call('/api/integrations', {'consumerApplicationId': 'payments', 'providerApplicationId': 'invoice'})
assert status == 201, (status, integration)
read_status, _, read = call(headers['Location'])
assert read_status == 200 and read == integration, (read_status, read)
(folder / 'registration.json').write_text(json.dumps({'version': version, 'integration': integration}, indent=2))
print('Container smoke passed: health, OpenAPI, version registration/retry/conflict and integration persistence.')
PY
