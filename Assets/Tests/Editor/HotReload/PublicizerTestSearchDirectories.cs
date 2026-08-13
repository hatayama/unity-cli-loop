using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Builds the same Cecil resolver search-directory set production uses for this test
    /// assembly: directories of existing <see cref="UnityCompilationAssembly.allReferences"/>.
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

            HashSet<string> directories = new HashSet<string>(StringComparer.Ordinal);
            if (compilationAssembly.allReferences == null)
            {
                return directories;
            }

            foreach (string reference in compilationAssembly.allReferences)
            {
                if (string.IsNullOrEmpty(reference) || !File.Exists(reference))
                {
                    continue;
                }

                string directory = Path.GetDirectoryName(Path.GetFullPath(reference));
                if (!string.IsNullOrEmpty(directory))
                {
                    directories.Add(directory);
                }
            }

            return directories;
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
