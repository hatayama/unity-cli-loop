#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
WORKFLOW="$ROOT_DIR/.github/workflows/native-cli-publish.yml"

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

test_attestation_permissions() {
  assert_contains "$WORKFLOW" "  actions: write"
  assert_contains "$WORKFLOW" "  id-token: write"
  assert_contains "$WORKFLOW" "  attestations: write"
  assert_contains "$WORKFLOW" "  artifact-metadata: write"
}

test_go_is_available_for_package_release_sync() {
  assert_contains "$WORKFLOW" "      - name: Setup Go"
  assert_contains "$WORKFLOW" "        if: steps.release.outputs.publish == 'true' || steps.release.outputs.release == 'true'"
  assert_before "$WORKFLOW" "      - name: Setup Go" "      - name: Sync release-please package releases"
}

test_release_assets_are_attested() {
  assert_contains "$WORKFLOW" "      - name: Attest native CLI release assets"
  assert_contains "$WORKFLOW" "        if: steps.release.outputs.publish == 'true' && steps.release.outputs.dry_run != 'true'"
  assert_contains "$WORKFLOW" "        uses: actions/attest@59d89421af93a897026c735860bf21b6eb4f7b26"
  assert_contains "$WORKFLOW" "          subject-path: dist/release/*"
  assert_before "$WORKFLOW" "      - name: Verify packaged release assets" "      - name: Attest native CLI release assets"
  assert_before "$WORKFLOW" "      - name: Attest native CLI release assets" "      - name: Upload native CLI assets"
}

test_release_asset_attestations_are_verified() {
  assert_contains "$WORKFLOW" "      - name: Verify native CLI asset attestations"
  assert_contains "$WORKFLOW" '          SIGNER_WORKFLOW: ${{ github.repository }}/.github/workflows/native-cli-publish.yml'
  assert_contains "$WORKFLOW" '          for asset_path in dist/release/*; do'
  assert_contains "$WORKFLOW" '            gh attestation verify "${asset_path}" \'
  assert_contains "$WORKFLOW" '              --repo "${GITHUB_REPOSITORY}" \'
  assert_contains "$WORKFLOW" '              --signer-workflow "${SIGNER_WORKFLOW}"'
  assert_before "$WORKFLOW" "      - name: Attest native CLI release assets" "      - name: Verify native CLI asset attestations"
  assert_before "$WORKFLOW" "      - name: Verify native CLI asset attestations" "      - name: Upload native CLI assets"
}

test_release_please_is_dispatched_after_publish() {
  assert_contains "$WORKFLOW" "      - name: Dispatch release-please after native CLI publish"
  assert_contains "$WORKFLOW" "        if: github.event_name == 'push' && steps.release.outputs.publish == 'true' && steps.release.outputs.dry_run != 'true'"
  assert_contains "$WORKFLOW" '          TARGET_BRANCH: ${{ github.ref_name }}'
  assert_contains "$WORKFLOW" '          gh workflow run release-please.yml --ref "${TARGET_BRANCH}" -f branch="${TARGET_BRANCH}"'
  assert_before "$WORKFLOW" "      - name: Publish draft release" "      - name: Dispatch release-please after native CLI publish"
  assert_before "$WORKFLOW" "      - name: Dispatch release-please after native CLI publish" "      - name: Sync release-please package releases"
}

test_native_cli_attestation_bundles_are_distributed_per_asset() {
  assert_contains "$WORKFLOW" "      - name: Distribute attestation bundles per asset"
  assert_contains "$WORKFLOW" "          scripts/distribute-attestation-bundles.sh \\"
  assert_contains "$WORKFLOW" "            --bundle \"\${BUNDLE_PATH}\" \\"
  assert_contains "$WORKFLOW" "            --release-dir dist/release"
  assert_before "$WORKFLOW" "      - name: Verify native CLI asset attestations" "      - name: Distribute attestation bundles per asset"
  assert_before "$WORKFLOW" "      - name: Distribute attestation bundles per asset" "      - name: Upload native CLI assets"
  assert_contains "$WORKFLOW" "          BUNDLE_NAME=\"\${asset_name}.sigstore.json\""
  assert_contains "$WORKFLOW" "              echo \"Missing remote native CLI attestation bundle: \${BUNDLE_NAME}\" >&2"
}

test_attestation_permissions
test_go_is_available_for_package_release_sync
test_release_assets_are_attested
test_release_asset_attestations_are_verified
test_release_please_is_dispatched_after_publish
test_native_cli_attestation_bundles_are_distributed_per_asset
