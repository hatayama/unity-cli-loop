using System.Collections.Generic;

using UnityEditor;
using UnityEditor.Compilation;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The compilation assembly list for hot reload, asked of Unity once per domain.
    /// Why: on a project with several hundred assemblies one CompilationPipeline.GetAssemblies() call
    /// takes 340-620 ms, while the list only changes through an import or a compile. The startup
    /// snapshot capture is the first caller after a domain reload, so runs find the list already kept.
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
            // Why: an import does not always compile (Play Mode with Recompile After Finished Playing
            // postpones it), yet it can change the source files of an assembly.
            EditorApplication.projectChanged += OnProjectChanged;
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

        private static void OnProjectChanged()
        {
            Cache.Invalidate();
        }
    }
}
