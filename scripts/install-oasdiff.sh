#!/bin/sh
set -eu
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_dir/oasdiff-version.env"
case "$(uname -s)/$(uname -m)" in
  Linux/x86_64|Linux/aarch64|Linux/arm64) ;;
  *) echo 'This source installer supports Linux amd64/arm64.' >&2; exit 1 ;;
esac
if [ "$(GOTOOLCHAIN=local go env GOVERSION)" != "go$OASDIFF_GO_VERSION" ]; then
  echo "Install Go $OASDIFF_GO_VERSION before building the pinned comparison tool." >&2
  exit 1
fi
install_dir=${1:?Usage: sh scripts/install-oasdiff.sh /absolute/install/directory}
mkdir -p "$install_dir"
install_dir=$(CDPATH= cd -- "$install_dir" && pwd)
scratch_dir=$(mktemp -d)
trap 'rm -rf "$scratch_dir"' EXIT HUP INT TERM
curl --fail --silent --show-error --location --retry 3 \
  "https://github.com/oasdiff/oasdiff/archive/$OASDIFF_SOURCE_COMMIT.tar.gz" \
  -o "$scratch_dir/archive.tar.gz"
printf '%s  %s\n' "$OASDIFF_SOURCE_SHA256" "$scratch_dir/archive.tar.gz" | sha256sum -c -
mkdir "$scratch_dir/source"
tar --no-same-owner -xzf "$scratch_dir/archive.tar.gz" -C "$scratch_dir/source" --strip-components=1
(
  cd "$scratch_dir/source"
  CGO_ENABLED=0 GOTOOLCHAIN=local go build -mod=readonly -trimpath -buildvcs=false \
    -ldflags "-s -w -X github.com/oasdiff/oasdiff/build.Version=$OASDIFF_VERSION" \
    -o "$install_dir/oasdiff" .
)
cp "$scratch_dir/source/LICENSE" "$install_dir/oasdiff-LICENSE"
chmod 755 "$install_dir/oasdiff"
"$install_dir/oasdiff" --version
