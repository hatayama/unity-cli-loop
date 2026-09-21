# Wiring a value into an added field

A field a reload added has no compiled storage: its values live in a side table, and it is
invisible to the Inspector until `uloop compile`. Nothing serializes into it, so an added
`[SerializeField] GameObject _target;` starts at `default(T)` for every instance.

To put a value or a scene reference in it without compiling, call the wiring entry point from
`uloop execute-dynamic-code`. The reading shim picks the value up on the next access.

## Recipe

```csharp
using UnityEngine;
using io.github.hatayama.UnityCliLoop.ToolContracts;

Enemy enemy = Object.FindObjectOfType<Enemy>();
GameObject target = GameObject.Find("Player");

HotReloadAddedFieldWiring.SetInstanceField(enemy, "_target", target);

HotReloadAddedFieldWiring.TryReadInstanceField(enemy, "_target", out object wired);
Debug.Log("wired: " + wired);
```

A static added field is wired through its type instead:

```csharp
HotReloadAddedFieldWiring.SetStaticField(typeof(Enemy), "_shared", target);
HotReloadAddedFieldWiring.TryReadStaticField(typeof(Enemy), "_shared", out object shared);
```

The field name is the name as written in the edited source. The declaring type comes from the
instance, and a field a base class declares is reached through a derived instance.

## What it refuses, and why that matters

Every check runs before anything is stored, so a refused call leaves an earlier wiring intact.

| Situation | What happens |
|-----------|--------------|
| Field name no active reload added | Throws, listing the added fields that type does have |
| Type has no added fields at all | Throws, saying so and that a reload has to run first |
| Value the field's declared type would not accept | Throws, naming the declared type and the type passed |
| A widening numeric (`int` into a `long` field) | Throws: the reader compares with `is`, so cast first |
| `null` into a non-nullable value-type field | Throws |
| Static field through an instance, or the reverse | Throws, naming the call to use instead |
| A destroyed `UnityEngine.Object` as the instance | Throws: its patched methods never run again |

`TryReadInstanceField` / `TryReadStaticField` return `false` when nothing has been wired yet,
and reading never fills the slot — the field still runs its initializer on the next access.

## Do not call the low-level store directly

`HotReloadAddedFieldStore` is the gateway the generated shims call on every field access. Its
slots are untyped, so it accepts a misspelled key, a value of the wrong type, and a
static/instance mix-up in silence. A wrong-typed value is the worst case: the next read finds
a type it cannot use and replaces it with the field's initializer, so the wiring looks like it
never happened. Use the wiring entry point, which refuses all of those.

## Wired values are not durable

The side table belongs to the current domain and the current reload, and re-running the script is
not enough on its own: `uloop compile`, a domain reload, and `--revert-all` all drop the field's
*declaration* as well, and the wiring call then refuses with

```
'_target' is not an added field of <Type>, which has no active added fields at all. Run a hot
reload that adds the field first; a compile or a domain reload drops the added fields.
```

So the recovery order is always **re-apply the hot reload first, then re-run the wiring script**.
`uloop hot-reload` with no `--files` re-selects the files changed since the last compile, which is
usually the one you want; it works while play mode is running.

After `uloop compile` there is a second case: if the compile included the edit that added the
field, the field is a real compiled field now. Set it through the Inspector or as a normal field
and delete the wiring script — the side table is no longer involved.

### Entering play mode

What play mode costs depends on the project's Enter Play Mode Options.

| Setting | What survives | What to do |
|---------|---------------|------------|
| Domain reload on (Unity's default) | Nothing. `--status` reports `0 change(s) currently active` and says the changes were discarded when play mode was entered | Re-apply the hot reload, then re-run the wiring script — both work from inside play mode |
| Domain reload disabled | The declarations. `--status` still lists the `Active` and `AddedField` rows, and the wiring call is accepted with no re-apply | Re-run the wiring script only |

Values never survive either way: play mode builds the scene's objects again, and an added field on
a new instance starts at its initializer. Wire the instance you are actually looking at.
