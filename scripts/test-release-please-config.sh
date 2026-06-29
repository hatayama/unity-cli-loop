#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
CONFIG="$ROOT_DIR/release-please-config.json"
MANIFEST="$ROOT_DIR/.release-please-manifest.json"
RELEASE_WORKFLOW="$ROOT_DIR/.github/workflows/release-please.yml"

strip_carriage_returns() {
  tr -d '\015'
}

assert_json_value() {
  expression=$1
  expected=$2
  actual=$(jq -r "$expression" "$CONFIG" | strip_carriage_returns)

  if [ "$actual" != "$expected" ]; then
    echo "Expected $expression to be $expected, got $actual." >&2
    exit 1
  fi
}

assert_manifest_semver() {
  expression=$1
  actual=$(jq -r "$expression // empty" "$MANIFEST" | strip_carriage_returns)

  if [ -z "$actual" ]; then
    echo "Expected manifest $expression to exist." >&2
    exit 1
  fi

  if ! printf '%s\n' "$actual" | grep -E '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$' >/dev/null; then
    echo "Expected manifest $expression to be a semantic version, got $actual." >&2
    exit 1
  fi
}

assert_repository_path_exists() {
  path=$1

  if [ ! -e "$ROOT_DIR/$path" ]; then
    echo "Missing release-please path: $path" >&2
    exit 1
  fi
}

assert_file_contains() {
  path=$1
  expected=$2

  if ! grep -F -- "$expected" "$path" >/dev/null; then
    echo "Expected $path to contain: $expected" >&2
    exit 1
  fi
}

line_number() {
  path=$1
  text=$2

  grep -nF -- "$text" "$path" | head -n 1 | cut -d: -f 1
}

assert_file_order() {
  path=$1
  earlier=$2
  later=$3

  earlier_line=$(line_number "$path" "$earlier")
  later_line=$(line_number "$path" "$later")
  if [ -z "$earlier_line" ] || [ -z "$later_line" ] || [ "$earlier_line" -ge "$later_line" ]; then
    echo "Expected '$earlier' to appear before '$later' in $path." >&2
    exit 1
  fi
}

assert_step_contains() {
  path=$1
  step=$2
  expected=$3

  step_line=$(line_number "$path" "$step")
  if [ -z "$step_line" ] || ! awk -v start="$step_line" '
    NR < start { next }
    NR == start { in_step = 1 }
    in_step && NR > start && /^      - name: / { exit }
    in_step { print }
  ' "$path" | grep -F -- "$expected" >/dev/null; then
    echo "Expected step '$step' to contain: $expected" >&2
    exit 1
  fi
}

assert_package_path_exists() {
  package_path=$1
  path=$2

  if [ "$package_path" = "." ]; then
    assert_repository_path_exists "$path"
    return
  fi

  case "$path" in
    /*)
      assert_repository_path_exists "${path#/}"
      ;;
    *)
      assert_repository_path_exists "$package_path/$path"
      ;;
  esac
}

assert_changelog_exists() {
  package_path=$1
  changelog_path=$2

  assert_package_path_exists "$package_path" "$changelog_path"
}

assert_json_value '.["pull-request-header"] | contains("unity-package")' 'true'
assert_json_value '.["pull-request-header"] | contains("uloop-project-runner")' 'true'
assert_json_value '.["pull-request-header"] | contains("Dispatcher releases")' 'true'
assert_json_value '.packages["."].["changelog-path"]' 'Packages/src/CHANGELOG.md'
assert_json_value '.packages["."].component' 'unity-package'
assert_json_value '.packages["."].["include-component-in-tag"]' 'false'
assert_json_value '.packages["."].["exclude-paths"][0]' 'cli'
assert_json_value '.packages["."].["extra-files"] | length' '3'
assert_json_value '.packages["."].["extra-files"][0].path' 'Packages/src/package.json'
assert_json_value '.packages["."].["extra-files"][0].jsonpath' '$.version'
assert_json_value '.packages["."].["extra-files"][1].path' 'Packages/src/project-runner-pin.json'
assert_json_value '.packages["."].["extra-files"][1].jsonpath' '$.packageVersion'
assert_json_value '.packages["."].["extra-files"][2].path' '.uloop/project-runner-pin.json'
assert_json_value '.packages["."].["extra-files"][2].jsonpath' '$.packageVersion'

assert_json_value '.packages["cli"].component' 'uloop-project-runner'
assert_json_value '.packages["cli"].["include-component-in-tag"]' 'true'
assert_json_value '.packages["cli"].["changelog-path"]' 'CHANGELOG.md'
assert_json_value '.packages["cli"].["extra-files"] | length' '4'
assert_json_value '.packages["cli"].["extra-files"][0].path' 'internal/tools/default-tools.json'
assert_json_value '.packages["cli"].["extra-files"][1].path' 'contract.json'
assert_json_value '.packages["cli"].["extra-files"][2].path' '/Packages/src/project-runner-pin.json'
assert_json_value '.packages["cli"].["extra-files"][2].jsonpath' '$.projectRunnerVersion'
assert_json_value '.packages["cli"].["extra-files"][3].path' '/.uloop/project-runner-pin.json'
assert_json_value '.packages["cli"].["extra-files"][3].jsonpath' '$.projectRunnerVersion'

assert_file_contains "$RELEASE_WORKFLOW" 'id: package_release_sync'
assert_file_contains "$RELEASE_WORKFLOW" "steps.package_release_sync.outputs.ready != 'false'"
assert_file_contains "$RELEASE_WORKFLOW" '  actions: write'
assert_file_contains "$RELEASE_WORKFLOW" '  checks: read'
assert_file_contains "$RELEASE_WORKFLOW" '      - name: Setup Go for package release sync'
assert_file_contains "$RELEASE_WORKFLOW" '      - name: Setup Go for release PR automation'
assert_file_contains "$RELEASE_WORKFLOW" '      - name: Dispatch release PR checks'
assert_file_contains "$RELEASE_WORKFLOW" '        working-directory: cli'
assert_file_contains "$RELEASE_WORKFLOW" '        run: go run ./cmd/dispatch-release-please-pr-checks'
assert_step_contains "$RELEASE_WORKFLOW" '      - name: Setup Go for release PR automation' "        if: steps.target.outputs.branch == 'v3-beta' && steps.release_commit.outputs.skip != 'true' && steps.package_release_sync.outputs.ready != 'false'"
assert_step_contains "$RELEASE_WORKFLOW" '      - name: Dispatch release PR checks' "        if: steps.target.outputs.branch == 'v3-beta' && steps.release_commit.outputs.skip != 'true' && steps.package_release_sync.outputs.ready != 'false'"
assert_file_order "$RELEASE_WORKFLOW" '      - name: Setup Go for package release sync' 'Sync release-please package releases'
assert_file_order "$RELEASE_WORKFLOW" '      - name: Setup Go for release PR automation' '      - name: Dispatch release PR checks'

assert_manifest_semver '.["."]'
assert_manifest_semver '.["cli"]'

jq -r '.packages | to_entries[] | [.key, .value["changelog-path"]] | @tsv' "$CONFIG" |
strip_carriage_returns |
while IFS='	' read -r package_path changelog_path; do
  assert_changelog_exists "$package_path" "$changelog_path"
done

jq -r '.packages | to_entries[] | .key as $package_path | .value["extra-files"][]?.path as $extra_file_path | [$package_path, $extra_file_path] | @tsv' "$CONFIG" |
strip_carriage_returns |
while IFS='	' read -r package_path extra_file_path; do
  assert_package_path_exists "$package_path" "$extra_file_path"
done
