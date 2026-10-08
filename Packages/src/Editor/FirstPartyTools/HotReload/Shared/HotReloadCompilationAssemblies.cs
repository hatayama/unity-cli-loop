using System.Collections.Generic;

using UnityEditor.Compilation;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The compilation assembly list for hot reload, asked of Unity once per domain.
    /// Why: on a project with several hundred assemblies one CompilationPipeline.GetAssemblies() call
    /// takes 340-620 ms, while the list only changes through a compile, or through an import that
    /// changes the script set without compiling (see HotReloadCompilationAssemblyListPostprocessor).
    /// The startup snapshot capture is the first caller after a domain reload, so runs find the list
    /// already kept.
    /// </summary>
    internal static class HotReloadCompilationAssemblies
    {
        private static readonly HotReloadCompilationAssemblyCache Cache =
            new HotReloadCompilationAssemblyCache(CompilationPipeline.GetAssemblies);

        static HotReloadCompilationAssemblies()
        {
            // Why: a compile that fails keeps the domain alive, so without this the list would keep
            // the asmdefs and source files from before that compile until the next reload.
            CompilationPipeline.compilationStarted += OnCompilationStarted;
        }

        /// <summary>
        /// Returns the compilation assemblies. The list is shared; callers only read it.
        /// </summary>
        internal static IReadOnlyList<UnityCompilationAssembly> Current()
        {
            return Cache.Current();
        }

        /// <summary>
        /// Returns the first compilation assembly with this name, or null when none has it.
        /// </summary>
        internal static UnityCompilationAssembly FindByName(string assemblyName)
        {
            return Cache.FindByName(assemblyName);
        }

        private static void OnCompilationStarted(object context)
        {
            Cache.Invalidate();
        }

        /// <summary>
        /// Drops the kept list because an import changed the script set without a domain reload.
        /// </summary>
        internal static void DropForScriptSetChange()
        {
            Cache.Invalidate();
        }
    }
}
