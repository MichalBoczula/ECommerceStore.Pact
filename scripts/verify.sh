#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

for stage in restore source format build test container; do
  bash scripts/ci.sh "$stage"
done
