using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;

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

            try
            {
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
            catch (IOException ex)
            {
                return CreateFailedResult(ex);
            }
            catch (ArgumentException ex)
            {
                return CreateFailedResult(ex);
            }
            catch (NotSupportedException ex)
            {
                return CreateFailedResult(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return CreateFailedResult(ex);
            }
            catch (SecurityException ex)
            {
                return CreateFailedResult(ex);
            }
        }

        internal static bool ContainsExistingPathSetup(string content, CliPathSetupPlan plan)
        {
            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            string[] references = BuildReferenceCandidates(plan);
            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            string lastPathSetupLine = null;
            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)
                    || trimmedLine.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!LooksLikePathSetupLine(trimmedLine, plan.ShellKind))
                {
                    continue;
                }

                lastPathSetupLine = trimmedLine;
            }

            return lastPathSetupLine != null
                && (string.Equals(lastPathSetupLine, plan.ConfigurationLine, StringComparison.Ordinal)
                    || ContainsPrependingReference(lastPathSetupLine, references, plan.ShellKind));
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
                    || IsFishPathSetCommand(line);
            }

            return line.StartsWith("PATH=", StringComparison.Ordinal)
                || line.StartsWith("PATH =", StringComparison.Ordinal)
                || line.StartsWith("export PATH=", StringComparison.Ordinal)
                || line.StartsWith("export PATH =", StringComparison.Ordinal);
        }

        private static bool ContainsPrependingReference(
            string line,
            string[] references,
            CliPathSetupShellKind shellKind)
        {
            if (shellKind == CliPathSetupShellKind.Fish
                && line.StartsWith("fish_add_path", StringComparison.Ordinal))
            {
                return !IsFishAddPathAppendCommand(line)
                    && IndexOfFirstPathEntryReference(line, references) >= 0;
            }

            int referenceIndex = IndexOfFirstPathEntryReference(line, references);
            if (referenceIndex < 0)
            {
                return false;
            }

            int pathVariableIndex = IndexOfFirstPathVariableReference(line, shellKind);
            if (pathVariableIndex >= 0 && referenceIndex >= pathVariableIndex)
            {
                return false;
            }

            int firstPathEntryIndex = IndexOfFirstPathEntryStart(line, shellKind);
            return firstPathEntryIndex >= 0 && referenceIndex == firstPathEntryIndex;
        }

        private static int IndexOfFirstPathEntryReference(string line, string[] references)
        {
            int firstIndex = -1;
            foreach (string reference in references)
            {
                if (!string.IsNullOrWhiteSpace(reference)
                    && TryFindPathEntryReference(line, reference, out int index)
                    && (firstIndex < 0 || index < firstIndex))
                {
                    firstIndex = index;
                }
            }

            return firstIndex;
        }

        private static int IndexOfFirstPathEntryStart(string line, CliPathSetupShellKind shellKind)
        {
            if (shellKind == CliPathSetupShellKind.Fish)
            {
                return IndexOfFishSetFirstPathEntryStart(line);
            }

            return IndexOfPosixPathFirstEntryStart(line);
        }

        private static int IndexOfPosixPathFirstEntryStart(string line)
        {
            int assignmentIndex = line.IndexOf('=');
            if (assignmentIndex < 0)
            {
                return -1;
            }

            return SkipPathEntryDecorators(line, assignmentIndex + 1);
        }

        private static int IndexOfFishSetFirstPathEntryStart(string line)
        {
            TokenPosition[] tokens = SplitTokenPositions(line);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Value;
                if (!string.Equals(token, "PATH", StringComparison.Ordinal)
                    && !string.Equals(token, "fish_user_paths", StringComparison.Ordinal))
                {
                    continue;
                }

                if (i + 1 >= tokens.Length)
                {
                    return -1;
                }

                return SkipPathEntryDecorators(line, tokens[i + 1].StartIndex);
            }

            return -1;
        }

        private static int IndexOfFirstPathVariableReference(string line, CliPathSetupShellKind shellKind)
        {
            string[] variables = shellKind == CliPathSetupShellKind.Fish
                ? new[] { "$PATH", "${PATH}", "$fish_user_paths" }
                : new[] { "$PATH", "${PATH}" };

            int firstIndex = -1;
            foreach (string variable in variables)
            {
                int index = line.IndexOf(variable, StringComparison.Ordinal);
                if (index >= 0 && (firstIndex < 0 || index < firstIndex))
                {
                    firstIndex = index;
                }
            }

            return firstIndex;
        }

        private static bool IsFishAddPathAppendCommand(string line)
        {
            string[] tokens = line.Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                if (string.Equals(token, "--append", StringComparison.Ordinal)
                    || string.Equals(token, "-a", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFishPathSetCommand(string line)
        {
            string[] tokens = line.Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2 || !string.Equals(tokens[0], "set", StringComparison.Ordinal))
            {
                return false;
            }

            for (int i = 1; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (token.StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }

                return string.Equals(token, "PATH", StringComparison.Ordinal)
                    || string.Equals(token, "fish_user_paths", StringComparison.Ordinal);
            }

            return false;
        }

        private static TokenPosition[] SplitTokenPositions(string line)
        {
            List<TokenPosition> tokens = new List<TokenPosition>();
            int index = 0;
            while (index < line.Length)
            {
                while (index < line.Length && char.IsWhiteSpace(line[index]))
                {
                    index++;
                }

                if (index >= line.Length)
                {
                    break;
                }

                int startIndex = index;
                while (index < line.Length && !char.IsWhiteSpace(line[index]))
                {
                    index++;
                }

                tokens.Add(new TokenPosition(
                    line.Substring(startIndex, index - startIndex),
                    startIndex));
            }

            return tokens.ToArray();
        }

        private static int SkipPathEntryDecorators(string line, int index)
        {
            int currentIndex = index;
            while (currentIndex < line.Length && char.IsWhiteSpace(line[currentIndex]))
            {
                currentIndex++;
            }

            if (currentIndex < line.Length
                && (line[currentIndex] == '"' || line[currentIndex] == '\''))
            {
                currentIndex++;
            }

            return currentIndex;
        }

        private static bool TryFindPathEntryReference(string line, string reference, out int referenceIndex)
        {
            int searchIndex = 0;
            while (searchIndex < line.Length)
            {
                int index = line.IndexOf(reference, searchIndex, StringComparison.Ordinal);
                if (index < 0)
                {
                    referenceIndex = -1;
                    return false;
                }

                if (HasPathEntryBoundaryBefore(line, index)
                    && HasPathEntryBoundaryAfter(line, index + reference.Length))
                {
                    referenceIndex = index;
                    return true;
                }

                searchIndex = index + reference.Length;
            }

            referenceIndex = -1;
            return false;
        }

        private static bool HasPathEntryBoundaryBefore(string line, int index)
        {
            return index == 0 || IsPathEntryBoundary(line[index - 1]);
        }

        private static bool HasPathEntryBoundaryAfter(string line, int index)
        {
            return index == line.Length || IsPathEntryBoundary(line[index]);
        }

        private static bool IsPathEntryBoundary(char character)
        {
            return character == ':'
                || character == '='
                || character == '"'
                || character == '\''
                || char.IsWhiteSpace(character);
        }

        private static bool NeedsLeadingNewLine(string content)
        {
            return !string.IsNullOrEmpty(content)
                && !content.EndsWith("\n", StringComparison.Ordinal);
        }

        private static CliPathSetupApplyResult CreateFailedResult(Exception exception)
        {
            Debug.Assert(exception != null, "exception must not be null");
            return new CliPathSetupApplyResult(
                false,
                CliPathSetupApplyStatus.Failed,
                exception.Message);
        }

        private readonly struct TokenPosition
        {
            public TokenPosition(string value, int startIndex)
            {
                Value = value;
                StartIndex = startIndex;
            }

            public string Value { get; }
            public int StartIndex { get; }
        }
    }
}
