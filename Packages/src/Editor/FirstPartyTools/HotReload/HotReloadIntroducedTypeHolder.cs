using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Owns the single introduced type registry and assembly resolver pair of the current domain.
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
        }

        /// <summary>
        /// Detaches the current pair and returns a scope that disposes whatever replaced it and
        /// restores the original one.
        /// </summary>
        public static IDisposable BeginReplacement()
        {
            HotReloadIntroducedTypeRegistry originalRegistry = registry;
            HotReloadIntroducedTypeAssemblyResolver originalResolver = resolver;
            registry = null;
            resolver = null;
            return new ReplacementScope(originalRegistry, originalResolver);
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
            private readonly HotReloadIntroducedTypeAssemblyResolver originalResolver;
            private bool restored;

            public ReplacementScope(
                HotReloadIntroducedTypeRegistry originalRegistry,
                HotReloadIntroducedTypeAssemblyResolver originalResolver)
            {
                this.originalRegistry = originalRegistry;
                this.originalResolver = originalResolver;
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
                if (resolver != null && !ReferenceEquals(resolver, originalResolver))
                {
                    resolver.Dispose();
                }

                registry = originalRegistry;
                resolver = originalResolver;
            }
        }
    }
}
