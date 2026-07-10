#!/bin/sh
# Verifies install.sh's validate_uloop_version helper accepts every version
# shape the dispatcher self-update flow can emit (latest, latest-beta, bare
# semver, dispatcher-v prefix, project-runner prefix, prerelease + build
# metadata) and rejects values that could break out of the release path.
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

LATEST_VERSION="latest"
LATEST_BETA_VERSION="latest-beta"
eval "$(extract_function validate_uloop_version)"

expect_pass() {
  value=$1
  if ! (validate_uloop_version "$value") >/dev/null 2>&1; then
    echo "FAIL: expected pass for '$value'" >&2
    exit 1
  fi
}

expect_fail() {
  value=$1
  if (validate_uloop_version "$value") >/dev/null 2>&1; then
    echo "FAIL: expected reject for '$value'" >&2
    exit 1
  fi
}

# Well-known channel selectors — must always pass.
expect_pass "latest"
expect_pass "latest-beta"

# Bare semver as emitted by --to-version after NormalizeTargetVersion.
expect_pass "3.0.0"
expect_pass "3.0.0-beta.5"
expect_pass "3.0.1-beta.12"
expect_pass "1.2.3-rc.1+build.7"

# Release-tag prefixed values as emitted by DispatcherReleaseTag /
# ProjectRunnerReleaseTag / bare 'v' prefix.
expect_pass "dispatcher-v3.0.0"
expect_pass "dispatcher-v3.0.0-beta.5"
expect_pass "uloop-project-runner-v3.0.0-beta.43"
expect_pass "v3.0.0"

# Path traversal or URL-injection shapes must not survive.
expect_fail "../../evil/repo/releases/download/v1"
expect_fail "3.0.0/../evil"
expect_fail "3.0.0?redirect=evil"
expect_fail "3.0.0#fragment"
expect_fail "3.0.0 3.0.0"
expect_fail ""
expect_fail "not-a-version"
expect_fail "3.0"

# Embedded newline: grep -Eq alone matches per-line, so "evil\n3.0.0" would
# smuggle a URL-poisoning first line past the ERE. The whole-string `case`
# guard in front of the grep must catch this.
expect_fail "$(printf 'evil\n3.0.0')"
expect_fail "$(printf '3.0.0\n../evil')"

echo "OK"
