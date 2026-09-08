using System;
using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Owns the single introduced type registry and assembly resolver pair of the current domain.
    /// A replacement scope disposes the resolver it takes over from and rebuilds one over the
    /// original registry when it closes.
    /// </summary>
    internal static class HotReloadIntroducedTypeHolder
    {
        private static HotReloadIntroducedTypeRegistry registry;
        private static HotReloadIntroducedTypeAssemblyResolver resolver;

        public static HotReloadIntroducedTypeRegistry Registry => RequireInitialized(registry);

        public static HotReloadIntroducedTypeAssemblyResolver Resolver => RequireInitialized(resolver);

        /// <summary>
        /// Builds the pair for this domain. Called once per domain reload from editor startup.
        /// </summary>
        public static void Initialize()
        {
            // Why not a lazy property: the resolver subscribes to AppDomain.AssemblyResolve in its
            // constructor, and a lazily built one would only subscribe on the first request that
            // already needs the subscription to succeed. Building it at startup makes the
            // subscription precede every bind that could reach an artifact assembly.
            resolver?.Dispose();
            registry = new HotReloadIntroducedTypeRegistry();
            resolver = new HotReloadIntroducedTypeAssemblyResolver(registry);
            HotReloadIntroducedTypeCoordination.DescribeActiveTypeNames = DescribeActiveTypeNames;
        }

        /// <summary>
        /// Reports the metadata names of the active introduced types for tools that cannot
        /// reference this assembly.
        /// </summary>
        /// <remarks>
        /// Why the field is read rather than the Registry property: a caller asking what this
        /// domain holds must get an answer even while a replacement scope is open, and the
        /// property throws when no registry is installed.
        /// </remarks>
        private static IReadOnlyList<string> DescribeActiveTypeNames()
        {
            HotReloadIntroducedTypeRegistry currentRegistry = registry;
            if (currentRegistry == null)
            {
                return Array.Empty<string>();
            }

            List<string> names = new List<string>();
            foreach (HotReloadIntroducedTypeDescriptor descriptor in currentRegistry.DescribeActive())
            {
                names.Add(descriptor.MetadataName);
            }

            return names;
        }

        /// <summary>
        /// Stops the current resolver from answering binds and returns a scope that restores a
        /// resolver over the original registry when it closes.
        /// </summary>
        /// <remarks>
        /// Why the current resolver is disposed rather than only detached from the fields: its
        /// AppDomain.AssemblyResolve subscription lives in the resolver itself, so leaving it
        /// alive would let two resolvers answer the same bind and leak the original registry's
        /// artifacts into whatever the caller installs.
        /// </remarks>
        public static IDisposable BeginReplacement()
        {
            HotReloadIntroducedTypeRegistry originalRegistry = registry;
            resolver?.Dispose();
            registry = null;
            resolver = null;
            return new ReplacementScope(originalRegistry);
        }

        private static T RequireInitialized<T>(T instance)
            where T : class
        {
            // A caller reaching the holder before startup wired it would silently work against a
            // pair no activation ever publishes to, so this is an internal contract violation.
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "The introduced type holder must be initialized before it is used.");
            }

            return instance;
        }

        private sealed class ReplacementScope : IDisposable
        {
            private readonly HotReloadIntroducedTypeRegistry originalRegistry;
            private bool restored;

            public ReplacementScope(HotReloadIntroducedTypeRegistry originalRegistry)
            {
                this.originalRegistry = originalRegistry;
            }

            public void Dispose()
            {
                if (restored)
                {
                    return;
                }

                restored = true;
                // The replacement resolver stays subscribed to AppDomain.AssemblyResolve until it
                // is disposed, so leaving it attached would keep answering binds after the scope.
                resolver?.Dispose();
                registry = originalRegistry;
                // Why a new resolver over the original registry: the original one was disposed
                // when the scope opened, and a disposed resolver no longer answers binds.
                resolver = originalRegistry == null
                    ? null
                    : new HotReloadIntroducedTypeAssemblyResolver(originalRegistry);
            }
        }
    }
}
