using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Encodes and decodes the shared Roslyn compiler worker protocol.
    /// </summary>
    internal static class SharedRoslynCompilerWorkerProtocol
    {
        internal const string SharedCompilerWorkerResultPrefix = "__ULOOP_RESULT__";
        internal const string SharedCompilerWorkerEndMarker = "__ULOOP_END__";
        internal const string SharedCompilerWorkerQuitCommand = "__QUIT__";
        internal const string CompileRequestPathPrefix = "path-base64:";
        private const string RoslynWorkerProgramTemplateRelativePath =
            "Editor/FirstPartyTools/ExecuteDynamicCode/DynamicCompilation/Templates/SharedRoslynCompilerWorkerProgram.cs.template";
        private const string SharedCompilerWorkerResultPrefixToken = "{{SHARED_COMPILER_WORKER_RESULT_PREFIX}}";
        private const string SharedCompilerWorkerEndMarkerToken = "{{SHARED_COMPILER_WORKER_END_MARKER}}";
        private const string SharedCompilerWorkerQuitCommandToken = "{{SHARED_COMPILER_WORKER_QUIT_COMMAND}}";
        private const string CompileRequestPathPrefixToken = "{{COMPILE_REQUEST_PATH_PREFIX}}";
        private const int SharedCompilerWorkerResponseTimeoutMilliseconds = 30000;

        internal static string CreateCompileRequestCommand(string requestFilePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(requestFilePath), "requestFilePath must not be empty");

            string fullRequestFilePath = Path.GetFullPath(requestFilePath);
            byte[] requestPathBytes = Encoding.UTF8.GetBytes(fullRequestFilePath);
            return CompileRequestPathPrefix + Convert.ToBase64String(requestPathBytes);
        }

        internal static bool TryParseResponseHeader(string responseHeader, out int exitCode)
        {
            exitCode = 0;

            if (!responseHeader.StartsWith(SharedCompilerWorkerResultPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string statusText = responseHeader.Substring(SharedCompilerWorkerResultPrefix.Length).Trim();
            return int.TryParse(statusText, out exitCode);
        }

        internal static string GetResponseHeaderFailureReason(string responseHeader)
        {
            if (!responseHeader.StartsWith(SharedCompilerWorkerResultPrefix, StringComparison.Ordinal))
            {
                return "worker_invalid_header";
            }

            return "worker_invalid_exit_code";
        }

        internal static List<string> ReadDiagnosticLines(StreamReader reader, CancellationToken ct)
        {
            List<string> outputLines = new();
            while (true)
            {
                string outputLine = ReadProtocolLine(reader, ct);
                if (outputLine == null)
                {
                    return null;
                }

                if (outputLine == SharedCompilerWorkerEndMarker)
                {
                    return outputLines;
                }

                outputLines.Add(outputLine);
            }
        }

        internal static string ReadProtocolLine(StreamReader reader, CancellationToken ct)
        {
            Debug.Assert(reader != null, "reader must not be null");

            Task<string> readTask = Task.Run(() => reader.ReadLine());
            Task timeoutTask = Task.Delay(SharedCompilerWorkerResponseTimeoutMilliseconds, ct);
            Task completedTask = Task.WhenAny(readTask, timeoutTask).GetAwaiter().GetResult();
            if (!ReferenceEquals(completedTask, readTask))
            {
                ct.ThrowIfCancellationRequested();
                return null;
            }

            return readTask.GetAwaiter().GetResult();
        }

        internal static string CreateProgramSource()
        {
            string templatePath = GetWorkerProgramTemplatePath();
            string templateSource = File.ReadAllText(templatePath, Encoding.UTF8);
            return templateSource
                .Replace(SharedCompilerWorkerResultPrefixToken, SharedCompilerWorkerResultPrefix)
                .Replace(SharedCompilerWorkerEndMarkerToken, SharedCompilerWorkerEndMarker)
                .Replace(SharedCompilerWorkerQuitCommandToken, SharedCompilerWorkerQuitCommand)
                .Replace(CompileRequestPathPrefixToken, CompileRequestPathPrefix);
        }

        internal static string GetWorkerProgramTemplatePath()
        {
            return Path.Combine(UnityCliLoopConstants.PackageResolvedPath, RoslynWorkerProgramTemplateRelativePath);
        }
    }
}
