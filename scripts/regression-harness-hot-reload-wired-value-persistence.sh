#!/bin/sh
set -e
# Regression harness for the HotReloadWiredValuePersistence trap:
# with Domain Reload disabled, entering or leaving Play Mode still reloads the scene
# and rebuilds every scene object. The driver hot-reloads an added Transform field
# into HotReloadWiredValueHost, wires the scene's Target into it from Edit Mode, and
# asserts that the rebuilt Play Mode instance reads Target back both in Awake (while
# the scene loads) and in Update, that --status counts the restore, and that the
# rebuilt Edit Mode instance reads it back after Stop. It then reverts everything,
# re-applies the same source without wiring, and asserts the value no longer comes
# back. See docs/regression-harness.md.
#
# Usage: sh scripts/regression-harness-hot-reload-wired-value-persistence.sh [--project-path <path>]
#
# Prerequisites:
#   - Enter Play Mode Options enabled with Domain Reload disabled
#     (Scene Reload stays enabled; that reload is the trap)
#   - Assets/RegressionHarness/HotReloadWiredValuePersistence/HotReloadWiredValuePersistence.unity
#     must be the active scene in a running Unity Editor, in Edit Mode
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

SOURCE_FILE="Assets/RegressionHarness/HotReloadWiredValuePersistence/HotReloadWiredValueHost.cs"
SCENE_FILE="Assets/RegressionHarness/HotReloadWiredValuePersistence/HotReloadWiredValuePersistence.unity"
SOURCE_ABS="$EFFECTIVE_ROOT/$SOURCE_FILE"
EDITOR_SETTINGS="$EFFECTIVE_ROOT/ProjectSettings/EditorSettings.asset"
if [ ! -f "$SOURCE_ABS" ]; then
    printf '%s\n' "Harness source not found: $SOURCE_ABS" >&2
    printf '%s\n' "--project-path must point at a checkout that contains $SOURCE_FILE" >&2
    exit 2
fi

# Why fail fast here: with Domain Reload on, Play entry drops the whole hot reload, and with
# Scene Reload off no host is rebuilt, so every assertion below would fail for a reason this
# harness does not test. Bit 1 is DisableDomainReload and bit 2 DisableSceneReload; bit 4
# (DisableSceneBackupUnlessDirty) does not matter here.
options_enabled="$(sed -n 's/^ *m_EnterPlayModeOptionsEnabled: *\([0-9]*\).*/\1/p' "$EDITOR_SETTINGS")"
options="$(sed -n 's/^ *m_EnterPlayModeOptions: *\([0-9]*\).*/\1/p' "$EDITOR_SETTINGS")"
if [ "$options_enabled" != "1" ] || [ -z "$options" ] || [ $((options & 1)) -eq 0 ] || [ $((options & 2)) -ne 0 ]; then
    printf '%s\n' "This harness requires Domain Reload OFF and Scene Reload ON (Enter Play Mode Options enabled, Reload Domain unchecked, Reload Scene checked)." >&2
    printf '%s\n' "Found m_EnterPlayModeOptionsEnabled=${options_enabled} m_EnterPlayModeOptions=${options} in $EDITOR_SETTINGS" >&2
    exit 1
fi

AWAKE_BASELINE_NEEDLE="Debug.Log(\"[HotReloadWiredValueHarness] awake-baseline\");"
UPDATE_BASELINE_NEEDLE="Debug.Log(\"[HotReloadWiredValueHarness] update-baseline\");"

# Why fail-fast on a dirty tree: a prior kill -9 can leave the patched source on disk;
# backing that up would let the run PASS and then restore the dirty bytes.
if ! grep -Fq "$AWAKE_BASELINE_NEEDLE" "$SOURCE_ABS" || ! grep -Fq "$UPDATE_BASELINE_NEEDLE" "$SOURCE_ABS"; then
    printf '%s\n' "Harness source is not pristine (expected baseline Awake and Update): $SOURCE_ABS" >&2
    printf '%s\n' "Restore it (git restore ${SOURCE_FILE}) before running." >&2
    exit 2
fi

RESULT_FILE="$(mktemp)"
LOG_FILE="$(mktemp)"
PROBE_FILE="$(mktemp)"
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
    printf "\033[36m[hot-reload-wired-value]\033[0m %s\n" "$1"
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
    run_uloop control-play-mode --action Stop > /dev/null 2>&1 || true
    run_uloop hot-reload --revert-all > /dev/null 2>&1 || true
    restore_source
    allow_auto_refresh
    rm -f "$RESULT_FILE" "$LOG_FILE" "$PROBE_FILE" "$SOURCE_BACKUP" "${SOURCE_ABS}.tmp"
}
# Why INT/TERM/HUP too: EXIT alone misses Ctrl-C / kill mid-write and would leave Assets dirty.
# EXIT still runs after the signal traps call exit; CLEANED_UP prevents a second pass.
trap cleanup EXIT
trap 'cleanup; exit 130' INT
trap 'cleanup; exit 143' TERM
trap 'cleanup; exit 129' HUP

