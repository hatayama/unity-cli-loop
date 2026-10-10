#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
SCRIPT="$ROOT_DIR/scripts/check-unity-package-release.sh"
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

  # The mock answers only the reads the check is allowed to make; a release
  # creation, edit, or any other write is an unexpected command and fails.
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
      printf '[{"name":"install.sh","size":1},{"name":"install.sh.sha256","size":1},{"name":"install.ps1","size":1},{"name":"install.ps1.sha256","size":1},{"name":"uloop-dispatcher-darwin-amd64.tar.gz","size":1},{"name":"uloop-dispatcher-darwin-amd64.tar.gz.sha256","size":1},{"name":"uloop-dispatcher-darwin-arm64.tar.gz","size":1},{"name":"uloop-dispatcher-darwin-arm64.tar.gz.sha256","size":1},{"name":"uloop-dispatcher-windows-amd64.zip","size":1},{"name":"uloop-dispatcher-windows-amd64.zip.sha256","size":1}]'
      ;;
    legacy)
      printf '[{"name":"install.sh","size":1},{"name":"install.ps1","size":1},{"name":"uloop-dispatcher-darwin-amd64.tar.gz","size":1},{"name":"uloop-dispatcher-darwin-amd64.tar.gz.sha256","size":1},{"name":"uloop-dispatcher-darwin-arm64.tar.gz","size":1},{"name":"uloop-dispatcher-darwin-arm64.tar.gz.sha256","size":1},{"name":"uloop-dispatcher-windows-amd64.zip","size":1},{"name":"uloop-dispatcher-windows-amd64.zip.sha256","size":1}]'
      ;;
    missing)
      printf '[]'
      ;;
  esac
}

release_state_json() {
  state=$1
  assets_function=$2

  case "$state" in
    published)
      printf '{"isDraft":false,"assets":'
      "$assets_function"
      printf '}\n'
      ;;
    draft)
      printf '{"isDraft":true,"assets":'
      "$assets_function"
      printf '}\n'
      ;;
    missing)
      echo "release not found" >&2
      exit 1
      ;;
    unavailable)
      echo "HTTP 502: Bad Gateway" >&2
      exit 1
      ;;
  esac
}

if [ "$1" = "release" ] && [ "$2" = "view" ]; then
  case "$3" in
    uloop-project-runner-v3.0.0-beta.6)
      release_state_json "${CLI_RELEASE_STATE:-published}" asset_json
      exit 0
      ;;
    dispatcher-v3.0.0)
      release_state_json "${DISPATCHER_RELEASE_STATE:-published}" dispatcher_asset_json
      exit 0
      ;;
  esac
  echo "release not found" >&2
  exit 1
fi

