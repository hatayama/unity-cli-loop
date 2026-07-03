#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
SCRIPT="$ROOT_DIR/scripts/sync-release-please-package-releases.sh"
TMP_DIR=$(mktemp -d)
ORIGINAL_PATH=$PATH

cleanup() {
  rm -rf "$TMP_DIR"
}

trap cleanup EXIT INT HUP TERM

write_mock_commands() {
  work_dir=$1
  mock_bin="$work_dir/bin"
  mkdir -p "$mock_bin"

  cat > "$mock_bin/gh" <<'MOCK_GH'
#!/bin/sh
set -eu

printf '%s\n' "$*" >> "$GH_LOG"

asset_json() {
  case "${CLI_RELEASE_ASSETS:-complete}" in
    complete)
      printf '[{"name":"uloop-project-runner-darwin-amd64.tar.gz","size":1},{"name":"uloop-project-runner-darwin-amd64.tar.gz.sha256","size":1},{"name":"uloop-project-runner-darwin-arm64.tar.gz","size":1},{"name":"uloop-project-runner-darwin-arm64.tar.gz.sha256","size":1},{"name":"uloop-project-runner-windows-amd64.zip","size":1},{"name":"uloop-project-runner-windows-amd64.zip.sha256","size":1}]'
      ;;
    missing)
      printf '[]'
      ;;
    empty)
      printf '[{"name":"uloop-project-runner-darwin-amd64.tar.gz","size":0},{"name":"uloop-project-runner-darwin-amd64.tar.gz.sha256","size":1},{"name":"uloop-project-runner-darwin-arm64.tar.gz","size":1},{"name":"uloop-project-runner-darwin-arm64.tar.gz.sha256","size":1},{"name":"uloop-project-runner-windows-amd64.zip","size":1},{"name":"uloop-project-runner-windows-amd64.zip.sha256","size":1}]'
      ;;
  esac
}

dispatcher_asset_json() {
  case "${DISPATCHER_RELEASE_ASSETS:-complete}" in
    complete)
      printf '[{"name":"install.sh","size":1},{"name":"install.ps1","size":1},{"name":"uloop-dispatcher-darwin-amd64.tar.gz","size":1},{"name":"uloop-dispatcher-darwin-amd64.tar.gz.sha256","size":1},{"name":"uloop-dispatcher-darwin-arm64.tar.gz","size":1},{"name":"uloop-dispatcher-darwin-arm64.tar.gz.sha256","size":1},{"name":"uloop-dispatcher-windows-amd64.zip","size":1},{"name":"uloop-dispatcher-windows-amd64.zip.sha256","size":1}]'
      ;;
    missing)
      printf '[]'
      ;;
  esac
}

