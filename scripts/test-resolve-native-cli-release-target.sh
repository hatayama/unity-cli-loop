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

case "$1" in
  diff)
    if [ "$DISPATCHER_CHANGED" = "true" ]; then
      exit 1
    fi
    exit 0
    ;;
  rev-parse)
    printf '%s\n' target-sha
    ;;
  show)
    case "$2" in
      *:Packages/src/Cli~/Dispatcher~/contract.json)
        case "$2" in
          target-sha:*)
            if [ "$CONTRACT_CHANGED" = "true" ]; then
              printf '{"schemaVersion":1,"dispatcherVersion":"%s","dispatcherVersionEnv":"ULOOP_DISPATCHER_VERSION_V2"}\n' "$CURRENT_VERSION"
            else
              printf '{"schemaVersion":1,"dispatcherVersion":"%s","dispatcherVersionEnv":"ULOOP_DISPATCHER_VERSION"}\n' "$CURRENT_VERSION"
            fi
            ;;
          *)
            printf '{"schemaVersion":1,"dispatcherVersion":"0.0.0","dispatcherVersionEnv":"ULOOP_DISPATCHER_VERSION"}\n'
            ;;
        esac
        ;;
      *)
        echo "unexpected git show target: $2" >&2
        exit 1
        ;;
    esac
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
    printf '[{"name":"install.sh","size":1},{"name":"install.ps1","size":1},{"name":"uloop-darwin-amd64.tar.gz","size":1},{"name":"uloop-darwin-amd64.tar.gz.sha256","size":1},{"name":"uloop-darwin-arm64.tar.gz","size":1},{"name":"uloop-darwin-arm64.tar.gz.sha256","size":1},{"name":"uloop-windows-amd64.zip","size":1},{"name":"uloop-windows-amd64.zip.sha256","size":1}]'
    return
  fi
  printf '[]'
}

release_json() {
  state=$1
  has_assets=$2
  if [ "$state" = "missing" ]; then
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
  if [ "$tag" = "v$CURRENT_VERSION" ]; then
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
  "Packages/src": "$version"
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

run_success_case() {
  name=$1
  current_version=$2
  event_name=$3
  branch_name=$4
  current_release_state=$5
  current_release_has_assets=$6
  previous_release_tag=$7
  previous_release_has_assets=$8
  dispatcher_changed=$9
  contract_changed=${10}
  expected_publish=${11}
  expected_release=${12}

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
      PREVIOUS_RELEASE_TAG="$previous_release_tag" \
      PREVIOUS_RELEASE_HAS_ASSETS="$previous_release_has_assets" \
      DISPATCHER_CHANGED="$dispatcher_changed" \
      CONTRACT_CHANGED="$contract_changed" \
      EVENT_NAME="$event_name" \
      EVENT_REF_NAME="$branch_name" \
      BEFORE_SHA=before \
      INPUT_RELEASE_TAG= \
      INPUT_DRY_RUN=false \
      "$SCRIPT" > output.txt 2> stderr.txt

    assert_contains output.txt "publish=$expected_publish"
    assert_contains output.txt "release=$expected_release"
    assert_contains output.txt "tag=v$current_version"
    assert_contains output.txt "version=$current_version"
    assert_contains output.txt "sha=target-sha"
    assert_contains output.txt "dry_run=false"
  )
}

run_failure_case() {
  name=$1
  current_version=$2
  event_name=$3
  branch_name=$4
  expected_error=$5

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
      CURRENT_RELEASE_STATE=missing \
      CURRENT_RELEASE_HAS_ASSETS=false \
      PREVIOUS_RELEASE_TAG=v3.0.0-beta.1 \
      PREVIOUS_RELEASE_HAS_ASSETS=true \
      DISPATCHER_CHANGED=false \
      CONTRACT_CHANGED=false \
      EVENT_NAME="$event_name" \
      EVENT_REF_NAME="$branch_name" \
      BEFORE_SHA=before \
      INPUT_RELEASE_TAG= \
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

# Verifies already published complete Dispatcher assets are not rebuilt.
test_complete_current_release_skips() {
  run_success_case current-complete 3.0.0-beta.2 push v3-beta published true v3.0.0-beta.1 true true false false false
}

# Verifies package-only version changes do not publish Dispatcher assets.
test_package_version_change_without_dispatcher_change_skips() {
  run_success_case package-only 3.0.0-beta.3 push v3-beta missing false v3.0.0-beta.1 true false false false true
}

# Verifies Dispatcher source changes publish assets on the current release tag.
test_dispatcher_change_publishes() {
  run_success_case dispatcher-change 3.0.0-beta.3 push v3-beta missing false v3.0.0-beta.1 true true false true true
}

# Verifies missing Dispatcher assets can be uploaded to an already published package release.
test_published_current_release_can_receive_dispatcher_assets() {
  run_success_case published-missing-assets 3.0.0-beta.3 push v3-beta published false v3.0.0-beta.1 true true false true false
}

# Verifies non-version Dispatcher contract changes publish assets.
test_dispatcher_contract_change_publishes() {
  run_success_case contract-change 3.0.0-beta.3 push v3-beta missing false v3.0.0-beta.1 true false true true true
}

# Verifies the first Dispatcher asset release is published when no previous asset tag exists.
test_missing_previous_dispatcher_release_publishes() {
  run_success_case bootstrap 3.0.0-beta.0 push v3-beta missing false "" false false false true true
}

# Verifies main refuses prerelease versions.
test_main_prerelease_fails() {
  run_failure_case main-prerelease 3.0.0-beta.2 push main "Refusing to publish prerelease version 3.0.0-beta.2 from main."
}

# Verifies v3-beta refuses stable versions.
test_v3_beta_stable_fails() {
  run_failure_case v3-beta-stable 3.0.0 push v3-beta "Refusing to publish stable version 3.0.0 from v3-beta."
}

test_complete_current_release_skips
test_package_version_change_without_dispatcher_change_skips
test_dispatcher_change_publishes
test_published_current_release_can_receive_dispatcher_assets
test_dispatcher_contract_change_publishes
test_missing_previous_dispatcher_release_publishes
test_main_prerelease_fails
test_v3_beta_stable_fails
