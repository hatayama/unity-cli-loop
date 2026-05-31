#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
TMP_DIR=$(mktemp -d)
ORIGINAL_PATH=$PATH

cleanup() {
  PATH=$ORIGINAL_PATH
  export PATH
  rm -rf "$TMP_DIR"
}

trap cleanup EXIT INT HUP TERM

write_fake_go() {
  version=$1
  bin_dir="$TMP_DIR/bin"
  mkdir -p "$bin_dir"

  cat > "$bin_dir/go" <<EOF_GO
#!/bin/sh
set -eu

if [ "\$1" = "env" ] && [ "\$2" = "GOVERSION" ]; then
  printf '%s\n' 'go$version'
  exit 0
fi

echo "unexpected go command: \$*" >&2
exit 1
EOF_GO
  chmod +x "$bin_dir/go"
  PATH="$bin_dir:$ORIGINAL_PATH"
  export PATH
}

create_repo() {
  version=$1
  repo_dir="$TMP_DIR/repo-$version"
  mkdir -p "$repo_dir/cli"
  printf '%s\n' "$version" > "$repo_dir/cli/.go-version"
  printf '%s\n' "$repo_dir"
}

SUCCESS_REPO=$(create_repo "1.26.3")
write_fake_go "1.26.3"
(
  . "$ROOT_DIR/scripts/go-cli-toolchain.sh"
  require_go_cli_toolchain "$SUCCESS_REPO"
)

FAILURE_REPO=$(create_repo "1.26.3")
write_fake_go "1.26.2"
MISMATCH_STDOUT="$TMP_DIR/mismatch.out"
MISMATCH_STDERR="$TMP_DIR/mismatch.err"
if (
  . "$ROOT_DIR/scripts/go-cli-toolchain.sh"
  require_go_cli_toolchain "$FAILURE_REPO"
) >"$MISMATCH_STDOUT" 2>"$MISMATCH_STDERR"; then
  echo "Expected Go version mismatch to fail." >&2
  exit 1
fi

if ! grep -F "Go 1.26.3 is required" "$MISMATCH_STDERR" >/dev/null 2>&1; then
  echo "Expected mismatch error to mention the required Go version." >&2
  exit 1
fi

echo "go-cli-toolchain test passed"
