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
`Success` false:

| Condition | `Reason` starts with |
|---|---|
| The declaration of an already-introduced type changed | `Changed introduced type requires a compile:` |
| Two files of the reload declare the same type | `Introduced type <type> is declared in more than one file of the group:` |
| The artifact assembly did not compile | `Introduced-type compilation failed:` |

## Reading the response

- `Introduced` — this reload compiled and activated the declaration.
- `AlreadyActive` — an earlier reload of this domain already holds it; this reload introduced
  nothing for it. Not an error.
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

`--revert-all` reverts patches and added members but cannot unload an introduced type; the
response says how many stayed. Auto Refresh stays held while any introduced type is active —
`uloop compile` always releases it, `--revert-all` only when no introduced type remains.
With Domain Reload enabled on Play entry (the default), entering Play Mode reloads the domain and
discards the types with the patches; they are counted in `DroppedByPlayModeEntryCount` until a
later apply re-introduces them. With Enter Play Mode Options set to disable Domain Reload, the
active changes and the introduced types survive Play entry and nothing is recorded as dropped.

Values are not preserved across the reload that ends a type's life, and editing the body of a
method **of** an introduced type is out of scope for this stage.

## Still needs `uloop compile`

Any refused shape above; use of the type from another assembly, from a file that is neither
passed to this reload nor already hot-reloaded, or from `uloop execute-dynamic-code`; anything
that reaches the type through Unity (serialization, `[SerializeField]`, Inspector,
`AddComponent`, `CreateInstance`, message discovery); and any new or changed `.asmdef` /
`.asmref`.

## File selection and new files

When `--files` is omitted or empty, a source is selected only when its compilation assembly has
a snapshot directory and that source has its own snapshot file. A missing per-file snapshot is
left out rather than guessed as changed. A file that has never been compiled therefore has no
snapshot and is never selected automatically — pass it with `--files`, or run `uloop compile` to
establish a complete baseline. A script under a brand-new `.asmdef` still needs `uloop compile`
first, because Unity has to create the assembly before a reload can target it.

Full rules, exact response wording, and the compile-required list: `docs/hot-reload-introduced-types.md`.
