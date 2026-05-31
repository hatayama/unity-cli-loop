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
      printf '[{"name":"uloop-darwin-amd64.tar.gz","size":1},{"name":"uloop-darwin-amd64.tar.gz.sha256","size":1},{"name":"uloop-darwin-arm64.tar.gz","size":1},{"name":"uloop-darwin-arm64.tar.gz.sha256","size":1},{"name":"uloop-windows-amd64.zip","size":1},{"name":"uloop-windows-amd64.zip.sha256","size":1}]'
      ;;
    missing)
      printf '[]'
      ;;
    empty)
      printf '[{"name":"uloop-darwin-amd64.tar.gz","size":0},{"name":"uloop-darwin-amd64.tar.gz.sha256","size":1},{"name":"uloop-darwin-arm64.tar.gz","size":1},{"name":"uloop-darwin-arm64.tar.gz.sha256","size":1},{"name":"uloop-windows-amd64.zip","size":1},{"name":"uloop-windows-amd64.zip.sha256","size":1}]'
      ;;
  esac
}

if [ "$1" = "release" ] && [ "$2" = "view" ]; then
  tag=$3
  if [ "$tag" = "cli-v3.0.0-beta.6" ]; then
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

  if [ -n "${EXISTING_RELEASE_TAG:-}" ] && [ "$tag" = "$EXISTING_RELEASE_TAG" ]; then
    printf '{"isDraft":%s,"targetCommitish":"%s","assets":[]}\n' "$EXISTING_RELEASE_DRAFT" "$EXISTING_RELEASE_TARGET"
    exit 0
  fi

  echo "release not found" >&2
  exit 1
fi

if [ "$1" = "release" ] && [ "$2" = "create" ]; then
  exit 0
fi

if [ "$1" = "release" ] && [ "$2" = "edit" ]; then
  exit 0
fi

echo "unexpected gh command: $*" >&2
exit 1
MOCK_GH

  chmod +x "$mock_bin/gh"
}

write_release_files() {
  version=$1

  mkdir -p Packages/src cli scripts
  cat > release-please-config.json <<'EOF_CONFIG'
{
  "packages": {
    ".": {
      "release-type": "go",
      "include-v-in-tag": true,
      "include-component-in-tag": false,
      "changelog-path": "Packages/src/CHANGELOG.md"
    },
    "cli": {
      "component": "cli",
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
  ".": "$version",
  "cli": "$version"
}
EOF_MANIFEST

  cat > Packages/src/CHANGELOG.md <<EOF_CHANGELOG
# Changelog

## [$version](https://example.test/compare/old...new)

### Bug Fixes

* keep the root package release baseline available
EOF_CHANGELOG

  cat > cli/CHANGELOG.md <<EOF_CLI_CHANGELOG
# Changelog

## [$version](https://example.test/compare/cli-old...cli-new)

### Bug Fixes

* keep the CLI release baseline available
EOF_CLI_CHANGELOG

  cat > scripts/verify-native-cli-release-assets.sh <<'EOF_VERIFY'
#!/bin/sh
set -eu

if [ "${1:-}" = "--list" ]; then
  printf '%s\n' \
    uloop-darwin-amd64.tar.gz \
    uloop-darwin-amd64.tar.gz.sha256 \
    uloop-darwin-arm64.tar.gz \
    uloop-darwin-arm64.tar.gz.sha256 \
    uloop-windows-amd64.zip \
    uloop-windows-amd64.zip.sha256
  exit 0
fi

exit 1
EOF_VERIFY
  chmod +x scripts/verify-native-cli-release-assets.sh
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
    git rev-parse HEAD > "$work_dir/release-sha.txt"

    printf '%s\n' "follow-up" > follow-up.txt
    git add follow-up.txt
    git commit -q -m "fix: follow up after release"
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
    git push -q origin "$commit_sha:refs/heads/$branch_name"
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

  touch "$work_dir/gh.log"
  touch "$work_dir/github-output.txt"
  write_mock_commands "$work_dir"

  PATH="$work_dir/bin:$ORIGINAL_PATH" \
    GH_LOG="$work_dir/gh.log" \
    EXISTING_RELEASE_TAG="$existing_tag" \
    EXISTING_RELEASE_DRAFT="$existing_draft" \
    EXISTING_RELEASE_TARGET="$existing_target" \
    CLI_RELEASE_STATE="$cli_release_state" \
    CLI_RELEASE_ASSETS="$cli_release_assets" \
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
  assert_contains "$work_dir/gh.log" "release view cli-v3.0.0-beta.6 --repo hatayama/unity-cli-loop --json isDraft,targetCommitish,assets"
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

# Verifies package releases wait until the matching CLI release is public.
test_waits_for_cli_release_before_creating_root_release() {
  work_dir=$(create_release_repo waits-for-cli)

  run_sync "$work_dir" "" false "" missing

  assert_contains "$work_dir/output.txt" "CLI release cli-v3.0.0-beta.6 is not published with complete assets; package release sync will wait."
  assert_not_contains "$work_dir/gh.log" "release create v3.0.0-beta.6"
  assert_contains "$work_dir/github-output.txt" "ready=false"
}

# Verifies package releases wait until the matching CLI release has all native assets.
test_waits_for_cli_assets_before_creating_root_release() {
  work_dir=$(create_release_repo waits-for-cli-assets)

  run_sync "$work_dir" "" false "" published missing

  assert_contains "$work_dir/output.txt" "CLI release cli-v3.0.0-beta.6 is not published with complete assets; package release sync will wait."
  assert_not_contains "$work_dir/gh.log" "release create v3.0.0-beta.6"
  assert_contains "$work_dir/github-output.txt" "ready=false"
}

test_creates_missing_root_release_from_release_commit
test_existing_root_release_is_reused
test_existing_root_release_target_branch_resolves_via_origin
test_existing_draft_root_release_is_published
test_waits_for_cli_release_before_creating_root_release
test_waits_for_cli_assets_before_creating_root_release
