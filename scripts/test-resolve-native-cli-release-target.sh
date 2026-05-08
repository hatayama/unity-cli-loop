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
  show)
    printf '{\n  "Packages/src": "%s"\n}\n' "$PREVIOUS_VERSION"
    ;;
  rev-parse)
    printf '%s\n' target-sha
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

case "$GH_RELEASE_STATE" in
  published)
    printf 'false\n'
    ;;
  draft)
    printf 'true\n'
    ;;
  missing)
    exit 1
    ;;
  *)
    echo "unexpected release state: $GH_RELEASE_STATE" >&2
    exit 1
    ;;
esac
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
  previous_version=$3
  event_name=$4
  branch_name=$5
  release_state=$6
  expected_publish=$7

  work_dir="$TMP_DIR/$name"
  mock_bin="$work_dir/bin"
  mkdir -p "$work_dir"
  write_mock_commands "$mock_bin"

  (
    cd "$work_dir"
    write_manifest "$current_version"
    PATH="$mock_bin:$ORIGINAL_PATH" \
      PREVIOUS_VERSION="$previous_version" \
      GH_RELEASE_STATE="$release_state" \
      EVENT_NAME="$event_name" \
      EVENT_REF_NAME="$branch_name" \
      BEFORE_SHA=before \
      INPUT_RELEASE_TAG= \
      INPUT_DRY_RUN=false \
      "$SCRIPT" > output.txt 2> stderr.txt

    assert_contains output.txt "publish=$expected_publish"
    assert_contains output.txt "tag=v$current_version"
    assert_contains output.txt "version=$current_version"
    assert_contains output.txt "sha=target-sha"
    assert_contains output.txt "dry_run=false"
  )
}

run_failure_case() {
  name=$1
  current_version=$2
  previous_version=$3
  event_name=$4
  branch_name=$5
  release_state=$6
  expected_error=$7

  work_dir="$TMP_DIR/$name"
  mock_bin="$work_dir/bin"
  mkdir -p "$work_dir"
  write_mock_commands "$mock_bin"

  (
    cd "$work_dir"
    write_manifest "$current_version"
    set +e
    PATH="$mock_bin:$ORIGINAL_PATH" \
      PREVIOUS_VERSION="$previous_version" \
      GH_RELEASE_STATE="$release_state" \
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

# Verifies same-version pushes skip when the release is already public.
test_same_version_published_release_skips() {
  run_success_case same-version-published 3.0.0-beta.2 3.0.0-beta.2 push v3-beta published false
}

# Verifies same-version pushes retry when the release is missing.
test_same_version_missing_release_retries() {
  run_success_case same-version-missing 3.0.0-beta.2 3.0.0-beta.2 push v3-beta missing true
}

# Verifies same-version pushes retry when the release is still a draft.
test_same_version_draft_release_retries() {
  run_success_case same-version-draft 3.0.0-beta.2 3.0.0-beta.2 push v3-beta draft true
}

# Verifies version-changing pushes publish without checking the existing release.
test_version_change_publishes() {
  run_success_case version-change 3.0.0-beta.2 3.0.0-beta.1 push v3-beta missing true
}

# Verifies main refuses prerelease versions.
test_main_prerelease_fails() {
  run_failure_case main-prerelease 3.0.0-beta.2 3.0.0-beta.1 push main missing "Refusing to publish prerelease version 3.0.0-beta.2 from main."
}

# Verifies v3-beta refuses stable versions.
test_v3_beta_stable_fails() {
  run_failure_case v3-beta-stable 3.0.0 2.1.1 push v3-beta missing "Refusing to publish stable version 3.0.0 from v3-beta."
}

test_same_version_published_release_skips
test_same_version_missing_release_retries
test_same_version_draft_release_retries
test_version_change_publishes
test_main_prerelease_fails
test_v3_beta_stable_fails