disallow_auto_refresh() {
    # Why: writes under Assets/; without this hold Unity may import + domain-reload mid-run
    # and erase the hot-reload patches before the Play Mode assertions run.
    run_uloop execute-dynamic-code --code "
using UnityEditor;
AssetDatabase.DisallowAutoRefresh();
return \"DisallowAutoRefresh\";
" > /dev/null
    AUTO_REFRESH_DISALLOWED="1"
}

assert_harness_scene_open() {
    run_uloop execute-dynamic-code --code "
using UnityEngine.SceneManagement;
return SceneManager.GetActiveScene().path;
" > "$PROBE_FILE"
    active_scene="$(jq -r '.Result // empty' "$PROBE_FILE")"
    if [ "$active_scene" != "$SCENE_FILE" ]; then
        log "FAIL: open $SCENE_FILE in the Editor first (active scene: ${active_scene})"
        exit 2
    fi
}

print_matched_log() {
    jq -r '[.. | objects | select(has("Message")) | .Message] | .[0] // empty' "$LOG_FILE" | head -n 1
}

# Why a variant that does not clear: Awake logs once per Play entry, so clearing the
# console before each poll could erase the only line there will ever be.
await_marker_without_clear() {
    expected_marker="$1"
    attempt=1
    max_attempts=30
    while [ "$attempt" -le "$max_attempts" ]; do
        run_uloop get-logs --log-type Log --max-count 50 --search-text "$expected_marker" > "$LOG_FILE"
        displayed="$(jq -r '.DisplayedCount // 0' "$LOG_FILE")"
        if [ "$displayed" -gt 0 ]; then
            log "observed: $(print_matched_log)"
            return 0
        fi
        sleep 1
        attempt=$((attempt + 1))
    done
    log "FAIL: did not observe log marker within ${max_attempts}s: $expected_marker"
    run_uloop get-logs --log-type Log --max-count 20 --search-text "[HotReloadWiredValueHarness]" || true
    return 1
}

await_marker_in_logs() {
    expected_marker="$1"
    attempt=1
    # Why 30s: cold PlayMode entry can exceed 10s before the first Update.
    max_attempts=30
    while [ "$attempt" -le "$max_attempts" ]; do
        run_uloop clear-console > /dev/null
        # Why sleep: Update logs once per frame; give PlayMode a beat to emit after clear.
        sleep 1
        run_uloop get-logs --log-type Log --max-count 50 --search-text "$expected_marker" > "$LOG_FILE"
        displayed="$(jq -r '.DisplayedCount // 0' "$LOG_FILE")"
        if [ "$displayed" -gt 0 ]; then
            log "observed: $(print_matched_log)"
            return 0
        fi
        attempt=$((attempt + 1))
    done
    log "FAIL: did not observe log marker within ${max_attempts}s: $expected_marker"
    run_uloop get-logs --log-type Log --max-count 20 --search-text "[HotReloadWiredValueHarness]" || true
    return 1
}

write_patched_source() {
    # Why the compiled Awake and Update are rewritten rather than added: the engine only calls
    # the compiled ones, and the patched bodies read the added field through the side table.
    cat > "${SOURCE_ABS}.tmp" <<'ENDPATCH'
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    public sealed class HotReloadWiredValueHost : MonoBehaviour
    {
        [SerializeField] private Transform target;

        private void Awake()
        {
            Debug.Log("[HotReloadWiredValueHarness] awake-target=" + (target == null ? "none" : target.name));
        }

        private void Update()
        {
            Debug.Log("[HotReloadWiredValueHarness] update-target=" + (target == null ? "none" : target.name));
        }
    }
}
ENDPATCH
    mv "${SOURCE_ABS}.tmp" "$SOURCE_ABS"
}

apply_patched_source() {
    run_uloop hot-reload --files "$SOURCE_FILE" > "$RESULT_FILE"
    success="$(jq -r '.Success' "$RESULT_FILE")"
    added_target="$(jq '[.AddedFields[]? | select(endswith(".target"))] | length' "$RESULT_FILE")"
    if [ "$success" != "true" ] || [ "$added_target" -lt 1 ]; then
        log "FAIL: expected Success=true and an added field named target"
        cat "$RESULT_FILE"
        exit 1
    fi
    log "hot-reload applied (AddedFields: $(jq -c '.AddedFields' "$RESULT_FILE"))."
}

