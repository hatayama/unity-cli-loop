using System;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Where one row of a worker response has its method: the home of the file the row came from,
    /// or the artifact this domain retains when the row names one. A row names an assembly only
    /// when an artifact serves the type, which is why a name this domain cannot account for is a
    /// broken input rather than a project assembly to fall back to.
    /// </summary>
    internal sealed class HotReloadEntryHomeResolver
    {
        private readonly HotReloadDomain _domain;
        private readonly string _projectRoot;

        internal HotReloadEntryHomeResolver(HotReloadDomain domain, string projectRoot)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");

            _domain = domain;
            _projectRoot = projectRoot;
        }

        /// <summary>
        /// The home a row's method is resolved and patched in. Throws when the row names an
        /// assembly no artifact of this domain carries: resolving it as a project assembly would
        /// look the method up in the assembly the row explicitly said it does not live in.
        /// </summary>
        internal HotReloadTypeHome Resolve(HotReloadTypeHome fileHome, string homeAssemblyName)
        {
            Debug.Assert(fileHome != null, "fileHome must not be null.");

            if (string.IsNullOrEmpty(homeAssemblyName))
            {
                return fileHome;
            }

            HotReloadTypeHome home = _domain.ResolveTypeHome(_projectRoot, homeAssemblyName);
            if (home.Kind != HotReloadTypeHomeKind.RetainedArtifact)
            {
                throw new InvalidOperationException(
                    "Hot reload was asked to patch a method in '"
                    + homeAssemblyName
                    + "', which this domain retains no introduced-type artifact for.");
            }

            return home;
        }
    }
}
