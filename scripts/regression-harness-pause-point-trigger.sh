#!/bin/sh
set -e
# Regression harness for the await-pause-point/enable-pause-point --await
# --trigger flag: dispatching the action that hits the marker from inside the
# wait call itself must report a hit within the marker's timeout, not fall
# back to the full duration of the triggered command. Reuses the
# KeyStateAfterPauseInterruption scene scaffolding (see
# regression-harness-key-state-after-pause-interruption.sh) but replaces its
# background-Press-then-separate-await flow with a single enable +
# await --trigger call.
#
# Usage: sh scripts/regression-harness-pause-point-trigger.sh [--project-path <path>]
#
# Prerequisites:
#   - Assets/RegressionHarness/KeyStateAfterPauseInterruption/KeyStateAfterPauseInterruption.unity
#     must be open in a running Unity Editor
#   - uloop CLI must be installed
#   - jq must be installed

PROJECT_PATH=""
if [ "$1" = "--project-path" ] && [ -n "$2" ]; then
    PROJECT_PATH="$2"
fi

MARKER_ID="Assets/RegressionHarness/KeyStateAfterPauseInterruption/SpaceHoldPoller.cs:19"
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
    printf "\033[36m[pause-point-trigger]\033[0m %s\n" "$1"
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

log "Arming pause-point on $MARKER_FILE:$MARKER_LINE..."
run_uloop enable-pause-point --file "$MARKER_FILE" --line "$MARKER_LINE" --timeout-seconds 30 > /dev/null

log "Awaiting the marker with --trigger driving Press for ${PRESS_DURATION}s in one call..."
START_SECONDS=$(date +%s)
run_uloop await-pause-point --id "$MARKER_ID" --timeout-seconds 30 \
    --trigger "simulate-keyboard --action Press --key Space --duration $PRESS_DURATION" \
    > "$RESULT_FILE" 2>&1 || true
ELAPSED_SECONDS=$(( $(date +%s) - START_SECONDS ))

STATUS="$(jq -r '.Status' "$RESULT_FILE")"
SUCCESS="$(jq -r '.Success' "$RESULT_FILE")"
TRIGGER_INTERRUPTED="$(jq -r '.TriggerResult.Response.InterruptedByPausePoint' "$RESULT_FILE")"

if [ "$SUCCESS" != "true" ] || [ "$STATUS" != "Hit" ] || [ "$TRIGGER_INTERRUPTED" != "true" ]; then
    log "FAIL: expected Success=true Status=Hit TriggerResult.Response.InterruptedByPausePoint=true, got Success=$SUCCESS Status=$STATUS InterruptedByPausePoint=$TRIGGER_INTERRUPTED"
    cat "$RESULT_FILE"
    exit 1
fi

# Why: a --trigger that isn't actually racing the wait would only ever return
# after the triggered command's own full duration, so an early hit is what
# distinguishes real concurrent dispatch from a coincidence.
if [ "$ELAPSED_SECONDS" -ge "$PRESS_DURATION" ]; then
    log "FAIL: await-pause-point took ${ELAPSED_SECONDS}s, not less than the triggered Press duration of ${PRESS_DURATION}s."
    cat "$RESULT_FILE"
    exit 1
fi

log "PASS: await-pause-point --trigger hit after ${ELAPSED_SECONDS}s (< ${PRESS_DURATION}s triggered Press duration)."
exit 0
