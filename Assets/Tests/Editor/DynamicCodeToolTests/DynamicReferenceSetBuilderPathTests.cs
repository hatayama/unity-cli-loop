using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies how the dynamic reference set builder resolves preferred base reference paths and filters
    /// additional references, using temp directories in place of an Editor install.
    /// </summary>
    public sealed class DynamicReferenceSetBuilderPathTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"RefPathTest_{System.Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            // The loaded-assembly cache is rebuilt from the AppDomain, so dropping it only costs a rebuild.
            DynamicReferenceSetBuilder.InvalidateReferenceCacheForEditorStartup();
        }

        [TearDown]
        public void TearDown()
        {
            DynamicReferenceSetBuilder.InvalidateReferenceCacheForEditorStartup();
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        /// <summary>
        /// Verifies a scripting root is searched even when no Editor contents path is known.
        /// </summary>
        [Test]
        public void ResolvePreferredBaseReferencePath_WithOnlyAScriptingRoot_FindsTheReferenceThere()
        {
            string expectedPath = CreateFile("UnityReferenceAssemblies", "unity-4.8-api", "System.dll");

            string resolvedPath = DynamicReferenceSetBuilder.ResolvePreferredBaseReferencePath(string.Empty, _tempDir, "System");

            Assert.That(resolvedPath, Is.EqualTo(expectedPath));
        }

        /// <summary>
        /// Verifies nothing is resolved when neither root holds the reference.
        /// </summary>
        [Test]
        public void ResolvePreferredBaseReferencePath_WhenNoRootHoldsTheReference_ReturnsNull()
        {
            string resolvedPath = DynamicReferenceSetBuilder.ResolvePreferredBaseReferencePath(string.Empty, _tempDir, "System");

            Assert.That(resolvedPath, Is.Null);
        }

        /// <summary>
        /// Verifies an assembly name without a preferred location is not resolved even when a same-named file exists.
        /// </summary>
        [Test]
        public void ResolvePreferredBaseReferencePath_WithAnUnknownAssemblyName_ReturnsNull()
        {
            CreateFile("Managed", "Custom.dll");

            string resolvedPath = DynamicReferenceSetBuilder.ResolvePreferredBaseReferencePath(_tempDir, _tempDir, "Custom");

            Assert.That(resolvedPath, Is.Null);
        }

        /// <summary>
        /// Verifies the contents path's Resources/Scripting folder is searched after a scripting root that does not
        /// hold the reference, even when the scripting root is the contents path itself.
        /// </summary>
        [Test]
        public void ResolvePreferredBaseReferencePath_WhenOnlyResourcesScriptingHoldsTheReference_FindsItThere()
        {
            string expectedPath = CreateFile("Resources", "Scripting", "Managed", "UnityEngine", "UnityEngine.CoreModule.dll");

            string resolvedPath = DynamicReferenceSetBuilder.ResolvePreferredBaseReferencePath(
                _tempDir, _tempDir, "UnityEngine.CoreModule");

            Assert.That(resolvedPath, Is.EqualTo(expectedPath));
        }

        /// <summary>
        /// Verifies a reference set built without compiler paths falls back to loaded assemblies and keeps only
        /// additional references that exist.
        /// </summary>
        [Test]
        public void BuildReferenceSet_WithoutCompilerPaths_UsesLoadedAssembliesAndDropsMissingReferences()
        {
            string existingReference = CreateFile("Gap.Probe.Reference.dll");
            string missingReference = Path.Combine(_tempDir, "Gap.Missing.Reference.dll");

            List<string> references = DynamicReferenceSetBuilder.BuildReferenceSet(
                new List<string> { missingReference, existingReference },
                null,
                null);

            Assert.That(references, Does.Contain(existingReference));
            Assert.That(references, Does.Not.Contain(missingReference));
            Assert.That(references.Exists(path => Path.GetFileName(path) == "mscorlib.dll"), Is.True);
        }

        private string CreateFile(params string[] relativeParts)
        {
            string[] parts = new string[relativeParts.Length + 1];
            parts[0] = _tempDir;
            relativeParts.CopyTo(parts, 1);
            string path = Path.Combine(parts);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, string.Empty);
            return path;
        }
    }
}
