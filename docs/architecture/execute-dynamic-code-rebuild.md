# execute-dynamic-code rebuild

This document describes the rebuilt `execute-dynamic-code` pipeline after the layering refactor.

## Layered overview

```mermaid
flowchart TD
    subgraph Entry["Entry layer"]
        Tool["ExecuteDynamicCodeTool"]
        Server["UnityCliLoopServerController"]
    end

    subgraph UseCase["UseCase layer"]
        ExecuteUseCase["IExecuteDynamicCodeUseCase / ExecuteDynamicCodeUseCase"]
        PrewarmUseCase["IPrewarmDynamicCodeUseCase / PrewarmDynamicCodeUseCase"]
    end

    subgraph Infrastructure["Infrastructure layer"]
        subgraph RuntimeAccess["Runtime access module"]
            RuntimeContract["IDynamicCodeExecutionRuntime"]
            RuntimeFacade["DynamicCodeExecutionFacade"]
            ExecutorPoolContract["IDynamicCodeExecutorPool"]
            ExecutorPool["DynamicCodeExecutorPool"]
            Provider["RegistryDynamicCodeExecutorFactory"]
        end

        subgraph Invocation["Invocation module"]
            ExecutorContract["IDynamicCodeExecutor"]
            Executor["DynamicCodeExecutor"]
            InvokerContract["ICompiledCommandInvoker"]
            Runner["CommandRunner"]
            EntryResolver["CompiledCommandEntryPointResolver"]
        end

        subgraph CompilationPipeline["Compilation pipeline module"]
            CompilerContract["IDynamicCompilationService"]
            Compiler["DynamicCodeCompiler"]
        end

        subgraph Planning["Planning module"]
            PlannerContract["IDynamicCompilationPlanner"]
            Planner["DynamicCompilationPlanner"]
            Plan["DynamicCompilationPlan"]
            SourcePrepSvc["DynamicCodeSourcePreparationService"]
            SourcePrep["DynamicCodeSourcePreparer"]
        end

        subgraph BackendBuild["Backend build module"]
            BuilderContract["ICompiledAssemblyBuilder"]
            Builder["CompiledAssemblyBuilder"]
            RefSvc["DynamicReferenceSetBuilderService"]
            RefBuilder["DynamicReferenceSetBuilder"]
            AutoUsing["PreUsingResolver / AutoUsingResolver / AssemblyTypeIndex"]
            Diagnostics["CompilerDiagnostics"]
            Backend["DynamicCompilationBackend"]
            PathSvc["ExternalCompilerPathResolutionService"]
            PathResolver["ExternalCompilerPathResolver"]
            Roslyn["RoslynCompilerBackend"]
            Worker["SharedRoslynCompilerWorkerHost"]
            Fallback["AssemblyBuilderFallbackCompilerBackend"]
        end

        subgraph SafetyLoad["Safety + load module"]
            LoadContract["ICompiledAssemblyLoader"]
            LoadSvc["CompiledAssemblyLoadService"]
            Loader["CompiledAssemblyLoader"]
            Cache["CompilationCacheManager"]
            Timing["DynamicCompilationTimingFormatter"]
        end
    end

    Tool --> ExecuteUseCase
    Server --> PrewarmUseCase

    ExecuteUseCase --> RuntimeContract
    PrewarmUseCase --> RuntimeContract
    RuntimeContract --> RuntimeFacade

    RuntimeFacade --> ExecutorPoolContract
    RuntimeFacade --> BuilderContract
    ExecutorPoolContract --> ExecutorPool
    ExecutorPool --> Provider
    Provider --> ExecutorContract
    ExecutorContract --> Executor
    Executor --> CompilerContract
    CompilerContract --> Compiler
    Executor --> InvokerContract
    InvokerContract --> Runner
    Runner --> EntryResolver

    Compiler --> PlannerContract
    PlannerContract --> Planner
    Planner --> Plan
    Planner --> SourcePrepSvc
    Compiler --> BuilderContract
    BuilderContract --> Builder
    Compiler --> LoadContract
    LoadContract --> LoadSvc
    Compiler --> Cache
    Compiler --> Timing

    SourcePrepSvc --> SourcePrep
    RefSvc --> RefBuilder
    RefBuilder --> AutoUsing
    LoadSvc --> Loader
    Backend --> PathSvc
    Backend --> Roslyn
    Backend --> Fallback
    PathSvc --> PathResolver
    Roslyn --> Worker
```

