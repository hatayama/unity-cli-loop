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
    /^      - name: Publish draft release$/ { printing = 1; next }
    printing && /^      - name:/ { exit }
    printing { print }
  ' "$WORKFLOW"
}

remote_attestation_verification_section() {
  awk '
    /^      - name: Verify remote attestation digest matches release tag$/ { printing = 1; next }
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
  assert_not_contains "inputs.release-tag"
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
  assert_contains "      - name: Download packaged release assets"
  assert_contains "          path: ."
  assert_contains "            dist/release"
  assert_contains "            release-input"
  if publish_section | grep -F "actions/checkout" >/dev/null 2>&1; then
    echo "Publish job must not check out repository source." >&2
    exit 1
  fi
  if publish_section | grep -F "scripts/" >/dev/null 2>&1; then
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

test_publish_has_no_recovery_target_mode() {
  # Verify publishing always uses the event-head release target without a recovery-only path.
  assert_not_contains "recovery-target"
  assert_not_contains "RECOVERY_TARGET"
  assert_not_contains "Recovery-target"
  assert_not_contains "Check out resolver-derived recovery target"
  assert_contains 'printf '\''RELEASE_SHA=%s\n'\'' "${release_sha}" >> "$GITHUB_ENV"'
  assert_count 3 'if [ "${tag_sha}" != "${RELEASE_SHA}" ]; then'
  assert_contains 'if [ -n "${tag_sha}" ] && [ "${tag_sha}" != "${RELEASE_SHA}" ]; then'
  assert_contains '--target "${RELEASE_SHA}"'
}

test_assets_are_attested_after_the_manifest_is_verified() {
  assert_contains "      - name: Verify release asset manifest"
  assert_contains "      - name: Attest native CLI release assets"
  assert_contains "          subject-path: dist/release/*"
  assert_contains "      - name: Attach attestation bundles to release assets"
  assert_contains "      - name: Upload native CLI assets"
  assert_contains "      - name: Verify remote release assets"
  assert_contains 'gh release view "${RELEASE_TAG}" --json assets'
  assert_contains 'bundle_name="${asset_name}.sigstore.json"'
  assert_contains '[ "${asset_count}" -ne 1 ] || [ "${bundle_count}" -ne 1 ] || [ "${asset_size}" -le 0 ] || [ "${bundle_size}" -le 0 ]'
  assert_contains "      - name: Verify native CLI asset attestations"
  assert_before "      - name: Verify release asset manifest" "      - name: Attest native CLI release assets"
  assert_before "      - name: Attest native CLI release assets" "      - name: Verify native CLI asset attestations"
  assert_before "      - name: Verify native CLI asset attestations" "      - name: Attach attestation bundles to release assets"
  assert_before "      - name: Attach attestation bundles to release assets" "      - name: Upload native CLI assets"
  assert_before "      - name: Upload native CLI assets" "      - name: Verify remote release assets"
  assert_before "      - name: Verify remote release assets" "      - name: Publish draft release"
}

test_remote_attestation_digests_match_the_release_tag_before_publishing() {
  # Verify the remote assets and their attached bundles are fail-closed checked
  # against the release tag before the draft release becomes public.
  assert_contains "      - name: Verify remote attestation digest matches release tag"
  assert_contains "        if: env.SHOULD_PUBLISH == 'true' && env.DRY_RUN != 'true'"
  assert_contains 'gh release download "${RELEASE_TAG}" --pattern "${asset_name}" --pattern "${bundle_name}" --dir "${asset_directory}"'
  assert_contains 'gh attestation verify "${downloaded_asset_path}" --repo "${GITHUB_REPOSITORY}" --bundle "${downloaded_bundle_path}" --signer-workflow "${SIGNER_WORKFLOW}" --format json'
  assert_contains '.verificationResult.signature.certificate.sourceRepositoryDigest'
  assert_contains 'gh api "repos/${GITHUB_REPOSITORY}/commits/${RELEASE_TAG}" --jq '\''.sha'\'''
  assert_contains 'if [ "${tag_sha}" != "${RELEASE_SHA}" ]; then'
  assert_contains 'if [ "${digest}" != "${tag_sha}" ]; then'
  assert_contains 'if [ "${verification_count}" -eq 0 ] || [ "${verification_count}" -ne "${digest_count}" ]; then'
  assert_before "      - name: Verify remote release assets" "      - name: Verify remote attestation digest matches release tag"
  assert_before "      - name: Verify remote attestation digest matches release tag" "      - name: Publish draft release"
  verification_section=$(remote_attestation_verification_section)
  for required_environment in \
    'GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}' \
    'GITHUB_REPOSITORY: ${{ github.repository }}' \
    'SIGNER_WORKFLOW: ${{ github.repository }}/.github/workflows/native-cli-publish.yml'; do
    if ! printf '%s\n' "${verification_section}" | grep -F -- "${required_environment}" >/dev/null 2>&1; then
      echo "Remote attestation verification must define ${required_environment}." >&2
      exit 1
    fi
  done
  tag_mismatch_guard='if [ "${tag_sha}" != "${RELEASE_SHA}" ]; then
            echo "Release tag ${RELEASE_TAG} does not match approved release commit ${RELEASE_SHA}." >&2
            exit 1
          fi'
  digest_mismatch_guard='if [ "${digest}" != "${tag_sha}" ]; then
                echo "Attestation digest for ${asset_name} does not match release tag ${RELEASE_TAG}." >&2
                exit 1
              fi'
  case "${verification_section}" in
    *"${tag_mismatch_guard}"*"${digest_mismatch_guard}"*) ;;
    *)
      echo "Remote attestation verification must fail for tag or digest mismatches." >&2
      exit 1
      ;;
  esac
}

