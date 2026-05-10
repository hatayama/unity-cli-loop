#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
TMP_DIR=$(mktemp -d)
ORIGINAL_PATH=$PATH

cleanup() {
  rm -rf "$TMP_DIR"
}

trap cleanup EXIT INT HUP TERM

assert_contains() {
  file=$1
  expected=$2

  if ! grep -F -- "$expected" "$file" >/dev/null; then
    echo "Expected $file to contain: $expected" >&2
    echo "Actual content:" >&2
    cat "$file" >&2
    exit 1
  fi
}

assert_not_contains() {
  file=$1
  unexpected=$2

  if grep -F -- "$unexpected" "$file" >/dev/null; then
    echo "Expected $file not to contain: $unexpected" >&2
    echo "Actual content:" >&2
    cat "$file" >&2
    exit 1
  fi
}

write_releases_json() {
  output_path=$1

  cat > "$output_path" <<'JSON'
[
  {
    "tag_name": "v3.0.0-beta.2",
    "draft": false,
    "prerelease": true,
    "assets": [
      {
        "name": "uloop-darwin-arm64.tar.gz",
        "browser_download_url": "https://github.com/hatayama/unity-cli-loop/releases/download/v3.0.0-beta.2/uloop-darwin-arm64.tar.gz"
      }
    ]
  },
  {
    "tag_name": "v2.0.0",
    "draft": false,
    "prerelease": false,
    "assets": [
      {
        "name": "uloop-darwin-arm64.tar.gz",
        "browser_download_url": "https://github.com/hatayama/unity-cli-loop/releases/download/v2.0.0/uloop-darwin-arm64.tar.gz"
      }
    ]
  }
]
JSON
}

write_mock_commands() {
  mock_bin=$1
  mkdir -p "$mock_bin"

  cat > "$mock_bin/uname" <<'MOCK_UNAME'
#!/bin/sh
set -eu

case "$1" in
  -s)
    echo Darwin
    ;;
  -m)
    echo arm64
    ;;
  *)
    echo "unexpected uname argument: $*" >&2
    exit 1
    ;;
esac
MOCK_UNAME

  cat > "$mock_bin/curl" <<'MOCK_CURL'
#!/bin/sh
set -eu

url=
output_file=
while [ "$#" -gt 0 ]; do
  case "$1" in
    -o)
      shift
      output_file=$1
      ;;
    -*)
      ;;
    *)
      url=$1
      ;;
  esac
  shift
done

case "$url" in
  https://api.github.com/repos/hatayama/unity-cli-loop/releases*)
    cat "$RELEASES_JSON"
    exit 0
    ;;
esac

printf '%s\n' "$url" >> "$CURL_LOG"

case "$url" in
  *v2.0.0/uloop-darwin-arm64.tar.gz)
    : > "$output_file"
    ;;
  *v2.0.0/uloop-darwin-arm64.tar.gz.sha256)
    printf 'fakehash  uloop-darwin-arm64.tar.gz\n' > "$output_file"
    ;;
  *v3.0.0-beta.2/uloop-darwin-arm64.tar.gz)
    if [ "${ULOOP_VERSION:-}" != "latest-beta" ]; then
      echo "Prerelease asset should not be downloaded: $url" >&2
      exit 1
    fi
    : > "$output_file"
    ;;
  *v3.0.0-beta.2/uloop-darwin-arm64.tar.gz.sha256)
    if [ "${ULOOP_VERSION:-}" != "latest-beta" ]; then
      echo "Prerelease checksum should not be downloaded: $url" >&2
      exit 1
    fi
    printf 'fakehash  uloop-darwin-arm64.tar.gz\n' > "$output_file"
    ;;
  *)
    echo "unexpected curl url: $url" >&2
    exit 1
    ;;
esac
MOCK_CURL

  cat > "$mock_bin/sha256sum" <<'MOCK_SHA256SUM'
#!/bin/sh
set -eu

if [ "$1" = "-c" ]; then
  exit 0
fi

echo "unexpected sha256sum arguments: $*" >&2
exit 1
MOCK_SHA256SUM

  cat > "$mock_bin/tar" <<'MOCK_TAR'
#!/bin/sh
set -eu

extract_dir=
while [ "$#" -gt 0 ]; do
  case "$1" in
    -C)
      shift
      extract_dir=$1
      ;;
  esac
  shift
done

if [ -z "$extract_dir" ]; then
  echo "tar extract directory is required" >&2
  exit 1
fi

