#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
WORKFLOW="$ROOT_DIR/.github/workflows/dispatcher-publish.yml"

assert_contains() {
  file=$1
  expected=$2
  if ! grep -F -- "$expected" "$file" >/dev/null 2>&1; then
    echo "Expected $file to contain: $expected" >&2
    exit 1
  fi
}

assert_not_contains() {
  file=$1
  unexpected=$2
  if grep -F -- "$unexpected" "$file" >/dev/null 2>&1; then
    echo "Expected $file not to contain: $unexpected" >&2
    exit 1
  fi
}

line_number() {
  file=$1
  text=$2
  grep -nF -- "$text" "$file" | head -n 1 | cut -d: -f 1
}

assert_before() {
  file=$1
  earlier=$2
  later=$3
  earlier_line=$(line_number "$file" "$earlier")
  later_line=$(line_number "$file" "$later")
  if [ -z "$earlier_line" ] || [ -z "$later_line" ] || [ "$earlier_line" -ge "$later_line" ]; then
    echo "Expected '$earlier' to appear before '$later' in $file." >&2
    exit 1
  fi
}

test_dispatcher_resolver_is_used() {
  assert_contains "$WORKFLOW" "      - name: Resolve dispatcher release target"
  assert_contains "$WORKFLOW" '          scripts/resolve-dispatcher-release-target.sh >> "$GITHUB_OUTPUT"'
}

test_dispatcher_publish_is_serialized_by_branch() {
  assert_contains "$WORKFLOW" "concurrency:"
  assert_contains "$WORKFLOW" '  group: dispatcher-publish-${{ github.ref }}'
  assert_contains "$WORKFLOW" "  cancel-in-progress: false"
}

test_dispatcher_assets_are_packaged_and_verified() {
  assert_contains "$WORKFLOW" "      - name: Package dispatcher release assets"
  assert_contains "$WORKFLOW" "        run: scripts/package-dispatcher.sh"
  assert_contains "$WORKFLOW" "      - name: Verify packaged dispatcher release assets"
  assert_contains "$WORKFLOW" "        run: scripts/verify-dispatcher-release-assets.sh"
  assert_before "$WORKFLOW" "      - name: Package dispatcher release assets" "      - name: Verify packaged dispatcher release assets"
}

test_installer_scripts_are_verified_before_upload() {
  assert_contains "$WORKFLOW" "      - name: Verify release-tagged installer scripts"
  assert_contains "$WORKFLOW" "          ./scripts/check-release-installer.ps1 -Version \$env:RELEASE_TAG"
  assert_before "$WORKFLOW" "      - name: Create or reuse draft dispatcher release" "      - name: Verify release-tagged installer scripts"
  assert_before "$WORKFLOW" "      - name: Verify release-tagged installer scripts" "      - name: Upload dispatcher assets"
}

test_existing_dispatcher_tag_target_is_checked() {
  assert_contains "$WORKFLOW" '          EXISTING_TAG_SHA=$(git rev-list -n 1 "${RELEASE_TAG}" 2>/dev/null || true)'
  assert_contains "$WORKFLOW" '          if [ -n "${EXISTING_TAG_SHA}" ] && [ "${EXISTING_TAG_SHA}" != "${TARGET_SHA}" ]; then'
  assert_contains "$WORKFLOW" '            echo "Dispatcher release tag ${RELEASE_TAG} points at ${EXISTING_TAG_SHA}, expected ${TARGET_SHA}." >&2'
  assert_before "$WORKFLOW" '          EXISTING_TAG_SHA=$(git rev-list -n 1 "${RELEASE_TAG}" 2>/dev/null || true)' '      - name: Verify release-tagged installer scripts'
}

