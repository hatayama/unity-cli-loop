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

emit_full_stamp() {
  version=$1
  printf '%s\n' 'diff --git a/.release-please-manifest.json b/.release-please-manifest.json'
  printf '+  "cli/project-runner": "%s"\n' "$version"
  printf '%s\n' 'diff --git a/cli/project-runner/CHANGELOG.md b/cli/project-runner/CHANGELOG.md'
  printf '+## [%s]\n' "$version"
}

emit_changelog_only_stamp() {
  version=$1
  printf '%s\n' 'diff --git a/cli/project-runner/CHANGELOG.md b/cli/project-runner/CHANGELOG.md'
  printf '+## [%s]\n' "$version"
}

case "$1" in
  rev-parse)
    printf '%s\n' "${BUILD_SHA_VALUE:-target-sha}"
    ;;
  show)
    case "${STAMP_KIND:-none}" in
      manifest)
        emit_full_stamp "$CURRENT_VERSION"
        ;;
      changelog)
        emit_changelog_only_stamp "$CURRENT_VERSION"
        ;;
      none)
        :
        ;;
    esac
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

if [ "$1" = "release" ] && [ "$2" = "view" ]; then
  tag=$3
  if [ "$tag" != "uloop-project-runner-v$CURRENT_VERSION" ]; then
    echo "release not found" >&2
    exit 1
  fi

  if [ "$CURRENT_RELEASE_STATE" = "missing" ]; then
    echo "release not found" >&2
    exit 1
  fi

  if [ "$CURRENT_RELEASE_STATE" = "error" ]; then
    echo "gh auth failed" >&2
    exit 1
  fi

  is_draft=false
  if [ "$CURRENT_RELEASE_STATE" = "draft" ]; then
    is_draft=true
  fi

  printf '{"isDraft":%s,"assets":' "$is_draft"
  asset_json "$CURRENT_RELEASE_HAS_ASSETS"
  printf '}\n'
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

run_success_case() {
  name=$1
  current_version=$2
  event_name=$3
  branch_name=$4
  current_release_state=$5
  current_release_has_assets=$6
  stamp_kind=$7
  expected_publish=$8
  expected_release=$9
  build_sha_value=${10:-target-sha}

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
      STAMP_KIND="$stamp_kind" \
      BUILD_SHA_VALUE="$build_sha_value" \
      EVENT_NAME="$event_name" \
      EVENT_REF_NAME="$branch_name" \
      INPUT_RELEASE_TAG= \
      INPUT_DRY_RUN=false \
      "$SCRIPT" > output.txt 2> stderr.txt

    assert_line_equals output.txt "publish=$expected_publish"
    assert_line_equals output.txt "release=$expected_release"
    assert_line_equals output.txt "tag=uloop-project-runner-v$current_version"
    assert_line_equals output.txt "version=$current_version"
    assert_line_equals output.txt "sha=$build_sha_value"
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
  stamp_kind=${8:-manifest}

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
      STAMP_KIND="$stamp_kind" \
      BUILD_SHA_VALUE=target-sha \
      EVENT_NAME="$event_name" \
      EVENT_REF_NAME="$branch_name" \
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

# Verifies push with a stamping commit publishes and releases when no release exists yet.
test_push_stamped_missing_release_publishes() {
  run_success_case push-stamped-missing 3.0.0-beta.3 push v3-beta missing false manifest true true
}

# Verifies push with a stamping commit publishes remaining assets onto a published release missing assets.
test_push_stamped_published_missing_assets_publishes() {
  run_success_case push-stamped-published-missing-assets 3.0.0-beta.3 push v3-beta published false manifest true false
}

# Verifies push with a stamping commit skips when the release is already fully published with all assets.
test_push_stamped_fully_published_skips() {
  run_success_case push-stamped-fully-published 3.0.0-beta.3 push v3-beta published true manifest false false
}

# Verifies push without a stamping commit skips publish/release and points to workflow_dispatch for retries.
test_push_unstamped_skips_and_reports_workflow_dispatch() {
  work_dir="$TMP_DIR/push-unstamped"
  mock_bin="$work_dir/bin"
  mkdir -p "$work_dir"
  write_mock_commands "$mock_bin"

  (
    cd "$work_dir"
    write_manifest 3.0.0-beta.3
    PATH="$mock_bin:$ORIGINAL_PATH" \
      CURRENT_VERSION=3.0.0-beta.3 \
      CURRENT_RELEASE_STATE=missing \
      CURRENT_RELEASE_HAS_ASSETS=false \
      STAMP_KIND=none \
      BUILD_SHA_VALUE=target-sha \
      EVENT_NAME=push \
      EVENT_REF_NAME=v3-beta \
      INPUT_RELEASE_TAG= \
      INPUT_DRY_RUN=false \
      "$SCRIPT" > output.txt 2> stderr.txt

    assert_line_equals output.txt "publish=false"
    assert_line_equals output.txt "release=false"
    assert_contains stderr.txt "does not stamp project runner version"
    assert_contains stderr.txt "workflow_dispatch"
  )
}

# Verifies a changelog-only stamp (version-series-realignment commits) still publishes on push.
test_push_changelog_only_stamp_publishes() {
  run_success_case push-changelog-only 3.0.0-beta.3 push v3-beta missing false changelog true true
}

# Verifies workflow_dispatch bypasses the push gate and publishes/releases via state-based evaluation when no release exists.
test_dispatch_unstamped_missing_release_publishes() {
  run_success_case dispatch-missing 3.0.0-beta.3 workflow_dispatch v3-beta missing false none true true
}

# Verifies workflow_dispatch state-based evaluation still uploads missing assets onto a published release.
test_dispatch_unstamped_published_missing_assets_publishes() {
  run_success_case dispatch-missing-assets 3.0.0-beta.3 workflow_dispatch v3-beta published false none true false
}

# Verifies workflow_dispatch state-based evaluation skips a fully published release.
test_dispatch_unstamped_fully_published_skips() {
  run_success_case dispatch-fully-published 3.0.0-beta.3 workflow_dispatch v3-beta published true none false false
}

# Verifies publishing always targets the approved event-head commit.
test_release_target_is_event_head() {
  run_success_case event-head-target 3.0.0-beta.2 push v3-beta missing false manifest true true build-sha
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
  run_failure_case release-lookup-error 3.0.0-beta.3 push v3-beta "gh auth failed" error "" manifest
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
  run_failure_case invalid-release-tag-empty-prerelease-identifier 3.0.0-beta.3 push v3-beta "Invalid release tag: uloop-project-runner-v3.0.0-alpha..1" missing uloop-project-runner-v3.0.0-alpha..1
}

test_push_stamped_missing_release_publishes
test_push_stamped_published_missing_assets_publishes
test_push_stamped_fully_published_skips
test_push_unstamped_skips_and_reports_workflow_dispatch
test_push_changelog_only_stamp_publishes
test_dispatch_unstamped_missing_release_publishes
test_dispatch_unstamped_published_missing_assets_publishes
test_dispatch_unstamped_fully_published_skips
test_release_target_is_event_head
test_main_prerelease_fails
test_v3_beta_stable_fails
test_release_lookup_error_fails
test_invalid_release_tag_version_fails
test_invalid_release_tag_numeric_prerelease_fails
test_invalid_release_tag_empty_prerelease_identifier_fails
