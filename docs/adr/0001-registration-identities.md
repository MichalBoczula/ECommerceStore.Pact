# ADR-0001: Immutable version and integration registration

Status: Accepted
Backlog: PACT/02

## Context

CI retries and parallel builds must not create competing identities or silently overwrite
metadata. Branch names and upload order do not identify immutable application versions.
The registry needs a stable foundation before receiving contract artifacts.

## Decision

- Identify a version by normalized application ID + full 40/64-character commit SHA.
  Application IDs are case-insensitive ASCII slugs; commits are lowercase hexadecimal.
- Keep repository URL and optional image digest immutable. Normalize HTTPS URL syntax and
  trailing slash, and digest case. A different repository or digest returns 409. Omission
  of a digest is also fixed metadata; a retry cannot enrich or replace the original record.
  Pipelines must register the final metadata, or leave image publication to a separate record
  in a future task. Image tags such as latest are not accepted as digests.
- Do not accept branch/ref in this endpoint. Those values belong to contract-publication
  provenance in PACT/03, where a version may be referenced from several branches.
- A directed integration is identified by normalized consumer + provider application IDs.
  Reverse edges have different identities. Self-edges are rejected. A declaration does not
  require version registration and is not evidence that contract verification passed.
- Use Domain entities/value identities, Application services/ports, explicit Infrastructure
  documents/mapping and API request/result DTOs. Domain/Application have no external packages.
- Store GUID identities as strings in MongoDB and timestamps as UTC BSON dates, normalized
  to milliseconds before insertion. Existing IDs and timestamps survive every retry.
- Use unique compound indexes and atomic find-and-update upserts containing only
  `$setOnInsert`. A duplicate-key race reads the winning record. Application compares its
  immutable metadata and returns the existing registration or a conflict.
- Create indexes before the first write, with cancellation and retry after an initialization
  failure. Do not block process startup on database availability. The semaphore coordinates
  initialization within one host; MongoDB indexes enforce uniqueness across hosts.
- Return 201 with Location for a new record, 200 for an equivalent registration, 400 for
  invalid input, 404 for an unknown identity, 409 for immutable metadata conflicts and 503
  for storage outages. Error responses use ProblemDetails with traceId. Unknown request
  properties and malformed/missing JSON fields are rejected rather than silently dropped.

## Consequences

There is no update/delete operation or branch-based latest lookup. A new commit is a new
version; a retry is a lookup of the original immutable registration. Index definition
changes require an explicit schema evolution task. Corrupt persisted records produce a
server failure, not a caller-validation 400.

PACT/02 introduced anonymous local registration. PACT/03 now protects all writes and stores
contract artifacts/publications; see [ADR-0002](0002-contract-publication.md). External hosting
is PACT/07. The default Compose stack remains bound to loopback. Verification runs, diff
classification and compatibility decisions remain planned.

## Verification

Domain tests cover canonical identities, metadata policies and timestamp precision.
Application tests exercise retries/conflicts and clock/port boundaries. Mapping tests
perform actual BSON round-trips. HTTP/MongoDB tests cover cross-host concurrency, conflicting
requests, independent identities, read locations, actual unique-index enforcement and state
preservation. The container smoke exercises business registration through the published API.
