using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Appends the single PATH setup line owned by Unity CLI Loop without interpreting full shell startup semantics.
    /// </summary>
    internal static class CliPathSetupWriter
    {
        public static CliPathSetupApplyResult ApplyToFileSystem(CliPathSetupPlan plan)
        {
            return Apply(
                plan,
                File.Exists,
                File.ReadAllText,
                Directory.CreateDirectory,
                File.AppendAllText);
        }

        internal static CliPathSetupApplyResult Apply(
            CliPathSetupPlan plan,
            Func<string, bool> fileExists,
            Func<string, string> readAllText,
            Func<string, DirectoryInfo> createDirectory,
            Action<string, string> appendAllText)
        {
            Debug.Assert(fileExists != null, "fileExists must not be null");
            Debug.Assert(readAllText != null, "readAllText must not be null");
            Debug.Assert(createDirectory != null, "createDirectory must not be null");
            Debug.Assert(appendAllText != null, "appendAllText must not be null");

            if (!plan.CanApplyAutomatically)
            {
                return new CliPathSetupApplyResult(
                    false,
                    CliPathSetupApplyStatus.Unsupported,
                    "This shell is not supported for automatic PATH setup.");
            }

            string existingContent = fileExists(plan.ConfigurationFilePath)
                ? readAllText(plan.ConfigurationFilePath)
                : string.Empty;
            if (ContainsExistingPathSetup(existingContent, plan))
            {
                return new CliPathSetupApplyResult(
                    true,
                    CliPathSetupApplyStatus.AlreadyConfigured,
                    "");
            }

            string configurationDirectory = Path.GetDirectoryName(plan.ConfigurationFilePath);
            if (!string.IsNullOrEmpty(configurationDirectory))
            {
                createDirectory(configurationDirectory);
            }

            string prefix = NeedsLeadingNewLine(existingContent) ? "\n" : string.Empty;
            appendAllText(
                plan.ConfigurationFilePath,
                prefix + plan.ConfigurationLine + "\n");
            return new CliPathSetupApplyResult(
                true,
                CliPathSetupApplyStatus.Applied,
                "");
        }

        internal static bool ContainsExistingPathSetup(string content, CliPathSetupPlan plan)
        {
            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            string[] references = BuildReferenceCandidates(plan);
            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)
                    || trimmedLine.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(trimmedLine, plan.ConfigurationLine, StringComparison.Ordinal))
                {
                    return true;
                }

                if (LooksLikePathSetupLine(trimmedLine, plan.ShellKind)
                    && ContainsAnyReference(trimmedLine, references))
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] BuildReferenceCandidates(CliPathSetupPlan plan)
        {
            List<string> candidates = new List<string>
            {
                plan.InstallDirectory,
                plan.ProfileInstallDirectory
            };

            if (plan.ProfileInstallDirectory.StartsWith("$HOME/", StringComparison.Ordinal))
            {
                string suffix = plan.ProfileInstallDirectory.Substring("$HOME".Length);
                candidates.Add("${HOME}" + suffix);
                candidates.Add("~" + suffix);
            }

            return candidates.ToArray();
        }

        private static bool LooksLikePathSetupLine(string line, CliPathSetupShellKind shellKind)
        {
            if (shellKind == CliPathSetupShellKind.Fish)
            {
                return line.StartsWith("fish_add_path", StringComparison.Ordinal)
                    || (line.StartsWith("set", StringComparison.Ordinal)
                        && line.Contains("PATH"));
            }

            return line.StartsWith("PATH=", StringComparison.Ordinal)
                || line.StartsWith("PATH =", StringComparison.Ordinal)
                || line.StartsWith("export PATH=", StringComparison.Ordinal)
                || line.StartsWith("export PATH =", StringComparison.Ordinal);
        }

        private static bool ContainsAnyReference(string line, string[] references)
        {
            foreach (string reference in references)
            {
                if (!string.IsNullOrWhiteSpace(reference)
                    && line.Contains(reference))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NeedsLeadingNewLine(string content)
        {
            return !string.IsNullOrEmpty(content)
                && !content.EndsWith("\n", StringComparison.Ordinal);
        }
    }
}
