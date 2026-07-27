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

    mkdir -p cli/common/clicore/subpkg cli/common/clitest cli/common/tools cli/common/version/subpkg cli/dispatcher/internal/install/scripts cli/dispatcher/internal/uninstall/scripts cli/project-runner scripts
    printf 'package clicore\n' > cli/common/clicore/core.go
    printf 'package subpkg\n' > cli/common/clicore/subpkg/core.go
    printf 'package clicore\n\n// test-only content\n' > cli/common/clicore/core_test.go
    printf 'package clitest\n' > cli/common/clitest/clitest.go
    printf 'package version\n' > cli/common/version/compare.go
    printf 'package subpkg\n' > cli/common/version/subpkg/compare.go
    printf 'module example.test/common\n' > cli/common/go.mod
    printf '{"projectRunnerVersion": "1.0.0"}\n' > cli/common/contract.json
    printf '{"tools":[]}\n' > cli/common/tools/default-tools.json
    printf 'echo install\n' > scripts/install.sh
    printf 'Write-Host install\n' > scripts/install.ps1
    printf 'echo embedded install\n' > cli/dispatcher/internal/install/scripts/install_darwin.sh
    printf 'Write-Host embedded install\n' > cli/dispatcher/internal/install/scripts/install_windows.ps1
    printf 'echo embedded uninstall\n' > cli/dispatcher/internal/uninstall/scripts/uninstall_darwin.sh
    printf 'Write-Host embedded uninstall\n' > cli/dispatcher/internal/uninstall/scripts/uninstall_windows_delete.ps1
    printf 'Write-Host embedded uninstall launch\n' > cli/dispatcher/internal/uninstall/scripts/uninstall_windows_launch.ps1
    printf '{}\n' > cli/dispatcher/shared-inputs-stamp.json
    printf '{}\n' > cli/project-runner/shared-inputs-stamp.json
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
assert_valid_stamp "$work_dir" cli/project-runner/shared-inputs-stamp.json
assert_valid_stamp "$work_dir" cli/dispatcher/shared-inputs-stamp.json

runner_hash_initial=$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)
dispatcher_hash_initial=$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)

# Verifies the installer inputs make the dispatcher stamp differ from the runner stamp.
if [ "$runner_hash_initial" = "$dispatcher_hash_initial" ]; then
  echo "Expected the dispatcher stamp to also cover installer scripts." >&2
  exit 1
fi

# Verifies re-running without input changes is idempotent.
commit_fixture_change "$work_dir" "stamp baseline"
run_stamp "$work_dir"
if [ "$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)" != "$runner_hash_initial" ] ||
  [ "$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)" != "$dispatcher_hash_initial" ]; then
  echo "Expected repeated stamping without input changes to keep both hashes." >&2
  exit 1
fi

# Verifies a common Go source change moves both stamps.
printf 'package clicore\n\nconst changed = true\n' > "$work_dir/cli/common/clicore/core.go"
run_stamp "$work_dir"
runner_hash_after_common=$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)
dispatcher_hash_after_common=$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)
if [ "$runner_hash_after_common" = "$runner_hash_initial" ] ||
  [ "$dispatcher_hash_after_common" = "$dispatcher_hash_initial" ]; then
  echo "Expected a common source change to move both stamps." >&2
  exit 1
fi

# Verifies a change to the embedded tool catalog moves both stamps, since it is compiled into both
# binaries even though it is JSON.
commit_fixture_change "$work_dir" "common source change"
printf '{"tools":[{"name":"compile"}]}\n' > "$work_dir/cli/common/tools/default-tools.json"
run_stamp "$work_dir"
runner_hash_after_catalog=$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)
dispatcher_hash_after_catalog=$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)
if [ "$runner_hash_after_catalog" = "$runner_hash_after_common" ] ||
  [ "$dispatcher_hash_after_catalog" = "$dispatcher_hash_after_common" ]; then
  echo "Expected an embedded tool catalog change to move both stamps." >&2
  exit 1
fi
runner_hash_after_common=$runner_hash_after_catalog
dispatcher_hash_after_common=$dispatcher_hash_after_catalog

# Verifies a nested shared common Go source change also moves both stamps.
commit_fixture_change "$work_dir" "shared common change"
printf 'package subpkg\n\nconst changed = true\n' > "$work_dir/cli/common/clicore/subpkg/core.go"
run_stamp "$work_dir"
runner_hash_after_nested_common=$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)
dispatcher_hash_after_nested_common=$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)
if [ "$runner_hash_after_nested_common" = "$runner_hash_after_common" ] ||
  [ "$dispatcher_hash_after_nested_common" = "$dispatcher_hash_after_common" ]; then
  echo "Expected a nested common source change to move both stamps." >&2
  exit 1
fi

# Verifies an installer-only change moves only the dispatcher stamp.
commit_fixture_change "$work_dir" "nested shared common change"
printf 'echo install v2\n' > "$work_dir/scripts/install.sh"
run_stamp "$work_dir"
if [ "$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)" != "$runner_hash_after_nested_common" ]; then
  echo "Expected an installer-only change to keep the project-runner stamp." >&2
  exit 1
fi
if [ "$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)" = "$dispatcher_hash_after_nested_common" ]; then
  echo "Expected an installer-only change to move the dispatcher stamp." >&2
  exit 1
