#!/bin/sh
set -eu

# Stamps the shared-release-input hash into each release package root.
# release-please attributes a commit to a component only when the commit
# touches that package root, so changes to shared inputs (the common module,
# installer assets) need these stamp updates to reach every affected release.
ROOT_DIR=${ULOOP_REPO_ROOT:-$(CDPATH= cd "$(dirname "$0")/.." && pwd)}

cd "$ROOT_DIR"

# Input selection mirrors the release trigger guard
# (tools/release-automation/internal/automation/release_trigger_guard.go):
# non-test Go sources and module files count; release-please stamp targets
# such as contract.json and default-tools.json do not.
list_common_inputs() {
  git ls-files -- 'common/*.go' common/go.mod common/go.sum |
    grep -v '_test\.go$' || true
}

list_installer_inputs() {
  git ls-files -- scripts/install.sh scripts/install.ps1
}

# Hashes path/content pairs so renames change the stamp as well. Only tracked
# files are hashed; the release trigger guard in PR CI remains the safety net
# for files that were not yet added when the stamp ran.
hash_input_list() {
  LC_ALL=C sort | while IFS= read -r input_file; do
    [ -n "$input_file" ] || continue
    printf '%s ' "$input_file"
    git hash-object "$input_file"
  done | git hash-object --stdin
}

write_stamp() {
  stamp_path=$1
  hash_value=$2

  printf '{\n  "schemaVersion": 1,\n  "sharedInputsHash": "%s"\n}\n' "$hash_value" > "$stamp_path"
  echo "Stamped $stamp_path"
}

common_hash=$(list_common_inputs | hash_input_list)
dispatcher_hash=$({ list_common_inputs; list_installer_inputs; } | hash_input_list)

write_stamp project-runner/shared-inputs-stamp.json "$common_hash"
write_stamp dispatcher/shared-inputs-stamp.json "$dispatcher_hash"
