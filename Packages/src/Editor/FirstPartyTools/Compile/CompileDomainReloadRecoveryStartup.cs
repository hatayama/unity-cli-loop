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
        private const int RecoveryMaxWaitMs = 5000;

        static CompileDomainReloadRecoveryStartup()
        {
            EditorApplication.delayCall += () => RecoverAfterDomainReload(DateTime.UtcNow);
        }

        private static void RecoverAfterDomainReload(DateTime startedAtUtc)
        {
            PendingCompileResultRecoveryService recoveryService = CreateRecoveryService();
            bool recoverWhileEditorCompiling =
                ShouldRecoverWhileEditorCompiling(startedAtUtc, DateTime.UtcNow);
            PendingCompileRecoveryStatus status = recoveryService.Recover(recoverWhileEditorCompiling);
            if (status == PendingCompileRecoveryStatus.Completed)
            {
                return;
            }

            EditorApplication.delayCall += () => RecoverAfterDomainReload(startedAtUtc);
        }

        internal static bool ShouldRecoverWhileEditorCompiling(DateTime startedAtUtc, DateTime utcNow)
        {
            System.Diagnostics.Debug.Assert(startedAtUtc.Kind == DateTimeKind.Utc, "startedAtUtc must be UTC");
            System.Diagnostics.Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");

            TimeSpan elapsed = utcNow - startedAtUtc;
            return elapsed.TotalMilliseconds >= RecoveryMaxWaitMs;
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
