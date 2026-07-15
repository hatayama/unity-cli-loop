#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
WORKFLOW="$ROOT_DIR/.github/workflows/native-cli-publish.yml"

assert_contains() {
  expected=$1
  if ! grep -F -- "$expected" "$WORKFLOW" >/dev/null 2>&1; then
    echo "Expected workflow to contain: $expected" >&2
    exit 1
  fi
}

assert_not_contains() {
  unexpected=$1
  if grep -F -- "$unexpected" "$WORKFLOW" >/dev/null 2>&1; then
    echo "Expected workflow not to contain: $unexpected" >&2
    exit 1
  fi
}

line_number() {
  text=$1
  grep -nF -- "$text" "$WORKFLOW" | head -n 1 | cut -d: -f 1
}

assert_before() {
  earlier=$1
  later=$2
  earlier_line=$(line_number "$earlier")
  later_line=$(line_number "$later")
  if [ -z "$earlier_line" ] || [ -z "$later_line" ] || [ "$earlier_line" -ge "$later_line" ]; then
    echo "Expected '$earlier' to appear before '$later'." >&2
    exit 1
  fi
}

publish_section() {
  awk '
    /^  publish:/ { printing = 1 }
    /^  post-publish:/ { exit }
    printing { print }
  ' "$WORKFLOW"
}

test_build_and_publish_jobs_have_separate_trust_boundaries() {
  assert_contains "  build:"
  assert_contains "  publish:"
  assert_contains "    needs: build"
  assert_contains "    environment: cli-release"
  assert_contains "      contents: read"
  assert_contains "      contents: write"
  assert_contains "      id-token: write"
  assert_contains "      attestations: write"
}

test_unprivileged_build_uses_only_the_approved_event_commit() {
  assert_not_contains "inputs.ref"
  assert_not_contains "inputs.release-tag"
  assert_not_contains "INPUT_REF"
  assert_contains '          ref: ${{ github.sha }}'
  assert_contains "github.ref == 'refs/heads/main' || github.ref == 'refs/heads/v3-beta'"
}

test_publish_validates_metadata_without_checking_out_source() {
  assert_contains "      - name: Write release metadata"
  assert_contains "release-metadata.env"
  assert_contains "release-assets.manifest"
  assert_contains "      - name: Verify release metadata"
  assert_contains 'BUILD_SHA="${build_sha}"'
  assert_contains 'if [ "${BUILD_SHA}" != "${GITHUB_SHA}" ] || [ "${TARGET_SHA}" != "${GITHUB_SHA}" ]; then'
  assert_contains "      - name: Download packaged release assets"
  if publish_section | grep -F "actions/checkout" >/dev/null 2>&1; then
    echo "Publish job must not check out repository source." >&2
    exit 1
  fi
  if publish_section | grep -F "scripts/" >/dev/null 2>&1; then
    echo "Publish job must not execute repository scripts." >&2
    exit 1
  fi
}

test_assets_are_attested_after_the_manifest_is_verified() {
  assert_contains "      - name: Verify release asset manifest"
  assert_contains "      - name: Attest native CLI release assets"
  assert_contains "          subject-path: release-input/dist/release/*"
  assert_contains "      - name: Attach attestation bundles to release assets"
  assert_contains "      - name: Upload native CLI assets"
}

test_build_verifies_assets_before_writing_the_publish_input() {
  assert_contains "      - name: Package native CLI release assets"
  assert_contains "      - name: Verify packaged release assets"
  assert_before "      - name: Package native CLI release assets" "      - name: Verify packaged release assets"
  assert_before "      - name: Verify packaged release assets" "      - name: Write release metadata"
}

test_post_publish_automation_remains_outside_the_privileged_job() {
  assert_contains "  post-publish:"
  assert_contains "      - name: Dispatch release-please after native CLI publish"
  assert_contains "      - name: Sync release-please package releases"
  assert_contains "      - name: Mark release PR as tagged"
}

test_build_and_publish_jobs_have_separate_trust_boundaries
test_unprivileged_build_uses_only_the_approved_event_commit
test_publish_validates_metadata_without_checking_out_source
test_assets_are_attested_after_the_manifest_is_verified
test_build_verifies_assets_before_writing_the_publish_input
test_post_publish_automation_remains_outside_the_privileged_job
