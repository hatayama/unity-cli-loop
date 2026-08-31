# Unity CLI Loop

English | [日本語](README_ja.md)

[![Unity](https://img.shields.io/badge/Unity-2022.3+-red.svg)](https://unity3d.com/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE.md)<br>
![ClaudeCode](https://img.shields.io/badge/Claude_Code-555?logo=claude)
![Cursor](https://img.shields.io/badge/Cursor-111?logo=Cursor)
![Codex](https://img.shields.io/badge/Codex-111?logo=data:image/svg+xml;base64,PHN2ZyByb2xlPSJpbWciIHZpZXdCb3g9IjAgMCAyNCAyNCIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj48cGF0aCBmaWxsPSJ3aGl0ZSIgZD0iTTIyLjI4MTkgOS44MjExYTUuOTg0NyA1Ljk4NDcgMCAwIDAtLjUxNTctNC45MTA4IDYuMDQ2MiA2LjA0NjIgMCAwIDAtNi41MDk4LTIuOUE2LjA2NTEgNi4wNjUxIDAgMCAwIDQuOTgwNyA0LjE4MThhNS45ODQ3IDUuOTg0NyAwIDAgMC0zLjk5NzcgMi45IDYuMDQ2MiA2LjA0NjIgMCAwIDAgLjc0MjcgNy4wOTY2IDUuOTggNS45OCAwIDAgMCAuNTExIDQuOTEwNyA2LjA1MSA2LjA1MSAwIDAgMCA2LjUxNDYgMi45MDAxQTUuOTg0NyA1Ljk4NDcgMCAwIDAgMTMuMjU5OSAyNGE2LjA1NTcgNi4wNTU3IDAgMCAwIDUuNzcxOC00LjIwNTggNS45ODk0IDUuOTg5NCAwIDAgMCAzLjk5NzctMi45MDAxIDYuMDU1NyA2LjA1NTcgMCAwIDAtLjc0NzUtNy4wNzI5em0tOS4wMjIgMTIuNjA4MWE0LjQ3NTUgNC40NzU1IDAgMCAxLTIuODc2NC0xLjA0MDhsLjE0MTktLjA4MDQgNC43NzgzLTIuNzU4MmEuNzk0OC43OTQ4IDAgMCAwIC4zOTI3LS42ODEzdi02LjczNjlsMi4wMiAxLjE2ODZhLjA3MS4wNzEgMCAwIDEgLjAzOC4wNTJ2NS41ODI2YTQuNTA0IDQuNTA0IDAgMCAxLTQuNDk0NSA0LjQ5NDR6bS05LjY2MDctNC4xMjU0YTQuNDcwOCA0LjQ3MDggMCAwIDEtLjUzNDYtMy4wMTM3bC4xNDIuMDg1MiA0Ljc4MyAyLjc1ODJhLjc3MTIuNzcxMiAwIDAgMCAuNzgwNiAwbDUuODQyOC0zLjM2ODV2Mi4zMzI0YS4wODA0LjA4MDQgMCAwIDEtLjAzMzIuMDYxNUw5Ljc0IDE5Ljk1MDJhNC40OTkyIDQuNDk5MiAwIDAgMS02LjE0MDgtMS42NDY0ek0yLjM0MDggNy44OTU2YTQuNDg1IDQuNDg1IDAgMCAxIDIuMzY1NS0xLjk3MjhWMTEuNmEuNzY2NC43NjY0IDAgMCAwIC4zODc5LjY3NjVsNS44MTQ0IDMuMzU0My0yLjAyMDEgMS4xNjg1YS4wNzU3LjA3NTcgMCAwIDEtLjA3MSAwbC00LjgzMDMtMi43ODY1QTQuNTA0IDQuNTA0IDAgMCAxIDIuMzQwOCA3Ljg3MnptMTYuNTk2MyAzLjg1NThMMTMuMTAzOCA4LjM2NCAxNS4xMTkyIDcuMmEuMDc1Ny4wNzU3IDAgMCAxIC4wNzEgMGw0LjgzMDMgMi43OTEzYTQuNDk0NCA0LjQ5NDQgMCAwIDEtLjY3NjUgOC4xMDQydi01LjY3NzJhLjc5Ljc5IDAgMCAwLS40MDctLjY2N3ptMi4wMTA3LTMuMDIzMWwtLjE0Mi0uMDg1Mi00Ljc3MzUtMi43ODE4YS43NzU5Ljc3NTkgMCAwIDAtLjc4NTQgMEw5LjQwOSA5LjIyOTdWNi44OTc0YS4wNjYyLjA2NjIgMCAwIDEgLjAyODQtLjA2MTVsNC44MzAzLTIuNzg2NmE0LjQ5OTIgNC40OTkyIDAgMCAxIDYuNjgwMiA0LjY2ek04LjMwNjUgMTIuODYzbC0yLjAyLTEuMTYzOGEuMDgwNC4wODA0IDAgMCAxLS4wMzgtLjA1NjdWNi4wNzQyYTQuNDk5MiA0LjQ5OTIgMCAwIDEgNy4zNzU3LTMuNDUzN2wtLjE0Mi4wODA1TDguNzA0IDUuNDU5YS43OTQ4Ljc5NDggMCAwIDAtLjM5MjcuNjgxM3ptMS4wOTc2LTIuMzY1NGwyLjYwMi0xLjQ5OTggMi42MDY5IDEuNDk5OHYyLjk5OTRsLTIuNTk3NCAxLjQ5OTctMi42MDY3LTEuNDk5N1oiLz48L3N2Zz4=)
![Antigravity](https://img.shields.io/badge/Antigravity-111?logo=google)
![GitHubCopilot](https://img.shields.io/badge/GitHub_Copilot-111?logo=githubcopilot)

<p align="center">
    <img height="450" alt="logo-black-bg" src="Packages/src/Documentation~/images/logo.png" /><br>
    <sub>(Logo inspired by Daft Punk's <i>Human After All</i> album art)</sub>  
</p>
  

Let an AI agent compile, test, and operate your Unity project from popular LLM tools via CLI.

Designed to keep AI-driven development loops running autonomously inside your existing Unity projects.

> [!IMPORTANT]
> - **[What's New in V3](Packages/src/Documentation~/whats-new-v3.md)** — no more Node.js setup or port management, the new `hot-reload` / `pause-point` tools, automatic CLI updates with per-project version selection, and improved connection stability
> - **[Migrating Custom Tools and Skills to V3](Packages/src/Documentation~/migration-v2-to-v3.md)** — for anyone who has written C# custom tools, or skills and scripts that call the `uloop` command. Everyone else migrates just by updating the package and the CLI

# Concept
Unity CLI Loop is a Unity integration tool designed so that **AI can drive your Unity project forward with minimal human intervention**.
Tasks that humans typically handle manually — compiling, running the Test Runner, checking logs, editing scenes, capturing windows to verify UI layouts, and even operating a freshly implemented feature to confirm it actually works — can all be carried out from LLM tools.

Unity CLI Loop is built around four core ideas:

1. **A self-hosted development loop where AI autonomously compiles, tests, inspects logs, and fixes issues** — it can even pause execution at any source line without editing code and read the variables at that moment to pin down a cause. Method-body fixes can be applied instantly to the running game without waiting for a recompile. Uses `compile`, `run-tests`, `get-logs`, `clear-console`, `pause-point`, `hot-reload`.
2. **AI-driven Unity Editor operation — scene building, object manipulation, menu execution, and UI refinement from screenshots.** Uses `execute-dynamic-code`, `screenshot`.
3. **PlayMode automated testing — AI clicks buttons, drags elements, presses keys, replays recorded input, and verifies game behavior.** Uses `simulate-mouse-ui`, `simulate-mouse-input`, `simulate-keyboard`, `replay-input`, `execute-dynamic-code`, `screenshot`.
4. **Achieving the above with a minimal set of tools.** See [Design Philosophy](#design-philosophy).

https://github.com/user-attachments/assets/569a2110-7351-4cf3-8281-3a83fe181817

# Quickstart

This guide installs three things — the CLI, the Unity package, and the skills — so that your LLM tool can drive Unity. You can install everything from the terminal or from the Unity UI; either path is complete on its own.

## Before you begin

Make sure you have:

- A Unity project on Unity 2022.3 or later
- An LLM tool that can load skills, such as Claude Code or Codex

> [!NOTE]
> **Upgrading from V2**: if the project has custom tools written against the V2 API, compile errors appear right after the V3 package is installed. This is expected. Do not fix them by hand: choose `Ignore` in Unity's Safe Mode prompt on startup, then press **Migrate** in the migration window that opens automatically (`Window > Unity CLI Loop > Custom Tool Migration`). See [Migrating Custom Tools and Skills to V3](Packages/src/Documentation~/migration-v2-to-v3.md) for the full procedure.

## Install from the terminal

### Step 1: Install the CLI

**macOS, Windows Git Bash:**

```sh
curl -fsSL https://raw.githubusercontent.com/hatayama/unity-cli-loop/main/scripts/install.sh | sh
```

**Windows PowerShell:**

```powershell
irm https://raw.githubusercontent.com/hatayama/unity-cli-loop/main/scripts/install.ps1 | iex
```

**Homebrew (macOS):**

```bash
brew install hatayama/tap/uloop
```

### Step 2: Install the Unity package

Run this in the root of your Unity project:

```bash
uloop package install
```

This adds the OpenUPM scoped registry and the `io.github.hatayama.uloopmcp` dependency to `Packages/manifest.json`. Add `--version <x.y.z>` to pin a specific version.

### Step 3: Install the skills

Run the command for your LLM tool in the root of your Unity project:

```bash
# Claude Code
uloop skills install --claude

# Codex and other tools that read .agents/skills
uloop skills install --agents

# Install globally instead of into the project
uloop skills install --claude --global

# Sync into any directory (e.g. an external skill-package store)
uloop skills install --output-dir path/to/skills
```

### Step 4: Verify the setup

With the project open in Unity, run this in the project root:

```bash
uloop -v
```

You are done when the CLI version is followed by the project runner version this project uses:

```text
3.0.1
This Unity project pins uloop project runner 3.0.0.
```

## Install from the Unity UI

### Step 1: Install the Unity package

In `Window > Package Manager`, press "+", choose **Add package from git URL**, and enter:

```text
https://github.com/hatayama/unity-cli-loop.git?path=/Packages/src
```

<details>
<summary>Installing from the OpenUPM scoped registry</summary>

1. In `Project Settings > Package Manager`, add this entry to Scoped Registries:
```text
Name: OpenUPM
URL: https://package.openupm.com
Scope(s): io.github.hatayama.uloopmcp
```
2. In `Window > Package Manager`, select OpenUPM under My Registries and install Unity CLI Loop.

</details>

### Step 2: Install the CLI

Open `Window > Unity CLI Loop > Settings` and press **Install CLI**:

<img width="350" alt="Settings window before the CLI is installed, showing the Install CLI button" src="Packages/src/Documentation~/images/settings-cli-not-installed.png" />

You are done when the button disappears and the CLI version is shown:

<img width="350" alt="Settings window after CLI detection succeeds, showing a green indicator and the CLI version" src="Packages/src/Documentation~/images/settings-cli-installed.png" />

### Step 3: Install the skills

In the same Settings window, select your target (Claude Code, Codex, etc.) and press **Install Skills**:

<img width="350" alt="The Skills section of the Settings window, with a target selected and the Install Skills button ready" src="Packages/src/Documentation~/images/settings-skills-install.png" />

<details>
<summary>Working alongside V2 projects</summary>

Projects on the V2 package and V3 projects can coexist on the same machine. Keep the V3 CLI installed: in a V2 project, the `uloop` command automatically uses the V2 CLI (downloaded on first use; Node.js 22 or later is required).

Do not press **Update CLI** or **Downgrade CLI** in a V2 project's Settings window, and do not reinstall the V2 CLI with `npm install -g uloop-cli`. The old npm `uloop` would hide the V3 CLI. If that has already happened, run `npm uninstall -g uloop-cli`, then run the CLI installer from Step 1 of "Install from the terminal" again or press **Install CLI** in the Settings window.

</details>

That's it! After installing Skills, LLM tools can automatically handle instructions like these:

| Your Instruction | Skill Used by LLM Tools |
|---|---|
| "Launch Unity for this project" | `/uloop-launch` |
| "Fix the compile errors" | `/uloop-compile` |
| "Apply this fix right now without compiling" | `/uloop-hot-reload` |
| "Run the tests and tell me why they failed" | `/uloop-run-tests` + `/uloop-get-logs` |
| "Check the scene hierarchy" | `/uloop-get-hierarchy` |
| "Play the game and bring Unity to the front" | `/uloop-control-play-mode` + `/uloop-focus-window` |
| "Bulk-update prefab parameters" | `/uloop-execute-dynamic-code` |
| "Take a screenshot of Game View and adjust the UI layout" | `/uloop-screenshot` + `/uloop-execute-dynamic-code` |
| "Replay the recorded input" | `/uloop-replay-input` |
| "Pause at this line and investigate the bug" | `/uloop-pause-point` |


<details>
<summary>All 18 Bundled Skills</summary>

- `/uloop-launch` - Launch Unity with correct version
- `/uloop-compile` - Execute compilation
- `/uloop-get-logs` - Get console logs
- `/uloop-run-tests` - Run tests
- `/uloop-hot-reload` - Apply method-body changes to running code instantly, without recompiling
- `/uloop-clear-console` - Clear console
- `/uloop-focus-window` - Bring Unity Editor to front
- `/uloop-get-hierarchy` - Get scene hierarchy
- `/uloop-find-game-objects` - Find GameObjects
- `/uloop-screenshot` - Capture EditorWindow
- `/uloop-pause-point` - Pause execution at any line and capture variables
- `/uloop-set-game-view-size` - Read and set the Game View custom resolution
- `/uloop-simulate-mouse-ui` - Simulate mouse click, long-press, and drag on PlayMode UI elements
- `/uloop-simulate-mouse-input` - Simulate mouse input in PlayMode via Input System
- `/uloop-simulate-keyboard` - Simulate keyboard input in PlayMode via Input System
- `/uloop-replay-input` - Replay input recorded in the Recordings window during PlayMode
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

# Compile without waiting for Domain Reload
uloop compile --no-wait-for-domain-reload

# Apply the method bodies of changed .cs files to running code without recompiling
uloop hot-reload

# Get logs
uloop get-logs --max-count 10

# Run tests
uloop run-tests --filter-type all

# Execute dynamic code
uloop execute-dynamic-code --code 'using UnityEngine; Debug.Log("Hello from CLI!");'
```

</details>

## Configuration for Claude Code

Claude Code runs shell commands inside a sandbox, and the sandbox blocks network access by default. The IPC connection to Unity is also affected, so `uloop` fails with `UNITY_NOT_REACHABLE` (`connect: operation not permitted`) at the moment it tries to connect — even when Unity itself is running fine.

Add `uloop *` to `sandbox.excludedCommands` in `~/.claude/settings.json` to exclude `uloop` commands from the sandbox:

```json
{
  "sandbox": {
    "excludedCommands": ["uloop *"]
  }
}
```

The pattern is matched against **the command string as typed**, so invocations starting with `uloop` are excluded. See [docs/claude-code-sandbox.md](docs/claude-code-sandbox.md) for details and measured results.

# How It Works

A `uloop` command reaches the Unity Editor through the following chain:

- **The global `uloop` dispatcher** — the single entry point on PATH. It interprets the command and delegates it to the runner for the target project
- **`uloop-project-runner`** — the per-project runner. The version to use is determined by each project's `.uloop/project-runner-pin.json` (the pin) and downloaded automatically into a per-version user cache, which is how multiple projects on different versions coexist on one machine → [project runner pin details](docs/project-runner-pin.md)
- **The IPC server inside the Unity Editor** — accepts connections from the runner, executes Unity APIs, and returns the results

The connection uses **no TCP ports**. It goes over a Unix domain socket on macOS/Linux and a named pipe on Windows, so there is no port to configure and no port collision with other Editor instances.

# Design Philosophy

Unity CLI Loop does not chase tool count. With dynamic C# code execution (`execute-dynamic-code`), almost any Unity Editor operation can be accomplished through a single tool.

Too many tools make it harder for AI to choose the right one. And even when tools are packaged as Skills, each tool's description still consumes the context window. We believe keeping tools to the essential minimum is good design.

Dedicated tools exist only for operations that dynamic code execution cannot handle by nature — such as frame-spanning input simulation and screenshot capture — and for operations called so frequently in the development loop, like `compile` and `get-logs`, that generating C# code each time would waste tokens.

When you find yourself wanting a new dedicated tool, a Skill usually suffices. Write the routine operation down as SKILL.md instructions, a shell script, or a C# snippet passed to `execute-dynamic-code`, and the AI just invokes it. The skill body is loaded only at startup and nothing has to be regenerated per run, so the additional token cost is close to zero. See the [Custom Tool Development Guide](Packages/src/Documentation~/custom-tools.md#custom-skills-for-your-tools) for how to build one.

# Key Features

For detailed descriptions and usage examples of every tool, see the **[Tool Reference](Packages/src/Documentation~/tools.md)**.

## Development Loop Tools
- `compile` - Run compilation and return errors and warnings
- `get-logs` - Retrieve the same logs as the Console, filtered by type or search string
- `run-tests` - Run Unity Test Runner (PlayMode / EditMode)
- `hot-reload` - Apply method-body changes to running code instantly, without recompiling
- `pause-point` - Pause PlayMode at any line without editing code and read the variables at that moment

## Unity Editor Automation & Discovery Tools
- `clear-console` - Clear the Console logs
- `find-game-objects` - Search scene objects and inspect components
- `get-hierarchy` - Retrieve the scene structure as JSON
- `focus-window` - Bring the Unity Editor window to the front
- `screenshot` - Save screenshots of EditorWindows or the Game View
- `set-game-view-size` - Read and set the Game View custom resolution
- `control-play-mode` - Play, stop, and pause Play Mode
- `execute-dynamic-code` - Execute dynamic C# code

## PlayMode Automated Testing Tools
- `simulate-mouse-ui` - Simulate mouse operations on UI elements (via EventSystem)
- `simulate-mouse-input` - Simulate mouse input via Input System
- `simulate-keyboard` - Simulate keyboard input via Input System
- `replay-input` - Replay input recorded in the Recordings window during PlayMode

## Unity CLI Loop Extension Development

Add project-specific custom tools in a type-safe way without touching the core package. Ship a `Skill/SKILL.md` alongside your tool and AI agents discover it automatically.

For implementation steps and how to write Skills, see the **[Custom Tool Development Guide](Packages/src/Documentation~/custom-tools.md)**.

## Other

### Unity CLI Loop Files

`UserSettings/UnityCliLoopSettings.json` stores per-user editor settings and should always remain local-only.

The `.uloop/` directory at the project root stores CLI cache, tool registry, and runtime outputs. Most of its contents are local-only, but some files can optionally be git-tracked for team sharing.

| File | Purpose | Git-track? |
|------|---------|------------|
| `project-runner-pin.json` | Project runner version contract used by the global dispatcher | Yes |
| `settings.tools.json` | Per-tool enable/disable preferences | Optional |
| `tools.json` | Auto-generated CLI tool registry | No |
| `outputs/` | Runtime outputs (test results, screenshots, hierarchy dumps) | No |

> [!TIP]
> **Recommended `.gitignore` pattern**
>
> ```gitignore
> **/.uloop/*
> !**/.uloop/project-runner-pin.json
> !**/.uloop/settings.tools.json
> ```
>
> This ignores auto-generated files and runtime outputs while allowing the dispatcher pin and team-shared configuration to be tracked.
> Remove the `!` line if you don't need to share tool enable/disable preferences.

## License
MIT License
