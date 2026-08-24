#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
TMP_DIR=$(mktemp -d)
ORIGINAL_PATH=$PATH
ULOOP_ARCHIVE_MANIFEST='fakehash  uloop-dispatcher-darwin-arm64.tar.gz
fakehash  uloop-dispatcher-windows-amd64.zip'
export ULOOP_ARCHIVE_MANIFEST

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

assert_file_not_exists() {
  file=$1

  if [ -e "$file" ]; then
    echo "Expected $file not to exist" >&2
    exit 1
  fi
}

write_releases_json() {
  output_path=$1

  cat > "$output_path" <<'JSON'
[
  {
    "tag_name": "dispatcher-v3.0.0-beta.2",
    "draft": false,
    "prerelease": true,
    "assets": [
      {
        "name": "uloop-dispatcher-darwin-arm64.tar.gz",
        "browser_download_url": "https://github.com/hatayama/unity-cli-loop/releases/download/dispatcher-v3.0.0-beta.2/uloop-dispatcher-darwin-arm64.tar.gz"
      },
      {
        "name": "uloop-dispatcher-windows-amd64.zip",
        "browser_download_url": "https://github.com/hatayama/unity-cli-loop/releases/download/dispatcher-v3.0.0-beta.2/uloop-dispatcher-windows-amd64.zip"
      }
    ]
  },
  {
    "tag_name": "dispatcher-v2.0.0",
    "draft": false,
    "prerelease": false,
    "assets": [
      {
        "name": "uloop-dispatcher-darwin-arm64.tar.gz",
        "browser_download_url": "https://github.com/hatayama/unity-cli-loop/releases/download/dispatcher-v2.0.0/uloop-dispatcher-darwin-arm64.tar.gz"
      },
      {
        "name": "uloop-dispatcher-windows-amd64.zip",
        "browser_download_url": "https://github.com/hatayama/unity-cli-loop/releases/download/dispatcher-v2.0.0/uloop-dispatcher-windows-amd64.zip"
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
  */Packages/src/project-runner-pin.json)
    # Why: pin URL handling must sit after CURL_LOG recording so tests can
    # assert whether the pin fallback path was taken. Returning before the
    # log (like the releases API case) would make "pin URL absent" vacuous.
    if [ "${MOCK_PIN_FETCH_FAIL:-0}" = "1" ]; then
      exit 22
    fi
    cat "$PIN_JSON"
    exit 0
    ;;
  *dispatcher-v2.0.0/uloop-dispatcher-darwin-arm64.tar.gz)
    : > "$output_file"
    ;;
  *dispatcher-v2.0.0/uloop-dispatcher-darwin-arm64.tar.gz.sha256)
    printf '%s  uloop-dispatcher-darwin-arm64.tar.gz\n' "${MOCK_ASSET_DIGEST:-fakehash}" > "$output_file"
    ;;
  *dispatcher-v2.0.0/uloop-dispatcher-windows-amd64.zip)
    : > "$output_file"
    ;;
  *dispatcher-v2.0.0/uloop-dispatcher-windows-amd64.zip.sha256)
    printf '%s  uloop-dispatcher-windows-amd64.zip\n' "${MOCK_ASSET_DIGEST:-fakehash}" > "$output_file"
    ;;
  *dispatcher-v3.0.0-beta.2/uloop-dispatcher-darwin-arm64.tar.gz)
    if [ "${ULOOP_VERSION:-}" != "latest-beta" ]; then
      echo "Prerelease asset should not be downloaded: $url" >&2
      exit 1
    fi
    : > "$output_file"
    ;;
  *dispatcher-v3.0.0-beta.2/uloop-dispatcher-darwin-arm64.tar.gz.sha256)
    if [ "${ULOOP_VERSION:-}" != "latest-beta" ]; then
      echo "Prerelease checksum should not be downloaded: $url" >&2
      exit 1
    fi
    printf '%s  uloop-dispatcher-darwin-arm64.tar.gz\n' "${MOCK_ASSET_DIGEST:-fakehash}" > "$output_file"
    ;;
  *dispatcher-v3.0.0-beta.2/uloop-dispatcher-windows-amd64.zip)
    if [ "${ULOOP_VERSION:-}" != "latest-beta" ]; then
      echo "Prerelease asset should not be downloaded: $url" >&2
      exit 1
    fi
    : > "$output_file"
    ;;
  *dispatcher-v3.0.0-beta.2/uloop-dispatcher-windows-amd64.zip.sha256)
    if [ "${ULOOP_VERSION:-}" != "latest-beta" ]; then
      echo "Prerelease checksum should not be downloaded: $url" >&2
      exit 1
    fi
    printf '%s  uloop-dispatcher-windows-amd64.zip\n' "${MOCK_ASSET_DIGEST:-fakehash}" > "$output_file"
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

