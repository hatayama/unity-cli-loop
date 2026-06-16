#!/bin/sh
# E2E verification: record input, replay it through the CLI, and compare behavior.
#
# Usage: sh verify-replay-via-cli.sh [--project-path <path>] [--automated-input]
#
# Prerequisites:
#   - Unity Editor running for the target project
#   - PlayMode is not running because this script starts it

set -e

PROJECT_PATH=""
ULOOP_PATH="${ULOOP_BIN:-uloop}"
AUTOMATED_INPUT=false
SCENE_PATH="Assets/Scenes/InputReplayVerificationScene.unity"

fail() {
    echo "ERROR: $1" >&2
    exit 1
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --project-path)
            [ "$#" -ge 2 ] || fail "--project-path requires a value"
            PROJECT_PATH=$2
            shift 2
            ;;
        --uloop-path)
            [ "$#" -ge 2 ] || fail "--uloop-path requires a value"
            ULOOP_PATH=$2
            shift 2
            ;;
        --automated-input)
            AUTOMATED_INPUT=true
            shift
            ;;
        -h|--help)
            echo "Usage: sh verify-replay-via-cli.sh [--project-path <path>] [--uloop-path <path>] [--automated-input]"
            exit 0
            ;;
        *)
            fail "unknown option: $1"
            ;;
    esac
done

run_uloop() {
    if [ -n "$PROJECT_PATH" ]; then
        "$ULOOP_PATH" --project-path "$PROJECT_PATH" "$@"
    else
        "$ULOOP_PATH" "$@"
    fi
}

RECORDING_LOG=".uloop/outputs/InputRecordings/recording-event-log.txt"
REPLAY_LOG=".uloop/outputs/InputRecordings/replay-event-log.txt"

if [ -n "$PROJECT_PATH" ]; then
    RECORDING_LOG="$PROJECT_PATH/$RECORDING_LOG"
    REPLAY_LOG="$PROJECT_PATH/$REPLAY_LOG"
fi

run_uloop_json() {
    output=$(run_uloop "$@" 2>&1) || {
        printf '%s\n' "$output" >&2
        fail "uloop $* failed"
    }
    if printf '%s\n' "$output" | grep -Eq '"success"[[:space:]]*:[[:space:]]*false'; then
        printf '%s\n' "$output" >&2
        fail "uloop $* returned success=false"
    fi

    printf '%s\n' "$output"
}

