using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What one hot-reload run knows that can explain a shim compile error, gathered once per
    /// group so every path that reports the failure appends the same notes.
    /// </summary>
    /// <remarks>
    /// Why one object: the first-compile fallback and the isolation plan each append notes, and
    /// the isolation path passes the material through three calls. Growing that material as
    /// separate parameters would change every one of those signatures again.
    /// </remarks>
    internal sealed class HotReloadCompileFailureNoteSources
    {
        internal HotReloadCompileFailureNoteSources(
            TransformWorkerSkippedDto[] skippedMembers,
            IReadOnlyList<HotReloadRefusedIntroducedType> refusedIntroducedTypes)
        {
            SkippedMembers = skippedMembers ?? Array.Empty<TransformWorkerSkippedDto>();
            RefusedIntroducedTypes = refusedIntroducedTypes ?? Array.Empty<HotReloadRefusedIntroducedType>();
        }

        // Members the worker skipped in this run; a compile error naming one of them is
        // explained by the reason it was skipped.
        internal TransformWorkerSkippedDto[] SkippedMembers { get; }

        // Types the preparation refused in this run; a compile error that cannot find one of
        // them is explained by the refusal, since the source still declares the type.
        internal IReadOnlyList<HotReloadRefusedIntroducedType> RefusedIntroducedTypes { get; }
    }
}
