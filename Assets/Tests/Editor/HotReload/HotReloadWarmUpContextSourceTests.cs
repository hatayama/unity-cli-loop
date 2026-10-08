using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Contract of the warm-up's context source: when it skips and why, and which recorded
    /// assemblies become targets. Works on a temp project root with empty dll and PDB files,
    /// since the source only checks that they exist.
    /// </summary>
    public sealed class HotReloadWarmUpContextSourceTests
    {
        private string _projectRoot;
        private HotReloadEditorStateSnapshot _state;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "uloop-warm-up-source-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectRoot);
            _state = new HotReloadEditorStateSnapshot(false, false, false);
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
        /// What: a project without a ledger skips with no_targets.
        /// </summary>
        [Test]
        public void Capture_WithoutALedger_SkipsWithNoTargets()
        {
            Assert.That(Capture().SkipReason, Is.EqualTo(HotReloadWarmUpCapture.SkipReasonNoTargets));
        }

        /// <summary>
        /// What: a compiling Editor skips with compiling even when the ledger has a compiled assembly.
        /// </summary>
        [Test]
        public void Capture_WhileCompiling_SkipsWithCompiling()
        {
            RecordCompiled("A");
            _state = new HotReloadEditorStateSnapshot(true, false, false);

            HotReloadWarmUpCapture capture = Capture();

            Assert.That(capture.SkipReason, Is.EqualTo(HotReloadWarmUpCapture.SkipReasonCompiling));
            Assert.That(capture.Context, Is.Null);
        }

        /// <summary>
        /// What: an importing Editor skips with updating.
        /// </summary>
        [Test]
        public void Capture_WhileUpdating_SkipsWithUpdating()
        {
            RecordCompiled("A");
            _state = new HotReloadEditorStateSnapshot(false, true, false);

            Assert.That(Capture().SkipReason, Is.EqualTo(HotReloadWarmUpCapture.SkipReasonUpdating));
        }

        /// <summary>
        /// What: a recorded assembly whose PDB is missing is left out of the targets.
        /// </summary>
        [Test]
        public void Capture_WithoutThePdb_LeavesThatAssemblyOut()
        {
            RecordCompiled("A");
            WriteCompiledFile("B.dll");
            HotReloadWarmUpTargetLedger.Record(_projectRoot, new[] { "B", "A" });

            HotReloadWarmUpCapture capture = Capture();

            Assert.That(TargetNames(capture), Is.EqualTo(new[] { "A" }));
        }

        /// <summary>
        /// What: when no recorded assembly has its dll and PDB, the source skips with no_compiled_assembly.
        /// </summary>
        [Test]
        public void Capture_WithNoCompiledAssembly_SkipsWithNoCompiledAssembly()
        {
            HotReloadWarmUpTargetLedger.Record(_projectRoot, new[] { "A", "B" });

            Assert.That(Capture().SkipReason, Is.EqualTo(HotReloadWarmUpCapture.SkipReasonNoCompiledAssembly));
        }

        /// <summary>
        /// What: of more recorded assemblies than the cap, the targets are the first ones in ledger order.
        /// </summary>
        [Test]
        public void Capture_WithMoreThanTheCap_TakesTheMostRecentInLedgerOrder()
        {
            List<string> names = new List<string>();
            for (int index = 0; index < 10; index++)
            {
                string name = "Assembly" + index;
                names.Add(name);
                WriteCompiledFile(name + ".dll");
                WriteCompiledFile(name + ".pdb");
            }

            HotReloadWarmUpTargetLedger.Record(_projectRoot, names);

            Assert.That(
                TargetNames(Capture()),
                Is.EqualTo(names.GetRange(0, HotReloadWarmUpContextSource.MaxWarmedTargets)));
        }

        /// <summary>
        /// What: a target carries its dll and PDB paths and the referencing dlls the collector returns for it.
        /// </summary>
        [Test]
        public void Capture_PutsThePathsAndTheReferencingDllsIntoTheTarget()
        {
            RecordCompiled("A");
            string[] referencing = { "referencing.dll" };
            HotReloadWarmUpContextSource source = new HotReloadWarmUpContextSource(
                _projectRoot,
                new HotReloadStubEditorStateSnapshotCapture(() => _state),
                name => name == "A" ? referencing : Array.Empty<string>());

            HotReloadWarmUpTarget target = source.Capture().Context.Targets[0];

            Assert.That(target.DllPath, Is.EqualTo(CompiledPath("A.dll")), "dll");
            Assert.That(target.PdbPath, Is.EqualTo(CompiledPath("A.pdb")), "pdb");
            Assert.That(target.ReferencingDllPaths, Is.EqualTo(referencing), "referencing");
        }

        /// <summary>
        /// What: a failed last compile does not skip, because the loaded dlls are the last good build.
        /// </summary>
        [Test]
        public void Capture_AfterAFailedCompile_StillReturnsTheTargets()
        {
            RecordCompiled("A");
            _state = new HotReloadEditorStateSnapshot(false, false, true);

            Assert.That(TargetNames(Capture()), Is.EqualTo(new[] { "A" }));
        }

        private HotReloadWarmUpCapture Capture()
        {
            HotReloadWarmUpContextSource source = new HotReloadWarmUpContextSource(
                _projectRoot,
                new HotReloadStubEditorStateSnapshotCapture(() => _state),
                name => Array.Empty<string>());
            return source.Capture();
        }

        private void RecordCompiled(string name)
        {
            WriteCompiledFile(name + ".dll");
            WriteCompiledFile(name + ".pdb");
            HotReloadWarmUpTargetLedger.Record(_projectRoot, new[] { name });
        }

        private void WriteCompiledFile(string fileName)
        {
            string path = CompiledPath(fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, Array.Empty<byte>());
        }

        private string CompiledPath(string fileName)
        {
            return Path.Combine(CompiledAssemblyLayout.Resolve(_projectRoot).CompiledAssembliesDirectory, fileName);
        }

        private static List<string> TargetNames(HotReloadWarmUpCapture capture)
        {
            Assert.That(capture.Context, Is.Not.Null, "skipped with " + capture.SkipReason);
            List<string> names = new List<string>();
            foreach (HotReloadWarmUpTarget target in capture.Context.Targets)
            {
                names.Add(target.AssemblyName);
            }

            return names;
        }
    }
}
