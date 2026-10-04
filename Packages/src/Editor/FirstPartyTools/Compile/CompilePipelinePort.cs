using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Unity Editor operations CompileController needs to start and observe a compile request.
    /// Kept behind a port so tests can drive the controller without refreshing assets or compiling.
    /// </summary>
    internal interface ICompilePipelinePort
    {
        void RefreshAssets();

        AssemblyDefinitionConsoleErrorResult FindCurrentAssemblyDefinitionErrors();

        UnityCliLoopConsoleLogEntry[] ReadConsoleErrorEntries();

        void SubscribeCompilationEvents(
            Action<object> compilationFinished,
            Action<string, CompilerMessage[]> assemblyFinished);

        void UnsubscribeCompilationEvents(
            Action<object> compilationFinished,
            Action<string, CompilerMessage[]> assemblyFinished);

        void RequestScriptCompilation(bool cleanBuildCache);

        void StartWatchdog(
            CompileLifecycleRecoveryCoordinator coordinator,
            TaskCompletionSource<CompileResult> compileTask,
            CancellationToken ct);
    }

    /// <summary>
    /// Production compile pipeline that forwards to AssetDatabase, the Unity Console, and CompilationPipeline.
    /// </summary>
    internal sealed class UnityCompilePipelinePort : ICompilePipelinePort
    {
        public void RefreshAssets()
        {
            AssetDatabase.Refresh();
        }

        public AssemblyDefinitionConsoleErrorResult FindCurrentAssemblyDefinitionErrors()
        {
            AssemblyDefinitionConsoleErrorValidationService assemblyDefinitionValidationService = new();
            return assemblyDefinitionValidationService.FindCurrentErrors();
        }

        /// <summary>
        /// Snapshots the current Unity Console error entries for indeterminate-result diagnosis.
        /// </summary>
        public UnityCliLoopConsoleLogEntry[] ReadConsoleErrorEntries()
        {
            IUnityCliLoopConsoleLogService consoleLogs = new LogRetrievalService();
            UnityCliLoopConsoleLogResult errorLogs = consoleLogs.GetLogs(UnityCliLoopLogType.Error);
            return errorLogs.LogEntries;
        }

        public void SubscribeCompilationEvents(
            Action<object> compilationFinished,
            Action<string, CompilerMessage[]> assemblyFinished)
        {
            CompilationPipeline.compilationFinished += compilationFinished;
            CompilationPipeline.assemblyCompilationFinished += assemblyFinished;
        }

        public void UnsubscribeCompilationEvents(
            Action<object> compilationFinished,
            Action<string, CompilerMessage[]> assemblyFinished)
        {
            CompilationPipeline.compilationFinished -= compilationFinished;
            CompilationPipeline.assemblyCompilationFinished -= assemblyFinished;
        }

        public void RequestScriptCompilation(bool cleanBuildCache)
        {
            if (cleanBuildCache)
            {
                CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
                return;
            }

            CompilationPipeline.RequestScriptCompilation();
        }

        public void StartWatchdog(
            CompileLifecycleRecoveryCoordinator coordinator,
            TaskCompletionSource<CompileResult> compileTask,
            CancellationToken ct)
        {
            coordinator.StartWatchdog(compileTask, ct);
        }
    }
}
