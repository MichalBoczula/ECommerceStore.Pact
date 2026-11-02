# Architecture and versioning

## Implemented boundary (PACT/01-05)

API owns HTTP hosting, generated OpenAPI, ProblemDetails and health response formatting.
Domain owns immutable application-version and directed-integration identities and their
validation. Application owns registration/lookup services and repository ports.
Infrastructure owns MongoDB documents, explicit mapping, unique indexes, atomic registration,
client lifetime and the readiness probe. API maps request/result DTOs and errors.
Implemented collections are `application_versions`, `integrations`, `contract_artifacts`,
`contract_versions`, `contract_publications`, `verification_runs` and `contract_comparisons`. Publication separates shared content from
immutable source-version bindings and branch/build/publisher provenance.
See [ADR-0001](adr/0001-registration-identities.md) for normalization, metadata and retry rules.
See [ADR-0002](adr/0002-contract-publication.md) for publication, validation profiles and access.
See [ADR-0003](adr/0003-verification-runs.md) for report identities, statuses and retry rules.
See [ADR-0004](adr/0004-contract-changes-and-issue-history.md) for pinned comparison semantics and evidence-based issue transitions.

MongoDB availability is a readiness condition, not a process-liveness condition. A probe
has a bounded timeout and does not expose the original driver exception. Valid connection
configuration is required at startup even while the database itself is unavailable.

## Model and delivery state

| Record | Identity and responsibility |
| --- | --- |
| ApplicationVersion (implemented) | Application + immutable full commit SHA, repository URL, optional immutable image digest. |
| Integration (implemented) | Directed consumer + provider declaration, independent of version and verification. |
| ContractArtifact (implemented) | Exact original bytes + content hash + artifact kind; immutable. |
| ContractBinding (implemented) | Registered source version + kind + integration maps to one immutable artifact. |
| ContractPublication (implemented) | Binding + source ref + build URL + authenticated publisher; retry-stable provenance. |
| VerificationRun (implemented) | Exact consumer/provider versions + Pact hash + verifier/configuration + CI run/attempt/check; append-only report and structured diagnostics. |
| ContractComparison (implemented) | Explicit before/after publications, integration, selected branch baseline and pinned schema/content analysis snapshot. |
| IssueHistory (implemented projection) | Explicit run pair and versioned policy; requirement-level transitions without changing stored evidence. |

A provider OpenAPI artifact describes the offered schema. A consumer Pact describes the
actual request/expected-response interactions exercised by consumer tests. The registry
does not treat these formats as interchangeable or implement a new Pact matching engine.
Execution belongs in CI using official Pact tooling. The registry stores uploads, provenance and provider CI reports. It validates references and
report consistency; it does not independently attest execution. Compatibility selection is PACT/06.

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
