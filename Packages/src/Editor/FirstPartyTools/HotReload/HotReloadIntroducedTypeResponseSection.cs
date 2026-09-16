using System.Collections.Generic;
using System.Globalization;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Turns the type outcomes of a run into the rows of the public response.
    /// </summary>
    /// <remarks>
    /// Why apart from the apply response builder: that file already carries the method rows and
    /// the whole warning assembly, and the type section grows further with its own wording.
    /// </remarks>
    internal static class HotReloadIntroducedTypeResponseSection
    {
        internal static bool HoldsFailure(IReadOnlyList<HotReloadIntroducedTypeOutcome> outcomes)
        {
            foreach (HotReloadIntroducedTypeOutcome outcome in outcomes)
            {
                if (outcome.Kind == HotReloadIntroducedTypeOutcomeKind.Failed)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// How many types this run itself activated, which is what a partial apply is measured in.
        /// </summary>
        /// <remarks>
        /// Why the retained declarations are left out: an AlreadyActive row was activated by an
        /// earlier reload, so this run has nothing of its own to keep or discard for it — the same
        /// reason an AlreadyActive method is not counted as patched.
        /// </remarks>
        internal static int CountIntroducedTypes(IReadOnlyList<HotReloadIntroducedTypeOutcome> outcomes)
        {
            return CountOfKind(outcomes, HotReloadIntroducedTypeOutcomeKind.Introduced);
        }

        /// <summary>
        /// The message a run with type rows reports, or false when the methods alone decide it.
        /// </summary>
        /// <remarks>
        /// Why asked before the unchanged-methods message: the ordinary type-only reload edits a
        /// file that also holds untouched methods, so the unchanged message would claim nothing
        /// needed doing about a run that introduced a type.
        /// </remarks>
        internal static bool TryBuildMessage(
            IReadOnlyList<HotReloadIntroducedTypeOutcome> outcomes,
            bool hasMethodFailure,
            int patchedMethodCount,
            int addedMethodCount,
            out string message)
        {
            message = null;
            if (outcomes.Count == 0)
            {
                return false;
            }

            if (HoldsFailure(outcomes))
            {
                message = hasMethodFailure
                    ? HotReloadConstants.IntroducedTypeAndMethodFailureApplyMessage
                    : HotReloadConstants.IntroducedTypeFailureApplyMessage;
                return true;
            }

            if (hasMethodFailure)
            {
                return false;
            }

            int introducedCount = CountOfKind(outcomes, HotReloadIntroducedTypeOutcomeKind.Introduced);
            if (patchedMethodCount + addedMethodCount > 0)
            {
                return TryBuildBodyEditedMessage(
                    outcomes,
                    introducedCount,
                    patchedMethodCount,
                    addedMethodCount,
                    out message);
            }

            message = string.Format(
                CultureInfo.InvariantCulture,
                introducedCount > 0
                    ? HotReloadConstants.IntroducedTypesOnlyApplyMessageFormat
                    : HotReloadConstants.AlreadyActiveIntroducedTypesOnlyApplyMessageFormat,
                introducedCount > 0
                    ? introducedCount
                    : CountOfKind(outcomes, HotReloadIntroducedTypeOutcomeKind.AlreadyActive));
            return true;
        }

        /// <summary>
        /// The message a run reports when the only type rows it carries are declarations a
        /// retained artifact serves and the bodies of some of their methods were patched, or
        /// false when what the methods did is better told by the ordinary apply message.
        /// </summary>
        /// <remarks>
        /// Why only when this run introduced nothing: a run that also activated a type of its own
        /// has two different things to report about its types, and the ordinary apply message with
        /// the type-row summary appended is what says both. Why only when it added nothing: an
        /// added method is not a patched body, so counting it here would both overstate the
        /// patched bodies and drop the addition the ordinary message names.
        /// </remarks>
        private static bool TryBuildBodyEditedMessage(
            IReadOnlyList<HotReloadIntroducedTypeOutcome> outcomes,
            int introducedCount,
            int patchedMethodCount,
            int addedMethodCount,
            out string message)
        {
            message = null;
            if (introducedCount > 0 || addedMethodCount > 0 || !HoldsBodyEditedDeclaration(outcomes))
            {
                return false;
            }

            message = string.Format(
                CultureInfo.InvariantCulture,
                HotReloadConstants.AlreadyActiveIntroducedTypesPatchedApplyMessageFormat,
                CountOfKind(outcomes, HotReloadIntroducedTypeOutcomeKind.AlreadyActive),
                patchedMethodCount);
            return true;
        }

        // A retained declaration whose body changed is the one case where a patched method can
        // belong to a type no compiled assembly of the project carries.
        private static bool HoldsBodyEditedDeclaration(
            IReadOnlyList<HotReloadIntroducedTypeOutcome> outcomes)
        {
            foreach (HotReloadIntroducedTypeOutcome outcome in outcomes)
            {
                if (outcome.Kind == HotReloadIntroducedTypeOutcomeKind.AlreadyActive
                    && outcome.BodyEdited)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Points a message the methods decided at the type rows, so a run that both patched and
        /// introduced does not report only half of what it did.
        /// </summary>
        internal static string AppendTypeSummary(
            string message,
            IReadOnlyList<HotReloadIntroducedTypeOutcome> outcomes)
        {
            if (outcomes.Count == 0)
            {
                return message;
            }

            return message + string.Format(
                CultureInfo.InvariantCulture,
                HotReloadConstants.IntroducedTypesApplyMessageSuffixFormat,
                outcomes.Count);
        }

        private static int CountOfKind(
            IReadOnlyList<HotReloadIntroducedTypeOutcome> outcomes,
            HotReloadIntroducedTypeOutcomeKind kind)
        {
            int count = 0;
            foreach (HotReloadIntroducedTypeOutcome outcome in outcomes)
            {
                if (outcome.Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        internal static List<HotReloadIntroducedTypeResult> BuildRows(
            IReadOnlyList<HotReloadIntroducedTypeOutcome> outcomes)
        {
            List<HotReloadIntroducedTypeResult> rows =
                new List<HotReloadIntroducedTypeResult>(outcomes.Count);
            foreach (HotReloadIntroducedTypeOutcome outcome in outcomes)
            {
                rows.Add(
                    new HotReloadIntroducedTypeResult
                    {
                        Kind = outcome.Kind.ToString(),
                        TypeName = outcome.MetadataName,
                        AssemblyName = outcome.OriginalAssemblyName,
                        FilePath = outcome.OwnerProjectRelativePath,
                        Reason = outcome.Reason
                    });
            }

            return rows;
        }
    }
}
