#!/bin/sh
set -eu

# Stamps the shared-release-input hash into each release package root.
# release-please attributes a commit to a component only when the commit
# touches that package root, so changes to shared inputs (the common module,
# installer assets) need these stamp updates to reach every affected release.
# SCRIPT_ROOT is the checkout that owns this script so `go run` can find the
# digest command even when ULOOP_REPO_ROOT points at a fixture repository.
SCRIPT_ROOT=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
ROOT_DIR=${ULOOP_REPO_ROOT:-$SCRIPT_ROOT}

cd "$ROOT_DIR"
# Resolve a relative ULOOP_REPO_ROOT after the cd so later `go run -path`
# still points at the fixture when the digest command changes directory.
ROOT_DIR=$(pwd)

# Input selection mirrors the release trigger guard
# (cli/release-automation/internal/automation/release_trigger_guard.go):
# only package roots imported by shipped binaries count; release-please stamp
# targets such as contract.json do not. The embedded tool catalog is the one
# JSON that does count, structure only; descriptions are excluded because the
# CLI reloads them from SKILL.md at run time.
list_shared_common_inputs() {
  git ls-files -- \
    cli/common/go.mod \
    cli/common/go.sum \
    'cli/common/clicontract/' \
    'cli/common/clicore/' \
    'cli/common/errors/' \
    'cli/common/ipcendpoint/' \
    'cli/common/progress/' \
    'cli/common/project/' \
    'cli/common/skilldocs/' \
    'cli/common/skillscan/' \
    'cli/common/tooldocs/' \
    'cli/common/tools/' \
    'cli/common/ui/' \
    'cli/common/unityipc/' \
    'cli/common/unityprocess/' \
    'cli/common/vibelog/' |
    grep -E '\.go$|\.ps1$|/go\.mod$|/go\.sum$|^cli/common/tools/default-tools\.json$' |
    grep -v '_test\.go$' || true
}

list_dispatcher_only_common_inputs() {
  git ls-files -- 'cli/common/version/' |
    grep '\.go$' |
    grep -v '_test\.go$' || true
}

list_dispatcher_script_inputs() {
  git ls-files -- \
    scripts/install.sh \
    scripts/install.ps1 \
    'cli/dispatcher/internal/install/scripts/' \
    'cli/dispatcher/internal/uninstall/scripts/'
}

# Hashes path/content pairs so renames change the stamp as well. Only tracked
# files are hashed; the release trigger guard in PR CI remains the safety net
# for files that were not yet added when the stamp ran.
hash_input_list() {
  # The manifest goes through a command substitution instead of a direct pipe
  # into `git hash-object --stdin`: POSIX pipelines report only the last
  # command's status, which would let a mid-list hash failure produce a
  # plausible-looking stamp over a truncated manifest.
  input_manifest=$(LC_ALL=C sort | while IFS= read -r input_file; do
    [ -n "$input_file" ] || continue
    if [ "$input_file" = "cli/common/tools/default-tools.json" ]; then
      object_hash=$(cd "$SCRIPT_ROOT/cli/release-automation" && go run ./cmd/tool-catalog-shape-digest -path "$ROOT_DIR/$input_file") || exit 1
    else
      object_hash=$(git hash-object "$input_file") || exit 1
    fi
    printf '%s %s\n' "$input_file" "$object_hash"
  done) || {
    echo "Failed to hash a shared release input." >&2
    exit 1
  }

  printf '%s\n' "$input_manifest" | git hash-object --stdin
}

write_stamp() {
  stamp_path=$1
  hash_value=$2

  printf '{\n  "schemaVersion": 1,\n  "sharedInputsHash": "%s"\n}\n' "$hash_value" > "$stamp_path"
  echo "Stamped $stamp_path"
}

common_hash=$(list_shared_common_inputs | hash_input_list)
dispatcher_hash=$({ list_shared_common_inputs; list_dispatcher_only_common_inputs; list_dispatcher_script_inputs; } | hash_input_list)

write_stamp cli/project-runner/shared-inputs-stamp.json "$common_hash"
write_stamp cli/dispatcher/shared-inputs-stamp.json "$dispatcher_hash"
