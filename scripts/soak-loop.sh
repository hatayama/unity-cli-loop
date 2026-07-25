#!/bin/sh
set -u
# Endurance (soak) harness for the uloop CLI against a real Unity project.
#
# Each iteration rewrites a scratch editor script inside the target project to
# force a genuine script compilation + domain reload, then exercises the
# commands that must survive hundreds of reload crossings: compile, get-logs,
# get-hierarchy, screenshot, execute-dynamic-code. On a cadence it also runs
# Unity tests, full editor restarts, forced recompiles, and a PlayMode
# pause-point cycle against a generated minimal scene. Every command's exit code, duration, and payload
# size is appended to a CSV so latency drift and failure rate over time can be
# graphed afterwards. Editor memory (RSS) and leftover uloop processes are
# sampled per iteration as leak signals.
#
# Usage:
#   sh scripts/soak-loop.sh --project-path <unity-project> \
#     [--iterations N]      total iterations (default: 100)
#     [--restart-every N]   run `uloop launch -r` every N iterations (default: 25, 0 = never)
#     [--force-every N]     use compile --force-recompile every N iterations (default: 10, 0 = never)
#     [--pause-every N]     run a PlayMode cycle (button click + pause-point) every N iterations (default: 5, 0 = never)
#     [--tests-every N]     run `uloop run-tests` every N iterations (default: 10, 0 = never)
#     [--test-assembly A]   test assembly name passed to run-tests --filter-type assembly
#                           (required when --tests-every > 0; never run the full suite of a large project)
#     [--sleep-seconds N]   pause between iterations (default: 0)
#     [--out-dir DIR]       results directory (default: ./uloop-soak-results/<timestamp>)
#
# Environment:
#   ULOOP_BIN  uloop binary to use (default: uloop from PATH — the realistic
#              release configuration; point at a dist/ binary to soak unreleased CLI code)
#
# Prerequisites:
#   - Unity Editor already running with the target project open
#   - uloop package installed in the target project
#
# All generated files live under Assets/UloopSoak/ in the target project (the
# recompile scratch script, the PlayMode ticker script, and the pause-point
# scene) and are left behind after the run — delete the folder and its .meta
# manually when done. When --pause-every > 0, the harness opens its generated
# scene and keeps it open, so save your own scene changes before running.

ULOOP_BIN="${ULOOP_BIN:-uloop}"

PROJECT_PATH=""
ITERATIONS=100
RESTART_EVERY=25
FORCE_EVERY=10
PAUSE_EVERY=5
TESTS_EVERY=10
TEST_ASSEMBLY=""
SLEEP_SECONDS=0
OUT_DIR=""

usage() {
    sed -n '/^# Usage:/,/^#   - uloop package installed/p' "$0"
    exit 1
}

while [ $# -gt 0 ]; do
    case "$1" in
        --project-path)  PROJECT_PATH="$2"; shift 2 ;;
        --iterations)    ITERATIONS="$2"; shift 2 ;;
        --restart-every) RESTART_EVERY="$2"; shift 2 ;;
        --force-every)   FORCE_EVERY="$2"; shift 2 ;;
        --pause-every)   PAUSE_EVERY="$2"; shift 2 ;;
        --tests-every)   TESTS_EVERY="$2"; shift 2 ;;
        --test-assembly) TEST_ASSEMBLY="$2"; shift 2 ;;
        --sleep-seconds) SLEEP_SECONDS="$2"; shift 2 ;;
        --out-dir)       OUT_DIR="$2"; shift 2 ;;
        *) echo "Unknown argument: $1" >&2; usage ;;
    esac
done

[ -n "$PROJECT_PATH" ] || { echo "Error: --project-path is required" >&2; usage; }
[ -f "$PROJECT_PATH/ProjectSettings/ProjectVersion.txt" ] || {
    echo "Error: $PROJECT_PATH does not look like a Unity project (no ProjectSettings/ProjectVersion.txt)" >&2
    exit 1
}
if [ "$TESTS_EVERY" -gt 0 ] && [ -z "$TEST_ASSEMBLY" ]; then
    echo "Error: --test-assembly is required when tests are enabled (pass --tests-every 0 to skip tests; running the full suite of a large project is not a safe default)" >&2
    exit 1
