#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
SCRIPT="$ROOT_DIR/scripts/sync-release-please-go-cli-dist.sh"
TMP_DIR=$(mktemp -d)
ORIGINAL_PATH=$PATH

cleanup() {
  rm -rf "$TMP_DIR"
}

trap cleanup EXIT INT HUP TERM

write_mock_commands() {
  work_dir=$1
  mock_bin="$work_dir/bin"
  mkdir -p "$mock_bin" "$work_dir/scripts"

  cat > "$mock_bin/gh" <<'MOCK_GH'
#!/bin/sh
set -eu

if [ "$1" = "pr" ] && [ "$2" = "list" ]; then
  printf '%s\n' "$GH_PR_LIST_JSON"
  exit 0
fi

if [ "$1" = "pr" ] && [ "$2" = "ready" ]; then
  printf '%s\n' "$*" >> "$GH_LOG"
  exit 0
fi

echo "unexpected gh command: $*" >&2
exit 1
MOCK_GH

  cat > "$mock_bin/git" <<'MOCK_GIT'
#!/bin/sh
set -eu

printf '%s\n' "$*" >> "$GIT_LOG"

case "$1" in
  fetch|checkout|config|add|commit|push)
    exit 0
    ;;
  diff)
    if [ "$DIST_DIRTY" = "true" ]; then
      exit 1
    fi
    exit 0
    ;;
  ls-files)
    if [ "$DIST_UNTRACKED" = "true" ]; then
      printf '%s\n' "Packages/src/Cli~/dist/linux-amd64/uloop"
    fi
    exit 0
    ;;
  *)
    echo "unexpected git command: $*" >&2
    exit 1
    ;;
esac
MOCK_GIT

  cat > "$work_dir/scripts/build-go-cli.sh" <<'MOCK_BUILD'
#!/bin/sh
set -eu
printf '%s\n' build >> "$SCRIPT_LOG"
MOCK_BUILD

  cat > "$work_dir/scripts/check-go-cli.sh" <<'MOCK_CHECK'
#!/bin/sh
set -eu
printf '%s\n' check >> "$SCRIPT_LOG"
MOCK_CHECK

  chmod +x "$mock_bin/gh" "$mock_bin/git" "$work_dir/scripts/build-go-cli.sh" "$work_dir/scripts/check-go-cli.sh"
}

assert_contains() {
  file=$1
  expected=$2

  if ! grep -F "$expected" "$file" >/dev/null; then
    echo "Expected $file to contain: $expected" >&2
    cat "$file" >&2
    exit 1
  fi
}

assert_not_contains() {
  file=$1
  unexpected=$2

  if grep -F "$unexpected" "$file" >/dev/null; then
    echo "Expected $file not to contain: $unexpected" >&2
    cat "$file" >&2
    exit 1
  fi
}

run_case() {
  name=$1
  pr_json=$2
  dist_dirty=$3
  dist_untracked=$4

  work_dir="$TMP_DIR/$name"
  mkdir -p "$work_dir"
  write_mock_commands "$work_dir"
  touch "$work_dir/gh.log" "$work_dir/git.log" "$work_dir/script.log"

  (
    cd "$work_dir"
    PATH="$work_dir/bin:$ORIGINAL_PATH" \
      GH_PR_LIST_JSON="$pr_json" \
      GH_LOG="$work_dir/gh.log" \
      GIT_LOG="$work_dir/git.log" \
      SCRIPT_LOG="$work_dir/script.log" \
      DIST_DIRTY="$dist_dirty" \
      DIST_UNTRACKED="$dist_untracked" \
      TARGET_BRANCH=v3-beta \
      GITHUB_REPOSITORY=hatayama/unity-cli-loop \
      "$SCRIPT" > output.txt 2> stderr.txt
  )
}

# Verifies the sync exits cleanly when no release PR exists.
test_no_release_pr_exits() {
  run_case no-release-pr '[]' false false

  assert_contains "$TMP_DIR/no-release-pr/output.txt" "No pending release-please PR found for v3-beta."
  assert_not_contains "$TMP_DIR/no-release-pr/script.log" "build"
  assert_not_contains "$TMP_DIR/no-release-pr/git.log" "checkout"
}

# Verifies unrelated pending PRs are ignored even if they use the release-please label.
test_ignores_non_release_pending_pr() {
  run_case non-release-pr '[{"number":1042,"headRefName":"feature/example","title":"fix: unrelated change","url":"https://example.test/pr/1042"}]' false false

  assert_contains "$TMP_DIR/non-release-pr/output.txt" "No pending release-please PR found for v3-beta."
  assert_not_contains "$TMP_DIR/non-release-pr/script.log" "build"
  assert_not_contains "$TMP_DIR/non-release-pr/git.log" "checkout"
}

