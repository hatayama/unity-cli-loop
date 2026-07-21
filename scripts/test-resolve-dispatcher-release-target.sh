#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
SCRIPT="$ROOT_DIR/scripts/resolve-dispatcher-release-target.sh"
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

if [ "$1" = "rev-parse" ]; then
  printf '%s\n' "${BUILD_SHA_VALUE:-target-sha}"
  exit 0
fi

if [ "$1" = "rev-list" ] && [ "$2" = "-n" ] && [ "$3" = "1" ]; then
  if [ -n "${DISPATCHER_TAG_SHA_VALUE:-}" ]; then
    printf '%s\n' "$DISPATCHER_TAG_SHA_VALUE"
    exit 0
  fi
  exit 1
fi

if [ "$1" = "show" ]; then
  if [ "${DISPATCHER_BUILD_COMMIT_STAMPS:-false}" != "true" ]; then
    exit 0
  fi
  case "${DISPATCHER_STAMP_KIND:-contract}" in
    contract)
      printf '%s\n' 'diff --git a/cli/dispatcher/dispatchercontract/dispatcher-contract.json b/cli/dispatcher/dispatchercontract/dispatcher-contract.json'
      printf '+  "dispatcherVersion": "%s"\n' "$CURRENT_VERSION"
      ;;
    changelog)
      printf '%s\n' 'diff --git a/cli/dispatcher/CHANGELOG.md b/cli/dispatcher/CHANGELOG.md'
      printf '+## [%s]\n' "$CURRENT_VERSION"
      ;;
  esac
  exit 0
fi

echo "unexpected git command: $*" >&2
exit 1
MOCK_GIT

  cat > "$mock_bin/gh" <<'MOCK_GH'
#!/bin/sh
set -eu

asset_json() {
  has_assets=$1
  if [ "$has_assets" = "true" ]; then
    printf '[{"name":"install.sh","size":1},{"name":"install.sh.sha256","size":1},{"name":"install.ps1","size":1},{"name":"install.ps1.sha256","size":1},{"name":"uloop-dispatcher-darwin-amd64.tar.gz","size":1},{"name":"uloop-dispatcher-darwin-amd64.tar.gz.sha256","size":1},{"name":"uloop-dispatcher-darwin-arm64.tar.gz","size":1},{"name":"uloop-dispatcher-darwin-arm64.tar.gz.sha256","size":1},{"name":"uloop-dispatcher-windows-amd64.zip","size":1},{"name":"uloop-dispatcher-windows-amd64.zip.sha256","size":1}]'
    return
  fi
  printf '[]'
}

if [ "$1" = "release" ] && [ "$2" = "view" ]; then
  if [ "$DISPATCHER_RELEASE_STATE" = "missing" ]; then
    echo "release not found" >&2
    exit 1
  fi
  is_draft=false
  if [ "$DISPATCHER_RELEASE_STATE" = "draft" ]; then
    is_draft=true
  fi
  printf '{"isDraft":%s,"assets":' "$is_draft"
  asset_json "$DISPATCHER_RELEASE_HAS_ASSETS"
  printf '}\n'
  exit 0
fi

echo "unexpected gh command: $*" >&2
exit 1
MOCK_GH

  chmod +x "$mock_bin/git" "$mock_bin/gh"
}

write_contract() {
  version=$1
  mkdir -p cli/dispatcher/dispatchercontract
  printf '%s\n' \
    '{' \
    '  "dispatcherVersion": "'"$version"'"' \
    '}' > cli/dispatcher/dispatchercontract/dispatcher-contract.json
}

# Runs the resolver with mocked git/gh and returns stdout; stderr is captured to
# "$TMP_DIR/$name/stderr.txt" for tests that need to assert skip messages.
run_case() {
  name=$1
  release_state=$2
  has_assets=$3
  tag_sha=${4:-}
  ref_name=${5:-v3-beta}
  event_name=${6:-push}
  build_commit_stamps=${7:-true}
  stamp_kind=${8:-contract}
  version=${9:-3.0.0}
  work_dir="$TMP_DIR/$name"
  mkdir -p "$work_dir"
  (
    cd "$work_dir"
    write_contract "$version"
    mock_bin="$work_dir/bin"
    write_mock_commands "$mock_bin"
    PATH="$mock_bin:$ORIGINAL_PATH" \
      EVENT_NAME="$event_name" \
      EVENT_REF_NAME="$ref_name" \
      DISPATCHER_RELEASE_STATE="$release_state" \
      DISPATCHER_RELEASE_HAS_ASSETS="$has_assets" \
      DISPATCHER_TAG_SHA_VALUE="$tag_sha" \
      DISPATCHER_BUILD_COMMIT_STAMPS="$build_commit_stamps" \
      DISPATCHER_STAMP_KIND="$stamp_kind" \
      CURRENT_VERSION="$version" \
      "$SCRIPT" > output.txt 2> stderr.txt
  )
  cat "$work_dir/output.txt"
}

