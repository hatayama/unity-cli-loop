[日本語](README_ja.md)

# uloop-cli

[![npm version](https://img.shields.io/npm/v/uloop-cli.svg)](https://www.npmjs.com/package/uloop-cli)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Node.js](https://img.shields.io/badge/Node.js-22+-green.svg)](https://nodejs.org/)

**CLI companion for [Unity CLI Loop](https://github.com/hatayama/unity-cli-loop)** - Let AI agents compile, test, and operate your Unity project.

> **Prerequisites**: This CLI requires [Unity CLI Loop](https://github.com/hatayama/unity-cli-loop) to be installed in your Unity project. The Unity package automatically creates or refreshes the project-local CLI bundle when it is missing or version-mismatched.

## Installation

```bash
npm install -g uloop-cli
```

## Quick Start

### Step 1: Install Skills

Skills allow LLM tools (Claude Code, Cursor, etc.) to automatically invoke Unity operations.

```bash
# Install for Claude Code (project-level)
uloop skills install --claude

# Install for OpenAI Codex (project-level)
uloop skills install --codex

# Or install globally
uloop skills install --claude --global
uloop skills install --codex --global
```

### Step 2: Use with LLM Tools

After installing Skills, LLM tools can automatically handle instructions like:

| Your Instruction | Skill Used |
|---|---|
| "Fix the compile errors" | `/uloop-compile` |
| "Run the tests and tell me why they failed" | `/uloop-run-tests` |
| "Check the scene hierarchy" | `/uloop-get-hierarchy` |

> **No external protocol configuration required.** LLM tools communicate with Unity through the `uloop` CLI and installed Skills.

## Available Skills

Skills are dynamically loaded from the Unity CLI Loop package in your Unity project. These are the default skills provided by Unity CLI Loop:

- `/uloop-compile` - Execute compilation
- `/uloop-get-logs` - Get console logs
- `/uloop-run-tests` - Run tests
- `/uloop-clear-console` - Clear console
- `/uloop-focus-window` - Bring Unity Editor to front
- `/uloop-get-hierarchy` - Get scene hierarchy
- `/uloop-find-game-objects` - Find GameObjects
- `/uloop-screenshot` - Take a screenshot of EditorWindow
- `/uloop-control-play-mode` - Control Play Mode
- `/uloop-execute-dynamic-code` - Execute dynamic C# code
- `/uloop-launch` - Launch Unity project with matching Editor version

Custom skills defined in your project are also automatically detected.

## CLI Command Reference  
([日本語](README_ja.md#cli-コマンドリファレンス))

You can also call the CLI directly without using Skills.

### Project Path Specification

If `--project-path` is omitted, the port is automatically selected from the Unity project detected in the current directory.

To operate multiple Unity instances from a single LLM tool, explicitly specify a project path:

```bash
# Specify by project path (absolute or relative)
uloop compile --project-path /Users/foo/my-unity-project
uloop compile --project-path ../other-project
```

### Chaining Commands

Chain commands with `&&` to automate a sequence of operations:

```bash
# Compile → wait for Domain Reload → play → focus Unity
uloop compile --wait-for-domain-reload true && uloop control-play-mode --action Play && uloop focus-window
```

### Utility Commands

```bash
uloop list           # List available tools from Unity
uloop sync           # Sync tool definitions from Unity to local cache
uloop completion     # Setup shell completion
```

### compile

Execute Unity project compilation.

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--force-recompile` | boolean | `false` | Force full recompilation |
| `--wait-for-domain-reload` | boolean | `false` | Wait for Domain Reload completion |

```bash
uloop compile
uloop compile --force-recompile true --wait-for-domain-reload true
```

**Output:**
- `Success` (boolean | null) - Compilation succeeded (`null` when `--force-recompile` is used)
- `ErrorCount` / `WarningCount` (integer | null) - Number of errors/warnings (`null` when `--force-recompile` is used)
- `Errors` / `Warnings` (array | null) - Issues with `Message`, `File`, `Line` fields (`null` when `--force-recompile` is used)

### get-logs

Retrieve logs from Unity Console.

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--log-type` | enum | `All` | Log type filter: `Error`, `Warning`, `Log`, `All` |
| `--max-count` | integer | `100` | Maximum number of logs to retrieve |
| `--search-text` | string | | Text to search within logs |
| `--include-stack-trace` | boolean | `false` | Include stack trace in output |
| `--use-regex` | boolean | `false` | Use regex for search |
| `--search-in-stack-trace` | boolean | `false` | Search within stack trace |

```bash
uloop get-logs
uloop get-logs --log-type Error --max-count 10
uloop get-logs --search-text "NullReference" --include-stack-trace true
```

**Output:**
- `TotalCount` (integer) - Total available logs matching filter
- `DisplayedCount` (integer) - Number of logs returned
- `Logs` (array) - Log entries with `Type` (Error/Warning/Log), `Message`, `StackTrace` fields

### run-tests

Execute Unity Test Runner.

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--test-mode` | enum | `EditMode` | Test mode: `EditMode`, `PlayMode` |
| `--filter-type` | enum | `all` | Filter type: `all`, `exact`, `regex`, `assembly` |
| `--filter-value` | string | | Filter value (used when filter-type is not `all`) |
| `--save-before-run` | boolean | `false` | Save unsaved loaded Scene changes and current Prefab Stage changes before running tests |

```bash
uloop run-tests
uloop run-tests --test-mode EditMode --filter-type regex --filter-value "MyTests"
uloop run-tests --filter-type assembly --filter-value "MyApp.Tests.Editor"
uloop run-tests --save-before-run true
```

**Output:**
- `Success` (boolean) - All tests passed
- `TestCount` / `PassedCount` / `FailedCount` / `SkippedCount` (integer) - Test result counts
- `XmlPath` (string) - Path to NUnit XML result file (saved on failure)

### clear-console

Clear Unity console logs.

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--add-confirmation-message` | boolean | `true` | Add confirmation message after clearing |

```bash
uloop clear-console
```

**Output:**
- `Success` (boolean) - Operation succeeded
- `ClearedLogCount` (integer) - Number of logs cleared

### focus-window

Bring Unity Editor window to front.

No parameters.

```bash
uloop focus-window
```

**Output:**
- `Success` (boolean) - Operation succeeded
- `Message` (string) - Result message

### get-hierarchy

Get Unity Hierarchy structure.

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--root-path` | string | | Root GameObject path to start from |
| `--max-depth` | integer | `-1` | Maximum depth (-1 for unlimited) |
| `--include-components` | boolean | `true` | Include component information |
| `--include-inactive` | boolean | `true` | Include inactive GameObjects |
| `--include-paths` | boolean | `false` | Include path information |
| `--use-components-lut` | string | `auto` | Use LUT for components: `auto`, `true`, `false` |
| `--use-selection` | boolean | `false` | Use selected GameObject(s) as root(s) |

```bash
uloop get-hierarchy
uloop get-hierarchy --root-path "Canvas" --max-depth 3
uloop get-hierarchy --use-selection true
```

**Output:**
- `hierarchyFilePath` (string) - Path to saved JSON file containing hierarchy data

### find-game-objects

Find GameObjects with search criteria.

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--name-pattern` | string | | Name pattern to search |
| `--search-mode` | enum | `Exact` | Search mode: `Exact`, `Path`, `Regex`, `Contains`, `Selected` |
| `--required-components` | array | | Required component type names |
| `--tag` | string | | Tag filter |
| `--layer` | integer | | Layer filter |
| `--max-results` | integer | `20` | Maximum number of results |
| `--include-inactive` | boolean | `false` | Include inactive GameObjects |
| `--include-inherited-properties` | boolean | `false` | Include inherited properties |

```bash
uloop find-game-objects --name-pattern "Player"
uloop find-game-objects --required-components "Camera" --include-inactive true
uloop find-game-objects --tag "Enemy" --max-results 50
```

**Output:**
- `totalFound` (integer) - Total GameObjects found
- `results` (array) - GameObjects with `name`, `path`, `isActive`, `tag`, `layer`, `components` fields

### screenshot

Take a screenshot of Unity EditorWindow and save as PNG.

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--window-name` | string | `Game` | Window name (e.g., "Game", "Scene", "Console", "Inspector", "Project", "Hierarchy") |
| `--resolution-scale` | number | `1` | Resolution scale (0.1 to 1.0) |
| `--match-mode` | enum | `exact` | Matching mode: `exact`, `prefix`, `contains` (case-insensitive) |
| `--output-directory` | string | | Output directory path (uses .uloop/outputs/Screenshots/ when empty) |

```bash
uloop screenshot
uloop screenshot --window-name Scene
uloop screenshot --window-name Project --match-mode prefix
```

**Output:**
- `ScreenshotCount` (integer) - Number of windows captured
- `Screenshots` (array) - Items with `ImagePath`, `Width`, `Height`, `FileSizeBytes` fields

### execute-dynamic-code

Execute C# code in Unity Editor.

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--code` | string | | C# code to execute |
| `--parameters` | object | | Runtime parameters for execution |
| `--compile-only` | boolean | `false` | Compile only without execution |

```bash
uloop execute-dynamic-code --code 'using UnityEngine; Debug.Log("Hello!");'
uloop execute-dynamic-code --code 'Selection.activeGameObject.name' --compile-only true
```

**Output:**
- `Success` (boolean) - Execution succeeded
- `Result` (string) - Return value of executed code
- `Logs` (array) - Log messages emitted during execution
- `CompilationErrors` (array) - Errors with `Message`, `Line`, `Column`, `Hint` fields

### control-play-mode

Control Unity Editor play mode.

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--action` | enum | `Play` | Action: `Play`, `Stop`, `Pause` |

```bash
uloop control-play-mode --action Play
uloop control-play-mode --action Stop
uloop control-play-mode --action Pause
```

**Output:**
- `IsPlaying` (boolean) - Currently in play mode
- `IsPaused` (boolean) - Currently paused
- `Message` (string) - Action description

### launch

Open a Unity project with the matching Editor version.

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `[project-path]` | string | | Path to Unity project (searches current directory if omitted) |
| `-r, --restart` | flag | | Kill running Unity and restart |
| `-q, --quit` | flag | | Gracefully quit running Unity |
| `-d, --delete-recovery` | flag | | Delete Assets/_Recovery before launch |
| `-p, --platform <platform>` | string | | Build target (e.g., Android, iOS, StandaloneOSX) |
| `--max-depth <n>` | number | `3` | Search depth when project-path is omitted (-1 for unlimited) |
| `-a, --add-unity-hub` | flag | | Add to Unity Hub only (does not launch) |
| `-f, --favorite` | flag | | Add to Unity Hub as favorite (does not launch) |

```bash
uloop launch
uloop launch /path/to/project
uloop launch -r
uloop launch -p Android
```

## Shell Completion

Install Bash/Zsh/PowerShell completion for tab-completion support:

```bash
# Auto-detect shell and install
uloop completion --install

# Explicitly specify shell
uloop completion --shell bash --install        # Git Bash / MINGW64
uloop completion --shell powershell --install  # PowerShell
```

## Requirements

- **Node.js 22.0 or later**
- **Unity 2022.3 or later** with [Unity CLI Loop](https://github.com/hatayama/unity-cli-loop) installed
- Project-local CLI automatically created or refreshed by the Unity package

## Links

- [Unity CLI Loop Repository](https://github.com/hatayama/unity-cli-loop) - Main package and documentation
- [Tool Reference](https://github.com/hatayama/unity-cli-loop/blob/main/Packages/src/TOOL_REFERENCE.md) - Detailed API specifications and response schemas

## License

MIT License