# install.sh now computes the digest with `sha256sum <path>` and compares
# against the parsed .sha256 file so the same hash string is available for
# both the same-origin check and the attestation-manifest cross-check. Default
# digest stays "fakehash" for existing fixtures; pin-fallback cases override
# MOCK_ASSET_DIGEST to a 64-digit hex that validate_pin_manifest accepts.
if [ "$#" -ge 1 ] && [ -f "$1" ]; then
  printf '%s  %s\n' "${MOCK_ASSET_DIGEST:-fakehash}" "$1"
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
set -eu

if [ "${1:-}" = "install" ]; then
  if [ "${MOCK_NATIVE_INSTALL_UNSUPPORTED:-0}" = "1" ]; then
    exit 1
  fi
  if [ "${2:-}" = "--help" ]; then
    if [ "${MOCK_NATIVE_INSTALL_HELP_ONLY:-0}" = "1" ]; then
      echo "mock install help"
      exit 0
    fi
    echo "mock install help"
    echo "On macOS, updates shell PATH and removes legacy npm uloop-cli launchers."
    exit 0
  fi
  if [ -n "${NATIVE_INSTALL_LOG:-}" ]; then
    printf '%s\n' "$*" >> "$NATIVE_INSTALL_LOG"
  fi
  exit "${MOCK_NATIVE_INSTALL_EXIT_CODE:-0}"
fi

echo "uloop mock version"
ULOOP
  chmod +x "$extract_dir/uloop"
MOCK_TAR

  cat > "$mock_bin/unzip" <<'MOCK_UNZIP'
#!/bin/sh
set -eu

extract_dir=
while [ "$#" -gt 0 ]; do
  case "$1" in
    -d)
      shift
      extract_dir=$1
      ;;
  esac
  shift
done

if [ -z "$extract_dir" ]; then
  echo "unzip extract directory is required" >&2
  exit 1
fi

cat > "$extract_dir/uloop.exe" <<'ULOOP'
#!/bin/sh
set -eu

if [ "${1:-}" = "install" ]; then
  if [ "${MOCK_NATIVE_INSTALL_UNSUPPORTED:-0}" = "1" ]; then
    exit 1
  fi
  if [ "${2:-}" = "--help" ]; then
    if [ "${MOCK_NATIVE_INSTALL_HELP_ONLY:-0}" = "1" ]; then
      echo "mock install help"
      exit 0
    fi
    echo "mock install help"
    echo "On Windows, updates User PATH and removes legacy npm uloop-cli launchers."
    exit 0
  fi
  if [ -n "${NATIVE_INSTALL_LOG:-}" ]; then
    printf '%s\n' "$*" >> "$NATIVE_INSTALL_LOG"
  fi
  exit "${MOCK_NATIVE_INSTALL_EXIT_CODE:-0}"
fi

echo "uloop mock version"
ULOOP
chmod +x "$extract_dir/uloop.exe"
MOCK_UNZIP

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

  chmod +x "$mock_bin/uname" "$mock_bin/curl" "$mock_bin/sha256sum" "$mock_bin/tar" "$mock_bin/unzip" "$mock_bin/npm"
}

# Builds a pretty-print pin JSON whose dispatcherArchiveManifest digests come
# from PIN_ASSET_DIGEST (or MOCK_ASSET_DIGEST). Tag is always dispatcher-v2.0.0
# so pin-driven installs hit the mocked stable asset URLs.
write_pin_json() {
  output_path=$1
  digest=${PIN_ASSET_DIGEST:-${MOCK_ASSET_DIGEST:-fakehash}}
  asset_name=${PIN_ASSET_NAME:-uloop-dispatcher-darwin-arm64.tar.gz}

  cat > "$output_path" <<PIN
{
  "dispatcherArchiveManifest": "${digest}  ${asset_name}\\n${digest}  install.sh",
  "dispatcherReleaseTag": "dispatcher-v2.0.0",
  "minimumDispatcherVersion": "3.0.0-beta.19",
  "projectRunnerVersion": "3.0.0-beta.64"
}
PIN
}

write_legacy_npm_uloop_shim() {
  legacy_uloop=$1
  target=$2

  if ln -s "$target" "$legacy_uloop" 2>/dev/null && [ -L "$legacy_uloop" ]; then
    return
  fi

  rm -f "$legacy_uloop"
  cat > "$legacy_uloop" <<'SHIM'
#!/bin/sh
# node_modules/uloop-cli shim marker
echo "legacy uloop"
SHIM
  chmod +x "$legacy_uloop"
}

