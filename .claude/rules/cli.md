---
paths: Packages/src/Cli~/**
---

# uloop CLI

The uloop CLI is the native Go command surface for communicating with Unity Editor.

## Architecture

- `Dispatcher~` contains the global/user-facing `uloop-dispatcher` entrypoint.
- `Core~` contains the project-local `uloop-core` implementation that resolves Unity connections and dispatches tool calls.
- `Shared~` contains common domain, project, framing, and version helpers used by both binaries.
- `layout-contract.json`, `Core~/contract.json`, and `Dispatcher~/contract.json` define the versioned CLI layout contract.

## Directory Structure

```text
Cli~/
├── Core~/
│   ├── cmd/uloop-core/        # Core binary entrypoint
│   ├── internal/application/  # Use cases
│   ├── internal/ports/        # Boundary interfaces
│   ├── internal/adapters/     # Unity transport and platform adapters
│   └── internal/presentation/ # CLI commands, tools, skills, and output
├── Dispatcher~/
│   ├── cmd/uloop-dispatcher/  # Dispatcher binary entrypoint
│   └── internal/dispatcher/   # Project-local core resolution and dispatch
├── Shared~/                  # Shared domain and adapter packages
└── layout-contract.json
```

## Build and Validation

Use the repository scripts:

```bash
scripts/check-go-cli.sh
```

This runs the Go CLI source checks and validates checked-in dist artifacts.

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

1. CLI-only bundled skills under `Core~/internal/presentation/cli/skill-definitions/cli-only/`
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
