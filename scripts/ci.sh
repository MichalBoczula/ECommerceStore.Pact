#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

case "${1:-}" in
  restore)
    dotnet restore ECommerceStore.Pact.slnx --locked-mode
    ;;
  source)
    git diff --check
    bash -n scripts/ci.sh scripts/verify.sh scripts/smoke-container.sh
    python3 scripts/check-architecture.py
    ;;
  format)
    dotnet format ECommerceStore.Pact.slnx --verify-no-changes --no-restore
    ;;
  build)
    dotnet build ECommerceStore.Pact.slnx -c Release --no-restore
    ;;
  test)
    dotnet test ECommerceStore.Pact.slnx -c Release --no-build \
      --logger trx --results-directory "$PWD/artifacts/tests"
    ;;
  container)
    bash scripts/smoke-container.sh
    ;;
  *)
    echo "Usage: bash scripts/ci.sh {restore|source|format|build|test|container}" >&2
    exit 2
    ;;
esac
