#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
TMP_DIR=$(mktemp -d "${TMPDIR:-/tmp}/uloop-recovery-e2e-test.XXXXXX")
PROJECT_PATH="$TMP_DIR/project"
FAKE_ULOOP="$TMP_DIR/uloop"
CALL_LOG="$TMP_DIR/calls.log"

cleanup() {
    rm -rf "$TMP_DIR"
}

trap cleanup EXIT INT TERM

mkdir -p "$PROJECT_PATH/Assets" "$PROJECT_PATH/ProjectSettings"

cat > "$FAKE_ULOOP" <<'EOF'
#!/bin/sh
set -eu

if [ "$#" -lt 3 ] || [ "$1" != "--project-path" ]; then
    echo "missing leading --project-path" >&2
    exit 99
fi

project_path="$2"
shift 2
command_name="$1"
shift

printf '%s|%s|%s\n' "$project_path" "$command_name" "$*" >> "$CALL_LOG"

case "$command_name" in
    launch)
        echo "Unity is already running for $project_path (PID: 1234)"
        ;;
    get-logs)
        if [ -f "$project_path/Temp/UnityCliLoop/server-state.json" ]; then
            cat >&2 <<'JSON'
{
  "success": false,
  "error": {
    "errorCode": "UNITY_NOT_REACHABLE",
    "message": "Unity is not running, but a stale Unity CLI Loop recovery state file says it is still busy.",
    "nextActions": [
      "Run `uloop fix` to remove stale recovery state files."
    ]
  }
}
JSON
            exit 1
        fi
        echo '{"DisplayedCount":0,"Logs":[],"MaxCount":1,"TotalCount":0}'
        ;;
    compile)
        echo '{"Success":true,"ErrorCount":0,"WarningCount":0}'
        ;;
    execute-dynamic-code)
        echo '{"Success":true,"Result":"cli-recovery-readiness-e2e"}'
        ;;
    fix)
        rm -f "$project_path/Temp/UnityCliLoop/server-state.json" \
            "$project_path/Temp/UnityCliLoop/server-state.json.tmp" \
            "$project_path/Temp/UnityCliLoop/server-state.json.tmp.write" \
            "$project_path/Temp/UnityCliLoop/server-state.json.bak"
        echo "Cleaned up 1 recovery state file(s)."
        ;;
    *)
        echo "unexpected command: $command_name" >&2
        exit 98
        ;;
esac
EOF
chmod +x "$FAKE_ULOOP"

CALL_LOG="$CALL_LOG" python3 "$ROOT_DIR/scripts/smoke-cli-recovery-readiness.py" \
    --project-path "$PROJECT_PATH" \
    --uloop-path "$FAKE_ULOOP" \
    --timeout 2 \
    --launch-timeout 2 > "$TMP_DIR/output.txt"

grep -F "launch" "$CALL_LOG" >/dev/null
grep -F "compile|--wait-for-domain-reload" "$CALL_LOG" >/dev/null
grep -F "execute-dynamic-code" "$CALL_LOG" >/dev/null
grep -F "fix" "$CALL_LOG" >/dev/null
grep -F "stale recovery-state check passed" "$TMP_DIR/output.txt" >/dev/null

echo "smoke-cli-recovery-readiness harness test passed"
