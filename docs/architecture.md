# Architecture and versioning

## Implemented boundary (PACT/01)

API owns HTTP hosting, generated OpenAPI, ProblemDetails and health response formatting.
Infrastructure owns MongoDB options, validation, client lifetime and the readiness probe.
Domain and Application projects are introduced with actual registry behavior in PACT/02;
this foundation does not add placeholder business layers or persistence collections.

MongoDB availability is a readiness condition, not a process-liveness condition. A probe
has a bounded timeout and does not expose the original driver exception. Valid connection
configuration is required at startup even while the database itself is unavailable.

## Target model (planned, not yet implemented)

| Record | Identity and responsibility |
| --- | --- |
| ApplicationVersion | Application + immutable full commit SHA, optional matching image digest. |
| ContractArtifact | Exact original bytes + content hash + artifact kind; immutable. |
| ContractPublication | Artifact hash + source application version + integration + branch/provenance. |
| VerificationRun | Exact consumer/provider versions + Pact hash + verifier identity + CI execution. |
| ContractChange | Explicit before/after artifacts and format-aware difference classification. |

A provider OpenAPI artifact describes the offered schema. A consumer Pact describes the
actual request/expected-response interactions exercised by consumer tests. The registry
does not treat these formats as interchangeable or implement a new Pact matching engine.
Execution remains in CI using official Pact tooling; the registry stores evidence and
applies an explicit compatibility-selection policy.

Application versions and artifact contents are separate. A source-code change may produce
an identical contract. Conversely, multiple verification runs can examine the same contract
against different provider versions. Branches and upload timestamps are not version IDs.

## Decisions and history

- Select an explicit baseline within an integration/branch or recorded environment.
- Keep schema-change classification separate from runtime verification status.
- Passed requires applicable successful verification evidence; unknown pairs are Unverified.
- Failed means a verified contract mismatch. Error means the verification could not complete.
- Keep runs append-only. An ingestion retry reuses its execution identity; a rerun has a new
  identity and preserves the previous outcome.
- Show a provider issue as resolved only with comparable consumer requirements and a passing
  verification, not merely because a new consumer removed the requirement.
- A dependency graph is derived from declared/tested integration edges. Pact interactions
  do not establish an observed distributed runtime trace or orchestration sequence.

## Boundaries of the MVP

Start with Payments -> Invoice GET /orders/{orderId}, then expand. Consumer and provider
test projects remain in their own repositories so they execute production adapters and
provider handlers. Registry business tests belong here. The frontend is the final stage.

Official references:

- [How Pact works](https://docs.pact.io/getting_started/how_pact_works)
- [Writing consumer tests](https://docs.pact.io/consumer)
- [Pact Broker responsibilities](https://docs.pact.io/pact_broker)
- [Version-specific deployment decisions](https://docs.pact.io/pact_broker/can_i_deploy)
