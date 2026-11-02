# Contract registry delivery backlog

Each task is a separately reviewable PR. Complete the stated outcome before marking
a task done. Backend and one real CI integration precede the frontend.

| Task | Package | Status | Depends on |
| --- | --- | --- | --- |
| PACT/01 | API, MongoDB and verification foundation | Done (PR #1) | - |
| PACT/02 | Application versions and integration identities | Done (PR #2) | 01 |
| PACT/03 | Immutable contract publication and retrieval | Done (PR #3) | 02 |
| PACT/04 | Verification runs and structured issues | Done (PR #4) | 03 |
| PACT/05 | Contract changes and issue history | Done (PR #5) | 03, 04 |
| PACT/06 | Compatibility decisions for explicit versions | Done (PR #6) | 04, 05 |
| PACT/07 | Image publication and Azure deployment configuration | In progress (publication/apply pending) | 06 |
| PACT/08 | Payments consumer Pact pilot | Planned | 03 |
| PACT/09 | Invoice verification and complete CI pilot | Planned | 06, 07, 08 |
| PACT/10 | Remaining application integrations | Planned | 09 |
| PACT/11 | Report and dependency graph API | Planned | 05, 06, 10 |
| PACT/12 | Frontend graph and history views | Planned | 11 |

## PACT/01 - API, MongoDB and verification foundation

- .NET 10 Minimal API with validated MongoDB configuration and a singleton client.
- Liveness independent of storage, bounded MongoDB readiness, generated OpenAPI,
  ProblemDetails and no connection/exception details in public health output.
- Local Compose with persistent MongoDB, rootless API image and configurable ports.
- Locked dependencies, local/CI verification entry points, HTTP tests, actual MongoDB
  integration test, container smoke and a fail-closed quality-gate job.
- Record the integration inventory, architecture decisions and remaining backlog.
- Acceptance: verify scripts and required CI jobs pass; database outage gives readiness
  503 while liveness stays 200. No claim that business contract testing is implemented.

## PACT/02 - Application versions and integration identities

- Add Domain and Application layers with immutable application identities and versions.
- An application version is identified by application + full commit SHA; branch/ref
  information is publication metadata, not a replacement for the immutable identity.
- Register/read versions and consumer -> provider integration identities in MongoDB.
- Unique indexes and atomic/idempotent registration, including concurrent duplicates.
- Use request/result DTOs at API boundaries and explicit MongoDB document mapping.
- Acceptance: repeated registration returns the same identity; conflicting immutable
  metadata is rejected; invalid inputs return consistent ProblemDetails.

## PACT/03 - Immutable contract publication and retrieval

- Accept provider OpenAPI and consumer Pact as distinct artifact kinds. A consumer Pact
  contains both outgoing requests and expected provider responses.
- Calculate a SHA-256 over the exact uploaded UTF-8 bytes; retain original content.
  Content deduplication and publication identity are separate, so identical content can
  be referenced from several application versions.
- Store consumer/provider identities, source version, provenance and publication time.
- Add bounded payload validation, explicit schema/version support and protected writes;
  no provider-controlled regeneration of consumer expectations.
- Acceptance: repeated publication is idempotent, existing content cannot be replaced,
  content is retrievable byte-for-byte and invalid/unauthorized writes fail.

## PACT/04 - Verification runs and structured issues

- Append immutable executions linked to consumer version, provider version, contract
  hash, verifier version/configuration and CI run + attempt + check identity.
- Distinguish Passed, Failed, Unverified and Error; infrastructure failure is not proof
  of incompatibility. Store diagnostics with operation, interaction, JSON path, expected
  and actual values, and links to evidence.
- Ingestion retries are idempotent without overwriting a prior execution or collapsing
  a new rerun. Validate references and bind CI provenance to an authorized publisher.
- Acceptance: the same contract can have different results against two provider
  versions; failed/error executions and original diagnostics remain queryable.

## PACT/05 - Contract changes and issue history

- Compare explicit artifacts within an integration and selected branch/environment
  baseline, never the globally most recent upload.
- Choose a pinned, format-aware comparison tool and document its supported semantics:
  request vs response requiredness, type/nullability changes, references and composition.
- Separate textual/content change, potential schema break and verified consumer impact.
- Track issue appearance and resolution for comparable requirements. Removing a consumer
  requirement is not automatically reported as a provider fix.
- Acceptance: additive response fields remain compatible; required field removal is
  identified; an unchanged failing contract remains failing; historical data is immutable.

## PACT/06 - Compatibility decisions for explicit versions

- Query a matrix for explicit consumer/provider version pairs, exact contract hash and
  applicable verifier evidence. Define selection policy and preserve its version.
- Return the decision plus supporting executions and unresolved/missing checks.
- Missing evidence is Unverified and cannot silently become Passed; content equality
  alone cannot turn a failed result into success.
- Add environment deployment records if decisions target deployed versions.
- Acceptance: deterministic decisions under concurrent publications and a new provider
  version passing/failing independently of the previous provider version.

## PACT/07 - Image publication and Azure deployment configuration

- Extend the existing CI to scan and publish the tested image using repository secrets.
- Prepare Terraform/configuration consistent with the existing ACA environment, explicit
  MongoDB settings, managed secret injection and health probes.
- Configure ingress and authenticated CI publication access; avoid public anonymous writes.
- Acceptance: reproducible local/deployed configuration, documented required secrets,
  deployment procedure and a reachable readiness endpoint after deployment.

## PACT/08 - Payments consumer Pact pilot

- In Payments, execute the real HttpOrderReader, generated Kiota client, production
  decimal parser and mapper against a Pact mock provider for GET /orders/{orderId}.
- Assert the resulting application model, exact currency/amount conversion and the
  required fields. Include meaningful success and not-found interactions.
- Keep malformed-response and transport-failure tests local; do not require the provider
  to generate intentionally invalid JSON just to test consumer resilience.
- Acceptance: consumer tests produce a stable versioned Pact and detect request/response
  mapping mistakes without running Invoice or the registry.

## PACT/09 - Invoice verification and complete CI pilot

- Verify Payments' Pact against the candidate Invoice API with deterministic, isolated
  provider states and an actual TCP endpoint; preserve production DTO serialization.
- Publish Pact artifacts, provider OpenAPI and structured results to the registry.
- Wire a required contract job into the source repositories' existing quality gates.
  A separate central dashboard/workflow is not sufficient to block the source PR.
- Authenticate publication, pin exact revisions and define how pending evidence is awaited.
- Acceptance: demonstrate a temporary required-field removal failing the provider PR and
  restoring it passing, with the same consumer contract and history visible in the registry.
  Do not merge the deliberately breaking demonstration.

## PACT/10 - Remaining application integrations

- Cover Invoice -> Products; Web -> Products, Users, Invoice and Payments via BFF;
  Web -> BFF registration; BFF -> Users and Invoice registration gateways.
- Use real handwritten adapters and generated clients, not independent HTTP test copies.
- Keep transparent BFF transport/routing checks distinct from consumer data requirements.
- Acceptance: the inventory lists each used operation, owner, consumer test and provider
  verification; uncovered operations are visible rather than reported as passing.

## PACT/11 - Report and dependency graph API

- Paginated run/issue histories, explicit-version compatibility matrix, contract diffs,
  affected consumers, provenance links and application/integration graph projections.
- Label relationships as declared/tested; do not present them as observed runtime traces
  or infer an entire distributed business flow from independent Pact interactions.
- Acceptance: query missing totalAmount in one provider version and its confirmed fix
  in another; graph edges link to the underlying contracts and verification evidence.

## PACT/12 - Frontend graph and history views

- Build Angular only after the backend contracts stabilize; generate its client with Kiota.
- Display applications and dependencies, version filters, matrix, run details and issue
  timeline with expected/actual paths and links to the responsible CI/commit.
- Keep Unverified and Error visually distinct from Passed and Failed.
- Acceptance: navigate from a failing integration to its issue and confirmed fix without
  needing to inspect raw MongoDB documents or CI logs.
