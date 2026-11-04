# What's New in V3

English | [日本語](whats-new-v3_ja.md)

In V3, driving Unity from an AI agent no longer needs a Node.js setup or any port management. Connection stability is also improved.

CLI version management is gone, too. V2 required manually reinstalling a CLI that matched the package on every update; in V3 you install the CLI once and it updates itself from then on. It also selects the right CLI version per project automatically, so working on projects with different package versions in parallel never requires switching CLIs by hand.

The headline new capabilities are `hot-reload` and `pause-point`. `hot-reload` applies C# edits (method bodies, added members, and new types) to the running Editor instantly, without a recompile or a Domain Reload in between. `pause-point` stops PlayMode at any source line and reads the variables at that moment, without editing source or recompiling. It is also handy for taking a screenshot of a specific moment, or inspecting the Hierarchy at a specific moment. For what one screenshot cannot show, such as flicker or a one-frame glitch, `record-video` records the Game View or any Editor window and returns frames an agent can read.

MCP support has been removed; the CLI plus Skills is now the only integration path. The sections below carry the details of each change.

## Upgrading

For most users the upgrade is three steps. Raise the Unity package version, open `Window > Unity Loop Harness > Settings` and press **Install CLI** (or **Update CLI**) to replace the old npm CLI with the native `uloop` command, and finally press **Install Skills** (or **Update Skills**) in the same window to refresh your installed Skills to the V3 content. The installer attempts to remove the obsolete npm package with `npm uninstall -g uloop-cli` and prints the command to run manually when it cannot.

The new `uloop` command is also compatible with V2 projects. When a project still resolves to the V2 package, it fetches the matching V2 CLI automatically and delegates the command to it — so replacing the CLI first does not break the V2 projects you still have around.

You only need the migration guide if you wrote your own integrations: C# custom tools built on the V2 extension API, or your own `SKILL.md` files, Markdown docs, shell scripts, or PowerShell scripts that invoke `uloop`. In that case read [Migrating Custom Tools and Skills to V3](migration-v2-to-v3.md) before you start fixing anything by hand.

## New Tools

### `hot-reload` — apply C# edits instantly without recompiling

`hot-reload` applies edited `.cs` files directly to the running Editor (EditMode / PlayMode) without a recompile or a Domain Reload in between. No attributes or source markers are required, and access to private / internal members, static methods, async methods, and iterators all work. You can fix game logic without leaving PlayMode and see the new behavior on the spot.

Beyond method bodies, it applies added methods, fields, properties, and field-like events, and introduces new top-level classes, structs, enums, and interfaces. One reload can span several files of the same assembly: added members are visible to the other files edited in that reload, so pass the declaring file and its callers together. Using a new type from another assembly or from files outside the reload, reflection, serialization, and Unity message discovery still need `uloop compile`. Edits it cannot apply, such as constructors, operators, and struct methods, are reported as `Skipped` with the reason. A `Failed` method leaves its whole file unapplied, and a `Failed` type leaves every file of its assembly unapplied; files in other assemblies still apply.

```bash
# Apply the method bodies of the given files to the running Editor
uloop hot-reload --files Assets/Scripts/Enemy.cs

# With no arguments, auto-select the .cs files changed since the last compile
uloop hot-reload

# List the currently applied changes, and revert all of them
uloop hot-reload --status
uloop hot-reload --revert-all
```

Treat hot reload as the exploration phase and `uloop compile` as the landing phase. A compile or a Domain Reload reverts every change applied by hot reload (the source edits themselves remain and land normally through the compile). When some edits stay unapplied, `--compile-on-skip` decides whether the same command also runs `uloop compile`: `auto` (the default) does so only in EditMode, so it never stops a PlayMode session, and `off` never does.

### `pause-point` — stop at a line and read the frame

`pause-point` patches an already-compiled method at a source `file:line` and pauses Unity when execution reaches it, so you do not edit source or recompile to investigate a bug, and it can be armed in the middle of a PlayMode session. The hit response carries `CapturedVariables` — the method's locals, its parameters, and the `this` instance fields — captured immediately before the target line runs, exactly like an IDE breakpoint. Because the values are point-in-time strings rather than live references, they remain valid evidence after Unity resumes.

