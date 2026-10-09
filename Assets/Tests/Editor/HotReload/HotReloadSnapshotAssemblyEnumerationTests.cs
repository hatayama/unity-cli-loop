using System;
using System.Collections.Generic;
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
            _snapshotRoot = Path.Combine(_projectRoot, HotReloadConstants.SourceSnapshotRelativeDirectory);
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
        public void CaptureAssemblyIfNeeded_WithoutStamp_CapturesSourcesRemovesStaleSnapshotAndWritesStamp()
        {
            string dllPath = PlantCompiledAssembly("Fixture");
            string mvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(dllPath);
            string staleDirectory = Path.Combine(_snapshotRoot, "Fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staleDirectory);

            HotReloadSourceSnapshotter.CaptureAssemblyIfNeeded(
                _projectRoot,
                _snapshotRoot,
                CreateAssembly("Fixture"),
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
        public void CaptureAssemblyIfNeeded_WhenStampMatches_SkipsCapture()
        {
            string dllPath = PlantCompiledAssembly("Fixture");
            string mvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(dllPath);
            string stamp = ExpectedStamp(dllPath, mvid);
            File.WriteAllText(StampPath("Fixture"), stamp);

            HotReloadSourceSnapshotter.CaptureAssemblyIfNeeded(
                _projectRoot,
                _snapshotRoot,
                CreateAssembly("Fixture"),
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
        public void CaptureAssemblyIfNeeded_WhenMvidDirectoryExists_KeepsSnapshotsAndWritesStamp()
        {
            string dllPath = PlantCompiledAssembly("Fixture");
            string mvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(dllPath);
            string currentDirectory = Path.Combine(_snapshotRoot, "Fixture-" + mvid);
            Directory.CreateDirectory(currentDirectory);
            string staleDirectory = Path.Combine(_snapshotRoot, "Fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staleDirectory);

            HotReloadSourceSnapshotter.CaptureAssemblyIfNeeded(
                _projectRoot,
                _snapshotRoot,
                CreateAssembly("Fixture"),
                HotReloadCompileStart.Unknown,
                CreateDocumentIndex());

            Assert.That(Directory.GetFiles(currentDirectory), Is.Empty);
            Assert.That(Directory.Exists(staleDirectory), Is.True);
            Assert.That(File.ReadAllText(StampPath("Fixture")), Is.EqualTo(ExpectedStamp(dllPath, mvid)));
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
            string mvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(dllPath);
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
            string mvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(dllPath);
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
                string mvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(dllPath);

                HotReloadSourceSnapshotter.CaptureAssemblies(
                    playerRoot,
                    new[] { CreateAssembly("Fixture") },
                    HotReloadCompileStart.Unknown,
                    CreateDocumentIndex());

                string playerSnapshotSource = Path.Combine(
                    playerRoot,
                    HotReloadConstants.SourceSnapshotRelativeDirectory,
                    "Fixture-" + mvid,
                    HotReloadSourceSnapshotter.HashProjectRelativePath(SourceRelativePath) + ".cs");
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
        public void CaptureAssemblyIfNeeded_ASourceSavedAfterTheCompileStartedButBeforeTheDll_IsMarked()
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
        public void CaptureAssemblyIfNeeded_WithoutARecordedStart_LeavesASourceSavedBeforeTheDllUnmarked()
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
            string mvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(dllPath);

            HotReloadSourceSnapshotter.CaptureAssemblyIfNeeded(
                _projectRoot,
                _snapshotRoot,
                CreateAssembly("Fixture"),
                compileStart,
                CreateDocumentIndex());

            return Path.Combine(_snapshotRoot, "Fixture-" + mvid);
        }

        private static string SnapshotFileName()
        {
            return HotReloadSourceSnapshotter.HashProjectRelativePath(SourceRelativePath) + ".cs";
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
            return Path.Combine(
                _snapshotRoot,
                assemblyName + "-" + mvid,
                HotReloadSourceSnapshotter.HashProjectRelativePath(SourceRelativePath) + ".cs");
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