if [ "$1" = "release" ] && [ "$2" = "view" ]; then
  tag=$3
  if [ "$tag" = "${CLI_RELEASE_TAG:-uloop-project-runner-v3.0.0-beta.6}" ]; then
    if [ -n "${CLI_RELEASE_READY_AFTER_ATTEMPTS:-}" ]; then
      attempt_file="$GH_LOG.cli-release-attempts"
      attempt=1
      if [ -f "$attempt_file" ]; then
        attempt=$(($(cat "$attempt_file") + 1))
      fi
      printf '%s\n' "$attempt" > "$attempt_file"
      if [ "$attempt" -lt "$CLI_RELEASE_READY_AFTER_ATTEMPTS" ]; then
        echo "release not found" >&2
        exit 1
      fi
    fi

    case "${CLI_RELEASE_STATE:-published}" in
      published)
        printf '{"isDraft":false,"targetCommitish":"%s","assets":' "${CLI_RELEASE_TARGET:-cli-release-sha}"
        asset_json
        printf '}\n'
        exit 0
        ;;
      draft)
        printf '{"isDraft":true,"targetCommitish":"%s","assets":' "${CLI_RELEASE_TARGET:-cli-release-sha}"
        asset_json
        printf '}\n'
        exit 0
        ;;
      missing)
        echo "release not found" >&2
        exit 1
        ;;
    esac
  fi

  if [ "$tag" = "${DISPATCHER_RELEASE_TAG:-dispatcher-v3.0.0}" ]; then
    case "${DISPATCHER_RELEASE_STATE:-published}" in
      published)
        printf '{"isDraft":false,"targetCommitish":"%s","assets":' "${DISPATCHER_RELEASE_TARGET:-dispatcher-release-sha}"
        dispatcher_asset_json
        printf '}\n'
        exit 0
        ;;
      draft)
        printf '{"isDraft":true,"targetCommitish":"%s","assets":' "${DISPATCHER_RELEASE_TARGET:-dispatcher-release-sha}"
        dispatcher_asset_json
        printf '}\n'
        exit 0
        ;;
      missing)
        echo "release not found" >&2
        exit 1
        ;;
    esac
  fi

  if [ -n "${EXISTING_RELEASE_TAG:-}" ] && [ "$tag" = "$EXISTING_RELEASE_TAG" ]; then
    printf '{"isDraft":%s,"targetCommitish":"%s","assets":[]}\n' "$EXISTING_RELEASE_DRAFT" "$EXISTING_RELEASE_TARGET"
    exit 0
  fi

  if [ -n "${CREATE_RELEASE_RACE_TAG:-}" ] && [ "$tag" = "$CREATE_RELEASE_RACE_TAG" ]; then
    race_file="$GH_LOG.release-created-by-other-workflow"
    if [ -f "$race_file" ]; then
      printf '{"isDraft":false,"targetCommitish":"%s","assets":[]}\n' "$CREATE_RELEASE_RACE_TARGET"
      exit 0
    fi
  fi

  echo "release not found" >&2
  exit 1
fi

if [ "$1" = "release" ] && [ "$2" = "create" ]; then
  tag=$3
  if [ -n "${CREATE_RELEASE_RACE_TAG:-}" ] && [ "$tag" = "$CREATE_RELEASE_RACE_TAG" ]; then
    touch "$GH_LOG.release-created-by-other-workflow"
    echo "release already exists" >&2
    exit 1
  fi

  exit 0
fi

if [ "$1" = "release" ] && [ "$2" = "edit" ]; then
  exit 0
fi

echo "unexpected gh command: $*" >&2
exit 1
MOCK_GH

  chmod +x "$mock_bin/gh"

  cat > "$mock_bin/go" <<'MOCK_GO'
#!/bin/sh
set -eu

printf '%s\n' "$*" >> "$GO_LOG"
exit "${PROTOCOL_CHECK_STATUS:-0}"
MOCK_GO

  chmod +x "$mock_bin/go"

  cat > "$mock_bin/sleep" <<'MOCK_SLEEP'
#!/bin/sh
set -eu

printf '%s\n' "$*" >> "$SLEEP_LOG"
MOCK_SLEEP

  chmod +x "$mock_bin/sleep"
}