fi
if [ "$PAUSE_EVERY" -gt 0 ] && ! command -v jq > /dev/null 2>&1; then
    echo "Error: jq is required for the PlayMode UI cycle (pass --pause-every 0 to skip it)" >&2
    exit 1
fi

STAMP="$(date +%Y%m%d-%H%M%S)"
[ -n "$OUT_DIR" ] || OUT_DIR="$PWD/uloop-soak-results/$STAMP"
mkdir -p "$OUT_DIR"
COMMANDS_CSV="$OUT_DIR/commands.csv"
METRICS_CSV="$OUT_DIR/metrics.csv"
RUN_LOG="$OUT_DIR/run.log"
SOAK_ASSETS_DIR="$PROJECT_PATH/Assets/UloopSoak"
SCRATCH_DIR="$SOAK_ASSETS_DIR/Editor"
SCRATCH_FILE="$SCRATCH_DIR/UloopSoakScratch.cs"
TICKER_REL="Assets/UloopSoak/UloopSoakTicker.cs"
PROBE_REL="Assets/UloopSoak/UloopSoakButtonProbe.cs"
SCENE_REL="Assets/UloopSoak/UloopSoak.unity"

echo "epoch_ms,iteration,command,exit_code,duration_ms,payload_bytes" > "$COMMANDS_CSV"
echo "epoch_ms,iteration,unity_rss_kb,project_runner_procs,outputs_dir_kb" > "$METRICS_CSV"

log() {
    printf '%s [soak] %s\n' "$(date +%H:%M:%S)" "$1" | tee -a "$RUN_LOG"
}

now_ms() {
    perl -MTime::HiRes=time -e 'printf "%d\n", time * 1000'
}

run_uloop() {
    "$ULOOP_BIN" "$@" --project-path "$PROJECT_PATH"
}

# Runs one uloop command, appends a CSV row, and returns the command's exit code.
# $1 = iteration, $2 = label, rest = uloop arguments.
CAPTURE_FILE="$OUT_DIR/.last-output"
timed() {
    _iter="$1"; _label="$2"; shift 2
    _start="$(now_ms)"
    run_uloop "$@" > "$CAPTURE_FILE" 2>&1
    _exit=$?
    _end="$(now_ms)"
    _bytes="$(wc -c < "$CAPTURE_FILE" | tr -d ' ')"
    echo "$_start,$_iter,$_label,$_exit,$((_end - _start)),$_bytes" >> "$COMMANDS_CSV"
    if [ "$_exit" -ne 0 ]; then
        log "FAIL iter=$_iter $_label exit=$_exit ($(head -c 200 "$CAPTURE_FILE" | tr '\n' ' '))"
    fi
    return "$_exit"
}

# Forces a script recompile by rewriting the scratch file with a new constant.
write_scratch() {
    mkdir -p "$SCRATCH_DIR"
    cat > "$SCRATCH_FILE" <<EOF
// Auto-generated by soak-loop.sh — safe to delete.
// Rewritten every iteration to force a script recompile and domain reload.
public static class UloopSoakScratch
{
    public const int Iteration = $1;
}
EOF
}

# Line number of the tickCount++ statement in the heredoc below — the two must
# stay in sync or every pause-point cycle arms a line that never executes.
TICKER_LINE=11
write_ticker() {
    mkdir -p "$SOAK_ASSETS_DIR"
    cat > "$PROJECT_PATH/$TICKER_REL" <<'EOF'
// Auto-generated by soak-loop.sh — safe to delete.
// PlayMode ticker whose Update line is the soak's pause-point target.
using UnityEngine;

public class UloopSoakTicker : MonoBehaviour
{
    private int tickCount;

    private void Update()
    {
        tickCount++;
    }
}
EOF
    # The probe must live in its own file: Unity only serializes a
    # MonoBehaviour into a scene when its class name matches the file name,
    # and a second class in a shared file saves as a missing-script reference.
    cat > "$PROJECT_PATH/$PROBE_REL" <<'EOF'
// Auto-generated by soak-loop.sh — safe to delete.
// Counts EventSystem clicks so the soak can verify simulate-mouse-ui delivery.
using UnityEngine;

public class UloopSoakButtonProbe : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
{
    public int ClickCount { get; private set; }

    public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
    {
        ClickCount++;
        Debug.Log("UloopSoakButtonClicked count=" + ClickCount);
    }
}
EOF
}

