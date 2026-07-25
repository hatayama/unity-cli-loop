# execute-dynamic-code architecture notes

The `execute-dynamic-code` pipeline lives under
`Packages/src/Editor/FirstPartyTools/ExecuteDynamicCode/` in the
`UnityCLILoop.FirstPartyTools.Editor` assembly. The layer names below describe the layering
inside that tool plugin, not the onion assemblies around it.

This document deliberately carries no structure diagram and no per-class list. Those duplicate
what the code already states and rot on every refactor; the types themselves carry their own
`/// <summary>` and `// Why` comments. What follows is only what reading the code does not give
you: where to start, and the rules the boundaries were chosen to satisfy.

## Reading order

1. `ExecuteDynamicCodeTool` — delegates and nothing else.
2. `ExecuteDynamicCodeUseCase` — the user-facing workflow: request shaping, the missing-`return`
   retry, the foreground warm-up fallback, and response shaping.
3. `IDynamicCodeExecutionRuntime` and its facade — the single gateway into infrastructure.
4. The infrastructure modules, in dependency order: runtime access, warm-up, planning,
   backend build, safety + load, invocation.
5. `DynamicCodeServices` / `DynamicCodeServicesRegistry` last. It is the one place expected to
   know several concrete classes at once, so a concrete-to-concrete edge that appears only there
   is wiring, not a runtime dependency.

Server startup and recovery do not enter this pipeline. `UnityCliLoopFirstPartyServerLifecycleBinding`
in `UnityCLILoop.CompositionRoot.Editor` is the readiness probe: it resets server-scoped services
and warms the project IPC transport with the internal `get-version` bridge command, so a
user-disabled tool cannot block startup. Dynamic-code warm-up is owned entirely by this pipeline.

## Why the module boundaries sit where they do

- **Runtime access** is the only runtime-facing gateway, so use cases never reach into factory or
  executor wiring. It also arbitrates foreground against idle-only work, which is what lets a real
  user request preempt background compilation.
- **Warm-up** exists so every entrypoint compiles the same snippet shapes. Split them and one path
  can report warm while the shape the user hits first is still cold.
- **Planning** keeps wrapper generation and literal hoisting together, so the compiler never learns
  source-preparation details.
- **Backend build** owns reference resolution, auto-using retry, and backend selection, hiding
  Roslyn worker lifetime from compiler orchestration.
- **Safety + load** loads DLL bytes only after a successful build, keeping `Assembly.Load` behind
  one service so backend code cannot depend on assembly-load mechanics.
- **Invocation** hides reflection-heavy entry-point resolution, letting the executor stay a thin
  bridge between compile and invoke.

## Design intent

- Keep the runtime dependency chain narrower than the composition graph.
- Let the composition root know concrete classes; keep every runtime layer on contracts or use cases.
- Keep the async-only contracts honest.
- Keep performance work inside infrastructure and out of the entry layer.