write_release_files() {
  version=$1
  unity_package_key=${2:-Packages/src}
  unity_changelog_path=${3:-CHANGELOG.md}

  mkdir -p Packages/src dispatcher project-runner scripts tools/release-automation
  cat > release-please-config.json <<EOF_CONFIG
{
  "packages": {
    "$unity_package_key": {
      "component": "unity-package",
      "release-type": "go",
      "include-v-in-tag": true,
      "include-component-in-tag": false,
      "changelog-path": "$unity_changelog_path"
    },
    "dispatcher": {
      "component": "dispatcher",
      "release-type": "go",
      "include-v-in-tag": true,
      "include-component-in-tag": true,
      "changelog-path": "CHANGELOG.md"
    },
    "project-runner": {
      "component": "uloop-project-runner",
      "release-type": "go",
      "include-v-in-tag": true,
      "include-component-in-tag": true,
      "changelog-path": "CHANGELOG.md"
    }
  }
}
EOF_CONFIG

  cat > .release-please-manifest.json <<EOF_MANIFEST
{
  "$unity_package_key": "$version",
  "dispatcher": "$version",
  "project-runner": "$version"
}
EOF_MANIFEST

  cat > Packages/src/project-runner-pin.json <<EOF_PIN
{
  "schemaVersion": 1,
  "packageName": "test.package",
  "packageVersion": "$version",
  "projectRunnerVersion": "$version",
  "requiredProtocolVersion": 2,
  "minimumDispatcherVersion": "3.0.0"
}
EOF_PIN

  cat > Packages/src/CHANGELOG.md <<EOF_CHANGELOG
# Changelog

## [$version](https://example.test/compare/old...new)

### Bug Fixes

* keep the root package release baseline available
EOF_CHANGELOG

  cat > project-runner/CHANGELOG.md <<EOF_CLI_CHANGELOG
# Changelog

## [$version](https://example.test/compare/cli-old...cli-new)

### Bug Fixes

* keep the Project runner release baseline available
EOF_CLI_CHANGELOG

  cat > dispatcher/CHANGELOG.md <<EOF_DISPATCHER_CHANGELOG
# Changelog

## [$version](https://example.test/compare/dispatcher-old...dispatcher-new)

### Bug Fixes

* keep the dispatcher release baseline available
EOF_DISPATCHER_CHANGELOG

  cat > scripts/verify-native-cli-release-assets.sh <<'EOF_VERIFY'
#!/bin/sh
set -eu

if [ "${1:-}" = "--list" ]; then
  printf '%s\n' \
    uloop-project-runner-darwin-amd64.tar.gz \
    uloop-project-runner-darwin-amd64.tar.gz.sha256 \
    uloop-project-runner-darwin-arm64.tar.gz \
    uloop-project-runner-darwin-arm64.tar.gz.sha256 \
    uloop-project-runner-windows-amd64.zip \
    uloop-project-runner-windows-amd64.zip.sha256
  exit 0
fi

exit 1
EOF_VERIFY
  chmod +x scripts/verify-native-cli-release-assets.sh

  cat > scripts/verify-dispatcher-release-assets.sh <<'EOF_VERIFY_DISPATCHER'
#!/bin/sh
set -eu

if [ "${DISPATCHER_ASSET_LIST_FAIL:-false}" = "true" ]; then
  exit 1
fi

if [ "${1:-}" = "--list" ]; then
  printf '%s\n' \
    install.sh \
    install.ps1 \
    uloop-dispatcher-darwin-amd64.tar.gz \
    uloop-dispatcher-darwin-amd64.tar.gz.sha256 \
    uloop-dispatcher-darwin-arm64.tar.gz \
    uloop-dispatcher-darwin-arm64.tar.gz.sha256 \
    uloop-dispatcher-windows-amd64.zip \
    uloop-dispatcher-windows-amd64.zip.sha256
  exit 0
fi

exit 1
EOF_VERIFY_DISPATCHER
  chmod +x scripts/verify-dispatcher-release-assets.sh
}

create_release_repo() {
  name=$1
  work_dir="$TMP_DIR/$name"
  mkdir -p "$work_dir"

  (
    cd "$work_dir"
    git init -q
    git config user.email "test@example.com"
    git config user.name "Test User"

    write_release_files 3.0.0-beta.5
    git add .
    git commit -q -m "chore(v3-beta): release 3.0.0-beta.5"

    write_release_files 3.0.0-beta.6
    git add .
    git commit -q -m "chore: release v3-beta"
    git tag uloop-project-runner-v3.0.0-beta.6
    git rev-parse HEAD > "$work_dir/release-sha.txt"

    printf '%s\n' "follow-up" > follow-up.txt
    git add follow-up.txt
    git commit -q -m "fix: follow up after release"
  )

  printf '%s\n' "$work_dir"
}

