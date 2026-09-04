#!/bin/sh
set -e
# Regression harness for focus-return Auto Refresh: a .cs file and an open-scene
# rename made while the Editor is unfocused must import, compile, and reload
# after focus returns, without a native modal dialog. The Auto Refresh hold
# removed for issue #2575 used to leave the .cs unimported until an explicit
# compile. A second pass then edits the open scene while focused and checks
# that uloop compile imports and reloads it without the native dialog.
# See docs/regression-harness.md.
#
# Usage: sh scripts/regression-harness-focus-return-auto-refresh.sh [--project-path <path>]
#
# Prerequisites:
#   - Unity Editor must already be running for this project and unfocused
#   - uloop CLI must be installed
#   - jq must be installed
#   - the harness scene must be clean in git

PROJECT_PATH=""
if [ "$1" = "--project-path" ] && [ -n "$2" ]; then
    PROJECT_PATH="$2"
fi

SCENE_REL="Assets/RegressionHarness/FocusReturnAutoRefresh/FocusReturnAutoRefresh.unity"
PROBE_REL="Assets/RegressionHarness/FocusReturnAutoRefresh/FocusReturnProbe.cs"
CODE_FILE="$(mktemp)"
RESULT_FILE="$(mktemp)"

if [ -n "$PROJECT_PATH" ]; then
    PROJECT_ROOT="$PROJECT_PATH"
else
    PROJECT_ROOT="."
fi

run_uloop() {
    if [ -n "$PROJECT_PATH" ]; then
        uloop "$@" --project-path "$PROJECT_PATH"
    else
        uloop "$@"
    fi
}

log() {
    printf "\033[36m[focus-return-auto-refresh]\033[0m %s\n" "$1"
}

cleanup() {
    # Close the harness scene before restoring the file. A focused Editor that
    # still has MarkerExternal loaded will raise Unity's native reload dialog
    # when git checkout writes Marker back and compile refreshes assets.
    cat > "$CODE_FILE" <<'EOF'
using UnityEditor.SceneManagement;
EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
return "closed";
EOF
    run_uloop execute-dynamic-code --code-file "$CODE_FILE" > /dev/null 2>&1 || true
    rm -f "$PROJECT_ROOT/$PROBE_REL" "$PROJECT_ROOT/${PROBE_REL}.meta"
    git -C "$PROJECT_ROOT" checkout -- "$SCENE_REL"
    run_uloop compile > /dev/null 2>&1 || true
    rm -f "$CODE_FILE" "$RESULT_FILE"
}
trap cleanup EXIT

if ! command -v jq >/dev/null 2>&1; then
    log "FAIL: jq is required."
    exit 1
fi

SCENE_DIRTY="$(git -C "$PROJECT_ROOT" status --porcelain -- "$SCENE_REL")"
if [ -n "$SCENE_DIRTY" ]; then
    log "FAIL: $SCENE_REL has uncommitted changes. Commit or restore it before running."
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

log "Opening the harness scene..."
cat > "$CODE_FILE" <<'EOF'
using UnityEditor.SceneManagement;
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/RegressionHarness/FocusReturnAutoRefresh/FocusReturnAutoRefresh.unity");
return "opened";
EOF
run_uloop execute-dynamic-code --code-file "$CODE_FILE" > /dev/null

log "Writing external .cs and scene changes while unfocused..."
printf '%s\n' 'public class FocusReturnProbe { }' > "$PROJECT_ROOT/$PROBE_REL"
SCENE_TMP="$PROJECT_ROOT/${SCENE_REL}.tmp"
sed 's/m_Name: Marker$/m_Name: MarkerExternal/' "$PROJECT_ROOT/$SCENE_REL" > "$SCENE_TMP"
mv "$SCENE_TMP" "$PROJECT_ROOT/$SCENE_REL"

log "Returning focus to the Unity Editor..."
run_uloop focus-window > /dev/null

FOCUS_START="$(date +%s)"
FOCUSED=""
while :; do
    NOW="$(date +%s)"
    if [ $((NOW - FOCUS_START)) -ge 10 ]; then
        break
    fi
    cat > "$CODE_FILE" <<'EOF'
