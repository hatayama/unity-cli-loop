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
        /// <summary>
        /// Makes Unity's Script Updating Consent dialog decline itself while a CLI compile is in
        /// flight. Safe to call on every compile; only the first call in a domain does the work.
        /// </summary>
        void InstallApiUpdaterConsentDecline();

        void RefreshAssets();

        AssemblyDefinitionConsoleErrorResult FindCurrentAssemblyDefinitionErrors();

        /// <summary>
        /// Whether Unity has a script compilation requested or running. Read right after
        /// <see cref="RefreshAssets"/>, true means the refresh found a change that needs a compile:
        /// a script, an Assembly Definition, or a define.
        /// </summary>
        bool IsScriptCompilationPending();

        /// <summary>
        /// Whether the last script compilation ended with errors, so its errors are only reported
        /// again by compiling.
        /// </summary>
        bool DidLastScriptCompilationFail();

        UnityCliLoopConsoleLogEntry[] ReadConsoleErrorEntries();

        void SubscribeCompilationEvents(
            Action<object> compilationStarted,
            Action<object> compilationFinished,
            Action<string, CompilerMessage[]> assemblyFinished);

        void UnsubscribeCompilationEvents(
            Action<object> compilationStarted,
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
        public void InstallApiUpdaterConsentDecline()
        {
            CompileApiUpdaterConsentPatcher.Install();
        }

        public void RefreshAssets()
        {
            AssetDatabase.Refresh();
        }

        public AssemblyDefinitionConsoleErrorResult FindCurrentAssemblyDefinitionErrors()
        {
            AssemblyDefinitionConsoleErrorValidationService assemblyDefinitionValidationService = new();
            return assemblyDefinitionValidationService.FindCurrentErrors();
        }

        public bool IsScriptCompilationPending()
        {
            // Why isCompiling: it is also true while a compilation is only requested, which is the
            // state a refresh that found script changes leaves behind before Unity starts compiling.
            return EditorApplication.isCompiling;
        }

        public bool DidLastScriptCompilationFail()
        {
            return EditorUtility.scriptCompilationFailed;
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
            Action<object> compilationStarted,
            Action<object> compilationFinished,
            Action<string, CompilerMessage[]> assemblyFinished)
        {
            CompilationPipeline.compilationStarted += compilationStarted;
            CompilationPipeline.compilationFinished += compilationFinished;
            CompilationPipeline.assemblyCompilationFinished += assemblyFinished;
        }

        public void UnsubscribeCompilationEvents(
            Action<object> compilationStarted,
            Action<object> compilationFinished,
            Action<string, CompilerMessage[]> assemblyFinished)
        {
            CompilationPipeline.compilationStarted -= compilationStarted;
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
