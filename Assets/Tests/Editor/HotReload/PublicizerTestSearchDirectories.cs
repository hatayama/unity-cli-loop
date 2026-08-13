using System.Collections.Generic;

using NUnit.Framework;

using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Resolves Cecil resolver search directories for this test assembly via the production
    /// <see cref="ReferencePublicizer.CollectResolverSearchDirectories"/> helper.
    /// </summary>
    internal static class PublicizerTestSearchDirectories
    {
        private const string HotReloadTestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        public static IReadOnlyCollection<string> ForHotReloadTestAssembly()
        {
            UnityCompilationAssembly compilationAssembly = FindHotReloadTestAssembly();
            Assert.That(
                compilationAssembly,
                Is.Not.Null,
                "CompilationPipeline assembly not found: " + HotReloadTestAssemblyName);

            return ReferencePublicizer.CollectResolverSearchDirectories(compilationAssembly.allReferences);
        }

        private static UnityCompilationAssembly FindHotReloadTestAssembly()
        {
            UnityCompilationAssembly[] assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            foreach (UnityCompilationAssembly assembly in assemblies)
            {
                if (assembly.name == HotReloadTestAssemblyName)
                {
                    return assembly;
                }
            }

            return null;
        }
    }
}