## Composition graph

```mermaid
flowchart TD
    Services["DynamicCodeServices"]
    SourcePrepSvc["DynamicCodeSourcePreparationService"]
    PathSvc["ExternalCompilerPathResolutionService"]
    Planner["IDynamicCompilationPlanner / DynamicCompilationPlanner"]
    Builder["ICompiledAssemblyBuilder / CompiledAssemblyBuilder"]
    LoadSvc["ICompiledAssemblyLoader / CompiledAssemblyLoadService"]
    Backend["DynamicCompilationBackend"]
    EntryResolver["CompiledCommandEntryPointResolver"]
    Provider["RegistryDynamicCodeExecutorFactory"]
    ExecutorPool["IDynamicCodeExecutorPool / DynamicCodeExecutorPool"]
    RuntimeFacade["DynamicCodeExecutionFacade"]
    ExecuteUseCase["ExecuteDynamicCodeUseCase"]
    PrewarmUseCase["PrewarmDynamicCodeUseCase"]

    Services --> SourcePrepSvc
    Services --> PathSvc
    Services --> Planner
    Services --> Builder
    Services --> LoadSvc
    Services --> Backend
    Services --> EntryResolver
    Services --> Provider
    Services --> ExecutorPool
    Services --> RuntimeFacade
    Services --> ExecuteUseCase
    Services --> PrewarmUseCase

    Provider --> SourcePrepSvc
    Provider --> EntryResolver
    ExecutorPool --> Provider
    RuntimeFacade --> ExecutorPool
    RuntimeFacade --> Builder
    ExecuteUseCase --> RuntimeFacade
    PrewarmUseCase --> RuntimeFacade
```

## Reading guide

1. Start with `Entry layer`.
   - `ExecuteDynamicCodeTool` only delegates the tool workflow.
   - `UnityCliLoopServerController` only requests warm-up after server start and recovery.
2. Move to `UseCase layer`.
   - `ExecuteDynamicCodeUseCase` owns the user-facing workflow for execute-dynamic-code.
   - `PrewarmDynamicCodeUseCase` owns the warm-up workflow.
3. Only then read `Infrastructure layer`.
   - `Runtime access module` is the only runtime-facing gateway.
   - `Planning`, `Backend build`, `Safety + load`, and `Invocation` split the heavy mechanics into named modules.
4. Read `Composition graph` last.
   - `DynamicCodeServices` is the only place that is expected to know many concrete classes at once.
   - If a concrete-to-concrete edge only appears there, it is a wiring edge rather than a runtime dependency.

## Layer responsibilities

- `Entry layer`
  - Translate external calls into use-case invocations.
  - Avoid business workflow logic.
  - Avoid reaching into executor or compiler wiring directly.

- `UseCase layer`
  - Own temporal cohesion.
  - Decide the workflow order for the feature.
  - Keep user-facing retry rules such as the missing-`return` retry in one place.
  - Depend on runtime contracts instead of concrete infrastructure types.

- `Infrastructure layer`
  - Own the mechanics of execution, compilation, loading, caching, path discovery, and worker lifecycle.
  - Keep low-level concerns isolated behind contracts and focused service classes.

## Infrastructure module boundaries

- `Runtime access module`
  - Exposes `IDynamicCodeExecutionRuntime` to the use-case layer.
  - Reuses executors per security level through `IDynamicCodeExecutorPool`.
  - Queries warm-up capability through the build facade instead of reaching into path resolution directly.

- `Planning module`
  - Turns `CompilationRequest` into `DynamicCompilationPlan`.
  - Keeps wrapper generation and literal hoisting together.
  - Prevents `DynamicCodeCompiler` from knowing preparation details.

