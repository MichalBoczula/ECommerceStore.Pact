# Architecture and versioning

## Implemented boundary (PACT/01-06)

API owns HTTP hosting, generated OpenAPI, ProblemDetails and health response formatting.
Domain owns immutable application-version and directed-integration identities and their
validation. Application owns registration/lookup services and repository ports.
Infrastructure owns MongoDB documents, explicit mapping, unique indexes, atomic registration,
client lifetime and the readiness probe. API maps request/result DTOs and errors.
Implemented collections are `application_versions`, `integrations`, `contract_artifacts`,
`contract_versions`, `contract_publications`, `verification_runs`, `contract_comparisons`, `verification_profiles` and `compatibility_decisions`. Publication separates shared content from
immutable source-version bindings and branch/build/publisher provenance.
See [ADR-0001](adr/0001-registration-identities.md) for normalization, metadata and retry rules.
See [ADR-0002](adr/0002-contract-publication.md) for publication, validation profiles and access.
See [ADR-0003](adr/0003-verification-runs.md) for report identities, statuses and retry rules.
See [ADR-0004](adr/0004-contract-changes-and-issue-history.md) for pinned comparison semantics and evidence-based issue transitions.
See [ADR-0005](adr/0005-explicit-compatibility-decisions.md) for verification profiles, exact CI selection and immutable decisions.

MongoDB availability is a readiness condition, not a process-liveness condition. A probe
has a bounded timeout and does not expose the original driver exception. Valid connection
configuration is required at startup even while the database itself is unavailable.

Release configuration is PACT/07: one built image is smoke-tested/scanned, transferred as an
artifact, then published after the main quality gate. Source/image identity and a registry-digest
receipt connect release to deployment. The companion Terraform root reuses existing ACA/Mongo
infrastructure with scoped ACA secret references. Hosted readiness is verified after an operator
applies the reviewed plan; mocked plans and PR CI do not establish an Azure deployment.
See [deployment instructions](deployment.md) and [ADR-0006](adr/0006-image-publication-and-hosting.md).

PACT/08 consumer execution belongs to Payments. Its real HttpOrderReader/Kiota/precise-parser
boundary generates a deterministic five-interaction Pact over official Pact HTTP, with a hash
manifest in the existing required source CI suite. [Pilot details](payments-consumer-pilot.md)
record consumed fields, currency examples and parameterized provider states. Invoice verification
and registry publication remain PACT/09; no provider compatibility is inferred from this mock.

## Model and delivery state

WEB/02 adds bounded dashboard discovery over these existing records: paginated applications,
versions, declarations, publications, reports, profiles, comparison snapshots and decisions,
plus graph pages and separately scoped report counts. API DTOs map Application read models;
Infrastructure owns MongoDB projections/indexes. No artifact bytes are loaded for browsing,
no latest upload/run becomes a compatibility policy, and no business record is written by GETs.
See [ADR-0007](adr/0007-dashboard-read-models.md) and [the browsing contract](registry-browsing.md).

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
| VerificationProfile (implemented) | Integration + name/version, consumer-owned immutable required verifier/check configuration. |
| CompatibilityDecision (implemented) | Bounded explicit-version matrix, profile/policy, selected CI execution per pair, observed evidence and stored row/matrix outcomes. |

A provider OpenAPI artifact describes the offered schema. A consumer Pact describes the
actual request/expected-response interactions exercised by consumer tests. The registry
does not treat these formats as interchangeable or implement a new Pact matching engine.
Execution belongs in CI using official Pact tooling. The registry stores uploads, provenance and provider CI reports. It validates references and
report consistency; it does not independently attest execution. Compatibility selection uses a pinned profile and explicit version/hash/CI selectors. Environment/deployment selection is not implemented.

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