Markers come in three capture modes: `single-shot` (the default) disarms after the first hit, `continuous` pauses on every hit and keeps a history of earlier frames, and `trace` records every hit without pausing at all. Watch expressions (`uloop enable-watch` / `uloop get-watch-values`) re-evaluate automatically on each paused Editor Step, which is how you follow a value as it changes frame by frame. `--snapshot-timing post-line` captures the values right after the line runs instead, and `--persist` re-arms a marker after a Domain Reload (a compile or entering PlayMode); watch expressions survive Domain Reloads as well. Scripts inside packages take pause points too (`--file Packages/<package-id>/...`). When the Editor's Code Optimization is Release, enabling a pause point switches this Editor session to Debug and says so in a warning; `uloop set-code-optimization debug --startup` makes Debug the default for later sessions.

```bash
# Arm a pause point, fire the input, and wait for the hit in one call
uloop enable-pause-point --file Assets/Scripts/Enemy.cs --line 42 --timeout-seconds 30 \
  --await --trigger "simulate-keyboard --action Press --key Space"

# Inspect the current marker state, then clear it
uloop pause-point-status --id "Assets/Scripts/Enemy.cs:42"
uloop clear-pause-point --id "Assets/Scripts/Enemy.cs:42"
```

### `record-video` — see what one screenshot cannot show

`record-video` records the Game View during PlayMode, or any Editor window such as Scene, Inspector, or Console, to `.mp4` (`.webm` on Linux). An agent cannot watch a video, so the response also points to what it can read: a sheet of up to 32 consecutive frames around the moment you marked (or around the biggest change when nothing was marked), a timeline sheet of the whole recording, and per-frame change numbers. Use it for flicker, stutter, one-frame glitches, fades, and transitions. With `--seconds`, one command starts the recording, runs the `--trigger` command, marks that moment, waits, and stops.

```bash
# Record 3 seconds of the Game View around one key press
uloop record-video --seconds 3 --trigger "simulate-keyboard --action Press --key Space"

# Record an Editor window instead (no PlayMode needed)
uloop record-video --window-name Inspector --seconds 5
```

### `game-view-mode` — check the UI in a device frame

`game-view-mode` reports whether the Game tab shows the Game view or the Device Simulator, and switches it in place. Switching to the Simulator rotates the simulated device to match the Game view's orientation when Player Settings allow it, and `--device` picks the simulated device by name (`--list-devices` lists the names).

```bash
# Show the UI on a simulated device, then switch back
uloop game-view-mode --device "iPhone 12"
uloop game-view-mode --to GameView
```

### `uloop status` and `uloop cancel` — know whether Unity can take a command

`uloop status` reports, without side effects, whether the project's Editor can take a command now, or whether it is starting, compiling, importing assets, busy with another command, or not running. When a running command does not finish, `uloop cancel` stops the tool the Editor is running, and the command that was waiting on it returns `TOOL_CANCELLED`.

> `set-game-view-size` also arrives in V3: it reads and sets the Game View custom rendering resolution (`uloop set-game-view-size --width 1920 --height 1080`), which is useful for keeping the coordinate space of `screenshot --capture-mode rendering` stable across runs.
>
> `simulate-mouse-input` gained `--dry-run`: check what a Game View coordinate hits in 3D physics without injecting mouse input (`uloop simulate-mouse-input --dry-run --x 640 --y 360`). Use it to confirm where a click will land before sending it.

## Improvements to Existing Tools

- **`run-tests`** — `--filter-type class` runs the tests of one class, and `--rerun-failed` reruns only the tests that failed in the last run. `--unsaved-changes` chooses what happens to unsaved Scene and Prefab Stage changes before the run: save them (the default), fail the run, or discard them. For PlayMode tests, `--respect-enter-play-mode-settings` keeps the project's Enter Play Mode settings instead of forcing Domain Reload off.
- **`screenshot` and `simulate-mouse-ui`** — `--annotate-elements` labels UI Toolkit controls as well as uGUI elements, and `simulate-mouse-ui` clicks and drags UI Toolkit controls at the labeled coordinates. `screenshot --prefab-path` renders a prefab or model asset alone in an isolated preview scene, without opening it and without touching the open scenes.
- **`launch`** — `uloop launch -r` (restart) and `-q` (quit) ask Unity to quit by itself first, so it keeps its window layout. Unsaved scene changes are discarded without a save dialog. Unity is force-killed only when it cannot take the request or does not exit within 30 seconds.

