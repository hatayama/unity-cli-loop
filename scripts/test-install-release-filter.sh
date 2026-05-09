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

  if ! grep -F "$expected" "$file" >/dev/null; then
    echo "Expected $file to contain: $expected" >&2
    echo "Actual content:" >&2
    cat "$file" >&2
    exit 1
  fi
}

assert_not_contains() {
  file=$1
  unexpected=$2

  if grep -F "$unexpected" "$file" >/dev/null; then
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
  *v3.0.0-beta.2*)
    echo "Prerelease asset should not be downloaded: $url" >&2
    exit 1
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

  chmod +x "$mock_bin/uname" "$mock_bin/curl" "$mock_bin/sha256sum" "$mock_bin/tar"
}

test_posix_latest_skips_prerelease_assets() {
  work_dir="$TMP_DIR/posix-latest"
  mock_bin="$work_dir/bin"
  install_dir="$work_dir/install"
  releases_json="$work_dir/releases.json"
  curl_log="$work_dir/curl.log"
  mkdir -p "$work_dir"
  : > "$curl_log"
  write_releases_json "$releases_json"
  write_mock_commands "$mock_bin"

  PATH="$mock_bin:$ORIGINAL_PATH" \
    ULOOP_VERSION=latest \
    ULOOP_INSTALL_DIR="$install_dir" \
    RELEASES_JSON="$releases_json" \
    CURL_LOG="$curl_log" \
    "$ROOT_DIR/scripts/install.sh" > "$work_dir/output.txt" 2> "$work_dir/stderr.txt"

  assert_contains "$curl_log" "v2.0.0/uloop-darwin-arm64.tar.gz"
  assert_contains "$curl_log" "v2.0.0/uloop-darwin-arm64.tar.gz.sha256"
  assert_not_contains "$curl_log" "v3.0.0-beta.2"
}

test_powershell_latest_skips_prerelease_assets() {
  assert_contains "$ROOT_DIR/scripts/install.ps1" 'if ($Release.draft -or $Release.prerelease) {'
}

test_posix_latest_skips_prerelease_assets
test_powershell_latest_skips_prerelease_assets
