#!/bin/sh
set -e
# Regression harness for enable-pause-point --await --resume-play --trigger:
# arming a pause-point while PlayMode is manually paused, then resuming and
# firing the input trigger in one CLI call, must hit the marker and report
# ResumePlayResult.WasPaused=true / Resumed=true. Reuses the
# KeyStateAfterPauseInterruption scene scaffolding (see
# regression-harness-key-state-after-pause-interruption.sh).
#
# Usage: sh scripts/regression-harness-resume-play-paused-arm.sh [--project-path <path>]
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

run_uloop() {
    if [ -n "$PROJECT_PATH" ]; then
        uloop "$@" --project-path "$PROJECT_PATH"
    else
        uloop "$@"
    fi
}

log() {
    printf "\033[36m[resume-play-paused-arm]\033[0m %s\n" "$1"
}

cleanup() {
    rm -f "$RESULT_FILE"
    run_uloop clear-pause-point --all > /dev/null 2>&1 || true
    run_uloop control-play-mode --action Stop > /dev/null 2>&1 || true
}
trap cleanup EXIT

log "Stopping any existing Play Mode session..."
run_uloop control-play-mode --action Stop > /dev/null

log "Clearing any existing pause-point markers..."
run_uloop clear-pause-point --all > /dev/null

log "Starting Play Mode..."
run_uloop control-play-mode --action Play > /dev/null

log "Pausing Play Mode before arming..."
run_uloop control-play-mode --action Pause > /dev/null

log "Arming + awaiting with --resume-play --trigger on $MARKER_FILE:$MARKER_LINE..."
# Why: TimeoutSeconds is not frozen during a manual Pause, so keep the window
# generous enough for resume + trigger + hit under a slow Editor tick.
run_uloop enable-pause-point \
    --file "$MARKER_FILE" \
    --line "$MARKER_LINE" \
    --timeout-seconds 60 \
    --await \
    --resume-play \
    --trigger "simulate-keyboard --action Press --key Space --duration $PRESS_DURATION" \
    > "$RESULT_FILE" 2>&1 || true

SUCCESS="$(jq -r '.Success' "$RESULT_FILE")"
STATUS="$(jq -r '.Status' "$RESULT_FILE")"
WAS_PAUSED="$(jq -r '.ResumePlayResult.WasPaused' "$RESULT_FILE")"
RESUMED="$(jq -r '.ResumePlayResult.Resumed' "$RESULT_FILE")"
TRIGGER_INTERRUPTED="$(jq -r '.TriggerResult.Response.InterruptedByPausePoint' "$RESULT_FILE")"

if [ "$SUCCESS" != "true" ] || [ "$STATUS" != "Hit" ]; then
    log "FAIL: expected Success=true Status=Hit, got Success=$SUCCESS Status=$STATUS"
    cat "$RESULT_FILE"
    exit 1
fi

if [ "$WAS_PAUSED" != "true" ] || [ "$RESUMED" != "true" ]; then
    log "FAIL: expected ResumePlayResult WasPaused=true Resumed=true, got WasPaused=$WAS_PAUSED Resumed=$RESUMED"
    cat "$RESULT_FILE"
    exit 1
fi

if [ "$TRIGGER_INTERRUPTED" != "true" ]; then
    log "FAIL: expected TriggerResult.Response.InterruptedByPausePoint=true, got $TRIGGER_INTERRUPTED"
    cat "$RESULT_FILE"
    exit 1
fi

log "PASS: paused-arm --resume-play --trigger hit with ResumePlayResult.Resumed=true."
exit 0
