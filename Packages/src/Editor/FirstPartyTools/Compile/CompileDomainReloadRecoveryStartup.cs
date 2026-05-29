using UnityEditor;
using System;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Starts pending compile result recovery after Unity recreates editor assemblies.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class CompileDomainReloadRecoveryStartup
    {
        private const int RecoveryPollIntervalMs = 100;
        private const int RecoveryMaxWaitMs = 5000;

        static CompileDomainReloadRecoveryStartup()
        {
            EditorApplication.delayCall += () => RecoverAfterDomainReload(0);
        }

        private static void RecoverAfterDomainReload(int waitedMs)
        {
            PendingCompileResultRecoveryService recoveryService = CreateRecoveryService();
            bool recoverWhileEditorCompiling = waitedMs >= RecoveryMaxWaitMs;
            PendingCompileRecoveryStatus status = recoveryService.Recover(recoverWhileEditorCompiling);
            if (status == PendingCompileRecoveryStatus.Completed)
            {
                return;
            }

            EditorApplication.delayCall += () => RecoverAfterDomainReload(waitedMs + RecoveryPollIntervalMs);
        }

        private static PendingCompileResultRecoveryService CreateRecoveryService()
        {
            UnityCliLoopEditorSessionStateService sessionStateService =
                new UnityCliLoopEditorSessionStateService(new UnityCliLoopEditorSessionStateRepository());
            return new PendingCompileResultRecoveryService(
                sessionStateService,
                () => EditorApplication.isCompiling,
                CompileResultPersistenceService.ResultExists,
                CompileResultPersistenceService.SaveResult,
                UnityCliLoopPathResolver.GetProjectRoot,
                () => DateTime.UtcNow);
        }
    }
}
