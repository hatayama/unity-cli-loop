# Introduced Types

A reload can introduce a type the running domain has never compiled. The type is compiled into
an artifact assembly the domain loads and keeps, and the bodies of the same reload bind to it.

## What qualifies

Top-level, non-nested, non-partial, non-generic, `public`, and one of: class (including
`static` helper classes), struct, enum, interface.

Everything else is refused. The declaration is simply not introduced, the rest of the reload
continues, and `Warnings` carries `<file>: <reason>: <type>` where the reason is one of:

`Generic introduced type requires a compile` · `Partial introduced type requires a compile` ·
`Record introduced type requires a compile` · `Non-public introduced type requires a compile` ·
`Ref-like introduced type requires a compile` · `Unsafe introduced type requires a compile` ·
`Unity object introduced type requires a compile` · `Serializable introduced type requires a compile` ·
`Module initializer introduced type requires a compile` · `Delegate introduced type requires a compile` ·
`Unsupported introduced type requires a compile` ·
`Nested type requires a compile` · `Nested declaration inside an introduced type requires a compile`

Three conditions produce a `Failed` row in `IntroducedTypes` instead, and a `Failed` row makes
`Success` false and leaves every file that shares an assembly with the refused declaration
unapplied — no method body of those files is patched in that run, files in other assemblies still
apply, and patches from earlier reloads stay active:

| Condition | `Reason` starts with |
|---|---|
| The declaration of an already-introduced type changed | `Changed introduced type requires a compile:` |
| A member body of an already-introduced type changed and is neither an ordinary method body nor a getter-only property body | `Changed member body of introduced type requires a compile:` |
| Two files of the reload declare the same type | `Introduced type <type> is declared in more than one file of the group:` |
| The artifact assembly did not compile | `Introduced-type compilation failed:` |

## Reading the response

- `Introduced` — this reload compiled and activated the declaration.
- `AlreadyActive` — an earlier reload of this domain already holds it; this reload introduced
  nothing for it. Not an error. Editing only the bodies of its ordinary methods, or of a
  property whose getter is its only accessor with a body, keeps this row and patches those
  bodies on the artifact that already carries the type. Ordinary methods,
  fields and properties added to the type keep it as well and are applied as `Added` rows.
  A struct is the exception: its method bodies are `Skipped` ("Struct (value type) methods are
  skipped…") on an introduced struct as on a compiled one, so a struct body edit needs
  `uloop compile`.
- `Failed` — refused; see the table above.
- `ActiveIntroducedTypeTotal` counts the types the domain holds after the run, whatever the
  methods did. Type rows never count toward `PatchedTotal`, `ActivePatchTotal`,
  `AddedFieldTotal`, or `ClearedCount`.
- A run can introduce a type and still fail a method: preparation happens before the patches
  commit. The types stay loaded, so treat that run as partially applied rather than retrying it
  blindly.

## Identity and lifetime

An introduced type is identified by its original assembly name plus its metadata name, and that
pair names one implementation for the rest of the domain's life. A changed declaration of an
already-introduced type is therefore `Failed`, not a replacement, and deleting the declaration
does not unload it either. Only `uloop compile` gets a changed or removed declaration into the
Editor.

An edit to an introduced type is also refused when that type appears in the member signatures
of another type that an earlier reload retained and this edit leaves unchanged, because the
change would split the type between the retained assembly and this edit. The refusal names that
type. Editing it in the same reload (a method body change is enough) lets both bind from this
edit, so the run applies; passing its file without editing it changes nothing. Otherwise run
`uloop compile`.

`--revert-all` reverts patches and added members but cannot unload an introduced type; the
response says how many stayed. Auto Refresh stays held while any introduced type is active —
`uloop compile` always releases it, `--revert-all` only when no introduced type remains.
With Domain Reload enabled on Play entry (the default), entering Play Mode reloads the domain and
discards the types with the patches; they are counted in `DroppedByPlayModeEntryCount` until a
later apply re-introduces them. With Enter Play Mode Options set to disable Domain Reload, the
active changes and the introduced types survive Play entry and nothing is recorded as dropped.

Values are not preserved across the reload that ends a type's life. Body-only edits of an
introduced type's ordinary methods, and of a property whose getter is its only accessor with a
body, are patched on the artifact assembly (except on a struct, whose method bodies are
`Skipped`), and added ordinary methods, fields and properties are
applied as `Added` rows. Constructor, setter, init, indexer and event accessor bodies,
initializer bodies, member removals, signature changes, and added constructors, operators,
events, indexers or nested types still require a compile.

## Still needs `uloop compile`

Any refused shape above; use of the type from another assembly, from a file that is neither
passed to this reload nor already hot-reloaded; anything
that reaches the type through Unity (serialization, `[SerializeField]`, Inspector,
`AddComponent`, `CreateInstance`, message discovery); a method body edit of an introduced
struct, which is `Skipped` like any struct method; a call to a member an earlier or the same
reload *added* to a compiled type (an `Added` row), because introduced types compile against the
compiled assemblies and retained artifacts only, so the compile fails naming the missing member;
and any new or changed `.asmdef` / `.asmref`. A snippet run by `uloop execute-dynamic-code` is
the exception: every active artifact is referenced by that compilation, so the snippet can name an
introduced type directly by its full name. When such a snippet still fails on the name, the
diagnostic's `Hint` names the type and how to spell it.

## File selection and new files

When `--files` is omitted or empty, a source is selected only when its compilation assembly has
a snapshot directory and that source has its own snapshot file. A missing per-file snapshot is
left out rather than guessed as changed. A file that has never been compiled therefore has no
snapshot and is never selected automatically — pass it with `--files`, or run `uloop compile` to
establish a complete baseline. A script under a brand-new `.asmdef` still needs `uloop compile`
first, because Unity has to create the assembly before a reload can target it.

One exception: when the Play Mode domain reload discards types that hot reload had introduced,
the files that declare them are remembered. The next reload without `--files` selects them again
after the changed files, as long as they are still on disk, and the message names them. A
successful compile, `--revert-all`, or a reload that brings those types back forgets them.

Full rules, exact response wording, and the compile-required list: `docs/hot-reload-introduced-types.md`.
