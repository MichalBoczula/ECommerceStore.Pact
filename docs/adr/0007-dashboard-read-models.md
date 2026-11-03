# ADR-0007: Browse registry evidence through bounded read models

- Status: Accepted
- Scope: WEB/02, the browsing foundation of PACT/11

## Problem and decision

Exact-ID reads preserve evidence but do not let the Angular dashboard discover applications,
versions, publications, executions or stored decisions. Add anonymous GET discovery and reporting
endpoints over the existing collections. API owns request/result DTOs and links; Application owns
normalization, cursor scope, read models and the repository port; Infrastructure owns MongoDB
pipelines, projections, mappings and indexes. No extra database, materialized collection, writer,
matching engine, external package or consumer/provider execution is introduced.

See [the browsing contract](../registry-browsing.md) for routes, query fields and examples. Existing
exact-ID/content reads, scoped POSTs, immutable identities and history semantics remain authoritative.
Publication pages join binding metadata without loading artifact content. Run pages return execution
metadata/counts and link to the original diagnostics instead of embedding all expected/actual JSON.
Comparison pages summarize saved findings without running oasdiff. Decision pages retain the existing
domain integrity checks before exposing summary status; a corrupt matrix fails rather than appearing
compatible. Exact-ID reads provide the complete profile, matrix, diagnostics and comparison findings.

## Pagination and consistency

- Page size defaults to 50 and is bounded to 1-100. Applications group registered versions and sort
  by normalized application ID. Other collections sort by descending server-recorded timestamp and
  ascending canonical record ID as a deterministic tie breaker. Execution time and upload order are
  never compatibility selection policies.
- Read one extra row to determine continuation. Cursors encode a versioned resource, normalized
  filter fingerprint and position. Changing the resource or filters returns validation ProblemDetails;
  page size may change. Cursors are opaque continuation tokens, not authentication credentials.
- Keyset reads do not promise a transactional snapshot. Newer records inserted after the first page
  are visible on a fresh traversal, not injected into later pages. Backdated inserts before the current
  position also require a restart. Inventory counts and evidence aggregates are separate reads and
  can change concurrently. `observedAt` is the server observation time, not a persisted snapshot ID.
- Indexes cover application/version, timestamp/ID and common integration/version evidence queries.
  Queries have bounded output, a ten-second server aggregation/count limit and request cancellation;
  existing driver timeouts still apply. Initial access ensures indexes, but GETs never append or modify
  registry business records. Public reads assume the same existing database/index privileges as writes.

## Evidence and graph semantics

The overview separates **global inventory** from **matching report counts** under the returned
selection. Counters represent all matching recorded executions, preserving reruns and all four statuses.
They do not elect a latest run, prove complete coverage or create a compatibility decision.

The dependency endpoint pages declared integrations, returning only the nodes needed for that page.
Nodes can be declared parties without registered versions. `isComplete` is true only when this response
contains the entire selected edge set (the first page with no continuation). A client combining pages
must merge nodes by application ID. This describes a changing declaration set, not an observed trace.

Version/hash/ref/verifier/CI/status/issue filters select reports associated with each edge; they never
remove an otherwise selected declaration simply because no report matches. A zero count means no
matching recorded reports, not proof that an application was never tested. Consumer/provider commit
selectors apply to those roles on each edge. Pin an integration when comparing two specific applications.
The edge's reports link carries the same evidence selectors and leads to original execution details.

Code and JSON-path filters match the **same issue**. Consumer/provider/hash filters on saved decisions
match the **same matrix row**. A decision list's status is the stored whole-decision outcome, not the
status of one matching row. Incomplete/inapplicable evidence remains Unverified under the existing
decision policy; graph and overview counters never replace that policy.

## Delivery boundary

WEB/03 can pin the generated OpenAPI from a reviewed backend commit and generate its Kiota client.
This work does not complete backend PACT/09-10, hosted deployment, end-to-end source-CI evidence,
environment/deployment selectors or the full PACT/11 issue timeline/affected-consumer acceptance.
