using System.Collections.Generic;

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
