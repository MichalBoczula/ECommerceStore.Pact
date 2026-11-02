# ADR-0005: Pinned required checks and immutable exact-execution decisions

Status: Accepted
Backlog: PACT/06

## Context

A schema diff, equal artifact bytes or a successful verification of another provider commit
cannot prove a selected version pair compatible. Evidence selection must be explicit and
repeatable, including when CI publishes contracts/results concurrently. Missing checks must
never pass by default, and a new successful attempt must not erase the previous failure.

## Verification profiles

`verification_profiles` stores integration + normalized name + opaque version as an immutable
identity, protected by `uq_verification_profile`. Only the integration's consumer-scoped publisher
may create its required-check profile. The integration must already exist. The API derives the
publisher from authentication. Profiles specify 1-10 distinct, case-sensitive check IDs and exact
verifier name/version/configuration SHA-256. Configuration contents/secrets are not stored.

Checks are sorted by ID before fingerprinting. Equivalent registration returns the original
ID/time/publisher (200), including reordered checks or another authorized consumer credential.
Changing checks for the same name/version returns 409 `verification_profile_conflict`; register
a new version for an intentional policy change. Atomic insert-only upserts and a unique key
handle concurrent equivalent/conflicting registration across hosts. No update/delete API exists.

Source CI must pin the chosen profile ID. The registry enforces every check in that profile,
not whether its author selected enough business requirements. There is no automatic latest
profile, caller-supplied temporary check subset, provider-generated consumer expectation or
claim that a manually selected profile covers all dependencies. Source CI rollout is PACT/08-09.

## Explicit matrix and evidence selection

POST `/api/compatibility-decisions` accepts a profile ID, requesting source application and
1-10 distinct rows. The source must be the profile's consumer or provider, with matching token
scope. Every row selects the full consumer/provider commits, exact Pact byte hash, opaque
provider CI run ID, attempt and full provider source ref. Logical applications come from the
pinned profile. There is no environment selector or deployment record in this implementation;
targeting deployed versions would require that separate model.

Resolve registered versions and the unique ConsumerPact binding for that consumer version and
integration. Missing versions/profile return 404; a bound hash differing from the requested hash
returns 400. A missing binding produces Unverified/ContractNotPublished for every required check.
Bound artifact presence/kind/hash integrity are checked; corrupt storage is an error, not missing
or passing evidence. A decision does not require a provider OpenAPI publication or diff service.

Use the existing unique execution key for each required check: consumer-version ID,
provider-version ID, binding ID, exact Pact hash, CI run ID, attempt and check ID. Query only
those keys, at most 100, using the unique execution index. Reported verifier name/version/config
must match the profile, and provider source ref must equal the selector. A different publication
of the same immutable binding may supply evidence; its original publication ID is returned.
A different consumer source version cannot, even when its bytes happen to match.

| Evidence state | Check status/reason |
| --- | --- |
| Consumer binding absent | Unverified / ContractNotPublished |
| Selected execution absent | Unverified / MissingExecution |
| Verifier name/version/config differ | Unverified / VerifierMismatch; original observed report retained |
| Provider ref differs | Unverified / SourceRefMismatch; original observed report retained |
| Applicable selected report | Its Passed, Failed, Error or Unverified status / Reported |

Opaque CI run IDs must be unique in the provider's CI namespace and stable across upload retries.
They do not infer a chronological order. The registry trusts the existing authenticated report
provenance; this feature adds no independent cryptographic/workflow execution attestation.

## Versioned outcome policy

Policy `explicit-ci-execution-v1` requires **all** required checks to pass for a row to pass.
Aggregate rows the same way. Precedence is Failed, then Error, then Unverified, then Passed.
A known applicable Failed check is preserved even alongside unavailable checks. Infrastructure
Error alone is never converted into Failed. An Error report with partial diagnostics remains
Error under this policy. Empty matrices/profiles are invalid, not vacuously passing.

`compatible` is true only for Passed, for the selected pairs/profile/executions. It is not a
blanket deployment approval covering unrequested integrations or environment state. A new provider
commit needs its own evidence. An unchanged failing Pact remains failed. Content equality,
comparison snapshots, a later upload or another run/attempt never replace selected evidence.

The response contains the profile/policy, all selectors, per-row/check outcomes, missing and
unresolved check IDs, observed verifier/status, evidence IDs and report URLs, payload hashes,
original publication, CI build URL/publisher and issue counts. Missing checks includes all
Unverified checks (absent, inapplicable or explicitly unverified); unresolved includes every
non-Passed check. Full original diagnostics remain on the linked immutable verification runs.

## Immutable snapshots and concurrency

`compatibility_decisions` has unique `uq_compatibility_decision` identity over the decision format,
policy, pinned profile/content, canonical ordered selectors and observed binding/evidence state,
including check/row/matrix outcomes and evidence payload hashes. Rows sort by normalized selector
hash; check sets sort by check ID. Reordered rows/evidence do not change identity. Duplicate
normalized selectors are rejected. Server ID/time/requesting publisher do not affect equivalence.
The first authorized requester's metadata remains unchanged on retry from either side.

Atomic insert-only upsert returns 201 for a new evidence snapshot or 200 for the same observed
snapshot. Unlike permanently caching a query with no evidence, a new selected report/binding
produces another snapshot. The old Failed/Unverified record is never overwritten. Selecting a
new CI attempt is another explicit query and preserves the earlier attempt's decision.

Evidence lookup observes a bounded returned set, not a claim of a global timestamp snapshot
across independent concurrent Mongo writes. Canonicalization makes each observed set deterministic.
A publication after a read may require reevaluation; the new response references its own evidence.
Unrelated publications/checks/versions do not affect selection. A batch may observe available
reports plus missing ones and must remain non-Passed until every required check is present.

Store the profile snapshot, policy version, original row/matrix outcomes and evidence summaries.
GET by ID reads them without consulting current profiles, executions, branch heads or diff tools.
Known v1 records validate aggregation consistency on read; older policy labels keep their stored
outcomes instead of applying the current aggregation algorithm. Explicit document mapping and
fingerprint checks reject corrupt records with safe 500, not an accidental passing decision.

## HTTP, bounds and validation

Profile and decision POSTs use the shared streamed UTF-8 JSON reader, including unknown
Content-Length, with 1 MiB envelopes. Unknown/duplicate DTO fields are rejected. At most 10
checks/profile and 10 rows/matrix keep repository queries and snapshot summaries bounded. No
expected/actual JSON fragments are duplicated in decisions; they remain in original reports.
Validation returns ProblemDetails 400, scope 403, missing references 404, profile conflict 409,
oversize 413, wrong content type 415 and unavailable MongoDB 503. Empty publisher configuration
continues to deny every write. Public GET by ID follows the registry's existing read policy.

Tests cover all statuses/precedence, missing and inapplicable evidence, complete check sets,
independent provider versions, explicit retries/attempts, stable snapshots under concurrency,
new selected evidence without historical overwrite, immutable profile conflicts, scope/reference
validation, corrupt record handling and stored policy outcomes. Container smoke exercises the
same endpoints with synthetic provider reports; actual service execution remains PACT/08-09.