fi

# Verifies a Windows installer-only change also moves only the dispatcher stamp.
commit_fixture_change "$work_dir" "posix installer change"
runner_hash_after_installer=$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)
dispatcher_hash_after_installer=$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)
printf 'Write-Host install v2\n' > "$work_dir/scripts/install.ps1"
run_stamp "$work_dir"
if [ "$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)" != "$runner_hash_after_installer" ]; then
  echo "Expected a Windows installer-only change to keep the project-runner stamp." >&2
  exit 1
fi
if [ "$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)" = "$dispatcher_hash_after_installer" ]; then
  echo "Expected a Windows installer-only change to move the dispatcher stamp." >&2
  exit 1
fi

# Verifies an embedded installer template change also moves only the dispatcher stamp.
commit_fixture_change "$work_dir" "windows installer change"
runner_hash_after_windows_installer=$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)
dispatcher_hash_after_windows_installer=$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)
printf 'echo embedded install v2\n' > "$work_dir/cli/dispatcher/internal/install/scripts/install_darwin.sh"
run_stamp "$work_dir"
if [ "$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)" != "$runner_hash_after_windows_installer" ]; then
  echo "Expected an embedded installer change to keep the project-runner stamp." >&2
  exit 1
fi
if [ "$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)" = "$dispatcher_hash_after_windows_installer" ]; then
  echo "Expected an embedded installer change to move the dispatcher stamp." >&2
  exit 1
fi

# Verifies an embedded uninstaller template change also moves only the dispatcher stamp.
commit_fixture_change "$work_dir" "embedded installer change"
runner_hash_after_embedded_installer=$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)
dispatcher_hash_after_embedded_installer=$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)
printf 'Write-Host embedded uninstall v2\n' > "$work_dir/cli/dispatcher/internal/uninstall/scripts/uninstall_windows_delete.ps1"
run_stamp "$work_dir"
if [ "$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)" != "$runner_hash_after_embedded_installer" ]; then
  echo "Expected an embedded uninstaller change to keep the project-runner stamp." >&2
  exit 1
fi
if [ "$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)" = "$dispatcher_hash_after_embedded_installer" ]; then
  echo "Expected an embedded uninstaller change to move the dispatcher stamp." >&2
  exit 1
fi

# Verifies a dispatcher-only common package change moves only the dispatcher stamp.
commit_fixture_change "$work_dir" "embedded uninstaller change"
runner_hash_before_dispatcher_only=$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)
dispatcher_hash_before_dispatcher_only=$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)
printf 'package version\n\nconst changed = true\n' > "$work_dir/cli/common/version/compare.go"
run_stamp "$work_dir"
if [ "$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)" != "$runner_hash_before_dispatcher_only" ]; then
  echo "Expected a dispatcher-only common package change to keep the project-runner stamp." >&2
  exit 1
fi
if [ "$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)" = "$dispatcher_hash_before_dispatcher_only" ]; then
  echo "Expected a dispatcher-only common package change to move the dispatcher stamp." >&2
  exit 1
fi

# Verifies a nested dispatcher-only common package change also moves only the dispatcher stamp.
commit_fixture_change "$work_dir" "dispatcher-only common change"
runner_hash_before_nested_dispatcher_only=$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)
dispatcher_hash_before_nested_dispatcher_only=$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)
printf 'package subpkg\n\nconst changed = true\n' > "$work_dir/cli/common/version/subpkg/compare.go"
run_stamp "$work_dir"
if [ "$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)" != "$runner_hash_before_nested_dispatcher_only" ]; then
  echo "Expected a nested dispatcher-only common package change to keep the project-runner stamp." >&2
  exit 1
fi
if [ "$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)" = "$dispatcher_hash_before_nested_dispatcher_only" ]; then
  echo "Expected a nested dispatcher-only common package change to move the dispatcher stamp." >&2
  exit 1
fi

# Verifies test-only and stamp-target changes under common move neither stamp.
commit_fixture_change "$work_dir" "nested dispatcher-only common change"
runner_hash_before_test_only=$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)
dispatcher_hash_before_test_only=$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)
printf 'package clicore\n\n// updated test-only content\n' > "$work_dir/cli/common/clicore/core_test.go"
printf 'package clitest\n\nconst helper = true\n' > "$work_dir/cli/common/clitest/clitest.go"
printf '{"projectRunnerVersion": "1.0.1"}\n' > "$work_dir/cli/common/contract.json"
run_stamp "$work_dir"
if [ "$(stamp_hash "$work_dir" cli/project-runner/shared-inputs-stamp.json)" != "$runner_hash_before_test_only" ] ||
  [ "$(stamp_hash "$work_dir" cli/dispatcher/shared-inputs-stamp.json)" != "$dispatcher_hash_before_test_only" ]; then
  echo "Expected test-only and stamp-target changes to keep both stamps." >&2
  exit 1
fi

# Verifies stamping fails instead of writing a partial hash when an input cannot be hashed.
rm "$work_dir/cli/common/go.mod"
if ULOOP_REPO_ROOT="$work_dir" "$SCRIPT" > /dev/null 2>&1; then
  echo "Expected stamping to fail when a tracked input cannot be hashed." >&2
  exit 1
fi

echo "stamp-release-inputs tests passed."
