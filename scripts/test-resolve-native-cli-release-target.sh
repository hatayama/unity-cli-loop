#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
SCRIPT="$ROOT_DIR/scripts/resolve-native-cli-release-target.sh"
TMP_DIR=$(mktemp -d)
ORIGINAL_PATH=$PATH

cleanup() {
  rm -rf "$TMP_DIR"
}

trap cleanup EXIT INT HUP TERM

write_mock_commands() {
  mock_bin=$1
  mkdir -p "$mock_bin"

  cat > "$mock_bin/git" <<'MOCK_GIT'
#!/bin/sh
set -eu

emit_cli_release_diff() {
  version=$1
  printf '%s\n' 'diff --git a/.release-please-manifest.json b/.release-please-manifest.json'
  printf '+  "cli/project-runner": "%s"\n' "$version"
  printf '%s\n' 'diff --git a/cli/project-runner/CHANGELOG.md b/cli/project-runner/CHANGELOG.md'
  printf '+## [%s]\n' "$version"
}

case "$1" in
  diff)
    if [ "$CLI_SOURCE_CHANGED" = "true" ] || [ "$CONTRACT_CHANGED" = "true" ] || [ "$CLI_REQUIREMENT_CHANGED" = "true" ]; then
      exit 1
    fi
    exit 0
    ;;
  rev-parse)
    printf '%s\n' "${BUILD_SHA_VALUE:-target-sha}"
    ;;
  show)
    commit_sha=$3
    if [ "$commit_sha" = "${BUILD_SHA_VALUE:-target-sha}" ] && [ "${BUILD_COMMIT_UPDATES_CLI:-false}" = "true" ]; then
      emit_cli_release_diff "$CURRENT_VERSION"
      exit 0
    fi
    exit 0
    ;;
  *)
    echo "unexpected git command: $*" >&2
    exit 1
    ;;
esac
MOCK_GIT

  cat > "$mock_bin/gh" <<'MOCK_GH'
#!/bin/sh
set -eu

asset_json() {
  has_assets=$1
  if [ "$has_assets" = "true" ]; then
    printf '[{"name":"uloop-project-runner-darwin-amd64.tar.gz","size":1},{"name":"uloop-project-runner-darwin-amd64.tar.gz.sha256","size":1},{"name":"uloop-project-runner-darwin-arm64.tar.gz","size":1},{"name":"uloop-project-runner-darwin-arm64.tar.gz.sha256","size":1},{"name":"uloop-project-runner-windows-amd64.zip","size":1},{"name":"uloop-project-runner-windows-amd64.zip.sha256","size":1}]'
    return
  fi
  printf '[]'
}

release_json() {
  state=$1
  has_assets=$2
  if [ "$state" = "missing" ]; then
    echo "release not found" >&2
    exit 1
  fi

  if [ "$state" = "error" ]; then
    echo "gh auth failed" >&2
    exit 1
  fi

  is_draft=false
  if [ "$state" = "draft" ]; then
    is_draft=true
  fi

  printf '{"isDraft":%s,"assets":' "$is_draft"
  asset_json "$has_assets"
  printf '}\n'
}

if [ "$1" = "release" ] && [ "$2" = "view" ]; then
  tag=$3
	  if [ "$tag" = "uloop-project-runner-v$CURRENT_VERSION" ]; then
	    release_json "$CURRENT_RELEASE_STATE" "$CURRENT_RELEASE_HAS_ASSETS"
	    exit 0
	  fi
	
	  if [ -n "$PREVIOUS_RELEASE_TAG" ] && [ "$tag" = "$PREVIOUS_RELEASE_TAG" ]; then
	    release_json published "$PREVIOUS_RELEASE_HAS_ASSETS"
	    exit 0
  fi

  exit 1
fi

if [ "$1" = "release" ] && [ "$2" = "list" ]; then
  if [ -n "$PREVIOUS_RELEASE_TAG" ]; then
    printf '[{"tagName":"%s","isDraft":false}]\n' "$PREVIOUS_RELEASE_TAG"
    exit 0
  fi

  printf '[]\n'
  exit 0
fi

echo "unexpected gh command: $*" >&2
exit 1
MOCK_GH

  chmod +x "$mock_bin/git" "$mock_bin/gh"
}

write_manifest() {
  version=$1
  cat > .release-please-manifest.json <<EOF_MANIFEST
{
  "cli/project-runner": "$version"
}
EOF_MANIFEST
}

assert_contains() {
  file=$1
  expected=$2

  if ! grep -F "$expected" "$file" >/dev/null; then
    echo "Expected $file to contain: $expected" >&2
    echo "Actual content:" >&2
    cat "$file" >&2
    exit 1
  fi
}

