#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
SCRIPT="$ROOT_DIR/scripts/stamp-release-inputs.sh"
TMP_DIR=$(mktemp -d)

cleanup() {
  rm -rf "$TMP_DIR"
}

trap cleanup EXIT INT HUP TERM

create_fixture_repo() {
  work_dir="$TMP_DIR/fixture"
  mkdir -p "$work_dir"

  (
    cd "$work_dir"
    git init -q
    git config user.email "test@example.com"
    git config user.name "Test User"

    mkdir -p common/clicore dispatcher project-runner scripts
    printf 'package clicore\n' > common/clicore/core.go
    printf 'package clicore\n\n// test-only content\n' > common/clicore/core_test.go
    printf 'module example.test/common\n' > common/go.mod
    printf '{"projectRunnerVersion": "1.0.0"}\n' > common/contract.json
    printf 'echo install\n' > scripts/install.sh
    printf 'Write-Host install\n' > scripts/install.ps1
    printf '{}\n' > dispatcher/shared-inputs-stamp.json
    printf '{}\n' > project-runner/shared-inputs-stamp.json
    git add .
    git commit -qm "init fixture"
  )

  printf '%s\n' "$work_dir"
}

run_stamp() {
  work_dir=$1

  # Surface the captured logs on failure; the temp files vanish with the trap
  # cleanup, so a silent redirect would leave CI failures undiagnosable.
  if ULOOP_REPO_ROOT="$work_dir" "$SCRIPT" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"; then
    return 0
  fi
  echo "stamp-release-inputs.sh failed; captured output follows." >&2
  cat "$work_dir/output.txt" "$work_dir/stderr.txt" >&2
  exit 1
}

stamp_hash() {
  work_dir=$1
  stamp_path=$2

  jq -r '.sharedInputsHash' "$work_dir/$stamp_path"
}

assert_valid_stamp() {
  work_dir=$1
  stamp_path=$2

  schema_version=$(jq -r '.schemaVersion' "$work_dir/$stamp_path")
  if [ "$schema_version" != "1" ]; then
    echo "Expected $stamp_path schemaVersion to be 1, got $schema_version." >&2
    exit 1
  fi

  hash_value=$(stamp_hash "$work_dir" "$stamp_path")
  if [ -z "$hash_value" ] || [ "$hash_value" = "null" ]; then
    echo "Expected $stamp_path to contain a sharedInputsHash." >&2
    exit 1
  fi
}

commit_fixture_change() {
  work_dir=$1
  message=$2

  (
    cd "$work_dir"
    git add .
    git commit -qm "$message"
  )
}

work_dir=$(create_fixture_repo)

# Verifies both stamps are written as valid JSON with a shared-inputs hash.
run_stamp "$work_dir"
assert_valid_stamp "$work_dir" project-runner/shared-inputs-stamp.json
assert_valid_stamp "$work_dir" dispatcher/shared-inputs-stamp.json

runner_hash_initial=$(stamp_hash "$work_dir" project-runner/shared-inputs-stamp.json)
dispatcher_hash_initial=$(stamp_hash "$work_dir" dispatcher/shared-inputs-stamp.json)

# Verifies the installer inputs make the dispatcher stamp differ from the runner stamp.
if [ "$runner_hash_initial" = "$dispatcher_hash_initial" ]; then
  echo "Expected the dispatcher stamp to also cover installer scripts." >&2
  exit 1
fi

# Verifies re-running without input changes is idempotent.
commit_fixture_change "$work_dir" "stamp baseline"
run_stamp "$work_dir"
if [ "$(stamp_hash "$work_dir" project-runner/shared-inputs-stamp.json)" != "$runner_hash_initial" ] ||
  [ "$(stamp_hash "$work_dir" dispatcher/shared-inputs-stamp.json)" != "$dispatcher_hash_initial" ]; then
  echo "Expected repeated stamping without input changes to keep both hashes." >&2
  exit 1
fi

# Verifies a common Go source change moves both stamps.
printf 'package clicore\n\nconst changed = true\n' > "$work_dir/common/clicore/core.go"
run_stamp "$work_dir"
runner_hash_after_common=$(stamp_hash "$work_dir" project-runner/shared-inputs-stamp.json)
dispatcher_hash_after_common=$(stamp_hash "$work_dir" dispatcher/shared-inputs-stamp.json)
if [ "$runner_hash_after_common" = "$runner_hash_initial" ] ||
  [ "$dispatcher_hash_after_common" = "$dispatcher_hash_initial" ]; then
  echo "Expected a common source change to move both stamps." >&2
  exit 1
fi

# Verifies an installer-only change moves only the dispatcher stamp.
commit_fixture_change "$work_dir" "common change"
printf 'echo install v2\n' > "$work_dir/scripts/install.sh"
run_stamp "$work_dir"
if [ "$(stamp_hash "$work_dir" project-runner/shared-inputs-stamp.json)" != "$runner_hash_after_common" ]; then
  echo "Expected an installer-only change to keep the project-runner stamp." >&2
  exit 1
fi
if [ "$(stamp_hash "$work_dir" dispatcher/shared-inputs-stamp.json)" = "$dispatcher_hash_after_common" ]; then
  echo "Expected an installer-only change to move the dispatcher stamp." >&2
  exit 1
fi

# Verifies a Windows installer-only change also moves only the dispatcher stamp.
commit_fixture_change "$work_dir" "posix installer change"
runner_hash_after_installer=$(stamp_hash "$work_dir" project-runner/shared-inputs-stamp.json)
dispatcher_hash_after_installer=$(stamp_hash "$work_dir" dispatcher/shared-inputs-stamp.json)
printf 'Write-Host install v2\n' > "$work_dir/scripts/install.ps1"
run_stamp "$work_dir"
if [ "$(stamp_hash "$work_dir" project-runner/shared-inputs-stamp.json)" != "$runner_hash_after_installer" ]; then
  echo "Expected a Windows installer-only change to keep the project-runner stamp." >&2
  exit 1
fi
if [ "$(stamp_hash "$work_dir" dispatcher/shared-inputs-stamp.json)" = "$dispatcher_hash_after_installer" ]; then
  echo "Expected a Windows installer-only change to move the dispatcher stamp." >&2
  exit 1
fi

# Verifies test-only and stamp-target changes under common move neither stamp.
commit_fixture_change "$work_dir" "windows installer change"
runner_hash_before_test_only=$(stamp_hash "$work_dir" project-runner/shared-inputs-stamp.json)
dispatcher_hash_before_test_only=$(stamp_hash "$work_dir" dispatcher/shared-inputs-stamp.json)
printf 'package clicore\n\n// updated test-only content\n' > "$work_dir/common/clicore/core_test.go"
printf '{"projectRunnerVersion": "1.0.1"}\n' > "$work_dir/common/contract.json"
run_stamp "$work_dir"
if [ "$(stamp_hash "$work_dir" project-runner/shared-inputs-stamp.json)" != "$runner_hash_before_test_only" ] ||
  [ "$(stamp_hash "$work_dir" dispatcher/shared-inputs-stamp.json)" != "$dispatcher_hash_before_test_only" ]; then
  echo "Expected test-only and stamp-target changes to keep both stamps." >&2
  exit 1
fi

# Verifies stamping fails instead of writing a partial hash when an input cannot be hashed.
rm "$work_dir/common/go.mod"
if ULOOP_REPO_ROOT="$work_dir" "$SCRIPT" > /dev/null 2>&1; then
  echo "Expected stamping to fail when a tracked input cannot be hashed." >&2
  exit 1
fi

echo "stamp-release-inputs tests passed."
