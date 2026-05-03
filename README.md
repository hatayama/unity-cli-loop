# Unity CLI Loop
[日本語](README_ja.md)

[![Unity](https://img.shields.io/badge/Unity-2022.3+-red.svg)](https://unity3d.com/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE.md)<br>
![ClaudeCode](https://img.shields.io/badge/Claude_Code-555?logo=claude)
![Cursor](https://img.shields.io/badge/Cursor-111?logo=Cursor)
![Codex](https://img.shields.io/badge/Codex-111?logo=data:image/svg+xml;base64,PHN2ZyByb2xlPSJpbWciIHZpZXdCb3g9IjAgMCAyNCAyNCIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj48cGF0aCBmaWxsPSJ3aGl0ZSIgZD0iTTIyLjI4MTkgOS44MjExYTUuOTg0NyA1Ljk4NDcgMCAwIDAtLjUxNTctNC45MTA4IDYuMDQ2MiA2LjA0NjIgMCAwIDAtNi41MDk4LTIuOUE2LjA2NTEgNi4wNjUxIDAgMCAwIDQuOTgwNyA0LjE4MThhNS45ODQ3IDUuOTg0NyAwIDAgMC0zLjk5NzcgMi45IDYuMDQ2MiA2LjA0NjIgMCAwIDAgLjc0MjcgNy4wOTY2IDUuOTggNS45OCAwIDAgMCAuNTExIDQuOTEwNyA2LjA1MSA2LjA1MSAwIDAgMCA2LjUxNDYgMi45MDAxQTUuOTg0NyA1Ljk4NDcgMCAwIDAgMTMuMjU5OSAyNGE2LjA1NTcgNi4wNTU3IDAgMCAwIDUuNzcxOC00LjIwNTggNS45ODk0IDUuOTg5NCAwIDAgMCAzLjk5NzctMi45MDAxIDYuMDU1NyA2LjA1NTcgMCAwIDAtLjc0NzUtNy4wNzI5em0tOS4wMjIgMTIuNjA4MWE0LjQ3NTUgNC40NzU1IDAgMCAxLTIuODc2NC0xLjA0MDhsLjE0MTktLjA4MDQgNC43NzgzLTIuNzU4MmEuNzk0OC43OTQ4IDAgMCAwIC4zOTI3LS42ODEzdi02LjczNjlsMi4wMiAxLjE2ODZhLjA3MS4wNzEgMCAwIDEgLjAzOC4wNTJ2NS41ODI2YTQuNTA0IDQuNTA0IDAgMCAxLTQuNDk0NSA0LjQ5NDR6bS05LjY2MDctNC4xMjU0YTQuNDcwOCA0LjQ3MDggMCAwIDEtLjUzNDYtMy4wMTM3bC4xNDIuMDg1MiA0Ljc4MyAyLjc1ODJhLjc3MTIuNzcxMiAwIDAgMCAuNzgwNiAwbDUuODQyOC0zLjM2ODV2Mi4zMzI0YS4wODA0LjA4MDQgMCAwIDEtLjAzMzIuMDYxNUw5Ljc0IDE5Ljk1MDJhNC40OTkyIDQuNDk5MiAwIDAgMS02LjE0MDgtMS42NDY0ek0yLjM0MDggNy44OTU2YTQuNDg1IDQuNDg1IDAgMCAxIDIuMzY1NS0xLjk3MjhWMTEuNmEuNzY2NC43NjY0IDAgMCAwIC4zODc5LjY3NjVsNS44MTQ0IDMuMzU0My0yLjAyMDEgMS4xNjg1YS4wNzU3LjA3NTcgMCAwIDEtLjA3MSAwbC00LjgzMDMtMi43ODY1QTQuNTA0IDQuNTA0IDAgMCAxIDIuMzQwOCA3Ljg3MnptMTYuNTk2MyAzLjg1NThMMTMuMTAzOCA4LjM2NCAxNS4xMTkyIDcuMmEuMDc1Ny4wNzU3IDAgMCAxIC4wNzEgMGw0LjgzMDMgMi43OTEzYTQuNDk0NCA0LjQ5NDQgMCAwIDEtLjY3NjUgOC4xMDQydi01LjY3NzJhLjc5Ljc5IDAgMCAwLS40MDctLjY2N3ptMi4wMTA3LTMuMDIzMWwtLjE0Mi0uMDg1Mi00Ljc3MzUtMi43ODE4YS43NzU5Ljc3NTkgMCAwIDAtLjc4NTQgMEw5LjQwOSA5LjIyOTdWNi44OTc0YS4wNjYyLjA2NjIgMCAwIDEgLjAyODQtLjA2MTVsNC44MzAzLTIuNzg2NmE0LjQ5OTIgNC40OTkyIDAgMCAxIDYuNjgwMiA0LjY2ek04LjMwNjUgMTIuODYzbC0yLjAyLTEuMTYzOGEuMDgwNC4wODA0IDAgMCAxLS4wMzgtLjA1NjdWNi4wNzQyYTQuNDk5MiA0LjQ5OTIgMCAwIDEgNy4zNzU3LTMuNDUzN2wtLjE0Mi4wODA1TDguNzA0IDUuNDU5YS43OTQ4Ljc5NDggMCAwIDAtLjM5MjcuNjgxM3ptMS4wOTc2LTIuMzY1NGwyLjYwMi0xLjQ5OTggMi42MDY5IDEuNDk5OHYyLjk5OTRsLTIuNTk3NCAxLjQ5OTctMi42MDY3LTEuNDk5N1oiLz48L3N2Zz4=)
![Antigravity](https://img.shields.io/badge/Antigravity-111?logo=google)
![GitHubCopilot](https://img.shields.io/badge/GitHub_Copilot-111?logo=githubcopilot)

<p align="center">
    <img height="450" alt="logo-black-bg" src="https://github.com/user-attachments/assets/fca3047f-9042-4bf9-83bd-58b03f061082" /><br>
    <sub>(Logo inspired by Daft Punk's <i>Discovery</i> album art)</sub>  
</p>
  

Let an AI agent compile, test, and operate your Unity project from popular LLM tools via CLI.

Designed to keep AI-driven development loops running autonomously inside your existing Unity projects.

> **Note**: This project was formerly known as **uLoopMCP**.

# Concept
Unity CLI Loop is a Unity integration tool designed so that **AI can drive your Unity project forward with minimal human intervention**.
Tasks that humans typically handle manually—compiling, running the Test Runner, checking logs, editing scenes, and capturing windows to verify UI layouts—are exposed as tools that LLMs can orchestrate.

Unity CLI Loop is built around four core ideas:

1. **A self-hosted development loop where AI autonomously compiles, tests, inspects logs, and fixes issues.** Uses `compile`, `run-tests`, `get-logs`, `clear-console`.
2. **AI-driven Unity Editor operation—scene building, object manipulation, menu execution, and UI refinement from screenshots.** Uses `execute-dynamic-code`, `screenshot`.
3. **PlayMode automated testing—AI clicks buttons, drags elements, presses keys, records and replays input, and verifies game behavior.** Uses `simulate-mouse-ui`, `simulate-mouse-input`, `simulate-keyboard`, `record-input`, `replay-input`, `execute-dynamic-code`, `screenshot`.
4. **Achieving the above with a minimal set of tools.** See [Design Philosophy](#design-philosophy).

https://github.com/user-attachments/assets/569a2110-7351-4cf3-8281-3a83fe181817

# Installation

> [!WARNING]
> The following software is required
>
> - **Unity 2022.3 or later**

## Via Unity Package Manager

1. Open Unity Editor
2. Open Window > Package Manager
3. Click the "+" button
4. Select "Add package from git URL"
5. Enter the following URL:
```text
https://github.com/hatayama/unity-cli-loop.git?path=/Packages/src
```

> **If you installed via git URL before v1.0.0**: The repository was renamed from `uLoopMCP` to `unity-cli-loop` in v1.0.0. Please update your `manifest.json`:
> ```text
> Old: https://github.com/hatayama/uLoopMCP.git?path=/Packages/src
> New: https://github.com/hatayama/unity-cli-loop.git?path=/Packages/src
> ```
> The old URL still works via GitHub redirect, but updating is recommended. OpenUPM users are not affected.

## Via OpenUPM (Recommended)

## Using Scoped registry in Unity Package Manager
1. Open Project Settings window and go to Package Manager page
2. Add the following entry to the Scoped Registries list:
```text
Name: OpenUPM
URL: https://package.openupm.com
Scope(s): io.github.hatayama.uloopmcp
```

3. Open Package Manager window and select OpenUPM in the My Registries section. Unity CLI Loop will be displayed.

> [!NOTE]
> `com.unity.inputsystem` is now an optional dependency. Install it only if you want Input System features such as `simulate-keyboard`, `simulate-mouse-input`, `record-input`, `replay-input`, and the Recordings window.

# Quickstart

## Step 1: Install the CLI

Select Window > Unity CLI Loop > Settings. A dedicated window will open. If the **CLI** button is not highlighted in blue, press **Install CLI**.

<details>
<summary>CLI-only terminal install</summary>

Use this only when you want to install the standalone global CLI without opening Unity package setup.

```bash
curl -fsSL https://raw.githubusercontent.com/hatayama/unity-cli-loop/main/scripts/install.sh | sh
```

On Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/hatayama/unity-cli-loop/main/scripts/install.ps1 | iex
```

On Windows, set `ULOOP_REMOVE_LEGACY=1` to remove package-owned legacy `uloop` launcher shims and an unused legacy command bin directory from User PATH:

```powershell
$env:ULOOP_REMOVE_LEGACY = "1"
irm https://raw.githubusercontent.com/hatayama/unity-cli-loop/main/scripts/install.ps1 | iex
```

</details>

<img width="277" height="306" alt="1" src="https://github.com/user-attachments/assets/0e25c327-73bf-4af6-997b-eebb3c26b372" />

The Settings window shows whether the global `uloop` command is detected.



If you see the following display, the installation was successful.  
<img width="272" height="309" alt="2" src="https://github.com/user-attachments/assets/ec14f73b-53be-4435-af95-84bb9125e3e4" />

## Step 2: Install Skills

Select your target (Claude Code, Codex, etc.) and press the **Install Skills** button.  
<img width="272" height="306" alt="3" src="https://github.com/user-attachments/assets/492e9680-726a-425b-8bf1-e5983f4f15a5" />



<details>
<summary>To install from terminal</summary>

```bash
# Install for Claude Code project
uloop skills install --claude

# Install for OpenAI Codex project
uloop skills install --codex

# Or install globally
uloop skills install --claude --global
```
</details>

That's it! After installing Skills, LLM tools can automatically handle instructions like these:

| Your Instruction | Skill Used by LLM Tools |
|---|---|
| "Launch Unity for this project" | `/uloop-launch` |
| "Fix the compile errors" | `/uloop-compile` |
| "Run the tests and tell me why they failed" | `/uloop-run-tests` + `/uloop-get-logs` |
| "Check the scene hierarchy" | `/uloop-get-hierarchy` |
| "Play the game and bring Unity to the front" | `/uloop-control-play-mode` + `/uloop-focus-window` |
| "Bulk-update prefab parameters" | `/uloop-execute-dynamic-code` |
| "Take a screenshot of Game View and adjust the UI layout" | `/uloop-screenshot` + `/uloop-execute-dynamic-code` |
| "Record my gameplay input" | `/uloop-record-input` |
| "Replay the recorded input" | `/uloop-replay-input` |


<details>
<summary>All 16 Bundled Skills</summary>

- `/uloop-launch` - Launch Unity with correct version
- `/uloop-compile` - Execute compilation
- `/uloop-get-logs` - Get console logs
- `/uloop-run-tests` - Run tests
- `/uloop-clear-console` - Clear console
- `/uloop-focus-window` - Bring Unity Editor to front
- `/uloop-get-hierarchy` - Get scene hierarchy
- `/uloop-find-game-objects` - Find GameObjects
- `/uloop-screenshot` - Capture EditorWindow
- `/uloop-simulate-mouse-ui` - Simulate mouse click, long-press, and drag on PlayMode UI elements
- `/uloop-simulate-mouse-input` - Simulate mouse input in PlayMode via Input System
- `/uloop-simulate-keyboard` - Simulate keyboard input in PlayMode via Input System
- `/uloop-record-input` - Record keyboard and mouse input during PlayMode
- `/uloop-replay-input` - Replay recorded input during PlayMode
- `/uloop-control-play-mode` - Control Play Mode
- `/uloop-execute-dynamic-code` - Execute dynamic C# code

</details>

<details>
<summary>Direct CLI Usage (Advanced)</summary>

You can also call the CLI directly without using Skills:

```bash
# List available tools
uloop list

# Launch Unity project with correct version
uloop launch

# Launch with build target (Android, iOS, StandaloneOSX, etc.)
uloop launch -p Android

# Kill running Unity and restart
uloop launch -r

# Execute compilation
uloop compile

# Compile and wait for Domain Reload to complete
uloop compile --wait-for-domain-reload

# Get logs
uloop get-logs --max-count 10

# Run tests
uloop run-tests --filter-type all

# Execute dynamic code
uloop execute-dynamic-code --code 'using UnityEngine; Debug.Log("Hello from CLI!");'
```

</details>

<details>
<summary>Shell Completion (Optional)</summary>

You can install Bash/Zsh/PowerShell completion:

```bash
# Add completion script to shell config (auto-detects shell)
uloop completion --install

# Explicitly specify shell (when auto-detection fails on Windows)
uloop completion --shell bash --install        # Git Bash / MINGW64
uloop completion --shell powershell --install  # PowerShell

# Check completion script
uloop completion
```

</details>

## Project Path Specification

If `--project-path` is omitted, the port is automatically selected from the Unity project detected in the current directory.

To operate multiple Unity instances from a single LLM tool, explicitly specify a project path:

```bash
# Specify by project path (absolute or relative)
uloop compile --project-path /Users/foo/my-unity-project
uloop compile --project-path ../other-project
```

# Design Philosophy

Unity CLI Loop does not chase tool count. With dynamic C# code execution (`execute-dynamic-code`), almost any Unity Editor operation can be accomplished through a single tool.

Too many tools make it harder for AI to choose the right one. And even when tools are packaged as Skills, each tool's description still consumes the context window. We believe keeping tools to the essential minimum is good design.

Dedicated tools exist only for operations that dynamic code execution cannot handle by nature — such as frame-spanning input simulation and screenshot capture — and for operations called so frequently in the development loop, like `compile` and `get-logs`, that generating C# code each time would waste tokens.

# Key Features
## Development Loop Tools
### 1. compile - Execute Compilation
Performs AssetDatabase.Refresh() and then compiles, returning the results. Can detect errors and warnings that built-in linters cannot find.
You can choose between incremental compilation and forced full compilation.
With `WaitForDomainReload=true`, results are returned after Domain Reload completes, regardless of the `ForceRecompile` value.
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
```
→ get-logs (LogType: Error, SearchText: "NullReference", MaxCount: 10)
→ get-logs (LogType: All, SearchText: "(?i).*error.*", UseRegex: true, MaxCount: 20)
→ get-logs (LogType: All, SearchText: "MyClass", SearchInStackTrace: true, MaxCount: 50)
→ Identify cause from stacktrace, fix relevant code
```

### 3. run-tests - Execute TestRunner (PlayMode, EditMode supported)
Executes Unity Test Runner and retrieves test results. You can set conditions with FilterType and FilterValue.
- FilterType: all (all tests), exact (individual test method name), regex (class name or namespace), assembly (assembly name)
- FilterValue: Value according to filter type (class name, namespace, etc.)
- SaveBeforeRun: Saves unsaved loaded Scene changes and current Prefab Stage changes before running tests when explicitly enabled
Test results can be output as xml. The output path is returned so AI can read it.
This is also a strategy to avoid consuming context.
```text
→ run-tests (FilterType: exact, FilterValue: "PlayerControllerTests.TestJump")
→ run-tests (SaveBeforeRun: true)
→ Check failed tests, fix implementation to pass tests
```
> [!WARNING]
> During PlayMode test execution, Domain Reload is forcibly turned OFF. (Settings are restored after test completion)
> Note that static variables will not be reset during this period.

### Unity Editor Automation & Discovery Tools
### 4. clear-console - Log Cleanup
Clear logs that become noise during log searches.
```text
→ clear-console
→ Start new debug session
```

### 5. find-game-objects - Search Scene Objects
Retrieve objects and examine component parameters. Also retrieve information about currently selected GameObjects (multiple selection supported) in Unity Editor.
```text
→ find-game-objects (RequiredComponents: ["Camera"])
→ Investigate Camera component parameters

→ find-game-objects (SearchMode: "Selected")
→ Get detailed information about currently selected GameObjects in Unity Editor (supports multiple selection)
```

### 6. get-hierarchy - Analyze Scene Structure
Retrieve information about the currently active Hierarchy in nested JSON format. Works at runtime as well.
**Automatic File Export**: Retrieved hierarchy data is always saved as JSON in `{project_root}/.uloop/outputs/HierarchyResults/` directory. The response only returns the file path, minimizing token consumption even for large datasets.
**Selection Mode**: Use `UseSelection: true` to get hierarchy starting from currently selected GameObject(s) in Unity Editor. Supports multiple selection - when parent and child are both selected, only the parent is used as root to avoid duplicate traversal.
```text
→ Understand parent-child relationships between GameObjects, discover and fix structural issues
→ Regardless of scene size, hierarchy data is saved to a file and the path is returned instead of raw JSON
→ get-hierarchy (UseSelection: true)
→ Get hierarchy of currently selected GameObjects without specifying paths manually
```

### 7. focus-window - Bring Unity Editor Window to Front (macOS & Windows)
Ensures the Unity Editor window becomes the foreground application on macOS and Windows Editor builds.
Great for keeping visual feedback in sync after other apps steal focus. (Linux is currently unsupported.)

### 8. screenshot - Take a Screenshot of EditorWindow
Take a screenshot of any EditorWindow as a PNG. Specify the window name (the text displayed in the title bar/tab) to capture.
When multiple windows of the same type are open (e.g., 3 Inspector windows), all windows are saved with numbered filenames.
Supports three matching modes: `exact` (default), `prefix`, and `contains` - all case-insensitive.
```text
→ screenshot (WindowName: "Console")
→ Save Console window state as PNG
→ Provide visual feedback to AI
```

### 9. control-play-mode - Control Play Mode
Control Unity Editor's Play Mode. Supports three actions: Play (start/resume), Stop, and Pause.
```
→ control-play-mode (Action: Play)
→ Start Play Mode to verify game behavior
→ control-play-mode (Action: Pause)
→ Pause to inspect state
```

### 10. execute-dynamic-code - Dynamic C# Code Execution
Execute C# code dynamically within Unity Editor.

Async support:
- You can write await in your snippet (Task/ValueTask/UniTask and any awaitable type)
- Cancellation is propagated when you pass a CancellationToken to the tool

**Security Level Support**: Implements 2-tier security control to restrict executable code. To disable this tool entirely, use the tool on/off toggle in the Tool Settings UI.

  - **Level 1 - Restricted** 【Recommended Setting】
    - All Unity APIs and .NET standard libraries are generally available
    - User-defined assemblies (Assembly-CSharp, etc.) are also accessible
    - Only pinpoint blocking of security-critical operations:
      - **File deletion**: `File.Delete`, `Directory.Delete`, `FileUtil.DeleteFileOrDirectory`
      - **File writing**: `File.WriteAllText`, `File.WriteAllBytes`, `File.Replace`
      - **Network communication**: All `HttpClient`, `WebClient`, `WebRequest`, `Socket`, `TcpClient` operations
      - **Process execution**: `Process.Start`, `Process.Kill`
      - **Dynamic code execution**: `Assembly.Load*`, `Type.InvokeMember`, `Activator.CreateComInstanceFrom`
      - **Thread manipulation**: Direct `Thread`, `Task` manipulation
      - **Registry operations**: All `Microsoft.Win32` namespace operations
    - Safe operations are allowed:
      - File reading (`File.ReadAllText`, `File.Exists`, etc.)
      - Path operations (all `Path.*` operations)
      - Information retrieval (`Assembly.GetExecutingAssembly`, `Type.GetType`, etc.)
    - Use cases: Normal Unity development, automation with safety assurance

  - **Level 2 - FullAccess**
    - **All assemblies are accessible (no restrictions)**
    - ⚠️ **Warning**: Security risks exist, use only with trusted code
```
→ execute-dynamic-code (Code: "GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube); return \"Cube created\";")
→ Rapid prototype verification, batch processing automation
→ Unity API usage restricted according to security level
```


> [!IMPORTANT]
> **Security Settings**
>
> Custom tools are always available. Use individual tool toggles to hide tools from AI agents when needed.
>
> **Dynamic Code Security Level** (`execute-dynamic-code` tool):
> - **Level 1 (Restricted)**: Unity API only, dangerous operations blocked (recommended)
> - **Level 2 (FullAccess)**: All APIs available (use with caution)
>
> To disable `execute-dynamic-code` entirely, turn it off using the tool on/off toggle.
>
> Setting changes take effect immediately without reinstalling the CLI.
>

### PlayMode Automated Testing Tools
### 11. simulate-mouse-ui - Simulate Mouse Input on PlayMode UI
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

### 12. simulate-mouse-input - Simulate Mouse Input in PlayMode via Input System
Simulate mouse input in PlayMode via Input System. Injects button clicks, mouse delta, and scroll wheel directly into `Mouse.current` for game logic that reads Input System. Unlike `simulate-mouse-ui` which fires EventSystem pointer events for uGUI, this tool targets game logic that reads `Mouse.current` directly. This tool is available only when the Input System package is installed, and Active Input Handling must be set to `Input System Package (New)` or `Both` in Player Settings.

Supports 5 actions: Click, LongPress, MoveDelta, SmoothDelta, Scroll.

```text
→ simulate-mouse-input (Action: Click, X: 400, Y: 300)
→ simulate-mouse-input (Action: Click, X: 400, Y: 300, Button: Right)
→ simulate-mouse-input (Action: LongPress, X: 400, Y: 300, Duration: 2.0)
→ simulate-mouse-input (Action: MoveDelta, DeltaX: 100, DeltaY: 0)
→ simulate-mouse-input (Action: Scroll, ScrollY: 120)
→ simulate-mouse-input (Action: SmoothDelta, DeltaX: 300, DeltaY: 0, Duration: 0.5)
```

### 13. simulate-keyboard - Simulate Keyboard Input in PlayMode
Simulate keyboard key input in PlayMode via Input System. Supports single key taps, sustained holds, and multi-key combinations (e.g. Shift+W for sprinting). This tool is available only when the Input System package is installed, and Active Input Handling must be set to `Input System Package (New)` or `Both` in Player Settings. Game code must read input via Input System API (e.g. `Keyboard.current[Key.W].isPressed`), not legacy `Input.GetKey()`.

Supports 3 actions: Press (one-shot tap or timed hold), KeyDown (hold key down), KeyUp (release held key).

```text
→ simulate-keyboard (Action: Press, Key: Space)
→ simulate-keyboard (Action: Press, Key: W, Duration: 2.0)
→ simulate-keyboard (Action: KeyDown, Key: LeftShift)
→ simulate-keyboard (Action: KeyDown, Key: W)
→ screenshot (CaptureMode: rendering)
→ simulate-keyboard (Action: KeyUp, Key: W)
→ simulate-keyboard (Action: KeyUp, Key: LeftShift)
```

### 14. record-input - Record Input During PlayMode
Record keyboard and mouse input during PlayMode frame-by-frame into a JSON file. Captures key presses, mouse movement, clicks, and scroll events via Input System device state diffing. This tool is available only when the Input System package is installed.

```text
→ record-input (Action: Start)
→ record-input (Action: Start, Keys: "W,A,S,D,Space")
→ record-input (Action: Stop)
→ JSON file saved to .uloop/outputs/InputRecordings/
```

### 15. replay-input - Replay Recorded Input During PlayMode
Replay recorded keyboard and mouse input during PlayMode. Loads a JSON recording and injects input frame-by-frame via Input System. Supports looping and progress monitoring. This tool is available only when the Input System package is installed.

```text
→ replay-input (Action: Start)
→ replay-input (Action: Start, InputPath: "scripts/my-play.json", Loop: true)
→ replay-input (Action: Status)
→ replay-input (Action: Stop)
```

## Tool Reference

For detailed specifications of all tools (parameters, responses, examples), see **[TOOL_REFERENCE.md](/Packages/src/TOOL_REFERENCE.md)**.

## Unity CLI Loop Extension Development
Unity CLI Loop enables efficient development of project-specific tools without requiring changes to the core package.
The type-safe design allows for reliable custom tool implementation in minimal time.
(If you ask AI, they should be able to make it for you soon ✨)

You can publish your extension tools on GitHub and reuse them across other projects.

> [!TIP]
> **For AI-assisted development**: Detailed implementation guides are available in [.claude/rules/cli.md](/.claude/rules/cli.md) for CLI and Skills development. These guides are automatically loaded by Claude Code when working in the relevant directories.

> [!IMPORTANT]
> **Security Settings**
>
> Project-specific tools are available without enabling an additional permission.
> When developing custom tools that involve dynamic code execution, consider the **Dynamic Code Security Level** setting.

<details>
<summary>View Implementation Guide</summary>

**Step 1: Create Schema Class** (define parameters):
```csharp
using System.ComponentModel;

public class MyCustomSchema : BaseToolSchema
{
    [Description("Parameter description")]
    public string MyParameter { get; set; } = "default_value";

    [Description("Example enum parameter")]
    public MyEnum EnumParameter { get; set; } = MyEnum.Option1;
}

public enum MyEnum
{
    Option1 = 0,
    Option2 = 1,
    Option3 = 2
}
```

**Step 2: Create Response Class** (define return data):
```csharp
public class MyCustomResponse : BaseToolResponse
{
    public string Result { get; set; }
    public bool Success { get; set; }

    public MyCustomResponse(string result, bool success)
    {
        Result = result;
        Success = success;
    }

    // Required parameterless constructor
    public MyCustomResponse() { }
}
```

**Step 3: Create Tool Class**:
```csharp
using System.Threading;
using System.Threading.Tasks;

[McpTool(Description = "Description of my custom tool")]  // Auto-registered; attribute name is historical
public class MyCustomTool : AbstractUnityTool<MyCustomSchema, MyCustomResponse>
{
    public override string ToolName => "my-custom-tool";

    // Executed on main thread
    protected override Task<MyCustomResponse> ExecuteAsync(MyCustomSchema parameters, CancellationToken cancellationToken)
    {
        // Type-safe parameter access
        string param = parameters.MyParameter;
        MyEnum enumValue = parameters.EnumParameter;

        // Check for cancellation before long-running operations
        cancellationToken.ThrowIfCancellationRequested();

        // Implement custom logic here
        string result = ProcessCustomLogic(param, enumValue);
        bool success = !string.IsNullOrEmpty(result);

        // For long-running operations, periodically check for cancellation
        // cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new MyCustomResponse(result, success));
    }

    private string ProcessCustomLogic(string input, MyEnum enumValue)
    {
        // Implement custom logic
        return $"Processed '{input}' with enum '{enumValue}'";
    }
}
```

> [!IMPORTANT]
> **Important Notes**:
> - **Thread Safety**: Tools execute on Unity's main thread, so Unity API calls are safe without additional synchronization.

Please also refer to [Custom Tool Samples](/Assets/Editor/CustomToolSamples).

</details>

### Custom Skills for Your Tools

When you create a custom tool, you can create a `Skill/` subfolder within the tool folder and place a `SKILL.md` file there. This allows LLM tools to automatically discover and use your custom tool through the Skills system.

**How it works:**
1. Create a `Skill/` subfolder in your custom tool's folder
2. Place `SKILL.md` inside the `Skill/` folder
3. Run `uloop skills install --claude` to install all skills (bundled + project)
4. LLM tools will automatically recognize your custom skill

**Directory structure:**
```
Assets/Editor/CustomTools/MyTool/
├── MyTool.cs           # Tool implementation
└── Skill/
    ├── SKILL.md        # Skill definition (required)
    └── references/     # Additional files (optional)
        └── usage.md
```

**SKILL.md format:**
```markdown
---
name: uloop-my-custom-tool
description: "Description of what the tool does and when to use it."
---

# uloop my-custom-tool

Detailed documentation for the tool...
```

**Scanned locations** (searches for `Skill/SKILL.md` files):
- `Assets/**/Editor/<ToolFolder>/Skill/SKILL.md`
- `Packages/*/Editor/<ToolFolder>/Skill/SKILL.md`
- `Library/PackageCache/*/Editor/<ToolFolder>/Skill/SKILL.md`

> [!TIP]
> - Add `internal: true` to the frontmatter to exclude a skill from installation (useful for internal/debug tools)
> - Additional files in the `Skill/` folder (such as `references/`, `scripts/`, `assets/`) are also copied during installation

See [HelloWorld sample](/Assets/Editor/CustomCommandSamples/HelloWorld/Skill/SKILL.md) for a complete example.

## Other

### Native Go CLI Development

Run the native Go CLI checks before changing files under `Packages/src/GoCli~`:

```bash
scripts/check-go-cli.sh
```

The check script verifies formatting with `goimports` and `gofumpt`, runs `go vet ./...`, runs `golangci-lint`, and then runs `go test ./...`. Install `golangci-lint` first if it is not available on your `PATH`.

Use the existing build script when you need to refresh the checked-in native binaries:

```bash
scripts/build-go-cli.sh
```

### Unity CLI Loop Files

`UserSettings/UnityMcpSettings.json` stores per-user editor session state and should always remain local-only. The file name is a historical compatibility name.

The `.uloop/` directory at the project root stores CLI cache, tool registry, and runtime outputs. Most of its contents are local-only, but some files can optionally be git-tracked for team sharing.

| File | Purpose | Git-track? |
|------|---------|------------|
| `settings.permissions.json` | Team-wide security policy (dynamic code security level) | Optional |
| `settings.tools.json` | Per-tool enable/disable preferences | Optional |
| `tools.json` | Auto-generated CLI tool registry | No |
| `outputs/` | Runtime outputs (test results, screenshots, hierarchy dumps) | No |

> [!TIP]
> **Recommended `.gitignore` pattern**
>
> ```gitignore
> **/.uloop/*
> !**/.uloop/settings.permissions.json
> !**/.uloop/settings.tools.json
> ```
>
> This ignores auto-generated files and runtime outputs while allowing team-shared configuration to be tracked.
> Adjust the `!` lines to match your team's needs — you can remove either line if you don't need to share that file.

## License
MIT License
