using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

using Mono.Cecil.Cil;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Contract of the per-assembly PDB document index: which documents it finds, and when it
    /// reads a dll and its PDB again. Tests that change a file work on copies in a private temp
    /// directory so that ScriptAssemblies is never touched.
    /// </summary>
    public class HotReloadPdbDocumentIndexTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadE2EFixtures.cs";
        private const string CoreFixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadCoreFixtures.cs";
        private const string PredefinedEditorAssemblyName = "Assembly-CSharp-Editor";
        private const string PredefinedEditorFixtureProjectRelativePath =
            "Assets/RegressionHarness/AnnotatedScreenshotMismatch/Editor/AnnotatedScreenshotMismatchSceneBuilder.cs";
        private const string BodylessFixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadSnapshotBodylessFixture.cs";

        private HotReloadPdbDocumentIndex _index;

        [SetUp]
        public void SetUp()
        {
            _index = new HotReloadPdbDocumentIndex();
        }

        /// <summary>
        /// What: a second source file of an assembly already read is found without reading the dll and the PDB again.
        /// </summary>
        [Test]
        public void TryFindDocument_TwoFilesOfOneAssembly_ReadsThePdbOnce()
        {
            string dllPath = DllPath(TestAssemblyName);

            bool foundFirst = Find(_index, dllPath, FixtureProjectRelativePath, out HotReloadPdbDocument _);
            bool foundSecond = Find(_index, dllPath, CoreFixtureProjectRelativePath, out HotReloadPdbDocument _);

            Assert.That(foundFirst, Is.True);
            Assert.That(foundSecond, Is.True);
            Assert.That(_index.LoadCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: the document found for a file with method bodies carries the checksum of that file's bytes on disk.
        /// </summary>
        [Test]
        public void TryFindDocument_ForASourceFileWithMethodBodies_ReturnsTheChecksumOfItsBytes()
        {
            bool found = Find(_index, DllPath(TestAssemblyName), FixtureProjectRelativePath, out HotReloadPdbDocument document);

            Assert.That(found, Is.True);
            Assert.That(document.Hash, Is.Not.Null.And.Not.Empty);
            byte[] sourceBytes = File.ReadAllBytes(Path.Combine(ProjectRoot(), FixtureProjectRelativePath));
            Assert.That(
                ComputeDocumentHash(document.HashAlgorithm, sourceBytes).SequenceEqual(document.Hash),
                Is.True,
                "The checksum must equal the hash of the on-disk source bytes (algorithm=" + document.HashAlgorithm + ").");
        }

        /// <summary>
        /// What: a file without a method body is not found, although the PDB's document table lists it.
        /// </summary>
        [Test]
        public void TryFindDocument_ForAFileWithoutMethodBodies_ReturnsFalse()
        {
            bool found = Find(_index, DllPath(TestAssemblyName), BodylessFixtureProjectRelativePath, out HotReloadPdbDocument _);

            Assert.That(found, Is.False);
        }

        /// <summary>
        /// What: looking up the same file twice reads the dll and the PDB once.
        /// </summary>
        [Test]
        public void TryFindDocument_SameFileTwice_ReadsThePdbOnce()
        {
            string dllPath = DllPath(TestAssemblyName);

            bool foundFirst = Find(_index, dllPath, FixtureProjectRelativePath, out HotReloadPdbDocument _);
            bool foundSecond = Find(_index, dllPath, FixtureProjectRelativePath, out HotReloadPdbDocument _);

            Assert.That(foundFirst, Is.True);
            Assert.That(foundSecond, Is.True);
            Assert.That(_index.LoadCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a newer write time on the dll makes the next lookup read the files again.
        /// </summary>
        [Test]
        public void TryFindDocument_DllWriteTimeChanged_ReadsAgain()
        {
            string dllPath = CopyAssemblyToTemp(TestAssemblyName);
            try
            {
                bool foundBefore = Find(_index, dllPath, FixtureProjectRelativePath, out HotReloadPdbDocument _);
                File.SetLastWriteTimeUtc(dllPath, File.GetLastWriteTimeUtc(dllPath).AddSeconds(2));
                bool foundAfter = Find(_index, dllPath, FixtureProjectRelativePath, out HotReloadPdbDocument _);

                Assert.That(foundBefore, Is.True);
                Assert.That(foundAfter, Is.True);
                Assert.That(_index.LoadCount, Is.EqualTo(2));
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(dllPath), recursive: true);
            }
        }

        /// <summary>
        /// What: a newer write time on the PDB alone makes the next lookup read the files again.
        /// </summary>
        [Test]
        public void TryFindDocument_PdbWriteTimeChanged_ReadsAgain()
        {
            string dllPath = CopyAssemblyToTemp(TestAssemblyName);
            try
            {
                string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
                bool foundBefore = Find(_index, dllPath, FixtureProjectRelativePath, out HotReloadPdbDocument _);
                File.SetLastWriteTimeUtc(pdbPath, File.GetLastWriteTimeUtc(pdbPath).AddSeconds(2));
                bool foundAfter = Find(_index, dllPath, FixtureProjectRelativePath, out HotReloadPdbDocument _);

                Assert.That(foundBefore, Is.True);
                Assert.That(foundAfter, Is.True);
                Assert.That(_index.LoadCount, Is.EqualTo(2));
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(dllPath), recursive: true);
            }
        }

        /// <summary>
        /// What: lookups that alternate between two assemblies keep a list for each, so each pair of files is read once.
        /// </summary>
        [Test]
        public void TryFindDocument_TwoAssemblies_KeepsBothLists()
        {
            string testDllPath = DllPath(TestAssemblyName);
            string editorDllPath = DllPath(PredefinedEditorAssemblyName);

            bool foundTestFirst = Find(_index, testDllPath, FixtureProjectRelativePath, out HotReloadPdbDocument _);
            bool foundEditorFirst = Find(_index, editorDllPath, PredefinedEditorFixtureProjectRelativePath, out HotReloadPdbDocument _);
            bool foundTestSecond = Find(_index, testDllPath, FixtureProjectRelativePath, out HotReloadPdbDocument _);
            bool foundEditorSecond = Find(_index, editorDllPath, PredefinedEditorFixtureProjectRelativePath, out HotReloadPdbDocument _);

            Assert.That(foundTestFirst, Is.True);
            Assert.That(foundEditorFirst, Is.True);
            Assert.That(foundTestSecond, Is.True);
            Assert.That(foundEditorSecond, Is.True);
            Assert.That(_index.LoadCount, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a different module version id for the same, unchanged files makes the lookup read them again.
        /// </summary>
        [Test]
        public void TryFindDocument_DifferentModuleVersionIdForTheSameFiles_ReadsAgain()
        {
            string dllPath = CopyAssemblyToTemp(TestAssemblyName);
            try
            {
                string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
                bool foundBefore = Find(_index, dllPath, FixtureProjectRelativePath, out HotReloadPdbDocument _);
                bool foundAfter = _index.TryFindDocument(
                    dllPath,
                    pdbPath,
                    "00000000000000000000000000000000",
                    FixtureProjectRelativePath,
                    out HotReloadPdbDocument _);

                Assert.That(foundBefore, Is.True);
                Assert.That(foundAfter, Is.True);
                Assert.That(_index.LoadCount, Is.EqualTo(2));
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(dllPath), recursive: true);
            }
        }

        /// <summary>
        /// What: once the PDB can no longer be read, every lookup throws instead of answering from the
        /// list read from the earlier files, and no list is stored for the unreadable files.
        /// </summary>
        [Test]
        public void TryFindDocument_PdbUnreadableAfterAListWasKept_ThrowsAndKeepsNoStaleList()
        {
            string dllPath = CopyAssemblyToTemp(TestAssemblyName);
            try
            {
                string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
                bool foundBefore = Find(_index, dllPath, FixtureProjectRelativePath, out HotReloadPdbDocument _);
                // Why a shorter file: the length alone makes the files differ from the ones the list
                // was read from, even if the new write time lands on the same tick as the old one.
                File.WriteAllBytes(pdbPath, new byte[] { 0x6E, 0x6F, 0x74, 0x20, 0x61, 0x20, 0x70, 0x64, 0x62 });
                TestDelegate findAgain = () => Find(_index, dllPath, FixtureProjectRelativePath, out HotReloadPdbDocument _);

                Assert.That(foundBefore, Is.True);
                Assert.That(findAgain, Throws.Exception, "The list read before the PDB changed must not answer.");
                Assert.That(findAgain, Throws.Exception, "No list may be stored for a PDB that could not be read.");
                Assert.That(_index.LoadCount, Is.EqualTo(1));
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(dllPath), recursive: true);
            }
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string DllPath(string assemblyName)
        {
            return Path.Combine(
                ProjectRoot(),
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + HotReloadConstants.CompiledAssemblyExtension);
        }

        // Looks the file up the way the snapshot loader does: the PDB next to the dll, and the
        // MVID read from the dll.
        private static bool Find(
            HotReloadPdbDocumentIndex index,
            string dllPath,
            string projectRelativePath,
            out HotReloadPdbDocument document)
        {
            string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            string moduleVersionId = HotReloadSourceSnapshotter.ReadAssemblyMvid(dllPath);
            return index.TryFindDocument(dllPath, pdbPath, moduleVersionId, projectRelativePath, out document);
        }

        // Copies the assembly's dll and PDB into a new temp directory and returns the dll's path.
        // The caller deletes the directory.
        private static string CopyAssemblyToTemp(string assemblyName)
        {
            string sourceDllPath = DllPath(assemblyName);
            string sourcePdbPath = Path.ChangeExtension(sourceDllPath, ".pdb");
            Assert.That(File.Exists(sourceDllPath), Is.True, "Assembly dll missing: " + sourceDllPath);
            Assert.That(File.Exists(sourcePdbPath), Is.True, "Assembly PDB missing: " + sourcePdbPath);

            string directory = Path.Combine(
                Path.GetTempPath(),
                "uloop-pdb-document-index-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string dllPath = Path.Combine(directory, Path.GetFileName(sourceDllPath));
            File.Copy(sourceDllPath, dllPath);
            File.Copy(sourcePdbPath, Path.ChangeExtension(dllPath, ".pdb"));
            return dllPath;
        }

        private static byte[] ComputeDocumentHash(DocumentHashAlgorithm algorithm, byte[] sourceBytes)
        {
            switch (algorithm)
            {
                case DocumentHashAlgorithm.SHA1:
                    using (SHA1 sha1 = SHA1.Create())
                    {
                        return sha1.ComputeHash(sourceBytes);
                    }
                case DocumentHashAlgorithm.SHA256:
                    using (SHA256 sha256 = SHA256.Create())
                    {
                        return sha256.ComputeHash(sourceBytes);
                    }
                default:
                    Assert.Fail("Unsupported Document.HashAlgorithm: " + algorithm);
                    return null;
            }
        }
    }
}
