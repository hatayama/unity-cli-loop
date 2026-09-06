using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves active and scoped prepared artifact assemblies by exact full identity only.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeAssemblyResolver : IDisposable
    {
        private readonly HotReloadIntroducedTypeRegistry registry;
        private readonly object gate;
        private readonly Dictionary<string, Assembly> preparedAssemblies =
            new Dictionary<string, Assembly>(StringComparer.Ordinal);
        private int resolutionCount;
        private bool disposed;

        internal int ResolutionCount => Volatile.Read(ref resolutionCount);

        public HotReloadIntroducedTypeAssemblyResolver(HotReloadIntroducedTypeRegistry registry)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            // The prepared map and the registry's mappings are read together inside one resolve, so
            // they share the registry's gate rather than taking two locks in an unspecified order.
            gate = registry.Gate;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        public IDisposable RegisterPrepared(HotReloadIntroducedTypeArtifact artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            lock (gate)
            {
                // Dispose has already detached the handler, so an assembly registered afterwards
                // would never be resolved and the caller would silently get an unresolvable type.
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(HotReloadIntroducedTypeAssemblyResolver));
                }

                preparedAssemblies.Add(artifact.AssemblyFullName, artifact.Assembly);
            }

            return new PreparedAssemblyScope(gate, preparedAssemblies, artifact.AssemblyFullName);
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                preparedAssemblies.Clear();
            }

            // Detaching outside the gate keeps the handler subscription change off the lock a
            // resolve on another thread may already hold.
            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
        }

        internal Assembly ResolveExact(string requestedAssemblyFullName)
        {
            if (string.IsNullOrEmpty(requestedAssemblyFullName))
            {
                return null;
            }

            // This body must stay a pure lookup. It runs inside AssemblyResolve while the gate is
            // held, so loading an assembly or calling a Unity API here could re-enter the handler
            // on the same thread or block the main thread and deadlock the Editor.
            lock (gate)
            {
                if (preparedAssemblies.TryGetValue(requestedAssemblyFullName, out Assembly prepared))
                {
                    return prepared;
                }

                return registry.TryResolveActiveAssembly(requestedAssemblyFullName, out HotReloadIntroducedTypeArtifact active)
                    ? active.Assembly
                    : null;
            }
        }

        private Assembly Resolve(object sender, ResolveEventArgs arguments)
        {
            Interlocked.Increment(ref resolutionCount);
            return arguments == null ? null : ResolveExact(arguments.Name);
        }

        private sealed class PreparedAssemblyScope : IDisposable
        {
            private readonly object gate;
            private readonly Dictionary<string, Assembly> assemblies;
            private readonly string assemblyFullName;
            private bool removed;

            public PreparedAssemblyScope(
                object gate,
                Dictionary<string, Assembly> assemblies,
                string assemblyFullName)
            {
                this.gate = gate;
                this.assemblies = assemblies;
                this.assemblyFullName = assemblyFullName;
            }

            public void Dispose()
            {
                lock (gate)
                {
                    // A second Dispose must not remove an entry a later RegisterPrepared re-added
                    // under the same assembly identity.
                    if (removed)
                    {
                        return;
                    }

                    removed = true;
                    assemblies.Remove(assemblyFullName);
                }
            }
        }
    }
}
