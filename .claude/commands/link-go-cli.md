---
description: Link this repository's native Go uloop CLI as the global uloop command
allowed-tools: Bash, Read, Grep
---

# Link Go CLI

Link this repository's in-development Go CLI as the global `uloop` command.

## Goal

Build and verify the native Go CLI under `cli`, then point the `uloop` command on PATH at this checkout's local development binary.

## Important Assumptions

- The target is `cli`. Do not use older package-local CLI paths or `npm link` workflows.
- The development binary is generated under `cli/dist/.../uloop`. It is ignored by git and must not be committed.
- Use `scripts/use-local-uloop.sh link|status|restore` instead of hand-writing the symlink.
- Do not treat the link as complete without verification.

## Steps

### Step 1: Confirm the Repository Root

```bash
git rev-parse --show-toplevel
git status --short --branch
```

Confirm that `cli/go.mod`, `scripts/check-go-cli.sh`, and `scripts/use-local-uloop.sh` exist.

### Step 2: Build and Verify the Go CLI

```bash
scripts/check-go-cli.sh
```

This script checks Go CLI formatting, vet, lint, tests, and local dist validation. If it fails because local development binaries are missing, run `scripts/build-go-cli.sh`, then rerun the check.

### Step 3: Link the Local CLI

```bash
scripts/use-local-uloop.sh link
```

The script selects `cli/dist/darwin-arm64/uloop` or `cli/dist/darwin-amd64/uloop` on macOS, backs up an existing global `uloop` as `uloop.before-local-link`, and creates the symlink.

Use `scripts/use-local-uloop.sh status` to inspect the current state and `scripts/use-local-uloop.sh restore` to put the previous global command back.

### Step 4: Verify the Link Result

```bash
which uloop
readlink "$(which uloop)"
uloop --version
uloop --help
```

Confirm that the `readlink` result points at this checkout's `cli/dist/.../uloop`.

## Completion Report

Briefly report the following:

- Result of `which uloop`
- Symlink target
- `uloop --version`
- `scripts/check-go-cli.sh` result
- Whether a backup exists