assert_line_equals() {
  file=$1
  expected=$2

  if ! grep -Fx "$expected" "$file" >/dev/null; then
    echo "Expected $file to contain line: $expected" >&2
    echo "Actual content:" >&2
    cat "$file" >&2
    exit 1
  fi
}

assert_script_contains() {
  expected=$1

  if ! grep -F "$expected" "$SCRIPT" >/dev/null; then
    echo "Expected $SCRIPT to contain: $expected" >&2
    exit 1
  fi
}

run_success_case() {
  name=$1
  current_version=$2
  event_name=$3
  branch_name=$4
  current_release_state=$5
  current_release_has_assets=$6
  previous_release_tag=$7
  previous_release_has_assets=$8
  cli_source_changed=$9
  contract_changed=${10}
  cli_requirement_changed=${11}
  expected_publish=${12}
  expected_release=${13}
  expected_sha=${14:-target-sha}
  build_sha_value=${15:-target-sha}
  build_commit_updates_cli=${16:-false}

  work_dir="$TMP_DIR/$name"
  mock_bin="$work_dir/bin"
  mkdir -p "$work_dir"
  write_mock_commands "$mock_bin"

  (
    cd "$work_dir"
    write_manifest "$current_version"
    PATH="$mock_bin:$ORIGINAL_PATH" \
      CURRENT_VERSION="$current_version" \
      CURRENT_RELEASE_STATE="$current_release_state" \
      CURRENT_RELEASE_HAS_ASSETS="$current_release_has_assets" \
      BUILD_SHA_VALUE="$build_sha_value" \
      BUILD_COMMIT_UPDATES_CLI="$build_commit_updates_cli" \
	      PREVIOUS_RELEASE_TAG="$previous_release_tag" \
	      PREVIOUS_RELEASE_HAS_ASSETS="$previous_release_has_assets" \
      CLI_SOURCE_CHANGED="$cli_source_changed" \
      CONTRACT_CHANGED="$contract_changed" \
      CLI_REQUIREMENT_CHANGED="$cli_requirement_changed" \
      EVENT_NAME="$event_name" \
      EVENT_REF_NAME="$branch_name" \
      BEFORE_SHA=before \
      INPUT_RELEASE_TAG= \
      INPUT_DRY_RUN=false \
      "$SCRIPT" > output.txt 2> stderr.txt

    assert_line_equals output.txt "publish=$expected_publish"
    assert_line_equals output.txt "release=$expected_release"
    assert_line_equals output.txt "tag=uloop-project-runner-v$current_version"
    assert_line_equals output.txt "version=$current_version"
    assert_line_equals output.txt "sha=$expected_sha"
    assert_line_equals output.txt "build_sha=$build_sha_value"
    assert_line_equals output.txt "dry_run=false"
  )
}

run_failure_case() {
  name=$1
  current_version=$2
  event_name=$3
  branch_name=$4
  expected_error=$5
  current_release_state=${6:-missing}
  input_release_tag=${7:-}

  work_dir="$TMP_DIR/$name"
  mock_bin="$work_dir/bin"
  mkdir -p "$work_dir"
  write_mock_commands "$mock_bin"

  (
    cd "$work_dir"
    write_manifest "$current_version"
    set +e
    PATH="$mock_bin:$ORIGINAL_PATH" \
      CURRENT_VERSION="$current_version" \
      CURRENT_RELEASE_STATE="$current_release_state" \
      CURRENT_RELEASE_HAS_ASSETS=false \
      BUILD_SHA_VALUE=target-sha \
	      PREVIOUS_RELEASE_TAG=uloop-project-runner-v3.0.0-beta.1 \
      PREVIOUS_RELEASE_HAS_ASSETS=true \
      CLI_SOURCE_CHANGED=false \
      CONTRACT_CHANGED=false \
      CLI_REQUIREMENT_CHANGED=false \
      EVENT_NAME="$event_name" \
      EVENT_REF_NAME="$branch_name" \
      BEFORE_SHA=before \
      INPUT_RELEASE_TAG="$input_release_tag" \
      INPUT_DRY_RUN=false \
      "$SCRIPT" > output.txt 2> stderr.txt
    status=$?
    set -e

    if [ "$status" -eq 0 ]; then
      echo "Expected $name to fail." >&2
      exit 1
    fi

    assert_contains stderr.txt "$expected_error"
  )
}

# Verifies already published complete Project runner assets are not rebuilt.
test_complete_current_release_skips() {
  run_success_case current-complete 3.0.0-beta.2 push v3-beta published true uloop-project-runner-v3.0.0-beta.1 true true false false false false
}