- `Backend build module`
  - Takes a plan and produces `CompiledAssemblyBuildResult`.
  - Owns reference resolution, auto-using retry, backend selection, temp artifact handling, and build timings.
  - Hides Roslyn worker details from the compiler orchestration.

- `Safety + load module`
  - Loads DLL bytes only after build success.
  - Keeps preload validation, `Assembly.Load`, and IL validation together.
  - Prevents backend code from leaking security decisions.

- `Invocation module`
  - Executes the compiled wrapper method through `ICompiledCommandInvoker`.
  - Keeps reflection-heavy entry-point resolution behind a focused facade.
  - Lets `DynamicCodeExecutor` stay a thin bridge between compile and invoke.

## Class responsibilities

- `ExecuteDynamicCodeTool`
  - Thin entry point for the CLI tool.
  - Delegates the full workflow to `IExecuteDynamicCodeUseCase`.

- `ExecuteDynamicCodeUseCase`
  - Resolves the current security level.
  - Converts parameters into the runtime request.
  - Performs the missing-`return` retry.
  - Shapes `ExecutionResult` into `ExecuteDynamicCodeResponse`.

- `PrewarmDynamicCodeUseCase`
  - Owns the single-flight warm-up flow.
  - Reuses the same runtime contract as the real execution path.

- `IDynamicCodeExecutionRuntime`
  - Contract between use cases and runtime infrastructure.
  - Keeps use cases from depending on factory and executor wiring directly.

- `DynamicCodeExecutionFacade`
  - Reuses executors through `IDynamicCodeExecutorPool`.
  - Checks warm-up capability through `ICompiledAssemblyBuilder`.
  - Hides provider, pool, and build-capability wiring from use cases.

- `DynamicCodeExecutorPool`
  - Owns executor reuse and disposal per security level.
  - Keeps that caching concern out of the runtime facade itself.

- `RegistryDynamicCodeExecutorFactory`
  - Builds `DynamicCodeExecutor` and `CommandRunner` from registered compiler services.
  - Lives in the composition graph and runtime infrastructure, not in the entry layer.

- `DynamicCodeExecutor`
  - Bridges compilation and execution.
  - Merges timing information.
  - Converts hoisted literals into execution parameters.

- `DynamicCodeCompiler`
  - Orchestrates cache lookup, source security, planning, build, and assembly load.
  - Depends on module facades instead of low-level helpers directly.

- `DynamicCompilationPlanner`
  - Produces the normalized `DynamicCompilationPlan`.
  - Keeps request normalization and source preparation together.

- `CompiledAssemblyBuilder`
  - Builds the assembly bytes from a plan.
  - Owns the auto-using retry loop and ambiguity rollback.

- `DynamicCodeSourcePreparationService` / `DynamicCodeSourcePreparer`
  - Normalize snippets into wrapper code.
  - Handle top-level mode, return completion, and literal hoisting.

- `DynamicCompilationBackend`
  - Chooses between the Roslyn path and the AssemblyBuilder fallback path inside the build module.

- `RoslynCompilerBackend` / `SharedRoslynCompilerWorkerHost`
  - Provide the fast path with the shared external worker.

- `CompiledAssemblyLoadService` / `CompiledAssemblyLoader`
  - Keep metadata validation, assembly loading, and IL validation together.

- `CommandRunner` / `CompiledCommandEntryPointResolver`
  - Execute the compiled wrapper method while hiding reflection-heavy lookup.
  - Form the `Invocation` facade seen by `DynamicCodeExecutor`.

## Design intent

- Make the architecture readable as `Entry -> UseCase -> Infrastructure`.
- Keep the runtime dependency chain narrower than the composition graph.
- Allow the composition root to know concrete classes, while runtime layers depend on contracts or use cases.
- Keep the `Infrastructure` layer readable as a set of named module facades instead of one large helper cluster.
- Keep the async-only contracts honest.
- Keep performance work in infrastructure without leaking that complexity into the entry layer.
