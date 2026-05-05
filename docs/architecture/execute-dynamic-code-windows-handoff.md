# execute-dynamic-code Windows handoff

This note is for resuming the `execute-dynamic-code` rebuild on a Windows machine.

## Branch

- Work on `codex/rebuild-execute-dynamic-code`
- Do not switch back to the historical reference branch unless you need to compare behavior

## What changed already

The rebuild has already moved to a layered shape:

- `Entry`
  - `ExecuteDynamicCodeTool`
  - `McpServerController`
- `UseCase`
  - `ExecuteDynamicCodeUseCase`
  - `PrewarmDynamicCodeUseCase`
- `Infrastructure`
  - `Runtime access`
  - `Invocation`
  - `Compilation pipeline`
  - `Planning`
  - `Backend build`
  - `Safety + load`

The key design rule is:

- Entry knows only use cases
- Use cases know only `IDynamicCodeExecutionRuntime`
- Infrastructure internals are split behind module-level facades

## Read this first

Start with these files in order:

1. `docs/architecture/execute-dynamic-code-rebuild.md`
2. `Packages/src/Editor/Api/McpTools/ExecuteDynamicCode/ExecuteDynamicCodeTool.cs`
3. `Packages/src/Editor/Api/McpTools/UseCases/Tools/ExecuteDynamicCodeUseCase.cs`
4. `Packages/src/Editor/Execution/DynamicCodeExecutionFacade.cs`
5. `Packages/src/Editor/Compilation/DynamicCodeCompiler.cs`
6. `Packages/src/Editor/Compilation/CompiledAssemblyBuilder.cs`

## Important module facades

- Runtime access
  - `IDynamicCodeExecutionRuntime`
  - `IDynamicCodeExecutorPool`
- Invocation
  - `ICompiledCommandInvoker`
- Compilation pipeline
  - `IDynamicCompilationService`
- Planning
  - `IDynamicCompilationPlanner`
- Backend build
  - `ICompiledAssemblyBuilder`
- Safety + load
  - `ICompiledAssemblyLoader`

## Current Windows expectation

Windows is the biggest remaining unknown.

Expected behavior from the current code:

- Shared Roslyn worker is intentionally disabled on `RuntimePlatform.WindowsEditor`
- One-shot Roslyn compile may still work if Unity ships the expected `dotnet.exe` and `DotNetSdkRoslyn` layout
- If Unity's bundled Roslyn files cannot be resolved, the build should fall back to `AssemblyBuilder`

This means:

- Functionality may still work on Windows
- Performance may be slower than macOS
- Fast-path failures should now be visible immediately

## Fast-path failure visibility

If the ideal Roslyn path is not available, the code now emits both:

- `VibeLogger.LogError`
- `Debug.LogError`

Look for messages related to:

- `dynamic_code_fast_path_unavailable`
- `dynamic_code_shared_worker_fallback`
- `dynamic_code_shared_worker_failure`
- `dynamic_code_one_shot_compiler_start_failure`

Relevant files:

- `Packages/src/Editor/Compilation/DynamicCompilationHealthMonitor.cs`
- `Packages/src/Editor/Compilation/ExternalCompilerPathResolver.cs`
- `Packages/src/Editor/Compilation/RoslynCompilerBackend.cs`
- `Packages/src/Editor/Compilation/SharedRoslynCompilerWorkerHost.cs`

## Windows validation checklist

Run these in order on Windows:

1. `uloop compile`
2. `uloop run-tests --test-mode EditMode --filter-type regex --filter-value 'io.github.hatayama.UnityCliLoop.DynamicCodeToolTests'`
3. `uloop execute-dynamic-code --code "return 1 + 2;"`
4. `uloop execute-dynamic-code --code "StringBuilder sb = new StringBuilder(); sb.Append(\"ok\"); return sb.ToString();"`
5. `uloop execute-dynamic-code --code "UnityEngine.GameObject go = new UnityEngine.GameObject(\"bench\"); UnityEngine.Object.DestroyImmediate(go); return \"ok\";"`

Then inspect the Unity Console for:

- compile errors
- fallback error logs
- worker failure logs

## How to interpret results

- If all commands succeed and no fast-path error logs appear:
  - Windows one-shot Roslyn path is probably healthy
- If commands succeed but `dynamic_code_fast_path_unavailable` appears:
  - Windows is running through the `AssemblyBuilder` fallback
- If commands fail and `dynamic_code_one_shot_compiler_start_failure` appears:
  - Unity's bundled `dotnet.exe` or `csc.dll` assumptions are wrong on Windows
- If commands fail and `dynamic_code_shared_worker_failure` appears:
  - The worker path unexpectedly activated or broke before fallback completed

## Likely next steps on Windows

If Windows is broken, investigate in this order:

1. `ExternalCompilerPathResolver`
   - Are `NetCoreRuntime/dotnet.exe` and `DotNetSdkRoslyn/*` really present?
2. `RoslynCompilerBackend`
   - Does the one-shot `dotnet csc.dll` process launch?
3. `AssemblyBuilderFallbackCompilerBackend`
   - If Roslyn fast path is unavailable, does fallback still produce a valid DLL?
4. `SharedRoslynCompilerWorkerHost`
   - Do not enable this on Windows until lifecycle, pipes, and file-lock behavior are proven stable

## Current quality bar before changing more code

Do not optimize Windows first. First make sure:

- basic execution works
- diagnostics are visible
- fallback behavior is understandable
- the current tests stay green

Only after that should Windows-specific performance work start.

## Paste this into Windows Codex

Use this prompt as the starting message on Windows:

```text
Please resume work on branch `codex/rebuild-execute-dynamic-code`.

First read:
- docs/architecture/execute-dynamic-code-rebuild.md
- docs/architecture/execute-dynamic-code-windows-handoff.md

Important recent commits:
- 583cde7e Log dynamic compile fast-path failures for Windows handoff
- 893f42f2 Route prewarm capability through build facade
- 0889d2c6 Split dynamic code infrastructure into module facades

Goal:
- Validate execute-dynamic-code on Windows
- Confirm whether the fast Roslyn path, one-shot Roslyn path, or AssemblyBuilder fallback is being used
- If the ideal path is not active, inspect Unity Console error logs and fix the cause

Suggested validation commands:
1. uloop compile
2. uloop run-tests --test-mode EditMode --filter-type regex --filter-value 'io.github.hatayama.UnityCliLoop.DynamicCodeToolTests'
3. uloop execute-dynamic-code --code "return 1 + 2;"
4. uloop execute-dynamic-code --code "StringBuilder builder1002 = new StringBuilder(); builder1002.Append(\"ok-1002\"); return builder1002.ToString();"
5. uloop execute-dynamic-code --code "DynamicAssemblyTest test1004 = new DynamicAssemblyTest(); return test1004.HelloWorld();"

Watch the Unity Console for:
- dynamic_code_fast_path_unavailable
- dynamic_code_shared_worker_fallback
- dynamic_code_shared_worker_failure
- dynamic_code_one_shot_compiler_start_failure
```
