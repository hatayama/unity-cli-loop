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
> - **[What's New in V3](Packages/src/Documentation~/whats-new-v3.md)** — what changed since V2: the move to a native Go CLI, the end of port management, the new `pause-point` tool, and more
> - **[Migrating Custom Tools and Skills to V3](Packages/src/Documentation~/migration-v2-to-v3.md)** — for anyone with C# custom tools or hand-written skills/scripts that invoke `uloop`. Everyone else migrates just by updating the package and the CLI

# Concept
Unity CLI Loop is a Unity integration tool designed so that **AI can drive your Unity project forward with minimal human intervention**.
Tasks that humans typically handle manually — compiling, running the Test Runner, checking logs, editing scenes, capturing windows to verify UI layouts, and even operating a freshly implemented feature to confirm it actually works — can all be carried out from LLM tools.

Unity CLI Loop is built around four core ideas:

1. **A self-hosted development loop where AI autonomously compiles, tests, inspects logs, and fixes issues** — it can even pause execution at any source line without editing code and read the variables at that moment to pin down a cause. Uses `compile`, `run-tests`, `get-logs`, `clear-console`, `pause-point`.
2. **AI-driven Unity Editor operation — scene building, object manipulation, menu execution, and UI refinement from screenshots.** Uses `execute-dynamic-code`, `screenshot`.
3. **PlayMode automated testing — AI clicks buttons, drags elements, presses keys, records and replays input, and verifies game behavior.** Uses `simulate-mouse-ui`, `simulate-mouse-input`, `simulate-keyboard`, `record-input`, `replay-input`, `execute-dynamic-code`, `screenshot`.
4. **Achieving the above with a minimal set of tools.** See [Design Philosophy](#design-philosophy).

https://github.com/user-attachments/assets/569a2110-7351-4cf3-8281-3a83fe181817

# Installation

This section installs the Unity package. The CLI itself (a native binary) is installed after the package, in [Quickstart Step 1](#step-1-install-the-cli). A terminal-only way to install the CLI without going through Unity is folded into the same step.

## Via Unity Package Manager

1. Open Unity Editor
2. Open Window > Package Manager
3. Click the "+" button
4. Select "Add package from git URL"
5. Enter the following URL:
```text
https://github.com/hatayama/unity-cli-loop.git?path=/Packages/src
```

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

# Quickstart

## Step 1: Install the CLI

Select Window > Unity CLI Loop > Settings. A dedicated window will open. If the **CLI** button is not highlighted in blue, press **Install CLI**.

The installer places the global `uloop` command on PATH. Project-specific `uloop-project-runner` binaries are downloaded into the user cache automatically from each project's `.uloop/project-runner-pin.json`.

<details>
<summary>Working with V2 projects side by side</summary>

Keep the v3 dispatcher installed when working with both v2 and v3 projects. If Unity resolves a project to a v2 `io.github.hatayama.uloopmcp` package, the dispatcher automatically installs the matching v2 `uloop-cli` release into its versioned user cache and delegates the command to it. The resolved package version takes precedence over a stale v3 project-runner pin left after a downgrade. The initial npm installation and the v2-mode notice are written to stderr so stdout remains the delegated command's output. V3 projects continue to use the project runner selected by their pin.

The global `install`, `update`, `uninstall`, and `launch` commands remain owned by the v3 dispatcher in every project. Other project commands, help, and the project-scoped version request are delegated for detected v2 projects.

V2 delegation requires Node.js 22 or later, including npm for the first command that populates the cache. Do not press **Update CLI** or **Downgrade CLI** in a v2 project's Settings window. These buttons are normally hidden because the delegated CLI reports the matching v2 version, but using one can restore a global npm CLI that hides the v3 dispatcher depending on PATH order.

</details>

<details>
<summary>To install only the CLI from a terminal</summary>

Use this only when you want to install the standalone global CLI without opening Unity package setup.

> [!NOTE]
> This command is verbose for a security reason: it verifies with sigstore attestations that the downloaded installer and assets match what this repository's CI actually built, before anything runs. Unity's GUI (the **Install CLI** button) performs the same check against verified digests, but it ships those CI-verified digests inside the package, which is why it does not need `gh` or `jq`.

Install `gh` (logged in) and `jq` through your operating system or package channel first. The commands below neither install these two tools nor fall back to alternatives. The latest dispatcher Release tag is resolved automatically. To install a specific version instead, assign an immutable tag (e.g. `dispatcher-v3.0.0`) to `RELEASE_TAG` directly.

The command performs these five steps in order:

1. Resolve the latest dispatcher Release tag (`RELEASE_TAG`)
2. Download the installer and its signing information (the sigstore attestation bundle) from the Release (`gh release download`)
3. Verify that the installer was built by this repository's CI from the tag's commit (`gh attestation verify`)
4. Extract the verified hash list for the CLI archives from the signing information (`jq`)
5. Run the installer with the hash list. If an archive does not match the list, the installer aborts before executing anything

On macOS or Windows Git Bash, copy and paste the whole block below as-is and run it. There is no need to execute it line by line.

<!-- Do not add # comments to this block. Pasted into plain zsh (interactivecomments disabled), comment lines error out and break the && chaining that stops execution when verification fails. Put explanations in the list above. -->
```bash
REPOSITORY=hatayama/unity-cli-loop
RELEASE_TAG=$(gh api "repos/$REPOSITORY/releases?per_page=100" --jq '[.[] | select(.tag_name | startswith("dispatcher-v"))][0].tag_name')
SOURCE_REF=refs/heads/main
tmp_dir=$(mktemp -d)
gh release download "$RELEASE_TAG" --repo "$REPOSITORY" --pattern 'install.sh' --pattern 'install.sh.sigstore.json' --dir "$tmp_dir" && \
tag_sha=$(gh api "repos/$REPOSITORY/commits/$RELEASE_TAG" --jq .sha) && \
gh attestation verify "$tmp_dir/install.sh" --bundle "$tmp_dir/install.sh.sigstore.json" --repo "$REPOSITORY" --signer-workflow "$REPOSITORY/.github/workflows/dispatcher-publish.yml" --source-ref "$SOURCE_REF" --source-digest "$tag_sha" && \
manifest=$(jq -r '.dsseEnvelope.payload | @base64d | fromjson | .subject[] | "\(.digest.sha256)  \(.name)"' "$tmp_dir/install.sh.sigstore.json" | LC_ALL=C sort) && \
ULOOP_VERSION="$RELEASE_TAG" ULOOP_ARCHIVE_MANIFEST="$manifest" sh "$tmp_dir/install.sh"
```

On Windows PowerShell, likewise copy and paste the whole block below as-is and run it.

```powershell
$repository = 'hatayama/unity-cli-loop'
# Resolve the latest dispatcher Release tag (assign a tag string directly to pin a version)
$releaseTag = (gh api "repos/$repository/releases?per_page=100" | ConvertFrom-Json | Where-Object { $_.tag_name -like 'dispatcher-v*' } | Select-Object -First 1).tag_name
if (-not $releaseTag) { throw 'No dispatcher release found.' }
$sourceRef = 'refs/heads/main'
$temporaryDirectory = New-Item -ItemType Directory -Force -Path (Join-Path $env:TEMP ([guid]::NewGuid()))
# Download the installer and its signing information (sigstore attestation bundle) from the Release
gh release download $releaseTag --repo $repository --pattern 'install.ps1' --pattern 'install.ps1.sigstore.json' --dir $temporaryDirectory.FullName
if ($LASTEXITCODE -ne 0) { throw 'Installer download failed.' }
$tagSha = gh api "repos/$repository/commits/$releaseTag" --jq .sha
if ($LASTEXITCODE -ne 0) { throw 'Release tag resolution failed.' }
# Verify that the installer was built by this repository's CI from the tag's commit
gh attestation verify (Join-Path $temporaryDirectory.FullName 'install.ps1') --bundle (Join-Path $temporaryDirectory.FullName 'install.ps1.sigstore.json') --repo $repository --signer-workflow "$repository/.github/workflows/dispatcher-publish.yml" --source-ref $sourceRef --source-digest $tagSha
if ($LASTEXITCODE -ne 0) { throw 'Installer attestation verification failed.' }
# Extract the verified hash list for the CLI archives from the signing information
$bundle = Get-Content -Raw -Encoding UTF8 (Join-Path $temporaryDirectory.FullName 'install.ps1.sigstore.json') | ConvertFrom-Json
$statement = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($bundle.dsseEnvelope.payload)) | ConvertFrom-Json
$manifest = [string]::Join("`n", @($statement.subject | ForEach-Object { "$($_.digest.sha256)  $($_.name)" } | Sort-Object))
# Run the installer with the hash list (it aborts before executing if an archive does not match)
$env:ULOOP_VERSION = $releaseTag
$env:ULOOP_ARCHIVE_MANIFEST = $manifest
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $temporaryDirectory.FullName 'install.ps1')
```

After installing the native CLI, the installer automatically tries to remove the old npm package with `npm uninstall -g uloop-cli`.
If npm is unavailable or the old command belongs to a different Node prefix, the installer prints the manual command to run:

```bash
npm uninstall -g uloop-cli
```

Do not install a v2 CLI globally to switch projects. If your terminal resolves `uloop` to an old npm installation instead of the native dispatcher, remove the npm installation and reinstall the native dispatcher:

```bash
npm uninstall -g uloop-cli
# Run the verified native installer above again.
which uloop
uloop --version
```

On Windows PowerShell:

```powershell
npm uninstall -g uloop-cli
# Run the verified native installer above again.
Get-Command uloop
uloop --version
```

</details>


<img width="700" alt="The Settings window with the CLI not yet installed, showing the Install CLI button" src="Packages/src/Documentation~/images/settings-cli-not-installed.png" />

The Settings window shows whether the global `uloop` command is detected.

If you see the following display, the installation was successful.

<img width="700" alt="The Settings window after successful CLI detection, showing a green indicator and the CLI version" src="Packages/src/Documentation~/images/settings-cli-installed.png" />

## Step 2: Install Skills

Select your target (Claude Code, Codex, etc.) and press the **Install Skills** button.

<img width="700" alt="The Skills section of the Settings window, with a target selected and the Install Skills button ready" src="Packages/src/Documentation~/images/settings-skills-install.png" />


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
| "Pause at this line and investigate the bug" | `/uloop-pause-point` |


<details>
<summary>All 18 Bundled Skills</summary>

- `/uloop-launch` - Launch Unity with correct version
- `/uloop-compile` - Execute compilation
- `/uloop-get-logs` - Get console logs
- `/uloop-run-tests` - Run tests
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

# Compile without waiting for Domain Reload
uloop compile --no-wait-for-domain-reload

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

The pattern is matched against **the command string as typed**, so invocations starting with `uloop` are excluded. See [docs/claude-code-sandbox.md](/docs/claude-code-sandbox.md) for details and measured results.

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
- `pause-point` - Pause PlayMode at any line without editing code and read the variables at that moment

## Unity Editor Automation & Discovery Tools
- `clear-console` - Clear the Console logs
- `find-game-objects` - Search scene objects and inspect components
- `get-hierarchy` - Retrieve the scene structure as JSON
- `focus-window` - Bring the Unity Editor window to the front
- `screenshot` - Save screenshots of EditorWindows or the Game View
- `control-play-mode` - Play, stop, and pause Play Mode
- `execute-dynamic-code` - Execute dynamic C# code

## PlayMode Automated Testing Tools
- `simulate-mouse-ui` - Simulate mouse operations on UI elements (via EventSystem)
- `simulate-mouse-input` - Simulate mouse input via Input System
- `simulate-keyboard` - Simulate keyboard input via Input System
- `record-input` / `replay-input` - Record and replay input during PlayMode

## Unity CLI Loop Extension Development

Add project-specific custom tools in a type-safe way without touching the core package. Ship a `Skill/SKILL.md` alongside your tool and AI agents discover it automatically.

For implementation steps and how to write Skills, see the **[Custom Tool Development Guide](Packages/src/Documentation~/custom-tools.md)**.

## Other

### Unity CLI Loop Files

`UserSettings/UnityMcpSettings.json` stores per-user editor session state and should always remain local-only. The file name is a historical compatibility name.

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
