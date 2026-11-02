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
if [[ "${PACT_IMAGE_PREBUILT:-false}" == true ]]; then
  docker compose -p "$stack" pull mongodb
  docker compose -p "$stack" up --no-build --pull never -d
else
  docker compose -p "$stack" up --build -d
fi
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
# Compare explicit provider publications using the real pinned binary in the API image.
provider_base = {'openapi': '3.0.3', 'info': {'title': 'Orders', 'version': '1'}, 'paths': {'/orders': {'get': {
    'responses': {'200': {'description': 'OK', 'content': {'application/json': {'schema': {
        'type': 'object', 'required': ['totalAmount'], 'properties': {'totalAmount': {'type': 'number'}}}}}}}}}}}
def publish_provider(sha, spec):
    status, _, result = call('/api/contract-publications', {'sourceApplicationId': 'invoice', 'commitSha': sha, 'kind': 'ProviderOpenApi',
        'sourceRef': 'refs/heads/main', 'buildUrl': invoice_repo + '/actions/runs/1',
        'contentBase64': base64.b64encode(json.dumps(spec).encode()).decode()})
    assert status == 201, (status, result)
    return result
before = publish_provider('d' * 40, provider_base)
status, _, _ = call('/api/applications/invoice/versions', {'commitSha': 'f' * 40, 'repositoryUrl': invoice_repo})
assert status == 201, status
provider_after = json.loads(json.dumps(provider_base))
schema = provider_after['paths']['/orders']['get']['responses']['200']['content']['application/json']['schema']
schema['properties'].pop('totalAmount')
schema['required'] = []
after = publish_provider('f' * 40, provider_after)
comparison_request = {'sourceApplicationId': 'invoice', 'consumerApplicationId': 'payments', 'providerApplicationId': 'invoice',
    'beforePublicationId': before['id'], 'afterPublicationId': after['id'], 'baselineRef': 'refs/heads/main'}
status, headers, comparison = call('/api/contract-comparisons', comparison_request)
assert status == 201 and comparison['schemaAssessment'] == 'PotentiallyBreaking', (status, comparison)
assert any(item['code'] == 'response-required-property-removed' for item in comparison['findings']), comparison
assert comparison['toolVersion'] == '1.35.0' and comparison['consumerImpact'] == 'NotEvaluated', comparison
comparison_location = headers['Location']
status, _, retry = call('/api/contract-comparisons', comparison_request)
assert status == 200 and retry == comparison, (status, retry)
status, _, read = call(comparison_location, authenticate=False)
assert status == 200 and read == comparison, (status, read)
status, _, history = call('/api/issue-history?beforeRunId=' + failed['id'] + '&afterRunId=' + passed['id'] + '&baselineRef=refs/heads/main', authenticate=False)
assert status == 200 and history['comparable'] and history['issues'][0]['transition'] == 'Resolved', (status, history)
assert history['before']['status'] == 'Failed' and history['after']['status'] == 'Passed', history
(folder / 'comparison.json').write_text(json.dumps(comparison, indent=2))
(folder / 'issue-history.json').write_text(json.dumps(history, indent=2))
# The caller pins all required checks and an exact CI execution for each version pair.
profile_request = {'consumerApplicationId': 'payments', 'providerApplicationId': 'invoice', 'name': 'smoke-required', 'version': '1',
    'checks': [{'checkId': 'report-ingestion', 'verifierName': 'synthetic-smoke', 'verifierVersion': '1', 'configurationSha256': 'e' * 64}]}
status, _, profile = call('/api/verification-profiles', profile_request)
assert status == 201, (status, profile)
status, _, repeated = call('/api/verification-profiles', profile_request)
assert status == 200 and repeated == profile, (status, repeated)
row = {'consumerCommitSha': 'c' * 40, 'providerCommitSha': 'd' * 40, 'contractSha256': consumer['artifactSha256'],
       'ciRunId': '1', 'ciAttempt': 1, 'providerSourceRef': 'refs/heads/main'}
decision_request = {'sourceApplicationId': 'invoice', 'profileId': profile['id'], 'rows': [row]}
status, headers, failed_decision = call('/api/compatibility-decisions', decision_request)
assert status == 201 and failed_decision['status'] == 'Failed' and not failed_decision['compatible'], (status, failed_decision)
failed_decision_location = headers['Location']
assert failed_decision['rows'][0]['checks'][0]['evidenceId'] == failed['id'], failed_decision
status, _, retried = call('/api/compatibility-decisions', decision_request)
assert status == 200 and retried == failed_decision, (status, retried)
status, _, passed_decision = call('/api/compatibility-decisions', {**decision_request, 'rows': [{**row, 'ciAttempt': 2}]})
assert status == 201 and passed_decision['status'] == 'Passed' and passed_decision['compatible'], (status, passed_decision)
status, _, missing_decision = call('/api/compatibility-decisions', {**decision_request, 'rows': [{**row, 'providerCommitSha': 'f' * 40}]})
assert status == 201 and missing_decision['status'] == 'Unverified' and not missing_decision['compatible'], (status, missing_decision)
assert missing_decision['rows'][0]['checks'][0]['reason'] == 'MissingExecution', missing_decision
status, _, original = call(failed_decision_location, authenticate=False)
assert status == 200 and original == failed_decision, (status, original)
assert failed_decision['policy'] == 'explicit-ci-execution-v1', failed_decision
(folder / 'compatibility-decisions.json').write_text(json.dumps({'failed': failed_decision, 'passed': passed_decision, 'missing': missing_decision}, indent=2))
print('Container smoke passed: immutable contracts/reports, schema comparison, issue history and explicit-version compatibility decisions.')
PY

PACT_PUBLISHER_TOKEN="$PACT_SMOKE_TOKEN" python3 scripts/verify-deployment.py --url "$base_url" --application payments --allow-local-http > artifacts/container/deployment-verification.json
