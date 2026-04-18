# PlayMode Automation

Code examples for runtime automation during Play mode using `execute-dynamic-code`.
These examples manipulate live scene objects while the game is running.

## Click UI Button by Path

```csharp
using UnityEngine.UI;

Button btn = GameObject.Find("Canvas/StartButton")?.GetComponent<Button>();
if (btn == null) return "Button not found";

btn.onClick.Invoke();
return $"Clicked {btn.gameObject.name}";
```

## Click UI Button by Search

```csharp
using UnityEngine.UI;
using System.Linq;

Button[] buttons = Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
Button target = buttons.FirstOrDefault(b => b.gameObject.name == "PlayButton");
if (target == null) return $"PlayButton not found. Available: {string.Join(", ", buttons.Select(b => b.gameObject.name))}";

target.onClick.Invoke();
return $"Clicked {target.gameObject.name}";
```

## Raycast from Camera Center

```csharp
Camera cam = Camera.main;
if (cam == null) return "Main camera not found";

Ray ray = cam.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0));
if (Physics.Raycast(ray, out RaycastHit hit, 100f))
{
    return $"Hit: {hit.collider.gameObject.name} at {hit.point}";
}
return "No hit";
```

## Raycast Click at Screen Position

```csharp
using UnityEngine.EventSystems;
using System.Collections.Generic;

if (EventSystem.current == null) return "EventSystem not found";

PointerEventData pointerData = new PointerEventData(EventSystem.current)
{
    position = new Vector2(Screen.width / 2f, Screen.height / 2f)
};

List<RaycastResult> results = new List<RaycastResult>();
EventSystem.current.RaycastAll(pointerData, results);

if (results.Count == 0) return "No UI element at screen center";

GameObject target = results[0].gameObject;
ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerClickHandler);
return $"Clicked UI element: {target.name}";
```

## Toggle GameObject Active State

```csharp
GameObject obj = GameObject.Find("Enemy");
if (obj == null) return "Enemy not found";

obj.SetActive(!obj.activeSelf);
return $"{obj.name} is now {(obj.activeSelf ? "active" : "inactive")}";
```

## Invoke Method on MonoBehaviour

```csharp
using System.Reflection;

GameObject player = GameObject.Find("Player");
if (player == null) return "Player not found";

MonoBehaviour script = player.GetComponent("PlayerController") as MonoBehaviour;
if (script == null) return "PlayerController not found";

MethodInfo method = script.GetType().GetMethod("TakeDamage");
if (method == null) return "TakeDamage method not found";

method.Invoke(script, new object[] { 10f });
return $"Invoked TakeDamage(10) on {player.name}";
```

## Set Field Value at Runtime

```csharp
using System.Reflection;

GameObject player = GameObject.Find("Player");
if (player == null) return "Player not found";

MonoBehaviour script = player.GetComponent("PlayerController") as MonoBehaviour;
if (script == null) return "PlayerController not found";

FieldInfo field = script.GetType().GetField("moveSpeed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
if (field == null) return "moveSpeed field not found";

field.SetValue(script, 20f);
return $"Set moveSpeed to 20 on {player.name}";
```

## Move Player to Position

```csharp
GameObject player = GameObject.Find("Player");
if (player == null) return "Player not found";

Vector3 targetPos = new Vector3(5f, 0f, 10f);
player.transform.position = targetPos;
return $"Moved {player.name} to {targetPos}";
```

---

# Tool Combination Workflows

Examples of combining `execute-dynamic-code` with other uloop tools for multi-step PlayMode automation.

## find-game-objects → Click Button

Use `find-game-objects` to discover buttons with their hierarchy paths, then click one via `execute-dynamic-code`.

**Step 1**: Find all GameObjects with Button component

```bash
uloop find-game-objects --required-components UnityEngine.UI.Button --include-inactive false
```

**Step 2**: Click the target button using the path from Step 1

```csharp
using UnityEngine.UI;

// Use the path returned by find-game-objects (e.g. "Canvas/MainMenu/StartButton")
GameObject btnObj = GameObject.Find("Canvas/MainMenu/StartButton");
if (btnObj == null) return "Button not found at path";

Button btn = btnObj.GetComponent<Button>();
if (btn == null) return "No Button component";

btn.onClick.Invoke();
return $"Clicked {btnObj.name}";
```

## get-hierarchy → Navigate UI and Click

Use `get-hierarchy` to explore the UI tree structure, then target the right element.

**Step 1**: Get Canvas hierarchy to understand UI structure

```bash
uloop get-hierarchy --root-path "Canvas" --max-depth 3 --include-components true
```

**Step 2**: Based on the hierarchy JSON, click the desired button

```csharp
using UnityEngine.UI;

// Path identified from hierarchy output
GameObject btnObj = GameObject.Find("Canvas/SettingsPanel/AudioTab/MuteToggle");
if (btnObj == null) return "MuteToggle not found";

Toggle toggle = btnObj.GetComponent<Toggle>();
if (toggle != null)
{
    toggle.isOn = !toggle.isOn;
    return $"Toggled {btnObj.name} to {toggle.isOn}";
}
return "No Toggle component found";
```

## Execute Action → Screenshot to Verify

Run an action then capture a screenshot to visually confirm the result.

**Step 1**: Perform the action

```csharp
using UnityEngine.UI;

Button btn = GameObject.Find("Canvas/PlayButton")?.GetComponent<Button>();
if (btn == null) return "PlayButton not found";

btn.onClick.Invoke();
return "Clicked PlayButton";
```

**Step 2**: Capture Game View to verify the result

```bash
uloop screenshot --window-name Game
```

## Execute Action → Check Logs for Side Effects

Run an action then inspect Unity Console logs to verify expected behavior.

**Step 1**: Clear console before the action

```bash
uloop clear-console
```

**Step 2**: Perform the action

```csharp
using System.Reflection;

GameObject player = GameObject.Find("Player");
if (player == null) return "Player not found";

MonoBehaviour script = player.GetComponent("PlayerController") as MonoBehaviour;
if (script == null) return "PlayerController not found";

MethodInfo method = script.GetType().GetMethod("TakeDamage");
if (method == null) return "TakeDamage method not found";

method.Invoke(script, new object[] { 50f });
return "Invoked TakeDamage(50)";
```

**Step 3**: Check logs for expected output

```bash
uloop get-logs --log-type Log --search-text "damage"
```

## Full Automation: Play → Act → Capture → Stop

End-to-end test flow: start Play mode, perform actions, capture evidence, stop.

**Step 0**: Clear console to isolate this run

```bash
uloop clear-console
```

**Step 1**: Start Play mode

```bash
uloop control-play-mode --action Play
```

**Step 2**: Wait for scene initialization, then find and click a button

```csharp
using UnityEngine.UI;
using System.Linq;

// Scene may need a moment to initialize after Play starts
Button[] buttons = Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
Button startBtn = buttons.FirstOrDefault(b => b.gameObject.name.Contains("Start"));
if (startBtn == null) return $"Start button not found. Available: {string.Join(", ", buttons.Select(b => b.gameObject.name))}";

startBtn.onClick.Invoke();
return $"Clicked {startBtn.gameObject.name}";
```

**Step 3**: Capture screenshot as evidence

```bash
uloop screenshot --window-name Game
```

**Step 4**: Check logs for errors

```bash
uloop get-logs --log-type Error
```

**Step 5**: Stop Play mode

```bash
uloop control-play-mode --action Stop
```
