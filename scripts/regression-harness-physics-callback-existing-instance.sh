#!/bin/sh
set -e
# Regression harness for the "existing instance" physics-callback pause-point gap:
# Unity's physics message dispatch (OnCollision*/OnTrigger*) has been observed in real projects to
# bypass a Harmony patch applied while the GameObject already existed, so the pause point can
# silently miss even though the method body runs. The trigger condition is environment-dependent
# and has NOT been reproduced deterministically here (see docs/regression-harness.md for the list
# of ruled-out hypotheses).
#
# A "miss" is only valid evidence if a fresh contact is actually triggered after arming and the
# component's own hit counter proves the method body ran. Each scenario therefore: arms the pause
# point, reads the counter, triggers a fresh contact via reset_ball, reads the counter again, and
# reads IsHit. Three outcomes:
#   - IsHit=true                                  -> PASS, did not reproduce in this environment
#   - counter incremented AND IsHit=false          -> genuine miss (the method body ran but the
#     pause point never hit); this is a rare capture event worth surfacing loudly, so the harness
#     logs diagnostics and exits 1. An enabled-toggle probe then runs informationally (logged, not
#     asserted) since it is not a validated fix -- see
#     SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning.
#   - counter did NOT increment AND IsHit=false    -> harness self-failure: reset_ball did not
#     produce a fresh contact, so this run never exercised the scenario at all
#
# It covers three call shapes:
#   1. direct   -- pause point on the physics message method itself (OnCollisionEnter2D)
#   2. indirect -- pause point on a method called one hop deep from the physics message method,
#      with the instance primed (one prior contact before arming) to match the dominant pattern
#      reported from real games, where the marker sits in a helper method rather than the callback
#   3. trigger  -- pause point on OnTriggerEnter2D, a separate Unity dispatch path from OnCollision*
#
# The harness forces a fresh domain reload via EditorUtility.RequestScriptReload() before arming,
# so a leftover "resolved by a previous run's informational toggle probe" state does not carry into
# this run's counter/IsHit checks. RequestScriptReload queues the reload for a later editor update
# rather than running it inline, so polling for bare IPC responsiveness is not sufficient -- a
# still-loading-the-reload domain and the still-alive pre-reload domain both answer "list"
# successfully. Reload completion is instead detected via HarnessDomainMarker.Id, a static readonly
# value that is re-initialized only when the AppDomain actually reloads; the harness waits until
# this value changes.
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

# Reads one of the harness component's own hit counters (not the pause-point's own hit state) --
# this is the proof that a physics callback's method body actually ran, independent of whether the
# pause point captured it.
read_counter() {
    OBJECT_NAME="$1"
    COMPONENT_TYPE="$2"
    FIELD_NAME="$3"
    cat > "$DYNAMIC_CODE_FILE" <<EOF
using UnityEngine;
using io.github.hatayama.UnityCliLoop.RegressionHarness;

GameObject target = GameObject.Find("$OBJECT_NAME");
$COMPONENT_TYPE component = target.GetComponent<$COMPONENT_TYPE>();
return component.$FIELD_NAME.ToString();
EOF
    run_uloop execute-dynamic-code --code-file "$DYNAMIC_CODE_FILE" 2>/dev/null | jq -r '.Result // empty'
}

# Forces a fresh domain reload without touching any file, so this run's counter/IsHit checks are
# not contaminated by a "resolved by a previous run's informational toggle probe" state. Unity
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

# Informational-only probe: this workaround was investigated and found to not be validated (every
# local "it fixed the miss" observation turned out to be a harness false positive, see
# SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning). It is still run and
# logged on a genuine miss so a real recurrence carries this data point, but its result is never
# asserted.
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

