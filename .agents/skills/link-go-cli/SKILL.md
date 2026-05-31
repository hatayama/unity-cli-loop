---
name: link-go-cli
description: Link this repository's local Go uloop CLI as the global `uloop` command. Use when the user asks to link, relink, restore, or verify the Go CLI from this checkout, especially for `scripts/use-local-uloop.sh` or replacing old `npm link` workflows.
---

# Link Go CLI

## Overview

Link the current checkout's native Go CLI so `uloop` on PATH resolves to the development binary under `cli/dist/.../uloop`.

## Workflow

### 1. Confirm Repository State

Run from the repository root and confirm the Go CLI layout exists:

```bash
git rev-parse --show-toplevel
git status --short --branch
test -f cli/go.mod
test -x scripts/check-go-cli.sh
test -x scripts/use-local-uloop.sh
```

If the repository root is different from the visible cwd, switch to the actual root before continuing.

### 2. Verify the Go CLI

Run:

```bash
scripts/check-go-cli.sh
```

Do not continue to linking if formatting, vet, lint, or tests fail.

If the script fails because local development binaries are missing, run `scripts/build-go-cli.sh`, then rerun `scripts/check-go-cli.sh`.

### 3. Link the Local CLI

Use the repository script instead of hand-writing the symlink:

```bash
scripts/use-local-uloop.sh link
```

The script selects the current platform binary:

- `Darwin` + `arm64` or `aarch64`: `cli/dist/darwin-arm64/uloop`
- `Darwin` + `x86_64` or `amd64`: `cli/dist/darwin-amd64/uloop`

If the current platform does not match one of these combinations, stop without linking and report that this repository does not include a local development binary for the current platform.

The script backs up an existing global `uloop` as `uloop.before-local-link` before creating the symlink.

### 4. Inspect or Restore

Use:

```bash
scripts/use-local-uloop.sh status
scripts/use-local-uloop.sh restore
```

### 5. Verify the Link

Run:

```bash
which uloop
readlink "$(which uloop)"
uloop --version
uloop --help
```

The `readlink` result must point at this checkout's `cli/dist/.../uloop`.

## Completion Report

Report the `which uloop` path, symlink target, `uloop --version`, `scripts/check-go-cli.sh` result, and whether a backup exists.