read_wired_target() {
    run_uloop execute-dynamic-code --code "
using UnityEngine;
using io.github.hatayama.UnityCliLoop.RegressionHarness;
using io.github.hatayama.UnityCliLoop.ToolContracts;
HotReloadWiredValueHost host = Object.FindObjectOfType<HotReloadWiredValueHost>();
if (host == null)
{
    return \"error=no-host\";
}
if (!HotReloadAddedFieldWiring.TryReadInstanceField(host, \"target\", out object value))
{
    return \"no-slot\";
}
Transform wired = value as Transform;
return wired == null ? \"none\" : wired.name;
" > "$PROBE_FILE"
    jq -r '.Result // empty' "$PROBE_FILE"
}

log "Checking that the harness scene is open..."
assert_harness_scene_open

log "Disallowing AssetDatabase auto-refresh for the edit window..."
disallow_auto_refresh

log "Step 2: stopping Play Mode, adding the target field and applying hot-reload in Edit Mode..."
run_uloop control-play-mode --action Stop > /dev/null
# Why before write: a signal between mv and a later flag would skip restore.
SOURCE_DIRTY="1"
write_patched_source
apply_patched_source

log "Step 3: wiring the scene's Target into the Edit Mode host..."
run_uloop execute-dynamic-code --code "
using UnityEngine;
using io.github.hatayama.UnityCliLoop.RegressionHarness;
using io.github.hatayama.UnityCliLoop.ToolContracts;
HotReloadWiredValueHost host = Object.FindObjectOfType<HotReloadWiredValueHost>();
if (host == null)
{
    return \"error=no-host\";
}
HotReloadAddedFieldWiring.SetInstanceField(host, \"target\", host.transform.Find(\"Target\"));
return \"wired\";
" > "$PROBE_FILE"
wired="$(jq -r '.Result // empty' "$PROBE_FILE")"
if [ "$wired" != "wired" ]; then
    log "FAIL: wiring did not run (got: ${wired})"
    cat "$PROBE_FILE"
    exit 1
fi
wired_now="$(read_wired_target)"
if [ "$wired_now" != "Target" ]; then
    log "FAIL: the Edit Mode host does not hold Target after wiring (got: ${wired_now})"
    exit 1
fi

log "Step 4: entering Play Mode; the scene reload rebuilds the host..."
run_uloop clear-console > /dev/null
run_uloop control-play-mode --action Play > /dev/null
await_marker_without_clear "[HotReloadWiredValueHarness] awake-target=Target"
await_marker_in_logs "[HotReloadWiredValueHarness] update-target=Target"

log "Step 5: checking --status counts the restore..."
run_uloop hot-reload --status > "$RESULT_FILE"
restored="$(jq -r '.RestoredWiredValueCount // 0' "$RESULT_FILE")"
if [ "$restored" -lt 1 ]; then
    log "FAIL: --status RestoredWiredValueCount is ${restored}, expected >= 1"
    cat "$RESULT_FILE"
    exit 1
fi
log "--status RestoredWiredValueCount=${restored} UnrestoredWiredValues=$(jq -c '.UnrestoredWiredValues // []' "$RESULT_FILE")"

log "Step 6: stopping Play Mode; the Edit Mode host is rebuilt too..."
run_uloop control-play-mode --action Stop > /dev/null
after_stop="$(read_wired_target)"
if [ "$after_stop" != "Target" ]; then
    log "FAIL: the Edit Mode host rebuilt after Stop does not read Target (got: ${after_stop})"
    cat "$PROBE_FILE"
    exit 1
fi
log "Edit Mode host after Stop reads: ${after_stop}"

log "Step 7: --revert-all, then re-apply the same source without wiring..."
run_uloop hot-reload --revert-all > "$RESULT_FILE"
revert_success="$(jq -r '.Success' "$RESULT_FILE")"
if [ "$revert_success" != "true" ]; then
    log "FAIL: --revert-all did not succeed"
    cat "$RESULT_FILE"
    exit 1
fi
apply_patched_source
run_uloop control-play-mode --action Play > /dev/null
await_marker_in_logs "[HotReloadWiredValueHarness] update-target=none"
run_uloop control-play-mode --action Stop > /dev/null

log "PASS: the wired Target came back in Awake and Update after Play entry and in Edit Mode after Stop, and not after --revert-all."
exit 0
