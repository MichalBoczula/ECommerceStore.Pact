# ADR-0004: Explicit comparison snapshots and evidence-based issue transitions

Status: Accepted
Backlog: PACT/05

## Context

A schema difference is a potential risk, not a consumer verification result. Removing an
expectation from a consumer Pact must not look like a provider fix. Baselines must be selected
within an integration and branch rather than inferred from upload time.

## Comparison identity and storage

POST `/api/contract-comparisons` selects an integration, two publication IDs, source application
and baseline ref. Both publications must have the same kind and source application. Consumer
Pact bindings must match that integration; provider OpenAPI must belong to its provider. The
integration must exist. The before publication's full ref must equal the selected baseline;
the after publication can use a different branch. Environment/deployment selection is PACT/06.

The source application's authenticated publisher owns the write. The request cannot supply
publisher identity. Every referenced artifact/version/publication is read through the existing
registry. Results snapshot both IDs, source commits/refs/hashes and schema findings. A unique
key on `contract_comparisons` covers the integration, ordered publication IDs, baseline ref and
`registry-comparison-v1/oasdiff-1.35.0` profile. Atomic insert-only upsert keeps one winner across
hosts. Retry returns the original ID/time/publisher/result, without rerunning a stored analysis.
No update/delete API or unqualified latest baseline exists. Reads are public, as elsewhere.

The comparison separates:

- `contentChanged`: original byte hashes differ, including BOM/formatting.
- `jsonChanged`: structural JSON differs after ordinal object-key sorting. Array order and
  number lexical forms remain significant. Artifact bytes are never modified.
- `schemaAssessment`: NoBreakingChangesDetected, PotentiallyBreaking, or NotAssessed.
- `consumerImpact`: NotEvaluated. A schema comparison never writes or synthesizes a Passed run.

Pact v3/v4 comparisons are explicitly structural (`registry-json`, version 1), with NotAssessed
schema status. Matching rules/generators remain opaque; there is no parallel Pact matcher or
conversion to provider-generated expectations.

## Pinned OpenAPI comparison profile

Provider OpenAPI uses the official **oasdiff 1.35.0** release. Linux amd64/arm64 archive hashes
are committed in `scripts/oasdiff-version.env` and checked by `install-oasdiff.sh`. The API image
and CI install the same binary, retaining the release license. Host runs may select `OASDIFF_PATH` or `Comparison:Executable`;
the runtime rejects a different reported version. No new NuGet dependency is required.

Invoke `changelog --format json --lang en --allow-external-refs=false --flatten-allof
--flatten-params --config config.json` on private temporary copies named before/after.json.
An explicit empty config, isolated working directory and removal of OASDIFF environment options
prevent implicit user configuration. No URL inputs, external reference resolution, fetch or
upload/open option is used. The original stored/downloaded bytes remain untouched; a BOM is
removed only from the tool's temporary input.

| Concern | Implemented semantics and verification |
| --- | --- |
| Request requiredness | Adding a required property narrows accepted requests and reports breaking; removing requiredness does not. |
| Response requiredness | Removing a required property or its required guarantee reports breaking. Adding an optional response field is informational under the default tool policy. |
| Type changes | The pinned tool classifies request/response type changes; number-to-string response regression is exercised. |
| Nullability | Request narrowing and response widening are breaking; tested for both 3.0 nullable and 3.1 null type unions. Semantics are delegated to the pinned tool, not rewritten by the registry. |
| References | Self-contained local references resolve; a referenced response guarantee removal is exercised. External references are disabled even outside upload validation. |
| Composition | allOf and path/operation parameters are flattened explicitly. oneOf/anyOf use the tool's default branch comparison/inline-ref matching; allOf guarantee loss, oneOf request branch removal and an equivalent anyOf inline-to-ref refactor are exercised. This is not a general JSON Schema equivalence proof. |

