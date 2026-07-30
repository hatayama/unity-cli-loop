#!/bin/sh
# Verifies install.sh pin-manifest helpers: field extraction, \n-only unescape,
# fail-closed manifest validation, and ULOOP_REF format checks. Extracts the
# real functions from install.sh so a later refactor cannot silently drift.
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
INSTALL_SCRIPT="$ROOT_DIR/scripts/install.sh"

if ! command -v awk >/dev/null 2>&1; then
  echo "awk is required for this test" >&2
  exit 1
fi

extract_function() {
  name=$1
  awk -v want="$name" '
    $0 ~ ("^" want "\\(\\)") { in_fn = 1 }
    in_fn { print }
    in_fn && $0 == "}" { exit }
  ' "$INSTALL_SCRIPT"
}

expect_pass() {
  label=$1
  shift
  if ! ("$@") 2>/dev/null; then
    echo "FAIL: $label — expected pass but got failure" >&2
    exit 1
  fi
}

expect_fail() {
  label=$1
  shift
  if ("$@") 2>/dev/null; then
    echo "FAIL: $label — expected failure but got pass" >&2
    exit 1
  fi
}

eval "$(extract_function extract_pin_string_field)"
eval "$(extract_function unescape_pin_manifest)"
eval "$(extract_function validate_pin_manifest)"
eval "$(extract_function validate_uloop_ref)"

HEX64_LOWER="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
HEX64_UPPER="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
HEX63="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"

# Pretty-print fixture matches the real pin shape: each key on its own line.
# \\n in the unquoted heredoc becomes the two-character JSON escape \n.
pin_json=$(cat <<PIN
{
  "dispatcherArchiveManifest": "${HEX64_LOWER}  uloop-dispatcher-darwin-arm64.tar.gz\\n${HEX64_LOWER}  install.sh",
  "dispatcherReleaseTag": "dispatcher-v3.1.0-beta.16",
  "minimumDispatcherVersion": "3.0.0-beta.19",
  "projectRunnerVersion": "3.0.0-beta.64"
}
PIN
)

tag=$(extract_pin_string_field dispatcherReleaseTag) || {
  echo "FAIL: extract dispatcherReleaseTag from fixture" >&2
  exit 1
}
if [ "$tag" != "dispatcher-v3.1.0-beta.16" ]; then
  echo "FAIL: unexpected dispatcherReleaseTag: $tag" >&2
  exit 1
fi

raw=$(extract_pin_string_field dispatcherArchiveManifest) || {
  echo "FAIL: extract dispatcherArchiveManifest from fixture" >&2
  exit 1
}
expected_raw="${HEX64_LOWER}  uloop-dispatcher-darwin-arm64.tar.gz\\n${HEX64_LOWER}  install.sh"
if [ "$raw" != "$expected_raw" ]; then
  echo "FAIL: unexpected raw dispatcherArchiveManifest: $raw" >&2
  exit 1
fi

# Missing keys must fail closed.
pin_json=$(cat <<PIN
{
  "dispatcherReleaseTag": "dispatcher-v3.1.0-beta.16",
  "minimumDispatcherVersion": "3.0.0-beta.19",
  "projectRunnerVersion": "3.0.0-beta.64"
}
PIN
)
expect_fail "missing dispatcherArchiveManifest" extract_pin_string_field dispatcherArchiveManifest

pin_json=$(cat <<PIN
{
  "dispatcherArchiveManifest": "${HEX64_LOWER}  install.sh",
  "minimumDispatcherVersion": "3.0.0-beta.19",
  "projectRunnerVersion": "3.0.0-beta.64"
}
PIN
)
expect_fail "missing dispatcherReleaseTag" extract_pin_string_field dispatcherReleaseTag

# \n expands to real newlines; line count and contents must match.
unescaped=$(unescape_pin_manifest "$expected_raw") || {
  echo "FAIL: unescape of \\n-only value" >&2
  exit 1
}
line_count=$(printf '%s\n' "$unescaped" | awk 'END { print NR }')
if [ "$line_count" != "2" ]; then
  echo "FAIL: expected 2 unescaped lines, got $line_count" >&2
  exit 1
fi
assert_line=$(printf '%s\n' "$unescaped" | awk 'NR==1 { print }')
if [ "$assert_line" != "${HEX64_LOWER}  uloop-dispatcher-darwin-arm64.tar.gz" ]; then
  echo "FAIL: unexpected first unescaped line: $assert_line" >&2
  exit 1
fi
assert_line=$(printf '%s\n' "$unescaped" | awk 'NR==2 { print }')
if [ "$assert_line" != "${HEX64_LOWER}  install.sh" ]; then
  echo "FAIL: unexpected second unescaped line: $assert_line" >&2
  exit 1
fi

# Fail-closed on unexpected escapes.
expect_fail "reject CR escape" unescape_pin_manifest "${HEX64_LOWER}  install.sh\\r"
expect_fail "reject quote escape" unescape_pin_manifest "${HEX64_LOWER}  \\\"evil\\\""
expect_fail "reject backslash escape" unescape_pin_manifest "${HEX64_LOWER}  install\\\\.sh"

# Manifest validation: length/spacing fail; uppercase hex passes (C# symmetry).
expect_fail "reject 63-digit digest" validate_pin_manifest "${HEX63}  install.sh"
expect_fail "reject single-space separator" validate_pin_manifest "${HEX64_LOWER} install.sh"
expect_pass "accept uppercase hex digest" validate_pin_manifest "${HEX64_UPPER}  install.sh"
expect_fail "reject duplicate filenames" validate_pin_manifest "${HEX64_LOWER}  install.sh
${HEX64_UPPER}  install.sh"
expect_fail "reject empty manifest" validate_pin_manifest ""
expect_fail "reject CR in expanded manifest" validate_pin_manifest "${HEX64_LOWER}  install.sh$(printf '\r')"

# Ref validation takes the candidate as $1 (same interface as validate_uloop_version).
expect_pass "accept v3-beta ref" validate_uloop_ref "v3-beta"
expect_pass "accept main ref" validate_uloop_ref "main"
expect_pass "accept release/1.0 ref" validate_uloop_ref "release/1.0"
expect_fail "reject empty ref" validate_uloop_ref ""
expect_fail "reject path traversal ref" validate_uloop_ref "../evil"
expect_fail "reject space in ref" validate_uloop_ref "a b"

echo "OK"
