using System;
using System.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies run-tests no-discovery asmdef diagnostics.
    /// </summary>
    public sealed class RunTestsNoTestsDiagnosticServiceTests
    {
        [Test]
        public void AppendFindingsIfEligible_WhenRunWasSuccessful_DoesNotAppendFindings()
        {
            // Verifies that a normal successful result cannot receive no-test asmdef hints.
            RunTestsAsmdefDiagnosticFinding[] findings =
            {
                new RunTestsAsmdefDiagnosticFinding("Assets/Tests/EditMode/Sample.Tests.asmdef", "sample finding")
            };

            string message = RunTestsNoTestsDiagnosticService.AppendFindingsIfEligible(
                RunTestsResponse.NoTestsFoundMessage,
                success: true,
                testCount: 1,
                findings);

            Assert.That(message, Is.EqualTo(RunTestsResponse.NoTestsFoundMessage));
        }

        [Test]
        public void AppendFindingsIfEligible_WhenNoFindingsExist_DoesNotAppendGenericHint()
        {
            // Verifies that filter-only no-discovery results stay unchanged when asmdefs look healthy.
            string message = RunTestsNoTestsDiagnosticService.AppendFindingsIfEligible(
                RunTestsResponse.NoTestsFoundMessage,
                success: false,
                testCount: 0,
                Array.Empty<RunTestsAsmdefDiagnosticFinding>());

            Assert.That(message, Is.EqualTo(RunTestsResponse.NoTestsFoundMessage));
        }

        [Test]
        public void AppendFindingsIfEligible_WhenMessageIsNull_DoesNotAppendFindings()
        {
            // Verifies unset result messages are ignored by no-test diagnostics.
            RunTestsAsmdefDiagnosticFinding[] findings =
            {
                new RunTestsAsmdefDiagnosticFinding("Assets/Tests/EditMode/Sample.Tests.asmdef", "sample finding")
            };

            string message = RunTestsNoTestsDiagnosticService.AppendFindingsIfEligible(
                null,
                success: false,
                testCount: 0,
                findings);

            Assert.That(message, Is.Null);
        }

        [Test]
        public void ShouldAppendDiagnostics_WhenFilterIsAllAndNoTestsWereFound_ReturnsTrue()
        {
            // Verifies that unfiltered no-discovery results can receive asmdef diagnostics.
            bool shouldAppend = RunTestsNoTestsDiagnosticService.ShouldAppendDiagnostics(
                RunTestsResponse.NoTestsFoundMessage,
                success: false,
                testCount: 0,
                TestFilterType.all);

            Assert.That(shouldAppend, Is.True);
        }

        [Test]
        public void ShouldAppendDiagnostics_WhenFilterIsExact_ReturnsFalse()
        {
            // Verifies that narrow filter misses cannot receive asmdef diagnostics.
            bool shouldAppend = RunTestsNoTestsDiagnosticService.ShouldAppendDiagnostics(
                RunTestsResponse.NoTestsFoundMessage,
                success: false,
                testCount: 0,
                TestFilterType.exact);

            Assert.That(shouldAppend, Is.False);
        }

        [Test]
        public void Analyze_WhenTestNamedAsmdefIsNotMarkedAsTestAssembly_ReturnsMarkerFinding()
        {
            // Verifies that a test-named asmdef without any test assembly marker is diagnosed.
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/EditMode/Sample.EditMode.Tests.asmdef",
                "Sample.EditMode.Tests");

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { testAsmdef },
                UnityCliLoopTestMode.EditMode);

            Assert.That(HasFinding(findings, "not marked as a test assembly"), Is.True);
        }

        [Test]
        public void Analyze_WhenTestsEditorNamedAsmdefIsNotMarkedAsTestAssembly_ReturnsMarkerFinding()
        {
            // Verifies that the common Tests.Editor asmdef naming convention is diagnosed.
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/Editor/Sample.Tests.Editor.asmdef",
                "Sample.Tests.Editor");

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { testAsmdef },
                UnityCliLoopTestMode.EditMode);

            Assert.That(HasFinding(findings, "not marked as a test assembly"), Is.True);
        }

        [Test]
        public void Analyze_WhenNestedTestsEditModeAsmdefIsNotMarkedAsTestAssembly_ReturnsMarkerFinding()
        {
            // Verifies that nested feature test asmdefs using Tests.EditMode naming are diagnosed.
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/Gameplay/Editor/Gameplay.Tests.EditMode.asmdef",
                "Gameplay.Tests.EditMode");

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { testAsmdef },
                UnityCliLoopTestMode.EditMode);

            Assert.That(HasFinding(findings, "not marked as a test assembly"), Is.True);
        }

        [Test]
        public void Analyze_WhenNestedTestsFeatureEditorAsmdefIsNotMarkedAsTestAssembly_ReturnsMarkerFinding()
        {
            // Verifies that nested feature test asmdefs using Tests.Feature.Editor naming are diagnosed.
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/Gameplay/Editor/Gameplay.Tests.Rendering.Editor.asmdef",
                "Gameplay.Tests.Rendering.Editor");

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { testAsmdef },
                UnityCliLoopTestMode.EditMode);

            Assert.That(HasFinding(findings, "not marked as a test assembly"), Is.True);
        }

        [Test]
        public void Analyze_WhenEditorFolderAsmdefIsNotMarkedAsTestAssembly_ReturnsMarkerFinding()
        {
            // Verifies that asmdefs under Assets/Tests/Editor are diagnosed even without a test-like name.
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/Editor/Sample.asmdef",
                "Sample.EditorChecks");

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { testAsmdef },
                UnityCliLoopTestMode.EditMode);

            Assert.That(HasFinding(findings, "not marked as a test assembly"), Is.True);
        }

        [Test]
        public void Analyze_WhenPlayModeIsRequested_IgnoresEditorAsmdef()
        {
            // Verifies that PlayMode no-discovery diagnostics do not point at EditMode asmdefs.
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/Editor/Sample.asmdef",
                "Sample.EditorChecks");

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { testAsmdef },
                UnityCliLoopTestMode.PlayMode);

            Assert.That(findings, Is.Empty);
        }

        [Test]
        public void Analyze_WhenEditModeIsRequested_IgnoresPlayModeAsmdef()
        {
            // Verifies that EditMode no-discovery diagnostics do not point at PlayMode asmdefs.
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/PlayMode/Sample.Tests.PlayMode.asmdef",
                "Sample.Tests.PlayMode",
                includePlatforms: new[] { "Editor" });

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { testAsmdef },
                UnityCliLoopTestMode.EditMode);

            Assert.That(findings, Is.Empty);
        }

        [Test]
        public void Analyze_WhenPlayModeAsmdefIsNotMarkedAsTestAssembly_ReturnsMarkerFinding()
        {
            // Verifies that PlayMode-relevant asmdefs still receive missing-marker diagnostics.
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/PlayMode/Sample.Tests.PlayMode.asmdef",
                "Sample.Tests.PlayMode");

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { testAsmdef },
                UnityCliLoopTestMode.PlayMode);

            Assert.That(HasFinding(findings, "not marked as a test assembly"), Is.True);
        }

        [Test]
        public void Analyze_WhenPlayModeAsmdefTargetsEditor_ReturnsPlayModePlatformFinding()
        {
            // Verifies that PlayMode asmdefs restricted to Editor are diagnosed instead of being filtered out.
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/PlayMode/Sample.Tests.PlayMode.asmdef",
                "Sample.Tests.PlayMode",
                optionalUnityReferences: new[] { "TestAssemblies" },
                includePlatforms: new[] { "Editor" });

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { testAsmdef },
                UnityCliLoopTestMode.PlayMode);

            Assert.That(HasFinding(findings, "PlayMode test asmdef but targets Editor only"), Is.True);
        }

        [Test]
        public void Analyze_WhenTestNamedAsmdefHasDirectTestRunnerReference_DoesNotReportMissingMarker()
        {
            // Verifies that legacy direct TestRunner references are not treated as missing markers by themselves.
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/EditMode/Sample.EditMode.Tests.asmdef",
                "Sample.EditMode.Tests",
                references: new[] { "UnityEngine.TestRunner" });

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { testAsmdef },
                UnityCliLoopTestMode.EditMode);

            Assert.That(HasFinding(findings, "not marked as a test assembly"), Is.False);
        }

        [Test]
        public void Analyze_WhenTestAssemblyAlsoReferencesNamedTestRunner_ReturnsDuplicateFinding()
        {
            // Verifies that TestAssemblies plus direct named TestRunner references are treated as duplicate-risk configuration.
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/EditMode/Sample.EditMode.Tests.asmdef",
                "Sample.EditMode.Tests",
                references: new[] { "UnityEngine.TestRunner", "UnityEditor.TestRunner" },
                optionalUnityReferences: new[] { "TestAssemblies" },
                includePlatforms: new[] { "Editor" });

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { testAsmdef },
                UnityCliLoopTestMode.EditMode);

            Assert.That(HasFinding(findings, "duplicate references"), Is.True);
        }

        [Test]
        public void Analyze_WhenTestAssemblyAlsoReferencesGuidTestRunner_ReturnsDuplicateFinding()
        {
            // Verifies that TestAssemblies plus GUID TestRunner references are treated as duplicate-risk configuration.
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/EditMode/Sample.EditMode.Tests.asmdef",
                "Sample.EditMode.Tests",
                references: new[] { "GUID:27619889b8ba8c24980f49ee34dbb44a" },
                optionalUnityReferences: new[] { "TestAssemblies" },
                includePlatforms: new[] { "Editor" });

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { testAsmdef },
                UnityCliLoopTestMode.EditMode);

            Assert.That(HasFinding(findings, "duplicate references"), Is.True);
        }

        [Test]
        public void Analyze_WhenEditModeAsmdefDoesNotTargetEditor_ReturnsEditorPlatformFinding()
        {
            // Verifies that EditMode test asmdefs without the Editor platform receive a targeted hint.
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/EditMode/Sample.EditMode.Tests.asmdef",
                "Sample.EditMode.Tests",
                optionalUnityReferences: new[] { "TestAssemblies" });

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { testAsmdef },
                UnityCliLoopTestMode.EditMode);

            Assert.That(HasFinding(findings, "includePlatforms"), Is.True);
        }

        [Test]
        public void Analyze_WhenRuntimeReferenceExists_DoesNotReturnRuntimeReferenceFinding()
        {
            // Verifies that test asmdefs referencing runtime code do not receive the runtime-reference hint.
            RunTestsAsmdefInfo runtimeAsmdef = CreateAsmdef(
                "Assets/Scripts/Sample.Runtime.asmdef",
                "Sample.Runtime");
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/EditMode/Sample.EditMode.Tests.asmdef",
                "Sample.EditMode.Tests",
                references: new[] { "Sample.Runtime" },
                optionalUnityReferences: new[] { "TestAssemblies" },
                includePlatforms: new[] { "Editor" });

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { runtimeAsmdef, testAsmdef },
                UnityCliLoopTestMode.EditMode);

            Assert.That(HasFinding(findings, "runtime asmdef"), Is.False);
        }

        [Test]
        public void Analyze_WhenRuntimeReferenceIsMissing_ReturnsRuntimeReferenceFinding()
        {
            // Verifies that a test asmdef without runtime references receives a weak runtime-code hint.
            RunTestsAsmdefInfo runtimeAsmdef = CreateAsmdef(
                "Assets/Scripts/Sample.Runtime.asmdef",
                "Sample.Runtime");
            RunTestsAsmdefInfo testAsmdef = CreateAsmdef(
                "Assets/Tests/EditMode/Sample.EditMode.Tests.asmdef",
                "Sample.EditMode.Tests",
                optionalUnityReferences: new[] { "TestAssemblies" },
                includePlatforms: new[] { "Editor" });

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { runtimeAsmdef, testAsmdef },
                UnityCliLoopTestMode.EditMode);

            Assert.That(HasFinding(findings, "runtime asmdef"), Is.True);
        }

        [Test]
        public void Analyze_WhenOtherModeTestAsmdefExists_DoesNotTreatItAsRuntime()
        {
            // Verifies that PlayMode test asmdefs do not create runtime-reference hints for EditMode tests.
            RunTestsAsmdefInfo playModeAsmdef = CreateAsmdef(
                "Assets/Tests/PlayMode/Gameplay.asmdef",
                "Gameplay");
            RunTestsAsmdefInfo editModeAsmdef = CreateAsmdef(
                "Assets/Tests/EditMode/Sample.EditMode.Tests.asmdef",
                "Sample.EditMode.Tests",
                optionalUnityReferences: new[] { "TestAssemblies" },
                includePlatforms: new[] { "Editor" });

            RunTestsAsmdefDiagnosticFinding[] findings = RunTestsNoTestsDiagnosticService.Analyze(
                new[] { playModeAsmdef, editModeAsmdef },
                UnityCliLoopTestMode.EditMode);

            Assert.That(HasFinding(findings, "runtime asmdef"), Is.False);
        }

        private static bool HasFinding(RunTestsAsmdefDiagnosticFinding[] findings, string expectedText)
        {
            return findings.Any(finding => finding.Message.Contains(expectedText, StringComparison.Ordinal));
        }

        private static RunTestsAsmdefInfo CreateAsmdef(
            string assetPath,
            string name,
            string[] references = null,
            string[] optionalUnityReferences = null,
            string[] includePlatforms = null,
            bool testAssemblies = false)
        {
            return new RunTestsAsmdefInfo(
                assetPath,
                string.Empty,
                name,
                references ?? Array.Empty<string>(),
                optionalUnityReferences ?? Array.Empty<string>(),
                includePlatforms ?? Array.Empty<string>(),
                testAssemblies);
        }
    }
}
