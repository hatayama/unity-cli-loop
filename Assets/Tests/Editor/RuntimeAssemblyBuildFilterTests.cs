using NUnit.Framework;
using UnityEditor;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Verifies that the Player build callback removes only the prefab-only runtime assembly.
    /// </summary>
    public sealed class RuntimeAssemblyBuildFilterTests
    {
        [Test]
        public void OnFilterAssemblies_WhenRuntimeAssemblyIsPresent_RemovesOnlyRuntimeAssembly()
        {
            RuntimeAssemblyBuildFilter filter = new RuntimeAssemblyBuildFilter();
            string[] assemblies =
            {
                "/build/Managed/Assembly-CSharp.dll",
                "/build/Managed/uLoopMCP.Runtime.dll",
                "/build/Managed/UnityEngine.CoreModule.dll"
            };

            string[] filteredAssemblies = filter.OnFilterAssemblies(BuildOptions.None, assemblies);

            Assert.That(filteredAssemblies, Is.EqualTo(new[]
            {
                "/build/Managed/Assembly-CSharp.dll",
                "/build/Managed/UnityEngine.CoreModule.dll"
            }));
        }

        [Test]
        public void OnFilterAssemblies_WhenTestAssembliesAreIncluded_KeepsRuntimeAssembly()
        {
            RuntimeAssemblyBuildFilter filter = new RuntimeAssemblyBuildFilter();
            string[] assemblies =
            {
                "/build/Managed/Assembly-CSharp.dll",
                "/build/Managed/uLoopMCP.Runtime.dll",
                "/build/Managed/uLoopMCP.Tests.PlayMode.dll"
            };

            string[] filteredAssemblies = filter.OnFilterAssemblies(BuildOptions.IncludeTestAssemblies, assemblies);

            Assert.That(filteredAssemblies, Is.EqualTo(assemblies));
        }
    }
}
