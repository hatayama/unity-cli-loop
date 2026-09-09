using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The two decisions a group run has to make about introduced types and empty generations,
    /// held apart from the commit stage itself.
    /// </summary>
    /// <remarks>
    /// Why a type of its own: the stages the group run calls out to need these decisions, and the
    /// commit stage is built from those stages, so a stage that asked the commit stage for them
    /// would close a construction cycle. This policy depends only on the domain and the file
    /// entry applier, both of which exist before the stages are bound.
    /// </remarks>
    internal sealed class HotReloadGroupCommitPolicy
    {
        private readonly HotReloadDomain _domain;
        private readonly HotReloadFileEntryApplier _fileEntryApplier;

        internal HotReloadGroupCommitPolicy(
            HotReloadDomain domain,
            HotReloadFileEntryApplier fileEntryApplier)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(fileEntryApplier != null, "fileEntryApplier must not be null.");
            _domain = domain;
            _fileEntryApplier = fileEntryApplier;
        }

        // A run whose introduced types become active at the commit boundary: the reload either
        // introduces a type itself, or the assembly it targets already owns one this domain
        // introduced, in which case its patches resolve through the artifact assemblies too.
        internal bool CommitsIntroducedTypes(
            HotReloadPreparedIntroducedTypes prepared,
            string targetAssemblyName)
        {
            return prepared != null
                || _domain.IntroducedTypes.HasActiveTypesForOriginalAssembly(targetAssemblyName);
        }

        // Deleting an added method and restoring its callers yields empty entries, so the
        // post-shim-compile BeginFileGeneration never runs and the previous run's generation would
        // otherwise stay live.
        internal void ClearEmptyFileGenerations(HotReloadApplyContext context)
        {
            foreach (HotReloadGroupFile file in context.Files)
            {
                // A file left unapplied on purpose keeps the previous run's generation even with no
                // entries: clearing it here would let a reload of broken source silently drop the
                // added members the previous run applied.
                if (file.SkipApply)
                {
                    continue;
                }

                _fileEntryApplier.ClearFileGeneration(context, file);
            }
        }
    }
}
