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

test_dispatcher_beta_releases_are_marked_prerelease() {
  assert_contains "$WORKFLOW" '          PRERELEASE_FLAG=""'
  assert_contains "$WORKFLOW" '              PRERELEASE_FLAG="--prerelease"'
  assert_contains "$WORKFLOW" '              gh release edit "${RELEASE_TAG}" --draft=false --prerelease'
  assert_before "$WORKFLOW" '              PRERELEASE_FLAG="--prerelease"' '          gh release create "${RELEASE_TAG}" \'
  assert_before "$WORKFLOW" '              gh release edit "${RELEASE_TAG}" --draft=false --prerelease' '      - name: Sync release-please package releases'
}

test_package_release_sync_runs_after_dispatcher_publish() {
  assert_contains "$WORKFLOW" "      - name: Sync release-please package releases"
  assert_before "$WORKFLOW" "      - name: Publish draft dispatcher release" "      - name: Sync release-please package releases"
}

test_dispatcher_resolver_is_used
test_dispatcher_assets_are_packaged_and_verified
test_installer_scripts_are_verified_before_upload
test_existing_dispatcher_tag_target_is_checked
test_dispatcher_beta_releases_are_marked_prerelease
test_package_release_sync_runs_after_dispatcher_publish
