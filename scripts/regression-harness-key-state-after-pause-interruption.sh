#!/bin/sh
set -e
# Regression harness for the KeyStateAfterPauseInterruption trap:
# 1) A pause-point hit during simulate-keyboard Press must report
#    InterruptedByPausePoint=true (early return).
# 2) Resuming within 3s must NOT re-apply a discarded queued press edge
#    (device stays unpressed without a new CLI input).
# 3) After a >6s pause that leaves device/bookkeeping inconsistent,
#    ReleaseAll must restore state so a fresh Press succeeds.
# See docs/regression-harness.md.
#
# Contract under test: arm the pause-point on the key-consuming line in
# SpaceHoldPoller.cs *before* starting Press, so the hit fires naturally from
# game code once Press drives the key down (not from a concurrent CLI call).
#
# Usage: sh scripts/regression-harness-key-state-after-pause-interruption.sh [--project-path <path>]
#
# Prerequisites:
#   - Assets/RegressionHarness/KeyStateAfterPauseInterruption/KeyStateAfterPauseInterruption.unity
#     must be open in a running Unity Editor
#   - uloop CLI must be installed
#   - jq must be installed

PROJECT_PATH=""
# Why: reject malformed argv before cleanup can touch the wrong Unity project.
if [ "$#" -eq 0 ]; then
    :
elif [ "$#" -eq 2 ] && [ "$1" = "--project-path" ] && [ -n "$2" ]; then
    PROJECT_PATH="$2"
else
    printf '%s\n' "Usage: $0 [--project-path <path>]" >&2
    exit 2
fi

MARKER_FILE="Assets/RegressionHarness/KeyStateAfterPauseInterruption/SpaceHoldPoller.cs"
MARKER_LINE="19"
PRESS_DURATION="5"
RESULT_FILE="$(mktemp)"
PROBE_FILE="$(mktemp)"

run_uloop() {
    if [ -n "$PROJECT_PATH" ]; then
        uloop "$@" --project-path "$PROJECT_PATH"
    else
        uloop "$@"
    fi
}

log() {
    printf "\033[36m[key-state-after-pause]\033[0m %s\n" "$1"
}

cleanup() {
    rm -f "$RESULT_FILE" "$PROBE_FILE"
    run_uloop clear-pause-point --all > /dev/null 2>&1 || true
    run_uloop control-play-mode --action Stop > /dev/null 2>&1 || true
}
trap cleanup EXIT

# Writes Result like: devicePressed=False isSpaceHeld=False
# When clear_sticky=1, clears SpaceHoldPoller's sticky latch before reading.
probe_space_state() {
    clear_sticky="$1"
    run_uloop execute-dynamic-code --code "
using UnityEngine;
using UnityEngine.InputSystem;
using io.github.hatayama.UnityCliLoop.RegressionHarness;
SpaceHoldPoller poller = Object.FindObjectOfType<SpaceHoldPoller>();
if (poller == null) { return \"error=no-poller\"; }
if ($clear_sticky == 1) { poller.ClearStickyHeld(); }
bool devicePressed = Keyboard.current != null && Keyboard.current[Key.Space].isPressed;
return \"devicePressed=\" + devicePressed + \" isSpaceHeld=\" + poller.IsSpaceHeld;
" > "$PROBE_FILE"
}

# --- Scenario 1: interrupt reports InterruptedByPausePoint ---
log "Stopping any existing Play Mode session..."
run_uloop control-play-mode --action Stop > /dev/null

log "Clearing any existing pause-point markers..."
run_uloop clear-pause-point --all > /dev/null

log "Starting Play Mode..."
run_uloop control-play-mode --action Play > /dev/null

log "Arming pause-point on $MARKER_FILE:$MARKER_LINE (before Press starts)..."
run_uloop enable-pause-point --file "$MARKER_FILE" --line "$MARKER_LINE" --timeout-seconds 30 > /dev/null

log "Pressing Space for ${PRESS_DURATION}s (the key-down should hit the marker)..."
START_SECONDS=$(date +%s)
run_uloop simulate-keyboard --action Press --key Space --duration "$PRESS_DURATION" > "$RESULT_FILE" 2>&1 || true
ELAPSED_SECONDS=$(( $(date +%s) - START_SECONDS ))

INTERRUPTED="$(jq -r '.InterruptedByPausePoint' "$RESULT_FILE")"
SUCCESS="$(jq -r '.Success' "$RESULT_FILE")"
MESSAGE="$(jq -r '.Message' "$RESULT_FILE")"

if [ "$SUCCESS" != "true" ] || [ "$INTERRUPTED" != "true" ]; then
    log "FAIL: expected Success=true and InterruptedByPausePoint=true, got Success=$SUCCESS InterruptedByPausePoint=$INTERRUPTED"
    cat "$RESULT_FILE"
    exit 1
fi

case "$MESSAGE" in
    *"queued input edge was discarded"*) ;;
    *)
        log "FAIL: Interrupted message missing 'queued input edge was discarded'"
        cat "$RESULT_FILE"
        exit 1
        ;;
esac

