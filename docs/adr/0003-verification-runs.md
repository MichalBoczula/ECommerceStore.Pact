# ADR-0003: Append-only provider verification reports

Status: Accepted
Backlog: PACT/04

## Context

One consumer Pact can fail against one provider commit and pass against another. A CI retry
must not duplicate an execution, and a rerun must not erase a failure. Infrastructure failure
must remain distinct from observed contract incompatibility.

## Decision

- POST `/api/verification-runs` accepts normalized reports from authorized provider CI. API
  DTOs are distinct from immutable domain values and explicit MongoDB documents/mapping.
- Resolve the consumer publication, binding and exact content hash. It must be ConsumerPact,
  bound to the stated consumer commit and logical provider. Both immutable versions must be
  registered. Provider OpenAPI is never accepted as consumer verification evidence.
- Persist version IDs and full application/commit identities, publication/binding IDs, exact
  Pact hash, verifier name/version/configuration hash, CI run/attempt/check and provider
  ref/build provenance. Configuration is hashed by CI; secrets/config contents are not stored.
- `verification_runs` has a unique `uq_verification_execution` index. A versioned SHA-256
  execution key covers consumer version ID, provider version ID, binding, Pact hash and
  CI run + attempt + check. Another provider version, run, attempt or check is another record.
  Run/check IDs are opaque and must be stable and unique within the provider CI namespace.
- Atomic `$setOnInsert` returns the winning immutable record across hosts. A separately
  versioned payload fingerprint detects any changed report under that execution identity:
  return 409 `verification_execution_conflict`, never update the winner. The publication ID,
  verifier/configuration, status, diagnostics, counts, evidence, ref/build URL, summary and
  original execution timestamps all belong to the immutable report. There is no update/delete API.
- UUID and recorded-at time are server-assigned; execution timestamps are caller-supplied,
  ordered and normalized to UTC milliseconds. Retries must retain original start/end times.
  The authenticated publisher and server recording time are excluded from the fingerprint:
  another provider-authorized credential may retry after rotation, returning the first
  publisher/ID/time unchanged. Publisher identity is never accepted from the body.
- Issue/evidence order does not change equivalence, but the originally stored order remains.
  Diagnostic JSON objects are canonicalized by ordinal property name; arrays retain order,
  and numbers retain their original precision/lexical form (1 and 1.0 are distinct).
  Duplicate JSON property names are rejected. Stable fingerprint fields are explicit and
  have a format version, independent of future DTO additions.

## Status and diagnostics

The expected interaction total comes from the bound Pact, including a BOM if present only
for byte hashing/storage. Parsing coverage metadata does not implement Pact matching.
`verifiedInteractions` is the successful count; `failedInteractions` is the mismatched count.
Both are nonnegative and their sum cannot exceed the expected total.

| Status | Required evidence consistency |
| --- | --- |
| Passed | All expected interactions succeeded; zero failures and no issues. |
| Failed | At least one failed interaction and Mismatch issue; no execution errors. |
| Error | At least one ExecutionError; partial successes/mismatches may remain. No compatibility conclusion is inferred. |
| Unverified | Zero completed interactions and no issues; summary explains why execution was not attempted. |

Any positive failed count requires mismatch diagnostics and vice versa. A mismatch records
code/message, a description present in the Pact, HTTP method/path, JSON path and expected
JSON value. `actualPresent: false` plus omitted actual means missing; true plus JSON null
means an explicitly present null. ExecutionError diagnostics may omit interaction/operation
and expected/actual data for failures such as provider startup or verifier infrastructure.
Methods/paths are diagnostic context, not independently matched against Pact generators.

All reports require a summary. At most 100 issues and 10 evidence links are accepted, with
8 KiB/depth-32 expected/actual fragments and a streamed 1 MiB UTF-8 JSON envelope (including
unknown Content-Length). HTTPS evidence URLs allow fragments, not credentials/query strings.
Unknown request properties and numeric enum values are rejected. Invalid data yields 400,
missing references 404, oversize 413 and unavailable storage 503 with safe ProblemDetails.

## Access and retrieval

All POSTs require the existing opaque Bearer publisher credential; verification ingestion
requires scope for the provider. A consumer token cannot declare that its provider passed.
Ref/build provenance is validated against the provider's registered repository. This is
trusted-publisher provenance, not signed attestation or a check that the workflow really ran.
Use synthetic data suitable for the existing public-read model, including expected/actual data.
Official Pact execution, source-repository CI gates and attestation are separate later work.

GET by ID returns the original report/diagnostics. GET collection requires explicit consumer,
provider and Pact hash, with limit 1-100 (default 50), an optional `after` ID and `nextCursor`.
An integration/hash/ID index supports bounded ascending-ID reads across all provider versions
and attempts. UUID ordering is deterministic traversal, not chronological ranking or evidence
selection. Concurrent inserts may sort behind a cursor; restart traversal to include them.
There is no unqualified latest endpoint or compatibility decision; PACT/06 defines that policy.

## Verification and consequences

Tests cover immutable failures across provider versions, all statuses, original diagnostics,
missing vs null, JSON precision, invalid references, provider ownership, cross-host races,
credential/restart retries, changed-report conflicts, reruns, bounded requests/pagination and
corrupt stored-record handling. Container smoke submits synthetic reports; it does not claim
to execute a real provider verifier. Real Payments/Invoice execution remains PACT/08-09.
Schema changes/issue resolution are PACT/05; explicit-version decision policy is PACT/06.

Official context: [Pact provider verification results](https://docs.pact.io/pact_broker/advanced_topics/provider_verification_results)
and [Pact Broker versioning](https://docs.pact.io/pact_broker/versioning_in_the_pact_broker).