return UnityEditor.EditorApplication.isFocused.ToString();
EOF
    if run_uloop execute-dynamic-code --code-file "$CODE_FILE" > "$RESULT_FILE"; then
        FOCUSED="$(jq -r '.Result // empty' "$RESULT_FILE")"
        if [ "$FOCUSED" = "True" ]; then
            break
        fi
    fi
    sleep 1
done
if [ "$FOCUSED" != "True" ]; then
    log "FAIL: Editor did not become focused within 10 seconds after focus-window."
    exit 1
fi

log "Waiting for import, compile, and scene reload..."
cat > "$CODE_FILE" <<'EOF'
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
string guid = AssetDatabase.AssetPathToGUID("Assets/RegressionHarness/FocusReturnAutoRefresh/FocusReturnProbe.cs");
bool typeLoaded = System.Type.GetType("FocusReturnProbe, Assembly-CSharp") != null;
bool markerRenamed = GameObject.Find("MarkerExternal") != null;
bool sceneClean = SceneManager.GetActiveScene().isDirty == false;
return "guidNonEmpty=" + (!string.IsNullOrEmpty(guid)).ToString()
    + " typeLoaded=" + typeLoaded.ToString()
    + " markerRenamed=" + markerRenamed.ToString()
    + " sceneClean=" + sceneClean.ToString();
EOF

WAIT_START="$(date +%s)"
LAST_STATE=""
while :; do
    NOW="$(date +%s)"
    if [ $((NOW - WAIT_START)) -ge 180 ]; then
        break
    fi
    if run_uloop execute-dynamic-code --code-file "$CODE_FILE" > "$RESULT_FILE"; then
        LAST_STATE="$(jq -r '.Result // empty' "$RESULT_FILE")"
        if [ "$LAST_STATE" = "guidNonEmpty=True typeLoaded=True markerRenamed=True sceneClean=True" ]; then
            log "PASS: external .cs imported and compiled, and the open scene reloaded without a modal dialog."
            log "Editing the open scene while focused and compiling..."
            SCENE_TMP="$PROJECT_ROOT/${SCENE_REL}.tmp"
            sed 's/m_Name: MarkerExternal$/m_Name: MarkerCompiled/' "$PROJECT_ROOT/$SCENE_REL" > "$SCENE_TMP"
            mv "$SCENE_TMP" "$PROJECT_ROOT/$SCENE_REL"
            if ! run_uloop compile --timeout-seconds 120 > /dev/null; then
                log "FAIL: compile did not return; Unity may be blocked by a modal dialog"
                exit 1
            fi
            cat > "$CODE_FILE" <<'EOF'
using UnityEngine;
using UnityEngine.SceneManagement;
bool markerRenamed = GameObject.Find("MarkerCompiled") != null;
bool sceneClean = SceneManager.GetActiveScene().isDirty == false;
return "markerCompiled=" + markerRenamed.ToString() + " sceneClean=" + sceneClean.ToString();
EOF
            if ! run_uloop execute-dynamic-code --code-file "$CODE_FILE" > "$RESULT_FILE"; then
                log "FAIL: could not verify the compile-path scene reload."
                cat "$RESULT_FILE"
                exit 1
            fi
            COMPILE_STATE="$(jq -r '.Result // empty' "$RESULT_FILE")"
            if [ "$COMPILE_STATE" != "markerCompiled=True sceneClean=True" ]; then
                log "FAIL: focused compile did not reload the external scene change. State: $COMPILE_STATE"
                exit 1
            fi
            log "PASS: external scene change made while focused was imported and reloaded by uloop compile without a modal dialog"
            exit 0
        fi
    fi
    sleep 3
done

log "Timed out: the external changes were not imported. Either Auto Refresh did not run on focus return, or Unity is blocked by a modal dialog. Check the Editor window."
if [ -n "$LAST_STATE" ]; then
    log "Last state: $LAST_STATE"
fi
exit 1
