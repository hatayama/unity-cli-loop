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

        /// <summary>
        /// Returns up to three deduplicated NextActions for the first ten error messages.
        /// </summary>
        internal static string[] Build(string[] errorMessages)
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

                string nextAction = TryBuildLanguageVersionNextAction(errorMessages[index]);
                if (nextAction == null)
                {
                    continue;
                }

                if (additions.Contains(nextAction))
                {
                    continue;
                }

                additions.Add(nextAction);
            }

            return additions.ToArray();
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
    }
}
