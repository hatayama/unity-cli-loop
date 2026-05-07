# execute-dynamic-code design notes

This directory owns the user-facing `execute-dynamic-code` tool contract.
The public contract must stay stable even when the internal compiler strategy changes.

## Fast path intent

- The preferred compilation path is Unity-bundled Roslyn.
- `SharedRoslynCompilerWorkerHost` keeps a warm compiler process so structural cache misses stay fast.
- The shared worker is an optimization only. It must not own response shaping, security validation, or tool-specific policy.
- `DynamicCodeStartupPrewarmer` requests one delayed idle execution at editor startup so the first visible user request does not pay the compiler startup cost.
- Startup prewarm is best-effort. A foreground request may preempt it, and the foreground warmup fallback must remain correct.

## Fallback intent

- Preferred order is: shared Roslyn worker -> one-shot Roslyn -> AssemblyBuilder fallback.
- Fallback changes performance, not semantics.
- Any fallback from the ideal fast path is a maintainer-visible error condition and must be logged through `DynamicCompilationHealthMonitor`.

## Invariants

- Security validation always runs in Unity after compilation and before execution.
- In restricted mode, metadata validation and IL validation must both complete before `Assembly.Load`.
- Tool entry points and use cases depend on runtime-facing facades, not compiler backend details.
- Worker state is disposable. Domain reload or worker protocol failure must be handled by rebuilding state, not by preserving it.
- The compile backend may change, but `ExecuteDynamicCodeResponse` shape and user-facing behavior must remain stable.
