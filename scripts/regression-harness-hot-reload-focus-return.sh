#!/bin/sh
set -e
# Regression harness for hot-reload + focus-return: Play Mode with an active
# patch must survive uloop focus-window. Without HotReloadAutoRefreshHold,
# native Auto Refresh imports the edited .cs, recompiles, and stops Play
# (ScriptCompilationDuringPlay=2). See docs/regression-harness.md.
#
# Usage: sh scripts/regression-harness-hot-reload-focus-return.sh [--project-path <path>]
#
# Prerequisites:
#   - Unity Editor must already be running for this project and unfocused
#   - dist/<platform>/uloop must be built (this checkout's development binary)
#   - jq must be installed
#   - Auto Refresh must be enabled
#   - the harness source must be clean in git

PROJECT_PATH=""
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

SOURCE_FILE="Assets/RegressionHarness/HotReload/HotReloadMarkerLogger.cs"
SOURCE_ABS="$EFFECTIVE_ROOT/$SOURCE_FILE"
OLD_LITERAL="marker=111"
NEW_LITERAL="marker=222"
# Unity default and the value that makes focus-return stop Play on script import.
STOP_PLAYING_AND_RECOMPILE="2"

if [ ! -f "$SOURCE_ABS" ]; then
    printf '%s\n' "Harness source not found: $SOURCE_ABS" >&2
    exit 2
fi

if ! command -v jq >/dev/null 2>&1; then
    printf '%s\n' "jq is required." >&2
    exit 1
fi

if ! grep -q "$OLD_LITERAL" "$SOURCE_ABS"; then
    printf '%s\n' "Harness source is not pristine (expected ${OLD_LITERAL}): $SOURCE_ABS" >&2
    printf '%s\n' "Restore it (git restore ${SOURCE_FILE}) before running." >&2
    exit 2
fi

CODE_FILE="$(mktemp)"
RESULT_FILE="$(mktemp)"
CLEANED_UP="0"
SOURCE_DIRTY="0"
SAVED_SCRIPT_COMPILATION=""

run_uloop() {
    "$ULOOP_BIN" "$@" --project-path "$EFFECTIVE_ROOT"
}

log() {
    printf "\033[36m[hot-reload-focus-return]\033[0m %s\n" "$1"
}

cleanup_temps() {
    rm -f "$CODE_FILE" "$RESULT_FILE"
}

restore_source() {
    if [ "$SOURCE_DIRTY" = "1" ]; then
        git -C "$EFFECTIVE_ROOT" checkout -- "$SOURCE_FILE"
        SOURCE_DIRTY="0"
        log "Restored $SOURCE_FILE."
    fi
}

restore_script_compilation() {
    if [ -z "$SAVED_SCRIPT_COMPILATION" ]; then
        return
    fi
    cat > "$CODE_FILE" <<EOF
UnityEditor.EditorPrefs.SetInt("ScriptCompilationDuringPlay", $SAVED_SCRIPT_COMPILATION);
return "restored=" + $SAVED_SCRIPT_COMPILATION;
EOF
    run_uloop execute-dynamic-code --code-file "$CODE_FILE" > /dev/null 2>&1 || true
    SAVED_SCRIPT_COMPILATION=""
}

cleanup() {
    # INT/TERM/HUP handlers also trigger EXIT; skip a second restore pass.
    if [ "$CLEANED_UP" = "1" ]; then
        return
    fi
    CLEANED_UP="1"
    restore_source
    run_uloop hot-reload --revert-all > /dev/null 2>&1 || true
    run_uloop control-play-mode --action Stop > /dev/null 2>&1 || true
    restore_script_compilation
    cleanup_temps
}

trap cleanup_temps EXIT

SOURCE_DIRTY_CHECK="$(git -C "$EFFECTIVE_ROOT" status --porcelain -- "$SOURCE_FILE")"
if [ -n "$SOURCE_DIRTY_CHECK" ]; then
    log "FAIL: $SOURCE_FILE has uncommitted changes. Commit or restore it before running."
    exit 1
fi

cat > "$CODE_FILE" <<'EOF'
return UnityEditor.EditorApplication.isFocused.ToString();
EOF
if ! run_uloop execute-dynamic-code --code-file "$CODE_FILE" > "$RESULT_FILE"; then
    log "FAIL: could not read EditorApplication.isFocused."
    cat "$RESULT_FILE"
    exit 1
