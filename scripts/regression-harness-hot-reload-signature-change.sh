#!/bin/sh
set -e
# Regression harness for the HotReloadSignatureChange trap:
# PlayMode logs SameFileValue from Update. The driver first changes that method's
# return type in the same file as its caller and applies uloop hot-reload (no
# domain reload), asserting Kind=Added + Patched and the new log value. It then
# restores, reverts, and changes SharedValue — which a second compiled file still
# calls — asserting both SharedValue and the edited same-file caller are Skipped.
# See docs/regression-harness.md.
#
# Usage: sh scripts/regression-harness-hot-reload-signature-change.sh [--project-path <path>]
#
# Prerequisites:
#   - Assets/RegressionHarness/HotReloadSignatureChange/HotReloadSignatureChange.unity
#     must be open in a running Unity Editor
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

SOURCE_FILE="Assets/RegressionHarness/HotReloadSignatureChange/HotReloadSignatureChangeTarget.cs"
SOURCE_ABS="$EFFECTIVE_ROOT/$SOURCE_FILE"
if [ ! -f "$SOURCE_ABS" ]; then
    printf '%s\n' "Harness source not found: $SOURCE_ABS" >&2
    printf '%s\n' "--project-path must point at a checkout that contains $SOURCE_FILE" >&2
    exit 2
fi

BASELINE_MARKER="[HotReloadSignatureChangeHarness] same=1"
APPLIED_MARKER="[HotReloadSignatureChangeHarness] same=10;applied"
PRISTINE_NEEDLE="Debug.Log(\"[HotReloadSignatureChangeHarness] same=\" + SameFileValue());"
GATE_REASON="compiled code outside this hot reload still calls the old method"
CALLER_REASON="this caller was left unpatched"

# Why fail-fast on a dirty tree: a prior kill -9 can leave the patched members on disk;
# backing that up would let the run PASS and then restore the dirty bytes.
if ! grep -Fq "$PRISTINE_NEEDLE" "$SOURCE_ABS"; then
    printf '%s\n' "Harness source is not pristine (expected baseline Update): $SOURCE_ABS" >&2
    printf '%s\n' "Restore it (git restore ${SOURCE_FILE}) before running." >&2
    exit 2
fi

RESULT_FILE="$(mktemp)"
LOG_FILE="$(mktemp)"
SOURCE_BACKUP="$(mktemp)"
AUTO_REFRESH_DISALLOWED="0"
SOURCE_DIRTY="0"
CLEANED_UP="0"

# Why copy-then-restore: the driver mutates a tracked Assets file; any early exit must
# leave the working tree clean, so the pristine bytes are snapshotted before the first edit.
cp "$SOURCE_ABS" "$SOURCE_BACKUP"

run_uloop() {
    "$ULOOP_BIN" "$@" --project-path "$EFFECTIVE_ROOT"
}

log() {
    printf "\033[36m[hot-reload-signature-change]\033[0m %s\n" "$1"
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
    rm -f "$RESULT_FILE" "$LOG_FILE" "$SOURCE_BACKUP" "${SOURCE_ABS}.tmp"
}
# Why INT/TERM/HUP too: EXIT alone misses Ctrl-C / kill mid-write and would leave Assets dirty.
# EXIT still runs after the signal traps call exit; CLEANED_UP prevents a second pass.
trap cleanup EXIT
trap 'cleanup; exit 130' INT
trap 'cleanup; exit 143' TERM
trap 'cleanup; exit 129' HUP

disallow_auto_refresh() {
    # Why: writes under Assets/; without this hold Unity may import + domain-reload mid-run
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

write_same_file_apply_source() {
    # Why a full rewrite: return-type change plus an Update body edit so the same-file
    # caller is an entry. Leaving Update unedited would be an uncovered same-file caller.
    cat > "${SOURCE_ABS}.tmp" <<'ENDPATCH'
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    public sealed class HotReloadSignatureChangeTarget : MonoBehaviour
    {
        public long SameFileValue()
        {
            return 10;
        }

        public int SharedValue()
        {
            return 2;
        }

        public int ReadShared()
        {
            return SharedValue();
        }

        private void Update()
        {
            Debug.Log("[HotReloadSignatureChangeHarness] same=" + SameFileValue() + ";applied");
        }
    }
}
ENDPATCH
    mv "${SOURCE_ABS}.tmp" "$SOURCE_ABS"
}

write_external_caller_skip_source() {
    # Why edit ReadShared: the gate must also skip the same-file caller of SharedValue.
    # ExternalCaller in the other file stays compiled and is never edited.
    cat > "${SOURCE_ABS}.tmp" <<'ENDPATCH'
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    public sealed class HotReloadSignatureChangeTarget : MonoBehaviour
    {
        public int SameFileValue()
        {
            return 1;
        }

        public long SharedValue()
        {
            return 20;
        }

        public int ReadShared()
        {
            return (int)SharedValue();
        }

        private void Update()
        {
            Debug.Log("[HotReloadSignatureChangeHarness] same=" + SameFileValue());
        }
    }
}
ENDPATCH
    mv "${SOURCE_ABS}.tmp" "$SOURCE_ABS"
}