# Why: a swallowed pause completes only after the full requested duration, so
# an early return is what distinguishes a real interruption from a coincidence.
if [ "$ELAPSED_SECONDS" -ge "$PRESS_DURATION" ]; then
    log "FAIL: Press took ${ELAPSED_SECONDS}s, not less than the requested ${PRESS_DURATION}s — the pause did not interrupt it early."
    cat "$RESULT_FILE"
    exit 1
fi

log "PASS scenario 1: Press interrupted after ${ELAPSED_SECONDS}s (< ${PRESS_DURATION}s)."

# --- Scenario 2: resume within 3s must not re-press discarded queued edge ---
log "Scenario 2: probe device while still paused, clear sticky latch, resume within 3s..."
probe_space_state 1
PROBE_RESULT="$(jq -r '.Result' "$PROBE_FILE")"
case "$PROBE_RESULT" in
    *devicePressed=False*|*devicePressed=false*) ;;
    *)
        log "FAIL scenario 2: expected devicePressed=false while paused after interrupt cleanup, got: $PROBE_RESULT"
        cat "$PROBE_FILE"
        exit 1
        ;;
esac

RESUME_START=$(date +%s)
run_uloop control-play-mode --action Play > /dev/null
# Observe without sending any new keyboard CLI input.
sleep 2
RESUME_ELAPSED=$(( $(date +%s) - RESUME_START ))
if [ "$RESUME_ELAPSED" -ge 3 ]; then
    log "FAIL scenario 2: resume+observe took ${RESUME_ELAPSED}s (>=3s); tighten the harness timing."
    exit 1
fi

probe_space_state 0
PROBE_AFTER="$(jq -r '.Result' "$PROBE_FILE")"
case "$PROBE_AFTER" in
    *devicePressed=True*|*devicePressed=true*|*isSpaceHeld=True*|*isSpaceHeld=true*)
        log "FAIL scenario 2: resume re-pressed Space without CLI input: $PROBE_AFTER"
        cat "$PROBE_FILE"
        exit 1
        ;;
esac
case "$PROBE_AFTER" in
    *devicePressed=False*|*devicePressed=false*) ;;
    *)
        log "FAIL scenario 2: unexpected probe result after resume: $PROBE_AFTER"
        cat "$PROBE_FILE"
        exit 1
        ;;
esac

log "PASS scenario 2: no re-press within ${RESUME_ELAPSED}s after resume ($PROBE_AFTER)."

run_uloop clear-pause-point --all > /dev/null
run_uloop control-play-mode --action Stop > /dev/null

# --- Scenario 3: >6s pause → ReleaseAll → Press succeeds ---
log "Scenario 3: interrupt, hold pause >6s, resume, ReleaseAll, then Press must succeed..."
run_uloop control-play-mode --action Play > /dev/null
run_uloop enable-pause-point --file "$MARKER_FILE" --line "$MARKER_LINE" --timeout-seconds 60 > /dev/null
run_uloop simulate-keyboard --action Press --key Space --duration "$PRESS_DURATION" > "$RESULT_FILE" 2>&1 || true
INTERRUPTED3="$(jq -r '.InterruptedByPausePoint' "$RESULT_FILE")"
if [ "$INTERRUPTED3" != "true" ]; then
    log "FAIL scenario 3 setup: expected InterruptedByPausePoint=true"
    cat "$RESULT_FILE"
    exit 1
fi

sleep 7
run_uloop control-play-mode --action Play > /dev/null

run_uloop simulate-keyboard --action ReleaseAll > "$RESULT_FILE" 2>&1 || true
RELEASE_SUCCESS="$(jq -r '.Success' "$RESULT_FILE")"
RELEASE_ACTION="$(jq -r '.Action' "$RESULT_FILE")"
if [ "$RELEASE_SUCCESS" != "true" ] || [ "$RELEASE_ACTION" != "ReleaseAll" ]; then
    log "FAIL scenario 3: ReleaseAll failed"
    cat "$RESULT_FILE"
    exit 1
fi

run_uloop simulate-keyboard --action Press --key Space --duration 0.2 > "$RESULT_FILE" 2>&1 || true
PRESS_SUCCESS="$(jq -r '.Success' "$RESULT_FILE")"
PRESS_INTERRUPTED="$(jq -r '.InterruptedByPausePoint // false' "$RESULT_FILE")"
ALREADY_DOWN="$(jq -r '.PressEdgeKeyAlreadyPressedBeforeQueue // false' "$RESULT_FILE")"
if [ "$PRESS_SUCCESS" != "true" ] || [ "$PRESS_INTERRUPTED" = "true" ]; then
    log "FAIL scenario 3: Press after ReleaseAll did not succeed cleanly"
    cat "$RESULT_FILE"
    exit 1
fi
if [ "$ALREADY_DOWN" = "true" ]; then
    log "FAIL scenario 3: Press still reported key already down after ReleaseAll"
    cat "$RESULT_FILE"
    exit 1
fi

log "PASS scenario 3: ReleaseAll recovered key state; Press succeeded."
log "PASS: all key-state-after-pause-interruption scenarios."
exit 0