# Verifies release-looking manual PRs are ignored unless they use a release-please branch.
test_ignores_release_title_without_release_please_branch() {
  run_case manual-release-title '[{"number":1042,"headRefName":"feature/manual-release","title":"chore(v3-beta): release 3.0.0-beta.5","url":"https://example.test/pr/1042"}]' false false

  assert_contains "$TMP_DIR/manual-release-title/output.txt" "No pending release-please PR found for v3-beta."
  assert_not_contains "$TMP_DIR/manual-release-title/script.log" "build"
  assert_not_contains "$TMP_DIR/manual-release-title/git.log" "checkout"
}

# Verifies current dist files run validation without an extra commit.
test_current_dist_checks_without_commit() {
  run_case current-dist '[{"number":1043,"headRefName":"release-please--branches--v3-beta","title":"chore(v3-beta): release 3.0.0-beta.5","url":"https://example.test/pr/1043"}]' false false

  assert_contains "$TMP_DIR/current-dist/script.log" "build"
  assert_contains "$TMP_DIR/current-dist/script.log" "check"
  assert_contains "$TMP_DIR/current-dist/gh.log" "pr ready 1043 --repo hatayama/unity-cli-loop --undo"
  assert_contains "$TMP_DIR/current-dist/git.log" "fetch origin release-please--branches--v3-beta"
  assert_contains "$TMP_DIR/current-dist/git.log" "checkout -B release-please--branches--v3-beta FETCH_HEAD"
  assert_not_contains "$TMP_DIR/current-dist/git.log" "commit -m"
  assert_not_contains "$TMP_DIR/current-dist/git.log" "push origin"
}

# Verifies grouped manifest release PR titles are still matched by branch and label.
test_grouped_release_title_matches() {
  run_case grouped-release-title '[{"number":1043,"headRefName":"release-please--branches--v3-beta","title":"chore: release v3-beta","url":"https://example.test/pr/1043"}]' false false

  assert_contains "$TMP_DIR/grouped-release-title/script.log" "build"
  assert_contains "$TMP_DIR/grouped-release-title/git.log" "checkout -B release-please--branches--v3-beta FETCH_HEAD"
}

# Verifies older component release-please branches remain supported during configuration transitions.
test_component_release_branch_still_matches() {
  run_case component-release '[{"number":1043,"headRefName":"release-please--branches--v3-beta--components--io.github.hatayama.uloopmcp","title":"chore(v3-beta): release 3.0.0-beta.5","url":"https://example.test/pr/1043"}]' false false

  assert_contains "$TMP_DIR/component-release/script.log" "build"
  assert_contains "$TMP_DIR/component-release/git.log" "checkout -B release-please--branches--v3-beta--components--io.github.hatayama.uloopmcp FETCH_HEAD"
}

# Verifies stale dist files are committed and pushed to the release PR branch.
test_stale_dist_commits_and_pushes() {
  run_case stale-dist '[{"number":1043,"headRefName":"release-please--branches--v3-beta","title":"chore(v3-beta): release 3.0.0-beta.5","url":"https://example.test/pr/1043"}]' true false

  assert_contains "$TMP_DIR/stale-dist/script.log" "build"
  assert_contains "$TMP_DIR/stale-dist/script.log" "check"
  assert_contains "$TMP_DIR/stale-dist/gh.log" "pr ready 1043 --repo hatayama/unity-cli-loop --undo"
  assert_contains "$TMP_DIR/stale-dist/git.log" "add Packages/src/Cli~/dist"
  assert_contains "$TMP_DIR/stale-dist/git.log" "commit -m chore(v3-beta): update native CLI binaries"
  assert_contains "$TMP_DIR/stale-dist/git.log" "push origin HEAD:release-please--branches--v3-beta"
}

# Verifies newly generated untracked dist files are committed and pushed to the release PR branch.
test_untracked_dist_commits_and_pushes() {
  run_case untracked-dist '[{"number":1043,"headRefName":"release-please--branches--v3-beta","title":"chore(v3-beta): release 3.0.0-beta.5","url":"https://example.test/pr/1043"}]' false true

  assert_contains "$TMP_DIR/untracked-dist/script.log" "build"
  assert_contains "$TMP_DIR/untracked-dist/script.log" "check"
  assert_contains "$TMP_DIR/untracked-dist/git.log" "ls-files --others --exclude-standard -- Packages/src/Cli~/dist"
  assert_contains "$TMP_DIR/untracked-dist/git.log" "add Packages/src/Cli~/dist"
  assert_contains "$TMP_DIR/untracked-dist/git.log" "commit -m chore(v3-beta): update native CLI binaries"
  assert_contains "$TMP_DIR/untracked-dist/git.log" "push origin HEAD:release-please--branches--v3-beta"
}

test_no_release_pr_exits
test_ignores_non_release_pending_pr
test_ignores_release_title_without_release_please_branch
test_current_dist_checks_without_commit
test_grouped_release_title_matches
test_component_release_branch_still_matches
test_stale_dist_commits_and_pushes
test_untracked_dist_commits_and_pushes
