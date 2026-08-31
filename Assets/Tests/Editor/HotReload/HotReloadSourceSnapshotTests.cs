using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for portable-PDB document checksums used by hot-reload source snapshots.
    /// </summary>
    public class HotReloadSourceSnapshotTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadE2EFixtures.cs";
        private const string PredefinedEditorAssemblyName = "Assembly-CSharp-Editor";
        private const string PredefinedEditorFixtureProjectRelativePath =
            "Assets/RegressionHarness/AnnotatedScreenshotMismatch/Editor/AnnotatedScreenshotMismatchSceneBuilder.cs";

        /// <summary>
        /// What: the portable PDB next to a script assembly carries a per-document checksum that matches the hash of the source file bytes, which the snapshot baseline validation relies on.
        /// </summary>
        [Test]
        public void PortablePdb_DocumentChecksum_MatchesSourceFileBytes()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                TestAssemblyName + ".dll");
            string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            string fixtureAbsolutePath = Path.Combine(projectRoot, FixtureProjectRelativePath);

            Assert.That(File.Exists(dllPath), Is.True, "Test assembly DLL must exist at " + dllPath);
            Assert.That(File.Exists(pdbPath), Is.True, "Portable PDB must exist next to the test assembly DLL.");
            Assert.That(File.Exists(fixtureAbsolutePath), Is.True, "E2E fixture source must exist.");

            Document document = FindDocumentForProjectRelativePath(dllPath, pdbPath, FixtureProjectRelativePath);
            Assert.That(document, Is.Not.Null, "PDB must contain a document for " + FixtureProjectRelativePath);
            Assert.That(document.Hash, Is.Not.Null.And.Not.Empty, "Document.Hash must be non-empty for baseline validation.");

            byte[] sourceBytes = File.ReadAllBytes(fixtureAbsolutePath);
            byte[] expectedHash = ComputeDocumentHash(document.HashAlgorithm, sourceBytes);
            Assert.That(
                expectedHash.SequenceEqual(document.Hash),
                Is.True,
                "Document.Hash must equal the hash of the on-disk source bytes (algorithm="
                + document.HashAlgorithm + ").");
        }

        /// <summary>
        /// What: LoadVerifiedSnapshotSource returns the on-disk fixture text when a PDB-validated snapshot exists for the test assembly.
        /// </summary>
        [Test]
        public void LoadVerifiedSnapshotSource_ForCapturedFixture_ReturnsOnDiskText()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                TestAssemblyName + HotReloadConstants.CompiledAssemblyExtension);
            string fixtureAbsolutePath = Path.Combine(projectRoot, FixtureProjectRelativePath);

            string loaded = HotReloadSourceBaseline.LoadVerifiedSnapshotSource(
                FixtureProjectRelativePath,
                dllPath);
            Assert.That(loaded, Is.Not.Null, "Verified snapshot must resolve after domain-reload capture.");
            Assert.That(loaded, Is.EqualTo(File.ReadAllText(fixtureAbsolutePath)));
        }

        /// <summary>
        /// What: predefined editor assemblies receive compile snapshots so bare hot reload can detect their changed sources.
        /// </summary>
        [Test]
        public void LoadVerifiedSnapshotSource_ForPredefinedEditorAssembly_ReturnsOnDiskText()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                PredefinedEditorAssemblyName + HotReloadConstants.CompiledAssemblyExtension);
            string fixtureAbsolutePath = Path.Combine(
                projectRoot,
                PredefinedEditorFixtureProjectRelativePath);

            string loaded = HotReloadSourceBaseline.LoadVerifiedSnapshotSource(
                PredefinedEditorFixtureProjectRelativePath,
                dllPath);

            Assert.That(loaded, Is.Not.Null, "Predefined editor assembly snapshot must exist after compile.");
            Assert.That(loaded, Is.EqualTo(File.ReadAllText(fixtureAbsolutePath)));
        }

        /// <summary>
        /// What: a one-byte tamper of the snapshot bytes fails PDB checksum validation and yields null.
        /// </summary>
        [Test]
        public void LoadVerifiedSnapshotSource_WhenSnapshotBytesTampered_ReturnsNull()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                TestAssemblyName + HotReloadConstants.CompiledAssemblyExtension);

            string verified = HotReloadSourceBaseline.LoadVerifiedSnapshotSource(
                FixtureProjectRelativePath,
                dllPath);
            Assert.That(verified, Is.Not.Null, "Precondition: a verified snapshot must exist.");

            string mvid;
            using (Mono.Cecil.AssemblyDefinition assemblyDefinition =
                   Mono.Cecil.AssemblyDefinition.ReadAssembly(
                       dllPath,
                       new Mono.Cecil.ReaderParameters { InMemory = true }))
            {
                mvid = assemblyDefinition.MainModule.Mvid.ToString("N");
            }

            string slashNormalizedRelativePath = FixtureProjectRelativePath.Replace('\\', '/');
            string snapshotFileName =
                HotReloadSourceSnapshotter.HashProjectRelativePath(slashNormalizedRelativePath) + ".cs";
            string realSnapshotPath = Path.Combine(
                projectRoot,
                HotReloadConstants.SourceSnapshotRelativeDirectory,
                TestAssemblyName + "-" + mvid,
                snapshotFileName);
            Assert.That(File.Exists(realSnapshotPath), Is.True);

            string fakeRoot = Path.Combine(Path.GetTempPath(), "uloop-hot-reload-snapshot-tamper-" + Guid.NewGuid().ToString("N"));
            string fakeSnapshotDir = Path.Combine(
                fakeRoot,
                HotReloadConstants.SourceSnapshotRelativeDirectory,
                TestAssemblyName + "-" + mvid);
            Directory.CreateDirectory(fakeSnapshotDir);
            string fakeSnapshotPath = Path.Combine(fakeSnapshotDir, snapshotFileName);
            byte[] tampered = File.ReadAllBytes(realSnapshotPath);
            tampered[0] = (byte)(tampered[0] ^ 0xFF);
            File.WriteAllBytes(fakeSnapshotPath, tampered);

            try
            {
                string loaded = HotReloadSourceBaseline.LoadVerifiedSnapshotSourceAt(
                    fakeRoot,
                    FixtureProjectRelativePath,
                    dllPath);
                Assert.That(loaded, Is.Null);
            }
            finally
            {
                if (Directory.Exists(fakeRoot))
                {
                    Directory.Delete(fakeRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// What: path matching tolerates separator differences and absolute-vs-relative document URLs, and on Windows ignores case.
        /// </summary>
        [Test]
        public void PathsReferToSameFile_MatchesRelativeAbsoluteAndSeparatorVariants()
        {
            const string relativePath = "Assets/Tests/Editor/HotReload/HotReloadE2EFixtures.cs";

            Assert.That(
                HotReloadSourcePathNormalizer.PathsReferToSameFile(relativePath, relativePath),
                Is.True);
            Assert.That(
                HotReloadSourcePathNormalizer.PathsReferToSameFile(
                    relativePath.Replace('/', '\\'),
                    relativePath),
                Is.True);
            Assert.That(
                HotReloadSourcePathNormalizer.PathsReferToSameFile(
                    "/Users/example/project/" + relativePath,
                    relativePath),
                Is.True);
            Assert.That(
                HotReloadSourcePathNormalizer.PathsReferToSameFile(
                    "C:/proj/" + relativePath.Replace('/', '\\'),
                    relativePath),
                Is.True);
            Assert.That(
                HotReloadSourcePathNormalizer.PathsReferToSameFile(
                    "Assets/Other/File.cs",
                    relativePath),
                Is.False);

            if (Path.DirectorySeparatorChar == '\\')
            {
                Assert.That(
                    HotReloadSourcePathNormalizer.PathsReferToSameFile(
                        relativePath.ToUpperInvariant(),
                        relativePath),
                    Is.True);
            }
        }

        /// <summary>
        /// What: on Windows, snapshot filename hashing lowercases the project-relative path so case-only path differences resolve to the same baseline file.
        /// </summary>
        [Test]
        public void HashProjectRelativePath_OnWindows_IgnoresCase()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                Assert.Pass("Case folding applies only on Windows path separators.");
                return;
            }

            Assert.That(
                HotReloadSourceSnapshotter.HashProjectRelativePath("Assets/Foo.cs"),
                Is.EqualTo(HotReloadSourceSnapshotter.HashProjectRelativePath("assets/foo.cs")));
        }

        /// <summary>
        /// What: on Windows, an extended-length source created beyond legacy MAX_PATH is captured byte-exactly from its unprefixed project-relative path.
        /// </summary>
        [Test]
        public void CaptureAssemblySourcesAtomically_LongWindowsSource_CapturesByteExactSnapshot()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                Assert.Pass("Windows extended-length path handling applies only on Windows.");
                return;
            }

            string root = Path.Combine(
                Path.GetTempPath(),
                "uloop-hot-reload-long-path-" + Guid.NewGuid().ToString("N"));
            string sourceRelativePath = Path.Combine(
                "Sources",
                new string('a', 80),
                new string('b', 80),
                new string('c', 80),
                "LongPath.cs");
            string absoluteSourcePath = Path.Combine(root, sourceRelativePath);
            string fileSystemSourcePath = HotReloadFileSystemPath.GetFileSystemPath(absoluteSourcePath);
            string snapshotDirectory = Path.Combine(root, "snapshot");
            const string sourceText = "internal static class LongPathFixture { }\n";

            try
            {
                Assert.That(absoluteSourcePath.Length, Is.GreaterThan(260));
                Directory.CreateDirectory(Path.GetDirectoryName(fileSystemSourcePath));
                File.WriteAllText(
                    fileSystemSourcePath,
                    sourceText,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                HotReloadSourceSnapshotter.CaptureAssemblySourcesAtomically(
                    root,
                    snapshotDirectory,
                    new[] { sourceRelativePath },
                    "LongPathAssembly");

                string normalizedRelativePath = sourceRelativePath.Replace('\\', '/');
                string snapshotPath = Path.Combine(
                    snapshotDirectory,
                    HotReloadSourceSnapshotter.HashProjectRelativePath(normalizedRelativePath) + ".cs");
                Assert.That(File.Exists(snapshotPath), Is.True);
                Assert.That(
                    File.ReadAllBytes(snapshotPath).SequenceEqual(File.ReadAllBytes(fileSystemSourcePath)),
                    Is.True);
            }
            finally
            {
                string extendedRoot = @"\\?\" + Path.GetFullPath(root);
                if (Directory.Exists(extendedRoot))
                {
                    Directory.Delete(extendedRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// What: an unreadable source is skipped with one warning while readable siblings still complete the atomic snapshot.
        /// </summary>
        [Test]
        public void CaptureAssemblySourcesAtomically_OneLockedSource_CompletesWithReadableSnapshots()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                // FileShare.None enforcement is only guaranteed by the Windows IO stack; POSIX
                // Mono may allow the second open, which would leave the expected warning unmet.
                Assert.Pass("Sharing-violation-driven skip can only be provoked reliably on Windows.");
                return;
            }

            string root = Path.Combine(
                Path.GetTempPath(),
                "uloop-hot-reload-snapshot-skip-" + Guid.NewGuid().ToString("N"));
            string sourceDirectory = Path.Combine(root, "Sources");
            string snapshotDirectory = Path.Combine(root, "snapshot");
            string[] sourceRelativePaths =
            {
                "Sources/First.cs",
                "Sources/Locked.cs",
                "Sources/Last.cs"
            };
            UTF8Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Directory.CreateDirectory(sourceDirectory);
            foreach (string sourceRelativePath in sourceRelativePaths)
            {
                File.WriteAllText(
                    Path.Combine(root, sourceRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    "internal static class Fixture { }\n",
                    encoding);
            }

            try
            {
                string lockedSourcePath = Path.Combine(root, "Sources", "Locked.cs");
                using (new FileStream(
                           lockedSourcePath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.None))
                {
                    LogAssert.Expect(
                        LogType.Warning,
                        "[UnityCliLoop] Skipped 1 unreadable source(s) while snapshotting " +
                        "LockedSourceAssembly: Sources/Locked.cs");
                    HotReloadSourceSnapshotter.CaptureAssemblySourcesAtomically(
                        root,
                        snapshotDirectory,
                        sourceRelativePaths,
                        "LockedSourceAssembly");
                }

                Assert.That(Directory.Exists(snapshotDirectory), Is.True);
                Assert.That(
                    File.Exists(Path.Combine(
                        snapshotDirectory,
                        HotReloadSourceSnapshotter.HashProjectRelativePath("Sources/First.cs") + ".cs")),
                    Is.True);
                Assert.That(
                    File.Exists(Path.Combine(
                        snapshotDirectory,
                        HotReloadSourceSnapshotter.HashProjectRelativePath("Sources/Locked.cs") + ".cs")),
                    Is.False);
                Assert.That(
                    File.Exists(Path.Combine(
                        snapshotDirectory,
                        HotReloadSourceSnapshotter.HashProjectRelativePath("Sources/Last.cs") + ".cs")),
                    Is.True);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>
        /// What: stale MVID and orphaned temporary snapshot directories are pruned, while the current snapshot, hyphenated sibling assembly, and non-MVID suffix are kept.
        /// </summary>
        [Test]
        public void DeleteStaleSnapshotDirectories_StaleAndOrphanTemp_RemovesOnlyExactMvidSiblings()
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-hot-reload-snapshot-prune-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            string mvidA = Guid.NewGuid().ToString("N");
            string mvidB = Guid.NewGuid().ToString("N");
            string mvidC = Guid.NewGuid().ToString("N");
            string mvidD = Guid.NewGuid().ToString("N");
            string orphanTempDir = Path.Combine(tempRoot, "Foo-" + mvidA + ".tmp");
            string staleDir = Path.Combine(tempRoot, "Foo-" + mvidB);
            string currentDir = Path.Combine(tempRoot, "Foo-" + mvidC);
            string siblingAssemblyDir = Path.Combine(tempRoot, "Foo-Bar-" + mvidD);
            string nonMvidDir = Path.Combine(tempRoot, "Foo-notamvid");
            Directory.CreateDirectory(orphanTempDir);
            Directory.CreateDirectory(currentDir);
            Directory.CreateDirectory(staleDir);
            Directory.CreateDirectory(siblingAssemblyDir);
            Directory.CreateDirectory(nonMvidDir);

            try
            {
                HotReloadSourceSnapshotter.DeleteStaleSnapshotDirectories(tempRoot, "Foo", currentDir);

                Assert.That(Directory.Exists(orphanTempDir), Is.False);
                Assert.That(Directory.Exists(staleDir), Is.False);
                Assert.That(Directory.Exists(currentDir), Is.True);
                Assert.That(Directory.Exists(siblingAssemblyDir), Is.True);
                Assert.That(Directory.Exists(nonMvidDir), Is.True);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// What: stamp matching accepts only the exact mvid,mtime,length triple and rejects malformed or mismatched stamps.
        /// </summary>
        [Test]
        public void HasMatchingStamp_AcceptsExactTripleAndRejectsMismatchOrMalformed()
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-hot-reload-snapshot-stamp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            string stampPath = Path.Combine(tempRoot, "Foo.stamp");
            const long mtimeTicks = 123456789L;
            const long byteLength = 4096L;
            string mvid = Guid.NewGuid().ToString("N");

            try
            {
                File.WriteAllText(stampPath, mvid + "," + mtimeTicks + "," + byteLength);
                Assert.That(HotReloadSourceSnapshotter.HasMatchingStamp(stampPath, mtimeTicks, byteLength), Is.True);
                Assert.That(HotReloadSourceSnapshotter.HasMatchingStamp(stampPath, mtimeTicks + 1, byteLength), Is.False);
                Assert.That(HotReloadSourceSnapshotter.HasMatchingStamp(stampPath, mtimeTicks, byteLength + 1), Is.False);

                File.WriteAllText(stampPath, mvid + "," + mtimeTicks);
                Assert.That(HotReloadSourceSnapshotter.HasMatchingStamp(stampPath, mtimeTicks, byteLength), Is.False);

                File.WriteAllText(stampPath, "," + mtimeTicks + "," + byteLength);
                Assert.That(HotReloadSourceSnapshotter.HasMatchingStamp(stampPath, mtimeTicks, byteLength), Is.False);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// What: immutable registry package sources are skipped while Assets and embedded package sources are captured.
        /// </summary>
        [Test]
        public void ShouldSkipImmutablePackageSources_SkipsRegistryKeepsAssetsAndEmbedded()
        {
            Assert.That(
                HotReloadSourceSnapshotter.ShouldSkipImmutablePackageSources(
                    new[] { FixtureProjectRelativePath }),
                Is.False,
                "Assets scripts must be captured.");

            string embeddedSourcePath = FindSourceFileForAssembly("UnityCLILoop.FirstPartyTools.HotReload.Editor");
            Assert.That(
                HotReloadSourceSnapshotter.ShouldSkipImmutablePackageSources(new[] { embeddedSourcePath }),
                Is.False,
                "Embedded package scripts must be captured.");

            string registrySourcePath = FindSourceFileForAssembly("Unity.InputSystem");
            Assert.That(
                HotReloadSourceSnapshotter.ShouldSkipImmutablePackageSources(new[] { registrySourcePath }),
                Is.True,
                "Registry package scripts must be skipped.");
        }

        private static string FindSourceFileForAssembly(string assemblyName)
        {
            foreach (UnityCompilationAssembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name != assemblyName)
                {
                    continue;
                }

                Assert.That(assembly.sourceFiles, Is.Not.Null.And.Not.Empty);
                return assembly.sourceFiles[0];
            }

            Assert.Fail("CompilationPipeline assembly not found: " + assemblyName);
            return null;
        }

        private static Document FindDocumentForProjectRelativePath(
            string dllPath,
            string pdbPath,
            string projectRelativePath)
        {
            using FileStream dllStream = File.Open(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using FileStream pdbStream = File.Open(pdbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            ReaderParameters readerParameters = new ReaderParameters
            {
                InMemory = true,
                ReadSymbols = true,
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                SymbolStream = pdbStream
            };

            using AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(dllStream, readerParameters);
            HashSet<Document> seenDocuments = new HashSet<Document>();

            foreach (TypeDefinition type in assemblyDefinition.MainModule.GetTypes())
            {
                foreach (MethodDefinition method in type.Methods)
                {
                    if (!method.HasBody)
                    {
                        continue;
                    }

                    MethodDebugInformation debugInformation = method.DebugInformation;
                    if (debugInformation == null || !debugInformation.HasSequencePoints)
                    {
                        continue;
                    }

                    foreach (SequencePoint sequencePoint in debugInformation.SequencePoints)
                    {
                        if (sequencePoint.IsHidden || sequencePoint.Document == null)
                        {
                            continue;
                        }

                        Document document = sequencePoint.Document;
                        if (!seenDocuments.Add(document))
                        {
                            continue;
                        }

                        if (PathsReferToSameFile(document.Url, projectRelativePath))
                        {
                            return document;
                        }
                    }
                }
            }

            return null;
        }

        // Same semantics as SourcePausePointPathNormalizer.PathsReferToSameFile; kept local so this
        // gate test does not depend on HotReload production helpers that land in later commits.
        private static bool PathsReferToSameFile(string documentUrl, string projectRelativePath)
        {
            string normalizedDocumentUrl = documentUrl.Replace('\\', '/');
            string normalizedRelativePath = projectRelativePath.Replace('\\', '/');
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (string.Equals(normalizedDocumentUrl, normalizedRelativePath, comparison))
            {
                return true;
            }

            return normalizedDocumentUrl.EndsWith("/" + normalizedRelativePath, comparison);
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
