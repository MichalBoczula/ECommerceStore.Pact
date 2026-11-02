#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

stack="pact-smoke-${GITHUB_RUN_ID:-local}-$$"
export PACT_HTTP_PORT=0
export PACT_MONGO_PORT=0
export PACT_SMOKE_TOKEN="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
export PACT_PUBLISHER_TOKEN_SHA256="$(python3 -c 'import hashlib,os; print(hashlib.sha256(os.environ["PACT_SMOKE_TOKEN"].encode()).hexdigest())')"
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
import base64
import hashlib
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

def call(path, payload=None, authenticate=True):
    headers = {'Content-Type': 'application/json'}
    if authenticate:
        headers['Authorization'] = 'Bearer ' + os.environ['PACT_SMOKE_TOKEN']
    request = Request(base_url + path,
                      data=json.dumps(payload).encode() if payload is not None else None,
                      headers=headers)
    try:
        response = urlopen(request, timeout=10)
    except HTTPError as error:
        response = error
    with response:
        return response.status, response.headers, json.load(response)

path = '/api/applications/payments/versions'
payload = {'commitSha': 'c' * 40, 'repositoryUrl': 'https://github.com/MichalBoczula/ECommerceStorePayments'}
unauthorized, _, _ = call(path, payload, authenticate=False)
assert unauthorized == 401, unauthorized
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
raw = json.dumps({'openapi': '3.0.3', 'info': {'title': 'Smoke – dokument', 'version': '1'}, 'paths': {}}, ensure_ascii=False).encode() + b'\r\n'
publication = {'sourceApplicationId': 'payments', 'commitSha': 'c' * 40, 'kind': 'ProviderOpenApi',
               'sourceRef': 'refs/heads/main', 'buildUrl': payload['repositoryUrl'] + '/actions/runs/1',
               'contentBase64': base64.b64encode(raw).decode()}
status, _, published = call('/api/contract-publications', publication)
assert status == 201 and published['artifactSha256'] == hashlib.sha256(raw).hexdigest(), (status, published)
status, _, repeated = call('/api/contract-publications', publication)
assert status == 200 and repeated == published, (status, repeated)
with urlopen(base_url + published['contentUrl'], timeout=10) as download:
    assert download.read() == raw
status, _, conflict = call('/api/contract-publications', {**publication, 'contentBase64': base64.b64encode(raw + b'\n').decode()})
assert status == 409 and conflict['code'] == 'contract_content_conflict', (status, conflict)
(folder / 'publication.json').write_text(json.dumps(published, indent=2))
# Synthetic report-ingestion smoke only: official provider verification belongs to source CI.
invoice_repo = 'https://github.com/MichalBoczula/ECommerceStoreInvoice'
status, _, _ = call('/api/applications/invoice/versions', {'commitSha': 'd' * 40, 'repositoryUrl': invoice_repo})
assert status == 201, status
pact = {'consumer': {'name': 'payments'}, 'provider': {'name': 'invoice'},
        'interactions': [{'description': 'read an order', 'request': {'method': 'GET', 'path': '/orders/123'},
                          'response': {'status': 200, 'body': {'totalAmount': 12.34}}}],
        'metadata': {'pactSpecification': {'version': '3.0.0'}}}
status, _, consumer = call('/api/contract-publications', {**publication, 'kind': 'ConsumerPact',
    'consumerApplicationId': 'payments', 'providerApplicationId': 'invoice',
    'contentBase64': base64.b64encode(json.dumps(pact).encode()).decode()})
assert status == 201, (status, consumer)
report = {'consumerPublicationId': consumer['id'], 'consumerApplicationId': 'payments', 'consumerCommitSha': 'c' * 40,
          'providerApplicationId': 'invoice', 'providerCommitSha': 'd' * 40, 'contractSha256': consumer['artifactSha256'],
          'verifierName': 'synthetic-smoke', 'verifierVersion': '1', 'configurationSha256': 'e' * 64,
          'ciRunId': '1', 'ciAttempt': 1, 'ciCheckId': 'report-ingestion', 'sourceRef': 'refs/heads/main',
          'buildUrl': invoice_repo + '/actions/runs/1', 'status': 'Failed', 'verifiedInteractions': 0, 'failedInteractions': 1,
          'summary': 'Synthetic missing field', 'issues': [{'kind': 'Mismatch', 'code': 'missing_field', 'message': 'Missing totalAmount',
          'interaction': 'read an order', 'method': 'GET', 'path': '/orders/123', 'jsonPath': '$.body.totalAmount',
          'expected': 12.34, 'actualPresent': False}], 'evidenceUrls': [],
          'startedAt': '2026-01-01T00:00:00Z', 'finishedAt': '2026-01-01T00:00:01Z'}
status, headers, failed = call('/api/verification-runs', report)
assert status == 201 and failed['expectedInteractions'] == 1, (status, failed)
failed_location = headers['Location']
status, _, retry = call('/api/verification-runs', report)
assert status == 200 and retry == failed, (status, retry)
passed_report = {**report, 'status': 'Passed', 'verifiedInteractions': 1, 'failedInteractions': 0, 'issues': [], 'summary': 'Synthetic passed run'}
status, _, conflict = call('/api/verification-runs', passed_report)
assert status == 409 and conflict['code'] == 'verification_execution_conflict', (status, conflict)
status, _, passed = call('/api/verification-runs', {**passed_report, 'ciAttempt': 2})
assert status == 201 and passed['id'] != failed['id'], (status, passed)
status, _, historical = call(failed_location, authenticate=False)
assert status == 200 and historical == failed, (status, historical)
status, _, page = call('/api/verification-runs?consumerApplicationId=payments&providerApplicationId=invoice&contractSha256=' + consumer['artifactSha256'], authenticate=False)
assert status == 200 and len(page['items']) == 2, (status, page)
(folder / 'verification-runs.json').write_text(json.dumps(page, indent=2))
print('Container smoke passed: protected writes, immutable publication, exact-byte download and append-only report history.')
PY
