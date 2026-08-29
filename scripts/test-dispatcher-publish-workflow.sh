#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
WORKFLOW="$ROOT_DIR/.github/workflows/dispatcher-publish.yml"

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

assert_count() {
  expected_count=$1
  text=$2
  actual_count=$(grep -Fc -- "$text" "$WORKFLOW")
  if [ "$actual_count" -ne "$expected_count" ]; then
    echo "Expected '$text' to appear $expected_count times, found $actual_count." >&2
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

publish_draft_section() {
  awk '
    /^      - name: Publish draft dispatcher release$/ { printing = 1; next }
    printing && /^      - name:/ { exit }
    printing { print }
  ' "$WORKFLOW"
}

post_publish_section() {
  awk '
    /^  post-publish:/ { printing = 1 }
    printing { print }
  ' "$WORKFLOW"
}

assert_post_publish_before() {
  earlier=$1
  later=$2
  earlier_line=$(post_publish_section | grep -nF -- "$earlier" | head -n 1 | cut -d: -f 1)
  later_line=$(post_publish_section | grep -nF -- "$later" | head -n 1 | cut -d: -f 1)
  if [ -z "$earlier_line" ] || [ -z "$later_line" ] || [ "$earlier_line" -ge "$later_line" ]; then
    echo "Expected post-publish '$earlier' to appear before '$later'." >&2
    exit 1
  fi
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
  assert_contains "      artifact-metadata: write"
}

test_unprivileged_build_uses_only_the_approved_event_commit() {
  assert_not_contains "inputs.ref"
  assert_not_contains "INPUT_REF"
  assert_contains '          ref: ${{ github.sha }}'
  assert_count 2 "          persist-credentials: false"
  assert_contains "if: github.ref == 'refs/heads/main'"
  assert_not_contains "github.ref == 'refs/heads/main' || github.ref == 'refs/heads/v3-beta'"
}

test_publish_validates_metadata_without_checking_out_source() {
  assert_contains "      - name: Write release metadata"
  assert_contains "release-metadata.env"
  assert_contains "release-assets.manifest"
  assert_contains "      - name: Verify release metadata"
  assert_contains 'BUILD_SHA="${build_sha}"'
  assert_contains 'if [ "${BUILD_SHA}" != "${GITHUB_SHA}" ] || [ "${TARGET_SHA}" != "${GITHUB_SHA}" ]; then'
  assert_contains "      - name: Download packaged dispatcher assets"
  assert_contains "          path: ."
  assert_contains "            dist/dispatcher-release"
  assert_contains "            release-input"
  if publish_section | grep -F "actions/checkout" >/dev/null 2>&1; then
    echo "Publish job must not check out repository source." >&2
    exit 1
  fi
  installer_path_lines=$(publish_section | grep -F "scripts/" || true)
  if [ "${installer_path_lines}" != "          for installer_path in scripts/install.ps1 scripts/install.sh; do" ]; then
    echo "Publish job must not execute repository scripts." >&2
    exit 1
  fi
}

test_checkout_free_publish_has_explicit_repository_context() {
  if ! publish_section | grep -F '    GH_REPO: ${{ github.repository }}' >/dev/null 2>&1; then
    echo "Checkout-free publish job must define GH_REPO for gh release commands." >&2
    exit 1
  fi
}

test_dispatcher_assets_are_attested_after_the_manifest_is_verified() {
  assert_contains "      - name: Verify release asset manifest"
  assert_contains "      - name: Attest dispatcher release assets"
  assert_contains "          subject-path: dist/dispatcher-release/*"
  assert_contains "      - name: Attach attestation bundles to dispatcher assets"
  assert_contains "      - name: Upload dispatcher assets"
  assert_contains "      - name: Verify remote dispatcher release assets"
  assert_contains 'gh release view "${RELEASE_TAG}" --json assets'
  assert_contains 'bundle_name="${asset_name}.sigstore.json"'
  assert_contains '[ "${asset_count}" -ne 1 ] || [ "${bundle_count}" -ne 1 ] || [ "${asset_size}" -le 0 ] || [ "${bundle_size}" -le 0 ]'
  assert_contains "      - name: Verify dispatcher asset attestations"
  assert_before "      - name: Verify release asset manifest" "      - name: Attest dispatcher release assets"
  assert_before "      - name: Attest dispatcher release assets" "      - name: Verify dispatcher asset attestations"
  assert_before "      - name: Verify dispatcher asset attestations" "      - name: Attach attestation bundles to dispatcher assets"
  assert_before "      - name: Attach attestation bundles to dispatcher assets" "      - name: Upload dispatcher assets"
  assert_before "      - name: Upload dispatcher assets" "      - name: Verify remote dispatcher release assets"
  assert_before "      - name: Verify remote dispatcher release assets" "      - name: Publish draft dispatcher release"
}

test_dispatcher_verifies_tagged_installers_after_draft_creation() {
  assert_contains "      - name: Verify tagged dispatcher installer scripts"
  assert_contains 'for installer_path in scripts/install.ps1 scripts/install.sh; do'
  assert_contains 'gh api "repos/${GITHUB_REPOSITORY}/contents/${installer_path}?ref=${RELEASE_TAG}" --jq '\''.size'\'''
  assert_contains '[ "${installer_size}" -gt 0 ]'
  assert_before "      - name: Create or reuse draft dispatcher release" "      - name: Verify tagged dispatcher installer scripts"
  assert_before "      - name: Verify tagged dispatcher installer scripts" "      - name: Upload dispatcher assets"
  assert_before "      - name: Verify tagged dispatcher installer scripts" "      - name: Publish draft dispatcher release"
}

test_publish_rejects_manifest_and_existing_tag_mismatches() {
  assert_contains "Unexpected release files are not listed in the manifest."
  assert_contains "Release manifest is missing files or has duplicate entries."
  assert_contains 'gh api "repos/${GITHUB_REPOSITORY}/commits/${RELEASE_TAG}" --jq '\''.sha'\'''
  assert_contains 'Release tag ${RELEASE_TAG} does not match approved build commit ${GITHUB_SHA}.'
  assert_not_contains "release-input/dist/dispatcher-release"
}

test_release_tag_is_created_before_the_release() {
  assert_contains 'gh api "repos/${GITHUB_REPOSITORY}/git/refs" -f ref="refs/tags/${RELEASE_TAG}" -f sha="${GITHUB_SHA}"'
  assert_before 'gh api "repos/${GITHUB_REPOSITORY}/git/refs"' 'gh release create "${RELEASE_TAG}"'
}

test_draft_creation_accepts_only_the_known_missing_tag_responses() {
  assert_contains '*"HTTP 404"*) tag_sha="" ;;'
  assert_contains '*"HTTP 422"*"No commit found for SHA"*|*"No commit found for SHA"*"HTTP 422"*) tag_sha="" ;;'
  assert_count 2 'tag_sha="" ;;'
  assert_contains '*) printf '\''%s\n'\'' "${tag_error}" >&2; exit "${tag_status}" ;;'
}

test_dispatcher_publish_rechecks_the_tag_before_publishing() {
  assert_contains "      - name: Publish draft dispatcher release"
  if ! publish_draft_section | grep -F 'tag_sha=$(gh api "repos/${GITHUB_REPOSITORY}/commits/${RELEASE_TAG}" --jq '\''.sha'\'')' >/dev/null 2>&1; then
    echo "Publish draft dispatcher release must recheck the release tag SHA." >&2
    exit 1
  fi
}

