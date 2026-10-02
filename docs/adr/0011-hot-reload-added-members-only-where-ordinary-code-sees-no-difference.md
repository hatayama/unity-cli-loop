# Hot Reload Emulates an Added Member Only Where Ordinary Code Sees No Difference

Date: 2026-10-01

## Decision

The Mono runtime in the Unity Editor cannot add a field, method, or event to a type that is
already loaded. Every member hot reload calls "added" is therefore an emulation: an added
method runs as a static method on a generated type, and an added field keeps its value in a
store owned by hot reload. Since some emulation is unavoidable, the line is drawn by one
question:

> Does code written the ordinary way — and the Unity messages it relies on — give the same
> result as it would after a compile?

An emulation is taken only when the answer is yes and the remaining differences show up only
through reflection, threading, or states that exist solely because hot reload was used (a
member removed in a later edit, `--revert-all`). An emulation that changes when or in what
order ordinary game code runs is not taken; that change keeps its refusal and a compile.

Applying this question to the requests from the 2026-10-01 usability round:

- **Taken — adding a field-like event to a compiled class.** The event's delegate lives in the
  added-field store under the same key form as an added field. Subscribing, unsubscribing,
  raising, comparing with null, and assigning work as they do after a compile, from the
  declaring type and from other types in the same run. What differs: reflection does not see
  the event, `+=` is not atomic (the store is main-thread only, as for added fields), and the
  store keeps subscriptions until `--revert-all`, a compile, or a domain reload. An instance
  event's subscriptions go with the object; a static event's outlive Play Mode when Domain
  Reload is off, as a compiled static event's do. Event declarations with accessors, events on
  structs or generic classes, events whose declaring or delegate type is not visible outside the
  assembly, and names that clash with a compiled member stay refused.
- **Not taken — forwarding an added `Awake`, `OnEnable`, `OnDisable`, or `OnDestroy`.** Unity
  calls these only on methods that exist when the object is created. Forwarding them through
  a hidden component runs them after the compiled lifecycle methods, ignores Script Execution
  Order, and runs late on objects created by `Instantiate`, on scene load, and on objects that
  are inactive. Closing each gap needs another hook into Unity's own API (`Instantiate`, scene
  loading, activation), and the order still differs from Unity's. Ordinary game code — "spawn,
  then call `Initialize` on the next line" — would get a different result. Adding these
  messages keeps its refusal, and the existing reason still tells the user to compile.
- **Not taken — a `--re-enable` option that calls `OnDisable` then `OnEnable` on live
  instances.** Calling the methods without changing `enabled` produces calls Unity never
  makes, and getting the pairing right (the old `OnDisable` must run before the new body is
  applied) needs a hook in the middle of an apply. The same effect is already available with
  Unity's own semantics: set `enabled = false` with `execute-dynamic-code`, apply with
  `hot-reload`, then set `enabled = true`. Unity calls the old `OnDisable` and the new
  `OnEnable`, and coroutines keep running because only the component is toggled.
- **Not taken in this change — method groups that name an added method
  (`publisher.Hit += OnHit;`), and a stable forwarding stub that would keep such delegates on
  the latest body.** Without the stub, a delegate created from an added method keeps running
  the body of the run that created it, which differs from a compiled method whose body is
  patched in place. The stub fixes that, but creates states plain C# cannot have: a removed
  added method still running through an old delegate, and a delegate that silently does
  nothing after `--revert-all`. These stay refused (`AddedMethodMethodGroupReference`); a
  lambda (`publisher.Hit += h => OnHit(h);`) is the workaround.

## Context

The 2026-10-01 usability round asked three testers what still made them stop Play Mode. Adding
an event and subscribing to it was refused for all three. Adding lifecycle messages and
re-running `OnEnable` on live objects were also requested. A follow-up asked whether the
proposed forwarding rules would work for their code; the answers turned on hypothetical next
steps ("if I later add an `Awake` to the spawned block"), not on code they had written that
day. In each tester's code the objects created at runtime had no `Awake` or `OnEnable` at all.
The compiles they actually made were mostly caused by new `MonoBehaviour` /
`ScriptableObject` types and by wiring new `[SerializeField]` references — which none of these
changes address.

The added-field store already ships, is covered by tests, and has a documented lifetime. An
added event reuses it, so the event change adds no new runtime mechanism.

## Rejected Alternatives

- **Forward added lifecycle messages with synchronous hooks on `Instantiate`, scene loading,
  and activation.** Narrows the timing gaps but still runs added messages after compiled ones
  and outside Script Execution Order, while hooking Unity's object creation for every project
  that uses hot reload. Rejected because ordinary game code still sees a different order.
- **Accept the late delivery and document it.** The case the testers described — initialize on
  the line after `Instantiate` — would be overwritten by an added `Awake` that runs a frame
  later. Documenting a different order does not make code written in Unity's order work.
- **Implement `--re-enable` with the old `OnDisable` before the apply.** Correct, but rebuilds
  inside the tool what toggling `enabled` around an apply already gives with Unity's own
  semantics.
- **Take the stable forwarding stub for added-method delegates now.** All three testers
  preferred it to "the old body until you subscribe again", and it does not change Unity
  message timing. It is deferred, not rejected on principle: the states it creates exist only
  after a removal or a revert, and the question is whether method-group refusals block real
  work often enough to justify a new runtime mechanism.

## Consequences

- An added field-like event on a compiled class can be raised and subscribed to without leaving
  Play Mode. Subscribing with a lambda or an accessible compiled method works; a private
  handler, or one that is an added method, is subscribed through a lambda, since a method group
  naming either stays refused.
- An added `Awake`, `OnEnable`, `OnDisable`, or `OnDestroy` still needs a compile, as before.
  Code added to an existing compiled lifecycle method is patched as any other body edit; Unity
  does not call it again for objects that already ran it.
- The skill documents the `enabled` toggle around an apply for re-running edited `OnEnable`
  subscriptions on live objects.
- A future request to emulate another member kind is triaged by the question in the Decision
  section before any design work.

## Reversal condition

Reopen the lifecycle-forwarding decision if a usability round in which testers develop
normally (not answering questions about the design) shows added-lifecycle refusals as a
leading reason for compiling, or if Unity provides a supported way to register a Unity message
on an existing type. Reopen the method-group decision if such a round shows
`AddedMethodMethodGroupReference` refusals blocking work that the lambda workaround does not
cover.
