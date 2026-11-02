# ADR-0006: Publish the tested image and reuse the shared Azure root

Status: Accepted (configuration; cloud publication/apply verification pending)
Backlog: PACT/07

A deployed registry must be traceable to the image that passed tests and scanning. It also needs
persistent MongoDB configuration and service-scoped publisher credentials before real source CI
can upload immutable contracts/results. Existing store infrastructure is owned by one Terraform
root/state and is deployed manually from reviewed plans.

The container job builds one Linux/amd64 image, runs the existing full Compose smoke with
`PACT_IMAGE_PREBUILT=true`, and scans the same tag with the store's pinned Trivy action. Fixed
HIGH/CRITICAL findings fail the job; scan output is retained. oasdiff 1.35.0 is built from its
checksum-pinned, unmodified release source with Go 1.27.3; the upstream binary embeds Go 1.27.2,
affected by [CVE-2026-111943](https://pkg.go.dev/vuln/GO-2026-6809). This keeps comparison
semantics/version pinned while applying the Go security fix in both CI and Docker.
Only successful image work exports
an archive and a metadata manifest bound to source commit, OCI revision, image ID and platform.
The main-only publish job waits for the quality gate, loads/validates that archive and pushes
commit/latest tags without rebuilding. Matching registry digests produce `image-reference.json`.
No image is published for PRs/dispatch. Missing configured push credentials fail explicitly.

Host configuration is a companion change in ECommerceStore.Terraform. `app_images.pact` uses an
immutable Docker Hub digest. A separate database namespace shares the existing Mongo-compatible
cluster, and the one new Container App shares the existing environment. CPU/memory and bounded
readiness/startup/liveness settings support API execution and the pinned diff subprocess. Default
inputs create no Pact app. No provider/state migration or replacement cluster is introduced.

Use existing native ACA secrets for Mongo connection strings and publisher token hashes.
Application scopes remain ordinary configuration; raw tokens remain in source CI secrets.
Reject hosted wildcard scopes and incomplete/duplicate token-hash configuration. Public HTTPS
supports GitHub-hosted CI; optional stable IPv4 ingress restrictions/internal mode require matching
operator/runner connectivity. API bearer scope checks remain the write authorization boundary.

A non-writing helper verifies hosted Mongo readiness, liveness, anonymous-write rejection and
publisher application scope through deliberately invalid version metadata rejected before storage.
It refuses redirects and public plain HTTP. Container CI runs it against loopback. API tests
verify 400 for valid scope, 403 for wrong scope and 401 without credentials before unavailable
storage is reached. Image-metadata tests reject another source, platform or rebuilt image.

Terraform mocked plans verify intended configuration/rejections without Azure or remote state.
Actual image publication requires main-branch registry secrets; Azure plan/apply and hosted
verification remain operator actions. Do not equate green mocks/PR checks with an existing live
registry. [Deployment instructions](../deployment.md) describe inputs, receipts and verification.
