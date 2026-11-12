# Registry browsing API

All routes below are anonymous GET reads. Existing POST authentication and exact-ID/content reads
are unchanged. All pages return `{ "items": [...], "nextCursor": "..." }`; `nextCursor` is null on
the last page. Pass it unchanged as `after`, retain the same filters, and use `limit` from 1-100
(default 50). Changing page size is allowed. Invalid filters/cursors return validation ProblemDetails
with a trace ID; storage failures return sanitized 503 ProblemDetails.

| Route | Filters beyond limit/after | Result |
| --- | --- | --- |
| `/api/applications` | `search` (normalized ID prefix) | Registered application IDs, version counts and versions links; alphabetical order. |
| `/api/applications/{applicationId}/versions` | Application selected by path | Immutable version DTOs, including full commit, repository URL and optional image digest. |
| `/api/integrations` | `applicationId`, `consumerApplicationId`, `providerApplicationId` | Directed declarations; applicationId selects either endpoint. |
| `/api/contract-publications` | `sourceApplicationId`, `commitSha`, `kind`, `consumerApplicationId`, `providerApplicationId`, `sourceRef`, `contractSha256` | Publication/binding/version/hash/provenance metadata and detail/original-content links. |
| `/api/reports/verification-runs` | Evidence selection below | Execution summaries, exact selectors, issue count, CI provenance and original-report/publication links. |
| `/api/verification-profiles` | `consumerApplicationId`, `providerApplicationId`, `name`, `version` | Existing profile DTOs, including immutable required checks. |
| `/api/compatibility-decisions` | `consumerApplicationId`, `providerApplicationId`, `consumerCommitSha`, `providerCommitSha`, `contractSha256`, `profileId`, `status` | Saved decision summaries with profile/policy, whole-matrix status, row count and original snapshot links. |
| `/api/contract-comparisons` | `consumerApplicationId`, `providerApplicationId`, `sourceApplicationId`, `kind`, `baselineRef` | Saved comparison summaries with exact publications/commits, schema assessment, tool/version and original snapshot links. |
| `/api/reports/dependencies` | Evidence selection below | A graph page with nodes, declared edges, matching report counts, evidence links, nextCursor, selection and isComplete. |
| `/api/reports/overview` | Evidence selection below; no pagination | Global inventory, separately selected matching report counts, normalized selection, evidence policy and observedAt. |

Except for applications, browse order is descending recorded/registration/publication/evaluation
timestamp followed by ascending record ID. It is a display order, never a compatibility policy.
Source refs belong to publications/executions; application versions are selected by full commits.
Kinds/statuses normalize case and accept only named enum values, not numeric enum representations.

## Evidence selection

Run browsing, graph evidence counts and overview evidence counts accept:

- `applicationId` (either integration endpoint), `consumerApplicationId`, `providerApplicationId`;
- `consumerCommitSha`, `providerCommitSha`, `contractSha256`, `sourceRef`;
- `ciRunId`, `ciAttempt`, `ciCheckId`, `verifierName`, `verifierVersion`, `configurationSha256`;
- `status` (`Passed`, `Failed`, `Error`, `Unverified`), `issueCode`, `jsonPath`.

Supply full 40/64-character commits and exact 64-character SHA-256 hashes. A code and JSON path
must occur on the same issue. Selected report counts preserve every matching execution/rerun;
they do not choose a winner or establish compatibility. To inspect a confirmed issue resolution,
follow the original reports and use the existing explicit `/api/issue-history` run-pair query.

The original `/api/verification-runs` query remains the bounded explicit-integration/hash history
contract with its existing GUID cursor. The new `/api/reports/verification-runs` discovery route
has its own scoped cursor. Do not exchange their cursors.

## Examples

```http
GET /api/applications?search=pay&limit=20
GET /api/applications/payments/versions?limit=20
GET /api/contract-publications?sourceApplicationId=payments&kind=ConsumerPact
GET /api/reports/verification-runs?consumerApplicationId=payments&providerApplicationId=invoice&issueCode=missing_field&jsonPath=%24.body.totalAmount
GET /api/reports/dependencies?providerApplicationId=invoice&limit=20
GET /api/reports/overview?consumerApplicationId=payments&providerApplicationId=invoice
GET /api/contract-comparisons?consumerApplicationId=payments&providerApplicationId=invoice&baselineRef=refs%2Fheads%2Fmain
```

For exact version/CI selection, add full `consumerCommitSha`, `providerCommitSha`, `contractSha256`
and CI/verifier selectors to the report/graph query. UI filters may be incomplete for browsing;
only the existing pinned-profile decision API can establish version-specific compatibility.

## Publication and graph details

Provider OpenAPI has no consumer binding. Filtering publications by `providerApplicationId` includes
both that provider's OpenAPI and Pacts targeting it. Adding `consumerApplicationId` selects consumer
Pacts only. `sourceApplicationId` and `commitSha` refer to the application's version that published
the artifact. Original downloads still return the exact stored bytes; browsing never loads those bytes.

Graph edges always have `relationship: "Declared"`. Matching report counts do not change the edge
into a runtime trace or deployment decision. A declared party without versions still gets a node;
its versions link returns an empty page. Evidence selectors do not hide declared gaps. The `nodes`
array covers only the returned edge page, and repeated nodes across pages must be merged by ID.

The graph reads the integration page first, then groups matching reports by directed pair and
status in a single query for that page. Edges with zero matches retain their position and links.

Global overview inventory counts registered applications/versions, declared integrations, publications,
profiles, stored comparisons and stored decisions. Evidence filters affect `matchingReports`, not
`inventory`. Reads/counts are nontransactional; restart traversal to observe newly inserted records.
See [ADR-0007](adr/0007-dashboard-read-models.md) for bounds, integrity and consistency rules.

## Follow a missing field to a confirmed fix

1. Request `/api/reports/dependencies?providerApplicationId=invoice&jsonPath=%24.totalAmount`.
   The matching report count identifies the affected edge. Follow its `reportsUrl` to the
   failing execution and its `detailUrl` to see the expected value and whether it was present.
   The edge's `publicationsUrl` and the report's `consumerPublicationUrl` link to the contracts.
2. Browse the same integration with the fixed provider's exact commit, the failed report's
   `contractSha256`, `status=Passed` and the candidate CI execution selectors. Keep the original
   failure's run ID and the selected passing run ID.
3. Request `/api/issue-history?beforeRunId={failedRunId}&afterRunId={passedRunId}&baselineRef={baselineRef}`,
   using the failing run's provider ref as the baseline. The fix is confirmed when the result
   has `comparable: true` and the missing-field diagnostic has transition `Resolved`.