test_dispatcher_build_preserves_release_checks() {
  assert_contains "concurrency:"
  assert_contains '  group: dispatcher-publish-${{ github.ref }}'
  assert_contains "      - name: Package dispatcher release assets"
  assert_contains "      - name: Verify packaged dispatcher release assets"
  assert_before "      - name: Package dispatcher release assets" "      - name: Verify packaged dispatcher release assets"
  assert_before "      - name: Verify packaged dispatcher release assets" "      - name: Write release metadata"
}

test_dispatcher_release_target_and_prerelease_state_remain_verified() {
  assert_contains 'gh api "repos/${GITHUB_REPOSITORY}/commits/${RELEASE_TAG}" --jq '\''.sha'\'''
  assert_contains 'Release tag ${RELEASE_TAG} does not match approved build commit ${GITHUB_SHA}.'
  assert_contains 'gh release edit "${RELEASE_TAG}" --draft=false --prerelease'
  assert_contains "      - name: Sync release-please package releases"
  assert_count 2 "      contents: write"
  if ! post_publish_section | grep -F "      - name: Setup Go" >/dev/null 2>&1; then
    echo "Post-publish job must set up the pinned Go toolchain." >&2
    exit 1
  fi
  if ! post_publish_section | grep -F "          go-version-file: cli/.go-version" >/dev/null 2>&1 || ! post_publish_section | grep -F "          cache-dependency-path: '**/go.sum'" >/dev/null 2>&1; then
    echo "Post-publish Go setup must use the repository pin and cache dependency path." >&2
    exit 1
  fi
  assert_post_publish_before "      - name: Setup Go" "      - name: Sync release-please package releases"
}

test_build_and_publish_jobs_have_separate_trust_boundaries
test_unprivileged_build_uses_only_the_approved_event_commit
test_publish_validates_metadata_without_checking_out_source
test_checkout_free_publish_has_explicit_repository_context
test_dispatcher_assets_are_attested_after_the_manifest_is_verified
test_dispatcher_verifies_tagged_installers_after_draft_creation
test_publish_rejects_manifest_and_existing_tag_mismatches
test_release_tag_is_created_before_the_release
test_draft_creation_accepts_only_the_known_missing_tag_responses
test_dispatcher_publish_rechecks_the_tag_before_publishing
test_dispatcher_build_preserves_release_checks
test_dispatcher_release_target_and_prerelease_state_remain_verified
