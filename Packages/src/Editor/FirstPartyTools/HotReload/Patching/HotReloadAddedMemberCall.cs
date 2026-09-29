using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One call a live patch or a registered added member makes into an added member: who calls,
    /// from which file, and the member it was applied against.
    /// </summary>
    internal sealed class HotReloadAddedMemberCall
    {
        internal HotReloadAddedMemberCall(string callerLabel, string callerFilePath, HotReloadCalledAddedMember callee)
        {
            if (string.IsNullOrEmpty(callerLabel))
            {
                throw new ArgumentException("A call is reported with its caller's label.", nameof(callerLabel));
            }

            if (string.IsNullOrEmpty(callerFilePath))
            {
                throw new ArgumentException(
                    "A call is reported with the project-relative path of its caller's file.",
                    nameof(callerFilePath));
            }

            CallerLabel = callerLabel;
            CallerFilePath = callerFilePath;
            Callee = callee ?? throw new ArgumentNullException(nameof(callee));
        }

        internal string CallerLabel { get; }

        internal string CallerFilePath { get; }

        internal HotReloadCalledAddedMember Callee { get; }
    }
}