fi
IS_FOCUSED="$(jq -r '.Result // empty' "$RESULT_FILE")"
if [ "$IS_FOCUSED" = "True" ]; then
    log "Click another application (for example this terminal) so that the Unity Editor loses focus, then rerun."
    exit 1
fi

# Why both keys: Unity 2020.2+ stores kAutoRefreshMode (0=Disabled); older editors
# used kAutoRefresh. Either form being disabled makes the focus-return repro impossible.
cat > "$CODE_FILE" <<'EOF'
int mode = UnityEditor.EditorPrefs.GetInt("kAutoRefreshMode", -1);
int legacy = UnityEditor.EditorPrefs.GetInt("kAutoRefresh", -1);
bool legacyBool = UnityEditor.EditorPrefs.GetBool("kAutoRefresh", true);
bool disabled;
if (mode >= 0)
{
    disabled = mode == 0;
}
else if (legacy >= 0)
{
    disabled = legacy == 0;
}
else
{
    disabled = !legacyBool;
}
return "disabled=" + disabled.ToString() + " mode=" + mode.ToString() + " legacy=" + legacy.ToString();
EOF
if ! run_uloop execute-dynamic-code --code-file "$CODE_FILE" > "$RESULT_FILE"; then
    log "FAIL: could not read Auto Refresh prefs."
    cat "$RESULT_FILE"
    exit 1
fi
AUTO_REFRESH_STATE="$(jq -r '.Result // empty' "$RESULT_FILE")"
case "$AUTO_REFRESH_STATE" in
    disabled=True*)
        log "FAIL: Auto Refresh is disabled ($AUTO_REFRESH_STATE). Enable it in Preferences, then rerun."
        exit 2
        ;;
esac
log "Auto Refresh precondition ok ($AUTO_REFRESH_STATE)."

cat > "$CODE_FILE" <<'EOF'
return UnityEditor.EditorPrefs.GetInt("ScriptCompilationDuringPlay", 0).ToString();
EOF
if ! run_uloop execute-dynamic-code --code-file "$CODE_FILE" > "$RESULT_FILE"; then
    log "FAIL: could not read ScriptCompilationDuringPlay."
    cat "$RESULT_FILE"
    exit 1
fi
SCRIPT_COMPILATION="$(jq -r '.Result // empty' "$RESULT_FILE")"
if [ "$SCRIPT_COMPILATION" != "$STOP_PLAYING_AND_RECOMPILE" ]; then
    SAVED_SCRIPT_COMPILATION="$SCRIPT_COMPILATION"
    # Why before the write: a failed SetInt or later abort must restore prefs.
    # Earlier preflight exits still use the temp-only EXIT trap.
    trap cleanup EXIT
    trap 'cleanup; exit 130' INT
    trap 'cleanup; exit 143' TERM
    trap 'cleanup; exit 129' HUP
    log "Setting ScriptCompilationDuringPlay from $SCRIPT_COMPILATION to $STOP_PLAYING_AND_RECOMPILE (will restore)."
    cat > "$CODE_FILE" <<EOF
UnityEditor.EditorPrefs.SetInt("ScriptCompilationDuringPlay", $STOP_PLAYING_AND_RECOMPILE);
return UnityEditor.EditorPrefs.GetInt("ScriptCompilationDuringPlay", 0).ToString();
EOF
    if ! run_uloop execute-dynamic-code --code-file "$CODE_FILE" > "$RESULT_FILE"; then
        log "FAIL: could not set ScriptCompilationDuringPlay."
        cat "$RESULT_FILE"
        exit 1
    fi
    APPLIED_SCRIPT_COMPILATION="$(jq -r '.Result // empty' "$RESULT_FILE")"
    if [ "$APPLIED_SCRIPT_COMPILATION" != "$STOP_PLAYING_AND_RECOMPILE" ]; then
        log "FAIL: ScriptCompilationDuringPlay stayed $APPLIED_SCRIPT_COMPILATION."
        exit 1
    fi
else
    log "ScriptCompilationDuringPlay is already $STOP_PLAYING_AND_RECOMPILE."
fi

# EXIT alone misses Ctrl-C / kill and would leave Assets dirty and prefs changed.
trap cleanup EXIT
trap 'cleanup; exit 130' INT
trap 'cleanup; exit 143' TERM
trap 'cleanup; exit 129' HUP