cat > "$extract_dir/uloop" <<'ULOOP'
#!/bin/sh
echo "uloop mock version"
ULOOP
chmod +x "$extract_dir/uloop"
MOCK_TAR

  cat > "$mock_bin/npm" <<'MOCK_NPM'
#!/bin/sh
set -eu

printf '%s\n' "$*" >> "$NPM_LOG"
if [ "$1" = "uninstall" ] && [ "$2" = "-g" ] && [ "$3" = "--prefix" ] && [ "$5" = "uloop-cli" ]; then
  rm -f "$LEGACY_ULOOP"
  exit 0
fi

if [ "$1" = "uninstall" ] && [ "$2" = "-g" ] && [ "$3" = "uloop-cli" ]; then
  if [ -n "${DEFAULT_LEGACY_ULOOP:-}" ]; then
    rm -f "$DEFAULT_LEGACY_ULOOP"
  fi
  exit 0
fi

echo "unexpected npm arguments: $*" >&2
exit 1
MOCK_NPM

  chmod +x "$mock_bin/uname" "$mock_bin/curl" "$mock_bin/sha256sum" "$mock_bin/tar" "$mock_bin/npm"
}

test_posix_latest_skips_prerelease_assets() {
  work_dir="$TMP_DIR/posix-latest"
  mock_bin="$work_dir/bin"
  legacy_bin="$work_dir/npm-global/bin"
  legacy_package_dist="$work_dir/npm-global/lib/node_modules/uloop-cli/dist"
  install_dir="$work_dir/install"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  legacy_uloop="$legacy_bin/uloop"
  mkdir -p "$work_dir" "$legacy_bin" "$legacy_package_dist"
  : > "$curl_log"
  : > "$npm_log"
  printf '%s\n' 'legacy node cli bundle' > "$legacy_package_dist/cli.bundle.cjs"
  chmod +x "$legacy_package_dist/cli.bundle.cjs"
  ln -s "../lib/node_modules/uloop-cli/dist/cli.bundle.cjs" "$legacy_uloop"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$legacy_bin:$mock_bin:$ORIGINAL_PATH" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    LEGACY_ULOOP="$legacy_uloop" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$curl_log" "v2.0.0/uloop-darwin-arm64.tar.gz"
  assert_contains "$curl_log" "v2.0.0/uloop-darwin-arm64.tar.gz.sha256"
  assert_not_contains "$curl_log" "v3.0.0-beta.2"
  assert_contains "$npm_log" "uninstall -g --prefix $work_dir/npm-global uloop-cli"
  if [ -e "$legacy_uloop" ]; then
    echo "Expected mocked npm uninstall to remove the legacy Node uloop shim: $legacy_uloop" >&2
    exit 1
  fi
}

test_posix_latest_beta_selects_prerelease_assets() {
  work_dir="$TMP_DIR/posix-latest-beta"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  mkdir -p "$work_dir"
  : > "$curl_log"
  : > "$npm_log"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    ULOOP_VERSION=latest-beta \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$curl_log" "v3.0.0-beta.2/uloop-darwin-arm64.tar.gz"
  assert_contains "$curl_log" "v3.0.0-beta.2/uloop-darwin-arm64.tar.gz.sha256"
  assert_not_contains "$curl_log" "v2.0.0/uloop-darwin-arm64.tar.gz"
  assert_not_contains "$npm_log" "uninstall -g uloop-cli"
}

test_posix_removes_npm_package_even_when_native_command_is_first() {
  work_dir="$TMP_DIR/posix-native-first"
  mock_bin="$work_dir/bin"
  legacy_bin="$work_dir/npm-global/bin"
  legacy_package_dist="$work_dir/npm-global/lib/node_modules/uloop-cli/dist"
  install_dir="$work_dir/install"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  legacy_uloop="$legacy_bin/uloop"
  native_uloop="$install_dir/uloop"
  mkdir -p "$work_dir" "$legacy_bin" "$legacy_package_dist" "$install_dir"
  : > "$curl_log"
  : > "$npm_log"
  printf '%s\n' '#!/bin/sh' 'echo v3.0.0-beta.2' > "$native_uloop"
  chmod +x "$native_uloop"
  printf '%s\n' 'legacy node cli bundle' > "$legacy_package_dist/cli.bundle.cjs"
  chmod +x "$legacy_package_dist/cli.bundle.cjs"
  ln -s "../lib/node_modules/uloop-cli/dist/cli.bundle.cjs" "$legacy_uloop"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$install_dir:$legacy_bin:$mock_bin:$ORIGINAL_PATH" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    LEGACY_ULOOP="$legacy_uloop" \
    DEFAULT_LEGACY_ULOOP="$legacy_uloop" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$npm_log" "uninstall -g uloop-cli"
  assert_not_contains "$npm_log" "uninstall -g --prefix $install_dir uloop-cli"
  if [ -e "$legacy_uloop" ]; then
    echo "Expected default npm uninstall to remove the hidden legacy Node uloop shim: $legacy_uloop" >&2
    exit 1
  fi
}

