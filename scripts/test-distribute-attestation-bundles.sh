#!/bin/sh
set -eu

# Local test for distribute-attestation-bundles.sh. Runs the script with a
# fake bundle and asserts every non-bundle asset gets a matching
# `<asset>.sigstore.json` file.

SCRIPT_DIR=$(CDPATH= cd "$(dirname "$0")" && pwd)
DISTRIBUTE="$SCRIPT_DIR/distribute-attestation-bundles.sh"

TEST_DIR=$(mktemp -d)
trap 'rm -rf "$TEST_DIR"' EXIT

RELEASE_DIR="$TEST_DIR/release"
mkdir -p "$RELEASE_DIR"
touch "$RELEASE_DIR/asset-a.tar.gz"
touch "$RELEASE_DIR/asset-a.tar.gz.sha256"
touch "$RELEASE_DIR/asset-b.zip"
touch "$RELEASE_DIR/asset-b.zip.sha256"

BUNDLE_PATH="$TEST_DIR/attestation-bundle.sigstore"
printf '{"fake":"bundle"}\n' > "$BUNDLE_PATH"

"$DISTRIBUTE" --bundle "$BUNDLE_PATH" --release-dir "$RELEASE_DIR" >/dev/null

for asset in asset-a.tar.gz asset-a.tar.gz.sha256 asset-b.zip asset-b.zip.sha256; do
  bundle_copy="$RELEASE_DIR/$asset.sigstore.json"
  if [ ! -s "$bundle_copy" ]; then
    echo "FAIL: expected $bundle_copy to exist and be non-empty" >&2
    exit 1
  fi
  if ! diff -q "$BUNDLE_PATH" "$bundle_copy" >/dev/null; then
    echo "FAIL: $bundle_copy differs from source bundle" >&2
    exit 1
  fi
done

# Ensure sigstore.json files do not get their own .sigstore.json duplicates.
for extra in $(find "$RELEASE_DIR" -maxdepth 1 -type f -name "*.sigstore.json.sigstore.json"); do
  echo "FAIL: found double-suffix bundle $extra" >&2
  exit 1
done

# Ensure the script fails when the bundle path is missing.
if "$DISTRIBUTE" --bundle "$TEST_DIR/missing.sigstore" --release-dir "$RELEASE_DIR" >/dev/null 2>&1; then
  echo "FAIL: expected failure for missing bundle" >&2
  exit 1
fi

# Ensure the script fails when the release directory has no candidate assets.
EMPTY_DIR="$TEST_DIR/empty"
mkdir -p "$EMPTY_DIR"
if "$DISTRIBUTE" --bundle "$BUNDLE_PATH" --release-dir "$EMPTY_DIR" >/dev/null 2>&1; then
  echo "FAIL: expected failure for empty release directory" >&2
  exit 1
fi

echo "OK distribute-attestation-bundles.sh"