# Discards any previous soak scene and rebuilds it from scratch: the
# pause-point ticker plus a clickable button wired for the UI simulation
# check. Recreating every time keeps stale generated state from leaking
# between cycles. Refuses to discard unsaved USER scene changes (returns
# DIRTY_SCENE); the soak scene itself is always disposable.
write_scene_setup_snippet() {
    cat > "$OUT_DIR/setup-scene.cs" <<EOF
string scenePath = "$SCENE_REL";
UnityEngine.SceneManagement.Scene active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (active.path != scenePath && active.isDirty)
{
    return "DIRTY_SCENE";
}
UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
if (System.IO.File.Exists(scenePath))
{
    UnityEditor.AssetDatabase.DeleteAsset(scenePath);
}
UnityEngine.GameObject tickerGo = new UnityEngine.GameObject("SoakTicker");
tickerGo.AddComponent<UloopSoakTicker>();
UnityEngine.GameObject canvasGo = new UnityEngine.GameObject("SoakCanvas");
UnityEngine.Canvas canvas = canvasGo.AddComponent<UnityEngine.Canvas>();
canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
UnityEngine.GameObject buttonGo = new UnityEngine.GameObject("SoakButton");
buttonGo.transform.SetParent(canvasGo.transform, false);
buttonGo.AddComponent<UnityEngine.UI.Image>();
buttonGo.AddComponent<UloopSoakButtonProbe>();
UnityEngine.RectTransform rect = buttonGo.GetComponent<UnityEngine.RectTransform>();
rect.sizeDelta = new UnityEngine.Vector2(320f, 120f);
UnityEngine.GameObject eventSystemGo = new UnityEngine.GameObject("SoakEventSystem");
eventSystemGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
eventSystemGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), scenePath);
return "recreated";
EOF
}

# Runs before every pause cycle, not just at setup: an editor restart mid-soak
# reopens the project's own last scene, which would silently leave the ticker
# out of PlayMode and expire every subsequent pause-point.
ensure_soak_scene() {
    timed "$1" scene-ensure execute-dynamic-code --code-file "$OUT_DIR/setup-scene.cs"
}

sample_metrics() {
    _rss="$(ps ax -o rss=,args= | grep -F "$PROJECT_PATH" | grep -F "Unity.app" | grep -v grep | awk '{print $1; exit}')"
    _runners="$(pgrep -f "uloop-project-runner" 2>/dev/null | wc -l | tr -d ' ')"
    _outputs_kb="$(du -sk "$PROJECT_PATH/.uloop/outputs" 2>/dev/null | awk '{print $1}')"
    echo "$(now_ms),$1,${_rss:-},$_runners,${_outputs_kb:-0}" >> "$METRICS_CSV"
}

# Polls get-logs until the editor answers again (used after restarts and recovery).
wait_for_editor() {
    _deadline=$(( $(date +%s) + 900 ))
    while [ "$(date +%s)" -lt "$_deadline" ]; do
        if run_uloop get-logs --max-count 1 > /dev/null 2>&1; then
            return 0
        fi
        sleep 20
    done
    return 1
}

# A soak aborted mid-pause-cycle would leave the editor paused in PlayMode;
# always hand the editor back in a usable state.
cleanup_editor_state() {
    if [ "$PAUSE_EVERY" -gt 0 ]; then
        run_uloop clear-pause-point --all > /dev/null 2>&1 || true
        run_uloop control-play-mode --action Stop > /dev/null 2>&1 || true
    fi
}

summarize() {
    cleanup_editor_state
    log "Results: $OUT_DIR"
    awk -F, 'NR > 1 {
        total[$3]++; ms[$3] += $5
        if ($4 != 0) fail[$3]++
    } END {
        printf "%-14s %8s %8s %10s\n", "command", "runs", "fails", "avg_ms"
        for (c in total)
            printf "%-14s %8d %8d %10d\n", c, total[c], fail[c] + 0, ms[c] / total[c]
    }' "$COMMANDS_CSV" | tee -a "$RUN_LOG"
    log "Reminder: delete $SOAK_ASSETS_DIR (and its .meta) from the target project when finished."
}
trap summarize EXIT
trap 'exit 130' INT TERM

