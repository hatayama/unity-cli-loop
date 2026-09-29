using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One added member a reloaded body calls, as the reload that applied the body resolved it:
    /// the label the added-member ledger keys it by, and the project-relative path of the file that
    /// declared it then.
    /// </summary>
    /// <remarks>
    /// Why the declaring file is kept: once a later reload retires the member, the ledger no longer
    /// knows where it came from, and a run that touches that file is one that has to say the call
    /// was left behind.
    /// </remarks>
    internal sealed class HotReloadCalledAddedMember
    {
        internal HotReloadCalledAddedMember(string label, string declaringFilePath)
        {
            if (string.IsNullOrEmpty(label))
            {
                throw new ArgumentException("A called added member is recorded with its ledger label.", nameof(label));
            }

            if (string.IsNullOrEmpty(declaringFilePath))
            {
                throw new ArgumentException(
                    "A called added member is recorded with the project-relative path of its declaring file.",
                    nameof(declaringFilePath));
            }

            Label = label;
            DeclaringFilePath = declaringFilePath;
        }

        internal string Label { get; }

        internal string DeclaringFilePath { get; }
    }
}