write_required_tool_links() {
  tool_bin=$1
  mkdir -p "$tool_bin"

  for command_name in awk cat chmod grep install mkdir mktemp mv readlink rm sed; do
    command_path=$(command -v "$command_name")
    {
      printf '%s\n' '#!/bin/sh'
      printf 'exec %s "$@"\n' "$command_path"
    } > "$tool_bin/$command_name"
    chmod +x "$tool_bin/$command_name"
  done
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
  write_legacy_npm_uloop_shim "$legacy_uloop" "../lib/node_modules/uloop-cli/dist/cli.bundle.cjs"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$legacy_bin:$mock_bin:$ORIGINAL_PATH" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="$legacy_uloop" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$curl_log" "dispatcher-v2.0.0/uloop-dispatcher-darwin-arm64.tar.gz"
  assert_contains "$curl_log" "dispatcher-v2.0.0/uloop-dispatcher-darwin-arm64.tar.gz.sha256"
  assert_not_contains "$curl_log" "dispatcher-v3.0.0-beta.2"
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
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$curl_log" "dispatcher-v3.0.0-beta.2/uloop-dispatcher-darwin-arm64.tar.gz"
  assert_contains "$curl_log" "dispatcher-v3.0.0-beta.2/uloop-dispatcher-darwin-arm64.tar.gz.sha256"
  assert_not_contains "$curl_log" "dispatcher-v2.0.0/uloop-dispatcher-darwin-arm64.tar.gz"
  assert_not_contains "$npm_log" "uninstall -g uloop-cli"
}

test_posix_invokes_native_install_setup() {
  work_dir="$TMP_DIR/posix-native-install"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  native_install_log="$work_dir/native-install.log"
  mkdir -p "$work_dir"
  : > "$curl_log"
  : > "$npm_log"
  : > "$native_install_log"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    NATIVE_INSTALL_LOG="$native_install_log" \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$native_install_log" "install --dir $install_dir"
  assert_contains "$work_dir/output.txt" "uloop mock version"
}

test_posix_help_only_native_probe_uses_fallback() {
  work_dir="$TMP_DIR/posix-help-only-native-probe"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  home_dir="$work_dir/home"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  native_install_log="$work_dir/native-install.log"
  mkdir -p "$work_dir" "$home_dir"
  : > "$curl_log"
  : > "$npm_log"
  : > "$native_install_log"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    HOME="$home_dir" \
    SHELL="/bin/zsh" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    NATIVE_INSTALL_LOG="$native_install_log" \
    MOCK_NATIVE_INSTALL_HELP_ONLY=1 \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_not_contains "$native_install_log" "install --dir $install_dir"
  assert_contains "$work_dir/output.txt" "Installed uloop to $install_dir, but that directory is not in PATH."
  assert_contains "$work_dir/output.txt" "uloop mock version"
}

test_posix_native_failure_uses_fallback() {
  work_dir="$TMP_DIR/posix-native-install-failure"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  home_dir="$work_dir/home"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  native_install_log="$work_dir/native-install.log"
  mkdir -p "$work_dir" "$home_dir"
  : > "$curl_log"
  : > "$npm_log"
  : > "$native_install_log"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    HOME="$home_dir" \
    SHELL="/bin/zsh" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    NATIVE_INSTALL_LOG="$native_install_log" \
    MOCK_NATIVE_INSTALL_EXIT_CODE=7 \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$native_install_log" "install --dir $install_dir"
  assert_contains "$work_dir/stderr.txt" "Native install setup failed."
  assert_contains "$work_dir/output.txt" "Installed uloop to $install_dir, but that directory is not in PATH."
  assert_contains "$work_dir/output.txt" "uloop mock version"
}