Levels 2/3 (Warning/Error) yield PotentiallyBreaking. Only a complete supported-profile analysis
with no such finding yields NoBreakingChangesDetected. This label is scoped to the tool's
checks; it is not a deployment decision or proof for every generated client/runtime.

The registry conservatively marks analysis partial when either JSON document contains callbacks,
webhooks, dynamic references/anchors, custom schema IDs/dialects, unevaluated properties/items,
dependentSchemas, conditionals, contains bounds, deprecation or sunset policies. The scan is
conservative and can also flag a business property with one of those names. Findings still
appear, but absent breaking findings give NotAssessed with a limitation, never a safe result.
Deprecation findings can depend on tool time policy; the immutable snapshot records the actual
comparison time. Advanced JSON Schema semantics not covered by the tool must be verified in CI.

A singleton limits each API host to two tool operations with a 20-second bound including queue
wait; process trees are killed on cancellation/failure. Stdout is capped at 4 MiB, stderr at
32 KiB, findings at 1000. Invalid/nonzero/version/timeout/oversize tool results return safe 503
`comparison_unavailable` and persist nothing. Temporary files are removed. Tool failure never
becomes an empty safe analysis. Storage failure remains a separate safe 503.

## Issue history policy

GET `/api/issue-history` selects beforeRunId, afterRunId and the before run's provider baseline
ref. Both runs must belong to the same logical integration. The response includes the original
reports/evidence and a deterministic `exact-pact-requirements-v1` projection. The caller selects
the direction; no timestamp/UUID/upload-order selection is inferred. No historical run is edited,
and there is no separate mutable resolved flag.

Comparable requirements require the **exact Pact byte hash**, verifier name/version/configuration
hash and check ID to match. Provider commits may differ, as may consumer commits if their exact
Pact content is unchanged. Even format-only Pact byte changes are conservatively RequirementsChanged.
A changed hash yields RequirementsChanged; a changed verifier/check yields VerifierChanged.
Neither permits declaring a provider fix. Broader requirement equivalence needs a new policy.

Requirement keys hash the integration, policy, mismatch kind/code, interaction, method/path,
JSON path and canonical expected JSON. Message and actual value are diagnostic observations,
not requirement identity. Duplicate diagnostics for the same key form one transition; both
complete original report arrays remain available, preserving all observations. ExecutionError
is retained in those original reports, not treated as a contract mismatch.

| Evidence | Transition |
| --- | --- |
| Baseline Passed, candidate has the mismatch | Appeared |
| Both report the same mismatch | Persisting |
| Old mismatch absent and comparable candidate Passed fully | Resolved |
| Old mismatch absent and candidate Failed, Error or Unverified | Unconfirmed |
| Candidate mismatch without a passing baseline | Observed |
| Different Pact hash | RequirementsChanged |
| Same hash, different verifier/version/configuration/check | VerifierChanged |

An unchanged failing Pact stays failed; content equality never overrides evidence. A missing
issue in a partial/failed run cannot establish resolution. Pairwise history is bounded by the
existing 100 diagnostics per run. Paginated cross-version timelines/report filters are PACT/11;
selection of evidence for an explicit-version deployment decision remains PACT/06.

## Validation and official sources

Tests execute the actual pinned tool and assert request/response direction, required field
removal, additive response fields, type/nullability, references/composition, unsupported-profile
handling and external-ref denial. HTTP/Mongo tests cover immutable snapshots, cross-host retries,
tool outages, scope/reference/baseline validation, byte-only changes and issue transitions.
Container smoke uses the image's real comparison binary and synthetic verification evidence.

- [Pinned diff behavior and reference defaults](https://github.com/oasdiff/oasdiff/blob/v1.35.0/docs/DIFF.md)
- [Pinned breaking-change policy](https://github.com/oasdiff/oasdiff/blob/v1.35.0/docs/BREAKING-CHANGES.md)
- [Official 1.35.0 release and checksums](https://github.com/oasdiff/oasdiff/releases/tag/v1.35.0)
