#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
TMP_DIR=$(mktemp -d "${TMPDIR:-/tmp}/uloop-recovery-e2e-test.XXXXXX")
PROJECT_PATH="$TMP_DIR/project"
case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*)
        FAKE_ULOOP="$TMP_DIR/uloop.exe"
        ;;
    *)
        FAKE_ULOOP="$TMP_DIR/uloop"
        ;;
esac
FAKE_ULOOP_SOURCE="$TMP_DIR/fake-uloop.go"
CALL_LOG="$TMP_DIR/calls.log"

cleanup() {
    rm -rf "$TMP_DIR"
}

trap cleanup EXIT INT TERM

mkdir -p "$PROJECT_PATH/Assets" "$PROJECT_PATH/ProjectSettings"
mkdir -p "$PROJECT_PATH/.uloop"
cp "$ROOT_DIR/.uloop/project-runner-pin.json" "$PROJECT_PATH/.uloop/project-runner-pin.json"

cat > "$FAKE_ULOOP_SOURCE" <<'EOF'
package main

import (
	"fmt"
	"os"
	"strings"
)

func main() {
	if len(os.Args) < 4 || os.Args[1] != "--project-path" {
		fmt.Fprintln(os.Stderr, "missing leading --project-path")
		os.Exit(99)
	}

	projectPath := os.Args[2]
	commandName := os.Args[3]
	commandArgs := os.Args[4:]
	appendCallLog(projectPath, commandName, commandArgs)

	switch commandName {
	case "launch":
		fmt.Printf("Unity is already running for %s (PID: 1234)\n", projectPath)
	case "get-logs":
		if os.Getenv("ULOOP_FAKE_CONNECTION_FAILURE") == "1" {
			fmt.Fprintln(os.Stderr, "{\n  \"Success\": false,\n  \"Error\": {\n    \"ErrorCode\": \"UNITY_NOT_REACHABLE\",\n    \"Message\": \"The Unity CLI Loop server is not reachable for this project.\",\n    \"NextActions\": [\n      \"If Unity is closed, run `uloop launch`.\"\n    ]\n  }\n}")
			os.Exit(1)
		}
		fmt.Println(`{"DisplayedCount":0,"Logs":[],"MaxCount":1,"TotalCount":0}`)
	case "compile":
		if os.Getenv("ULOOP_FAKE_COMPILE_WITHOUT_SUCCESS") == "1" {
			fmt.Println(`{"ErrorCount":0,"WarningCount":0}`)
			return
		}
		fmt.Println(`{"Success":true,"ErrorCount":0,"WarningCount":0}`)
	case "execute-dynamic-code":
		fmt.Println(`{"Success":true,"Result":"cli-recovery-readiness-e2e"}`)
	default:
		fmt.Fprintf(os.Stderr, "unexpected command: %s\n", commandName)
		os.Exit(98)
	}
}

func appendCallLog(projectPath string, commandName string, commandArgs []string) {
	callLogPath := os.Getenv("CALL_LOG")
	if callLogPath == "" {
		fmt.Fprintln(os.Stderr, "CALL_LOG is required")
		os.Exit(97)
	}

	line := fmt.Sprintf("%s|%s|%s\n", projectPath, commandName, strings.Join(commandArgs, " "))
	file, err := os.OpenFile(callLogPath, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o644)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(96)
	}
	defer file.Close()

	if _, err := file.WriteString(line); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(95)
	}
}

EOF

go build -o "$FAKE_ULOOP" "$FAKE_ULOOP_SOURCE"

if ! CALL_LOG="$CALL_LOG" go run "$ROOT_DIR/scripts/smoke-cli-recovery-readiness.go" \
    --project-path "$PROJECT_PATH" \
    --uloop-path "$FAKE_ULOOP" \
    --timeout 2 \
    --launch-timeout 2 > "$TMP_DIR/output.txt"; then
    cat "$TMP_DIR/output.txt"
    exit 1
fi

grep -F "launch" "$CALL_LOG" >/dev/null
grep -E '\|compile\|$' "$CALL_LOG" >/dev/null
grep -F "execute-dynamic-code" "$CALL_LOG" >/dev/null
if grep -F "fix" "$CALL_LOG" >/dev/null; then
    echo "cleanup command should not be called" >&2
    exit 1
fi
grep -F "stale recovery-state ignored" "$TMP_DIR/output.txt" >/dev/null

if CALL_LOG="$CALL_LOG" ULOOP_FAKE_COMPILE_WITHOUT_SUCCESS=1 go run "$ROOT_DIR/scripts/smoke-cli-recovery-readiness.go" \
    --project-path "$PROJECT_PATH" \
    --uloop-path "$FAKE_ULOOP" \
    --timeout 2 \
    --launch-timeout 2 > "$TMP_DIR/missing-success-output.txt" 2>&1; then
    cat "$TMP_DIR/missing-success-output.txt"
    echo "expected compile payload without success to fail" >&2
    exit 1
fi
grep -F "compile with domain reload wait returned invalid success payload" "$TMP_DIR/missing-success-output.txt" >/dev/null

echo "smoke-cli-recovery-readiness harness test passed"