test_publish_rejects_manifest_and_existing_tag_mismatches() {
  assert_contains "Unexpected release files are not listed in the manifest."
  assert_contains "Release manifest is missing files or has duplicate entries."
  assert_contains 'gh api "repos/${GITHUB_REPOSITORY}/commits/${RELEASE_TAG}" --jq '\''.sha'\'''
  assert_contains 'Release tag ${RELEASE_TAG} does not match approved release commit ${RELEASE_SHA}.'
  assert_not_contains "release-input/dist/release"
}

test_release_tag_is_created_before_the_release() {
  assert_contains 'gh api "repos/${GITHUB_REPOSITORY}/git/refs" \'
  assert_contains '  -f ref="refs/tags/${RELEASE_TAG}" \'
  assert_contains '  -f sha="${RELEASE_SHA}" > "${tag_create_error_path}" 2>&1'
  assert_before 'gh api "repos/${GITHUB_REPOSITORY}/git/refs"' 'gh release create "${RELEASE_TAG}"'
}

test_release_creation_403_points_to_roll_forward_recovery() {
  # Verify authorization failures direct operators to the supported recovery procedure.
  assert_contains 'if grep -qE '\''HTTP 403|Resource not accessible by integration'\'' "${error_path}"; then'
  assert_contains 'report_release_creation_failure "${tag_create_error_path}"'
  assert_contains 'report_release_creation_failure "${release_create_error_path}"'
  assert_contains 'See docs/release-recovery-runbook.md for the roll-forward recovery procedure.'
  assert_not_contains 'Create the tag and draft release as the repository owner, then rerun this workflow.'
}

test_draft_creation_accepts_only_the_known_missing_tag_responses() {
  assert_contains '*"HTTP 404"*) tag_sha="" ;;'
  assert_contains '*"HTTP 422"*"No commit found for SHA"*|*"No commit found for SHA"*"HTTP 422"*) tag_sha="" ;;'
  assert_count 2 'tag_sha="" ;;'
  assert_contains '*) printf '\''%s\n'\'' "${tag_error}" >&2; exit "${tag_status}" ;;'
}

test_publish_rechecks_the_tag_and_uses_least_privilege_post_publish_permissions() {
  assert_contains "      - name: Publish draft release"
  if ! publish_draft_section | grep -F 'tag_sha=$(gh api "repos/${GITHUB_REPOSITORY}/commits/${RELEASE_TAG}" --jq '\''.sha'\'')' >/dev/null 2>&1; then
    echo "Publish draft release must recheck the release tag SHA." >&2
    exit 1
  fi
  assert_not_contains "      issues: write"
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
test_publish_has_no_recovery_target_mode
test_publish_validates_metadata_without_checking_out_source
test_checkout_free_publish_has_explicit_repository_context
test_assets_are_attested_after_the_manifest_is_verified
test_remote_attestation_digests_match_the_release_tag_before_publishing
test_publish_rejects_manifest_and_existing_tag_mismatches
test_release_tag_is_created_before_the_release
test_release_creation_403_points_to_roll_forward_recovery
test_draft_creation_accepts_only_the_known_missing_tag_responses
test_publish_rechecks_the_tag_and_uses_least_privilege_post_publish_permissions
test_build_verifies_assets_before_writing_the_publish_input
test_post_publish_automation_remains_outside_the_privileged_job
