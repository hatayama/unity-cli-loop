using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor.Compilation;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Parses External Compiler Message data into the model used by this module.
    /// </summary>
    internal static class ExternalCompilerMessageParser
    {
        private static readonly Regex DiagnosticRegex = new(
            @"^(?<file>.+)\((?<line>\d+),(?<column>\d+)\): (?<severity>error|warning) (?<code>[A-Z]+\d+): (?<message>.+)$",
            RegexOptions.Compiled);

        public static CompilerMessage[] Parse(
            string stdout,
            string stderr,
            int exitCode)
        {
            List<CompilerMessage> messages = new();
            AddParsedMessages(messages, stdout);
            AddParsedMessages(messages, stderr);

            if (messages.Count > 0)
            {
                if (exitCode != 0 && !HasError(messages))
                {
                    messages.Add(CreateInfrastructureFailureMessage(stdout, stderr, exitCode));
                }

                return messages.ToArray();
            }

            if (exitCode == 0)
            {
                return Array.Empty<CompilerMessage>();
            }

            string combinedOutput = CombineOutput(stdout, stderr);
            return new CompilerMessage[]
            {
                new CompilerMessage
                {
                    type = CompilerMessageType.Error,
                    message = string.IsNullOrWhiteSpace(combinedOutput)
                        ? "External C# compiler failed without diagnostics"
                        : combinedOutput
                }
            };
        }

        private static bool HasError(IReadOnlyCollection<CompilerMessage> messages)
        {
            foreach (CompilerMessage message in messages)
            {
                if (message.type == CompilerMessageType.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static CompilerMessage CreateInfrastructureFailureMessage(
            string stdout,
            string stderr,
            int exitCode)
        {
            string combinedOutput = CombineOutput(stdout, stderr);
            string message = $"External C# compiler exited with code {exitCode} without reporting an error diagnostic";
            if (!string.IsNullOrWhiteSpace(combinedOutput))
            {
                message = $"{message}: {combinedOutput}";
            }

            return new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = message
            };
        }

        private static void AddParsedMessages(
            List<CompilerMessage> messages,
            string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return;
            }

            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                Match match = DiagnosticRegex.Match(line.Trim());
                if (!match.Success)
                {
                    continue;
                }

                messages.Add(new CompilerMessage
                {
                    type = match.Groups["severity"].Value == "warning"
                        ? CompilerMessageType.Warning
                        : CompilerMessageType.Error,
                    message = $"{match.Groups["code"].Value}: {match.Groups["message"].Value}",
                    file = match.Groups["file"].Value,
                    line = int.Parse(match.Groups["line"].Value),
                    column = int.Parse(match.Groups["column"].Value)
                });
            }
        }

        private static string CombineOutput(string stdout, string stderr)
        {
            if (string.IsNullOrWhiteSpace(stdout))
            {
                return stderr?.Trim();
            }

            if (string.IsNullOrWhiteSpace(stderr))
            {
                return stdout.Trim();
            }

            return $"{stdout.Trim()}\n{stderr.Trim()}";
        }
    }
}
