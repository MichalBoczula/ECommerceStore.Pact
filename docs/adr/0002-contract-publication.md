# ADR-0002: Content-addressed artifacts, immutable bindings and publication provenance

Status: Accepted
Backlog: PACT/03

## Context

A source commit may be built on several branches and retried by CI. Identical contract
bytes can be shared by several application versions. Neither a later upload nor a new
branch may overwrite the contract attached to an immutable source version.

## Decision

- `contract_artifacts` stores original bytes as BSON binary, keyed by lowercase SHA-256.
  Hashes cover the exact decoded bytes, including BOM, whitespace, Unicode and line endings.
  No JSON normalization or reserialization changes the stored/downloaded artifact.
- `contract_versions` binds a registered source-version ID + kind + integration key to one
  artifact hash. ProviderOpenApi is application-wide; ConsumerPact is scoped to a declared
  consumer/provider pair, and the source application must be that consumer.
- `contract_publications` is append-only provenance for a binding. Source ref, build URL and
  authenticated publisher ID form its idempotency key. A retry returns 200 with the original
  publication ID/time. Another branch/build/publisher records a new publication (201), sharing
  the same binding and artifact. Different bytes for that binding return 409 on every branch.
- Store content before the binding, then publication. Unique indexes and insert-only upserts
  enforce all identities across hosts without a multi-document transaction. A retry recovers
  when content/binding succeeded but publication failed. A losing conflicting race can leave
  an unreferenced immutable artifact; it never creates a binding/publication with missing or
  conflicting content. Retention/garbage collection is future work.
- Upload a JSON envelope containing `contentBase64`, not a parsed/reformatted content object.
  Decode at most 1 MiB of original UTF-8 JSON; read at most 2 MiB for the entire envelope,
  including requests without Content-Length. Oversized requests return 413.
- Read artifact metadata, publication metadata, or the raw file separately. Downloads have
  a content hash ETag and a generated attachment filename. Artifact reads check stored hashes.

## Supported validation profile

- Provider: OpenAPI 3.0.x/3.1.x JSON, parsed with pinned Microsoft.OpenApi 2.12.2 and its default
  validation rules. External references are not fetched; upload self-contained snapshots.
- Consumer: Pact specification 3.0/4.0 HTTP files with 1-1000 interactions. Party names exactly
  match registry application IDs. V4 interactions must be Synchronous/HTTP; messages/plugins
  are not supported. The registry checks envelope/profile shape and retains matching rules,
  bodies and generators as supplied. Official Pact tooling validates execution/matching in CI.
- Reject malformed JSON/UTF-8, duplicate JSON property names, unsupported versions/kinds,
  external OpenAPI references and mismatched consumer/provider/source identities. Pact body
  properties remain business data, including literal $ref values. A UTF-8 BOM may be
  removed for parsing only; the stored bytes/hash/download remain unchanged.
- Upload acceptance is not a verification result or compatibility decision. Those are PACT/04-06.

## Publisher access

Every POST requires an opaque Bearer publisher token. Configuration contains only its SHA-256
and permitted application IDs. Empty configuration denies all writes while reads/health work.
Tokens are compared using fixed-time hash comparison; they are not accepted from query strings.
Version writes require access to that application, integration declarations require access to
the consumer, and contract publication requires access to the source application. A consumer
Pact must additionally belong to that source consumer. The API derives publisher ID from the
authenticated principal; it is not a caller-supplied request field.

Provenance includes a full refs/heads, refs/tags or refs/pull ref and a credential/query-free
HTTPS build URL below the registered repository URL (ignoring a clone URL's .git suffix). This is trusted-publisher provenance,
not cryptographic attestation of a GitHub workflow. Workload identity/deployment configuration
is a later task. Public reads intentionally allow the reporting frontend and verifier to retrieve
immutable artifacts; publish synthetic contract data suitable for that visibility.

The local helper generates a random token into the ignored .env file. Compose injects only
the hash. The container smoke generates independent ephemeral credentials; it needs no GitHub
repository secret. External hosting is still PACT/07 and must use HTTPS and injected credentials.

## Consequences

PACT/02 write clients now need an authorized Bearer token. Existing version/integration identities
and storage documents do not change. New collections/indexes are added lazily before writes.
Raw artifacts are bounded below MongoDB's document size limit. There is no replace/delete API,
automatic provider-to-consumer contract generation, or assertion that an upload passed Pact.
