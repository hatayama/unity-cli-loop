using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The references one introduced-type artifact compiles against, and whether they expose the
    /// internal members of its target assembly, or why they could not be built.
    /// </summary>
    /// <remarks>
    /// Why exposure is fixed by the factory and not passed as a flag: an artifact compiled against
    /// exposed references can only run after the runtime grant, so a result claiming raw
    /// references for exposed paths would let an ungranted artifact be published.
    /// </remarks>
    internal sealed class HotReloadArtifactCompileReferences
    {
        public bool Success { get; }

        /// <summary>The reference paths in compile order; null when the build failed.</summary>
        public IReadOnlyList<string> Paths { get; }

        /// <summary>
        /// Whether the target assembly and its retained artifacts are referenced through copies whose
        /// internal members are public, which makes the compiled artifact require the runtime grant.
        /// </summary>
        public bool ExposesInternals { get; }

        public string ErrorMessage { get; }

        private HotReloadArtifactCompileReferences(
            bool success,
            IReadOnlyList<string> paths,
            bool exposesInternals,
            string errorMessage)
        {
            Success = success;
            Paths = paths;
            ExposesInternals = exposesInternals;
            ErrorMessage = errorMessage;
        }

        public static HotReloadArtifactCompileReferences Raw(IReadOnlyList<string> paths)
        {
            return Succeeded(paths, false);
        }

        public static HotReloadArtifactCompileReferences Exposed(IReadOnlyList<string> paths)
        {
            return Succeeded(paths, true);
        }

        public static HotReloadArtifactCompileReferences Failed(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                throw new ArgumentException(
                    "A failed reference build must explain why no references were built.",
                    nameof(errorMessage));
            }

            return new HotReloadArtifactCompileReferences(false, null, false, errorMessage);
        }

        // Why a copy: the builder keeps the list it assembled, and the compilation must see exactly
        // the references this result reported even if that list changes afterwards.
        private static HotReloadArtifactCompileReferences Succeeded(IReadOnlyList<string> paths, bool exposesInternals)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            return new HotReloadArtifactCompileReferences(
                true,
                new List<string>(paths).AsReadOnly(),
                exposesInternals,
                string.Empty);
        }
    }
}