# Verifies package-only version changes do not publish Project runner assets.
test_package_version_change_without_cli_change_skips() {
  run_success_case package-only 3.0.0-beta.3 push v3-beta missing false uloop-project-runner-v3.0.0-beta.1 true false false false false true
}

# Verifies CLI source changes publish assets on the current release tag.
test_cli_change_publishes() {
  run_success_case cli-change 3.0.0-beta.3 push v3-beta missing false uloop-project-runner-v3.0.0-beta.1 true true false false true true
}

# Verifies missing Project runner assets can be uploaded to an already published package release.
test_published_current_release_can_receive_cli_assets() {
  run_success_case published-missing-assets 3.0.0-beta.3 push v3-beta published false uloop-project-runner-v3.0.0-beta.1 true true false false true false
}

# Verifies non-version CLI contract changes publish assets.
test_cli_contract_change_publishes() {
  run_success_case contract-change 3.0.0-beta.3 push v3-beta missing false uloop-project-runner-v3.0.0-beta.1 true false true false true true
}

# Verifies CLI requirement version bumps publish assets for that required tag.
test_cli_requirement_change_publishes() {
  run_success_case cli-requirement-change 3.0.0-beta.3 push v3-beta missing false uloop-project-runner-v3.0.0-beta.1 true false false true true true
}

# Verifies Project runner release metadata-only release commits still publish native Project runner assets.
test_cli_release_metadata_change_publishes() {
  run_success_case cli-release-metadata-change 3.0.0-beta.3 push v3-beta missing false uloop-project-runner-v3.0.0-beta.1 true false false false true true target-sha target-sha true
}

# Verifies publishing always targets the approved event-head commit.
test_release_target_is_event_head() {
  run_success_case event-head-target 3.0.0-beta.2 push v3-beta missing false uloop-project-runner-v3.0.0-beta.1 true true false false true true build-sha build-sha
}

# Verifies the first Project runner asset release is published when no previous asset tag exists.
test_missing_previous_cli_release_publishes() {
  run_success_case bootstrap 3.0.0-beta.0 push v3-beta missing false "" false false false false true true
}

# Verifies main refuses prerelease versions.
test_main_prerelease_fails() {
  run_failure_case main-prerelease 3.0.0-beta.2 push main "Refusing to publish prerelease version 3.0.0-beta.2 from main."
}

# Verifies v3-beta refuses stable versions.
test_v3_beta_stable_fails() {
  run_failure_case v3-beta-stable 3.0.0 push v3-beta "Refusing to publish stable version 3.0.0 from v3-beta."
}

# Verifies release lookup failures other than not-found are not treated as missing releases.
test_release_lookup_error_fails() {
  run_failure_case release-lookup-error 3.0.0-beta.3 push v3-beta "gh auth failed" error
}

# Verifies explicit project runner tags must use a full SemVer suffix.
test_invalid_release_tag_version_fails() {
  run_failure_case invalid-release-tag-version 3.0.0-beta.3 push v3-beta "Invalid release tag: uloop-project-runner-v3-beta" missing uloop-project-runner-v3-beta
}

# Verifies numeric prerelease identifiers cannot have leading zeroes.
test_invalid_release_tag_numeric_prerelease_fails() {
  run_failure_case invalid-release-tag-numeric-prerelease 3.0.0-beta.3 push v3-beta "Invalid release tag: uloop-project-runner-v3.0.0-01" missing uloop-project-runner-v3.0.0-01
}

# Verifies prerelease identifiers cannot be empty.
test_invalid_release_tag_empty_prerelease_identifier_fails() {
  run_failure_case invalid-release-tag-empty-prerelease 3.0.0-beta.3 push v3-beta "Invalid release tag: uloop-project-runner-v3.0.0-alpha..1" missing uloop-project-runner-v3.0.0-alpha..1
}

assert_script_contains "cli/project-runner/go.mod"
test_complete_current_release_skips
test_package_version_change_without_cli_change_skips
test_cli_change_publishes
test_published_current_release_can_receive_cli_assets
test_cli_contract_change_publishes
test_cli_requirement_change_publishes
test_cli_release_metadata_change_publishes
test_release_target_is_event_head
test_missing_previous_cli_release_publishes
test_main_prerelease_fails
test_v3_beta_stable_fails
test_release_lookup_error_fails
test_invalid_release_tag_version_fails
test_invalid_release_tag_numeric_prerelease_fails
test_invalid_release_tag_empty_prerelease_identifier_fails