test_dispatcher_draft_state_uses_runner_temp() {
  assert_contains "$WORKFLOW" '          DRAFT_STATE_PATH="${RUNNER_TEMP}/dispatcher-release-draft-${GITHUB_RUN_ID}"'
  assert_contains "$WORKFLOW" '          if gh release view "${RELEASE_TAG}" --json isDraft --jq '"'"'.isDraft'"'"' >"${DRAFT_STATE_PATH}" 2>/dev/null; then'
  assert_contains "$WORKFLOW" '            IS_DRAFT=$(cat "${DRAFT_STATE_PATH}")'
  assert_not_contains "$WORKFLOW" "/tmp/dispatcher-release-draft"
}

test_dispatcher_beta_releases_are_marked_prerelease() {
  assert_contains "$WORKFLOW" '          RELEASE_PRERELEASE: ${{ steps.release.outputs.prerelease }}'
  assert_contains "$WORKFLOW" '          GITHUB_REPOSITORY: ${{ github.repository }}'
  assert_contains "$WORKFLOW" '          PRERELEASE_FLAG=""'
  assert_contains "$WORKFLOW" '          LATEST_FLAG=""'
  assert_contains "$WORKFLOW" '          if [ "${RELEASE_PRERELEASE}" = "true" ]; then'
  assert_contains "$WORKFLOW" '            PRERELEASE_FLAG="--prerelease"'
  assert_contains "$WORKFLOW" '            LATEST_FLAG="--latest=false"'
  assert_contains "$WORKFLOW" '            ${PRERELEASE_FLAG} \'
  assert_contains "$WORKFLOW" '            ${LATEST_FLAG}'
  assert_contains "$WORKFLOW" '            gh release edit "${RELEASE_TAG}" --draft=false --prerelease'
  assert_contains "$WORKFLOW" '            RELEASE_ID=$(gh api "repos/${GITHUB_REPOSITORY}/releases/tags/${RELEASE_TAG}" --jq '"'"'.id'"'"')'
  assert_contains "$WORKFLOW" '            gh api -X PATCH "repos/${GITHUB_REPOSITORY}/releases/${RELEASE_ID}" -F prerelease=true -F make_latest=false >/dev/null'
  assert_before "$WORKFLOW" '            PRERELEASE_FLAG="--prerelease"' '          gh release create "${RELEASE_TAG}" \'
  assert_before "$WORKFLOW" '            gh release edit "${RELEASE_TAG}" --draft=false --prerelease' '      - name: Sync release-please package releases'
}

test_package_release_sync_runs_after_dispatcher_publish() {
  assert_contains "$WORKFLOW" "      - name: Sync release-please package releases"
  assert_before "$WORKFLOW" "      - name: Publish draft dispatcher release" "      - name: Sync release-please package releases"
}

test_dispatcher_attestation_bundles_are_distributed_per_asset() {
  assert_contains "$WORKFLOW" "      - name: Distribute attestation bundles per asset"
  assert_contains "$WORKFLOW" "          scripts/distribute-attestation-bundles.sh \\"
  assert_contains "$WORKFLOW" "            --bundle \"\${BUNDLE_PATH}\" \\"
  assert_contains "$WORKFLOW" "            --release-dir dist/dispatcher-release"
  assert_before "$WORKFLOW" "      - name: Verify dispatcher asset attestations" "      - name: Distribute attestation bundles per asset"
  assert_before "$WORKFLOW" "      - name: Distribute attestation bundles per asset" "      - name: Upload dispatcher assets"
  assert_contains "$WORKFLOW" "          BUNDLE_NAME=\"\${asset_name}.sigstore.json\""
  assert_contains "$WORKFLOW" "              echo \"Missing remote dispatcher attestation bundle: \${BUNDLE_NAME}\" >&2"
}

test_dispatcher_resolver_is_used
test_dispatcher_publish_is_serialized_by_branch
test_dispatcher_assets_are_packaged_and_verified
test_installer_scripts_are_verified_before_upload
test_existing_dispatcher_tag_target_is_checked
test_dispatcher_draft_state_uses_runner_temp
test_dispatcher_beta_releases_are_marked_prerelease
test_package_release_sync_runs_after_dispatcher_publish
test_dispatcher_attestation_bundles_are_distributed_per_asset
