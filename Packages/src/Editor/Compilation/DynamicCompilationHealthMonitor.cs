using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    internal static class DynamicCompilationHealthMonitor
    {
        private static readonly object ReportedIssueLock = new();
        private static readonly HashSet<string> ReportedIssues = new(System.StringComparer.Ordinal);
        private static readonly AsyncLocal<string> ConsoleDiagnosticSource = new();

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
            string message =
                $"execute-dynamic-code shared Roslyn worker is unavailable; falling back to one-shot compiler execution; reason={reason}";
            LogErrorOnce(
                issueKey,
                "dynamic_code_shared_worker_fallback",
                message,
                context ?? new { reason },
                "Shared worker fast path is not active.",
                "Investigate shared worker startup, protocol, or platform support.");
        }

        public static void ReportSharedWorkerFailure(string reason, object context = null)
        {
            string issueKey = $"shared_worker_failure::{reason}";
            string message =
                $"execute-dynamic-code shared Roslyn worker failed to operate correctly; reason={reason}";
            LogErrorOnce(
                issueKey,
                "dynamic_code_shared_worker_failure",
                message,
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
            string effectiveIssueKey = CreateEffectiveIssueKey(issueKey);

            lock (ReportedIssueLock)
            {
                if (!ReportedIssues.Add(effectiveIssueKey))
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
            Debug.LogError(FormatConsoleErrorMessage(operation, message, context));
        }

        private static string CreateEffectiveIssueKey(string issueKey)
        {
            Debug.Assert(!string.IsNullOrEmpty(issueKey), "issueKey must not be empty");

            if (string.IsNullOrEmpty(ConsoleDiagnosticSource.Value))
            {
                return issueKey;
            }

            return $"{issueKey}::source::{ConsoleDiagnosticSource.Value}";
        }

        internal static System.IDisposable UseConsoleDiagnosticSource(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return EmptyDisposable.Instance;
            }

            string previousSource = ConsoleDiagnosticSource.Value;
            ConsoleDiagnosticSource.Value = source;
            return new ConsoleDiagnosticSourceScope(previousSource);
        }

        private static string FormatConsoleErrorMessage(string operation, string message, object context)
        {
            Debug.Assert(!string.IsNullOrEmpty(operation), "operation must not be empty");
            Debug.Assert(!string.IsNullOrEmpty(message), "message must not be empty");

            string diagnosticSourceLine = string.IsNullOrEmpty(ConsoleDiagnosticSource.Value)
                ? string.Empty
                : $"\ndiagnostic_source: {ConsoleDiagnosticSource.Value}";

            if (context == null)
            {
                return $"[{McpConstants.PROJECT_NAME}] {message}\noperation: {operation}{diagnosticSourceLine}";
            }

            return $"[{McpConstants.PROJECT_NAME}] {message}\noperation: {operation}{diagnosticSourceLine}\ncontext: {context}";
        }

        private sealed class ConsoleDiagnosticSourceScope : System.IDisposable
        {
            private readonly string _previousSource;

            public ConsoleDiagnosticSourceScope(string previousSource)
            {
                _previousSource = previousSource;
            }

            public void Dispose()
            {
                ConsoleDiagnosticSource.Value = _previousSource;
            }
        }

        private sealed class EmptyDisposable : System.IDisposable
        {
            public static readonly EmptyDisposable Instance = new EmptyDisposable();

            public void Dispose()
            {
            }
        }

        internal static void ResetForTests()
        {
            lock (ReportedIssueLock)
            {
                ReportedIssues.Clear();
            }

            ConsoleDiagnosticSource.Value = null;
        }
    }
}
