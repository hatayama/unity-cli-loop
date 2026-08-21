using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies predefined-assembly test scanning, notice formatting, and registry wiring.
    /// </summary>
    public sealed class RunTestsPredefinedAssemblyTestScannerTests
    {
        /// <summary>
        /// What: methods that live only in project test assemblies produce no findings.
        /// </summary>
        [Test]
        public void Build_WithMethodsOnlyInProjectAsmdefAssemblies_ReturnsNone()
        {
            (string AssemblyName, string TypeFullName, string MethodName)[] methods =
            {
                ("Game.Tests", "Game.Tests.PlayerTests", "Move")
            };

            RunTestsPredefinedAssemblyTestFindings findings = RunTestsPredefinedAssemblyTestScanner.Build(methods);

            Assert.That(findings.TotalCount, Is.EqualTo(0));
            Assert.That(findings.SampleEntries, Is.EqualTo(Array.Empty<string>()));
        }

        /// <summary>
        /// What: Build keeps predefined assemblies, drops others, deduplicates, sorts, and caps samples.
        /// </summary>
        [Test]
        public void Build_WithPredefinedAssemblyMethods_FiltersSortsAndCaps()
        {
            (string AssemblyName, string TypeFullName, string MethodName)[] methods =
            {
                ("Game.Tests", "Game.Tests.PlayerTests", "Move"),
                ("Assembly-CSharp-Editor-firstpass", "Plugin.EditorTests", "Zed"),
                ("Assembly-CSharp-firstpass", "Plugin.PlayTests", "Yap"),
                ("Assembly-CSharp-Editor", "Editor.Tests", "Xray"),
                ("Assembly-CSharp", "Game.Foo", "Alpha"),
                ("Assembly-CSharp", "Game.Foo", "Alpha"),
                ("Assembly-CSharp", "Game.Bar", "Beta"),
                ("Assembly-CSharp", "Game.Baz", "Gamma"),
                ("Assembly-CSharp", "Game.Qux", "Delta")
            };

            RunTestsPredefinedAssemblyTestFindings findings = RunTestsPredefinedAssemblyTestScanner.Build(methods);

            Assert.That(findings.TotalCount, Is.EqualTo(7));
            Assert.That(
                findings.SampleEntries,
                Is.EqualTo(
                    new[]
                    {
                        "Assembly-CSharp: Game.Bar.Beta",
                        "Assembly-CSharp: Game.Baz.Gamma",
                        "Assembly-CSharp: Game.Foo.Alpha",
                        "Assembly-CSharp: Game.Qux.Delta",
                        "Assembly-CSharp-Editor: Editor.Tests.Xray"
                    }));
        }

        /// <summary>
        /// What: the same assembly/type/method triple from overlapping attributes is counted once.
        /// </summary>
        [Test]
        public void Build_WithDuplicateAttributeEntries_CountsMethodOnce()
        {
            (string AssemblyName, string TypeFullName, string MethodName)[] methods =
            {
                ("Assembly-CSharp", "Game.Foo", "Alpha"),
                ("Assembly-CSharp", "Game.Foo", "Alpha"),
                ("Assembly-CSharp", "Game.Foo", "Alpha")
            };

            RunTestsPredefinedAssemblyTestFindings findings = RunTestsPredefinedAssemblyTestScanner.Build(methods);

            Assert.That(findings.TotalCount, Is.EqualTo(1));
            Assert.That(
                findings.SampleEntries,
                Is.EqualTo(new[] { "Assembly-CSharp: Game.Foo.Alpha" }));
        }

        /// <summary>
        /// What: FormatNotice matches the full notice when every finding fits in the sample list.
        /// </summary>
        [Test]
        public void FormatNotice_WhenSampleCoversTotal_MatchesLiteral()
        {
            RunTestsPredefinedAssemblyTestFindings findings = RunTestsPredefinedAssemblyTestFindings.Create(
                2,
                new[]
                {
                    "Assembly-CSharp: Game.Foo.Alpha",
                    "Assembly-CSharp-Editor: Editor.Bar.Beta"
                });

            string notice = RunTestsPredefinedAssemblyTestNoticeFormatter.FormatNotice(findings);

            Assert.That(
                notice,
                Is.EqualTo(
                    " Additionally, 2 NUnit test method(s) are compiled into predefined assemblies rather than any test assembly: Assembly-CSharp: Game.Foo.Alpha, Assembly-CSharp-Editor: Editor.Bar.Beta. Unity Test Runner does not discover tests that live outside a test assembly; move these scripts into a folder whose .asmdef has Test Assemblies enabled (EditMode tests target the Editor platform only), reference the assemblies under test, then run 'uloop compile' and rerun the tests."));
        }

        /// <summary>
        /// What: FormatNotice appends (+N more) when TotalCount exceeds the sample cap.
        /// </summary>
        [Test]
        public void FormatNotice_WhenTotalExceedsSampleLimit_AppendsMoreSuffix()
        {
            RunTestsPredefinedAssemblyTestFindings findings = RunTestsPredefinedAssemblyTestFindings.Create(
                7,
                new[]
                {
                    "Assembly-CSharp: Game.One.A",
                    "Assembly-CSharp: Game.Two.B",
                    "Assembly-CSharp: Game.Three.C",
                    "Assembly-CSharp: Game.Four.D",
                    "Assembly-CSharp: Game.Five.E"
                });

            string notice = RunTestsPredefinedAssemblyTestNoticeFormatter.FormatNotice(findings);

            Assert.That(
                notice,
                Is.EqualTo(
                    " Additionally, 7 NUnit test method(s) are compiled into predefined assemblies rather than any test assembly: Assembly-CSharp: Game.One.A, Assembly-CSharp: Game.Two.B, Assembly-CSharp: Game.Three.C, Assembly-CSharp: Game.Four.D, Assembly-CSharp: Game.Five.E (+2 more). Unity Test Runner does not discover tests that live outside a test assembly; move these scripts into a folder whose .asmdef has Test Assemblies enabled (EditMode tests target the Editor platform only), reference the assemblies under test, then run 'uloop compile' and rerun the tests."));
        }

        /// <summary>
        /// What: TypeCache 実経路の煙テスト. This repository's tests live in asmdefs, so Scan returns none.
        /// </summary>
        [Test]
        public async Task ScanPredefinedAssemblyTests_WhenRegistryIsLive_ReturnsNoneWithoutThrowing()
        {
            await MainThreadSwitcher.SwitchToMainThread(CancellationToken.None);

            RunTestsPredefinedAssemblyTestFindings findings =
                UnityTestFrameworkExecutionServiceRegistry.Current.ScanPredefinedAssemblyTests();

            Assert.That(findings.TotalCount, Is.EqualTo(0));
            Assert.That(findings.SampleEntries, Is.EqualTo(Array.Empty<string>()));
        }
    }
}
