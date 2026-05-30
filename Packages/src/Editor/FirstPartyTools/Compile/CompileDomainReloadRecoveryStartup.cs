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
    internal static class CompileDomainReloadRecoveryStartup
    {
        private const int RecoveryMaxWaitMs = 5000;

        internal static void Initialize()
        {
            VibeLogger.LogInfo(
                "compile_domain_reload_recovery_scheduled",
                "Scheduled pending compile recovery for the next editor delay call.",
                new { editor_compiling = EditorApplication.isCompiling });
            EditorApplication.delayCall += () => RecoverAfterDomainReload(DateTime.UtcNow);
        }

        private static void RecoverAfterDomainReload(DateTime startedAtUtc)
        {
            PendingCompileResultRecoveryService recoveryService = CreateRecoveryService();
            DateTime utcNow = DateTime.UtcNow;
            TimeSpan elapsed = utcNow - startedAtUtc;
            bool recoverWhileEditorCompiling =
                ShouldRecoverWhileEditorCompiling(startedAtUtc, utcNow);
            VibeLogger.LogInfo(
                "compile_domain_reload_recovery_attempt",
                "Attempting pending compile recovery after Domain Reload.",
                new
                {
                    elapsed_ms = elapsed.TotalMilliseconds,
                    editor_compiling = EditorApplication.isCompiling,
                    recover_while_editor_compiling = recoverWhileEditorCompiling
                });
            PendingCompileRecoveryStatus status = recoveryService.Recover(recoverWhileEditorCompiling);
            if (status == PendingCompileRecoveryStatus.Completed)
            {
                VibeLogger.LogInfo(
                    "compile_domain_reload_recovery_completed",
                    "Pending compile recovery finished for this delay-call pass.",
                    new
                    {
                        elapsed_ms = elapsed.TotalMilliseconds,
                        editor_compiling = EditorApplication.isCompiling
                    });
                return;
            }

            VibeLogger.LogInfo(
                "compile_domain_reload_recovery_retry_scheduled",
                "Pending compile recovery will retry on the next editor delay call.",
                new
                {
                    elapsed_ms = elapsed.TotalMilliseconds,
                    editor_compiling = EditorApplication.isCompiling
                });
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
