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
            File.WriteAllText(Path.Combine(_projectRoot, "Assets", "Fixture.cs"), "class Fixture {}\n");
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

            HotReloadSourceSnapshotter.CaptureAssemblyIfNeeded(_projectRoot, _snapshotRoot, CreateAssembly("Fixture"));

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

            HotReloadSourceSnapshotter.CaptureAssemblyIfNeeded(_projectRoot, _snapshotRoot, CreateAssembly("Fixture"));

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

            HotReloadSourceSnapshotter.CaptureAssemblyIfNeeded(_projectRoot, _snapshotRoot, CreateAssembly("Fixture"));

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
                new[] { CreateAssembly("Broken"), CreateAssembly("Fixture") });

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
