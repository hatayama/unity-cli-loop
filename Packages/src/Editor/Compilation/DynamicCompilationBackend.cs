using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor.Compilation;

namespace io.github.hatayama.uLoopMCP
{
    internal sealed class DynamicCompilationBackendResult
    {
        public CompilerMessage[] CompilerMessages { get; }

        public DynamicCompilationBackendKind BackendKind { get; }

        public DynamicCompilationBackendResult(
            CompilerMessage[] compilerMessages,
            DynamicCompilationBackendKind backendKind)
        {
            CompilerMessages = compilerMessages;
            BackendKind = backendKind;
        }
    }

    internal sealed class DynamicCompilationBackend
    {
        public Task<DynamicCompilationBackendResult> CompileAsync(
            string sourcePath,
            string dllPath,
            List<string> references,
            ExternalCompilerPaths externalCompilerPaths,
            CancellationToken ct,
            Action markBuildStarted,
            Action markBuildFinished,
            Action incrementBuildCount)
        {
            if (externalCompilerPaths != null)
            {
                return RoslynCompilerBackend.CompileAsync(
                    sourcePath,
                    dllPath,
                    references,
                    externalCompilerPaths,
                    ct,
                    markBuildStarted,
                    markBuildFinished,
                    incrementBuildCount);
            }

            return AssemblyBuilderFallbackCompilerBackend.CompileAsync(
                sourcePath,
                dllPath,
                references,
                ct,
                markBuildStarted,
                markBuildFinished,
                incrementBuildCount);
        }
    }
}
