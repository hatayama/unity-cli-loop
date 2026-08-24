#!/bin/sh
set -e
# Regression harness for pause-point capture on a hot-reload patched method:
# (A) compile-time body: enable-pause-point on ApplyMotion must capture post-update
#     locals (step>0, motion non-zero) and the field after the gravity line.
# (B) after a method-body literal hot-reload and re-arm, the same marker must
#     retarget (RetargetedToHotReloadPatch=true) and capture equivalent values.
# A capture at method entry instead shows step=0, motion=(0,0,0), field=-1.
# See docs/regression-harness.md.
#
# Usage: sh scripts/regression-harness-patched-method-pause-point-capture.sh [--project-path <path>]
#
# Prerequisites:
#   - A running Unity Editor for this project
#   - dist/<platform>/uloop must be built (this checkout's development binary)
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

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
# Why no Linux arm: scripts/build-go-cli.sh only emits darwin-arm64, darwin-amd64, and
# windows-amd64 — advertising dist/linux-* would be an unreachable fiction.
case "$(uname -s)" in
    Darwin)
        case "$(uname -m)" in
            arm64) ULOOP_BIN="$REPO_ROOT/dist/darwin-arm64/uloop" ;;
            *) ULOOP_BIN="$REPO_ROOT/dist/darwin-amd64/uloop" ;;
        esac
        ;;
    MINGW*|MSYS*|CYGWIN*)
        ULOOP_BIN="$REPO_ROOT/dist/windows-amd64/uloop.exe"
        ;;
    *)
        printf '%s\n' "Unsupported platform: $(uname -s)" >&2
        exit 2
        ;;
esac

if [ ! -x "$ULOOP_BIN" ]; then
    printf '%s\n' "uloop binary not found or not executable: $ULOOP_BIN" >&2
    printf '%s\n' "Build with scripts/build-go-cli.sh first." >&2
    exit 2
fi

if [ -n "$PROJECT_PATH" ]; then
    EFFECTIVE_ROOT=$(CDPATH= cd -- "$PROJECT_PATH" && pwd)
else
    EFFECTIVE_ROOT="$REPO_ROOT"
fi

SOURCE_FILE="Assets/RegressionHarness/PatchedMethodPausePointCapture/PatchedMethodPausePointCaptureTarget.cs"
SCENE_FILE="Assets/RegressionHarness/PatchedMethodPausePointCapture/PatchedMethodPausePointCapture.unity"
SOURCE_ABS="$EFFECTIVE_ROOT/$SOURCE_FILE"
if [ ! -f "$SOURCE_ABS" ]; then
    printf '%s\n' "Harness source not found: $SOURCE_ABS" >&2
    exit 2
fi

OLD_LITERAL="1.000f"
NEW_LITERAL="2.000f"
MARKER_LINE=$(grep -n 'body.Move(motion \* step);' "$SOURCE_ABS" | head -n 1 | cut -d: -f1)
if [ -z "$MARKER_LINE" ]; then
    printf '%s\n' "Could not find body.Move call in $SOURCE_ABS" >&2
    exit 2
fi

if ! grep -F -q "$OLD_LITERAL" "$SOURCE_ABS"; then
    printf '%s\n' "Harness source is not pristine (expected ${OLD_LITERAL}): $SOURCE_ABS" >&2
    printf '%s\n' "Restore it (git restore ${SOURCE_FILE}) before running." >&2
    exit 2
fi

RESULT_FILE="$(mktemp)"
SOURCE_BACKUP="$(mktemp)"
AUTO_REFRESH_DISALLOWED="0"
SOURCE_DIRTY="0"
CLEANED_UP="0"

cp "$SOURCE_ABS" "$SOURCE_BACKUP"

run_uloop() {
    "$ULOOP_BIN" "$@" --project-path "$EFFECTIVE_ROOT"
}

log() {
    printf "\033[36m[patched-method-pause-point-capture]\033[0m %s\n" "$1"
}

restore_source() {
    if [ "$SOURCE_DIRTY" = "1" ]; then
        cp "$SOURCE_BACKUP" "$SOURCE_ABS"
        SOURCE_DIRTY="0"
        log "Restored $SOURCE_FILE from backup."
    fi
}

allow_auto_refresh() {
    if [ "$AUTO_REFRESH_DISALLOWED" = "1" ]; then
        if ! run_uloop execute-dynamic-code --code "
using UnityEditor;
AssetDatabase.AllowAutoRefresh();
return \"AllowAutoRefresh\";
" > /dev/null 2>&1; then
            log "WARN: AllowAutoRefresh failed; re-enable auto refresh in the Editor manually."
        fi
        AUTO_REFRESH_DISALLOWED="0"
    fi
}

cleanup() {
    if [ "$CLEANED_UP" = "1" ]; then
        return
    fi
    CLEANED_UP="1"
    restore_source
    run_uloop clear-pause-point --all > /dev/null 2>&1 || true
    run_uloop hot-reload --revert-all > /dev/null 2>&1 || true
    allow_auto_refresh
    run_uloop control-play-mode --action Stop > /dev/null 2>&1 || true
    rm -f "$RESULT_FILE" "$SOURCE_BACKUP" "${SOURCE_ABS}.tmp"
}
trap cleanup EXIT
trap 'cleanup; exit 130' INT
trap 'cleanup; exit 143' TERM
trap 'cleanup; exit 129' HUP