test_posix_native_path_cleans_preinstall_legacy_shim() {
  work_dir="$TMP_DIR/posix-native-preinstall-legacy"
  mock_bin="$work_dir/bin"
  legacy_bin="$work_dir/npm-global/bin"
  legacy_package_dist="$work_dir/npm-global/lib/node_modules/uloop-cli/dist"
  install_dir="$work_dir/install"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  native_install_log="$work_dir/native-install.log"
  legacy_uloop="$legacy_bin/uloop"
  mkdir -p "$work_dir" "$legacy_bin" "$legacy_package_dist" "$install_dir"
  : > "$curl_log"
  : > "$npm_log"
  : > "$native_install_log"
  printf '%s\n' 'legacy node cli bundle' > "$legacy_package_dist/cli.bundle.cjs"
  chmod +x "$legacy_package_dist/cli.bundle.cjs"
  write_legacy_npm_uloop_shim "$legacy_uloop" "../lib/node_modules/uloop-cli/dist/cli.bundle.cjs"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$install_dir:$legacy_bin:$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    NATIVE_INSTALL_LOG="$native_install_log" \
    LEGACY_ULOOP="$legacy_uloop" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$native_install_log" "install --dir $install_dir"
  assert_contains "$npm_log" "uninstall -g --prefix $work_dir/npm-global uloop-cli"
  if [ -e "$legacy_uloop" ]; then
    echo "Expected pre-install legacy npm shim to be removed: $legacy_uloop" >&2
    exit 1
  fi
}

test_posix_prints_zsh_path_guidance_without_writing_profile() {
  work_dir="$TMP_DIR/posix-zsh-path-guidance"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  home_dir="$work_dir/home"
  zdot_dir="$work_dir/zdot"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  profile_path="$zdot_dir/.zshrc"
  mkdir -p "$work_dir" "$home_dir" "$zdot_dir"
  : > "$curl_log"
  : > "$npm_log"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    HOME="$home_dir" \
    ZDOTDIR="$zdot_dir" \
    SHELL="/bin/zsh" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$work_dir/output.txt" "Detected shell: zsh"
  assert_contains "$work_dir/output.txt" "Add this line to $profile_path:"
  assert_contains "$work_dir/output.txt" "  export PATH=\"$install_dir:\$PATH\""
  assert_contains "$work_dir/output.txt" "printf '\\n%s\\n' 'export PATH=\"$install_dir:\$PATH\"' >> '$profile_path'"
  assert_file_not_exists "$profile_path"
}

test_posix_prints_bash_path_guidance_without_modifying_existing_profile() {
  work_dir="$TMP_DIR/posix-bash-path-guidance"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  home_dir="$work_dir/home"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  profile_path="$home_dir/.bash_login"
  mkdir -p "$work_dir" "$home_dir"
  : > "$curl_log"
  : > "$npm_log"
  printf '%s\n' "existing bash profile" > "$profile_path"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    HOME="$home_dir" \
    SHELL="/bin/bash" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$work_dir/output.txt" "Detected shell: bash"
  assert_contains "$work_dir/output.txt" "Add this line to $profile_path:"
  assert_contains "$work_dir/output.txt" "  export PATH=\"$install_dir:\$PATH\""
  if [ "$(cat "$profile_path")" != "existing bash profile" ]; then
    echo "Expected bash profile to remain unchanged: $profile_path" >&2
    exit 1
  fi
}

test_posix_prints_fish_path_guidance_without_writing_profile() {
  work_dir="$TMP_DIR/posix-fish-path-guidance"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  home_dir="$work_dir/home"
  xdg_config_home="$work_dir/xdg"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  profile_path="$xdg_config_home/fish/config.fish"
  mkdir -p "$work_dir" "$home_dir"
  : > "$curl_log"
  : > "$npm_log"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    HOME="$home_dir" \
    XDG_CONFIG_HOME="$xdg_config_home" \
    SHELL="/opt/homebrew/bin/fish" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$work_dir/output.txt" "Detected shell: fish"
  assert_contains "$work_dir/output.txt" "Add this line to $profile_path:"
  assert_contains "$work_dir/output.txt" "  fish_add_path --move \"$install_dir\""
  assert_contains "$work_dir/output.txt" "mkdir -p '$xdg_config_home/fish' && printf '\\n%s\\n' 'fish_add_path --move \"$install_dir\"' >> '$profile_path'"
  assert_file_not_exists "$profile_path"
}

test_posix_prints_generic_path_guidance_for_unknown_shell() {
  work_dir="$TMP_DIR/posix-generic-path-guidance"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  home_dir="$work_dir/home"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  mkdir -p "$work_dir" "$home_dir"
  : > "$curl_log"
  : > "$npm_log"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    HOME="$home_dir" \
    SHELL="/bin/ksh" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$work_dir/output.txt" "Add this directory to PATH in your shell profile:"
  assert_contains "$work_dir/output.txt" "  $install_dir"
  assert_not_contains "$work_dir/output.txt" "Detected shell:"
  assert_file_not_exists "$home_dir/.kshrc"
}

