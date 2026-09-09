using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves active and scoped prepared artifact assemblies by exact full identity only.
    /// </summary>
    /// <remarks>
    /// Constructed detached: it answers no bind until the composition root attaches it through
    /// <see cref="Resume"/>. Subscribing in the constructor would put a second resolver on
    /// AppDomain.AssemblyResolve for as long as it takes the caller to suspend the one already
    /// installed, and a bind arriving in that window could be answered from two domains.
    /// </remarks>
    internal sealed class HotReloadIntroducedTypeAssemblyResolver : IDisposable
    {
        private readonly HotReloadIntroducedTypeRegistry registry;
        private readonly object gate;
        private readonly Dictionary<string, Assembly> preparedAssemblies =
            new Dictionary<string, Assembly>(StringComparer.Ordinal);
        private int resolutionCount;
        private bool disposed;
        private bool suspended = true;

        internal int ResolutionCount => Volatile.Read(ref resolutionCount);

        public HotReloadIntroducedTypeAssemblyResolver(HotReloadIntroducedTypeRegistry registry)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            // The prepared map and the registry's mappings are read together inside one resolve, so
            // they share the registry's gate rather than taking two locks in an unspecified order.
            gate = registry.Gate;
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

        /// <summary>
        /// Detaches this resolver from AppDomain.AssemblyResolve without ending its life, so the
        /// resolver installed after it can be the only one answering binds.
        /// </summary>
        /// <remarks>
        /// Why not Dispose: the suspended resolver is put back when the replacement scope closes,
        /// and a disposed one can neither answer binds nor accept a prepared registration again.
        /// </remarks>
        public void Suspend()
        {
            bool detach;
            lock (gate)
            {
                RequireNotDisposed();
                // A second Suspend must not detach a handler this resolver no longer has attached.
                detach = !suspended;
                suspended = true;
            }

            if (!detach)
            {
                return;
            }

            // Detaching outside the gate keeps the handler subscription change off the lock a
            // resolve on another thread may already hold.
            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
        }

        /// <summary>
        /// Attaches this resolver to AppDomain.AssemblyResolve. A newly constructed resolver is
        /// detached, so this is what puts it in the bind path in the first place.
        /// </summary>
        public void Resume()
        {
            bool attach;
            lock (gate)
            {
                RequireNotDisposed();
                // A second Resume must not subscribe the same handler twice, which would make one
                // resolver answer a single bind two times.
                attach = suspended;
                suspended = false;
            }

            if (!attach)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        public void Dispose()
        {
            bool detach;
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                // A suspended resolver has already detached its handler, so detaching again would
                // remove a subscription that belongs to whichever resolver is installed now.
                detach = !suspended;
                preparedAssemblies.Clear();
            }

            if (!detach)
            {
                return;
            }

            // Detaching outside the gate keeps the handler subscription change off the lock a
            // resolve on another thread may already hold.
            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
        }

        // Suspending or resuming a resolver whose handler is already gone for good would leave the
        // caller believing this domain still answers binds.
        private void RequireNotDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(HotReloadIntroducedTypeAssemblyResolver));
            }
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
