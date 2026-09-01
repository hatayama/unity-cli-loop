# Tool Reference

English | [日本語](tools_ja.md)

Detailed descriptions of the tools built into Unity CLI Loop. For the big picture and the design philosophy, see the [README](../../../README.md).

## Development Loop Tools

### 1. compile - Execute Compilation
Performs AssetDatabase.Refresh() and then compiles, returning the results after Domain Reload completes. Can detect errors and warnings that built-in linters cannot find.
You can choose between incremental compilation and forced full compilation.
Use `--no-wait-for-domain-reload` only when you need the fire-and-forget path.
```text
→ Execute compile, analyze error and warning content
→ Automatically fix relevant files
→ Verify with compile again
```

### 2. get-logs - Retrieve Logs Same as Unity Console
Filter by LogType or search target string with advanced search capabilities. You can also choose whether to include stacktrace.
This allows you to retrieve logs while keeping the context small.
**MaxCount behavior**: Returns the latest logs (tail-like behavior). When MaxCount=10, returns the most recent 10 logs.
**Advanced Search Features**:
- **Regular Expression Support**: Use `UseRegex: true` for powerful pattern matching
- **Stack Trace Search**: Use `SearchInStackTrace: true` to search within stack traces
```text
→ get-logs (LogType: Error, SearchText: "NullReference", MaxCount: 10)
→ get-logs (LogType: All, SearchText: "(?i).*error.*", UseRegex: true, MaxCount: 20)
→ get-logs (LogType: All, SearchText: "MyClass", SearchInStackTrace: true, MaxCount: 50)
→ Identify cause from stacktrace, fix relevant code
```

### 3. run-tests - Execute TestRunner (PlayMode, EditMode supported)
Executes Unity Test Runner and retrieves test results. You can set conditions with FilterType and FilterValue.
- FilterType: all (all tests), exact (individual test method name), regex (class name or namespace), assembly (assembly name)
- FilterValue: Value according to filter type (class name, namespace, etc.)
- UnsavedChanges: How to handle unsaved loaded Scene and Prefab Stage changes before tests. `save` (default) writes them, `fail` stops if any remain, `discard` reloads disk state (Untitled scenes fail).
Test results can be output as xml. The output path is returned so AI can read it.
This is also a strategy to avoid consuming context.
```text
→ run-tests (FilterType: exact, FilterValue: "PlayerControllerTests.TestJump")
→ run-tests (--unsaved-changes fail, stop if editor changes are unsaved)
→ Check failed tests, fix implementation to pass tests
```
> [!WARNING]
> During PlayMode test execution, Domain Reload is forcibly turned OFF. (Settings are restored after test completion)
> Note that static variables will not be reset during this period.

### 4. hot-reload - Apply Method-Body Changes Instantly Without Recompiling
Applies the method bodies of edited `.cs` files directly to the running Editor (EditMode / PlayMode) without a recompile or a Domain Reload in between. No attributes or source markers are required, and access to private / internal members, static methods, async methods, and iterators all work. You can fix game logic without leaving PlayMode and see the new behavior on the spot.

Adding new methods and fields is also supported (added members are visible only to edited code in the same file). Adding new types, or members referenced from other files, requires `compile`. Methods that cannot be applied are reported per method as `Skipped` / `Failed`, and one failing method does not stop the rest from applying.

Requiring no prior setup is another distinguishing point. Some existing hot-reload approaches require tagging target methods with an attribute ahead of time and compiling them in, or limit which kinds of methods can be targeted. uloop's hot reload needs none of that preparation and applies to any method of an already-running PlayMode after the fact. Beyond method-body edits, it can also apply changes to local variable types and to method signatures such as return types and parameters.

### 5. pause-point - Pause at Any Line and Read Variables Without Editing Code
Pauses PlayMode at any source `file:line` without editing source or recompiling. It patches the already-compiled method directly, so it can be armed in the middle of a PlayMode session.

The hit response carries `CapturedVariables` — the method's locals, its parameters, and the `this` instance fields, captured **immediately before** the target line runs, exactly like an IDE breakpoint. Because the values are point-in-time strings rather than live references, they remain valid evidence after Unity resumes. This removes the round trip of sprinkling `Debug.Log` and recompiling.

Three capture modes are available. `single-shot` (the default) disarms after the first hit, `continuous` pauses on every hit and keeps a history, and `trace` records hits without pausing. Watch expressions (`enable-watch` / `get-watch-values`) re-evaluate automatically on each paused Step, letting you follow a value frame by frame.

> [!NOTE]
> The Editor's Code Optimization mode must be Debug (enabling is rejected with instructions when it is set to Release). Compilation or a domain reload clears the patch, so re-arm it afterwards.

```text
→ enable-pause-point (File: "Assets/Scripts/Enemy.cs", Line: 42, Await: true,
                      Trigger: "simulate-keyboard --action Press --key Space")
→ Read the locals, parameters, and fields at that moment from CapturedVariables
→ Identify and fix the cause
```