log "Soak start: $ITERATIONS iterations against $PROJECT_PATH (uloop: $ULOOP_BIN)"
log "restart-every=$RESTART_EVERY force-every=$FORCE_EVERY pause-every=$PAUSE_EVERY tests-every=$TESTS_EVERY sleep=${SLEEP_SECONDS}s"

# A freshly launched editor can be busy importing/compiling for a long time
# (especially on large projects), so the preflight polls instead of one-shotting.
if ! run_uloop get-logs --max-count 1 > /dev/null 2>&1; then
    log "Editor not answering yet — waiting up to 15 minutes (is Unity running with this project open?)"
    if ! wait_for_editor; then
        log "Preflight failed: uloop cannot reach the editor."
        exit 1
    fi
fi

# Runner generations flipped this flag's polarity: newer runners need an explicit
# --wait-for-domain-reload (off by default), older ones wait by default and only
# expose --no-wait-for-domain-reload. Detect which flavor the pinned runner speaks.
COMPILE_WAIT_FLAG=""
if run_uloop compile --help 2>/dev/null | grep -qE '^[[:space:]]*--wait-for-domain-reload'; then
    COMPILE_WAIT_FLAG="--wait-for-domain-reload"
fi
log "compile wait flag: ${COMPILE_WAIT_FLAG:-(runner waits by default)}"

if [ "$PAUSE_EVERY" -gt 0 ]; then
    write_ticker
    if ! timed 0 setup-compile compile $COMPILE_WAIT_FLAG; then
        log "Setup compile for the pause-point ticker failed — aborting."
        exit 1
    fi
    write_scene_setup_snippet
    if ! ensure_soak_scene 0; then
        log "Pause-point scene setup failed — aborting."
        exit 1
    fi
    if grep -q "DIRTY_SCENE" "$CAPTURE_FILE"; then
        log "The active scene has unsaved changes — save or discard them, then rerun."
        exit 1
    fi
    log "pause-point scene ready: $SCENE_REL"
fi

