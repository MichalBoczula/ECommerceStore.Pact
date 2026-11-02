#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
: "${GITHUB_SHA:?Source commit is required}"
: "${PACT_IMAGE_REPOSITORY:?Docker Hub repository is required}"

# Validate repository syntax before passing it to Docker.
[[ "$PACT_IMAGE_REPOSITORY" =~ ^[a-z0-9]+([._-][a-z0-9]+)*/[a-z0-9]+([._-][a-z0-9]+)*$ ]] || { echo 'Invalid Docker Hub repository' >&2; exit 1; }
metadata=artifacts/image/image.json
docker image inspect ecommerce-store-pact:ci > "$RUNNER_TEMP/loaded-image.json"
python3 scripts/image-metadata.py verify --inspect "$RUNNER_TEMP/loaded-image.json" --metadata "$metadata" --commit "$GITHUB_SHA"
image_id=$(docker image inspect --format '{{.Id}}' ecommerce-store-pact:ci)
sha_tag="$PACT_IMAGE_REPOSITORY:$GITHUB_SHA"
latest_tag="$PACT_IMAGE_REPOSITORY:latest"
docker tag "$image_id" "$sha_tag"
docker tag "$image_id" "$latest_tag"
test "$image_id" = "$(docker image inspect --format '{{.Id}}' "$sha_tag")"
test "$image_id" = "$(docker image inspect --format '{{.Id}}' "$latest_tag")"
docker push "$sha_tag" | tee "$RUNNER_TEMP/pact-push-sha.log"
docker push "$latest_tag" | tee "$RUNNER_TEMP/pact-push-latest.log"
sha_digest=$(awk '/digest: sha256:/ { print $3 }' "$RUNNER_TEMP/pact-push-sha.log" | tail -n 1)
latest_digest=$(awk '/digest: sha256:/ { print $3 }' "$RUNNER_TEMP/pact-push-latest.log" | tail -n 1)
test -n "$sha_digest"
test "$sha_digest" = "$latest_digest"
python3 scripts/image-metadata.py receipt --metadata "$metadata" --repository "$PACT_IMAGE_REPOSITORY" --digest "$sha_digest" --out artifacts/image/image-reference.json
printf 'Published the tested/scanned image `%s`.\n\nDeploy with `docker.io/%s@%s`.\n' "$image_id" "$PACT_IMAGE_REPOSITORY" "$sha_digest" >> "$GITHUB_STEP_SUMMARY"