create_key_rename_repo() {
  name=$1
  work_dir="$TMP_DIR/$name"
  mkdir -p "$work_dir"

  (
    cd "$work_dir"
    git init -q
    git config user.email "test@example.com"
    git config user.name "Test User"

    write_release_files 3.0.0-beta.6 "." "Packages/src/CHANGELOG.md"
    git add .
    git commit -q -m "chore: release v3-beta"
    git tag uloop-project-runner-v3.0.0-beta.6
    git rev-parse HEAD > "$work_dir/release-sha.txt"

    write_release_files 3.0.0-beta.6
    git add .
    git commit -q -m "chore: move unity-package release root"
  )

  printf '%s\n' "$work_dir"
}

create_changelog_move_repo() {
  name=$1
  work_dir="$TMP_DIR/$name"
  mkdir -p "$work_dir"

  (
    cd "$work_dir"
    git init -q
    git config user.email "test@example.com"
    git config user.name "Test User"
    # A pathspec-limited diff hides the rename source, so a moved changelog
    # always reappears as fully added lines. Rename detection is additionally
    # disabled to keep that behavior deterministic across git versions.
    git config diff.renames false

    write_release_files 3.0.0-beta.6 "." "CHANGELOG.md"
    mv Packages/src/CHANGELOG.md CHANGELOG.md
    git add .
    git commit -q -m "chore: release v3-beta"
    git tag uloop-project-runner-v3.0.0-beta.6
    git rev-parse HEAD > "$work_dir/release-sha.txt"

    write_release_files 3.0.0-beta.6
    git rm -q CHANGELOG.md
    git add .
    git commit -q -m "chore: move unity-package release root"
  )

  printf '%s\n' "$work_dir"
}

prepare_origin_branch() {
  work_dir=$1
  branch_name=$2
  commit_sha=$3
  remote_dir="$work_dir.origin.git"

  git init --bare -q "$remote_dir"
  (
    cd "$work_dir"
    git remote add origin "$remote_dir"
    git push -q origin "$commit_sha:refs/heads/$branch_name" refs/tags/uloop-project-runner-v3.0.0-beta.6
  )
}

assert_contains() {
  file=$1
  expected=$2

  if ! grep -F -- "$expected" "$file" >/dev/null; then
    echo "Expected $file to contain: $expected" >&2
    cat "$file" >&2
    exit 1
  fi
}

assert_not_contains() {
  file=$1
  unexpected=$2

  if grep -F -- "$unexpected" "$file" >/dev/null; then
    echo "Expected $file not to contain: $unexpected" >&2
    cat "$file" >&2
    exit 1
  fi
}

