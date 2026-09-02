using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the asmdef reference-gap NextAction for CS0246 errors raised from scripts that belong to
    /// an Assembly Definition, without depending on CompilerMessage or CompilationPipeline.
    /// </summary>
    internal static class CompileAssemblyDefinitionReferenceHintBuilder
    {
        private static readonly Regex ErrorCodeRegex = new Regex(
            CompileErrorNextActionsConstants.ErrorCodePattern,
            RegexOptions.CultureInvariant);

        private static readonly Regex MissingTypeRegex = new Regex(
            CompileErrorNextActionsConstants.MissingTypePattern,
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Returns at most one hint per asmdef for the first ten errors.
        /// Why CS0246 only, and only under an asmdef: the message cannot name the declaring assembly,
        /// so the hint is worth its noise only where a missing asmdef reference is a plausible cause.
        /// </summary>
        internal static string[] Build(
            CompileErrorOrigin[] errors,
            Func<string, string> findAssemblyDefinitionPath)
        {
            if (errors == null || findAssemblyDefinitionPath == null)
            {
                return Array.Empty<string>();
            }

            List<string> hints = new List<string>();
            HashSet<string> hintedAssemblyDefinitions = new HashSet<string>(StringComparer.Ordinal);
            int scanCount = Math.Min(errors.Length, CompileErrorNextActionsConstants.MaxErrorsToScan);
            for (int index = 0; index < scanCount; index++)
            {
                if (hints.Count >= CompileErrorNextActionsConstants.MaxNextActionsToAppend)
                {
                    break;
                }

                TryAppendHint(hints, hintedAssemblyDefinitions, errors[index], findAssemblyDefinitionPath);
            }

            return hints.ToArray();
        }

        private static void TryAppendHint(
            List<string> hints,
            HashSet<string> hintedAssemblyDefinitions,
            CompileErrorOrigin error,
            Func<string, string> findAssemblyDefinitionPath)
        {
            string missingName = TryExtractMissingTypeName(error.Message);
            if (missingName == null || string.IsNullOrWhiteSpace(error.File))
            {
                return;
            }

            string assemblyDefinitionPath = findAssemblyDefinitionPath(error.File);
            if (string.IsNullOrWhiteSpace(assemblyDefinitionPath))
            {
                return;
            }

            if (!hintedAssemblyDefinitions.Add(assemblyDefinitionPath))
            {
                return;
            }

            hints.Add(string.Format(
                CompileErrorNextActionsConstants.AssemblyDefinitionReferenceHintFormat,
                CompileErrorNextActionsConstants.Cs0246ErrorCode,
                missingName,
                assemblyDefinitionPath));
        }

        /// <summary>
        /// Returns the unresolved name from a CS0246 message, or null for any other error.
        /// </summary>
        private static string TryExtractMissingTypeName(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return null;
            }

            Match errorCodeMatch = ErrorCodeRegex.Match(message);
            if (!errorCodeMatch.Success ||
                errorCodeMatch.Groups[1].Value != CompileErrorNextActionsConstants.Cs0246ErrorCode)
            {
                return null;
            }

            Match missingTypeMatch = MissingTypeRegex.Match(message);
            if (!missingTypeMatch.Success)
            {
                return null;
            }

            return missingTypeMatch.Groups["name"].Value;
        }
    }
}