assert_same_file_apply() {
    added_count="$(jq '[.Methods[]? | select(.Kind=="Added")] | length' "$RESULT_FILE")"
    patched_count="$(jq '[.Methods[]? | select(.Kind=="Patched")] | length' "$RESULT_FILE")"
    skipped_same="$(jq '[.Methods[]? | select((.Method // "") | contains("SameFileValue")) | select(.Kind=="Skipped")] | length' "$RESULT_FILE")"
    success="$(jq -r '.Success' "$RESULT_FILE")"
    if [ "$success" != "true" ] || [ "$added_count" -lt 1 ] || [ "$patched_count" -lt 1 ] || [ "$skipped_same" -ne 0 ]; then
        log "FAIL: expected Success=true, Kind=Added>=1, Kind=Patched>=1, SameFileValue not Skipped (Added=${added_count} Patched=${patched_count} SkippedSame=${skipped_same})"
        cat "$RESULT_FILE"
        exit 1
    fi
    log "same-file return-type change applied (Added=${added_count}, Patched=${patched_count})."
}

assert_external_caller_skip() {
    success="$(jq -r '.Success' "$RESULT_FILE")"
    skipped_shared="$(jq --arg reason "$GATE_REASON" '[.Methods[]? | select((.Method // "") | contains("SharedValue")) | select(.Kind=="Skipped") | select((.Reason // "") | contains($reason))] | length' "$RESULT_FILE")"
    skipped_reader="$(jq --arg reason "$CALLER_REASON" '[.Methods[]? | select((.Method // "") | contains("ReadShared")) | select(.Kind=="Skipped") | select((.Reason // "") | contains($reason))] | length' "$RESULT_FILE")"
    if [ "$success" != "true" ] || [ "$skipped_shared" -lt 1 ] || [ "$skipped_reader" -lt 1 ]; then
        log "FAIL: expected Success=true, SharedValue Skipped with gate reason, ReadShared Skipped with caller reason (Shared=${skipped_shared} Reader=${skipped_reader})"
        cat "$RESULT_FILE"
        exit 1
    fi
    log "external compiled caller gated SharedValue and ReadShared as Skipped."
}

log "Disallowing AssetDatabase auto-refresh for the edit window..."
disallow_auto_refresh

log "Stopping any existing Play Mode session..."
run_uloop control-play-mode --action Stop > /dev/null

log "Starting Play Mode..."
run_uloop control-play-mode --action Play > /dev/null

log "Asserting baseline marker before hot-reload..."
await_marker_in_logs "$BASELINE_MARKER"

log "Writing same-file return-type change into $SOURCE_FILE..."
# Why before write: a signal between mv and a later flag would skip restore.
SOURCE_DIRTY="1"
write_same_file_apply_source
if grep -Fq "return 1;" "$SOURCE_ABS"; then
    log "FAIL: patched source still contains the baseline SameFileValue body"
    exit 1
fi

log "Applying hot-reload to $SOURCE_FILE..."
run_uloop hot-reload --files "$SOURCE_FILE" > "$RESULT_FILE"
assert_same_file_apply

log "Asserting ${APPLIED_MARKER} after the same-file apply..."
await_marker_in_logs "$APPLIED_MARKER"

log "Restoring source and reverting all hot-reload patches..."
restore_source
run_uloop hot-reload --revert-all > "$RESULT_FILE"
revert_success="$(jq -r '.Success' "$RESULT_FILE")"
if [ "$revert_success" != "true" ]; then
    log "FAIL: --revert-all did not succeed after same-file apply"
    cat "$RESULT_FILE"
    exit 1
fi

log "Asserting baseline after revert-all..."
await_marker_in_logs "$BASELINE_MARKER"

log "Writing SharedValue return-type change with an external compiled caller..."
SOURCE_DIRTY="1"
write_external_caller_skip_source

log "Applying hot-reload to $SOURCE_FILE..."
run_uloop hot-reload --files "$SOURCE_FILE" > "$RESULT_FILE"
assert_external_caller_skip

log "Asserting baseline marker still holds because Update was not patched..."
await_marker_in_logs "$BASELINE_MARKER"

log "Restoring source and reverting all hot-reload patches..."
restore_source
run_uloop hot-reload --revert-all > "$RESULT_FILE"
revert_success="$(jq -r '.Success' "$RESULT_FILE")"
if [ "$revert_success" != "true" ]; then
    log "FAIL: --revert-all did not succeed after gated skip"
    cat "$RESULT_FILE"
    exit 1
fi

log "Asserting baseline after final revert-all..."
await_marker_in_logs "$BASELINE_MARKER"

log "PASS: same-file return-type change applied; external compiled caller gated SharedValue; revert-all restored baseline."
exit 0