run_sync() {
  work_dir=$1
  existing_tag=$2
  existing_draft=$3
  existing_target=$4
  cli_release_state=${5:-published}
  cli_release_assets=${6:-complete}
  cli_release_wait_timeout=${7:-0}
  cli_release_wait_interval=${8:-0}
  cli_release_ready_after_attempts=${9:-}
  dispatcher_release_state=${10:-published}
  dispatcher_release_assets=${11:-complete}

  touch "$work_dir/gh.log"
  touch "$work_dir/go.log"
  touch "$work_dir/github-output.txt"
  touch "$work_dir/sleep.log"
  write_mock_commands "$work_dir"

  PATH="$work_dir/bin:$ORIGINAL_PATH" \
    GH_LOG="$work_dir/gh.log" \
    GO_LOG="$work_dir/go.log" \
    PROTOCOL_CHECK_STATUS="${PROTOCOL_CHECK_STATUS:-0}" \
    SLEEP_LOG="$work_dir/sleep.log" \
    EXISTING_RELEASE_TAG="$existing_tag" \
    EXISTING_RELEASE_DRAFT="$existing_draft" \
    EXISTING_RELEASE_TARGET="$existing_target" \
    CLI_RELEASE_STATE="$cli_release_state" \
    CLI_RELEASE_ASSETS="$cli_release_assets" \
    CLI_RELEASE_TAG="${CLI_RELEASE_TAG:-uloop-project-runner-v3.0.0-beta.6}" \
    CLI_RELEASE_WAIT_TIMEOUT_SECONDS="$cli_release_wait_timeout" \
    CLI_RELEASE_WAIT_INTERVAL_SECONDS="$cli_release_wait_interval" \
    CLI_RELEASE_READY_AFTER_ATTEMPTS="$cli_release_ready_after_attempts" \
    DISPATCHER_RELEASE_STATE="$dispatcher_release_state" \
    DISPATCHER_RELEASE_ASSETS="$dispatcher_release_assets" \
    DISPATCHER_ASSET_LIST_FAIL="${DISPATCHER_ASSET_LIST_FAIL:-false}" \
    CREATE_RELEASE_RACE_TAG="${CREATE_RELEASE_RACE_TAG:-}" \
    CREATE_RELEASE_RACE_TARGET="${CREATE_RELEASE_RACE_TARGET:-}" \
    GITHUB_OUTPUT="$work_dir/github-output.txt" \
    GITHUB_REPOSITORY=hatayama/unity-cli-loop \
    ULOOP_REPO_ROOT="$work_dir" \
    "$SCRIPT" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"
}

# Verifies a missing root package release is created from the release-please commit, not from the latest follow-up commit.
test_creates_missing_root_release_from_release_commit() {
  work_dir=$(create_release_repo creates-missing-root)
  release_sha=$(cat "$work_dir/release-sha.txt")

  run_sync "$work_dir" "" false ""

  assert_contains "$work_dir/gh.log" "release view v3.0.0-beta.6 --repo hatayama/unity-cli-loop --json isDraft,targetCommitish"
  assert_contains "$work_dir/gh.log" "release create v3.0.0-beta.6 --repo hatayama/unity-cli-loop --title v3.0.0-beta.6 --notes-file"
  assert_contains "$work_dir/gh.log" "--target $release_sha --prerelease"
  assert_contains "$work_dir/gh.log" "release view uloop-project-runner-v3.0.0-beta.6 --repo hatayama/unity-cli-loop --json isDraft,targetCommitish,assets"
  assert_contains "$work_dir/go.log" "run ./cmd/check-protocol-minimum-version --verify-release --ref $release_sha"
  assert_contains "$work_dir/github-output.txt" "ready=true"
}

# Verifies an existing root package release is accepted without creating another release.
test_existing_root_release_is_reused() {
  work_dir=$(create_release_repo existing-root)
  release_sha=$(cat "$work_dir/release-sha.txt")

  run_sync "$work_dir" v3.0.0-beta.6 false "$release_sha"

  assert_contains "$work_dir/output.txt" "Release v3.0.0-beta.6 already exists."
  assert_not_contains "$work_dir/gh.log" "release create"
}

# Verifies an existing release target branch is compared through the fetched origin branch.
test_existing_root_release_target_branch_resolves_via_origin() {
  work_dir=$(create_release_repo existing-root-branch-target)
  release_sha=$(cat "$work_dir/release-sha.txt")
  prepare_origin_branch "$work_dir" v3-beta "$release_sha"

  run_sync "$work_dir" v3.0.0-beta.6 false v3-beta

  assert_contains "$work_dir/output.txt" "Release v3.0.0-beta.6 already exists."
  assert_not_contains "$work_dir/stderr.txt" "points at"
}

# Verifies a draft root package release is published once it points at the expected release commit.
test_existing_draft_root_release_is_published() {
  work_dir=$(create_release_repo draft-root)
  release_sha=$(cat "$work_dir/release-sha.txt")

  run_sync "$work_dir" v3.0.0-beta.6 true "$release_sha"

  assert_contains "$work_dir/gh.log" "release edit v3.0.0-beta.6 --repo hatayama/unity-cli-loop --draft=false --prerelease"
}