log "Opening the harness scene..."
cat > "$CODE_FILE" <<'EOF'
using UnityEditor.SceneManagement;
EditorSceneManager.OpenScene("Assets/RegressionHarness/HotReload/HotReload.unity");
return "opened";
EOF
run_uloop execute-dynamic-code --code-file "$CODE_FILE" > /dev/null

cat > "$CODE_FILE" <<'EOF'
return UnityEditor.EditorApplication.isFocused.ToString();
EOF
if ! run_uloop execute-dynamic-code --code-file "$CODE_FILE" > "$RESULT_FILE"; then
    log "FAIL: could not recheck EditorApplication.isFocused before Play."
    cat "$RESULT_FILE"
    exit 1
fi
IS_FOCUSED="$(jq -r '.Result // empty' "$RESULT_FILE")"
if [ "$IS_FOCUSED" = "True" ]; then
    log "FAIL: Unity regained focus before Play. Click another application so that the Editor loses focus, then rerun."
    exit 1
fi

log "Stopping any existing Play Mode session..."
run_uloop control-play-mode --action Stop > /dev/null

log "Starting Play Mode..."
if ! run_uloop control-play-mode --action Play > "$RESULT_FILE"; then
    log "FAIL: could not enter Play Mode."
    cat "$RESULT_FILE"
    exit 1
fi
IS_PLAYING="$(jq -r '.IsPlaying | tostring' "$RESULT_FILE")"
if [ "$IS_PLAYING" != "true" ] && [ "$IS_PLAYING" != "True" ]; then
    log "FAIL: Play Mode did not start. IsPlaying=$IS_PLAYING"
    cat "$RESULT_FILE"
    exit 1
fi

cat > "$CODE_FILE" <<'EOF'
return UnityEditor.EditorApplication.isFocused.ToString();
EOF
if ! run_uloop execute-dynamic-code --code-file "$CODE_FILE" > "$RESULT_FILE"; then
    log "FAIL: could not recheck EditorApplication.isFocused before editing the harness source."
    cat "$RESULT_FILE"
    exit 1
fi
IS_FOCUSED="$(jq -r '.Result // empty' "$RESULT_FILE")"
if [ "$IS_FOCUSED" = "True" ]; then
    log "FAIL: Unity regained focus before the source edit. Click another application so that the Editor loses focus, then rerun."
    exit 1
fi

log "Sed-editing in-body literal ${OLD_LITERAL} -> ${NEW_LITERAL} in $SOURCE_FILE..."
SOURCE_DIRTY="1"
# Why temp+mv instead of sed -i: -i is not POSIX, and BSD sed's -i demands a backup suffix.
sed "s/${OLD_LITERAL}/${NEW_LITERAL}/" "$SOURCE_ABS" > "${SOURCE_ABS}.tmp"
mv "${SOURCE_ABS}.tmp" "$SOURCE_ABS"
if ! grep -q "$NEW_LITERAL" "$SOURCE_ABS"; then
    log "FAIL: sed did not rewrite literal to ${NEW_LITERAL}"
    exit 1
fi

log "Applying hot-reload to $SOURCE_FILE..."
if ! run_uloop hot-reload --files "$SOURCE_FILE" > "$RESULT_FILE"; then
    log "FAIL: hot-reload command failed."
    cat "$RESULT_FILE"
    exit 1
fi
SUCCESS="$(jq -r '.Success' "$RESULT_FILE")"
ACTIVE_BEFORE="$(jq -r '.ActivePatchTotal // 0' "$RESULT_FILE")"
HELD_AFTER_APPLY="$(jq -r '.AutoRefreshHeld // "null"' "$RESULT_FILE")"
if [ "$SUCCESS" != "true" ] || [ "$ACTIVE_BEFORE" -lt 1 ]; then
    log "FAIL: expected Success=true and ActivePatchTotal>=1"
    cat "$RESULT_FILE"
    exit 1
fi
log "hot-reload applied (ActivePatchTotal=${ACTIVE_BEFORE}, AutoRefreshHeld=${HELD_AFTER_APPLY})."

log "Returning focus to the Unity Editor..."
run_uloop focus-window > /dev/null || true