assert_contains() {
  text=$1
  expected=$2
  if ! printf '%s\n' "$text" | grep -F -- "$expected" >/dev/null; then
    echo "Expected output to contain: $expected" >&2
    echo "$text" >&2
    exit 1
  fi
}

assert_file_contains() {
  file=$1
  expected=$2
  if ! grep -F -- "$expected" "$file" >/dev/null; then
    echo "Expected $file to contain: $expected" >&2
    echo "Actual content:" >&2
    cat "$file" >&2
    exit 1
  fi
}

# Verifies a push with a stamping commit publishes and releases when no dispatcher release exists yet.
test_missing_release_publishes() {
  output=$(run_case missing-release missing false)
  assert_contains "$output" "publish=true"
  assert_contains "$output" "release=true"
  assert_contains "$output" "tag=dispatcher-v3.0.0"
  assert_contains "$output" "prerelease=true"
}

# Verifies a push with a stamping commit skips when the dispatcher release is already fully published.
test_complete_release_skips() {
  output=$(run_case complete-release published true)
  assert_contains "$output" "publish=false"
  assert_contains "$output" "release=false"
}

# Verifies a push with a stamping commit uploads remaining assets onto a published release missing assets.
test_published_release_with_missing_assets_uploads_assets() {
  output=$(run_case missing-assets published false)
  assert_contains "$output" "publish=true"
  assert_contains "$output" "release=false"
}

# Verifies the existing release tag SHA is reported when following up on a published release missing assets.
test_published_release_with_missing_assets_uses_existing_tag_sha() {
  output=$(run_case missing-assets-followup published false release-sha)
  assert_contains "$output" "publish=true"
  assert_contains "$output" "release=false"
  assert_contains "$output" "sha=release-sha"
}

# Verifies a push with a stamping commit on main is not treated as a prerelease.
test_main_branch_release_is_not_prerelease() {
  output=$(run_case main-missing-release missing false "" main)
  assert_contains "$output" "publish=true"
  assert_contains "$output" "release=true"
  assert_contains "$output" "prerelease=false"
}

# Verifies a push without a stamping commit skips publish/release and points to workflow_dispatch for retries.
test_push_unstamped_skips_and_reports_workflow_dispatch() {
  output=$(run_case push-unstamped missing false "" v3-beta push false)
  assert_contains "$output" "publish=false"
  assert_contains "$output" "release=false"
  assert_file_contains "$TMP_DIR/push-unstamped/stderr.txt" "does not stamp dispatcher version"
  assert_file_contains "$TMP_DIR/push-unstamped/stderr.txt" "workflow_dispatch"
}

# Verifies a changelog-only stamp (version-series-realignment commits) still publishes on push.
test_push_changelog_only_stamp_publishes() {
  output=$(run_case push-changelog-only missing false "" v3-beta push true changelog)
  assert_contains "$output" "publish=true"
}

# Verifies workflow_dispatch bypasses the push gate and publishes via state-based evaluation when no release exists.
test_dispatch_unstamped_missing_release_publishes() {
  output=$(run_case dispatch-missing missing false "" v3-beta workflow_dispatch false)
  assert_contains "$output" "publish=true"
  assert_contains "$output" "release=true"
}

test_missing_release_publishes
test_complete_release_skips
test_published_release_with_missing_assets_uploads_assets
test_published_release_with_missing_assets_uses_existing_tag_sha
test_main_branch_release_is_not_prerelease
test_push_unstamped_skips_and_reports_workflow_dispatch
test_push_changelog_only_stamp_publishes
test_dispatch_unstamped_missing_release_publishes
