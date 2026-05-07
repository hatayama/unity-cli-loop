#!/bin/sh
# E2E verification for SimulateMouseDemoScene using the uloop CLI.

set -eu

SCENE_PATH="Assets/Scenes/SimulateMouseDemoScene.unity"
TMP_DIR="${TMPDIR:-/tmp}/unity-cli-loop-simulate-mouse"
ELEMENTS_JSON="$TMP_DIR/simulate-mouse-elements.json"
ORIGINAL_GAME_VIEW_SIZE_INDEX=""

fail() {
    printf 'ERROR: %s\n' "$1" >&2
    exit 1
}

cleanup() {
    uloop control-play-mode --action Stop >/dev/null 2>&1 || true
    if [ -n "${ORIGINAL_GAME_VIEW_SIZE_INDEX:-}" ]; then
        restore_game_view_size_index "$ORIGINAL_GAME_VIEW_SIZE_INDEX" >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT INT TERM

require_jq() {
    command -v jq >/dev/null 2>&1 || fail "jq is required to parse uloop JSON responses"
}

run_uloop_json() {
    if ! output=$(uloop "$@" 2>&1); then
        printf '%s\n' "$output" >&2
        fail "uloop $* failed"
    fi

    printf '%s\n' "$output"
}

assert_json_success() {
    json=$1
    context=$2

    printf '%s\n' "$json" | jq -e '.Success == true' >/dev/null \
        || fail "$context failed: $(printf '%s\n' "$json" | jq -r '.ErrorMessage // .Message // .Error // "unknown error"')"
}

assert_text_equals() {
    actual=$1
    expected=$2
    context=$3

    [ "$actual" = "$expected" ] || fail "$context: expected '$expected', got '$actual'"
}

wait_unity_ready() {
    attempt=0
    while [ "$attempt" -lt 15 ]; do
        if uloop get-logs --max-count 1 >/dev/null 2>&1; then
            return
        fi

        attempt=$((attempt + 1))
        sleep 2
    done

    fail "Unity did not respond to uloop"
}

wait_play_mode() {
    attempt=0
    while [ "$attempt" -lt 20 ]; do
        json=$(run_uloop_json execute-dynamic-code --code 'using UnityEngine; return Application.isPlaying;')
        if printf '%s\n' "$json" | jq -e '.Success == true and .Result == "True"' >/dev/null; then
            return
        fi

        attempt=$((attempt + 1))
        sleep 1
    done

    fail "Unity did not enter PlayMode"
}

initialize_demo_scene() {
    uloop control-play-mode --action Stop >/dev/null 2>&1 || true

    code="
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
string scenePath = \"$SCENE_PATH\";
if (SceneManager.GetActiveScene().path != scenePath)
{
    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
}
return SceneManager.GetActiveScene().path;
"

    json=$(run_uloop_json execute-dynamic-code --code "$code")
    assert_json_success "$json" "Load demo scene"
    scene=$(printf '%s\n' "$json" | jq -r '.Result')
    assert_text_equals "$scene" "$SCENE_PATH" "Active scene"
}

select_full_hd_game_view() {
    # Unity exposes no public setter for the Game View resolution dropdown.
    # This matches the manual "Full HD (1920x1080)" selection before reading UI coordinates.
    code='
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
const int Width = 1920;
const int Height = 1080;
EditorApplication.ExecuteMenuItem("Window/General/Game");
Type gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
Debug.Assert(gameViewType != null, "GameView type must exist.");
UnityEngine.Object[] gameViews = Resources.FindObjectsOfTypeAll(gameViewType);
EditorWindow gameView = null;
for (int i = 0; i < gameViews.Length; i++)
{
    EditorWindow candidate = gameViews[i] as EditorWindow;
    if (candidate == null)
    {
        continue;
    }
    if (gameView == null || candidate.hasFocus)
    {
        gameView = candidate;
    }
}
if (gameView == null)
{
    gameView = EditorWindow.GetWindow(gameViewType);
}
gameView.Show();
Type gameViewSizesType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
Debug.Assert(gameViewSizesType != null, "GameViewSizes type must exist.");
Type singletonType = typeof(ScriptableSingleton<>).MakeGenericType(gameViewSizesType);
PropertyInfo instanceProperty = singletonType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
Debug.Assert(instanceProperty != null, "GameViewSizes instance property must exist.");
object gameViewSizes = instanceProperty.GetValue(null);
MethodInfo getGroupMethod = gameViewSizesType.GetMethod("GetGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
Debug.Assert(getGroupMethod != null, "GameViewSizes.GetGroup must exist.");
object group = getGroupMethod.Invoke(gameViewSizes, new object[] { GameViewSizeGroupType.Standalone });
Debug.Assert(group != null, "Standalone GameViewSize group must exist.");
Type groupType = group.GetType();
MethodInfo getDisplayTextsMethod = groupType.GetMethod("GetDisplayTexts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
Debug.Assert(getDisplayTextsMethod != null, "GameViewSizeGroup.GetDisplayTexts must exist.");
string[] displayTexts = (string[])getDisplayTextsMethod.Invoke(group, null);
int selectedIndex = -1;
for (int i = 0; i < displayTexts.Length; i++)
{
    if (displayTexts[i].Contains("1920x1080") || displayTexts[i].Contains("Full HD"))
    {
        selectedIndex = i;
        break;
    }
}
if (selectedIndex < 0)
{
    Type gameViewSizeType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSize");
    Debug.Assert(gameViewSizeType != null, "GameViewSize type must exist.");
    Type gameViewSizeTypeEnum = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizeType");
    Debug.Assert(gameViewSizeTypeEnum != null, "GameViewSizeType enum must exist.");
    object fixedResolution = Enum.Parse(gameViewSizeTypeEnum, "FixedResolution");
    ConstructorInfo constructor = gameViewSizeType.GetConstructor(new Type[] { gameViewSizeTypeEnum, typeof(int), typeof(int), typeof(string) });
    Debug.Assert(constructor != null, "GameViewSize constructor must exist.");
    object selectedGameViewSize = constructor.Invoke(new object[] { fixedResolution, Width, Height, "Full HD" });
    MethodInfo addCustomSizeMethod = groupType.GetMethod("AddCustomSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Debug.Assert(addCustomSizeMethod != null, "GameViewSizeGroup.AddCustomSize must exist.");
    addCustomSizeMethod.Invoke(group, new object[] { selectedGameViewSize });
    displayTexts = (string[])getDisplayTextsMethod.Invoke(group, null);
    selectedIndex = displayTexts.Length - 1;
}
PropertyInfo selectedSizeIndexProperty = gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
Debug.Assert(selectedSizeIndexProperty != null, "GameView.selectedSizeIndex must exist.");
selectedSizeIndexProperty.SetValue(gameView, selectedIndex, null);
Screen.SetResolution(Width, Height, false);
gameView.Repaint();
Vector2 currentGameViewSize = Handles.GetMainGameViewSize();
return displayTexts[selectedIndex] + " / " + currentGameViewSize.x + "x" + currentGameViewSize.y;
'

    json=$(run_uloop_json execute-dynamic-code --code "$code")
    assert_json_success "$json" "Select Full HD Game View"
    printf '    %s\n' "$(printf '%s\n' "$json" | jq -r '.Result')"
    sleep 1
}

capture_game_view_size_index() {
    code='
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
Type gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
Debug.Assert(gameViewType != null, "GameView type must exist.");
UnityEngine.Object[] gameViews = Resources.FindObjectsOfTypeAll(gameViewType);
EditorWindow gameView = null;
for (int i = 0; i < gameViews.Length; i++)
{
    EditorWindow candidate = gameViews[i] as EditorWindow;
    if (candidate == null)
    {
        continue;
    }
    if (gameView == null || candidate.hasFocus)
    {
        gameView = candidate;
    }
}
if (gameView == null)
{
    gameView = EditorWindow.GetWindow(gameViewType);
}
PropertyInfo selectedSizeIndexProperty = gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
Debug.Assert(selectedSizeIndexProperty != null, "GameView.selectedSizeIndex must exist.");
return selectedSizeIndexProperty.GetValue(gameView, null).ToString();
'

    json=$(run_uloop_json execute-dynamic-code --code "$code")
    assert_json_success "$json" "Capture Game View size index"
    printf '%s\n' "$json" | jq -r '.Result'
}

restore_game_view_size_index() {
    selected_index=$1
    code="
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
int selectedIndex = $selected_index;
Debug.Assert(selectedIndex >= 0, \"GameView size index must be non-negative.\");
Type gameViewType = typeof(Editor).Assembly.GetType(\"UnityEditor.GameView\");
Debug.Assert(gameViewType != null, \"GameView type must exist.\");
UnityEngine.Object[] gameViews = Resources.FindObjectsOfTypeAll(gameViewType);
EditorWindow gameView = null;
for (int i = 0; i < gameViews.Length; i++)
{
    EditorWindow candidate = gameViews[i] as EditorWindow;
    if (candidate == null)
    {
        continue;
    }
    if (gameView == null || candidate.hasFocus)
    {
        gameView = candidate;
    }
}
if (gameView == null)
{
    gameView = EditorWindow.GetWindow(gameViewType);
}
PropertyInfo selectedSizeIndexProperty = gameViewType.GetProperty(\"selectedSizeIndex\", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
Debug.Assert(selectedSizeIndexProperty != null, \"GameView.selectedSizeIndex must exist.\");
selectedSizeIndexProperty.SetValue(gameView, selectedIndex, null);
gameView.Repaint();
return selectedIndex.ToString();
"

    run_uloop_json execute-dynamic-code --code "$code"
}

capture_annotated_elements() {
    run_uloop_json screenshot \
        --capture-mode rendering \
        --annotate-elements \
        --elements-only >"$ELEMENTS_JSON"

    jq -e '.ScreenshotCount == 1 and (.Screenshots[0].AnnotatedElements | length > 0)' "$ELEMENTS_JSON" >/dev/null \
        || fail "No annotated UI elements were returned"
}

get_element_json() {
    name=$1
    count=$(jq --arg name "$name" '[.Screenshots[0].AnnotatedElements[] | select(.Name == $name)] | length' "$ELEMENTS_JSON")
    [ "$count" = "1" ] || fail "Expected exactly one annotated element named '$name', found $count"

    jq -c --arg name "$name" '.Screenshots[0].AnnotatedElements[] | select(.Name == $name)' "$ELEMENTS_JSON"
}

element_field() {
    element_json=$1
    field=$2
    printf '%s\n' "$element_json" | jq -r ".$field"
}

assert_mouse_response() {
    json=$1
    expected_action=$2
    expected_hit=$3

    printf '%s\n' "$json" | jq -e --arg action "$expected_action" \
        '.Success == true and .Action == $action' >/dev/null \
        || fail "$expected_action failed: $(printf '%s\n' "$json" | jq -r '.Message // "unknown error"')"

    if [ -n "$expected_hit" ]; then
        actual_hit=$(printf '%s\n' "$json" | jq -r '.HitGameObjectName // ""')
        assert_text_equals "$actual_hit" "$expected_hit" "HitGameObjectName"
    fi
}

invoke_click() {
    name=$1
    element=$(get_element_json "$name")
    x=$(element_field "$element" "SimX")
    y=$(element_field "$element" "SimY")
    path=$(element_field "$element" "Path")

    json=$(run_uloop_json simulate-mouse-ui \
        --action Click \
        --x "$x" \
        --y "$y" \
        --bypass-raycast \
        --target-path "$path")
    assert_mouse_response "$json" "Click" "$name"
}

invoke_long_press() {
    name=$1
    duration=$2
    element=$(get_element_json "$name")
    x=$(element_field "$element" "SimX")
    y=$(element_field "$element" "SimY")
    path=$(element_field "$element" "Path")

    json=$(run_uloop_json simulate-mouse-ui \
        --action LongPress \
        --x "$x" \
        --y "$y" \
        --duration "$duration" \
        --bypass-raycast \
        --target-path "$path")
    assert_mouse_response "$json" "LongPress" "$name"
}

invoke_drag_to_drop_zone() {
    name=$1
    speed=$2
    element=$(get_element_json "$name")
    drop_zone=$(get_element_json "DropZone")
    from_x=$(element_field "$element" "SimX")
    from_y=$(element_field "$element" "SimY")
    to_x=$(element_field "$drop_zone" "SimX")
    to_y=$(element_field "$drop_zone" "SimY")
    target_path=$(element_field "$element" "Path")
    drop_path=$(element_field "$drop_zone" "Path")

    json=$(run_uloop_json simulate-mouse-ui \
        --action Drag \
        --from-x "$from_x" \
        --from-y "$from_y" \
        --x "$to_x" \
        --y "$to_y" \
        --drag-speed "$speed" \
        --bypass-raycast \
        --target-path "$target_path" \
        --drop-target-path "$drop_path")
    assert_mouse_response "$json" "Drag" "$name"
}

invoke_drag_start() {
    name=$1
    element=$(get_element_json "$name")
    x=$(element_field "$element" "SimX")
    y=$(element_field "$element" "SimY")
    path=$(element_field "$element" "Path")

    json=$(run_uloop_json simulate-mouse-ui \
        --action DragStart \
        --x "$x" \
        --y "$y" \
        --drag-speed 700 \
        --bypass-raycast \
        --target-path "$path")
    assert_mouse_response "$json" "DragStart" "$name"
}

invoke_drag_move() {
    x=$1
    y=$2
    speed=$3
    expected_hit=$4

    json=$(run_uloop_json simulate-mouse-ui \
        --action DragMove \
        --x "$x" \
        --y "$y" \
        --drag-speed "$speed")
    assert_mouse_response "$json" "DragMove" "$expected_hit"
}

invoke_drag_end() {
    x=$1
    y=$2
    speed=$3
    drop_path=$4
    expected_hit=$5

    json=$(run_uloop_json simulate-mouse-ui \
        --action DragEnd \
        --x "$x" \
        --y "$y" \
        --drag-speed "$speed" \
        --drop-target-path "$drop_path")
    assert_mouse_response "$json" "DragEnd" "$expected_hit"
}

get_text_from_scene() {
    object_name=$1
    code="
using UnityEngine;
using UnityEngine.UI;
GameObject target = GameObject.Find(\"$object_name\");
if (target == null) { return \"ERROR: $object_name not found\"; }
Text text = target.GetComponent<Text>();
if (text == null) { return \"ERROR: $object_name has no Text component\"; }
return text.text;
"

    json=$(run_uloop_json execute-dynamic-code --code "$code")
    assert_json_success "$json" "Read text from $object_name"
    printf '%s\n' "$json" | jq -r '.Result'
}

get_long_press_button_text() {
    code='
using UnityEngine;
using UnityEngine.UI;
GameObject target = GameObject.Find("LongPressButton");
if (target == null) { return "ERROR: LongPressButton not found"; }
Text text = target.GetComponentInChildren<Text>();
if (text == null) { return "ERROR: LongPressButton label not found"; }
return text.text;
'

    json=$(run_uloop_json execute-dynamic-code --code "$code")
    assert_json_success "$json" "Read LongPressButton text"
    printf '%s\n' "$json" | jq -r '.Result'
}

get_drop_zone_status() {
    code='
using UnityEngine;
GameObject target = GameObject.Find("DropZone");
if (target == null) { return "ERROR: DropZone not found"; }
DropZone dropZone = target.GetComponent<DropZone>();
if (dropZone == null) { return "ERROR: DropZone component not found"; }
return dropZone.StatusMessage;
'

    json=$(run_uloop_json execute-dynamic-code --code "$code")
    assert_json_success "$json" "Read DropZone status"
    printf '%s\n' "$json" | jq -r '.Result'
}

get_virtual_pad_state() {
    code='
using UnityEngine;
GameObject target = GameObject.Find("VirtualPadBackground");
if (target == null) { return "ERROR: VirtualPadBackground not found"; }
DemoVirtualPad pad = target.GetComponent<DemoVirtualPad>();
if (pad == null) { return "ERROR: DemoVirtualPad component not found"; }
if (Mathf.Abs(pad.Direction.x) < 0.001f && Mathf.Abs(pad.Direction.y) < 0.001f) { return "Zero"; }
return pad.Direction.ToString("F3");
'

    json=$(run_uloop_json execute-dynamic-code --code "$code")
    assert_json_success "$json" "Read VirtualPad state"
    printf '%s\n' "$json" | jq -r '.Result'
}

exercise_virtual_pad() {
    virtual_pad=$(get_element_json "VirtualPadBackground")
    pad_x=$(element_field "$virtual_pad" "SimX")
    pad_y=$(element_field "$virtual_pad" "SimY")
    pad_width=$(printf '%s\n' "$virtual_pad" | jq -r '.BoundsMaxX - .BoundsMinX')
    pad_height=$(printf '%s\n' "$virtual_pad" | jq -r '.BoundsMaxY - .BoundsMinY')
    pad_offset=$(jq -nr --argjson width "$pad_width" --argjson height "$pad_height" '([$width, $height] | min) * 0.28')
    target_path=$(element_field "$virtual_pad" "Path")

    json=$(run_uloop_json simulate-mouse-ui \
        --action DragStart \
        --x "$pad_x" \
        --y "$pad_y" \
        --bypass-raycast \
        --target-path "$target_path")
    assert_mouse_response "$json" "DragStart" "VirtualPadBackground"

    move_x=$(jq -nr --argjson center "$pad_x" --argjson offset "$pad_offset" '$center + $offset')
    move_y=$(jq -nr --argjson center "$pad_y" --argjson offset "$pad_offset" '$center - $offset')
    invoke_drag_move "$move_x" "$move_y" 400 "VirtualPadBackground"

    move_x=$(jq -nr --argjson center "$pad_x" --argjson offset "$pad_offset" '$center - $offset')
    move_y=$(jq -nr --argjson center "$pad_y" --argjson offset "$pad_offset" '$center + $offset')
    invoke_drag_move "$move_x" "$move_y" 400 "VirtualPadBackground"

    move_x="$pad_x"
    move_y=$(jq -nr --argjson center "$pad_y" --argjson offset "$pad_offset" '$center - $offset')
    invoke_drag_move "$move_x" "$move_y" 400 "VirtualPadBackground"

    move_x=$(jq -nr --argjson center "$pad_x" --argjson offset "$pad_offset" '$center + $offset')
    move_y="$pad_y"
    invoke_drag_move "$move_x" "$move_y" 400 "VirtualPadBackground"

    invoke_drag_end "$pad_x" "$pad_y" 400 "" "VirtualPadBackground"
}

mkdir -p "$TMP_DIR"
require_jq

printf '\n'
printf '=========================================\n'
printf '  SimulateMouse UI E2E Verification\n'
printf '=========================================\n'

wait_unity_ready

printf '[1/8] Loading SimulateMouse demo scene...\n'
initialize_demo_scene

printf '[2/8] Starting PlayMode...\n'
run_uloop_json control-play-mode --action Play >/dev/null
wait_play_mode
sleep 1

printf '[3/8] Selecting Full HD Game View resolution...\n'
ORIGINAL_GAME_VIEW_SIZE_INDEX=$(capture_game_view_size_index)
select_full_hd_game_view

printf '[4/8] Reading annotated UI coordinates...\n'
capture_annotated_elements

printf '[5/8] Clicking counter buttons...\n'
invoke_click "ClickButton1"
invoke_click "ClickButton2"
invoke_click "ClickButton1"
invoke_click "ClickButton2"
counter_text=$(get_text_from_scene "CounterText")
assert_text_equals "$counter_text" "Total Clicks: 4" "CounterText"

printf '[6/8] Long-pressing the hold button...\n'
invoke_long_press "LongPressButton" 3.2
long_press_text=$(get_long_press_button_text)
assert_text_equals "$long_press_text" "Activated!" "LongPressButton label"

printf '[7/8] Dragging boxes into the DropZone...\n'
invoke_drag_to_drop_zone "RedBox" 900
assert_text_equals "$(get_drop_zone_status)" "Dropped: RedBox" "DropZone after RedBox"

green_box=$(get_element_json "GreenBox")
drop_zone=$(get_element_json "DropZone")
green_x=$(element_field "$green_box" "SimX")
green_y=$(element_field "$green_box" "SimY")
drop_x=$(element_field "$drop_zone" "SimX")
drop_y=$(element_field "$drop_zone" "SimY")
drop_path=$(element_field "$drop_zone" "Path")

invoke_drag_start "GreenBox"
move_x=$(jq -nr --argjson center "$drop_x" '$center + 150')
move_y=$(jq -nr --argjson center "$green_y" '$center - 50')
invoke_drag_move "$move_x" "$move_y" 500 "GreenBox"
move_x=$(jq -nr --argjson center "$drop_x" '$center - 150')
move_y=$(jq -nr --argjson center "$drop_y" '$center + 50')
invoke_drag_move "$move_x" "$move_y" 500 "GreenBox"
invoke_drag_end "$drop_x" "$drop_y" 500 "$drop_path" "GreenBox"
assert_text_equals "$(get_drop_zone_status)" "Dropped: GreenBox" "DropZone after GreenBox"

invoke_drag_to_drop_zone "BlueBox" 900
assert_text_equals "$(get_drop_zone_status)" "Dropped: BlueBox" "DropZone after BlueBox"

printf '[8/8] Exercising the virtual pad...\n'
exercise_virtual_pad
assert_text_equals "$(get_virtual_pad_state)" "Zero" "VirtualPad state"

printf '\n'
printf '=========================================\n'
printf '  RESULT: PASS\n'
printf '=========================================\n'