if [ "$1" = "api" ]; then
  case "$2" in
    repos/*/commits/*)
      printf '%s\n' "$CLI_RELEASE_TARGET"
      exit 0
      ;;
  esac
fi

if [ "$1" = "release" ] && [ "$2" = "download" ]; then
  download_dir=""
  previous_argument=""
  for argument in "$@"; do
    if [ "$previous_argument" = "--dir" ]; then
      download_dir=$argument
      break
    fi
    previous_argument=$argument
  done
  mkdir -p "$download_dir"
  for argument in "$@"; do
    case "$argument" in
      uloop-project-runner-*.tar.gz|uloop-project-runner-*.zip|uloop-project-runner-*.sha256|*.sigstore.json)
        printf '%s\n' "mock release asset" > "$download_dir/$argument"
        ;;
    esac
  done
  exit 0
fi

if [ "$1" = "attestation" ] && [ "$2" = "verify" ]; then
  printf '[{"verificationResult":{"signature":{"certificate":{"sourceRepositoryDigest":"%s"}}}}]\n' "${CLI_ATTESTATION_DIGEST:-$CLI_RELEASE_TARGET}"
  exit 0
fi

echo "unexpected gh command: $*" >&2
exit 1
MOCK_GH

  chmod +x "$mock_bin/gh"

  # GO_CHATTY makes the verifiers print to stdout, as real go run output can.
  cat > "$mock_bin/go" <<'MOCK_GO'
#!/bin/sh
set -eu

printf '%s\n' "$*" >> "$GO_LOG"
if [ "${GO_CHATTY:-false}" = "true" ]; then
  echo "verified $*"
fi

case "$*" in
  *check-package-pin-consistency*)
    exit "${PIN_CONSISTENCY_CHECK_STATUS:-0}"
    ;;
esac

exit "${PROTOCOL_CHECK_STATUS:-0}"
MOCK_GO

  chmod +x "$mock_bin/go"
}

write_release_files() {
  version=$1
  unity_package_key=${2:-Packages/src}
  unity_changelog_path=${3:-CHANGELOG.md}

  mkdir -p Packages/src cli/dispatcher cli/project-runner scripts cli/release-automation
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
    "cli/dispatcher": {
      "component": "dispatcher",
      "release-type": "go",
      "include-v-in-tag": true,
      "include-component-in-tag": true,
      "changelog-path": "CHANGELOG.md"
    },
    "cli/project-runner": {
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
  "cli/dispatcher": "$version",
  "cli/project-runner": "$version"
}
EOF_MANIFEST

  cat > Packages/src/project-runner-pin.json <<EOF_PIN
{
  "projectRunnerVersion": "$version",
  "minimumDispatcherVersion": "3.0.0"
}
EOF_PIN

  cat > Packages/src/CHANGELOG.md <<EOF_CHANGELOG
# Changelog

## [$version](https://example.test/compare/old...new)

### Bug Fixes

* keep the root package release baseline available
EOF_CHANGELOG

  cat > cli/project-runner/CHANGELOG.md <<EOF_CLI_CHANGELOG
# Changelog

## [$version](https://example.test/compare/cli-old...cli-new)

### Bug Fixes

* keep the Project runner release baseline available
EOF_CLI_CHANGELOG

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
    install.sh.sha256 \
    install.ps1 \
    install.ps1.sha256 \
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

init_test_repo() {
  git init -q
  git config user.email "test@example.com"
  git config user.name "Test User"
}

# create_release_repo records the release commit of 3.0.0-beta.6 in
# release-sha.txt and adds a follow-up commit, so a check that reports the
# branch tip instead of the release commit is caught.
create_release_repo() {
  name=$1
  work_dir="$TMP_DIR/$name"
  mkdir -p "$work_dir"

  (
    cd "$work_dir"
    init_test_repo

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
    init_test_repo

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
    init_test_repo
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

# assert_output_is_release_commit checks the output contract: the release
# commit on one line and nothing else.
assert_output_is_release_commit() {
  work_dir=$1
  expected_output=$(cat "$work_dir/release-sha.txt")
  actual_output=$(cat "$work_dir/output.txt")
  line_count=$(wc -l < "$work_dir/output.txt" | tr -d ' ')

  if [ "$actual_output" != "$expected_output" ] || [ "$line_count" != "1" ]; then
    echo "Expected only the release commit $expected_output on stdout, got:" >&2
    cat "$work_dir/output.txt" >&2
    cat "$work_dir/stderr.txt" >&2
    exit 1
  fi
}

assert_output_is_empty() {
  work_dir=$1

  if [ -s "$work_dir/output.txt" ]; then
    echo "Expected empty stdout, got:" >&2
    cat "$work_dir/output.txt" >&2
    exit 1
  fi
}

# run_check takes the mock settings of one run as NAME=value arguments rather
# than as assignments before the call, which some shells keep after a function
# returns and would leak into the next test.
run_check() {
  work_dir=$1
  shift

  : > "$work_dir/gh.log"
  : > "$work_dir/go.log"
  write_mock_commands "$work_dir"

  env \
    PATH="$work_dir/bin:$ORIGINAL_PATH" \
    GH_LOG="$work_dir/gh.log" \
    GO_LOG="$work_dir/go.log" \
    CLI_RELEASE_TARGET="$(cat "$work_dir/release-sha.txt")" \
    GITHUB_REPOSITORY=hatayama/unity-cli-loop \
    ULOOP_REPO_ROOT="$work_dir" \
    "$@" \
    "$SCRIPT" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"
}

# expect_check_failure runs the check, requires it to fail without printing a
# commit, and keeps the logs for the caller's assertions.
expect_check_failure() {
  work_dir=$1
  description=$2
  shift 2

  if run_check "$work_dir" "$@"; then
    echo "Expected the check to fail: $description" >&2
    cat "$work_dir/output.txt" "$work_dir/stderr.txt" >&2
    exit 1
  fi
  assert_output_is_empty "$work_dir"
}

# Verifies a due release prints only its release-please release commit, not the later follow-up commit, after the runner attestations, protocol, and pin checks pass at that commit.
test_due_release_prints_its_release_commit() {
  work_dir=$(create_release_repo due-release)
  release_sha=$(cat "$work_dir/release-sha.txt")

  run_check "$work_dir"

  assert_output_is_release_commit "$work_dir"
  assert_contains "$work_dir/gh.log" "release view uloop-project-runner-v3.0.0-beta.6 --repo hatayama/unity-cli-loop --json isDraft,assets"
  assert_contains "$work_dir/gh.log" "attestation verify"
  assert_contains "$work_dir/gh.log" "release view dispatcher-v3.0.0 --repo hatayama/unity-cli-loop --json isDraft,assets"
  assert_contains "$work_dir/go.log" "run ./cmd/check-protocol-minimum-version --verify-release --ref $release_sha"
  assert_contains "$work_dir/go.log" "run ./cmd/check-package-pin-consistency --repo-root $work_dir --ref $release_sha"
}

# Verifies a chatty verifier cannot corrupt the output the caller reads as the commit.
test_chatty_verifier_does_not_corrupt_the_output() {
  work_dir=$(create_release_repo chatty-verifier)

  run_check "$work_dir" GO_CHATTY=true

  assert_output_is_release_commit "$work_dir"
  assert_contains "$work_dir/stderr.txt" "verified run ./cmd/check-package-pin-consistency"
}

# Verifies each runner release state that is not "published with every asset" leaves the release not due, with a notice and without running the release commit checks.
test_runner_release_that_is_not_ready_is_not_due() {
  for runner_case in missing:complete draft:complete published:missing published:empty; do
    runner_state=${runner_case%%:*}
    runner_assets=${runner_case#*:}
    work_dir=$(create_release_repo "runner-not-ready-$runner_state-$runner_assets")

    run_check "$work_dir" CLI_RELEASE_STATE="$runner_state" CLI_RELEASE_ASSETS="$runner_assets"

    assert_output_is_empty "$work_dir"
    assert_contains "$work_dir/stderr.txt" "::notice::Project runner release uloop-project-runner-v3.0.0-beta.6 is not published with complete assets yet"
    assert_not_contains "$work_dir/gh.log" "attestation verify"
    assert_not_contains "$work_dir/go.log" "check-protocol-minimum-version"
  done
}

# Verifies a dispatcher floor release without every asset, or whose asset list cannot be read, leaves the release not due.
test_dispatcher_release_that_is_not_ready_is_not_due() {
  work_dir=$(create_release_repo dispatcher-missing-assets)
  run_check "$work_dir" DISPATCHER_RELEASE_ASSETS=missing
  assert_output_is_empty "$work_dir"
  assert_contains "$work_dir/stderr.txt" "::notice::Dispatcher release dispatcher-v3.0.0 is not published with complete assets yet"
  assert_not_contains "$work_dir/go.log" "check-protocol-minimum-version"

  work_dir=$(create_release_repo dispatcher-asset-list-fails)
  run_check "$work_dir" DISPATCHER_ASSET_LIST_FAIL=true
  assert_output_is_empty "$work_dir"
  assert_contains "$work_dir/stderr.txt" "::notice::Dispatcher release dispatcher-v3.0.0 is not published with complete assets yet"
}

# Verifies a release lookup that fails for another reason than "not found" fails the check instead of reading as "not published yet".
test_release_lookup_failure_fails() {
  work_dir=$(create_release_repo runner-lookup-fails)
  expect_check_failure "$work_dir" "runner release lookup failure" CLI_RELEASE_STATE=unavailable
  assert_contains "$work_dir/stderr.txt" "HTTP 502"

  work_dir=$(create_release_repo dispatcher-lookup-fails)
  expect_check_failure "$work_dir" "dispatcher release lookup failure" DISPATCHER_RELEASE_STATE=unavailable
  assert_contains "$work_dir/stderr.txt" "HTTP 502"
}

# Verifies dispatcher floor readiness checks assets against the floor release's own tag generation, not HEAD's grown list.
test_dispatcher_release_ready_uses_tag_generation_asset_list() {
  work_dir=$(create_release_repo dispatcher-tag-generation-assets)

  (
    cd "$work_dir"
    cat > scripts/verify-dispatcher-release-assets.sh <<'EOF_VERIFY_DISPATCHER_LEGACY'
#!/bin/sh
set -eu

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
EOF_VERIFY_DISPATCHER_LEGACY
    git add scripts/verify-dispatcher-release-assets.sh
    git commit -q -m "fix: dispatcher floor generation asset requirements"
    git tag dispatcher-v3.0.0

    write_release_files 3.0.0-beta.6
    git add .
    git commit -q -m "fix: require installer checksums"
  )

  run_check "$work_dir" DISPATCHER_RELEASE_ASSETS=legacy

  assert_output_is_release_commit "$work_dir"
}

# Verifies Project runner readiness checks assets against the runner release's own tag generation, not HEAD's grown list.
test_runner_release_ready_uses_tag_generation_asset_list() {
  work_dir=$(create_release_repo runner-tag-generation-assets)

  (
    cd "$work_dir"
    cat > scripts/verify-native-cli-release-assets.sh <<'EOF_VERIFY_GROWN'
#!/bin/sh
set -eu

if [ "${1:-}" = "--list" ]; then
  printf '%s\n' \
    uloop-project-runner-darwin-amd64.tar.gz \
    uloop-project-runner-darwin-amd64.tar.gz.sha256 \
    uloop-project-runner-darwin-arm64.tar.gz \
    uloop-project-runner-darwin-arm64.tar.gz.sha256 \
    uloop-project-runner-windows-amd64.zip \
    uloop-project-runner-windows-amd64.zip.sha256 \
    uloop-project-runner-linux-amd64.tar.gz \
    uloop-project-runner-linux-amd64.tar.gz.sha256
  exit 0
fi

exit 1
EOF_VERIFY_GROWN
    git add scripts/verify-native-cli-release-assets.sh
    git commit -q -m "fix: require linux runner assets"
  )

  run_check "$work_dir"

  assert_output_is_release_commit "$work_dir"
}

# Verifies a runner attestation that does not match the runner tag fails instead of reading as "not due", because no later run can repair it.
test_runner_attestation_digest_mismatch_fails() {
  work_dir=$(create_release_repo runner-attestation-mismatch)

  expect_check_failure "$work_dir" "attestation digest mismatch" CLI_ATTESTATION_DIGEST=wrong-digest

  assert_contains "$work_dir/stderr.txt" "docs/release-recovery-runbook.md"
  assert_not_contains "$work_dir/stderr.txt" "::notice::"
}

# Verifies a release commit whose protocol or pin check fails is refused instead of reported as due.
test_release_commit_that_fails_its_checks_fails() {
  work_dir=$(create_release_repo package-pin-mismatch)
  release_sha=$(cat "$work_dir/release-sha.txt")
  expect_check_failure "$work_dir" "package pin mismatch" PIN_CONSISTENCY_CHECK_STATUS=1
  assert_contains "$work_dir/go.log" "run ./cmd/check-package-pin-consistency --repo-root $work_dir --ref $release_sha"

  work_dir=$(create_release_repo protocol-too-old)
  expect_check_failure "$work_dir" "protocol check failure" PROTOCOL_CHECK_STATUS=1
  assert_not_contains "$work_dir/go.log" "check-package-pin-consistency"
}

# Verifies a manifest version that no release-please release commit records fails instead of picking another commit.
test_missing_release_commit_fails() {
  work_dir=$(create_release_repo missing-release-commit)
  (
    cd "$work_dir"
    write_release_files 3.0.0-beta.7
    jq '."cli/project-runner" = "3.0.0-beta.6"' .release-please-manifest.json > manifest.tmp
    mv manifest.tmp .release-please-manifest.json
  )

  expect_check_failure "$work_dir" "no release commit for the manifest version"

  assert_contains "$work_dir/stderr.txt" "No release-please commit for Packages/src version 3.0.0-beta.7 was found."
}

# Verifies a failed read of the commit history fails with git's own error instead of reading as a history without the release commit.
test_unreadable_commit_history_fails() {
  work_dir=$(create_release_repo unreadable-commit-history)
  real_git=$(command -v git)
  mkdir -p "$work_dir/bin"
  cat > "$work_dir/bin/git" <<MOCK_GIT
#!/bin/sh
if [ "\$1" = "log" ]; then
  echo "fatal: simulated commit history read failure" >&2
  exit 128
fi
exec "$real_git" "\$@"
MOCK_GIT
  chmod +x "$work_dir/bin/git"

  expect_check_failure "$work_dir" "commit history read failure"

  assert_contains "$work_dir/stderr.txt" "fatal: simulated commit history read failure"
  assert_not_contains "$work_dir/stderr.txt" "No release-please commit"
}

# Verifies a manifest key rename at an unchanged version is not mistaken for the release-please release commit.
test_key_rename_commit_is_not_treated_as_release_commit() {
  work_dir=$(create_key_rename_repo key-rename-root)

  expect_check_failure "$work_dir" "key rename commit"

  assert_contains "$work_dir/stderr.txt" "No release-please commit for Packages/src version 3.0.0-beta.6 was found."
}

# Verifies a package-root move that relocates the changelog file is not mistaken for the release-please release commit even when git rename detection is unavailable.
test_changelog_move_commit_is_not_treated_as_release_commit() {
  work_dir=$(create_changelog_move_repo changelog-move-root)

  expect_check_failure "$work_dir" "changelog move commit"

  assert_contains "$work_dir/stderr.txt" "No release-please commit for Packages/src version 3.0.0-beta.6 was found."
}

# Verifies an unreadable manifest or config, or one without the package entry, fails instead of reading as a release that is not due.
test_unreadable_release_inputs_fail() {
  work_dir=$(create_release_repo manifest-not-json)
  printf '%s\n' "Packages/src: 3.0.0-beta.6" > "$work_dir/.release-please-manifest.json"
  expect_check_failure "$work_dir" "manifest that is not JSON"

  work_dir=$(create_release_repo manifest-without-package)
  jq 'del(."Packages/src")' "$work_dir/.release-please-manifest.json" > "$work_dir/manifest.tmp"
  mv "$work_dir/manifest.tmp" "$work_dir/.release-please-manifest.json"
  expect_check_failure "$work_dir" "manifest without the package version"
  assert_contains "$work_dir/stderr.txt" "has no version for Packages/src"

  work_dir=$(create_release_repo config-not-json)
  printf '%s\n' "packages:" > "$work_dir/release-please-config.json"
  expect_check_failure "$work_dir" "config that is not JSON"

  work_dir=$(create_release_repo config-without-changelog)
  jq 'del(.packages."Packages/src"."changelog-path")' "$work_dir/release-please-config.json" > "$work_dir/config.tmp"
  mv "$work_dir/config.tmp" "$work_dir/release-please-config.json"
  expect_check_failure "$work_dir" "config without the package changelog path"
  assert_contains "$work_dir/stderr.txt" "has no changelog-path"

  # An unreadable pin must not read as "no dispatcher floor", which would skip
  # the dispatcher check.
  work_dir=$(create_release_repo pin-not-json)
  printf '%s\n' "minimumDispatcherVersion: 3.0.0" > "$work_dir/Packages/src/project-runner-pin.json"
  expect_check_failure "$work_dir" "pin that is not JSON"
  assert_not_contains "$work_dir/go.log" "check-protocol-minimum-version"
}

# Verifies a manifest without a runner version skips the runner checks, and a pin without a dispatcher floor skips the dispatcher check, as before.
test_absent_dependencies_are_not_checked() {
  work_dir=$(create_release_repo no-runner-version)
  jq 'del(."cli/project-runner")' "$work_dir/.release-please-manifest.json" > "$work_dir/manifest.tmp"
  mv "$work_dir/manifest.tmp" "$work_dir/.release-please-manifest.json"
  run_check "$work_dir"
  assert_output_is_release_commit "$work_dir"
  assert_not_contains "$work_dir/gh.log" "uloop-project-runner-v"

  work_dir=$(create_release_repo no-dispatcher-floor)
  jq 'del(.minimumDispatcherVersion)' "$work_dir/Packages/src/project-runner-pin.json" > "$work_dir/pin.tmp"
  mv "$work_dir/pin.tmp" "$work_dir/Packages/src/project-runner-pin.json"
  run_check "$work_dir"
  assert_output_is_release_commit "$work_dir"
  assert_not_contains "$work_dir/gh.log" "dispatcher-v"
}

test_due_release_prints_its_release_commit
test_chatty_verifier_does_not_corrupt_the_output
test_runner_release_that_is_not_ready_is_not_due
test_dispatcher_release_that_is_not_ready_is_not_due
test_release_lookup_failure_fails
test_dispatcher_release_ready_uses_tag_generation_asset_list
test_runner_release_ready_uses_tag_generation_asset_list
test_runner_attestation_digest_mismatch_fails
test_release_commit_that_fails_its_checks_fails
test_missing_release_commit_fails
test_unreadable_commit_history_fails
test_key_rename_commit_is_not_treated_as_release_commit
test_changelog_move_commit_is_not_treated_as_release_commit
test_unreadable_release_inputs_fail
test_absent_dependencies_are_not_checked
