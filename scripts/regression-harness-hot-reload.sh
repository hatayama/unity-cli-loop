#!/bin/sh
set -e
# Regression harness for the HotReload trap:
# PlayMode logs a string literal from Update each frame. The driver sed-edits that
# in-body literal on disk, applies uloop hot-reload (no domain reload), asserts
# get-logs shows the new value, restores the source, reverts patches, and asserts
# the old value returns. Sed must target a method-body literal — not a class-level
# const or field initializer (those resolve from the stale compiled assembly).
# See docs/regression-harness.md.
#
# Usage: sh scripts/regression-harness-hot-reload.sh [--project-path <path>]
#
# Prerequisites:
#   - Assets/RegressionHarness/HotReload/HotReload.unity must be open in a running Unity Editor
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

# Why sed and uloop must share one root: --project-path selects the Unity project whose
# Assets file we mutate; resolving SOURCE_ABS from the script checkout alone would edit one
# tree while hot-reloading another.
if [ -n "$PROJECT_PATH" ]; then
    EFFECTIVE_ROOT=$(CDPATH= cd -- "$PROJECT_PATH" && pwd)
else
    EFFECTIVE_ROOT="$REPO_ROOT"
fi

SOURCE_FILE="Assets/RegressionHarness/HotReload/HotReloadMarkerLogger.cs"
SOURCE_ABS="$EFFECTIVE_ROOT/$SOURCE_FILE"
if [ ! -f "$SOURCE_ABS" ]; then
    printf '%s\n' "Harness source not found: $SOURCE_ABS" >&2
    printf '%s\n' "--project-path must point at a checkout that contains $SOURCE_FILE" >&2
    exit 2
fi

OLD_LITERAL="marker=111"
NEW_LITERAL="marker=222"
OLD_MARKER="[HotReloadHarness] ${OLD_LITERAL}"
NEW_MARKER="[HotReloadHarness] ${NEW_LITERAL}"

# Why fail-fast on a dirty tree: a prior kill -9 can leave marker=222 on disk; backing that
# up would let the run PASS and then restore the dirty bytes, leaving 222 behind.
if ! grep -q "$OLD_LITERAL" "$SOURCE_ABS"; then
    printf '%s\n' "Harness source is not pristine (expected ${OLD_LITERAL}): $SOURCE_ABS" >&2
    printf '%s\n' "Restore it (git restore ${SOURCE_FILE}) before running." >&2
    exit 2
fi

RESULT_FILE="$(mktemp)"
LOG_FILE="$(mktemp)"
SOURCE_BACKUP="$(mktemp)"
AUTO_REFRESH_DISALLOWED="0"
SOURCE_DIRTY="0"
CLEANED_UP="0"

# Why copy-then-restore: sed mutates a tracked Assets file; any early exit must leave the
# working tree clean, so the pristine bytes are snapshotted before the first edit.
cp "$SOURCE_ABS" "$SOURCE_BACKUP"

run_uloop() {
    "$ULOOP_BIN" "$@" --project-path "$EFFECTIVE_ROOT"
}

log() {
    printf "\033[36m[hot-reload]\033[0m %s\n" "$1"
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
        # Why best-effort: cleanup must not fail the harness after assertions already passed.
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
    # Why re-entry guard: INT/TERM handlers also trigger EXIT, and must not double-restore.
    if [ "$CLEANED_UP" = "1" ]; then
        return
    fi
    CLEANED_UP="1"
    restore_source
    run_uloop hot-reload --revert-all > /dev/null 2>&1 || true
    allow_auto_refresh
    run_uloop control-play-mode --action Stop > /dev/null 2>&1 || true
    # Why: a killed sed→mv window can leave SOURCE_ABS.tmp; Unity may then create .tmp.meta
    # which is not gitignored.
    rm -f "$RESULT_FILE" "$LOG_FILE" "$SOURCE_BACKUP" "${SOURCE_ABS}.tmp"
}
# Why INT/TERM/HUP too: EXIT alone misses Ctrl-C / kill mid-sed and would leave Assets dirty.
# EXIT still runs after the signal traps call exit; CLEANED_UP prevents a second pass.
trap cleanup EXIT
trap 'cleanup; exit 130' INT
trap 'cleanup; exit 143' TERM
trap 'cleanup; exit 129' HUP

disallow_auto_refresh() {
    # Why: sed writes under Assets/; without this hold Unity may import + domain-reload mid-run
    # and erase the hot-reload patches (or race the Mvid guard) before get-logs can assert.
    run_uloop execute-dynamic-code --code "
using UnityEditor;
AssetDatabase.DisallowAutoRefresh();
return \"DisallowAutoRefresh\";
" > /dev/null
    AUTO_REFRESH_DISALLOWED="1"
}

await_marker_in_logs() {
    expected_marker="$1"
    attempt=1
    # Why 30s: cold PlayMode entry (import / domain reload) can exceed 10s before the first Update.
    max_attempts=30
    while [ "$attempt" -le "$max_attempts" ]; do
        run_uloop clear-console > /dev/null
        # Why sleep: Update logs once per frame; give PlayMode a beat to emit after clear.
        sleep 1
        run_uloop get-logs --log-type Log --max-count 50 --search-text "$expected_marker" > "$LOG_FILE"
        displayed="$(jq -r '.DisplayedCount // 0' "$LOG_FILE")"
        if [ "$displayed" -gt 0 ]; then
            return 0
        fi
        attempt=$((attempt + 1))
    done
    log "FAIL: did not observe log marker within ${max_attempts}s: $expected_marker"
    cat "$LOG_FILE"
    return 1
}

log "Disallowing AssetDatabase auto-refresh for the sed window..."
disallow_auto_refresh

log "Stopping any existing Play Mode session..."
run_uloop control-play-mode --action Stop > /dev/null

log "Starting Play Mode..."
run_uloop control-play-mode --action Play > /dev/null

log "Asserting baseline ${OLD_LITERAL} before hot-reload..."
await_marker_in_logs "$OLD_MARKER"

log "Sed-editing in-body literal ${OLD_LITERAL} -> ${NEW_LITERAL} in $SOURCE_FILE..."
# Why before sed: a signal between mv and a later flag would skip restore and leave marker=222.
SOURCE_DIRTY="1"
# Why temp+mv instead of sed -i: -i is not POSIX, and BSD sed's -i demands a backup suffix.
sed "s/${OLD_LITERAL}/${NEW_LITERAL}/" "$SOURCE_ABS" > "${SOURCE_ABS}.tmp"
mv "${SOURCE_ABS}.tmp" "$SOURCE_ABS"
if ! grep -q "$NEW_LITERAL" "$SOURCE_ABS"; then
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

log "Asserting ${NEW_LITERAL} after hot-reload..."
await_marker_in_logs "$NEW_MARKER"

log "Restoring source and reverting all hot-reload patches..."
restore_source
run_uloop hot-reload --revert-all > "$RESULT_FILE"
revert_success="$(jq -r '.Success' "$RESULT_FILE")"
if [ "$revert_success" != "true" ]; then
    log "FAIL: --revert-all did not succeed"
    cat "$RESULT_FILE"
    exit 1
fi

log "Asserting ${OLD_LITERAL} after revert-all..."
await_marker_in_logs "$OLD_MARKER"

log "PASS: hot-reload changed PlayMode logs and revert-all restored the previous body."
exit 0