# Why wait for reachability first: an unfixed Editor imports the edited .cs on
# focus return, recompiles, and drops the IPC connection. Treating that
# disconnect as "not focused" hides the IsPlaying: false baseline failure.
REACHABLE=""
FOCUSED=""
REACH_START="$(date +%s)"
while :; do
    NOW="$(date +%s)"
    if [ $((NOW - REACH_START)) -ge 60 ]; then
        break
    fi
    cat > "$CODE_FILE" <<'EOF'
return UnityEditor.EditorApplication.isFocused.ToString();
EOF
    if run_uloop execute-dynamic-code --code-file "$CODE_FILE" > "$RESULT_FILE" 2>/dev/null; then
        FOCUSED="$(jq -r '.Result // empty' "$RESULT_FILE")"
        REACHABLE="1"
        if [ "$FOCUSED" = "True" ]; then
            break
        fi
        run_uloop focus-window > /dev/null 2>&1 || true
    fi
    sleep 2
done
if [ "$REACHABLE" != "1" ]; then
    log "FAIL: Unity did not become reachable within 60 seconds after focus-window."
    exit 1
fi

FOCUS_START="$(date +%s)"
while [ "$FOCUSED" != "True" ]; do
    NOW="$(date +%s)"
    if [ $((NOW - FOCUS_START)) -ge 10 ]; then
        break
    fi
    cat > "$CODE_FILE" <<'EOF'
return UnityEditor.EditorApplication.isFocused.ToString();
EOF
    if run_uloop execute-dynamic-code --code-file "$CODE_FILE" > "$RESULT_FILE" 2>/dev/null; then
        FOCUSED="$(jq -r '.Result // empty' "$RESULT_FILE")"
        if [ "$FOCUSED" = "True" ]; then
            break
        fi
    fi
    sleep 1
done
if [ "$FOCUSED" != "True" ]; then
    log "FAIL: Editor is reachable but isFocused is still false after focus-window."
    exit 1
fi

log "Waiting 5 seconds for native Auto Refresh / compile..."
sleep 5

log "Checking Play Mode status after focus return..."
if ! run_uloop control-play-mode --action Status > "$RESULT_FILE"; then
    log "FAIL: could not read Play Mode status."
    cat "$RESULT_FILE"
    exit 1
fi
IS_PLAYING="$(jq -r '.IsPlaying | tostring' "$RESULT_FILE")"
log "control-play-mode Status after focus return:"
cat "$RESULT_FILE"
if [ "$IS_PLAYING" != "true" ] && [ "$IS_PLAYING" != "True" ]; then
    ACTIVE_AFTER_FOCUS="unavailable"
    if run_uloop hot-reload --status > "$RESULT_FILE"; then
        log "hot-reload --status after Play stopped:"
        cat "$RESULT_FILE"
        ACTIVE_AFTER_FOCUS="$(jq -r '.ActivePatchTotal // 0' "$RESULT_FILE")"
    else
        log "WARN: hot-reload --status failed after Play stopped; ledger count unavailable."
        cat "$RESULT_FILE" || true
    fi
    log "FAIL: Play Mode stopped after focus return (IsPlaying: ${IS_PLAYING}, ActivePatchTotal: ${ACTIVE_AFTER_FOCUS}). Auto Refresh imported the edited script."
    exit 1
fi

if ! run_uloop hot-reload --status > "$RESULT_FILE"; then
    log "FAIL: hot-reload --status failed after focus return."
    cat "$RESULT_FILE"
    exit 1
fi
ACTIVE_AFTER_FOCUS="$(jq -r '.ActivePatchTotal // 0' "$RESULT_FILE")"
HELD_AFTER_FOCUS="$(jq -r '.AutoRefreshHeld // "null"' "$RESULT_FILE")"
if [ "$ACTIVE_AFTER_FOCUS" != "$ACTIVE_BEFORE" ]; then
    log "FAIL: ActivePatchTotal changed after focus return (${ACTIVE_BEFORE} -> ${ACTIVE_AFTER_FOCUS})."
    cat "$RESULT_FILE"
    exit 1
fi
if [ "$HELD_AFTER_APPLY" != "true" ]; then
    log "FAIL: AutoRefreshHeld was not true immediately after apply (${HELD_AFTER_APPLY})."
    cat "$RESULT_FILE"
    exit 1