disallow_auto_refresh() {
    run_uloop execute-dynamic-code --code "
using UnityEditor;
AssetDatabase.DisallowAutoRefresh();
return \"DisallowAutoRefresh\";
" > /dev/null
    AUTO_REFRESH_DISALLOWED="1"
}

open_harness_scene() {
    run_uloop execute-dynamic-code --code "
using UnityEditor.SceneManagement;
EditorSceneManager.OpenScene(\"$SCENE_FILE\");
return \"opened\";
" > /dev/null
}

captured_value() {
    name="$1"
    jq -r --arg name "$name" '
        .CapturedVariables[]
        | select(.Name == $name)
        | .Value
    ' "$RESULT_FILE" | head -n 1
}

assert_good_capture() {
    label="$1"
    step="$(captured_value step)"
    motion="$(captured_value motion)"
    vertical="$(captured_value verticalVelocity)"
    log "${label}: step=${step} motion=${motion} verticalVelocity=${vertical}"
    # Why tonumber: a glob such as 0.00* also matches a real small deltaTime
    # (0.006…) and would treat a post-assignment step as the method-entry zero.
    step_positive="$(jq -n --arg v "$step" 'try ($v | tonumber) catch 0 | . > 0')"
    if [ "$step_positive" != "true" ]; then
        log "FAIL: ${label} step is default/zero (${step}) — capture looks like method entry"
        cat "$RESULT_FILE"
        return 1
    fi
    case "$motion" in
        ""|null|"(0, 0, 0)"|"(0.0, 0.0, 0.0)")
            log "FAIL: ${label} motion is default/zero (${motion})"
            cat "$RESULT_FILE"
            return 1
            ;;
    esac
    case "$vertical" in
        ""|null|-1|-1.0)
            log "FAIL: ${label} verticalVelocity is still the pre-update sentinel (${vertical})"
            cat "$RESULT_FILE"
            return 1
            ;;
    esac
    return 0
}

await_capture() {
    label="$1"
    expect_retarget="$2"
    run_uloop enable-pause-point \
        --file "$SOURCE_FILE" \
        --line "$MARKER_LINE" \
        --method ApplyMove \
        --timeout-seconds 30 \
        --await \
        --captured-variables full \
        --captured-variable-names "step,motion,verticalVelocity" \
        > "$RESULT_FILE"
    success="$(jq -r '.Success' "$RESULT_FILE")"
    is_hit="$(jq -r '.IsHit // .Status' "$RESULT_FILE")"
    retargeted="$(jq -r '.RetargetedToHotReloadPatch // false' "$RESULT_FILE")"
    log "${label} enable: Success=${success} Status/IsHit=${is_hit} RetargetedToHotReloadPatch=${retargeted}"
    if [ "$success" != "true" ]; then
        log "FAIL: ${label} enable-pause-point did not succeed"
        cat "$RESULT_FILE"
        return 1
    fi
    if [ "$expect_retarget" = "1" ] && [ "$retargeted" != "true" ]; then
        log "FAIL: ${label} expected RetargetedToHotReloadPatch=true"
        cat "$RESULT_FILE"
        return 1
    fi
    assert_good_capture "$label"
}

log "Disallowing AssetDatabase auto-refresh for the sed window..."
disallow_auto_refresh

log "Opening harness scene..."
open_harness_scene

log "Stopping any existing Play Mode session..."
run_uloop control-play-mode --action Stop > /dev/null
run_uloop clear-pause-point --all > /dev/null

log "Starting Play Mode..."
run_uloop control-play-mode --action Play > /dev/null

log "(A) compile-time capture on ${SOURCE_FILE}:${MARKER_LINE}..."
await_capture "A" "0"
run_uloop clear-pause-point --all > /dev/null
run_uloop control-play-mode --action Resume > /dev/null 2>&1 || true

log "Sed-editing in-body literal ${OLD_LITERAL} -> ${NEW_LITERAL}..."
SOURCE_DIRTY="1"
sed "s/${OLD_LITERAL}/${NEW_LITERAL}/" "$SOURCE_ABS" > "${SOURCE_ABS}.tmp"
mv "${SOURCE_ABS}.tmp" "$SOURCE_ABS"
if ! grep -F -q "$NEW_LITERAL" "$SOURCE_ABS"; then
    log "FAIL: sed did not rewrite literal to ${NEW_LITERAL}"
    exit 1
fi

log "Applying hot-reload to $SOURCE_FILE..."
run_uloop hot-reload --files "$SOURCE_FILE" > "$RESULT_FILE"
success="$(jq -r '.Success' "$RESULT_FILE")"
patched_total="$(jq -r '.PatchedTotal // 0' "$RESULT_FILE")"
if [ "$success" != "true" ] || [ "$patched_total" -lt 1 ]; then
    log "FAIL: expected Success=true and PatchedTotal>=1"
    cat "$RESULT_FILE"
    exit 1
fi
log "hot-reload applied (PatchedTotal=${patched_total})."

log "(B) re-arm after hot-reload on ${SOURCE_FILE}:${MARKER_LINE}..."
await_capture "B" "1"

log "PASS: compile-time and hot-reload-patched captures both look post-line."
exit 0