test_posix_does_not_infer_npm_prefix_from_non_npm_command() {
  work_dir="$TMP_DIR/posix-non-npm-first"
  mock_bin="$work_dir/bin"
  non_npm_prefix="$work_dir/homebrew"
  non_npm_bin="$non_npm_prefix/bin"
  install_dir="$work_dir/install"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  non_npm_uloop="$non_npm_bin/uloop"
  mkdir -p "$work_dir" "$non_npm_bin"
  : > "$curl_log"
  : > "$npm_log"
  printf '%s\n' '#!/bin/sh' 'echo non npm uloop' > "$non_npm_uloop"
  chmod +x "$non_npm_uloop"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$non_npm_bin:$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$npm_log" "uninstall -g uloop-cli"
  assert_not_contains "$npm_log" "--prefix $non_npm_prefix"
  if [ ! -x "$non_npm_uloop" ]; then
    echo "Expected non-npm uloop command to remain untouched: $non_npm_uloop" >&2
    exit 1
  fi
}

test_posix_removes_npm_package_before_replacing_same_bin_path() {
  work_dir="$TMP_DIR/posix-same-bin"
  mock_bin="$work_dir/bin"
  legacy_bin="$work_dir/npm-global/bin"
  legacy_package_dist="$work_dir/npm-global/lib/node_modules/uloop-cli/dist"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  legacy_uloop="$legacy_bin/uloop"
  mkdir -p "$work_dir" "$legacy_bin" "$legacy_package_dist"
  : > "$curl_log"
  : > "$npm_log"
  printf '%s\n' 'legacy node cli bundle' > "$legacy_package_dist/cli.bundle.cjs"
  chmod +x "$legacy_package_dist/cli.bundle.cjs"
  ln -s "../lib/node_modules/uloop-cli/dist/cli.bundle.cjs" "$legacy_uloop"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$legacy_bin:$mock_bin:$ORIGINAL_PATH" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$legacy_bin" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    LEGACY_ULOOP="$legacy_uloop" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$npm_log" "uninstall -g --prefix $work_dir/npm-global uloop-cli"
  if [ ! -x "$legacy_uloop" ]; then
    echo "Expected native uloop to remain installed after same-bin npm removal: $legacy_uloop" >&2
    exit 1
  fi
  assert_contains "$work_dir/output.txt" "uloop mock version"
}

test_powershell_latest_skips_prerelease_assets() {
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$LatestBetaVersion = "latest-beta"'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'if ($ReleaseChannel -eq "stable" -and $Release.prerelease) {'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'if ($ReleaseChannel -eq "beta" `'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'function Test-LegacyNpmUloopPath'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '"uninstall", "-g", "--prefix", $LegacyPrefix, "uloop-cli"'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$NpmArgs = @("uninstall", "-g", "uloop-cli")'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$LegacyCommandIsNpmShim = $LegacyCommandShadowsNative `'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$ReleaseChannel = if ($Version -eq $LatestBetaVersion) { "beta" } else { "stable" }'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'if (Test-LegacyNpmUloopPath -CommandPath $LegacyUloopBeforeInstallPath) {'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$LegacyUloopCommandDetectedBeforeInstall = $null -ne $LegacyUloopBeforeInstallCommand'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'if ($LegacyUloopCommandDetectedBeforeInstall -and -not $LegacyNpmRemovedBeforeInstall) {'
  assert_not_contains "$ROOT_DIR/scripts/install.ps1" "ULOOP_REMOVE_LEGACY"
  assert_not_contains "$ROOT_DIR/scripts/install.ps1" "Remove-LegacyUloopShims"
}

test_posix_latest_skips_prerelease_assets
test_posix_latest_beta_selects_prerelease_assets
test_posix_removes_npm_package_even_when_native_command_is_first
test_posix_does_not_infer_npm_prefix_from_non_npm_command
test_posix_removes_npm_package_before_replacing_same_bin_path
test_powershell_latest_skips_prerelease_assets
