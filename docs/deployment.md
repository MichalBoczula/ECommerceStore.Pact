# Publish and deploy the Pact registry

## Release inputs

The Pact repository uses the store's quality-gated Docker Hub pattern. Configure secrets:

| Repository setting | Purpose |
| --- | --- |
| `DOCKER_USER` | Docker Hub username with push access to the image repository. |
| `DOCKER_TOKEN` | Docker Hub push token; do not reuse a publisher bearer token. |
| `DOCKER_IMAGE` (optional variable) | Namespace/repository; default `mb0101/ecommerce-store-pact-api`. |

`DOCKERHUB_USERNAME` / `DOCKERHUB_TOKEN` are compatibility fallbacks for existing service naming.
Set push secrets before merging the release PR. They are not needed for PR verification.
Source repositories later use separate `PACT_PUBLISHER_TOKEN` secrets for registry POSTs.

CI restores locked dependencies and runs the existing tests/format/source gates. The container
job builds a Linux/amd64 candidate once, smoke-tests that tag with real MongoDB and scans the
same image with Trivy, failing on fixed HIGH/CRITICAL findings and retaining the scan report.
Tests and scanning complete before image identity/archive creation.

The runtime build upgrades the inherited Ubuntu APT packages before switching to its non-root
user. This applies [CVE-2026-109518's security update](https://ubuntu.com/security/CVE-2026-109518)
(2.8.4ubuntu0.1 or newer on Ubuntu 24.04) even when the .NET base image still contains 2.8.3.
APT retries are disabled during that upgrade as recommended by Ubuntu. The fixed-HIGH/CRITICAL
image scan validates the resulting packages.

On a main push only, publication waits for the successful quality gate and container job.
It downloads the current run's archive, loads it and verifies platform, image ID, OCI source
revision and workflow commit. It pushes full commit and `latest` tags without rebuilding,
requires equal registry digests and writes an immutable deployment reference receipt. A PR or
manual dispatch never publishes. A missing push secret fails explicitly instead of skipping
publication. The temporary image archive is retained for one day; the small receipt for 30 days.

Download `pact-image-reference/image-reference.json` from the successful main workflow run.
It contains `imageReference`, such as `docker.io/mb0101/ecommerce-store-pact-api@sha256:...`.
This registry manifest digest differs from a local Docker image ID. Terraform accepts the
immutable digest reference for Pact; do not deploy `latest` or an unverified placeholder digest.

## Shared Azure configuration

Use [ECommerceStore.Terraform](https://github.com/MichalBoczula/ECommerceStore.Terraform), its
existing `terraform.tfvars` and backend/state key. The Pact companion change adds `app_images.pact`
and a stable `ca-ecommercestore-pact-dev` app to `cae-ecommercestore-dev`. Other applications stay
in the same image map. No alternate state/environment/cluster, deployment wrapper or paid database
tier is added. Default inputs leave Pact disabled.

Pact uses the existing MongoDB-compatible cluster with the dedicated `ecommerce-store-pact-db`
namespace. Connection credentials and SHA-256 publisher hashes use native ACA secret references,
following that root's existing convention. Plain environment values contain database name,
timeout, application scopes and HTTP hosting settings. Terraform state/plans still contain
sensitive connection/hash values and must retain their existing access protections.

Configure explicit consumer/provider publishers, for example:

```hcl
pact_publishers = {
  payments-ci = ["payments"]
  invoice-ci  = ["invoice"]
}
```

`TF_VAR_pact_publisher_token_sha256` supplies a JSON map of matching hashes. Each raw opaque
bearer token is at least 32 characters and belongs in its corresponding source repository's
`PACT_PUBLISHER_TOKEN` secret, outside Terraform. Hash the exact original UTF-8 token. Distinct
publishers must use distinct tokens; scopes are explicit application IDs. Missing/extra hashes,
empty hosted publisher configuration and wildcard scopes fail the hosted plan.

The ACA app uses 0–1 replicas, 0.5 CPU/1 GiB, port 8080, HTTP startup/liveness at `/health/live`
and MongoDB readiness at `/health/ready`. The 3-second driver/readiness timeout is bounded and
configurable from 1–30 seconds. No saved logs or extra cloud resources are introduced here.

Public HTTPS ingress supports GitHub-hosted source CI. Anonymous reads remain intentional;
every POST requires a scoped publisher token. Optional IPv4 CIDR restrictions apply to the
whole Pact ingress, including reads/probes from external operators. Use stable self-hosted runner
egress addresses if enabling an allowlist. Internal ingress requires a caller with connectivity
inside the ACA environment; it cannot be used directly by ordinary GitHub-hosted runners.

See the Terraform repository's **Pact registry** section for PowerShell hash generation and
the normal `terraform init`, `plan -out=...`, review and `apply` sequence. Preserve all existing
image entries and passwords. In an unchanged deployment, the intended addition is one ACA app.
Review unexpected replacements, credential rotations or unrelated changes before apply.

## Hosted verification

After applying the reviewed plan, get the Pact URL from `terraform output -json app_urls`.
From this repository, supply the raw publisher token securely in `PACT_PUBLISHER_TOKEN`:

```powershell
$pactUrl = (terraform output -json app_urls | ConvertFrom-Json).pact
$env:PACT_PUBLISHER_TOKEN = Read-Host 'Payments publisher token' -AsSecureString | ConvertFrom-SecureString -AsPlainText
python scripts/verify-deployment.py --url $pactUrl --application payments
Remove-Item Env:PACT_PUBLISHER_TOKEN
```

Run the output command in the Terraform checkout, then the helper from the Pact checkout (or
use its absolute path). The helper waits up to 180 seconds for MongoDB readiness, checks
liveness, asserts anonymous POST returns 401, and expects a known repository-URL validation
400 from the authenticated application scope. The deliberately invalid URL is rejected before
any version persistence; 401/403 or another response fails verification. It never follows a
redirect with credentials. `--allow-local-http` permits literal loopback only for container CI.

The helper verifies reachability, connectivity and write access configuration without creating
data. Real contract registration/verification is exercised by source CI in Tasks 08–09. Terraform
mock tests and local/PR container tests do not prove Azure capacity, permissions, firewall rules
or all DocumentDB Mongo behavior. Record actual hosted readiness and the deployed image receipt
after operator apply before treating the cloud acceptance criterion as complete.

## Rotation and rollback

Rotate each source publisher token together with its matching Terraform SHA-256 map value.
Apply updated ACA secrets, restart the active revision after a secret-only update as documented
in the Terraform README, and verify the scope with the helper. Runtime publisher identity is
server-derived and existing registry records remain immutable. Roll back the Pact image by
planning/applying a previously verified digest with the same database and scoped configuration;
do not destroy or recreate shared infrastructure to roll back one service.
