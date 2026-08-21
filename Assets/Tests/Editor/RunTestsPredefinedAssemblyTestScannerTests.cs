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
                    " Additionally, 2 NUnit test method(s) are compiled into predefined assemblies rather than any test assembly, so this run could not discover them: Assembly-CSharp: Game.Foo.Alpha, Assembly-CSharp-Editor: Editor.Bar.Beta. Move these scripts into a folder whose .asmdef has Test Assemblies enabled (EditMode tests target the Editor platform only), reference the assemblies under test, then run 'uloop compile' and rerun the tests."));
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
                    " Additionally, 7 NUnit test method(s) are compiled into predefined assemblies rather than any test assembly, so this run could not discover them: Assembly-CSharp: Game.One.A, Assembly-CSharp: Game.Two.B, Assembly-CSharp: Game.Three.C, Assembly-CSharp: Game.Four.D, Assembly-CSharp: Game.Five.E (+2 more). Move these scripts into a folder whose .asmdef has Test Assemblies enabled (EditMode tests target the Editor platform only), reference the assemblies under test, then run 'uloop compile' and rerun the tests."));
        }

        /// <summary>
        /// What: AppendIfNeeded inserts a period when the current message has no terminator.
        /// </summary>
        [Test]
        public void AppendIfNeeded_WhenMessageHasNoTerminator_InsertsPeriodBeforeNotice()
        {
            RunTestsPredefinedAssemblyTestFindings findings = RunTestsPredefinedAssemblyTestFindings.Create(
                1,
                new[] { "Assembly-CSharp: Game.Foo.Alpha" });

            string message = RunTestsPredefinedAssemblyTestNoticeFormatter.AppendIfNeeded(
                RunTestsResponse.NoTestsFoundMessage,
                findings);

            Assert.That(
                message,
                Is.EqualTo(
                    "No tests found matching the specified filter criteria. Additionally, 1 NUnit test method(s) are compiled into predefined assemblies rather than any test assembly, so this run could not discover them: Assembly-CSharp: Game.Foo.Alpha. Move these scripts into a folder whose .asmdef has Test Assemblies enabled (EditMode tests target the Editor platform only), reference the assemblies under test, then run 'uloop compile' and rerun the tests."));
        }

        /// <summary>
        /// What: AppendIfNeeded does not insert a second period when the current message already ends with one.
        /// </summary>
        [Test]
        public void AppendIfNeeded_WhenMessageAlreadyEndsWithPeriod_DoesNotInsertExtraPeriod()
        {
            RunTestsPredefinedAssemblyTestFindings findings = RunTestsPredefinedAssemblyTestFindings.Create(
                1,
                new[] { "Assembly-CSharp: Game.Foo.Alpha" });
            string messageWithHint =
                "No tests found matching the specified filter criteria Possible asmdef issues: Assets/Tests/EditMode/Sample.Tests.asmdef: sample finding.";

            string message = RunTestsPredefinedAssemblyTestNoticeFormatter.AppendIfNeeded(
                messageWithHint,
                findings);

            Assert.That(
                message,
                Is.EqualTo(
                    "No tests found matching the specified filter criteria Possible asmdef issues: Assets/Tests/EditMode/Sample.Tests.asmdef: sample finding. Additionally, 1 NUnit test method(s) are compiled into predefined assemblies rather than any test assembly, so this run could not discover them: Assembly-CSharp: Game.Foo.Alpha. Move these scripts into a folder whose .asmdef has Test Assemblies enabled (EditMode tests target the Editor platform only), reference the assemblies under test, then run 'uloop compile' and rerun the tests."));
        }

        /// <summary>
        /// What: TypeCache 実経路の煙テスト. The Assets/Util probe is compiled into Assembly-CSharp.
        /// </summary>
        [Test]
        public async Task ScanPredefinedAssemblyTests_WhenRegistryIsLive_FindsProbeFixture()
        {
            await MainThreadSwitcher.SwitchToMainThread(CancellationToken.None);

            RunTestsPredefinedAssemblyTestFindings findings =
                UnityTestFrameworkExecutionServiceRegistry.Current.ScanPredefinedAssemblyTests();

            Assert.That(findings.TotalCount, Is.EqualTo(1));
            Assert.That(
                findings.SampleEntries,
                Is.EqualTo(
                    new[]
                    {
                        "Assembly-CSharp: UnityCliLoop.RunTestsPredefinedAssemblyProbe.RunTestsPredefinedAssemblyProbe.Marker"
                    }));
        }
    }
}
