# Introduced Types

A reload can introduce a type the running domain has never compiled. The type is compiled into
an artifact assembly the domain loads and keeps, and the bodies of the same reload bind to it.

## What qualifies

Top-level, non-nested, non-partial, non-generic, declared `public`, `internal`, or without an
access modifier, and one of: class (including `static` helper classes), struct, enum, interface.
An `internal` or modifier-less declaration is compiled as `public` in its artifact, so the
edited bodies of the reload and `uloop execute-dynamic-code` snippets can name it; the source
and its fingerprint keep the modifier as written, so changing it later is a declaration change.

Everything else is refused. The declaration is simply not introduced, the rest of the reload
continues, and `Warnings` carries `<file>: <reason>: <type>` where the reason is one of:

`Generic introduced type requires a compile` · `Partial introduced type requires a compile` ·
`Record introduced type requires a compile` · `File-local introduced type requires a compile` ·
`Ref-like introduced type requires a compile` · `Unsafe introduced type requires a compile` ·
`Unity object introduced type requires a compile` · `Serializable introduced type requires a compile` ·
`Module initializer introduced type requires a compile` · `Delegate introduced type requires a compile` ·
`Unsupported introduced type requires a compile` ·
`Nested type requires a compile` · `Nested declaration inside an introduced type requires a compile` ·
`Internal override in an introduced type requires a compile`

On Unity 2022.3 the bundled compiler predates `file` types, so a `file` declaration fails to parse
instead of being refused. A top-level `private` or `protected` declaration is not repaired: the
artifact compile fails on it.

## Internals of the target assembly

An introduced type can use the `internal` (and `protected internal`) members, interfaces, and
base classes of the assembly it belongs to and of the types earlier reloads introduced into it.
The artifact compiles against copies of those assemblies that expose only `internal`
accessibility, and each artifact method is granted runtime access, and marked never to be
inlined, before any type of the artifact becomes active.

- `private` members stay out of reach; the compiler usually reports them as missing (CS0117).
  Internals of other assemblies stay out of reach, `InternalsVisibleTo` included.
- When the Editor runtime fails the internal-access probe, the artifact compiles against the
  original references: a type that uses no internals is still introduced, one that does fails
  with `Introduced-type compilation failed:` and CS0122.
- A grant refused after loading fails the declaration with `Introduced-type compilation failed:
  Granting internal access to the introduced-type artifact failed:`; nothing new becomes active.
- An override declared `internal`, `protected internal`, or `private protected` of a compiled
  or retained base member is refused (`Internal override in an introduced type requires a
  compile`); it compiles only when the base is introduced in the same reload.
- Internals look public to this compile, so it can accept what a regular compile rejects (a
  `public` member exposing an `internal` type). `uloop compile` is the real check.
- Body edits of, and members added to, an introduced `internal` or modifier-less type end as on
  a `public` introduced type, and what other types add that names it stays applied. A lambda,
  local function, or query expression that uses a compiled `internal` type is `Skipped` on both.
- A member another type adds cannot use a non-public member of an introduced type until
  `uloop compile`. It is `Skipped` on every reload: with `Added members of other types cannot
  use '…', a non-public member of a type hot reload introduced` when the reload edits that type,
  and as a body that could not be bound otherwise. A property counts by the accessor the use
  calls: reading one whose setter is `internal` stays applied, and writing it is `Skipped` naming
  the setter (`….set`).

When an edited body in the same run names a refused type, its shim compile fails with CS0246,
CS0234, or CS0426, or with CS0103 or CS0117 when the body reads a static member of it. That `Failed` row's `Reason` then ends with a note that quotes the refusal
and says `uloop compile` clears it.

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

The same split refuses a change to an introduced type when a new file in the same reload
introduces a type naming it in its signatures: the new type is compiled against the loaded
definition before the edit is applied. The refusal names the new type as introduced by this
reload, next to any retained type that also names the changed one. Reload in two steps: first
without the change, which introduces the new type, then the change together with an edit of every
type the refusal names (a method body change is enough). Editing only the retained type in the
same reload does not help while the new type is still being introduced.

The two steps do not apply when the changed type's file holds only what earlier reloads already
applied and this reload changes nothing in it, for example when the file comes back in only
because the new file uses it. The new type is still compiled against the definition the first
reload loaded, so no order of reloads joins the two, and the refusal says so: run `uloop compile`,
or name the type only inside method bodies of the new type rather than in its signatures. When a
retained type also names it in its signatures, the refusal names that type too, and moving the
use into the new type's bodies works only if the same reload also edits the retained type.

`--revert-all` reverts patches and added members but cannot unload an introduced type; the
response says how many stayed. Auto Refresh stays held while any introduced type is active —
`uloop compile` always releases it, `--revert-all` only when no introduced type remains.
With Domain Reload enabled on Play entry (the default for projects created before Unity 6.6), entering Play Mode reloads the domain and
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
reload *added* to a compiled type or to an earlier introduced type, because introduced types
compile against the compiled assemblies and retained artifacts only, so the compile fails naming
the missing member and says a hot reload addition shares its name (reloading the addition first
does not help); an added method that passes a type declared from source in this reload to a
member of an earlier introduced type whose signature was bound to the compiled copy, which is
`Skipped` naming both types; and any new or changed `.asmdef` / `.asmref`. A snippet run by `uloop execute-dynamic-code` is
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
