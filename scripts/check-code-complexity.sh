#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
MAX_COMPLEXITY=${CODE_COMPLEXITY_MAX_COMPLEXITY:-25}
FAIL_ON_EXCEEDED=${CODE_COMPLEXITY_FAIL_ON_EXCEEDED:-false}
GO_CONFIG="$ROOT_DIR/cli/.golangci-complexity.yml"
TEMP_GO_CONFIG=

GO_STATUS=0
CS_STATUS=0

cleanup() {
  if [ -n "$TEMP_GO_CONFIG" ] && [ -f "$TEMP_GO_CONFIG" ]; then
    rm -f "$TEMP_GO_CONFIG"
  fi
}

trap cleanup 0 1 2 15

if [ "$MAX_COMPLEXITY" != "25" ]; then
  TEMP_GO_CONFIG="$ROOT_DIR/cli/.golangci-complexity.$$.yml"
  awk -v max_complexity="$MAX_COMPLEXITY" '
    $1 == "max-complexity:" {
      print "      max-complexity: " max_complexity
      next
    }
    {
      print
    }
  ' "$GO_CONFIG" > "$TEMP_GO_CONFIG"
  GO_CONFIG="$TEMP_GO_CONFIG"
fi

echo "=== Go complexity (cyclop, max ${MAX_COMPLEXITY}) ==="
(
  cd "$ROOT_DIR/cli"
  golangci-lint run --config "$GO_CONFIG" ./...
) || GO_STATUS=$?

echo ""
echo "=== C# complexity (CA1502, max ${MAX_COMPLEXITY}) ==="
dotnet run --project "$ROOT_DIR/tools/UnityCliLoop.CodeComplexity/UnityCliLoop.CodeComplexity.csproj" -- \
  --root "$ROOT_DIR" \
  --max-complexity "$MAX_COMPLEXITY" \
  --fail-on-exceeded "$FAIL_ON_EXCEEDED" || CS_STATUS=$?

if [ "$FAIL_ON_EXCEEDED" = "true" ]; then
  if [ "$GO_STATUS" -ne 0 ] || [ "$CS_STATUS" -ne 0 ]; then
    exit 1
  fi

  exit 0
fi

if [ "$GO_STATUS" -ne 0 ] && [ "$GO_STATUS" -ne 1 ]; then
  exit "$GO_STATUS"
fi

if [ "$CS_STATUS" -ne 0 ]; then
  exit "$CS_STATUS"
fi

if [ "$GO_STATUS" -ne 0 ] || [ "$CS_STATUS" -ne 0 ]; then
  echo ""
  echo "Complexity findings were reported in warning mode; set CODE_COMPLEXITY_FAIL_ON_EXCEEDED=true to fail on findings."
fi

exit 0
