using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds compile-error NextActions from raw compiler error messages without depending on CompilerMessage.
    /// </summary>
    internal static class CompileErrorNextActionsBuilder
    {
        private static readonly Regex ErrorCodeRegex = new Regex(
            CompileErrorNextActionsConstants.ErrorCodePattern,
            RegexOptions.CultureInvariant);

        private static readonly Regex LanguageVersionFeatureRegex = new Regex(
            CompileErrorNextActionsConstants.LanguageVersionFeaturePattern,
            RegexOptions.CultureInvariant);

        private static readonly Regex MissingNamespaceRegex = new Regex(
            CompileErrorNextActionsConstants.MissingNamespacePattern,
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Returns up to three deduplicated NextActions for the first ten error messages.
        /// </summary>
        internal static string[] Build(
            string[] errorMessages,
            Func<string, string[]> findAssemblyNames = null)
        {
            if (errorMessages == null)
            {
                return Array.Empty<string>();
            }

            List<string> additions = new List<string>();
            int scanCount = Math.Min(errorMessages.Length, CompileErrorNextActionsConstants.MaxErrorsToScan);
            for (int index = 0; index < scanCount; index++)
            {
                if (additions.Count >= CompileErrorNextActionsConstants.MaxNextActionsToAppend)
                {
                    break;
                }

                AppendFromMessage(additions, errorMessages[index], findAssemblyNames);
            }

            return additions.ToArray();
        }

        private static void AppendFromMessage(
            List<string> additions,
            string message,
            Func<string, string[]> findAssemblyNames)
        {
            TryAdd(additions, TryBuildLanguageVersionNextAction(message));
            if (additions.Count >= CompileErrorNextActionsConstants.MaxNextActionsToAppend)
            {
                return;
            }

            TryAdd(additions, TryBuildMissingReferenceNextAction(message, findAssemblyNames));
        }

        private static void TryAdd(List<string> additions, string nextAction)
        {
            if (nextAction == null)
            {
                return;
            }

            if (additions.Contains(nextAction))
            {
                return;
            }

            additions.Add(nextAction);
        }

        /// <summary>
        /// Why: Roslyn's "use language version N or greater" suggestion is a dead end in Unity
        /// because the language version is fixed by the Editor version; without this correction
        /// agents attempt langversion changes.
        /// </summary>
        private static string TryBuildLanguageVersionNextAction(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return null;
            }

            Match errorCodeMatch = ErrorCodeRegex.Match(message);
            if (!errorCodeMatch.Success)
            {
                return null;
            }

            Match featureMatch = LanguageVersionFeatureRegex.Match(message);
            if (!featureMatch.Success)
            {
                return null;
            }

            return string.Format(
                CompileErrorNextActionsConstants.LanguageVersionPinnedNextActionFormat,
                errorCodeMatch.Groups[1].Value,
                featureMatch.Groups["feature"].Value,
                featureMatch.Groups["version"].Value);
        }

        /// <summary>
        /// Why: Unity's "are you missing an assembly reference?" names the problem but not the
        /// assembly; agents burned a round-trip discovering which asmdef reference to add.
        /// </summary>
        private static string TryBuildMissingReferenceNextAction(
            string message,
            Func<string, string[]> findAssemblyNames)
        {
            if (findAssemblyNames == null || string.IsNullOrEmpty(message))
            {
                return null;
            }

            Match errorCodeMatch = ErrorCodeRegex.Match(message);
            if (!errorCodeMatch.Success)
            {
                return null;
            }

            if (errorCodeMatch.Groups[1].Value != CompileErrorNextActionsConstants.Cs0234ErrorCode)
            {
                return null;
            }

            Match namespaceMatch = MissingNamespaceRegex.Match(message);
            if (!namespaceMatch.Success)
            {
                return null;
            }

            string searchName = namespaceMatch.Groups["outer"].Value + "." + namespaceMatch.Groups["inner"].Value;
            string[] assemblyNames = findAssemblyNames(searchName);
            string declaringAssemblies = FormatDeclaringAssemblies(assemblyNames);
            if (declaringAssemblies == null)
            {
                return null;
            }

            return string.Format(
                CompileErrorNextActionsConstants.MissingAssemblyReferenceNextActionFormat,
                errorCodeMatch.Groups[1].Value,
                searchName,
                declaringAssemblies);
        }

        private static string FormatDeclaringAssemblies(string[] assemblyNames)
        {
            if (assemblyNames == null || assemblyNames.Length == 0)
            {
                return null;
            }

            string[] sorted = new string[assemblyNames.Length];
            Array.Copy(assemblyNames, sorted, assemblyNames.Length);
            Array.Sort(sorted, StringComparer.Ordinal);
            int count = Math.Min(sorted.Length, CompileErrorNextActionsConstants.MaxDeclaringAssembliesToName);
            string[] selected = new string[count];
            Array.Copy(sorted, selected, count);
            if (selected.Length == 1)
            {
                return string.Format(
                    CompileErrorNextActionsConstants.SingleDeclaringAssemblyFormat,
                    selected[0]);
            }

            return string.Format(
                CompileErrorNextActionsConstants.MultipleDeclaringAssembliesFormat,
                string.Join("', '", selected));
        }
    }
}
