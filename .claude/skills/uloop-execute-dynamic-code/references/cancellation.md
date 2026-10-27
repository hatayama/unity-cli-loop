# Cancellation and Long Awaits

## The `ct` token

The snippet body runs inside a generated method with this signature:

```csharp
public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct)
```

So a `CancellationToken` named `ct` is always in scope; use it without declaring anything.

`ct` is cancelled when:

- the `uloop` CLI that sent the snippet disconnects (for example, its process is killed or interrupted),
- a domain reload starts,
- the Unity Editor quits,
- the uloop server inside the Editor stops or restarts,
- the snippet was sent with `--yield-to-foreground-requests` and another `execute-dynamic-code` request arrives that it yields to,
- `uloop cancel` is run while the snippet is running.

## Long awaits block other tool commands

uloop runs one tool command at a time per Editor. While a snippet is awaiting, other tool commands against that Editor (such as `compile`, `get-logs`, or `run-tests`) are rejected as BUSY. An await that may never finish — waiting for an animation or a scripted sequence to complete, or for an external event — keeps the Editor BUSY for as long as it waits. `uloop status` still answers while a tool runs, so use it to check what is running. To stop a running snippet from another terminal, run `uloop cancel`; the `uloop-status` skill's `references/cancel.md` lists its outcomes.

Another `execute-dynamic-code` is not rejected as BUSY: it returns `Success: false` with `ErrorMessage` "Another execution is already in progress". Since the next command an agent sends is often another snippet, treat that message as the sign that an earlier snippet is still awaiting.

## Recommended patterns

Do not add sleeps or polling loops. When you await an API that accepts a cancellation token, pass `ct` to it (`LoadSomethingAsync` stands for any such API):

```csharp
await LoadSomethingAsync(path, ct);
```

Passing `ct` to the API is the first choice, because it stops the underlying work. With UniTask, pass it to APIs that accept one (`cancellationToken: ct`). When an API takes no token, `await SomeUniTask().AttachExternalCancellation(ct);` makes the snippet stop waiting on cancellation, but the original work keeps running. UniTask's extension methods (`AttachExternalCancellation`, `Forget`) need `using Cysharp.Threading.Tasks;` at the top of the snippet; they are not in the default usings, and the automatic using resolution does not cover extension methods (the snippet fails with CS1061).

When you cannot know whether something will finish, start it and return instead of awaiting it. Let the shell do the waiting, then inspect the result with a follow-up command:

```csharp
using UnityEngine;
GameObject target = GameObject.Find("Target");
target.GetComponent<Animator>().Play("Open");
return "started";
```

With UniTask, call `.Forget()` on a sequence you only want to start.

State that the snippet restores at its end — such as `Time.timeScale` — stays changed if the snippet stops midway. Restore it in a separate command rather than relying on the end of a long snippet:

```csharp
Time.timeScale = 1f;
return Time.timeScale;
```

## What happens after `ct` is cancelled

uloop stops waiting for the snippet and frees the Editor for the next command. A snippet that never checks `ct` is not stopped, though: its remaining work keeps running in the background. Pass `ct` so the snippet actually stops.

What the CLI that sent the snippet sees depends on why `ct` fired:

- Yielded to a foreground request, or stopped by `uloop cancel`: the connection is still open, so the CLI receives `Success: false` with `ErrorMessage` "Execution was cancelled or timed out". While a command blocks Unity's main thread, the snippet cannot return, and `uloop status` alone never frees its slot. A tool command other than `execute-dynamic-code` frees it: the slot is taken back 5 seconds after that command first finds it held, and the CLI keeps retrying the busy command through those 5 seconds, so the command then runs.
- The CLI disconnected: no response is sent; the CLI that was waiting is already gone.
- Domain reload, Editor quit, or server stop/restart: no response arrives. The CLI treats it as a lost connection: after a domain reload it prints "Connection lost during execute-dynamic-code", waits for the Editor to come back, and then reports the disconnect as an error. Check the Editor with `uloop status` before sending the next command.
