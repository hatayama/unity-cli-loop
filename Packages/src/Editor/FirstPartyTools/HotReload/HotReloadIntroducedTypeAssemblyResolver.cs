using System;
using System.Collections.Generic;
using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves active and scoped prepared artifact assemblies by exact full identity only.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeAssemblyResolver : IDisposable
    {
        private readonly HotReloadIntroducedTypeRegistry registry;
        private readonly Dictionary<string, Assembly> preparedAssemblies =
            new Dictionary<string, Assembly>(StringComparer.Ordinal);
        private bool disposed;

        internal int ResolutionCount { get; private set; }

        public HotReloadIntroducedTypeAssemblyResolver(HotReloadIntroducedTypeRegistry registry)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        public IDisposable RegisterPrepared(HotReloadIntroducedTypeArtifact artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            preparedAssemblies.Add(artifact.AssemblyFullName, artifact.Assembly);
            return new PreparedAssemblyScope(preparedAssemblies, artifact.AssemblyFullName);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
            preparedAssemblies.Clear();
            disposed = true;
        }

        internal Assembly ResolveExact(string requestedAssemblyFullName)
        {
            if (string.IsNullOrEmpty(requestedAssemblyFullName))
            {
                return null;
            }

            if (preparedAssemblies.TryGetValue(requestedAssemblyFullName, out Assembly prepared))
            {
                return prepared;
            }

            if (registry.TryResolveActiveAssembly(requestedAssemblyFullName, out HotReloadIntroducedTypeArtifact active))
            {
                return active.Assembly;
            }

            return null;
        }

        private Assembly Resolve(object sender, ResolveEventArgs arguments)
        {
            ResolutionCount++;
            return arguments == null ? null : ResolveExact(arguments.Name);
        }

        private sealed class PreparedAssemblyScope : IDisposable
        {
            private readonly Dictionary<string, Assembly> assemblies;
            private readonly string assemblyFullName;

            public PreparedAssemblyScope(Dictionary<string, Assembly> assemblies, string assemblyFullName)
            {
                this.assemblies = assemblies;
                this.assemblyFullName = assemblyFullName;
            }

            public void Dispose()
            {
                assemblies.Remove(assemblyFullName);
            }
        }
    }
}
