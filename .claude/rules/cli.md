---
paths: cli/**
---

# uloop CLI

The uloop CLI is the native Go command surface for communicating with Unity Editor.

## Architecture

- `cmd/uloop` contains the global/user-facing `uloop` entrypoint.
- `internal/cli` contains command parsing, command execution, skills, readiness handling, and output formatting.
- `internal/unityipc` contains Unity Editor IPC transport and framing.
- `internal/install`, `internal/uninstall`, and `internal/update` contain native installer behavior.
- `internal/project`, `internal/tools`, `internal/skills`, and `internal/version` contain shared helpers.
- `layout-contract.json` and `contract.json` define the versioned CLI layout contract.
- `cli/dist` contains generated local development binaries and release assets. It is ignored by git.

## Directory Structure

```text
cli/
├── cmd/uloop/                # Native CLI entrypoint
├── internal/cli/             # Command surface and output
├── internal/unityipc/        # Unity IPC client and framing
├── internal/install/         # Native install command helpers
├── internal/uninstall/       # Native uninstall command helpers
├── internal/update/          # Native update command helpers
├── internal/automation/      # Release and workflow automation helpers
├── internal/project/         # Unity project path helpers
├── internal/skills/          # Bundled skill source helpers
├── internal/tools/           # Tool catalog contracts
├── internal/version/         # Version comparison helpers
├── dist/                     # Ignored generated binaries and release assets
├── contract.json
└── layout-contract.json
```

## Build and Validation

Use the repository scripts:

```bash
scripts/check-go-cli.sh
```

This runs the Go CLI source checks and validates generated local dist artifacts.

Release asset packaging is handled by:

```bash
scripts/package-go-cli.sh
scripts/verify-native-cli-release-assets.sh
```

## Native CLI Releases

The v3 CLI is released through GitHub Release assets built by `native-cli-publish.yml`.

Do not add npm publish or npm version-check workflows for the v3 CLI.

Expected release assets:

- `install.sh`
- `install.ps1`
- `uloop-darwin-amd64.tar.gz`
- `uloop-darwin-arm64.tar.gz`
- `uloop-windows-amd64.zip`
- matching `.sha256` files for each binary archive

## Skills System

Skills are collected from two sources:

1. CLI-only bundled skills under `Packages/src/Editor/CliOnlyTools~/`
2. Project skills scanned from Unity project's `Editor/` folders:
   - `Assets/**/Editor/`
   - `Packages/**/Editor/`
   - `Library/PackageCache/**/Editor/`

Skills with `internal: true` in frontmatter are excluded from the user-facing bundled skills list.

Currently internal skills:

- `uloop-get-project-info`
- `uloop-get-version`

When updating README documentation about bundled skills count, remember to exclude internal skills from the count.

## Domain Reload and Connection Drops

After `compile` command execution, Unity triggers a Domain Reload that disconnects the Unity-side server for a few seconds. This behavior is unavoidable.

When writing CLI tests:

- Prefer pure Go tests for command, contract, and dispatch behavior.
- Use retry-aware helpers for commands that run after `compile`.
- Place compile-related E2E checks at the end of a suite when Unity Editor state is involved.
