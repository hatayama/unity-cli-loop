using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies that the lightweight walker discovers .asmdef/.asmref directory structure without
    /// touching non-asmdef/asmref files, matching what the full project scan's
    /// ThirdPartyToolMigrationAssemblyReferenceResolver expects as input.
    /// </summary>
    public sealed class ThirdPartyToolMigrationLightweightAssemblyWalkerTests
    {
        [Test]
        public void DiscoverAssemblyStructure_WithNestedAsmdef_ReturnsItsDirectory()
        {
            // Verifies a single nested .asmdef file's directory is discovered.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "Editor", "Tool");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "Tool.asmdef"),
                    "{\"name\": \"Tool\"}");

                (List<string> asmdefDirectories, _) =
                    ThirdPartyToolMigrationLightweightAssemblyWalker.DiscoverAssemblyStructure(projectRoot);

                Assert.That(asmdefDirectories, Does.Contain(toolDirectory));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void DiscoverAssemblyStructure_WithAsmrefPointingAtAsmdef_ReturnsAssemblyReferenceDirectory()
        {
            // Verifies an .asmref file referencing an .asmdef by name is resolved into an
            // AssemblyReferenceDirectory pointing at the asmdef's directory.
            string projectRoot = CreateProjectRoot();
            try
            {
                string asmdefDirectory = Path.Combine(projectRoot, "Assets", "Editor", "Tool");
                string asmrefDirectory = Path.Combine(projectRoot, "Assets", "Editor", "Tool", "SubModule");
                Directory.CreateDirectory(asmdefDirectory);
                Directory.CreateDirectory(asmrefDirectory);
                File.WriteAllText(
                    Path.Combine(asmdefDirectory, "Tool.asmdef"),
                    "{\"name\": \"Tool\"}");
                File.WriteAllText(
                    Path.Combine(asmrefDirectory, "SubModule.asmref"),
                    "{\"reference\": \"Tool\"}");

                (_, List<AssemblyReferenceDirectory> assemblyReferenceDirectories) =
                    ThirdPartyToolMigrationLightweightAssemblyWalker.DiscoverAssemblyStructure(projectRoot);

                Assert.That(assemblyReferenceDirectories.Count, Is.EqualTo(1));
                Assert.That(assemblyReferenceDirectories[0].SourceDirectory, Is.EqualTo(asmrefDirectory));
                Assert.That(assemblyReferenceDirectories[0].TargetAssemblyDirectory, Is.EqualTo(asmdefDirectory));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void DiscoverAssemblyStructure_WithNoAssetsDirectory_ReturnsEmptyResults()
        {
            // Verifies a project root with no Assets directory yet does not throw and returns empty
            // results instead.
            string projectRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityCliLoopTests",
                "ThirdPartyToolMigrationLightweightAssemblyWalker",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(projectRoot);
            try
            {
                (List<string> asmdefDirectories, List<AssemblyReferenceDirectory> assemblyReferenceDirectories) =
                    ThirdPartyToolMigrationLightweightAssemblyWalker.DiscoverAssemblyStructure(projectRoot);

                Assert.That(asmdefDirectories.Count, Is.EqualTo(0));
                Assert.That(assemblyReferenceDirectories.Count, Is.EqualTo(0));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        private static string CreateProjectRoot()
        {
            string projectRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityCliLoopTests",
                "ThirdPartyToolMigrationLightweightAssemblyWalker",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            return projectRoot;
        }
    }
}
