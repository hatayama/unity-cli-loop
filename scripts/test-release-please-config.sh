#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
CONFIG="$ROOT_DIR/release-please-config.json"
MANIFEST="$ROOT_DIR/.release-please-manifest.json"

assert_json_value() {
  expression=$1
  expected=$2
  actual=$(jq -r "$expression" "$CONFIG")

  if [ "$actual" != "$expected" ]; then
    echo "Expected $expression to be $expected, got $actual." >&2
    exit 1
  fi
}

assert_manifest_semver() {
  expression=$1
  actual=$(jq -r "$expression // empty" "$MANIFEST")

  if [ -z "$actual" ]; then
    echo "Expected manifest $expression to exist." >&2
    exit 1
  fi

  if ! printf '%s\n' "$actual" | grep -E '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$' >/dev/null; then
    echo "Expected manifest $expression to be a semantic version, got $actual." >&2
    exit 1
  fi
}

assert_path_exists() {
  path=$1

  if [ ! -e "$ROOT_DIR/$path" ]; then
    echo "Missing release-please path: $path" >&2
    exit 1
  fi
}

assert_changelog_exists() {
  package_path=$1
  changelog_path=$2

  if [ "$package_path" = "." ]; then
    assert_path_exists "$changelog_path"
    return
  fi

  assert_path_exists "$package_path/$changelog_path"
}

assert_json_value '.packages["."].["changelog-path"]' 'Packages/src/CHANGELOG.md'
assert_json_value '.packages["."].["include-component-in-tag"]' 'false'
assert_json_value '.packages["."].["exclude-paths"][0]' 'Packages/src/Cli~'

assert_json_value '.packages["Packages/src/Cli~"].component' 'cli'
assert_json_value '.packages["Packages/src/Cli~"].["include-component-in-tag"]' 'true'
assert_json_value '.packages["Packages/src/Cli~"].["changelog-path"]' 'CHANGELOG.md'
assert_json_value '.packages["Packages/src/Cli~"].["extra-files"][0].path' 'Packages/src/Cli~/internal/tools/default-tools.json'
assert_json_value '.packages["Packages/src/Cli~"].["extra-files"][1].path' 'Packages/src/Cli~/contract.json'

assert_manifest_semver '.["."]'
assert_manifest_semver '.["Packages/src/Cli~"]'

jq -r '.packages | to_entries[] | [.key, .value["changelog-path"]] | @tsv' "$CONFIG" |
while IFS='	' read -r package_path changelog_path; do
  assert_changelog_exists "$package_path" "$changelog_path"
done

jq -r '.packages | to_entries[] | .value["extra-files"][]?.path' "$CONFIG" |
while IFS= read -r extra_file_path; do
  assert_path_exists "$extra_file_path"
done
