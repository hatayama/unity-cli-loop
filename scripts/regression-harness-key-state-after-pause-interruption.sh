#!/bin/sh
set -e
# Regression harness for the KeyStateAfterPauseInterruption trap: a pause-point
# hit during simulate-keyboard Press must be reported as an interruption
# (InterruptedByPausePoint=true) instead of being silently absorbed into a
# successful Completed result. See docs/regression-harness.md.
#
# Contract under test: arm the pause-point on the key-consuming line in
# SpaceHoldPoller.cs *before* starting Press, so the hit fires naturally from
# game code once Press drives the key down (not from a concurrent CLI call).
# The environment-dependent editor-tick-stall path that can also swallow a
# pause is covered by PressLifetimeIterationResolverTests (unit-level), not by
# this live harness — see that test file for the decisive Red/Green coverage.
#
# Usage: sh scripts/regression-harness-key-state-after-pause-interruption.sh [--project-path <path>]
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
    printf "\033[36m[key-state-after-pause]\033[0m %s\n" "$1"
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

log "Arming pause-point on $MARKER_FILE:$MARKER_LINE (before Press starts)..."
run_uloop enable-pause-point --file "$MARKER_FILE" --line "$MARKER_LINE" --timeout-seconds 30 > /dev/null

log "Pressing Space for ${PRESS_DURATION}s in the background (the key-down should hit the marker)..."
START_SECONDS=$(date +%s)
run_uloop simulate-keyboard --action Press --key Space --duration "$PRESS_DURATION" > "$RESULT_FILE" 2>&1 &
PRESS_PID=$!

wait "$PRESS_PID"
ELAPSED_SECONDS=$(( $(date +%s) - START_SECONDS ))

INTERRUPTED="$(jq -r '.InterruptedByPausePoint' "$RESULT_FILE")"
SUCCESS="$(jq -r '.Success' "$RESULT_FILE")"

if [ "$SUCCESS" != "true" ] || [ "$INTERRUPTED" != "true" ]; then
    log "FAIL: expected Success=true and InterruptedByPausePoint=true, got Success=$SUCCESS InterruptedByPausePoint=$INTERRUPTED"
    cat "$RESULT_FILE"
    exit 1
fi

# Why: a swallowed pause completes only after the full requested duration, so
# an early return is what distinguishes a real interruption from a coincidence.
if [ "$ELAPSED_SECONDS" -ge "$PRESS_DURATION" ]; then
    log "FAIL: Press took ${ELAPSED_SECONDS}s, not less than the requested ${PRESS_DURATION}s — the pause did not interrupt it early."
    cat "$RESULT_FILE"
    exit 1
fi

log "PASS: Press was interrupted by the pause-point hit after ${ELAPSED_SECONDS}s (< ${PRESS_DURATION}s requested)."
exit 0
