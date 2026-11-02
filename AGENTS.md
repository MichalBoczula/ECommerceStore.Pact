# Agent instructions

Read README.md, TECHNICAL_TODO.md and docs/architecture.md before editing. Scope each PR
to its backlog item. Commits, code, documentation and PR descriptions are in English.

- API owns HTTP request/result DTOs, validation responses, ProblemDetails and composition.
- Infrastructure owns MongoDB documents, explicit mapping, indexes, drivers and repositories.
- Domain owns immutable version/integration identities and validation. It must not reference
  ASP.NET, MongoDB, generated clients or infrastructure. Application owns use cases and ports.
- Version metadata, including an omitted image digest, is immutable. Branch/ref belongs to
  publication metadata. Integration registration declares a dependency, not compatibility.
- Preserve atomic insert-only registration, unique indexes and concurrent retry/conflict checks.
- Contract bytes are hashed/stored/downloaded unchanged. Separate shared content, immutable
  source-version bindings and branch/build/publisher publication provenance. Preserve all three.
- Every POST requires a publisher token and application scope. Never accept publisher identity
  from the upload body. Empty publisher configuration must keep writes closed.
- Keep upload limits on both decoded content and the streamed envelope; unknown Content-Length
  must not bypass limits. Supported JSON profiles are documented in ADR-0002.
- Preserve immutable version/artifact identities and append-only verification history.
  Never use upload order or an unqualified latest version as a compatibility decision.
- Verification writes are owned by the provider. Keep CI execution identity separate from
  report fingerprint, preserve reruns and distinguish infrastructure Error from Failed.
  Derive expected interaction count from the bound Pact, never caller-supplied totals.
- Use official Pact tooling in consumer/provider CI; do not implement a parallel matching engine.
- Keep consumer requirements independent from provider-generated expectations. Tests must
  execute existing production adapters and real generated-client serialization.
- Do not conflate a schema diff, a verification result and a deployment decision.
- Update a backlog status only when its stated acceptance criteria are met. Accepted docs
  describe implemented behavior; label planned behavior explicitly.
- Run bash scripts/verify.sh (requires .NET 10 and Docker), or relevant scripts/ci.sh stages.
  Report only checks actually run and describe unavailable verification. Do not weaken or
  skip gates to get a green result. Preserve locked dependencies and audit high/critical issues.
- Use the PR template and include the backlog ID, behavior, validation and remaining work.
