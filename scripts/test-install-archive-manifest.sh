#!/bin/sh
# Verifies install.sh's ULOOP_ARCHIVE_MANIFEST verification handles a matching
# digest, a mismatch, a missing entry, and an unset env consistently. Locks in
# the exact POSIX-sh semantics so a later refactor cannot silently regress the
# defense-in-depth over the same-origin .sha256 file.
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
INSTALL_SCRIPT="$ROOT_DIR/scripts/install.sh"

if ! command -v awk >/dev/null 2>&1; then
  echo "awk is required for this test" >&2
  exit 1
fi

# Extract the two functions from install.sh so the test does not have to
# re-run the entire installer top-to-bottom (which would try to reach
# api.github.com and mutate $HOME/.local/bin).
extract_function() {
  name=$1
  awk -v want="$name" '
    $0 ~ ("^" want "\\(\\)") { in_fn = 1 }
    in_fn { print }
    in_fn && $0 == "}" { exit }
  ' "$INSTALL_SCRIPT"
}

TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT INT HUP TERM

tmp_dir="$TMP_DIR"
asset_name="uloop-dispatcher-darwin-arm64.zip"
printf 'archive payload\n' > "$tmp_dir/$asset_name"

# Compute the reference digest with whichever sha tool we have available.
if command -v sha256sum >/dev/null 2>&1; then
  actual_hash=$(sha256sum "$tmp_dir/$asset_name" | awk '{print $1}')
else
  actual_hash=$(shasum -a 256 "$tmp_dir/$asset_name" | awk '{print $1}')
fi

# Source the two helpers extracted from install.sh so tests exercise the real
# implementations, not a copy that could drift.
eval "$(extract_function compute_asset_sha256)"
eval "$(extract_function verify_archive_attestation_manifest)"

expect_pass() {
  label=$1
  # Why: verify_archive_attestation_manifest uses `exit 1` on failure, so it
  # must run in a subshell to prevent the test harness itself from exiting.
  if ! (verify_archive_attestation_manifest) 2>/dev/null; then
    echo "FAIL: $label — expected pass but got failure" >&2
    exit 1
  fi
}

expect_fail() {
  label=$1
  if (verify_archive_attestation_manifest) 2>/dev/null; then
    echo "FAIL: $label — expected failure but got pass" >&2
    exit 1
  fi
}

# 1. Unset env: first installation has no preverified manifest, so accepting
#    it would permit the same-origin checksum-only fallback that the approved
#    trust model forbids.
unset ULOOP_ARCHIVE_MANIFEST || true
expect_fail "unset manifest fails closed"

# 2. Matching manifest entry: accept and continue.
ULOOP_ARCHIVE_MANIFEST="$actual_hash  $asset_name
0000000000000000000000000000000000000000000000000000000000000000  install.sh"
export ULOOP_ARCHIVE_MANIFEST
expect_pass "matching manifest digest accepted"

# 3. Mismatched digest: reject.
ULOOP_ARCHIVE_MANIFEST="0000000000000000000000000000000000000000000000000000000000000000  $asset_name"
export ULOOP_ARCHIVE_MANIFEST
expect_fail "mismatched manifest digest rejected"

# 4. Missing asset entry: reject rather than silently pass, otherwise a
#    tampered filename could bypass the check by omitting itself from the
#    manifest.
ULOOP_ARCHIVE_MANIFEST="1111111111111111111111111111111111111111111111111111111111111111  install.sh"
export ULOOP_ARCHIVE_MANIFEST
expect_fail "missing manifest entry for asset_name rejected"

echo "OK"