# Verifies draft package releases are not published when their release commit cannot be checked.
test_existing_draft_root_release_without_release_commit_fails() {
  work_dir=$(create_release_repo draft-root-missing-release-commit)

  (
    cd "$work_dir"
    write_release_files 3.0.0-beta.7
  )

  if CLI_RELEASE_TAG=uloop-project-runner-v3.0.0-beta.7 run_sync "$work_dir" v3.0.0-beta.7 true "manual-release-target"; then
    echo "Expected draft release without a release commit to fail." >&2
    exit 1
  fi

  assert_contains "$work_dir/stderr.txt" "Draft release v3.0.0-beta.7 cannot be protocol-verified"
  assert_not_contains "$work_dir/gh.log" "release edit v3.0.0-beta.7"
}

# Verifies package releases wait until the matching Project runner release is public.
test_waits_for_cli_release_before_creating_root_release() {
  work_dir=$(create_release_repo waits-for-cli)

  run_sync "$work_dir" "" false "" missing

  assert_contains "$work_dir/output.txt" "Project runner release uloop-project-runner-v3.0.0-beta.6 is not published with complete assets; package release sync will wait."
  assert_not_contains "$work_dir/gh.log" "release create v3.0.0-beta.6"
  assert_contains "$work_dir/github-output.txt" "ready=false"
}

# Verifies package releases wait until the matching Project runner release has all native assets.
test_waits_for_cli_assets_before_creating_root_release() {
  work_dir=$(create_release_repo waits-for-cli-assets)

  run_sync "$work_dir" "" false "" published missing

  assert_contains "$work_dir/output.txt" "Project runner release uloop-project-runner-v3.0.0-beta.6 is not published with complete assets; package release sync will wait."
  assert_not_contains "$work_dir/gh.log" "release create v3.0.0-beta.6"
  assert_contains "$work_dir/github-output.txt" "ready=false"
}

# Verifies package releases wait until the minimum dispatcher release has all dispatcher assets.
test_waits_for_dispatcher_assets_before_creating_root_release() {
  work_dir=$(create_release_repo waits-for-dispatcher-assets)

  run_sync "$work_dir" "" false "" published complete 0 0 "" published missing

  assert_contains "$work_dir/output.txt" "Dispatcher release dispatcher-v3.0.0 is not published with complete assets; package release sync will wait."
  assert_not_contains "$work_dir/gh.log" "release create v3.0.0-beta.6"
  assert_contains "$work_dir/github-output.txt" "ready=false"
}

# Verifies dispatcher asset-list failures cannot make the package release look ready.
test_waits_when_dispatcher_asset_list_fails() {
  work_dir=$(create_release_repo waits-for-dispatcher-asset-list)

  DISPATCHER_ASSET_LIST_FAIL=true run_sync "$work_dir" "" false "" published complete 0 0 "" published complete

  assert_contains "$work_dir/output.txt" "Dispatcher release dispatcher-v3.0.0 is not published with complete assets; package release sync will wait."
  assert_not_contains "$work_dir/gh.log" "release create v3.0.0-beta.6"
  assert_contains "$work_dir/github-output.txt" "ready=false"
}

# Verifies the release sync waits for a concurrently publishing Project runner release before creating package releases.
test_retries_until_cli_assets_are_ready() {
  work_dir=$(create_release_repo retries-until-cli-ready)
  release_sha=$(cat "$work_dir/release-sha.txt")

  run_sync "$work_dir" "" false "" published complete 3 0 3

  assert_contains "$work_dir/output.txt" "Project runner release uloop-project-runner-v3.0.0-beta.6 is not published with complete assets yet; waiting 1s before retry."
  assert_contains "$work_dir/sleep.log" "1"
  assert_contains "$work_dir/output.txt" "Project runner release uloop-project-runner-v3.0.0-beta.6 is now published with complete assets."
  assert_contains "$work_dir/gh.log" "release create v3.0.0-beta.6 --repo hatayama/unity-cli-loop --title v3.0.0-beta.6 --notes-file"
  assert_contains "$work_dir/gh.log" "--target $release_sha --prerelease"
  assert_contains "$work_dir/github-output.txt" "ready=true"
}