test_posix_skips_default_npm_cleanup_when_native_command_is_first() {
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
  write_legacy_npm_uloop_shim "$legacy_uloop" "../lib/node_modules/uloop-cli/dist/cli.bundle.cjs"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$install_dir:$legacy_bin:$mock_bin:$ORIGINAL_PATH" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="$legacy_uloop" \
    DEFAULT_LEGACY_ULOOP="$legacy_uloop" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_not_contains "$npm_log" "uninstall -g uloop-cli"
  assert_not_contains "$npm_log" "uninstall -g --prefix $install_dir uloop-cli"
  if [ ! -e "$legacy_uloop" ]; then
    echo "Expected hidden npm shim to remain when the detected command is native: $legacy_uloop" >&2
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
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_not_contains "$npm_log" "uninstall -g uloop-cli"
  assert_not_contains "$npm_log" "--prefix $non_npm_prefix"
  if [ ! -x "$non_npm_uloop" ]; then
    echo "Expected non-npm uloop command to remain untouched: $non_npm_uloop" >&2
    exit 1
  fi
}

test_posix_silences_legacy_cleanup_when_npm_is_unavailable() {
  work_dir="$TMP_DIR/posix-no-npm"
  mock_bin="$work_dir/bin"
  tool_bin="$work_dir/tools"
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
  write_legacy_npm_uloop_shim "$legacy_uloop" "../lib/node_modules/uloop-cli/dist/cli.bundle.cjs"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"
  rm -f "$mock_bin/npm"
  write_required_tool_links "$tool_bin"

  PATH="$legacy_bin:$mock_bin:$tool_bin" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="$legacy_uloop" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_not_contains "$work_dir/output.txt" "Could not remove the legacy npm package automatically."
  assert_not_contains "$work_dir/output.txt" "npm uninstall -g --prefix \"$work_dir/npm-global\" uloop-cli"
  assert_not_contains "$work_dir/output.txt" "Run this manually if the old npm command still shadows the native CLI:"
}