fi
if [ "$HELD_AFTER_FOCUS" != "true" ]; then
    log "FAIL: AutoRefreshHeld was not true after focus return (${HELD_AFTER_FOCUS})."
    cat "$RESULT_FILE"
    exit 1
fi

log "Stopping Play Mode..."
run_uloop control-play-mode --action Stop > /dev/null

if ! run_uloop hot-reload --status > "$RESULT_FILE"; then
    log "FAIL: hot-reload --status failed after Stop."
    cat "$RESULT_FILE"
    exit 1
fi
ACTIVE_AFTER_STOP="$(jq -r '.ActivePatchTotal // 0' "$RESULT_FILE")"
HELD_AFTER_STOP="$(jq -r '.AutoRefreshHeld | tostring' "$RESULT_FILE")"
log "hot-reload --status after Stop (ActivePatchTotal=${ACTIVE_AFTER_STOP}, AutoRefreshHeld=${HELD_AFTER_STOP}):"
cat "$RESULT_FILE"

cat > "$CODE_FILE" <<'EOF'
bool optionsEnabled = UnityEditor.EditorSettings.enterPlayModeOptionsEnabled;
UnityEditor.EnterPlayModeOptions options = UnityEditor.EditorSettings.enterPlayModeOptions;
bool domainReloadDisabled = optionsEnabled && options.HasFlag(UnityEditor.EnterPlayModeOptions.DisableDomainReload);
return "enabled=" + optionsEnabled.ToString() + " disableDomainReload=" + domainReloadDisabled.ToString();
EOF
if ! run_uloop execute-dynamic-code --code-file "$CODE_FILE" > "$RESULT_FILE"; then
    log "FAIL: could not read Enter Play Mode Options."
    cat "$RESULT_FILE"
    exit 1
fi
PLAY_OPTIONS="$(jq -r '.Result // empty' "$RESULT_FILE")"
log "Enter Play Mode Options: $PLAY_OPTIONS"

case "$PLAY_OPTIONS" in
    *disableDomainReload=True*)
        # Why compile instead of expecting 0 here: Disable Domain Reload keeps static
        # ledgers across Stop, so the hold must stay until compile or revert-all.
        if [ "$ACTIVE_AFTER_STOP" = "0" ] || [ "$HELD_AFTER_STOP" != "true" ]; then
            log "FAIL: Disable Domain Reload should keep ActivePatchTotal>=1 and AutoRefreshHeld=true after Stop."
            exit 1
        fi
        ;;
    *)
        if [ "$ACTIVE_AFTER_STOP" != "0" ] || [ "$HELD_AFTER_STOP" != "false" ]; then
            log "FAIL: expected ActivePatchTotal=0 and AutoRefreshHeld=false after Stop with domain reload."
            exit 1
        fi
        ;;
esac

log "Restoring source and compiling..."
restore_source
if ! run_uloop compile --timeout-seconds 120 > "$RESULT_FILE"; then
    log "FAIL: compile did not succeed after restore."
    cat "$RESULT_FILE"
    exit 1
fi
COMPILE_SUCCESS="$(jq -r '.Success | tostring' "$RESULT_FILE")"
if [ "$COMPILE_SUCCESS" != "true" ] && [ "$COMPILE_SUCCESS" != "True" ]; then
    log "FAIL: compile Success was ${COMPILE_SUCCESS}."
    cat "$RESULT_FILE"
    exit 1
fi

if ! run_uloop hot-reload --status > "$RESULT_FILE"; then
    log "FAIL: hot-reload --status failed after compile."
    cat "$RESULT_FILE"
    exit 1
fi
ACTIVE_AFTER_COMPILE="$(jq -r '.ActivePatchTotal // 0' "$RESULT_FILE")"
HELD_AFTER_COMPILE="$(jq -r '.AutoRefreshHeld | tostring' "$RESULT_FILE")"
if [ "$ACTIVE_AFTER_COMPILE" != "0" ] || [ "$HELD_AFTER_COMPILE" != "false" ]; then
    log "FAIL: expected ActivePatchTotal=0 and AutoRefreshHeld=false after compile (was ${ACTIVE_AFTER_COMPILE}, ${HELD_AFTER_COMPILE})."
    cat "$RESULT_FILE"
    exit 1
fi

log "PASS: Play Mode survived focus return while hot-reload patches were active; compile cleared the hold."
exit 0
