#!/bin/sh
set -eu

# Copy the Sigstore attestation bundle produced by actions/attest to a per-asset
# `<asset>.sigstore.json` file for every release asset in RELEASE_DIR. Every
# asset gets an identical copy of the single bundle because actions/attest
# emits one bundle that covers all matched subjects. Client verifiers only need
# to match the target asset digest against the bundle's subject list.

usage() {
  cat <<'USAGE' >&2
Usage: distribute-attestation-bundles.sh --bundle <path> --release-dir <dir>
USAGE
  exit 2
}

BUNDLE_PATH=""
RELEASE_DIR=""

while [ $# -gt 0 ]; do
  case "$1" in
    --bundle)
      BUNDLE_PATH="${2:-}"
      shift 2
      ;;
    --release-dir)
      RELEASE_DIR="${2:-}"
      shift 2
      ;;
    -h|--help)
      usage
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      ;;
  esac
done

if [ -z "$BUNDLE_PATH" ] || [ -z "$RELEASE_DIR" ]; then
  usage
fi

if [ ! -s "$BUNDLE_PATH" ]; then
  echo "Attestation bundle is missing or empty: $BUNDLE_PATH" >&2
  exit 1
fi

if [ ! -d "$RELEASE_DIR" ]; then
  echo "Release directory does not exist: $RELEASE_DIR" >&2
  exit 1
fi

distributed_count=0
for asset_path in "$RELEASE_DIR"/*; do
  [ -f "$asset_path" ] || continue
  case "$asset_path" in
    *.sigstore.json) continue ;;
  esac
  cp "$BUNDLE_PATH" "$asset_path.sigstore.json"
  distributed_count=$((distributed_count + 1))
done

if [ "$distributed_count" -eq 0 ]; then
  echo "No release assets found under $RELEASE_DIR to attach attestations to." >&2
  exit 1
fi

echo "Distributed attestation bundle to $distributed_count assets under $RELEASE_DIR."
