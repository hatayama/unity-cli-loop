# What's New in V3

[日本語](whats-new-v3_ja.md)

V3 replaces the npm-distributed CLI with a native Go binary, so Node.js is no longer required to drive Unity from an AI agent. The transport moved from TCP port management to a Unix domain socket on macOS/Linux and a named pipe on Windows, which removes port configuration and port conflicts entirely. The headline new capability is `pause-point`: it stops PlayMode at any `file:line` and reports the locals, parameters, and instance fields at that exact frame, without editing source or recompiling. MCP support and shell completion are gone; the CLI plus Skills is now the only integration path.

## Upgrading

For most users the upgrade is two steps: raise the Unity package version, then open `Window > Unity CLI Loop > Settings` and press **Install CLI** (or **Update CLI**) to replace the old npm CLI with the native dispatcher. The installer attempts to remove the obsolete npm package with `npm uninstall -g uloop-cli` and prints the command to run manually when it cannot.

You only need the migration guide if you wrote your own integrations: C# custom tools built on the V2 extension API, or your own `SKILL.md` files, Markdown docs, shell scripts, or PowerShell scripts that invoke `uloop`. In that case read [Migrating Custom Tools and Skills to V3](migration-v2-to-v3.md) before you start fixing anything by hand.

## Highlights

- **Native Go CLI, no Node.js** — `uloop` ships as a platform binary instead of an npm package. Node.js 22+ is no longer a requirement for running V3 projects.
- **`pause-point` — breakpoint-style investigation without touching code** — pause PlayMode at any source line and read the variables at that frame. No `Debug.Log` statements, no recompile, and it can be armed in the middle of a PlayMode session.
- **No more port management** — the CLI reaches Unity over a Unix domain socket (macOS/Linux) or a named pipe (Windows). There is no port to configure, and no port to collide with another Editor instance.
- **Version compatibility is enforced by protocol version** — the CLI and the Unity package agree on an integer protocol version, so a mismatched pair fails fast with a clear message instead of misbehaving at runtime.
- **Per-project CLI resolution** — each project pins the runner version it needs in `.uloop/project-runner-pin.json`, so several projects on different versions can coexist on one machine.

## New Tools

### `pause-point` — stop at a line and read the frame

`pause-point` patches an already-compiled method at a source `file:line` and pauses Unity when execution reaches it, so you do not edit source or recompile to investigate a bug. The hit response carries `CapturedVariables` — the method's locals, its parameters, and the `this` instance fields — captured immediately before the target line runs, exactly like an IDE breakpoint. Because the values are point-in-time strings rather than live references, they remain valid evidence after Unity resumes.

Markers come in three capture modes: `single-shot` (the default) disarms after the first hit, `continuous` pauses on every hit and keeps a history of earlier frames, and `trace` records every hit without pausing at all. Watch expressions (`uloop enable-watch` / `uloop get-watch-values`) re-evaluate automatically on each paused Editor Step, which is how you follow a value as it changes frame by frame. The Editor's Code Optimization mode must be Debug; enabling is rejected with instructions when it is set to Release.

```bash
# Arm a pause point, fire the input, and wait for the hit in one call
uloop enable-pause-point --file Assets/Scripts/Enemy.cs --line 42 --timeout-seconds 30 \
  --await --trigger "simulate-keyboard --action Press --key Space"

# Inspect the current marker state, then clear it
uloop pause-point-status --id "Assets/Scripts/Enemy.cs:42"
uloop clear-pause-point --id "Assets/Scripts/Enemy.cs:42"
```

### `raycast` — check what a Game View coordinate hits

`raycast` casts a ray from `Camera.main` through a Game View coordinate and reports what 3D physics hits. It takes the same top-left coordinate system as `simulate-mouse-ui`, so you can feed it a coordinate straight from an annotated screenshot and confirm the target before clicking. The response names the hit GameObject and its path, layer, distance, hit point, and hit normal.

Both hit and no-hit responses report `CameraName` and `CameraPath`, which is the fastest way to diagnose a surprising `No physics hit` result — another camera tagged `MainCamera` can silently win the `Camera.main` resolution, so the ray may not start from the viewpoint you assumed. Note that this uses Unity Physics raycasts, not UI EventSystem raycasts.

```bash
uloop raycast --x 960 --y 540
uloop raycast --x 960 --y 540 --layer-mask 1
```

> `set-game-view-size` also arrives in V3: it reads and sets the Game View custom rendering resolution (`uloop set-game-view-size --width 1920 --height 1080`), which is useful for keeping the coordinate space of `screenshot --capture-mode rendering` stable across runs.

## CLI and Distribution Changes

- **npm package to native binary** — the CLI is distributed as a signed platform binary rather than through npm. The V2 `uloop-cli` npm package is obsolete; remove it with `npm uninstall -g uloop-cli` if the installer could not.
- **Two-layer architecture** — a single global `uloop` dispatcher lives on your `PATH` and delegates to a per-project `uloop-project-runner`. The runner version comes from `.uloop/project-runner-pin.json` in each project and is downloaded into a per-version user cache automatically, so upgrading one project does not disturb another.
- **Installer authenticity verification** — release assets carry sigstore attestations. The documented install flow verifies them with `gh attestation verify` against the signing workflow and the release tag's commit before running the installer.
- **Bounded runtime output** — each subfolder under `.uloop/outputs/` is capped at 20 files, with the oldest removed first, so screenshots, test results, and hierarchy dumps no longer accumulate without limit.
- **Automatic delegation to V2 projects** — when the V3 dispatcher detects that a project still resolves to the V2 package, it installs the matching V2 `uloop-cli` release into a per-version cache and forwards the command. Keeping the V3 dispatcher installed is the supported way to work across V2 and V3 projects at the same time. Delegation itself still needs Node.js 22+, since it goes through npm.

## Removed in V3

- **MCP connection** — removed. Use the CLI together with the bundled Skills; every capability that was exposed over MCP is reachable through `uloop` commands.
- **Shell completion** — removed. `uloop completion` remains only as a no-op stub so existing shell profiles do not error out.
- **`capture-window`** — use `screenshot`, which absorbed its role and adds Game View rendering capture and element annotation.
- **`unity-search`, `get-unity-search-providers`, `get-provider-details`** — removed. Use `execute-dynamic-code` to call the Unity Search API directly when you need it, or `find-game-objects` for ordinary scene lookups.
- **`execute-menu-item`, `get-menu-items`** — removed. Use `execute-dynamic-code` with `EditorApplication.ExecuteMenuItem(...)`.

## Breaking Changes

- **Boolean options no longer take a value.** V2 accepted `--flag true` and `--flag=false`; V3 uses valueless flags. Options that default to true gained a negative form instead — for example `uloop compile --wait-for-domain-reload false` becomes `uloop compile --no-wait-for-domain-reload`, while `--wait-for-domain-reload true` is simply dropped. Run `uloop <command> --help` to see each flag's default (`default: enabled` / `default: disabled`).
- **Removed commands.** The commands listed above are gone; scripts and skills that call them need to be rewritten against their replacements.
- **The custom tool API moved to a new namespace and type names.** Custom tools now derive from `UnityCliLoopTool<TSchema, TResponse>` under `io.github.hatayama.UnityCliLoop.ToolContracts`. If your project contains C# custom tools written against the V2 API, upgrading to V3 *will* produce compile errors — this is expected, and the built-in migration wizard rewrites the affected files for you, so do not start fixing them by hand. See [Migrating Custom Tools and Skills to V3](migration-v2-to-v3.md).