# Verifies a manifest key rename at an unchanged version is not mistaken for the release-please release commit.
test_key_rename_commit_is_not_treated_as_release_commit() {
  work_dir=$(create_key_rename_repo key-rename-root)
  release_sha=$(cat "$work_dir/release-sha.txt")

  run_sync "$work_dir" v3.0.0-beta.6 false "$release_sha"

  assert_contains "$work_dir/output.txt" "Release v3.0.0-beta.6 already exists."
  assert_not_contains "$work_dir/stderr.txt" "points at"
}

# Verifies a package-root move that relocates the changelog file is not mistaken for the release-please release commit even when git rename detection is unavailable.
test_changelog_move_commit_is_not_treated_as_release_commit() {
  work_dir=$(create_changelog_move_repo changelog-move-root)
  release_sha=$(cat "$work_dir/release-sha.txt")

  run_sync "$work_dir" v3.0.0-beta.6 false "$release_sha"

  assert_contains "$work_dir/output.txt" "Release v3.0.0-beta.6 already exists."
  assert_not_contains "$work_dir/stderr.txt" "points at"
}

# Verifies the sync never creates dispatcher releases: dispatcher-publish owns the
# tag/draft/assets/publish flow, and a sync-created release would go public before
# its assets are uploaded.
test_dispatcher_package_release_is_left_to_dispatcher_publish() {
  work_dir=$(create_release_repo dispatcher-left-to-publish)

  run_sync "$work_dir" "" false ""

  assert_not_contains "$work_dir/gh.log" "release view dispatcher-v3.0.0-beta.6"
  assert_not_contains "$work_dir/gh.log" "release create dispatcher-v"
  assert_contains "$work_dir/github-output.txt" "ready=true"
}

# Verifies a root package release created by another workflow during creation is reused.
test_concurrent_root_release_creation_is_reused() {
  work_dir=$(create_release_repo concurrent-root-create)
  release_sha=$(cat "$work_dir/release-sha.txt")

  CREATE_RELEASE_RACE_TAG=v3.0.0-beta.6 CREATE_RELEASE_RACE_TARGET="$release_sha" run_sync "$work_dir" "" false ""

  assert_contains "$work_dir/output.txt" "Release v3.0.0-beta.6 was created by another workflow."
  assert_contains "$work_dir/gh.log" "release create v3.0.0-beta.6 --repo hatayama/unity-cli-loop --title v3.0.0-beta.6 --notes-file"
  assert_not_contains "$work_dir/stderr.txt" "release already exists"
  assert_contains "$work_dir/github-output.txt" "ready=true"
}

assert_contains "$SCRIPT" 'fetch_cli_release_tag "$cli_release_tag"'
test_creates_missing_root_release_from_release_commit
test_existing_root_release_is_reused
test_existing_root_release_target_branch_resolves_via_origin
test_existing_draft_root_release_is_published
test_existing_draft_root_release_without_release_commit_fails
test_waits_for_cli_release_before_creating_root_release
test_waits_for_cli_assets_before_creating_root_release
test_waits_for_dispatcher_assets_before_creating_root_release
test_waits_when_dispatcher_asset_list_fails
test_retries_until_cli_assets_are_ready
test_key_rename_commit_is_not_treated_as_release_commit
test_changelog_move_commit_is_not_treated_as_release_commit
test_dispatcher_package_release_is_left_to_dispatcher_publish
test_concurrent_root_release_creation_is_reused
