#!/bin/sh
# Verifies install.sh's uname-dependent helpers. detect_asset_name maps each
# supported uname OS/architecture pair to the published dispatcher asset, and
# rejects pairs that have no published asset (Linux arm64, unsupported OSes)
# with an explicit error instead of searching releases for a missing file.
# detect_bash_profile_path points Linux bash users at ~/.bashrc and keeps the
# macOS ~/.bash_profile choice.
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
INSTALL_SCRIPT="$ROOT_DIR/scripts/install.sh"

extract_function() {
  name=$1
  awk -v want="$name" '
    $0 ~ ("^" want "\\(\\)") { in_fn = 1 }
    in_fn { print }
    in_fn && $0 == "}" { exit }
  ' "$INSTALL_SCRIPT"
}

work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT

mock_bin="$work_dir/bin"
mkdir -p "$mock_bin"
cat > "$mock_bin/uname" <<'MOCK_UNAME'
#!/bin/sh
set -eu

case "${1:-}" in
  -s)
    echo "${MOCK_UNAME_OS:-Darwin}"
    ;;
  -m)
    echo "${MOCK_UNAME_ARCH:-arm64}"
    ;;
  *)
    echo "unexpected uname argument: $*" >&2
    exit 1
    ;;
esac
MOCK_UNAME
chmod +x "$mock_bin/uname"
PATH="$mock_bin:$PATH"
export PATH

# The mock uname reads these; functions called with a prefix assignment do not
# reliably export it in POSIX sh, so each case exports them inside a subshell.

eval "$(extract_function detect_asset_name)"
eval "$(extract_function detect_installed_command_name)"
eval "$(extract_function detect_bash_profile_path)"

expect_asset() {
  mock_os=$1
  mock_arch=$2
  expected_asset=$3
  expected_command=$4
  if ! actual_asset=$(export MOCK_UNAME_OS="$mock_os" MOCK_UNAME_ARCH="$mock_arch"; detect_asset_name 2>"$work_dir/stderr"); then
    echo "FAIL: $mock_os $mock_arch was rejected: $(cat "$work_dir/stderr")" >&2
    exit 1
  fi
  if [ "$actual_asset" != "$expected_asset" ]; then
    echo "FAIL: $mock_os $mock_arch resolved '$actual_asset', expected '$expected_asset'" >&2
    exit 1
  fi
  asset_name=$actual_asset
  actual_command=$(detect_installed_command_name)
  if [ "$actual_command" != "$expected_command" ]; then
    echo "FAIL: $mock_os $mock_arch installs '$actual_command', expected '$expected_command'" >&2
    exit 1
  fi
}

expect_reject() {
  mock_os=$1
  mock_arch=$2
  expected_message=$3
  # detect_asset_name exits on rejection, so it must run in a subshell.
  if (export MOCK_UNAME_OS="$mock_os" MOCK_UNAME_ARCH="$mock_arch"; detect_asset_name) >/dev/null 2>"$work_dir/stderr"; then
    echo "FAIL: expected $mock_os $mock_arch to be rejected" >&2
    exit 1
  fi
  if ! grep -F "$expected_message" "$work_dir/stderr" >/dev/null; then
    echo "FAIL: $mock_os $mock_arch rejection did not mention '$expected_message': $(cat "$work_dir/stderr")" >&2
    exit 1
  fi
}

expect_bash_profile() {
  mock_os=$1
  expected_profile=$2
  actual_profile=$(export MOCK_UNAME_OS="$mock_os"; detect_bash_profile_path)
  if [ "$actual_profile" != "$expected_profile" ]; then
    echo "FAIL: $mock_os bash profile resolved '$actual_profile', expected '$expected_profile'" >&2
    exit 1
  fi
}

expect_asset Linux x86_64 uloop-dispatcher-linux-amd64.tar.gz uloop
expect_asset Darwin arm64 uloop-dispatcher-darwin-arm64.tar.gz uloop
expect_asset Darwin x86_64 uloop-dispatcher-darwin-amd64.tar.gz uloop
expect_asset MINGW64_NT-10.0 x86_64 uloop-dispatcher-windows-amd64.zip uloop.exe
expect_reject Linux aarch64 "Unsupported Linux architecture"
expect_reject FreeBSD x86_64 "Unsupported OS"

# An existing ~/.bash_profile must not win on Linux, where terminals never read it.
HOME="$work_dir/home"
mkdir -p "$HOME"
: > "$HOME/.bash_profile"
expect_bash_profile Linux "$HOME/.bashrc"
expect_bash_profile Darwin "$HOME/.bash_profile"

echo "install.sh uname-dependent helper tests passed"