## CLI and Distribution Changes

- **npm package to native binary** — the CLI is distributed as platform-native binaries rather than through npm. Node.js 22+ is no longer a requirement for running V3 projects. The V2 `uloop-cli` npm package is obsolete; remove it with `npm uninstall -g uloop-cli` if the installer could not.
- **Linux support** — installing, self-updating, and downloading the project runner work on Linux (x86_64). `focus-window` is not supported there.
- **Homebrew and winget** — besides the install scripts, the CLI installs with `brew install hatayama/tap/uloop` on macOS and `winget install --id hatayama.uloop` on Windows. An install managed by either updates through that package manager (`brew upgrade uloop` / `winget upgrade --id hatayama.uloop`) instead of updating itself.
- **Skills at the Git repository root** — `uloop skills install --claude --root git` installs the skills at the root of the Git repository that contains the Unity project, for repositories where the Unity project sits in a subfolder.
- **No more port management** — the CLI reaches Unity over a Unix domain socket (macOS/Linux) or a named pipe (Windows). There is no port to configure, and no port to collide with another Editor instance.
- **Two-layer architecture, with the runner tracking the Unity package automatically** — a single global `uloop` command (the dispatcher) lives on your `PATH` and delegates to a per-project `uloop-project-runner`. The runner version comes from `.uloop/project-runner-pin.json` in each project and is downloaded into a per-version user cache automatically. Because the runner follows Unity package updates on its own, there is no per-project CLI version to manage, and projects on different versions coexist on one machine.
- **Installer authenticity verification** — release assets carry sigstore attestations, and the installer verifies the downloaded archive against the digests pinned in the repository before running anything.
- **Bounded runtime output** — each subfolder under `.uloop/outputs/` is capped at 20 files, with the oldest removed first, so screenshots, test results, and hierarchy dumps no longer accumulate without limit.
- **Automatic delegation to V2 projects** — when the V3 dispatcher detects that a project still resolves to the V2 package, it installs the matching V2 `uloop-cli` release into a per-version cache and forwards the command. Keeping the V3 dispatcher installed is the supported way to work across V2 and V3 projects at the same time. Note that Node.js 22+ is needed **only if you keep using V2 projects**, because delegation goes through npm; once everything is on V3, Node.js is not needed at all.

## Removed in V3

- **MCP connection** — removed. Use the CLI together with the bundled Skills; every capability that was exposed over MCP is reachable through `uloop` commands.
- **`get-version`, `get-project-info`** — removed as user commands. They were internal diagnostics and were never installed as agent skills, but V2 scripts could still invoke them. Use `uloop --version` for the CLI version, or `execute-dynamic-code` for Unity and project metadata.
- **`record-input`** — removed. Record input in the Unity Editor from **Window > Unity Loop Harness > Recordings**, then play the resulting JSON back with `replay-input`.

## Breaking Changes

- **Boolean options no longer take a value.** V2 accepted `--flag true` and `--flag=false`; V3 uses valueless flags. Options that default to true gained a negative form instead — for example `uloop compile --wait-for-domain-reload false` becomes `uloop compile --no-wait-for-domain-reload`, while `--wait-for-domain-reload true` is simply dropped. Run `uloop <command> --help` to see each flag's default (`default: enabled` / `default: disabled`).
- **Removed commands.** The commands listed above are gone; scripts and skills that call them need to be rewritten against their replacements.
- **The custom tool API moved to a new namespace and type names.** Custom tools now derive from `UnityCliLoopTool<TSchema, TResponse>` under `io.github.hatayama.UnityCliLoop.ToolContracts`. If your project contains C# custom tools written against the V2 API, upgrading to V3 *will* produce compile errors — this is expected, and the built-in migration wizard rewrites the affected files for you, so do not start fixing them by hand. See [Migrating Custom Tools and Skills to V3](migration-v2-to-v3.md).
