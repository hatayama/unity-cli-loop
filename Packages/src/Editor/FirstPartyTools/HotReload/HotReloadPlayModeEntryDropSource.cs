using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One introduced type discarded by Play entry, paired with the project-relative file that
    /// declares it so an omitted --files run can select that file again.
    /// </summary>
    internal sealed class HotReloadPlayModeEntryDropSource
    {
        public HotReloadPlayModeEntryDropSource(string identity, string projectRelativePath)
        {
            // Why the separators are refused here: the ledger stores each source as one
            // tab-separated line, which could not be read back into this identity and path.
            RequireStorable(identity, nameof(identity));
            RequireStorable(projectRelativePath, nameof(projectRelativePath));
            Identity = identity;
            ProjectRelativePath = projectRelativePath;
        }

        public string Identity { get; }

        public string ProjectRelativePath { get; }

        private static void RequireStorable(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException(parameterName + " must not be empty.", parameterName);
            }

            if (value.IndexOf(HotReloadPlayModeEntryDropSourceLedger.FieldSeparator) >= 0
                || value.IndexOf(HotReloadPlayModeEntryDropSourceLedger.LineSeparator) >= 0)
            {
                throw new ArgumentException(
                    parameterName + " must not contain a tab or a line break.",
                    parameterName);
            }
        }
    }
}
