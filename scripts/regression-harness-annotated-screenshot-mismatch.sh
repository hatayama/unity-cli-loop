#!/bin/sh
set -e
# Regression harness for annotated screenshot element dropouts and coordinate mismatch:
# S1/S2 pin by-design exclusions (no GraphicRaycaster / unresolved world-space camera).
# S3 asserts a center-blocked but still-clickable button stays in AnnotatedElements and
# that SimX/SimY click the button itself.
# S4 records SiblingIndex label-order vs actual draw order (documented limitation, INFO only).
# S5 pins ElementsOnly SimY/bounds against a full rendering capture (tolerance ±1).
# See docs/regression-harness.md.
#
# Usage: sh scripts/regression-harness-annotated-screenshot-mismatch.sh [--project-path <path>]
#
# Prerequisites:
#   - Assets/RegressionHarness/AnnotatedScreenshotMismatch/AnnotatedScreenshotMismatch.unity
#     must be open in a running Unity Editor
#   - uloop CLI must be installed (for local checkout validation, put dist/<platform> on PATH
#     so `uloop` resolves to the development binary rather than a released dispatcher)
#   - jq must be installed

PROJECT_PATH=""
if [ "$#" -eq 0 ]; then
    :
elif [ "$#" -eq 2 ] && [ "$1" = "--project-path" ] && [ -n "$2" ]; then
    PROJECT_PATH="$2"
else
    printf '%s\n' "Usage: $0 [--project-path <path>]" >&2
    exit 2
fi

CAPTURE_FILE="$(mktemp)"
ELEMENTS_ONLY_FILE="$(mktemp)"
CLICK_FILE="$(mktemp)"
FAILED=0

run_uloop() {
    if [ -n "$PROJECT_PATH" ]; then
        uloop "$@" --project-path "$PROJECT_PATH"
    else
        uloop "$@"
    fi
}

log() {
    printf "\033[36m[annotated-screenshot-mismatch]\033[0m %s\n" "$1"
}

cleanup() {
    rm -f "$CAPTURE_FILE" "$ELEMENTS_ONLY_FILE" "$CLICK_FILE"
}

trap cleanup EXIT

