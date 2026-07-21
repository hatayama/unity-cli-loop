#!/bin/sh
set -e
# Regression harness for the "existing instance" physics-callback pause-point gap:
# Unity's physics message dispatch (OnCollision*/OnTrigger*) has been observed in real projects to
# bypass a Harmony patch applied while the GameObject already existed, so the pause point can
# silently miss even though the method body runs. The trigger condition is environment-dependent
# and has NOT been reproduced deterministically here (see docs/regression-harness.md for the list
# of ruled-out hypotheses) -- this harness therefore treats the baseline-miss check as conditional:
#   - if the pre-existing instance misses the pause point (the miss reproduces in this run), the
#     enabled-toggle workaround is strictly asserted to fix it
#   - if it does not miss (the common case in this environment), that is logged and this run passes
#     without exercising the workaround assertion
# It covers three call shapes:
#   1. direct   -- pause point on the physics message method itself (OnCollisionEnter2D)
#   2. indirect -- pause point on a method called one hop deep from the physics message method,
#      with the instance primed (one prior contact before arming) to match the dominant pattern
#      reported from real games, where the marker sits in a helper method rather than the callback
#   3. trigger  -- pause point on OnTriggerEnter2D, a separate Unity dispatch path from OnCollision*
#
# The harness forces a fresh domain reload via EditorUtility.RequestScriptReload() before arming,
# since the enabled-toggle workaround (when it applies) re-resolves dispatch for the whole
# component type -- without a reload, a type already "fixed" by an earlier local run of this
# harness would carry that fix into this run. RequestScriptReload queues the reload for a later
# editor update rather than running it inline, so polling for bare IPC responsiveness is not
# sufficient -- a still-loading-the-reload domain and the still-alive pre-reload domain both answer
# "list" successfully. Reload completion is instead detected via HarnessDomainMarker.Id, a static
# readonly value that is re-initialized only when the AppDomain actually reloads; the harness waits
# until this value changes.
# See docs/regression-harness.md and
# SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning.
#
# Usage: sh scripts/regression-harness-physics-callback-existing-instance.sh [--project-path <path>]
#
# Prerequisites:
#   - Assets/RegressionHarness/PhysicsCallbackExistingInstance/PhysicsCallbackExistingInstance.unity
#     must be open in a running Unity Editor
#   - uloop CLI must be installed
#   - jq must be installed

PROJECT_PATH=""
if [ "$1" = "--project-path" ] && [ -n "$2" ]; then
    PROJECT_PATH="$2"
fi

FLOOR_FILE="Assets/RegressionHarness/PhysicsCallbackExistingInstance/PhysicsCallbackFloor.cs"
FLOOR_DIRECT_LINE="19"
FLOOR_INDIRECT_LINE="28"
TRIGGER_FILE="Assets/RegressionHarness/PhysicsCallbackExistingInstance/PhysicsCallbackTriggerZone.cs"
TRIGGER_LINE="15"
RESULT_FILE="$(mktemp)"
DYNAMIC_CODE_FILE="$(mktemp)"
RELOAD_RECOVERY_RETRIES=15
RELOAD_RECOVERY_INTERVAL_SECONDS=2

run_uloop() {
    if [ -n "$PROJECT_PATH" ]; then
        uloop "$@" --project-path "$PROJECT_PATH"
    else
        uloop "$@"
    fi
}

log() {
    printf "\033[36m[physics-callback-existing-instance]\033[0m %s\n" "$1"
}

cleanup() {
    rm -f "$RESULT_FILE" "$DYNAMIC_CODE_FILE"
    run_uloop clear-pause-point --all > /dev/null 2>&1 || true
    run_uloop control-play-mode --action Stop > /dev/null 2>&1 || true
}
trap cleanup EXIT

read_domain_marker() {
    cat > "$DYNAMIC_CODE_FILE" <<'EOF'
using io.github.hatayama.UnityCliLoop.RegressionHarness;

return HarnessDomainMarker.Id;
EOF
    run_uloop execute-dynamic-code --code-file "$DYNAMIC_CODE_FILE" 2>/dev/null | jq -r '.Result // empty'
}

# Forces a fresh domain reload without touching any file, so the component type carries no
# leftover "fixed by a previous toggle" state into this run's baseline-miss check. Unity
# disconnects the IPC bridge for the duration of the reload -- that failure is expected and
# swallowed here. RequestScriptReload queues the reload for a later editor update rather than
# running it inline, so bare IPC responsiveness is not proof the reload happened -- completion is
# instead detected by HarnessDomainMarker.Id (a static readonly value) changing from its
# pre-reload reading, with a hard retry cap.
force_clean_domain() {
    BEFORE_MARKER="$(read_domain_marker)"
    if [ -z "$BEFORE_MARKER" ]; then
        log "FAIL: could not read HarnessDomainMarker.Id before requesting a script reload."
        exit 1
    fi

    cat > "$DYNAMIC_CODE_FILE" <<'EOF'
using UnityEditor;

EditorUtility.RequestScriptReload();
return "Requested script reload";
EOF
    run_uloop execute-dynamic-code --code-file "$DYNAMIC_CODE_FILE" > /dev/null 2>&1 || true

    log "Waiting for the domain marker to change (proves the reload actually completed)..."
    ATTEMPT=1
    while [ "$ATTEMPT" -le "$RELOAD_RECOVERY_RETRIES" ]; do
        sleep "$RELOAD_RECOVERY_INTERVAL_SECONDS"
        AFTER_MARKER="$(read_domain_marker)"
        if [ -n "$AFTER_MARKER" ] && [ "$AFTER_MARKER" != "$BEFORE_MARKER" ]; then
            log "Domain reload confirmed after ${ATTEMPT} attempt(s) (marker changed)."
            return 0
        fi
        ATTEMPT=$((ATTEMPT + 1))
    done

    log "FAIL: HarnessDomainMarker.Id did not change within $((RELOAD_RECOVERY_RETRIES * RELOAD_RECOVERY_INTERVAL_SECONDS))s of requesting a script reload."
    exit 1
}