# Arms a pause point, triggers a fresh contact (reset_ball) after arming, and classifies the
# result from the component's own hit counter plus IsHit. See the file header for the three
# possible outcomes; only "harness self-failure" and "genuine miss" exit non-zero.
verify_gap() {
    LABEL="$1"
    MARKER_FILE="$2"
    MARKER_LINE="$3"
    OBJECT_NAME="$4"
    COMPONENT_TYPE="$5"
    COUNTER_FIELD="$6"
    MARKER_ID="${MARKER_FILE}:${MARKER_LINE}"

    log "[$LABEL] Arming pause-point on the pre-existing instance..."
    run_uloop enable-pause-point --file "$MARKER_FILE" --line "$MARKER_LINE" --timeout-seconds 30 > /dev/null

    COUNTER_BEFORE="$(read_counter "$OBJECT_NAME" "$COMPONENT_TYPE" "$COUNTER_FIELD")"
    if [ -z "$COUNTER_BEFORE" ]; then
        log "[$LABEL] FAIL: could not read $COMPONENT_TYPE.$COUNTER_FIELD on $OBJECT_NAME before triggering a fresh contact."
        exit 1
    fi

    log "[$LABEL] Triggering a fresh contact (reset_ball) now that the pause point is armed..."
    reset_ball
    sleep 3

    COUNTER_AFTER="$(read_counter "$OBJECT_NAME" "$COMPONENT_TYPE" "$COUNTER_FIELD")"
    run_uloop pause-point-status --id "$MARKER_ID" > "$RESULT_FILE"
    IS_HIT="$(jq -r '.IsHit' "$RESULT_FILE")"

    if [ "$IS_HIT" = "true" ]; then
        log "[$LABEL] PASS (did not reproduce in this environment; environment-dependent, see PR #1922): the fresh contact was captured by the pause point ($COUNTER_FIELD $COUNTER_BEFORE -> $COUNTER_AFTER)."
        run_uloop clear-pause-point --all > /dev/null
        return 0
    fi

    if [ -n "$COUNTER_AFTER" ] && [ "$COUNTER_AFTER" -gt "$COUNTER_BEFORE" ] 2>/dev/null; then
        log "[$LABEL] FAIL: genuine miss captured -- the method body ran ($COUNTER_FIELD $COUNTER_BEFORE -> $COUNTER_AFTER) but the pause point never hit. Investigate; see PR #1922."
        cat "$RESULT_FILE"
        log "[$LABEL] Running the enabled-toggle probe informationally (not asserted, not a validated fix)..."
        toggle_enabled "$OBJECT_NAME" "$COMPONENT_TYPE"
        reset_ball
        sleep 3
        run_uloop pause-point-status --id "$MARKER_ID" > "$RESULT_FILE"
        PROBE_IS_HIT="$(jq -r '.IsHit' "$RESULT_FILE")"
        log "[$LABEL] Toggle probe result (informational only): IsHit=$PROBE_IS_HIT"
        run_uloop clear-pause-point --all > /dev/null
        exit 1
    fi

    log "[$LABEL] FAIL: harness self-failure -- reset_ball did not produce a fresh contact ($COUNTER_FIELD stayed at $COUNTER_BEFORE); this scenario never actually exercised the pause point."
    run_uloop clear-pause-point --all > /dev/null
    exit 1
}

log "Stopping any existing Play Mode session..."
run_uloop control-play-mode --action Stop > /dev/null

log "Clearing any existing pause-point markers..."
run_uloop clear-pause-point --all > /dev/null

force_clean_domain

log "Starting Play Mode (Floor/TriggerZone/Ball already exist before any pause point is armed)..."
run_uloop control-play-mode --action Play > /dev/null

log "Waiting for the ball's initial fall to land on Floor (this priming contact seeds both HitCount and IndirectHitCount before any pause point is armed)..."
sleep 3
INITIAL_HIT_COUNT="$(read_counter "Floor" "PhysicsCallbackFloor" "HitCount")"
INITIAL_INDIRECT_COUNT="$(read_counter "Floor" "PhysicsCallbackFloor" "IndirectHitCount")"
if [ -z "$INITIAL_HIT_COUNT" ] || [ "$INITIAL_HIT_COUNT" -lt 1 ] 2>/dev/null; then
    log "FAIL: the ball did not make initial contact with Floor before arming (HitCount=$INITIAL_HIT_COUNT)."
    exit 1
fi
if [ -z "$INITIAL_INDIRECT_COUNT" ] || [ "$INITIAL_INDIRECT_COUNT" -lt 1 ] 2>/dev/null; then
    log "FAIL: the indirect callee was not primed before arming (IndirectHitCount=$INITIAL_INDIRECT_COUNT)."
    exit 1
fi
log "Priming confirmed (HitCount=$INITIAL_HIT_COUNT, IndirectHitCount=$INITIAL_INDIRECT_COUNT)."

verify_gap "direct" "$FLOOR_FILE" "$FLOOR_DIRECT_LINE" "Floor" "PhysicsCallbackFloor" "HitCount"
verify_gap "indirect" "$FLOOR_FILE" "$FLOOR_INDIRECT_LINE" "Floor" "PhysicsCallbackFloor" "IndirectHitCount"
verify_gap "trigger" "$TRIGGER_FILE" "$TRIGGER_LINE" "TriggerZone" "PhysicsCallbackTriggerZone" "HitCount"

log "PASS: direct, indirect, and trigger physics-callback scenarios all did not reproduce a genuine miss."
exit 0
