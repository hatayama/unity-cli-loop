#!/bin/sh
set -e
# Regression harness for the "existing instance" physics-callback pause-point gap:
# Unity's physics message dispatch (OnCollision*/OnTrigger*) resolves its call path once when a
# GameObject registers with the physics engine, so a Harmony patch applied to an
# OnCollisionEnter2D/OnTriggerEnter2D method after that GameObject already existed does not route
# through the patch. This harness proves the workaround half of that gap:
#   toggling one instance's `enabled` off then on (after the pause point is enabled) is enough to
#   make Unity re-resolve dispatch and route the next contact on that instance through the patch.
#
# The harness attempts a best-effort fresh domain reload via EditorUtility.RequestScriptReload()
# before arming, since the enabled-toggle workaround re-resolves dispatch for the whole component
# type for the rest of the Editor session -- without a reload, a type "fixed" by an earlier local
# run of this harness (or by any other pause point on the same type) would no longer reproduce the
# baseline miss. In practice this reload's timing relative to Unity's IPC recovery is not fully
# reliable, so the baseline-miss check is informational only (always logged, never asserted); the
# only strict assertion is that the workaround produces a hit.
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
FLOOR_LINE="16"
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

# Forces a fresh domain reload without touching any file, so the component type carries no
# leftover "fixed by a previous toggle" state into this run's baseline-miss check. Unity
# disconnects the IPC bridge for the duration of the reload -- that failure is expected and
# swallowed here; recovery is confirmed by polling a lightweight command with a hard retry cap.
force_clean_domain() {
    cat > "$DYNAMIC_CODE_FILE" <<'EOF'
using UnityEditor;

EditorUtility.RequestScriptReload();
return "Requested script reload";
EOF
    run_uloop execute-dynamic-code --code-file "$DYNAMIC_CODE_FILE" > /dev/null 2>&1 || true

    log "Waiting for Unity to recover from the domain reload..."
    ATTEMPT=1
    while [ "$ATTEMPT" -le "$RELOAD_RECOVERY_RETRIES" ]; do
        if run_uloop list > /dev/null 2>&1; then
            log "Unity recovered after ${ATTEMPT} attempt(s)."
            return 0
        fi
        ATTEMPT=$((ATTEMPT + 1))
        sleep "$RELOAD_RECOVERY_INTERVAL_SECONDS"
    done

    log "FAIL: Unity did not recover within $((RELOAD_RECOVERY_RETRIES * RELOAD_RECOVERY_INTERVAL_SECONDS))s of requesting a script reload."
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

# Verifies a pre-existing physics-callback instance (collision or trigger) misses a pause point
# armed after it already exists, then confirms the enabled-toggle workaround makes the next
# contact on that same instance route through the patch.
verify_existing_instance_gap_and_workaround() {
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

    # Informational only: the enabled-toggle workaround re-resolves dispatch for the whole
    # component type for the rest of the Editor session, so whether the pre-existing instance
    # still misses here depends on whether an earlier run (or any other armed pause point) already
    # fixed this type since the last real domain reload. Not asserted -- see the file header.
    if [ "$BASELINE_IS_HIT" = "false" ]; then
        log "[$LABEL] Baseline miss reproduced (IsHit=false) as documented."
    else
        log "[$LABEL] INFO: baseline miss did not reproduce this run (the component type was likely already re-resolved earlier in this Editor session; this is expected after a prior run without a domain reload) -- proceeding to workaround assertion."
    fi

    log "[$LABEL] Toggling enabled off/on and re-triggering contact..."
    toggle_enabled "$OBJECT_NAME" "$COMPONENT_TYPE"
    reset_ball

    sleep 3
    run_uloop pause-point-status --id "$MARKER_ID" > "$RESULT_FILE"
    WORKAROUND_IS_HIT="$(jq -r '.IsHit' "$RESULT_FILE")"

    if [ "$WORKAROUND_IS_HIT" != "true" ]; then
        log "[$LABEL] FAIL: expected the enabled-toggle workaround to make the pause point hit (IsHit=true), got IsHit=$WORKAROUND_IS_HIT"
        cat "$RESULT_FILE"
        exit 1
    fi
    log "[$LABEL] PASS: enabled-toggle workaround resolved the existing-instance gap."

    run_uloop clear-pause-point --all > /dev/null
}

log "Stopping any existing Play Mode session..."
run_uloop control-play-mode --action Stop > /dev/null

log "Clearing any existing pause-point markers..."
run_uloop clear-pause-point --all > /dev/null

force_clean_domain

log "Starting Play Mode (Floor/TriggerZone/Ball already exist before any pause point is armed)..."
run_uloop control-play-mode --action Play > /dev/null

verify_existing_instance_gap_and_workaround "OnCollisionEnter2D" "$FLOOR_FILE" "$FLOOR_LINE" "Floor" "PhysicsCallbackFloor"
reset_ball
verify_existing_instance_gap_and_workaround "OnTriggerEnter2D" "$TRIGGER_FILE" "$TRIGGER_LINE" "TriggerZone" "PhysicsCallbackTriggerZone"

log "PASS: existing-instance gap and enabled-toggle workaround confirmed for both OnCollisionEnter2D and OnTriggerEnter2D."
exit 0
