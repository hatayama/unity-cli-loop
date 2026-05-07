using UnityEditor.Compilation;
using System.Text;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Dev
{
    /// <summary>
    /// Provides Compile Log Display behavior for Unity CLI Loop.
    /// </summary>
    public class CompileLogDisplay : System.IDisposable
    {
        private StringBuilder _logBuilder = new();

        public string LogText => _logBuilder?.ToString() ?? "Compile results will be displayed here.";

        public void Clear()
        {
            if (_logBuilder == null) return;
            _logBuilder.Clear();
            _logBuilder.AppendLine("Compile results will be displayed here.");
        }

        public void RestoreFromText(string text)
        {
            if (_logBuilder == null) return;
            _logBuilder.Clear();
            _logBuilder.Append(text);
        }

        public void AppendStartMessage(string message)
        {
            if (_logBuilder == null) return;
            _logBuilder.AppendLine(message);
        }

        public void AppendAssemblyMessage(string assemblyName, CompilerMessage[] messages)
        {
            if (_logBuilder == null) return;
            _logBuilder.AppendLine($"Assembly [{assemblyName}] compilation finished.");

            foreach (CompilerMessage message in messages)
            {
                if (message.type == CompilerMessageType.Error)
                {
                    _logBuilder.AppendLine($"  [Error] {message.message}");
                }
                else if (message.type == CompilerMessageType.Warning)
                {
                    _logBuilder.AppendLine($"  [Warning] {message.message}");
                }
            }
        }

        public void AppendCompletionMessage(CompileResult result)
        {
            if (_logBuilder == null) return;

            string resultMessage = result.Message;
            if (string.IsNullOrEmpty(resultMessage))
            {
                if (result.Success == true)
                {
                    resultMessage = "Compilation successful! No issues.";
                }
                else if (result.Success == false)
                {
                    resultMessage = "Compilation failed! Please check the errors.";
                }
                else
                {
                    resultMessage = "Compilation status is indeterminate. Use get-logs tool to check results.";
                }
            }

            _logBuilder.AppendLine();

            // Display for when no assemblies were processed (no changes)
            if (!result.IsIndeterminate && result.Messages.Length == 0)
            {
                _logBuilder.AppendLine("No changes, so compilation was skipped.");
                _logBuilder.AppendLine("But it finished without any problems!");
            }

            _logBuilder.AppendLine("=== Compilation Finished ===");
            _logBuilder.AppendLine($"Result: {resultMessage}");
            string hasErrors = "Unknown";
            if (result.Success == true)
            {
                hasErrors = "False";
            }
            else if (result.Success == false)
            {
                hasErrors = "True";
            }
            _logBuilder.AppendLine($"Has Errors: {hasErrors}");
            _logBuilder.AppendLine($"Completion Time: {result.CompletedAt:HH:mm:ss}");

            if (result.Messages.Length > 0)
            {
                _logBuilder.AppendLine($"Errors: {result.ErrorCount}, Warnings: {result.WarningCount}");
            }
            else
            {
                string processedAssemblies;
                if (result.IsIndeterminate)
                {
                    processedAssemblies = "Processed Assemblies: Unknown (compilation did not provide details)";
                }
                else
                {
                    processedAssemblies = "Processed Assemblies: None (no changes)";
                }
                _logBuilder.AppendLine(processedAssemblies);
            }
        }

        public CompileLogDisplay()
        {
            Clear();
        }

        public void Dispose()
        {
            _logBuilder?.Clear();
            _logBuilder = null;
        }
    }
}