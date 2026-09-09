using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Whether the assembly a <see cref="HotReloadTypeHome"/> names is live in the AppDomain and
    /// still matches the compiled image on disk.
    /// </summary>
    internal enum HotReloadLoadedAssemblyState
    {
        Loaded,
        Stale,
        NotLoaded
    }

    /// <summary>
    /// The outcome of looking a <see cref="HotReloadTypeHome"/> up in the AppDomain: the live
    /// assembly when it matches the compiled Mvid, and the state that explains why it does not.
    /// Why no message: the diagnostic wording belongs to the callers (method matcher and the
    /// patch-target Mvid guard), so their responses stay byte-identical.
    /// </summary>
    internal readonly struct HotReloadLoadedAssemblyResolution
    {
        /// <summary>The live assembly; non-null only when <see cref="State"/> is Loaded.</summary>
        public Assembly Assembly { get; }

        public HotReloadLoadedAssemblyState State { get; }

        private HotReloadLoadedAssemblyResolution(Assembly assembly, HotReloadLoadedAssemblyState state)
        {
            Assembly = assembly;
            State = state;
        }

        public static HotReloadLoadedAssemblyResolution Loaded(Assembly assembly)
        {
            return new HotReloadLoadedAssemblyResolution(assembly, HotReloadLoadedAssemblyState.Loaded);
        }

        public static HotReloadLoadedAssemblyResolution Stale()
        {
            return new HotReloadLoadedAssemblyResolution(null, HotReloadLoadedAssemblyState.Stale);
        }

        public static HotReloadLoadedAssemblyResolution NotLoaded()
        {
            return new HotReloadLoadedAssemblyResolution(null, HotReloadLoadedAssemblyState.NotLoaded);
        }
    }
}