test_posix_silences_legacy_cleanup_when_npm_prefix_cannot_be_inferred() {
  work_dir="$TMP_DIR/posix-unknown-npm-prefix"
  mock_bin="$work_dir/bin"
  legacy_bin="$work_dir/custom-shims"
  install_dir="$work_dir/install"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  legacy_uloop="$legacy_bin/uloop"
  mkdir -p "$work_dir" "$legacy_bin"
  : > "$curl_log"
  : > "$npm_log"
  printf '%s\n' '#!/bin/sh' '# node_modules/uloop-cli custom shim' 'echo legacy uloop' > "$legacy_uloop"
  chmod +x "$legacy_uloop"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$legacy_bin:$mock_bin:$ORIGINAL_PATH" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="$legacy_uloop" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_not_contains "$npm_log" "uninstall -g uloop-cli"
  assert_not_contains "$npm_log" "uninstall -g --prefix"
  assert_not_contains "$work_dir/output.txt" "Could not remove the legacy npm package automatically."
  assert_not_contains "$work_dir/output.txt" "Legacy uloop command: $legacy_uloop"
  assert_not_contains "$work_dir/output.txt" "Run this manually if the old npm command still shadows the native CLI:"
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
  write_legacy_npm_uloop_shim "$legacy_uloop" "../lib/node_modules/uloop-cli/dist/cli.bundle.cjs"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$legacy_bin:$mock_bin:$ORIGINAL_PATH" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$legacy_bin" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
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
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$ReleaseChannel = if ($Version -eq $LatestBetaVersion) { "beta" } else { "stable" }'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'function Invoke-UloopNativeInstall'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'function Test-UloopNativeInstallSupported'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'function Set-CurrentPathWithInstallDirectoryFirst'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'function Invoke-CompatibilityWindowsInstall'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$NativeInstallArgs = @("install", "--dir", $Directory)'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$NativeInstallSupported = Test-UloopNativeInstallSupported -UloopPath $StagedUloopPath'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'if ($NativeInstallSupported) {'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'Invoke-UloopNativeInstall -UloopPath $FinalUloopPath -Directory $InstallDir'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'Set-CurrentPathWithInstallDirectoryFirst -Directory $InstallDir'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'Invoke-CompatibilityWindowsInstall -Directory $InstallDir -ExpectedUloopPath $FinalUloopPath'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'function Resolve-UloopManifestFromPin'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'function Test-UloopPinManifestFormat'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'raw.githubusercontent.com/$Repository/$PinRef/Packages/src/project-runner-pin.json'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$DefaultPinRef = "main"'
  assert_not_contains "$ROOT_DIR/scripts/install.ps1" "ULOOP_REMOVE_LEGACY"
}

test_git_bash_latest_installs_windows_zip_asset() {
  work_dir="$TMP_DIR/git-bash-latest"
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

  PATH="$mock_bin:$ORIGINAL_PATH" \
    MOCK_UNAME_OS=MINGW64_NT-10.0-26100 \
    MOCK_UNAME_ARCH=x86_64 \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$curl_log" "dispatcher-v2.0.0/uloop-dispatcher-windows-amd64.zip"
  assert_contains "$curl_log" "dispatcher-v2.0.0/uloop-dispatcher-windows-amd64.zip.sha256"
  assert_not_contains "$curl_log" "uloop-dispatcher-darwin-arm64.tar.gz"
  if [ ! -x "$install_dir/uloop.exe" ]; then
    echo "Expected Git Bash install to create executable uloop.exe: $install_dir/uloop.exe" >&2
    exit 1
  fi
  assert_contains "$work_dir/output.txt" "uloop mock version"
}

# Verifies -UseBasicParsing on the two payload downloads, that the TLS 1.2
# -bor assignment sits inside the PS 5.1 Major -lt 6 guard, and that that
# assignment appears before the first Invoke-WebRequest in install.ps1.
test_powershell_installer_enables_tls12_and_basic_parsing() {
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'Invoke-WebRequest -UseBasicParsing -Uri $DownloadUrl -OutFile $ArchivePath'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'Invoke-WebRequest -UseBasicParsing -Uri $ChecksumUrl -OutFile $ChecksumPath'
  assert_not_contains "$ROOT_DIR/scripts/install.ps1" 'Invoke-WebRequest -Uri'

  tls_assignment='[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12'
  tls_guard_block=$(awk '
    index($0, "if ($PSVersionTable.PSVersion.Major -lt 6) {") {
      capture = 1
    }
    capture {
      print
    }
    capture && index($0, "}") {
      exit
    }
  ' "$ROOT_DIR/scripts/install.ps1")
  if [ -z "$tls_guard_block" ]; then
    echo "Expected a PS 5.1 Major -lt 6 guard in install.ps1" >&2
    exit 1
  fi
  printf '%s\n' "$tls_guard_block" > "$TMP_DIR/tls-guard.ps1"
  assert_contains "$TMP_DIR/tls-guard.ps1" "$tls_assignment"

  tls_line=$(grep -n -F "$tls_assignment" "$ROOT_DIR/scripts/install.ps1" | head -n 1 | cut -d: -f1)
  first_web_request_line=$(grep -n -F 'Invoke-WebRequest' "$ROOT_DIR/scripts/install.ps1" | head -n 1 | cut -d: -f1)
  if [ -z "$tls_line" ] || [ -z "$first_web_request_line" ]; then
    echo "Expected TLS 1.2 assignment and Invoke-WebRequest line numbers in install.ps1" >&2
    exit 1
  fi
  if [ "$tls_line" -ge "$first_web_request_line" ]; then
    echo "Expected TLS 1.2 assignment at line $tls_line to precede first Invoke-WebRequest at line $first_web_request_line" >&2
    exit 1
  fi
}

test_powershell_installer_avoids_optional_archive_cmdlets() {
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'function Get-UloopSha256Hash'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '[System.Security.Cryptography.SHA256]::Create()'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'function Expand-UloopArchive'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '[System.IO.Compression.ZipFile]::ExtractToDirectory($ArchivePath, $DestinationPath)'
  assert_not_contains "$ROOT_DIR/scripts/install.ps1" "Get-FileHash"
  assert_not_contains "$ROOT_DIR/scripts/install.ps1" "Expand-Archive"
}

test_powershell_installer_uses_non_installer_staged_executable_name() {
  assert_contains "$ROOT_DIR/scripts/install.ps1" '"uloop-staged-"'
  assert_not_contains "$ROOT_DIR/scripts/install.ps1" '"uloop-install-"'
}

test_powershell_native_probe_restores_error_action_preference() {
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$PreviousErrorActionPreference = $ErrorActionPreference'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$ErrorActionPreference = "Continue"'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$ErrorActionPreference = $PreviousErrorActionPreference'
}

test_powershell_reports_persisted_path_shadowing() {
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'function Get-FirstUloopCommandFromPath'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$NormalizedPathEntry = ConvertTo-NormalizedPath -Path $PathEntry'
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'if ($NormalizedPathEntry -match "^[A-Za-z]:$")'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$NormalizedPathEntry = $NormalizedPathEntry + "\"'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$CandidatePath = Join-Path $NormalizedPathEntry $ShimName'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '[Environment]::GetEnvironmentVariable("Path", "Machine")'
  assert_contains "$ROOT_DIR/scripts/install.ps1" '$ResolvedPath = Get-FirstUloopCommandFromPath -PathValue ([string]::Join(";", @($MachinePath, $UserPath)))'
  assert_not_contains "$ROOT_DIR/scripts/install.ps1" 'Get-Command uloop -ErrorAction SilentlyContinue'
  assert_not_contains "$ROOT_DIR/scripts/install.ps1" '$RemovedAll'
}

PIN_TEST_DIGEST="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
PIN_MISMATCH_DIGEST="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"

# (a) Explicit ULOOP_ARCHIVE_MANIFEST must not fetch the repository pin.
test_posix_explicit_manifest_skips_pin_fetch() {
  work_dir="$TMP_DIR/posix-explicit-manifest-skips-pin"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  releases_json="$work_dir/releases.json"
  pin_json="$work_dir/pin.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  mkdir -p "$work_dir"
  : > "$curl_log"
  : > "$npm_log"
  write_releases_json "$releases_json"
  write_pin_json "$pin_json"
  write_mock_commands "$mock_bin"

  PATH="$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    PIN_JSON="$pin_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_not_contains "$curl_log" "Packages/src/project-runner-pin.json"
}

# (b) Empty env + pin → install succeeds via pin tag and pin URL.
test_posix_pin_fallback_installs_pinned_release() {
  work_dir="$TMP_DIR/posix-pin-fallback-success"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  releases_json="$work_dir/releases.json"
  pin_json="$work_dir/pin.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  mkdir -p "$work_dir"
  : > "$curl_log"
  : > "$npm_log"
  write_releases_json "$releases_json"
  MOCK_ASSET_DIGEST="$PIN_TEST_DIGEST" write_pin_json "$pin_json"
  write_mock_commands "$mock_bin"

  set +e
  PATH="$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    env -u ULOOP_ARCHIVE_MANIFEST \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    PIN_JSON="$pin_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_ASSET_DIGEST="$PIN_TEST_DIGEST" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"
  status=$?
  set -e
  if [ "$status" -ne 0 ]; then
    echo "Expected pin-fallback install to succeed" >&2
    cat "$work_dir/stderr.txt" >&2
    exit 1
  fi

  assert_contains "$curl_log" "Packages/src/project-runner-pin.json"
  assert_contains "$curl_log" "dispatcher-v2.0.0/uloop-dispatcher-darwin-arm64.tar.gz"
  assert_contains "$work_dir/output.txt" "pinned at"
}

# (c) Pin fetch failure must mention ULOOP_ARCHIVE_MANIFEST.
test_posix_pin_fetch_failure_mentions_manifest_env() {
  work_dir="$TMP_DIR/posix-pin-fetch-fail"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  releases_json="$work_dir/releases.json"
  pin_json="$work_dir/pin.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  mkdir -p "$work_dir"
  : > "$curl_log"
  : > "$npm_log"
  write_releases_json "$releases_json"
  MOCK_ASSET_DIGEST="$PIN_TEST_DIGEST" write_pin_json "$pin_json"
  write_mock_commands "$mock_bin"

  set +e
  PATH="$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    env -u ULOOP_ARCHIVE_MANIFEST \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    PIN_JSON="$pin_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_PIN_FETCH_FAIL=1 \
    MOCK_ASSET_DIGEST="$PIN_TEST_DIGEST" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"
  status=$?
  set -e
  if [ "$status" -eq 0 ]; then
    echo "Expected pin fetch failure to abort install" >&2
    exit 1
  fi
  assert_contains "$work_dir/stderr.txt" "ULOOP_ARCHIVE_MANIFEST"
}

# (d) Pin digest that does not match the computed archive hash must fail.
test_posix_pin_digest_mismatch_fails() {
  work_dir="$TMP_DIR/posix-pin-digest-mismatch"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  releases_json="$work_dir/releases.json"
  pin_json="$work_dir/pin.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  mkdir -p "$work_dir"
  : > "$curl_log"
  : > "$npm_log"
  write_releases_json "$releases_json"
  PIN_ASSET_DIGEST="$PIN_MISMATCH_DIGEST" MOCK_ASSET_DIGEST="$PIN_TEST_DIGEST" write_pin_json "$pin_json"
  write_mock_commands "$mock_bin"

  set +e
  PATH="$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    env -u ULOOP_ARCHIVE_MANIFEST \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    PIN_JSON="$pin_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_ASSET_DIGEST="$PIN_TEST_DIGEST" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"
  status=$?
  set -e
  if [ "$status" -eq 0 ]; then
    echo "Expected pin digest mismatch to abort install" >&2
    exit 1
  fi
}

# (e) Explicit ULOOP_VERSION that disagrees with the pin tag must fail.
test_posix_pin_version_mismatch_fails() {
  work_dir="$TMP_DIR/posix-pin-version-mismatch"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  releases_json="$work_dir/releases.json"
  pin_json="$work_dir/pin.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  mkdir -p "$work_dir"
  : > "$curl_log"
  : > "$npm_log"
  write_releases_json "$releases_json"
  MOCK_ASSET_DIGEST="$PIN_TEST_DIGEST" write_pin_json "$pin_json"
  write_mock_commands "$mock_bin"

  set +e
  PATH="$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    env -u ULOOP_ARCHIVE_MANIFEST \
    ULOOP_VERSION=dispatcher-v9.9.9 \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    PIN_JSON="$pin_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_ASSET_DIGEST="$PIN_TEST_DIGEST" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"
  status=$?
  set -e
  if [ "$status" -eq 0 ]; then
    echo "Expected pin tag mismatch to abort install" >&2
    exit 1
  fi
  assert_contains "$work_dir/stderr.txt" "ULOOP_VERSION"
  assert_contains "$work_dir/stderr.txt" "ULOOP_ARCHIVE_MANIFEST"
}

# (f) ULOOP_REF must appear in the pin raw URL.
test_posix_uloop_ref_appears_in_pin_url() {
  work_dir="$TMP_DIR/posix-uloop-ref-in-pin-url"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  releases_json="$work_dir/releases.json"
  pin_json="$work_dir/pin.json"
  curl_log="$work_dir/curl.log"
  npm_log="$work_dir/npm.log"
  mkdir -p "$work_dir"
  : > "$curl_log"
  : > "$npm_log"
  write_releases_json "$releases_json"
  MOCK_ASSET_DIGEST="$PIN_TEST_DIGEST" write_pin_json "$pin_json"
  write_mock_commands "$mock_bin"

  set +e
  PATH="$mock_bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    env -u ULOOP_ARCHIVE_MANIFEST \
    ULOOP_VERSION=latest \
    ULOOP_REF=custom-ref \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    PIN_JSON="$pin_json" \
    CURL_LOG="$curl_log" \
    NPM_LOG="$npm_log" \
    MOCK_ASSET_DIGEST="$PIN_TEST_DIGEST" \
    MOCK_NATIVE_INSTALL_UNSUPPORTED=1 \
    LEGACY_ULOOP="" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"
  status=$?
  set -e
  if [ "$status" -ne 0 ]; then
    echo "Expected custom-ref pin-fallback install to succeed" >&2
    cat "$work_dir/stderr.txt" >&2
    exit 1
  fi
  assert_contains "$curl_log" "raw.githubusercontent.com/hatayama/unity-cli-loop/custom-ref/Packages/src/project-runner-pin.json"
}

test_posix_latest_skips_prerelease_assets
test_posix_latest_beta_selects_prerelease_assets
test_posix_invokes_native_install_setup
test_posix_help_only_native_probe_uses_fallback
test_posix_native_failure_uses_fallback
test_posix_native_path_cleans_preinstall_legacy_shim
test_posix_prints_zsh_path_guidance_without_writing_profile
test_posix_prints_bash_path_guidance_without_modifying_existing_profile
test_posix_prints_fish_path_guidance_without_writing_profile
test_posix_prints_generic_path_guidance_for_unknown_shell
test_posix_skips_default_npm_cleanup_when_native_command_is_first
test_posix_does_not_infer_npm_prefix_from_non_npm_command
test_posix_silences_legacy_cleanup_when_npm_is_unavailable
test_posix_silences_legacy_cleanup_when_npm_prefix_cannot_be_inferred
test_posix_removes_npm_package_before_replacing_same_bin_path
test_powershell_latest_skips_prerelease_assets
test_git_bash_latest_installs_windows_zip_asset
test_powershell_installer_enables_tls12_and_basic_parsing
test_powershell_installer_avoids_optional_archive_cmdlets
test_powershell_installer_uses_non_installer_staged_executable_name
test_powershell_native_probe_restores_error_action_preference
test_powershell_reports_persisted_path_shadowing
test_posix_explicit_manifest_skips_pin_fetch
test_posix_pin_fallback_installs_pinned_release
test_posix_pin_fetch_failure_mentions_manifest_env
test_posix_pin_digest_mismatch_fails
test_posix_pin_version_mismatch_fails
test_posix_uloop_ref_appears_in_pin_url
