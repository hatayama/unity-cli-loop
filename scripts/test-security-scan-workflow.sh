#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
WORKFLOW="$ROOT_DIR/.github/workflows/security-scan.yml"

assert_contains() {
  expected=$1
  if ! grep -F -- "$expected" "$WORKFLOW" >/dev/null 2>&1; then
    echo "Expected security workflow to contain: $expected" >&2
    exit 1
  fi
}

assert_absent() {
  unexpected=$1
  if grep -F -- "$unexpected" "$WORKFLOW" >/dev/null 2>&1; then
    echo "Expected security workflow not to contain: $unexpected" >&2
    exit 1
  fi
}

assert_step_order() {
  first=$1
  second=$2
  first_line=$(grep -n -F -- "$first" "$WORKFLOW" | head -1 | cut -d: -f1)
  second_line=$(grep -n -F -- "$second" "$WORKFLOW" | head -1 | cut -d: -f1)
  if [ -z "$first_line" ] || [ -z "$second_line" ] || [ "$first_line" -ge "$second_line" ]; then
    echo "Expected $first to precede $second" >&2
    exit 1
  fi
}

assert_contains "      contents: read"
assert_contains "      security-events: write"
assert_absent "      actions: read"
assert_contains "uses: github/codeql-action/init@99df26d4f13ea111d4ec1a7dddef6063f76b97e9 # v4.37.0"
assert_contains "uses: github/codeql-action/analyze@99df26d4f13ea111d4ec1a7dddef6063f76b97e9 # v4.37.0"
assert_contains "uses: github/codeql-action/upload-sarif@cdf488f595d80d6e07e03d4674febd5ab45fa938 # v4.37.9"
assert_contains "        build-mode: none"
assert_contains "        tools: linked"
assert_contains "        queries: +security-extended"
assert_contains "      run: go run ./cmd/check-codeql-sarif --sarif \"\${{ runner.temp }}/codeql-results/csharp.sarif\""
assert_contains "      if: github.event_name != 'pull_request' || github.event.pull_request.head.repo.fork == false"
assert_contains "      if: github.event_name == 'pull_request' && github.event.pull_request.head.repo.fork"
assert_contains "      - '.github/workflows/**'"
assert_contains "      - 'cli/release-automation/**'"
assert_contains "      - 'scripts/**'"
assert_contains "      - 'cli/dispatcher/attestation/trusted_root.json'"
assert_contains "      - 'Packages/src/project-runner-pin.json'"
assert_contains "      - '.uloop/project-runner-pin.json'"
assert_absent "SecurityCodeScan"
assert_absent "placeholder SARIF"
assert_absent "continue-on-error"
assert_absent "if: always()"
assert_step_order "    - name: Analyze C# security code with CodeQL" "    - name: Validate CodeQL SARIF"
assert_step_order "    - name: Validate CodeQL SARIF" "    - name: Upload verified CodeQL SARIF"
