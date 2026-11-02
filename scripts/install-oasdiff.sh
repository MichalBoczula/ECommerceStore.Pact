#!/bin/sh
set -eu
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_dir/oasdiff-version.env"
case "$(uname -s)/$(uname -m)" in
  Linux/x86_64) archive_arch=amd64; archive_hash=$OASDIFF_SHA256_AMD64 ;;
  Linux/aarch64|Linux/arm64) archive_arch=arm64; archive_hash=$OASDIFF_SHA256_ARM64 ;;
  *) echo 'Use the pinned official oasdiff release for your platform; this installer supports Linux amd64/arm64.' >&2; exit 1 ;;
esac
install_dir=${1:?Usage: sh scripts/install-oasdiff.sh /absolute/install/directory}
mkdir -p "$install_dir"
scratch_dir=$(mktemp -d)
trap 'rm -rf "$scratch_dir"' EXIT HUP INT TERM
curl --fail --silent --show-error --location --retry 3 \
  "https://github.com/oasdiff/oasdiff/releases/download/v$OASDIFF_VERSION/oasdiff_${OASDIFF_VERSION}_linux_${archive_arch}.tar.gz" \
  -o "$scratch_dir/archive.tar.gz"
printf '%s  %s\n' "$archive_hash" "$scratch_dir/archive.tar.gz" | sha256sum -c -
tar --no-same-owner -xzf "$scratch_dir/archive.tar.gz" -C "$scratch_dir" oasdiff LICENSE
cp "$scratch_dir/oasdiff" "$install_dir/oasdiff"
cp "$scratch_dir/LICENSE" "$install_dir/oasdiff-LICENSE"
chmod 755 "$install_dir/oasdiff"
"$install_dir/oasdiff" --version
