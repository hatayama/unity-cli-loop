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

cat > "$FAKE_ULOOP_SOURCE" <<'EOF'
package main

import (
	"fmt"
	"os"
	"path/filepath"
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
		if fileExists(filepath.Join(projectPath, "Temp", "UnityCliLoop", "server-state.json")) {
			fmt.Fprintln(os.Stderr, "{\n  \"success\": false,\n  \"error\": {\n    \"errorCode\": \"UNITY_NOT_REACHABLE\",\n    \"message\": \"Unity is not running, but a stale Unity CLI Loop recovery state file says it is still busy.\",\n    \"nextActions\": [\n      \"Run `uloop fix` to remove stale recovery state files.\"\n    ]\n  }\n}")
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
	case "fix":
		removeRecoveryStateFiles(projectPath)
		fmt.Println("Cleaned up 1 recovery state file(s).")
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

func fileExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && !info.IsDir()
}

func removeRecoveryStateFiles(projectPath string) {
	stateDir := filepath.Join(projectPath, "Temp", "UnityCliLoop")
	files := []string{
		"server-state.json",
		"server-state.json.tmp",
		"server-state.json.tmp.write",
		"server-state.json.bak",
	}
	for _, file := range files {
		_ = os.Remove(filepath.Join(stateDir, file))
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
grep -F "compile|--wait-for-domain-reload" "$CALL_LOG" >/dev/null
grep -F "execute-dynamic-code" "$CALL_LOG" >/dev/null
grep -F "fix" "$CALL_LOG" >/dev/null
grep -F "stale recovery-state check passed" "$TMP_DIR/output.txt" >/dev/null

if CALL_LOG="$CALL_LOG" ULOOP_FAKE_COMPILE_WITHOUT_SUCCESS=1 go run "$ROOT_DIR/scripts/smoke-cli-recovery-readiness.go" \
    --project-path "$PROJECT_PATH" \
    --uloop-path "$FAKE_ULOOP" \
    --timeout 2 \
    --launch-timeout 2 > "$TMP_DIR/missing-success-output.txt" 2>&1; then
    cat "$TMP_DIR/missing-success-output.txt"
    echo "expected compile payload without Success to fail" >&2
    exit 1
fi
grep -F "compile with domain reload wait returned invalid success payload" "$TMP_DIR/missing-success-output.txt" >/dev/null

echo "smoke-cli-recovery-readiness harness test passed"