element_json() {
    FILE="$1"
    NAME="$2"
    jq -c --arg name "$NAME" '
        (.Screenshots[0].AnnotatedElements // [])
        | map(select(.Name == $name))
        | .[0] // empty
    ' "$FILE"
}

element_label() {
    FILE="$1"
    NAME="$2"
    JSON="$(element_json "$FILE" "$NAME")"
    if [ -z "$JSON" ]; then
        printf '%s\n' ""
        return 0
    fi
    printf '%s\n' "$JSON" | jq -r '.Label // empty'
}

abs_diff_ok() {
    jq -n --argjson a "$1" --argjson b "$2" '
        (($a - $b) | if . < 0 then -. else . end) <= 1
    '
}

log "Entering Play Mode (already-playing is treated as success)..."
run_uloop control-play-mode --action Play >/dev/null

log "Capturing rendering screenshot with --annotate-elements..."
run_uloop screenshot --capture-mode rendering --annotate-elements >"$CAPTURE_FILE"

NO_RAYCASTER="$(element_json "$CAPTURE_FILE" "Button_NoRaycaster")"
if [ -z "$NO_RAYCASTER" ]; then
    log "S1: PASS Button_NoRaycaster is absent (by-design GraphicRaycaster exclusion)"
else
    log "S1: FAIL Button_NoRaycaster was listed in AnnotatedElements"
    FAILED=1
fi

WORLD_NO_CAMERA="$(element_json "$CAPTURE_FILE" "Button_WorldNoCamera")"
if [ -z "$WORLD_NO_CAMERA" ]; then
    log "S2: PASS Button_WorldNoCamera is absent (by-design unresolved camera exclusion)"
else
    log "S2: FAIL Button_WorldNoCamera was listed in AnnotatedElements"
    FAILED=1
fi

CENTER_BLOCKED="$(element_json "$CAPTURE_FILE" "Button_CenterBlocked")"
if [ -z "$CENTER_BLOCKED" ]; then
    log "S3: FAIL Button_CenterBlocked is missing from AnnotatedElements"
    FAILED=1
    S3_PRESENT=0
else
    SIM_X="$(printf '%s\n' "$CENTER_BLOCKED" | jq -r '.SimX')"
    SIM_Y="$(printf '%s\n' "$CENTER_BLOCKED" | jq -r '.SimY')"
    log "Clicking Button_CenterBlocked at SimX=$SIM_X SimY=$SIM_Y..."
    run_uloop simulate-mouse-ui --action Click --x "$SIM_X" --y "$SIM_Y" >"$CLICK_FILE"
    HIT_NAME="$(jq -r '.HitGameObjectName // empty' "$CLICK_FILE")"
    if [ "$HIT_NAME" = "Button_CenterBlocked" ]; then
        log "S3: PASS Button_CenterBlocked is listed and SimX/SimY click hits Button_CenterBlocked"
        S3_PRESENT=1
    else
        log "S3: FAIL click at SimX=$SIM_X SimY=$SIM_Y hit '$HIT_NAME' (expected Button_CenterBlocked)"
        FAILED=1
        S3_PRESENT=1
    fi
fi

DEEP_FRONT_LABEL="$(element_label "$CAPTURE_FILE" "Button_DeepFront")"
SHALLOW_BACK_LABEL="$(element_label "$CAPTURE_FILE" "Button_ShallowBack")"
log "S4: INFO label-order deepFront=${DEEP_FRONT_LABEL} shallowBack=${SHALLOW_BACK_LABEL} (documented limitation)"

if [ "${S3_PRESENT:-0}" -ne 1 ]; then
    log "S5: SKIP Button_CenterBlocked absent; ElementsOnly coordinate compare is not applicable"
else
    log "Capturing --elements-only rendering annotation..."
    run_uloop screenshot --capture-mode rendering --annotate-elements --elements-only >"$ELEMENTS_ONLY_FILE"
    ELEMENTS_ONLY_CENTER="$(element_json "$ELEMENTS_ONLY_FILE" "Button_CenterBlocked")"
    if [ -z "$ELEMENTS_ONLY_CENTER" ]; then
        log "S5: FAIL Button_CenterBlocked missing from ElementsOnly AnnotatedElements"
        FAILED=1
    else
        FULL_SIM_Y="$(printf '%s\n' "$CENTER_BLOCKED" | jq -r '.SimY')"
        FULL_MIN_Y="$(printf '%s\n' "$CENTER_BLOCKED" | jq -r '.BoundsMinY')"
        FULL_MAX_Y="$(printf '%s\n' "$CENTER_BLOCKED" | jq -r '.BoundsMaxY')"
        ONLY_SIM_Y="$(printf '%s\n' "$ELEMENTS_ONLY_CENTER" | jq -r '.SimY')"
        ONLY_MIN_Y="$(printf '%s\n' "$ELEMENTS_ONLY_CENTER" | jq -r '.BoundsMinY')"
        ONLY_MAX_Y="$(printf '%s\n' "$ELEMENTS_ONLY_CENTER" | jq -r '.BoundsMaxY')"
        if [ "$(abs_diff_ok "$FULL_SIM_Y" "$ONLY_SIM_Y")" = "true" ] \
            && [ "$(abs_diff_ok "$FULL_MIN_Y" "$ONLY_MIN_Y")" = "true" ] \
            && [ "$(abs_diff_ok "$FULL_MAX_Y" "$ONLY_MAX_Y")" = "true" ]; then
            log "S5: PASS ElementsOnly SimY/BoundsMinY/BoundsMaxY match the full capture within ±1"
        else
            log "S5: FAIL ElementsOnly coordinates differ (full SimY=$FULL_SIM_Y [$FULL_MIN_Y,$FULL_MAX_Y] vs only SimY=$ONLY_SIM_Y [$ONLY_MIN_Y,$ONLY_MAX_Y])"
            FAILED=1
        fi
    fi
fi

if [ "$FAILED" -ne 0 ]; then
    log "FAIL: one or more scenarios failed"
    exit 1
fi

log "PASS: S1/S2/S3/S5 passed (S4 is INFO only)"
exit 0