assert_json_result() {
    json=$1
    expected=$2
    context=$3

    actual=$(printf '%s\n' "$json" | sed -n 's/.*"result"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
    [ "$actual" = "$expected" ] || fail "$context: expected '$expected', got '$actual'"
}

wait_for_unity() {
    i=0
    while [ $i -lt 15 ]; do
        if run_uloop get-logs --max-count 1 > /dev/null 2>&1; then
            return 0
        fi
        sleep 2
        i=$((i + 1))
    done
    echo "ERROR: Unity not responding"
    exit 1
}

activate_for_record() {
    json=$(run_uloop_json execute-dynamic-code --code '
var cube = GameObject.Find("VerificationCube");
if (cube == null) return "ERROR: VerificationCube not found";
cube.SendMessage("ActivateForExternalControl");
return "OK: activated for recording";
')
    assert_json_result "$json" "OK: activated for recording" "Activate recording controller"
}

activate_for_replay() {
    json=$(run_uloop_json execute-dynamic-code --code '
var cube = GameObject.Find("VerificationCube");
if (cube == null) return "ERROR: VerificationCube not found";
cube.SendMessage("ActivateForExternalReplay");
return "OK: activated for replay";
')
    assert_json_result "$json" "OK: activated for replay" "Activate replay controller"
}

save_log() {
    unity_path=$1
    if command -v cygpath >/dev/null 2>&1; then
        unity_path=$(cygpath -w "$1")
    fi
    escaped_path=$(printf '%s\n' "$unity_path" | sed 's/\\/\\\\/g; s/"/\\"/g')
    rm -f "$1"
    json=$(run_uloop_json execute-dynamic-code --code "
var cube = GameObject.Find(\"VerificationCube\");
if (cube == null) return \"ERROR: VerificationCube not found\";
cube.SendMessage(\"SaveLog\", \"$escaped_path\");
return \"OK: log saved\";
")
    assert_json_result "$json" "OK: log saved" "Save event log"
    [ -f "$1" ] || fail "Save event log did not create $1"
}

initialize_replay_scene() {
    run_uloop control-play-mode --action Stop >/dev/null 2>&1 || true

    json=$(run_uloop_json execute-dynamic-code --code "
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
string scenePath = \"$SCENE_PATH\";
if (SceneManager.GetActiveScene().path != scenePath)
{
    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
}
return SceneManager.GetActiveScene().path;
")
    assert_json_result "$json" "$SCENE_PATH" "Load replay verification scene"
}

invoke_automated_input() {
    run_uloop_json simulate-mouse-input --action SmoothDelta --delta-x 96 --delta-y 0 --duration 0.25 >/dev/null
    sleep 1
    run_uloop_json simulate-mouse-input --action Click --x 400 --y 300 >/dev/null
    sleep 1
    run_uloop_json simulate-mouse-input --action Scroll --scroll-y 120 >/dev/null
    sleep 1
}

echo ""
echo "========================================="
echo "  Input Record/Replay E2E Verification"
echo "========================================="

# ---- Phase 1: Record human input ----

echo ""
echo "[1/9] Loading replay verification scene..."
initialize_replay_scene

echo "[2/9] Starting PlayMode..."
run_uloop_json control-play-mode --action Play >/dev/null
echo "  Waiting for Unity..."
sleep 6
wait_for_unity

echo "[3/9] Activating controller..."
activate_for_record

echo "[4/9] Starting recording via CLI..."
if [ "$AUTOMATED_INPUT" = true ]; then
    run_uloop_json record-input --action Start --delay-seconds 0 --no-show-overlay >/dev/null
else
    run_uloop_json record-input --action Start >/dev/null
fi

if [ "$AUTOMATED_INPUT" = true ]; then
    echo "  Running automated input sequence..."
    sleep 1
    invoke_automated_input
else
    echo ""
    echo "========================================="
    echo "  Recording is active!"
    echo "  Go to the Unity Game View and play."
    echo ""
    echo "  WASD: move | Mouse: rotate"
    echo "  Left click: red | Right click: blue"
    echo "  Scroll: scale"
    echo ""
    echo "  Press ENTER here when done."
    echo "========================================="
    echo ""
    read -r _
fi

echo "[5/9] Stopping recording via CLI..."
RECORD_STOP_RESULT=$(run_uloop_json record-input --action Stop)
echo "  $RECORD_STOP_RESULT"
RECORDING_INPUT_PATH=$(printf '%s\n' "$RECORD_STOP_RESULT" | sed -n 's/.*"outputPath"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')

echo "[6/9] Saving recording event log..."
save_log "$RECORDING_LOG"
[ -s "$RECORDING_LOG" ] || fail "Recording event log is empty"

# ---- Phase 2: Replay via CLI ----

echo "[7/9] Restarting PlayMode..."
run_uloop_json control-play-mode --action Stop >/dev/null
sleep 3
run_uloop_json control-play-mode --action Play >/dev/null
echo "  Waiting for Unity..."
sleep 6
wait_for_unity

echo "[8/9] Activating controller + starting replay via CLI..."
activate_for_replay
echo "  Starting replay..."
if [ "$AUTOMATED_INPUT" = true ] && [ -n "$RECORDING_INPUT_PATH" ]; then
    REPLAY_RESULT=$(run_uloop replay-input --action Start --input-path "$RECORDING_INPUT_PATH" --no-show-overlay 2>&1) || true
else
    REPLAY_RESULT=$(run_uloop replay-input --action Start 2>&1) || true
fi
echo "  $REPLAY_RESULT"

echo "  Waiting for replay to finish..."
sleep 2
waited=0
while [ $waited -lt 60 ]; do
    STATUS_RESULT=$(run_uloop replay-input --action Status 2>&1) || true
    playing=$(echo "$STATUS_RESULT" | grep -o '"isReplaying": *[a-z]*' | sed 's/.*: *//')
    if [ "$playing" = "false" ]; then
        echo "  Replay completed."
        break
    fi
    if [ $((waited % 5)) -eq 0 ]; then
        progress=$(echo "$STATUS_RESULT" | grep -o '"progress": *[0-9.]*' | sed 's/.*: *//')
        echo "  Progress: ${progress:-...}"
    fi
    sleep 1
    waited=$((waited + 1))
done
echo ""

if [ $waited -ge 60 ]; then
    echo "ERROR: Replay did not complete within 60s"
    echo "  Last status: $STATUS_RESULT"
    exit 1
fi
sleep 1

echo "[9/9] Saving replay event log..."
save_log "$REPLAY_LOG"
[ -s "$REPLAY_LOG" ] || fail "Replay event log is empty"

# ---- Phase 3: Compare ----

echo ""
echo "[Final] Comparing logs..."
echo ""

# Normalize frame numbers to relative (first event = frame 0).
# CLI commands introduce variable delays, so absolute frame numbers
# differ, but relative timing between events should be identical.
normalize_frames() {
    base=$(head -1 "$1" | sed 's/Frame \([0-9]*\):.*/\1/')
    sed "s/Frame \([0-9]*\)/Frame \1/" "$1" | while IFS= read -r line; do
        frame=$(echo "$line" | sed 's/Frame \([0-9]*\):.*/\1/')
        rest=$(echo "$line" | sed 's/Frame [0-9]*: //')
        echo "Frame $((frame - base)): $rest"
    done
}

normalize_frames "$RECORDING_LOG" > "$RECORDING_LOG.norm"
normalize_frames "$REPLAY_LOG" > "$REPLAY_LOG.norm"

if diff "$RECORDING_LOG.norm" "$REPLAY_LOG.norm" > /dev/null 2>&1; then
    lines=$(wc -l < "$RECORDING_LOG.norm" | tr -d ' ')
    echo "========================================="
    echo "  RESULT: MATCH ($lines events identical)"
    echo "  Relative frame timing verified."
    echo "========================================="
    echo ""
    rm -f "$RECORDING_LOG.norm" "$REPLAY_LOG.norm"
    exit 0
else
    cnt=$(diff "$RECORDING_LOG.norm" "$REPLAY_LOG.norm" | grep -c '^[<>]' || true)
    echo "========================================="
    echo "  RESULT: MISMATCH ($cnt differences)"
    echo "========================================="
    echo ""
    diff "$RECORDING_LOG.norm" "$REPLAY_LOG.norm" | head -20
    echo ""
    rm -f "$RECORDING_LOG.norm" "$REPLAY_LOG.norm"
    exit 1
fi