## Unity Editor Automation & Discovery Tools

### 6. clear-console - Log Cleanup
Clear logs that become noise during log searches.
```text
→ clear-console
→ Start new debug session
```

### 7. find-game-objects - Search Scene Objects
Retrieve objects and examine component parameters. Also retrieve information about currently selected GameObjects (multiple selection supported) in Unity Editor.
```text
→ find-game-objects (RequiredComponents: ["Camera"])
→ Investigate Camera component parameters

→ find-game-objects (SearchMode: "Selected")
→ Get detailed information about currently selected GameObjects in Unity Editor (supports multiple selection)
```

### 8. get-hierarchy - Analyze Scene Structure
Retrieve information about the currently active Hierarchy in nested JSON format. Works at runtime as well.
**Automatic File Export**: Retrieved hierarchy data is always saved as JSON in `{project_root}/.uloop/outputs/HierarchyResults/` directory. The response only returns the file path, minimizing token consumption even for large datasets.
**Selection Mode**: Use `uloop get-hierarchy --use-selection` to get hierarchy starting from currently selected GameObject(s) in Unity Editor. Supports multiple selection - when parent and child are both selected, only the parent is used as root to avoid duplicate traversal.
```text
→ Understand parent-child relationships between GameObjects, discover and fix structural issues
→ Regardless of scene size, hierarchy data is saved to a file and the path is returned instead of raw JSON
→ uloop get-hierarchy --use-selection
→ Get hierarchy of currently selected GameObjects without specifying paths manually
```

### 9. focus-window - Bring Unity Editor Window to Front (macOS & Windows)
Ensures the Unity Editor window becomes the foreground application on macOS and Windows Editor builds.
Great for keeping visual feedback in sync after other apps steal focus. (Linux is currently unsupported.)

### 10. screenshot - Take a Screenshot of EditorWindow
Take a screenshot of any EditorWindow as a PNG. Specify the window name (the text displayed in the title bar/tab) to capture.
When multiple windows of the same type are open (e.g., 3 Inspector windows), all windows are saved with numbered filenames.
Supports three matching modes: `exact` (default), `prefix`, and `contains` - all case-insensitive.

Use `CaptureMode: rendering` to capture the Game View's rendering output directly instead of the EditorWindow's appearance. This is for grabbing the in-game picture during PlayMode without the Editor's window chrome or scaling getting in the way.
Add `AnnotateRaycastGrid: true` to overlay a coordinate grid on the captured image, which makes it easier for an AI looking at the image to pick coordinates to pass to `simulate-mouse-input`.

Use `uloop set-game-view-size --width 1920 --height 1080` to pin a custom Game View resolution. Do this when you want the coordinate space of `CaptureMode: rendering` to stay stable across runs (run it without arguments to read the current resolution).

Use `uloop record-video` to record the Game View while Play Mode runs. Start returns immediately so other commands can run during capture; Stop finalizes an MP4 (WebM on Linux) under `.uloop/outputs/Videos/`:
```text
uloop record-video --action Start
uloop simulate-keyboard --key W --duration 2
uloop record-video --action Stop
```
```text
→ screenshot (WindowName: "Console")
→ Save Console window state as PNG
→ Provide visual feedback to AI
```

### 11. control-play-mode - Control Play Mode
Control Unity Editor's Play Mode. Supports three actions: Play (start/resume), Stop, and Pause.
```text
→ control-play-mode (Action: Play)
→ Start Play Mode to verify game behavior
→ control-play-mode (Action: Pause)
→ Pause to inspect state
```

### 12. execute-dynamic-code - Dynamic C# Code Execution
Execute C# code dynamically within Unity Editor.

Async support:
- You can write await in your snippet (Task/ValueTask/UniTask and any awaitable type)
- Cancellation is propagated when you pass a CancellationToken to the tool

When enabled, dynamic code execution runs with full Unity Editor process permissions and can use Unity APIs, .NET APIs, and project assemblies. Disable this tool with the Tool Settings toggle when AI agents should not execute arbitrary C#.
```text
→ execute-dynamic-code (Code: "GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube); return \"Cube created\";")
→ Rapid prototype verification, batch processing automation
→ Full Unity Editor API access for trusted automation
```

## PlayMode Automated Testing Tools

### 13. simulate-mouse-ui - Simulate Mouse Input on PlayMode UI
Simulate mouse click, long-press, and drag on PlayMode UI elements. Uses EventSystem and ExecuteEvents to dispatch pointer events directly — works independently of both old and new Input System. For game logic that reads Input System (e.g. `Mouse.current.leftButton.wasPressedThisFrame`), use `simulate-mouse-input` instead.

Supports 6 actions: Click, LongPress, Drag (one-shot), DragStart/DragMove/DragEnd (split drag).

