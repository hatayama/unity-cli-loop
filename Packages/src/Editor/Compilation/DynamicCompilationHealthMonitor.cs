using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    internal static class DynamicCompilationHealthMonitor
    {
        private static readonly object ReportedIssueLock = new();
        private static readonly HashSet<string> ReportedIssues = new(System.StringComparer.Ordinal);

        public static void ReportFastPathUnavailable(
            string editorPath,
            string contentsPath,
            IReadOnlyCollection<string> missingComponents)
        {
            string issueKey = $"fast_path_unavailable::{editorPath}";
            LogErrorOnce(
                issueKey,
                "dynamic_code_fast_path_unavailable",
                "execute-dynamic-code fast Roslyn path is unavailable; AssemblyBuilder fallback will be used",
                new
                {
                    editor_path = editorPath,
                    editor_contents_path = contentsPath,
                    missing_components = missingComponents
                },
                "Fast dynamic compilation path is unavailable in this Unity installation.",
                "Check Unity version layout changes and ExternalCompilerPathResolver assumptions.");
        }

        public static void ReportSharedWorkerFallback(string reason, object context = null)
        {
            string issueKey = $"shared_worker_fallback::{reason}";
            LogErrorOnce(
                issueKey,
                "dynamic_code_shared_worker_fallback",
                "execute-dynamic-code shared Roslyn worker is unavailable; falling back to one-shot compiler execution",
                context ?? new { reason },
                "Shared worker fast path is not active.",
                "Investigate shared worker startup, protocol, or platform support.");
        }

        public static void ReportSharedWorkerFailure(string reason, object context = null)
        {
            string issueKey = $"shared_worker_failure::{reason}";
            LogErrorOnce(
                issueKey,
                "dynamic_code_shared_worker_failure",
                "execute-dynamic-code shared Roslyn worker failed to operate correctly",
                context ?? new { reason },
                "Shared Roslyn worker encountered an unexpected failure.",
                "Investigate worker startup, request handling, and Unity-version-specific runtime assumptions.");
        }

        public static void ReportOneShotCompilerStartFailure(object context)
        {
            LogErrorOnce(
                "one_shot_compiler_start_failure",
                "dynamic_code_one_shot_compiler_start_failure",
                "execute-dynamic-code one-shot Roslyn compiler process failed to start",
                context,
                "One-shot Roslyn compiler could not be started.",
                "Investigate Unity-bundled dotnet/csc availability and process launch assumptions.");
        }

        private static void LogErrorOnce(
            string issueKey,
            string operation,
            string message,
            object context,
            string humanNote,
            string aiTodo)
        {
            lock (ReportedIssueLock)
            {
                if (!ReportedIssues.Add(issueKey))
                {
                    return;
                }
            }

            VibeLogger.LogError(
                operation,
                message,
                context,
                humanNote: humanNote,
                aiTodo: aiTodo);
            Debug.LogError($"[{McpConstants.PROJECT_NAME}] {message}");
        }

        internal static void ResetForTests()
        {
            lock (ReportedIssueLock)
            {
                ReportedIssues.Clear();
            }
        }
    }
}