reset_ball() {
    cat > "$DYNAMIC_CODE_FILE" <<'EOF'
using UnityEngine;

GameObject ball = GameObject.Find("Ball");
Rigidbody2D ballBody = ball.GetComponent<Rigidbody2D>();
ballBody.velocity = Vector2.zero;
ball.transform.position = new Vector3(0f, 3f, 0f);
return "ball reset";
EOF
    run_uloop execute-dynamic-code --code-file "$DYNAMIC_CODE_FILE" > /dev/null
}

toggle_enabled() {
    OBJECT_NAME="$1"
    COMPONENT_TYPE="$2"
    cat > "$DYNAMIC_CODE_FILE" <<EOF
using UnityEngine;
using io.github.hatayama.UnityCliLoop.RegressionHarness;

GameObject target = GameObject.Find("$OBJECT_NAME");
$COMPONENT_TYPE component = target.GetComponent<$COMPONENT_TYPE>();
component.enabled = false;
component.enabled = true;
return "toggled";
EOF
    run_uloop execute-dynamic-code --code-file "$DYNAMIC_CODE_FILE" > /dev/null
}

# Waits for the ball to make contact and settle, then arms a pause point and reports whether the
# pre-existing instance missed it. If it missed, strictly asserts the enabled-toggle workaround
# fixes it. If it did not miss, logs that this run did not reproduce the trap (expected in an
# environment where the trigger condition does not apply) and returns without asserting further.
verify_conditional_gap_and_workaround() {
    LABEL="$1"
    MARKER_FILE="$2"
    MARKER_LINE="$3"
    OBJECT_NAME="$4"
    COMPONENT_TYPE="$5"
    MARKER_ID="${MARKER_FILE}:${MARKER_LINE}"

    log "[$LABEL] Arming pause-point on the pre-existing instance..."
    run_uloop enable-pause-point --file "$MARKER_FILE" --line "$MARKER_LINE" --timeout-seconds 30 > /dev/null

    sleep 3
    run_uloop pause-point-status --id "$MARKER_ID" > "$RESULT_FILE"
    BASELINE_IS_HIT="$(jq -r '.IsHit' "$RESULT_FILE")"

    if [ "$BASELINE_IS_HIT" = "true" ]; then
        log "[$LABEL] PASS (trap did not reproduce in this environment; environment-dependent, see PR #1922): the pre-existing instance was hit on arming without needing the enabled-toggle workaround."
        run_uloop clear-pause-point --all > /dev/null
        return 0
    fi
    log "[$LABEL] Baseline miss reproduced (IsHit=false) -- asserting the enabled-toggle workaround strictly."

    log "[$LABEL] Toggling enabled off/on and re-triggering contact..."
    toggle_enabled "$OBJECT_NAME" "$COMPONENT_TYPE"
    reset_ball

    sleep 3
    run_uloop pause-point-status --id "$MARKER_ID" > "$RESULT_FILE"
    WORKAROUND_IS_HIT="$(jq -r '.IsHit' "$RESULT_FILE")"

    if [ "$WORKAROUND_IS_HIT" != "true" ]; then
        log "[$LABEL] FAIL: reproduced the baseline miss but the enabled-toggle workaround did not make the pause point hit (IsHit=$WORKAROUND_IS_HIT)"
        cat "$RESULT_FILE"
        exit 1
    fi
    log "[$LABEL] PASS: reproduced the baseline miss and the enabled-toggle workaround resolved it."

    run_uloop clear-pause-point --all > /dev/null
}

log "Stopping any existing Play Mode session..."
run_uloop control-play-mode --action Stop > /dev/null

log "Clearing any existing pause-point markers..."
run_uloop clear-pause-point --all > /dev/null

force_clean_domain

log "Starting Play Mode (Floor/TriggerZone/Ball already exist before any pause point is armed)..."
run_uloop control-play-mode --action Play > /dev/null

log "[indirect] Priming Floor with one contact before the indirect callee is armed..."
sleep 3

verify_conditional_gap_and_workaround "direct" "$FLOOR_FILE" "$FLOOR_DIRECT_LINE" "Floor" "PhysicsCallbackFloor"
verify_conditional_gap_and_workaround "indirect" "$FLOOR_FILE" "$FLOOR_INDIRECT_LINE" "Floor" "PhysicsCallbackFloor"
reset_ball
verify_conditional_gap_and_workaround "trigger" "$TRIGGER_FILE" "$TRIGGER_LINE" "TriggerZone" "PhysicsCallbackTriggerZone"

log "PASS: direct, indirect, and trigger physics-callback scenarios all handled (reproduced-and-fixed, or did-not-reproduce)."
exit 0