```text
→ screenshot (CaptureMode: rendering, AnnotateElements: true)
→ Get element coordinates from AnnotatedElements (SimX/SimY)
→ simulate-mouse-ui (Action: Click, X: 400, Y: 300)
→ simulate-mouse-ui (Action: LongPress, X: 400, Y: 300, Duration: 5.0)
→ simulate-mouse-ui (Action: Drag, FromX: 100, FromY: 500, X: 400, Y: 300)
→ simulate-mouse-ui (Action: DragStart, X: 100, Y: 500)
→ simulate-mouse-ui (Action: DragMove, X: 200, Y: 400, DragSpeed: 300)
→ simulate-mouse-ui (Action: DragEnd, X: 400, Y: 300)
```
https://github.com/user-attachments/assets/c7ee9103-c282-4f90-8b01-64bb17400f3e

### 14. simulate-mouse-input - Simulate Mouse Input in PlayMode via Input System
Simulate mouse input in PlayMode via Input System. Injects button clicks, mouse delta, and scroll wheel directly into `Mouse.current`. Unlike `simulate-mouse-ui`, which fires EventSystem pointer events, this tool targets game logic that reads `Mouse.current` directly. It is available only when the Input System package is installed, and Active Input Handling must be set to `Input System Package (New)` or `Both` in Player Settings.

Supports 5 actions: Click, LongPress, MoveDelta, SmoothDelta, Scroll.

Add `--dry-run` to check what a Game View coordinate hits in 3D physics (GameObject name and path, layer, distance, hit point and normal) instead of injecting mouse input. Use it to confirm a coordinate you picked from a screenshot before sending the click. Dry-run also works in EditMode and does not require the Input System package.

```text
→ simulate-mouse-input (DryRun: true, X: 400, Y: 300)
→ simulate-mouse-input (Action: Click, X: 400, Y: 300)
→ simulate-mouse-input (Action: Click, X: 400, Y: 300, Button: Right)
→ simulate-mouse-input (Action: LongPress, X: 400, Y: 300, Duration: 2.0)
→ simulate-mouse-input (Action: MoveDelta, DeltaX: 100, DeltaY: 0)
→ simulate-mouse-input (Action: Scroll, ScrollY: 120)
→ simulate-mouse-input (Action: SmoothDelta, DeltaX: 300, DeltaY: 0, Duration: 0.5)
```

### 15. simulate-keyboard - Simulate Keyboard Input in PlayMode
Simulate keyboard key input in PlayMode via Input System. Supports single key taps, sustained holds, and multi-key combinations (e.g. Shift+W for sprinting). This tool is available only when the Input System package is installed, and Active Input Handling must be set to `Input System Package (New)` or `Both` in Player Settings. Game code must read input via Input System API (e.g. `Keyboard.current[Key.W].isPressed`), not legacy `Input.GetKey()`.

Supports 3 actions: Press (one-shot tap or timed hold), KeyDown (hold key down), KeyUp (release held key). Use Press for edge-triggered gameplay such as `Keyboard.current.spaceKey.wasPressedThisFrame`; KeyDown emits only one initial press edge and then becomes held state, so use KeyDown/KeyUp only when the test intentionally needs a held key.

```text
→ simulate-keyboard (Action: Press, Key: Space)
→ simulate-keyboard (Action: Press, Key: W, Duration: 2.0)
→ simulate-keyboard (Action: KeyDown, Key: LeftShift)
→ simulate-keyboard (Action: KeyDown, Key: W)
→ screenshot (CaptureMode: rendering)
→ simulate-keyboard (Action: KeyUp, Key: W)
→ simulate-keyboard (Action: KeyUp, Key: LeftShift)
```

### 16. replay-input - Replay Recorded Input During PlayMode
Replay recorded keyboard and mouse input during PlayMode. Loads a JSON recording and injects input frame-by-frame via Input System. Supports looping and progress monitoring. This tool is available only when the Input System package is installed. Create recording files first in the Unity Editor from **Window > Unity CLI Loop > Recordings** using **Start Recording** and **Stop Recording**. There is no CLI command for recording input.

```text
→ replay-input (Action: Start)
→ replay-input (Action: Start, InputPath: "scripts/my-play.json", Loop: true)
→ replay-input (Action: Status)
→ replay-input (Action: Stop)
```

Terminal-driven E2E coverage is available through one runner per shell family:

```bash
sh scripts/run-posix-e2e.sh --project-path /path/to/unity-project
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-windows-e2e.ps1
```

`run-posix-e2e.sh` uses the built native CLI binary by default, passes an explicit `--project-path` to every `uloop` invocation, and runs CLI recovery/readiness and simulate-mouse UI coverage in one sequence. To verify `replay-input` against a JSON created in the Recordings window, run `verify-replay-via-cli.sh` separately.
