using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the per-assembly decisions of snapshot capture and changed-file aggregation, run
    /// against compilation assemblies planted in a per-test temporary project root.
    /// </summary>
    public sealed class HotReloadSnapshotAssemblyEnumerationTests
    {
        private const string SourceRelativePath = "Assets/Fixture.cs";
        private static readonly DateTime CompileStartUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private string _projectRoot;
        private string _snapshotRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            _snapshotRoot = HotReloadSourceSnapshotLayout.Root(_projectRoot);
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Assets"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, HotReloadConstants.ScriptAssembliesRelativeDirectory));
            Directory.CreateDirectory(_snapshotRoot);
            string fixturePath = Path.Combine(_projectRoot, "Assets", "Fixture.cs");
            File.WriteAllText(fixturePath, "class Fixture {}\n");
            // A source the compile read is older than the compiled assembly; keeping it so leaves it
            // out of the capture's PDB check.
            File.SetLastWriteTimeUtc(fixturePath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, true);
            }
        }

        /// <summary>
        /// Verifies a first capture copies the sources under the MVID-named directory, removes an older MVID's
        /// snapshot of the same assembly, and writes the stamp.
        /// </summary>
        [Test]
        public void CaptureAssemblies_WithoutStamp_CapturesSourcesRemovesStaleSnapshotAndWritesStamp()
        {
            string dllPath = PlantCompiledAssembly("Fixture");
            string mvid = HotReloadAssemblyMvid.Read(dllPath);
            string staleDirectory = Path.Combine(_snapshotRoot, "Fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staleDirectory);

            HotReloadSourceSnapshotter.CaptureAssemblies(
                _projectRoot,
                new[] { CreateAssembly("Fixture") },
                HotReloadCompileStart.Unknown,
                CreateDocumentIndex());

            Assert.That(File.Exists(SnapshotSourcePath("Fixture", mvid)), Is.True);
            Assert.That(Directory.Exists(staleDirectory), Is.False);
            Assert.That(File.ReadAllText(StampPath("Fixture")), Is.EqualTo(ExpectedStamp(dllPath, mvid)));
        }

        /// <summary>
        /// Verifies a stamp matching the compiled assembly's mtime and length skips the capture entirely.
        /// </summary>
        [Test]
        public void CaptureAssemblies_WhenStampMatches_SkipsCapture()
        {
            string dllPath = PlantCompiledAssembly("Fixture");
            string mvid = HotReloadAssemblyMvid.Read(dllPath);
            string stamp = ExpectedStamp(dllPath, mvid);
            File.WriteAllText(StampPath("Fixture"), stamp);

            HotReloadSourceSnapshotter.CaptureAssemblies(
                _projectRoot,
                new[] { CreateAssembly("Fixture") },
                HotReloadCompileStart.Unknown,
                CreateDocumentIndex());

            Assert.That(Directory.Exists(Path.Combine(_snapshotRoot, "Fixture-" + mvid)), Is.False);
            Assert.That(File.ReadAllText(StampPath("Fixture")), Is.EqualTo(stamp));
        }

        /// <summary>
        /// Verifies an existing directory for the current MVID is kept as is, older snapshots are left alone, and
        /// only the stamp is rewritten.
        /// </summary>
        [Test]
        public void CaptureAssemblies_WhenMvidDirectoryExists_KeepsSnapshotsAndWritesStamp()
        {
            string dllPath = PlantCompiledAssembly("Fixture");
            string mvid = HotReloadAssemblyMvid.Read(dllPath);
            string currentDirectory = Path.Combine(_snapshotRoot, "Fixture-" + mvid);
            Directory.CreateDirectory(currentDirectory);
            string staleDirectory = Path.Combine(_snapshotRoot, "Fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staleDirectory);

            HotReloadSourceSnapshotter.CaptureAssemblies(
                _projectRoot,
                new[] { CreateAssembly("Fixture") },
                HotReloadCompileStart.Unknown,
                CreateDocumentIndex());

            Assert.That(Directory.GetFiles(currentDirectory), Is.Empty);
            Assert.That(Directory.Exists(staleDirectory), Is.True);
            Assert.That(File.ReadAllText(StampPath("Fixture")), Is.EqualTo(ExpectedStamp(dllPath, mvid)));
        }

        /// <summary>
        /// Verifies a stamp that is malformed, or records another mtime or length (older or newer than
        /// the DLL's), does not short-circuit the capture: the sources are captured again and the stamp
        /// is rewritten.
        /// </summary>
        [TestCase("{mvid},{mtime}")]
        [TestCase(",{mtime},{length}")]
        [TestCase("{mvid},not-a-number,{length}")]
        [TestCase("{mvid},{mtime},not-a-number")]
        [TestCase("{mvid},{mtime+1},{length}")]
        [TestCase("{mvid},{mtime},{length+1}")]
        [TestCase("{mvid},{mtime-1},{length}")]
        [TestCase("{mvid},{mtime},{length-1}")]
        public void CaptureAssemblies_WhenStampIsMalformedOrDiffers_CapturesAgain(string stampTemplate)
        {
            string dllPath = PlantCompiledAssembly("Fixture");
            string mvid = HotReloadAssemblyMvid.Read(dllPath);
            FileInfo dllInfo = new FileInfo(dllPath);
            long mtime = dllInfo.LastWriteTimeUtc.Ticks;
            long length = dllInfo.Length;
            // Why the "+1" / "-1" placeholders go first: replacing "{mtime}" first would break "{mtime+1}" apart.
            string stamp = stampTemplate
                .Replace("{mtime+1}", (mtime + 1).ToString(CultureInfo.InvariantCulture))
                .Replace("{length+1}", (length + 1).ToString(CultureInfo.InvariantCulture))
                .Replace("{mtime-1}", (mtime - 1).ToString(CultureInfo.InvariantCulture))
                .Replace("{length-1}", (length - 1).ToString(CultureInfo.InvariantCulture))
                .Replace("{mvid}", mvid)
                .Replace("{mtime}", mtime.ToString(CultureInfo.InvariantCulture))
                .Replace("{length}", length.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(StampPath("Fixture"), stamp);

            HotReloadSourceSnapshotter.CaptureAssemblies(
                _projectRoot,
                new[] { CreateAssembly("Fixture") },
                HotReloadCompileStart.Unknown,
                CreateDocumentIndex());

            Assert.That(Directory.Exists(HotReloadSourceSnapshotLayout.AssemblyDirectory(_projectRoot, "Fixture", mvid)), Is.True);
            Assert.That(File.ReadAllText(StampPath("Fixture")), Is.EqualTo(ExpectedStamp(dllPath, mvid)));
        }

        /// <summary>
        /// Verifies a capture removes only this assembly's older MVID directories and leftover temporary
        /// directories, and leaves a hyphenated sibling assembly's directory and a non-MVID directory alone.
        /// </summary>
        [Test]
        public void CaptureAssemblies_RemovesOnlyThisAssemblysStaleMvidDirectories()
        {
            string dllPath = PlantCompiledAssembly("Fixture");
            string mvid = HotReloadAssemblyMvid.Read(dllPath);
            string orphanTemporary =
                HotReloadSourceSnapshotLayout.AssemblyDirectory(_projectRoot, "Fixture", Guid.NewGuid().ToString("N"))
                + HotReloadSourceSnapshotLayout.IncompleteDirectorySuffix;
            string stale = HotReloadSourceSnapshotLayout.AssemblyDirectory(_projectRoot, "Fixture", Guid.NewGuid().ToString("N"));
            string sibling = HotReloadSourceSnapshotLayout.AssemblyDirectory(_projectRoot, "Fixture-Bar", Guid.NewGuid().ToString("N"));
            string nonMvid = Path.Combine(_snapshotRoot, "Fixture-notamvid");
            Directory.CreateDirectory(orphanTemporary);
            Directory.CreateDirectory(stale);
            Directory.CreateDirectory(sibling);
            Directory.CreateDirectory(nonMvid);

            HotReloadSourceSnapshotter.CaptureAssemblies(
                _projectRoot,
                new[] { CreateAssembly("Fixture") },
                HotReloadCompileStart.Unknown,
                CreateDocumentIndex());

            Assert.That(Directory.Exists(orphanTemporary), Is.False);
            Assert.That(Directory.Exists(stale), Is.False);
            Assert.That(Directory.Exists(sibling), Is.True);
            Assert.That(Directory.Exists(nonMvid), Is.True);
            Assert.That(Directory.Exists(HotReloadSourceSnapshotLayout.AssemblyDirectory(_projectRoot, "Fixture", mvid)), Is.True);
        }

        /// <summary>
        /// Verifies an assembly without sources is skipped before anything is written: no snapshot directory and no stamp.
        /// </summary>
        [Test]
        public void CaptureAssemblies_WhenAssemblyHasNoSources_WritesNothing()
        {
            string dllPath = PlantCompiledAssembly("NoSources");
            string mvid = HotReloadAssemblyMvid.Read(dllPath);

            HotReloadSourceSnapshotter.CaptureAssemblies(
                _projectRoot,
                new[] { CreateAssemblyWithSources("NoSources", Array.Empty<string>()) },
                HotReloadCompileStart.Unknown,
                CreateDocumentIndex());

            Assert.That(Directory.Exists(HotReloadSourceSnapshotLayout.AssemblyDirectory(_projectRoot, "NoSources", mvid)), Is.False);
            Assert.That(File.Exists(StampPath("NoSources")), Is.False);
        }

        /// <summary>
        /// Verifies an assembly whose compiled DLL or PDB is missing is skipped without a warning: no snapshot directory and no stamp.
        /// </summary>
        [TestCase(".dll")]
        [TestCase(".pdb")]
        public void CaptureAssemblies_WhenCompiledOutputIsMissing_WritesNothingAndDoesNotWarn(string missingExtension)
        {
            string dllPath = PlantCompiledAssembly("Fixture");
            File.Delete(Path.ChangeExtension(dllPath, missingExtension));

            HotReloadSourceSnapshotter.CaptureAssemblies(
                _projectRoot,
                new[] { CreateAssembly("Fixture") },
                HotReloadCompileStart.Unknown,
                CreateDocumentIndex());

            Assert.That(Directory.GetDirectories(_snapshotRoot, "Fixture-*"), Is.Empty);
            Assert.That(File.Exists(StampPath("Fixture")), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// Verifies a matching stamp returns before the compiled assembly is opened: a DLL whose bytes are no
        /// longer an assembly, behind a stamp that matches its current mtime and length, is neither read
        /// (no warning) nor captured.
        /// </summary>
        [Test]
        public void CaptureAssemblies_WhenStampMatches_DoesNotOpenTheCompiledAssembly()
        {
            string dllPath = PlantCompiledAssembly("Fixture");
            string mvid = HotReloadAssemblyMvid.Read(dllPath);
            File.WriteAllBytes(dllPath, new byte[16]);
            // Why the stamp is taken after the overwrite: it must match the bytes now on disk.
            string stamp = ExpectedStamp(dllPath, mvid);
            File.WriteAllText(StampPath("Fixture"), stamp);

            HotReloadSourceSnapshotter.CaptureAssemblies(
                _projectRoot,
                new[] { CreateAssembly("Fixture") },
                HotReloadCompileStart.Unknown,
                CreateDocumentIndex());

            Assert.That(Directory.Exists(HotReloadSourceSnapshotLayout.AssemblyDirectory(_projectRoot, "Fixture", mvid)), Is.False);
            Assert.That(File.ReadAllText(StampPath("Fixture")), Is.EqualTo(stamp));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// Verifies an assembly whose compiled image cannot be read is reported as a warning and does not stop
        /// the capture of the assemblies after it.
        /// </summary>
        [Test]
        public void CaptureAssemblies_WhenOneAssemblyCannotBeRead_WarnsAndCapturesTheRest()
        {
            string brokenDllPath = Path.Combine(
                _projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                "Broken" + HotReloadConstants.CompiledAssemblyExtension);
            File.WriteAllText(brokenDllPath, "not an assembly");
            File.WriteAllText(Path.ChangeExtension(brokenDllPath, ".pdb"), "not a pdb");
            string dllPath = PlantCompiledAssembly("Fixture");
            string mvid = HotReloadAssemblyMvid.Read(dllPath);
            LogAssert.Expect(LogType.Warning, new Regex("Snapshot capture failed for assembly Broken"));

            HotReloadSourceSnapshotter.CaptureAssemblies(
                _projectRoot,
                new[] { CreateAssembly("Broken"), CreateAssembly("Fixture") },
                HotReloadCompileStart.Unknown,
                CreateDocumentIndex());

            Assert.That(File.Exists(StampPath("Broken")), Is.False);
            Assert.That(File.Exists(SnapshotSourcePath("Fixture", mvid)), Is.True);
            Assert.That(File.Exists(StampPath("Fixture")), Is.True);
        }

        /// <summary>
        /// Verifies only assemblies with sources and an adjacent compiled DLL and PDB become snapshot candidates,
        /// each named by its current MVID.
        /// </summary>
        [Test]
        public void CollectSnapshotAssemblies_KeepsOnlyAssembliesWithSourcesAndCompiledOutput()
        {
            string dllPath = PlantCompiledAssembly("Fixture");
            string mvid = HotReloadAssemblyMvid.Read(dllPath);
            PlantCompiledAssembly("NoSources");
            File.Delete(Path.ChangeExtension(PlantCompiledAssembly("NoPdb"), ".pdb"));

            List<HotReloadSnapshotAssembly> result = HotReloadChangedFileAggregator.CollectSnapshotAssemblies(
                _projectRoot,
                new[]
                {
                    CreateAssemblyWithSources("NoSources", Array.Empty<string>()),
                    CreateAssembly("NotCompiled"),
                    CreateAssembly("NoPdb"),
                    CreateAssembly("Fixture")
                });

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].SnapshotDirectoryName, Is.EqualTo("Fixture-" + mvid));
            Assert.That(result[0].SourceFiles, Is.EqualTo(new[] { SourceRelativePath }));
        }

        /// <summary>
        /// Verifies a Virtual Player's capture reads the main project's compiled assembly and writes
        /// the snapshot under the player's own root, leaving no hot reload state under the main project.
        /// </summary>
        [Test]
        public void CaptureAssemblies_ForVirtualPlayerRoot_ReadsTheMainProjectsDllAndSnapshotsUnderThePlayer()
        {
            string mainRoot = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                string scriptAssembliesDirectory = Path.Combine(mainRoot, "Library", "ScriptAssemblies");
                Directory.CreateDirectory(scriptAssembliesDirectory);
                string sourceDllPath = typeof(HotReloadSnapshotAssemblyEnumerationTests).Assembly.Location;
                string dllPath = Path.Combine(scriptAssembliesDirectory, "Fixture" + HotReloadConstants.CompiledAssemblyExtension);
                File.Copy(sourceDllPath, dllPath);
                File.Copy(Path.ChangeExtension(sourceDllPath, ".pdb"), Path.ChangeExtension(dllPath, ".pdb"));
                string playerRoot = Path.Combine(mainRoot, "Library", "VP", "mppm1");
                Directory.CreateDirectory(Path.Combine(playerRoot, "Assets"));
                string playerFixturePath = Path.Combine(playerRoot, "Assets", "Fixture.cs");
                File.WriteAllText(playerFixturePath, "class Fixture {}\n");
                // A source the compile read is older than the compiled assembly; keeping it so leaves
                // it out of the capture's PDB check.
                File.SetLastWriteTimeUtc(playerFixturePath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                string mvid = HotReloadAssemblyMvid.Read(dllPath);

                HotReloadSourceSnapshotter.CaptureAssemblies(
                    playerRoot,
                    new[] { CreateAssembly("Fixture") },
                    HotReloadCompileStart.Unknown,
                    CreateDocumentIndex());

                string playerSnapshotSource = HotReloadSourceSnapshotLayout.SourcePath(
                    HotReloadSourceSnapshotLayout.AssemblyDirectory(playerRoot, "Fixture", mvid),
                    SourceRelativePath);
                Assert.That(File.Exists(playerSnapshotSource), Is.True);
                Assert.That(Directory.Exists(Path.Combine(mainRoot, "Library", "UloopHotReload")), Is.False);
            }
            finally
            {
                if (Directory.Exists(mainRoot))
                {
                    Directory.Delete(mainRoot, true);
                }
            }
        }

        /// <summary>
        /// Verifies that the capture takes the recorded compile start as the start of the PDB check, so
        /// a source saved after the compile started but before the DLL was written is checked and, with
        /// no PDB document to confirm it, marked as edited after the compile.
        /// </summary>
        [Test]
        public void CaptureAssemblies_ASourceSavedAfterTheCompileStartedButBeforeTheDll_IsMarked()
        {
            string snapshotDirectory = CaptureWithSourceSavedBetweenStartAndDll(
                HotReloadCompileStart.At(CompileStartUtc.Ticks));

            HotReloadSourceStampManifest manifest = HotReloadSourceStampManifest.Load(snapshotDirectory);
            Assert.That(manifest.IsEditedAfterCompile(SnapshotFileName()), Is.True);
        }

        /// <summary>
        /// Verifies that without a recorded compile start the same source is not checked, because the
        /// check then starts at the DLL write; the mark in the case above comes from the compile start.
        /// </summary>
        [Test]
        public void CaptureAssemblies_WithoutARecordedStart_LeavesASourceSavedBeforeTheDllUnmarked()
        {
            string snapshotDirectory = CaptureWithSourceSavedBetweenStartAndDll(HotReloadCompileStart.Unknown);

            HotReloadSourceStampManifest manifest = HotReloadSourceStampManifest.Load(snapshotDirectory);
            Assert.That(manifest.IsEditedAfterCompile(SnapshotFileName()), Is.False);
            Assert.That(manifest.TryGetStamp(SnapshotFileName(), out long _, out long _), Is.True);
        }

        // Plants the compiled assembly and orders the write times as compile start < source < DLL.
        // The fixture source has no document in the planted PDB, so a check cannot confirm it.
        private string CaptureWithSourceSavedBetweenStartAndDll(HotReloadCompileStart compileStart)
        {
            string dllPath = PlantCompiledAssembly("Fixture");
            File.SetLastWriteTimeUtc(dllPath, new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(
                Path.Combine(_projectRoot, "Assets", "Fixture.cs"),
                new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            string mvid = HotReloadAssemblyMvid.Read(dllPath);

            HotReloadSourceSnapshotter.CaptureAssemblies(
                _projectRoot,
                new[] { CreateAssembly("Fixture") },
                compileStart,
                CreateDocumentIndex());

            return Path.Combine(_snapshotRoot, "Fixture-" + mvid);
        }

        private static string SnapshotFileName()
        {
            return HotReloadSourceSnapshotLayout.SourceFileName(SourceRelativePath);
        }

        // Persists the PDB document lists under the temporary root, so a test never writes the
        // Editor's shared index.
        private HotReloadPdbDocumentIndex CreateDocumentIndex()
        {
            return new HotReloadPdbDocumentIndex(Path.Combine(_projectRoot, "PdbDocuments"));
        }

        // Copies this test assembly's own DLL and PDB, so the fixture has a real image for Cecil to read.
        private string PlantCompiledAssembly(string assemblyName)
        {
            string sourceDllPath = typeof(HotReloadSnapshotAssemblyEnumerationTests).Assembly.Location;
            string dllPath = Path.Combine(
                _projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + HotReloadConstants.CompiledAssemblyExtension);
            File.Copy(sourceDllPath, dllPath);
            File.Copy(Path.ChangeExtension(sourceDllPath, ".pdb"), Path.ChangeExtension(dllPath, ".pdb"));
            return dllPath;
        }

        private static UnityCompilationAssembly CreateAssembly(string assemblyName)
        {
            return CreateAssemblyWithSources(assemblyName, new[] { SourceRelativePath });
        }

        private static UnityCompilationAssembly CreateAssemblyWithSources(string assemblyName, string[] sourceFiles)
        {
            return new UnityCompilationAssembly(
                assemblyName,
                HotReloadConstants.ScriptAssembliesRelativeDirectory + "/" + assemblyName + HotReloadConstants.CompiledAssemblyExtension,
                sourceFiles,
                Array.Empty<string>(),
                Array.Empty<UnityCompilationAssembly>(),
                Array.Empty<string>(),
                AssemblyFlags.EditorAssembly);
        }

        private string SnapshotSourcePath(string assemblyName, string mvid)
        {
            return HotReloadSourceSnapshotLayout.SourcePath(
                HotReloadSourceSnapshotLayout.AssemblyDirectory(_projectRoot, assemblyName, mvid),
                SourceRelativePath);
        }

        private string StampPath(string assemblyName)
        {
            return Path.Combine(_snapshotRoot, assemblyName + ".stamp");
        }

        private static string ExpectedStamp(string dllPath, string mvid)
        {
            FileInfo dllInfo = new FileInfo(dllPath);
            return mvid + "," + dllInfo.LastWriteTimeUtc.Ticks + "," + dllInfo.Length;
        }
    }
}
