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
  assert_contains "$WORKFLOW" "  id-token: write"
  assert_contains "$WORKFLOW" "  attestations: write"
  assert_contains "$WORKFLOW" "  artifact-metadata: write"
}

test_release_assets_are_attested() {
  assert_contains "$WORKFLOW" "      - name: Attest native CLI release assets"
  assert_contains "$WORKFLOW" "        if: steps.release.outputs.publish == 'true' && steps.release.outputs.dry_run != 'true'"
  assert_contains "$WORKFLOW" "        uses: actions/attest@59d89421af93a897026c735860bf21b6eb4f7b26"
  assert_contains "$WORKFLOW" "          subject-path: cli/dist/release/*"
  assert_before "$WORKFLOW" "      - name: Verify packaged release assets" "      - name: Attest native CLI release assets"
  assert_before "$WORKFLOW" "      - name: Attest native CLI release assets" "      - name: Upload native CLI assets"
}

test_release_asset_attestations_are_verified() {
  assert_contains "$WORKFLOW" "      - name: Verify native CLI asset attestations"
  assert_contains "$WORKFLOW" '          SIGNER_WORKFLOW: ${{ github.repository }}/.github/workflows/native-cli-publish.yml'
  assert_contains "$WORKFLOW" '          for asset_path in cli/dist/release/*; do'
  assert_contains "$WORKFLOW" '            gh attestation verify "${asset_path}" \'
  assert_contains "$WORKFLOW" '              --repo "${GITHUB_REPOSITORY}" \'
  assert_contains "$WORKFLOW" '              --signer-workflow "${SIGNER_WORKFLOW}"'
  assert_before "$WORKFLOW" "      - name: Attest native CLI release assets" "      - name: Verify native CLI asset attestations"
  assert_before "$WORKFLOW" "      - name: Verify native CLI asset attestations" "      - name: Upload native CLI assets"
}

test_release_tagged_installer_scripts_are_verified() {
  assert_contains "$WORKFLOW" "      - name: Verify release-tagged installer scripts"
  assert_contains "$WORKFLOW" "        shell: pwsh"
  assert_contains "$WORKFLOW" "          ./scripts/check-release-installer.ps1 -Version \$env:RELEASE_TAG"
  assert_before "$WORKFLOW" "      - name: Create or reuse draft release" "      - name: Verify release-tagged installer scripts"
  assert_before "$WORKFLOW" "      - name: Verify release-tagged installer scripts" "      - name: Upload native CLI assets"
}

test_attestation_permissions
test_release_assets_are_attested
test_release_asset_attestations_are_verified
test_release_tagged_installer_scripts_are_verified
