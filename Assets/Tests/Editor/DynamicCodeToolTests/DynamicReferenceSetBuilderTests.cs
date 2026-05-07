using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Dynamic Reference Set Builder behavior.
    /// </summary>
    [TestFixture]
    public class DynamicReferenceSetBuilderTests
    {
        private string _tempDir;
        private DynamicReferenceSetBuilderService _referenceSetBuilder;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"MergeRefsTest_{System.Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _referenceSetBuilder = new DynamicReferenceSetBuilderService();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [Test]
        public void Should_KeepFirstOccurrence_When_SameAssemblyNameExistsInBothBaseAndAdditional()
        {
            string basePath = Path.Combine(_tempDir, "mono", "System.Threading.dll");
            string additionalPath = Path.Combine(_tempDir, "netstandard", "System.Threading.dll");
            CreateDummyFile(basePath);
            CreateDummyFile(additionalPath);

            string[] baseRefs = { basePath };
            List<string> additionalRefs = new() { additionalPath };

            string[] result = _referenceSetBuilder.MergeReferencesByAssemblyName(baseRefs, additionalRefs);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(basePath));
        }

        [Test]
        public void Should_IncludeBoth_When_DifferentAssemblyNames()
        {
            string threadingPath = Path.Combine(_tempDir, "System.Threading.dll");
            string debugPath = Path.Combine(_tempDir, "System.Diagnostics.Debug.dll");
            CreateDummyFile(threadingPath);
            CreateDummyFile(debugPath);

            string[] baseRefs = { threadingPath };
            List<string> additionalRefs = new() { debugPath };

            string[] result = _referenceSetBuilder.MergeReferencesByAssemblyName(baseRefs, additionalRefs);

            Assert.That(result, Has.Length.EqualTo(2));
            Assert.That(result, Does.Contain(threadingPath));
            Assert.That(result, Does.Contain(debugPath));
        }

        [Test]
        public void Should_DeduplicateBaseReferences_When_SameNameAppearsMultipleTimes()
        {
            string path1 = Path.Combine(_tempDir, "dir1", "MyAssembly.dll");
            string path2 = Path.Combine(_tempDir, "dir2", "MyAssembly.dll");
            CreateDummyFile(path1);
            CreateDummyFile(path2);

            string[] baseRefs = { path1, path2 };
            List<string> additionalRefs = new();

            string[] result = _referenceSetBuilder.MergeReferencesByAssemblyName(baseRefs, additionalRefs);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(path1));
        }

        [Test]
        public void Should_BeCaseInsensitive_When_ComparingAssemblyNames()
        {
            string lowerPath = Path.Combine(_tempDir, "system.threading.dll");
            string upperPath = Path.Combine(_tempDir, "alt", "System.Threading.dll");
            CreateDummyFile(lowerPath);
            CreateDummyFile(upperPath);

            string[] baseRefs = { lowerPath };
            List<string> additionalRefs = new() { upperPath };

            string[] result = _referenceSetBuilder.MergeReferencesByAssemblyName(baseRefs, additionalRefs);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(lowerPath));
        }

        [Test]
        public void Should_SkipAdditionalRef_When_FileDoesNotExist()
        {
            string basePath = Path.Combine(_tempDir, "Existing.dll");
            CreateDummyFile(basePath);
            string nonExistentPath = Path.Combine(_tempDir, "NonExistent.dll");

            string[] baseRefs = { basePath };
            List<string> additionalRefs = new() { nonExistentPath };

            string[] result = _referenceSetBuilder.MergeReferencesByAssemblyName(baseRefs, additionalRefs);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(basePath));
        }

        [Test]
        public void Should_SkipAdditionalRef_When_PathIsNullOrEmpty()
        {
            string basePath = Path.Combine(_tempDir, "Valid.dll");
            CreateDummyFile(basePath);

            string[] baseRefs = { basePath };
            List<string> additionalRefs = new() { null, "", "  " };

            string[] result = _referenceSetBuilder.MergeReferencesByAssemblyName(baseRefs, additionalRefs);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(basePath));
        }

        [Test]
        public void ResolvePreferredBaseReferencePath_WhenLegacyLayoutExists_ShouldReturnLegacyPath()
        {
            string editorContentsPath = CreateDirectory("Contents");
            string expectedPath = CreateDummyFile(Path.Combine(
                "Contents",
                "UnityReferenceAssemblies",
                "unity-4.8-api",
                "mscorlib.dll"));

            string preferredPath = DynamicReferenceSetBuilder.ResolvePreferredBaseReferencePath(
                editorContentsPath,
                "mscorlib");

            Assert.That(preferredPath, Is.EqualTo(expectedPath));
        }

        [Test]
        public void ResolvePreferredBaseReferencePath_WhenResourcesScriptingLayoutExists_ShouldReturnResourcesScriptingPath()
        {
            string editorContentsPath = CreateDirectory("Contents");
            string expectedPath = CreateDummyFile(Path.Combine(
                "Contents",
                "Resources",
                "Scripting",
                "Managed",
                "UnityEngine.dll"));

            string preferredPath = DynamicReferenceSetBuilder.ResolvePreferredBaseReferencePath(
                editorContentsPath,
                "UnityEngine");

            Assert.That(preferredPath, Is.EqualTo(expectedPath));
        }

        private string CreateDirectory(string relativePath)
        {
            string directoryPath = Path.Combine(_tempDir, relativePath);
            Directory.CreateDirectory(directoryPath);
            return directoryPath;
        }

        private string CreateDummyFile(string relativePath)
        {
            string path = Path.Combine(_tempDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new byte[] { 0 });
            return path;
        }
    }
}
