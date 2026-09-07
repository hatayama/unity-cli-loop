# Hot Reload: Introduced Types

A hot reload can introduce a type the running domain has never compiled. The type is compiled
into an artifact assembly the Editor domain loads and keeps, and the bodies of the same reload
bind against it. This document states exactly which declarations qualify, how the response
reports the rest, and what still requires `uloop compile`.

Response field reference: `Packages/src/Editor/FirstPartyTools/HotReload/Skill/references/output.md`.
General hot-reload rules: `docs/hot-reload.md`.

## Scope

Supported, when the declaration is top-level (not nested inside another type), non-partial,
non-generic, and `public`:

| Category | Example |
|---|---|
| Class | `public class Order { … }` |
| Static helper class | `public static class OrderMath { … }` |
| Struct | `public struct Money { … }` |
| Enum | `public enum OrderState { … }` |
| Interface | `public interface IPricing { … }` |

Every other shape is refused. A refused declaration is simply not introduced — the source that
declares it stays in the tree, the rest of the reload continues, and the response carries a
`Warnings` entry prefixed with the declaring file:

| Refused shape | `Warnings` text |
|---|---|
| Generic type | `Generic introduced type requires a compile: <type>` |
| `partial` type | `Partial introduced type requires a compile: <type>` |
| `record` / `record struct` | `Record introduced type requires a compile: <type>` |
| Non-`public` type | `Non-public introduced type requires a compile: <type>` |
| `ref struct` | `Ref-like introduced type requires a compile: <type>` |
| Type containing `unsafe` code | `Unsafe introduced type requires a compile: <type>` |
| Type deriving from `UnityEngine.Object` | `Unity object introduced type requires a compile: <type>` |
| Type marked `[Serializable]` | `Serializable introduced type requires a compile: <type>` |
| Type declaring a `[ModuleInitializer]` method | `Module initializer introduced type requires a compile: <type>` |
| Delegate | `Delegate introduced type requires a compile: <type>` |
| Any other type kind | `Unsupported introduced type requires a compile: <type>` |
| Nested type | `Nested type requires a compile: <type>` |
| Type that itself declares a nested type | `Nested declaration inside an introduced type requires a compile: <type>/<nested>` |

Three conditions are reported as `Failed` rows in `IntroducedTypes` instead, because the run
cannot proceed as if the declaration were absent:

| Condition | `Reason` |
|---|---|
| The declaration of a type this domain already introduced has changed | `Changed introduced type requires a compile: <type>` |
| Two files of the same reload declare the same type | `Introduced type <type> is declared in more than one file of the group: <paths>.` |
| The artifact assembly failed to compile | `Introduced-type compilation failed: <compiler output>` |

## Type identity is fixed until the next domain reload

An introduced type is identified by its original assembly name and its metadata name. Once the
reload activates it, that pair names one implementation for the rest of the domain's life.

This is why a changed declaration of an already-introduced type is a `Failed` row rather than a
replacement: the domain cannot unload the artifact assembly, so accepting the edit would leave
callers bound to a definition the source no longer declares. Deleting the declaration does not
unload it either — the type stays loaded, and the active state of the domain is unchanged.
`uloop compile` is the only way to get a changed or removed declaration into the running Editor.

## Partial apply

Type preparation runs before the reload commits its method patches, so a run can introduce types
and still fail a method. The three shapes a caller has to be able to read:

| Run | `Success` | `IntroducedTypes` | `Methods` | `Message` |
|---|---|---|---|---|
| Only a new type, nothing to patch | `true` | one `Introduced` row | empty | `Hot reload introduced 1 type(s); no method body needed patching.` |
| The declaration was already introduced by an earlier reload | `true` | one `AlreadyActive` row | empty | `Hot reload bound 1 introduced type(s) this domain already holds; no method body needed patching.` |
| A type became active, then a method failed | `false` | one `Introduced` row | one `Failed` row | `Hot reload finished with one or more Failed outcomes. See Methods and IntroducedTypes.` |

`ActiveIntroducedTypeTotal` always reports how many types the domain holds after the run,
whatever the methods did. In the third shape the recommended next action is a partial-apply
recovery: the types stay loaded whatever the methods did, so a re-apply is not a clean retry.

## When a compile is still required

- Any refused shape in the tables above.
- Use of the type from another assembly. The artifact is loaded for the reload's own assembly
  group; a caller compiled into a different assembly cannot bind to it.
- Use of the type from code that is not in this reload. A body that is neither passed to the
  reload nor already hot-reloaded still refers to the compiled world, where the type is absent.
- Anything that reads the type through Unity: serialization, `[SerializeField]`, Inspector
  display, `AddComponent`, `ScriptableObject.CreateInstance`, Unity message discovery.
- Use from `uloop execute-dynamic-code`. Dynamic code compiles against the compiled assemblies,
  not against the reload's artifact.
- A new or changed `.asmdef` / `.asmref`. Assembly layout is decided at compile time.

## Lifecycle

- `uloop hot-reload --revert-all` reverts patched methods and added members. It does **not**
  unload an introduced type: the response says how many stayed
  (`N introduced type(s) stay loaded until the next Domain Reload; a revert cannot unload the
  assembly that carries them.`).
- Auto Refresh stays held while any introduced type is active, so returning focus to the Editor
  does not recompile. `--revert-all` releases the hold only when no introduced type remains;
  `uloop compile` always releases it.
- When Domain Reload is enabled on Play entry (the default), entering Play Mode reloads the
  domain, which discards the introduced types along with the patches. They are counted in
  `DroppedByPlayModeEntryCount` on the next `--status`, and re-applying the same declaration
  clears that record. With Enter Play Mode Options set to disable Domain Reload, Play entry
  reloads nothing: the active changes and the introduced types survive it, and nothing is
  recorded as dropped.
- **Values are not preserved.** Nothing carries the state of an introduced type's instances
  across the domain reload that ends its life, and this stage makes no attempt to. Treat an
  introduced type as an Editor-session illusion, exactly like an added member.
- Editing the body of a method **of** an introduced type is out of scope for this stage. The
  type's implementation is fixed once it is active.

## File selection and new files

The default selection of `uloop hot-reload` (no `--files`) reads the compile snapshots, so it
only sees sources Unity has already compiled. A file that has never been compiled is not
selected automatically; pass it (and any other path) with `--files`.

- No snapshot at all: `No compile snapshots exist yet. Run 'uloop compile' first or pass
  project-relative .cs paths with --files. Files that have never been compiled are not selected
  automatically; pass them (and any other path) with --files.`
- A snapshot exists but nothing compiled has changed: `No .cs files changed since the last
  compile were found. Files that have never been compiled are not selected automatically; pass
  them (and any other path) with --files.`

A brand-new script under a brand-new `.asmdef` still needs `uloop compile` first — Unity has to
create the assembly before any reload can target it.
