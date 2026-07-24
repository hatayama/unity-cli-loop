# Dispatcher / Project Runner Split Decision

Date: 2026-07-24

## Decision

uloop keeps its two-binary CLI architecture: a thin, globally installed dispatcher and a
per-project runner whose exact version is pinned by the Unity package
(`projectRunnerVersion` in `Packages/src/project-runner-pin.json`). We do not consolidate
into a single self-contained CLI binary in the style of Unity's official `unity` CLI
(`unity` 1.0.0-beta.2 + `com.unity.pipeline`, investigated 2026-07-24).

## Context

Unity's official CLI is a single global binary. Its command surface is self-describing: the
Editor-side `com.unity.pipeline` server exposes a schema-annotated command catalog over
`/api/commands`, and the CLI builds its commands (and MCP tools) dynamically from that
catalog on every connection. Version alignment between the CLI and the package is handled
by making the CLI the package's installer: `unity pipeline install` rewrites
`Packages/manifest.json` to the latest package version. No explicit protocol-version gate
between CLI and package was found in the shipped binaries.

uloop already shares the self-describing half of this design: the runner fetches the tool
catalog (including user-defined custom tools) from the Unity package via `get-tool-details`
and caches it in `.uloop/tools.json`, with the embedded `default-tools.json` as a fallback
only. Custom tool authors therefore never depend on the CLI version. The question was
whether to adopt the other half as well — collapsing dispatcher and runner into one global
binary.

## Rationale

### A self-describing catalog carries schemas, not behavior

The single-binary model holds only while the CLI remains a generic dispatcher that wraps
"command name + JSON parameters" in a transport envelope. The uloop runner already contains
client-side behavior that co-evolves with package features: pause-point wait loops,
transient-connection retry, post-compile warmup, test-status polling, progress display.
This class of logic cannot be delivered through a schema catalog — it is code. As
runner-only responsibilities keep growing, the distance from the generic-dispatcher model
grows with them; that trend is an argument against the single-binary model, not for it.

### The split solves cross-project version skew

One machine typically hosts several Unity projects, each pinning its own package version in
version control. A single global binary would have to stay compatible with every package
generation present on the machine at once — either by carrying an internal compatibility
matrix or by forcing lockstep upgrades across all projects. The pin architecture dissolves
the problem instead: each package release names the exact runner release it was built with,
and the dispatcher fetches that runner per project. Package and runner form a co-released
pair, so no compatibility matrix exists anywhere.

Unity's CLI avoids the same problem from the opposite direction — the CLI dictates the
package version by force-writing latest into the manifest. The manifest rewrite itself is
not the structural objection: it only happens on `pipeline install`/`upgrade`, and the same
team discipline that governs every other package ("do not upgrade unilaterally") covers it.
The structural objection is what remains once the package IS pinned: someone must align the
CLI to the pinned package generation, per project, and the single-binary model offers no
mechanism for that:

- No recorded compatibility mapping and no version gate. Nothing states which CLI
  generation pairs with a given package version, and a mismatched pair is not even
  detected — there is nothing to consult when trying to align, and no signal when
  alignment is wrong.
- One global binary per machine. Projects pinned to different package generations would
  need per-project CLI switching, which the model does not provide; a developer moving
  between such projects cannot hold a correct pair for both at once.

(The install channel currently offering only the latest CLI is deliberately not counted
here: that is read as a beta-stage limitation, and a stable release can be expected to
allow version-selected installs.)

uloop's model is the inverse — the package dictates the runner version — and the pin plus
dispatcher is exactly the machinery that makes "align the CLI to the package" automatic:
the compatibility mapping is the pin (`projectRunnerVersion`), and per-project selection is
the dispatcher's job. This matches how Unity projects are actually operated.

### The equality-gated protocol version depends on the split

The IPC `protocolVersion` pair (see `docs/protocol-version.md`) is an exact-equality gate,
not a minimum-compatible range. Equality is only affordable because runner and package
release together and generation skew is structurally rare. A single global binary would
reintroduce skew by design and force range-based compatibility management — a strictly
heavier contract than the current one.

### The dispatcher's cost is deliberately small

The dispatcher's responsibilities are pin resolution, runner download, and self-update via
the pin's `minimumDispatcherVersion` floor (see `docs/project-runner-pin.md`). Keeping it
thin keeps global-install churn low; the price of the second binary is the pin machinery,
which is already built and documented.

## Alternatives

### Single self-contained CLI binary (Unity official CLI style)

Rejected under current requirements. It would require either dropping support for
per-project package pinning (adopting Unity's force-latest install model) or maintaining a
cross-generation compatibility matrix inside one binary. Reversal condition: if uloop ever
commits to always forcing the package to latest and abandons per-project package pinning,
consolidation becomes viable and this decision should be revisited.

### Keep the split but push more runner behavior into data-driven form

Compatible with this decision and worth pursuing opportunistically: the more runner
behavior is generic or catalog-driven, the less often a package release needs a paired
runner release. This reduces coupling but cannot eliminate it, so it complements rather
than replaces the split.