CONSECUTIVE_FAILS=0
i=1
while [ "$i" -le "$ITERATIONS" ]; do
    write_scratch "$i"

    ITER_FAILED=0
    # Forced recompiles rebuild every assembly — a heavier reload path worth
    # soaking, but far too slow to run on every iteration of a large project.
    # Unity may legitimately report an unknown forced result after the domain
    # reload (ForceCompileUnknownResult); the tool's own guidance is to follow
    # up with a plain compile, so only that follow-up counts against the
    # iteration and any other forced failure still does.
    if [ "$FORCE_EVERY" -gt 0 ] && [ $((i % FORCE_EVERY)) -eq 0 ]; then
        if ! timed "$i" compile-forced compile $COMPILE_WAIT_FLAG --force-recompile; then
            grep -q "definitive result" "$CAPTURE_FILE" || ITER_FAILED=1
        fi
    fi
    timed "$i" compile compile $COMPILE_WAIT_FLAG || ITER_FAILED=1
    timed "$i" get-logs get-logs --max-count 200 || ITER_FAILED=1
    timed "$i" get-hierarchy get-hierarchy --max-depth 5 || ITER_FAILED=1
    timed "$i" screenshot screenshot --window-name Game --resolution-scale 0.5 || ITER_FAILED=1
    timed "$i" dynamic-code execute-dynamic-code --code "int iteration = $i; return iteration + UnityEngine.SceneManagement.SceneManager.sceneCount;" || ITER_FAILED=1

    if [ "$PAUSE_EVERY" -gt 0 ] && [ $((i % PAUSE_EVERY)) -eq 0 ]; then
        SCENE_OK=1
        if ! ensure_soak_scene "$i"; then
            SCENE_OK=0
        elif grep -q "DIRTY_SCENE" "$CAPTURE_FILE"; then
            SCENE_OK=0
        fi
        if [ "$SCENE_OK" -ne 1 ]; then
            log "iter=$i could not open the soak scene (unsaved changes?) — pause cycle failed"
            ITER_FAILED=1
        elif timed "$i" play-start control-play-mode --action Play; then
            # UI simulation runs before the pause-point so EventSystem click
            # processing happens while the game is still un-paused. Per the
            # simulate-mouse-ui contract, clicks are coordinate-driven — the
            # canonical flow is reading the button's SimX/SimY from the
            # screenshot element annotation, which this also soaks.
            BUTTON_XY=""
            if timed "$i" ui-annotate screenshot --capture-mode rendering --annotate-elements --elements-only; then
                BUTTON_XY="$(jq -r '.Screenshots[0].AnnotatedElements[] | select(.Path == "SoakCanvas/SoakButton") | "\(.SimX) \(.SimY)"' "$CAPTURE_FILE" 2>/dev/null | head -1)"
            fi
            if [ -z "$BUTTON_XY" ]; then
                log "iter=$i SoakButton missing from annotated elements"
                ITER_FAILED=1
            else
                if timed "$i" ui-click simulate-mouse-ui --action Click --x "${BUTTON_XY% *}" --y "${BUTTON_XY#* }"; then
                    grep -q '"HitGameObjectName": "SoakButton"' "$CAPTURE_FILE" || { log "iter=$i click did not hit SoakButton"; ITER_FAILED=1; }
                else
                    ITER_FAILED=1
                fi
                if timed "$i" ui-verify execute-dynamic-code --code 'UloopSoakButtonProbe probe = UnityEngine.Object.FindFirstObjectByType<UloopSoakButtonProbe>(); return probe == null ? "probe-missing" : probe.ClickCount.ToString();'; then
                    grep -q '"Result": "1"' "$CAPTURE_FILE" || { log "iter=$i button click was not registered by the probe"; ITER_FAILED=1; }
                else
                    ITER_FAILED=1
                fi
            fi
            timed "$i" pause-arm enable-pause-point --file "$TICKER_REL" --line "$TICKER_LINE" --timeout-seconds 60 || ITER_FAILED=1
            if timed "$i" pause-await await-pause-point --id "$TICKER_REL:$TICKER_LINE" --timeout-seconds 60; then
                grep -q '"Hit"' "$CAPTURE_FILE" || { log "iter=$i pause-point await returned without a Hit"; ITER_FAILED=1; }
                # A Hit whose CapturedVariables lacks the ticker's field means
                # the variable-capture pipeline broke even though pausing works.
                grep -q '"tickCount"' "$CAPTURE_FILE" || { log "iter=$i pause-point hit but tickCount was not captured"; ITER_FAILED=1; }
            else
                ITER_FAILED=1
            fi
            run_uloop clear-pause-point --all > /dev/null 2>&1
            timed "$i" play-stop control-play-mode --action Stop || ITER_FAILED=1
        else
            ITER_FAILED=1
        fi
    fi

    if [ "$TESTS_EVERY" -gt 0 ] && [ $((i % TESTS_EVERY)) -eq 0 ]; then
        # Red project tests are not a soak failure — the harness measures whether
        # uloop transported and completed the run, so only a missing test report
        # (no TestCount in the response) counts against the iteration.
        if ! timed "$i" run-tests run-tests --test-mode EditMode --filter-type assembly --filter-value "$TEST_ASSEMBLY"; then
            grep -q '"TestCount"' "$CAPTURE_FILE" || ITER_FAILED=1
        fi
    fi

    if [ "$RESTART_EVERY" -gt 0 ] && [ $((i % RESTART_EVERY)) -eq 0 ] && [ "$i" -lt "$ITERATIONS" ]; then
        log "iter=$i scheduled editor restart"
        timed "$i" launch-restart launch -r || true
        if ! wait_for_editor; then
            log "Editor did not come back within 15 minutes after scheduled restart — aborting."
            exit 1
        fi
    fi

    sample_metrics "$i"

    if [ "$ITER_FAILED" -ne 0 ]; then
        CONSECUTIVE_FAILS=$((CONSECUTIVE_FAILS + 1))
    else
        CONSECUTIVE_FAILS=0
    fi
    if [ "$CONSECUTIVE_FAILS" -ge 3 ]; then
        log "3 consecutive failing iterations — attempting one recovery restart."
        run_uloop launch -r > /dev/null 2>&1 || true
        if ! wait_for_editor; then
            log "Recovery restart failed — aborting soak."
            exit 1
        fi
        CONSECUTIVE_FAILS=0
        log "Recovery succeeded, continuing."
    fi

    [ "$SLEEP_SECONDS" -gt 0 ] && sleep "$SLEEP_SECONDS"
    if [ $((i % 10)) -eq 0 ]; then
        log "progress: $i/$ITERATIONS iterations done"
    fi
    i=$((i + 1))
done

log "Soak completed: $ITERATIONS iterations."
