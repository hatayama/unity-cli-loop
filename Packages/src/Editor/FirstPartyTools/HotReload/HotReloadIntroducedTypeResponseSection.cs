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
        /// How many types this run left active: the ones it introduced plus the ones it bound from
        /// an assembly the domain already retained. Both stay loaded until the next Domain Reload.
        /// </summary>
        internal static int CountCommittedTypes(IReadOnlyList<HotReloadIntroducedTypeOutcome> outcomes)
        {
            return CountOfKind(outcomes, HotReloadIntroducedTypeOutcomeKind.Introduced)
                + CountOfKind(outcomes, HotReloadIntroducedTypeOutcomeKind.AlreadyActive);
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
            int appliedMethodCount,
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

            if (hasMethodFailure || appliedMethodCount > 0)
            {
                return false;
            }

            int introducedCount = CountOfKind(outcomes, HotReloadIntroducedTypeOutcomeKind.Introduced);
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
