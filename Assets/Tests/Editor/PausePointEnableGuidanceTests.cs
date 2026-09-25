using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.Tests.PausePointToolsFixtures;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies enable-pause-point arming next-action, re-arm discard warning, and closing-brace warning.
    /// </summary>
    [TestFixture]
    public sealed class PausePointEnableGuidanceTests
    {
        private const string FixtureFilePath = "Assets/Tests/Editor/PausePointToolsFixture.cs";

        // This file itself: its PDB-mapped PdbUnavailableProbe is the method a no-PDB shim lookup
        // claims as patched.
        private const string GuidanceTestsFilePath = "Assets/Tests/Editor/PausePointEnableGuidanceTests.cs";

        private const string CompiledMethodSpanFixtureFile =
            "Assets/Tests/Editor/SourcePausePointResolver/Fixtures/CompiledMethodSpanFixture.cs";
        private const int FixtureStatementLine = 12;
        private const int FixtureClosingBraceLine = 13;

        // The blank line between the field and Add. Not the field line itself: its initializer gives
        // the implicit constructor a sequence point there, so that line resolves to the constructor.
        private const int FixtureBlankLineAboveMethod = 8;

        // A path no compiled assembly lists, so resolving a line in it always fails.
        private const string IntroducedTypeFilePath = "Assets/DoesNotExist/IntroducedOwner.cs";

        private const int IntroducedTypeRequestedLine = 10;

        // The class's closing brace: inside the file, but no sequence point exists on or after it
        // (Add's closing brace is line 13), so the resolver fails with nearby method spans.
        private const int UnresolvableFixtureLine = 14;

        // Past the end of the fixture, for a patched span that no fixture line falls inside.
        private const int LinePastTheEndOfTheFixture = 9999;

        // The Methods[] label hot reload reports for the fixture's Add.
        private const string FixtureAddRowLabel =
            "io.github.hatayama.UnityCliLoop.Tests.PausePointToolsFixtures.EnableBySourceLocationFixture.Add(System.Int32,System.Int32)";

        private const string ExpectedArmingNextActionForJump =
            "Run the code path so the marker can hit, then read the outcome with: uloop pause-point-status --id \"jump\". To block until it hits without a trigger command (e.g. waiting for physics or a multi-step action): uloop await-pause-point --id \"jump\" --timeout-seconds <n>. To arm, trigger, and collect in one call: uloop enable-pause-point --await --resume-play --trigger \"<uloop subcommand without the leading 'uloop', e.g. simulate-keyboard --action Press --key Space>\".";

        private const string ExpectedRearmDiscardWarningGeneration1 =
            "Generation 1 of this pause point had already hit; this re-arm discarded its CapturedVariables and CapturedVariableHistory. Read results with pause-point-status before re-arming when you need them.";

        private const string FixtureClosingBraceMethodDisplayName =
            "System.Int32 io.github.hatayama.UnityCliLoop.Tests.PausePointToolsFixtures.EnableBySourceLocationFixture::Add(System.Int32,System.Int32)";

        private const string ExpectedClosingBraceWarningForPlayerMoveLine42 =
            "--line resolved to the method's closing brace at line 42. Every return path through Player.Move reaches this line, including early returns, so captured variables can reflect a different path than the one you meant. To observe one specific path, target a statement line inside that path.";

        private const string ExpectedClosingBraceWarningForFixture =
            "--line resolved to the method's closing brace at line 13. Every return path through "
            + FixtureClosingBraceMethodDisplayName
            + " reaches this line, including early returns, so captured variables can reflect a different path than the one you meant. To observe one specific path, target a statement line inside that path.";

        [SetUp]
        public void SetUp()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakePausePointPauseController(), () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// What: filling an empty success RecommendedNextAction uses the arming guidance literal.
        /// </summary>
        [Test]
        public void ResolveSuccessEnableRecommendedNextAction_WhenExistingIsEmpty_ReturnsArmingGuidance()
        {
            string action = PausePointEnableWarnings.ResolveSuccessEnableRecommendedNextAction(
                string.Empty,
                "jump");

            Assert.That(action, Is.EqualTo(ExpectedArmingNextActionForJump));
        }

        /// <summary>
        /// What: arming guidance describes the trigger as a subcommand without a leading uloop command.
        /// </summary>
        [Test]
        public void ResolveSuccessEnableRecommendedNextAction_WhenExistingIsEmpty_ExplainsTriggerSubcommandFormat()
        {
            // Keep the deprecated placeholder split so repository searches for its contiguous spelling
            // report only real regressions in user-facing guidance.
            const string deprecatedPlaceholder = "<uloop " + "command>";
            string action = PausePointEnableWarnings.ResolveSuccessEnableRecommendedNextAction(
                string.Empty,
                "jump");

            Assert.That(action, Does.Not.Contain(deprecatedPlaceholder));
            Assert.That(action, Does.Contain("without"));
        }

        /// <summary>
        /// What: arming guidance offers a blocking wait that needs no --trigger, so a marker driven
        /// by physics or a multi-step action is not presented as requiring a trigger command.
        /// </summary>
        [Test]
        public void ResolveSuccessEnableRecommendedNextAction_WhenExistingIsEmpty_OffersAwaitWithoutATrigger()
        {
            string action = PausePointEnableWarnings.ResolveSuccessEnableRecommendedNextAction(
                string.Empty,
                "jump");

            Assert.That(action, Does.Contain("uloop await-pause-point --id \"jump\" --timeout-seconds"));
            Assert.That(
                action.IndexOf("await-pause-point", StringComparison.Ordinal),
                Is.LessThan(action.IndexOf("--trigger", StringComparison.Ordinal)),
                "the trigger-free wait must be offered before the trigger form");
        }

        /// <summary>
        /// What: a non-empty RecommendedNextAction is kept verbatim and is not replaced by arming guidance.
        /// </summary>
        [Test]
        public void ResolveSuccessEnableRecommendedNextAction_WhenExistingIsNonEmpty_KeepsExisting()
        {
            const string existing =
                "Verify ResolvedLineText is the statement you intended. If it is not, run 'uloop compile' "
                + "and re-enable the pause point.";

            string action = PausePointEnableWarnings.ResolveSuccessEnableRecommendedNextAction(
                existing,
                "jump");

            Assert.That(action, Is.EqualTo(existing));
        }

        /// <summary>
        /// What: enable by id fills RecommendedNextAction with the arming guidance for that id.
        /// </summary>
        [Test]
        public void Enable_WhenIdPathSucceeds_SetsArmingRecommendedNextAction()
        {
            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                Id = "jump",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            Assert.That(response.RecommendedNextAction, Is.EqualTo(ExpectedArmingNextActionForJump));
            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                UnityCliLoopJsonResponseSerializerSettings.Settings);
            JObject payload = JObject.Parse(json);
            Assert.That(payload["LineBasis"], Is.Null);
        }

        /// <summary>
        /// What: enable by file:line fills RecommendedNextAction with arming guidance for the derived id.
        /// </summary>
        [Test]
        public void Enable_WhenFileLinePathSucceeds_SetsArmingRecommendedNextAction()
        {
            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureFilePath,
                Line = FixtureStatementLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(
                response.Success,
                Is.True,
                response.ErrorCode + " / " + response.Message);
            string expected =
                "Run the code path so the marker can hit, then read the outcome with: uloop pause-point-status --id \""
                + FixtureFilePath
                + ":"
                + FixtureStatementLine
                + "\". To block until it hits without a trigger command (e.g. waiting for physics or a multi-step action): uloop await-pause-point --id \""
                + FixtureFilePath
                + ":"
                + FixtureStatementLine
                + "\" --timeout-seconds <n>. To arm, trigger, and collect in one call: uloop enable-pause-point --await --resume-play --trigger \"<uloop subcommand without the leading 'uloop', e.g. simulate-keyboard --action Press --key Space>\".";
            Assert.That(response.RecommendedNextAction, Is.EqualTo(expected));
            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                UnityCliLoopJsonResponseSerializerSettings.Settings);
            JObject payload = JObject.Parse(json);
            Assert.That(payload["LineBasis"]?.Value<string>(), Is.EqualTo("EditedFile"));
        }

        /// <summary>
        /// What: re-arming an id that already hit warns with the previous generation number.
        /// </summary>
        [Test]
        public void Enable_WhenReArmingAfterHit_AppendsDiscardWarning()
        {
            PausePointUseCase useCase = new PausePointUseCase();
            PausePointResponse first = useCase.Enable(new EnablePausePointSchema
            {
                Id = "jump",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });
            Assert.That(first.Success, Is.True, first.ErrorCode + " / " + first.Message);
            Assert.That(first.Generation, Is.EqualTo(1));
            UloopPausePointRegistry.Hit("jump");

            PausePointResponse response = useCase.Enable(new EnablePausePointSchema
            {
                Id = "jump",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                PausePointEnableWarnings.CreateEnableWarning(),
                ExpectedRearmDiscardWarningGeneration1);
            Assert.That(response.Warning, Is.EqualTo(expectedWarning));
        }

        /// <summary>
        /// What: re-arming an id that never hit does not add the capture-discard warning.
        /// </summary>
        [Test]
        public void Enable_WhenReArmingBeforeHit_OmitsDiscardWarning()
        {
            PausePointUseCase useCase = new PausePointUseCase();
            PausePointResponse first = useCase.Enable(new EnablePausePointSchema
            {
                Id = "jump",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });
            Assert.That(first.Success, Is.True, first.ErrorCode + " / " + first.Message);

            PausePointResponse response = useCase.Enable(new EnablePausePointSchema
            {
                Id = "jump",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            // Normalized to empty because an omitted warning is now null, while the builder returns
            // empty when it has nothing to say: both mean "no discard warning was added".
            Assert.That(
                response.Warning ?? string.Empty,
                Is.EqualTo(PausePointEnableWarnings.CreateEnableWarning()));
        }

        /// <summary>
        /// What: a resolved closing brace at the method end uses the closing-brace warning literal.
        /// </summary>
        [Test]
        public void BuildClosingBraceWarningOrEmpty_WhenResolvedLineIsClosingBrace_ReturnsWarning()
        {
            string warning = PausePointEnableWarnings.BuildClosingBraceWarningOrEmpty(
                "}",
                42,
                "Player.Move",
                42);

            Assert.That(warning, Is.EqualTo(ExpectedClosingBraceWarningForPlayerMoveLine42));
        }

        /// <summary>
        /// What: a non-brace resolved line does not produce the closing-brace warning.
        /// </summary>
        [Test]
        public void BuildClosingBraceWarningOrEmpty_WhenResolvedLineIsNotClosingBrace_ReturnsEmpty()
        {
            string warning = PausePointEnableWarnings.BuildClosingBraceWarningOrEmpty(
                "return sum;",
                12,
                "EnableBySourceLocationFixture.Add",
                13);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a nested closing brace that is not the method end stays silent.
        /// </summary>
        [Test]
        public void BuildClosingBraceWarningOrEmpty_WhenClosingBraceIsNotMethodEnd_ReturnsEmpty()
        {
            string warning = PausePointEnableWarnings.BuildClosingBraceWarningOrEmpty(
                "}",
                20,
                "Player.Move",
                42);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a closing brace with no method end available stays silent.
        /// </summary>
        [Test]
        public void BuildClosingBraceWarningOrEmpty_WhenMethodEndIsUnknown_ReturnsEmpty()
        {
            string warning = PausePointEnableWarnings.BuildClosingBraceWarningOrEmpty(
                "}",
                42,
                "Player.Move",
                0);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: enabling on the fixture method's closing brace appends the closing-brace warning.
        /// </summary>
        [Test]
        public void Enable_WhenResolvedLineIsMethodClosingBrace_AppendsClosingBraceWarning()
        {
            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureFilePath,
                Line = FixtureClosingBraceLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(
                response.Success,
                Is.True,
                response.ErrorCode + " / " + response.Message);
            Assert.That(response.ResolvedLine, Is.EqualTo(FixtureClosingBraceLine));
            Assert.That(response.ResolvedLineText, Is.EqualTo("}"));
            Assert.That(response.ResolvedMethod, Is.EqualTo(FixtureClosingBraceMethodDisplayName));
            string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                PausePointEnableWarnings.MergeWarnings(
                    PausePointEnableWarnings.CreateEnableWarning(),
                    SourcePausePointConstants.SmallMethodInliningRiskWarning),
                ExpectedClosingBraceWarningForFixture);
            Assert.That(response.Warning, Is.EqualTo(expectedWarning));
        }

        /// <summary>
        /// What: enabling a physics-callback method puts the dispatch warning and the mid-solver
        /// warning in separate Warnings entries, while Warning stays their space-joined form.
        /// </summary>
        [Test]
        public void Enable_WhenPhysicsCallbackMethod_SplitsDispatchAndMidSolverWarnings()
        {
            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = "Assets/Tests/Editor/PausePointToolsPhysicsFixture.cs",
                Line = 11,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(
                response.Success,
                Is.True,
                response.ErrorCode + " / " + response.Message);
            Assert.That(
                response.Warnings,
                Does.Contain(SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning));
            Assert.That(
                response.Warnings,
                Does.Contain(SourcePausePointConstants.PhysicalCallbackMidSolverValuesWarning));
            int dispatchIndex = IndexOfWarning(
                response.Warnings,
                SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning);
            int midSolverIndex = IndexOfWarning(
                response.Warnings,
                SourcePausePointConstants.PhysicalCallbackMidSolverValuesWarning);
            Assert.That(dispatchIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(midSolverIndex, Is.EqualTo(dispatchIndex + 1));
            Assert.That(
                response.Warning,
                Is.EqualTo(string.Join(" ", response.Warnings)));
            Assert.That(
                response.Warning,
                Does.Contain(SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning));
            Assert.That(
                response.Warning,
                Does.Contain(SourcePausePointConstants.PhysicalCallbackMidSolverValuesWarning));
        }

        /// <summary>
        /// What: a file the hot-reload side reports as declaring an introduced type gets the
        /// explanation that it has no compiled code, instead of advice to fix the path or
        /// recompute the line against a compiled source it does not have.
        /// </summary>
        [Test]
        public void Enable_WhenTheFileDeclaresAnIntroducedType_ExplainsThereIsNoCompiledCode()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.IntroducedTypeSourceFiles = new HashSet<string> { IntroducedTypeFilePath };

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = IntroducedTypeFilePath,
                    Line = 10,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(response.Message, Does.Contain("introduced without a compile"));
                Assert.That(response.Message, Does.Contain("no compiled code"));
                Assert.That(response.Message, Does.Not.Contain("line map"));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.IntroducedTypeResolveFailureNextAction));
            }
        }

        /// <summary>
        /// What: the introduced-type refusal is the explanation on its own, with no resolver
        /// sentence appended after it. A resolver sentence names a line and reads as a second,
        /// contradicting reason, which sends the caller looking for a different line number.
        /// </summary>
        [Test]
        public void Enable_WhenTheFileDeclaresAnIntroducedType_DoesNotAppendTheResolverSentence()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.IntroducedTypeSourceFiles = new HashSet<string> { IntroducedTypeFilePath };

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = IntroducedTypeFilePath,
                    Line = IntroducedTypeRequestedLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False);
                Assert.That(
                    response.Message,
                    Is.EqualTo(string.Format(
                        SourcePausePointConstants.IntroducedTypeResolveFailureMessageFormat,
                        IntroducedTypeFilePath)));
                Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.IntroducedTypeResolveFailureNextAction));
            }
        }

        /// <summary>
        /// What: a resolve failure in a file with a hot reload patch names the requested line of the
        /// file on disk, lists the nearby spans in those lines, and gives the edited-file next
        /// action without a line map warning, because --line is an edited-file line there.
        /// </summary>
        [Test]
        public void Enable_WhenResolveFailsInAPatchedFile_UsesTheEditedFileGuidanceWithoutALineMapWarning()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                string fixtureSource = ReadProjectSource(FixtureFilePath);
                scope.Port.VerifiedSnapshotSource = (file, dllPath) => fixtureSource;
                scope.Port.ShimLookupForFile = _ => CreateFixtureShimLookup(
                    FixtureStatementLine - 2,
                    FixtureClosingBraceLine);

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = FixtureFilePath,
                    Line = UnresolvableFixtureLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(
                    response.Message,
                    Does.StartWith(string.Format(
                        SourcePausePointConstants.ResolveFailedNoCompiledStatementInEditedFileMessageFormat,
                        UnresolvableFixtureLine,
                        FixtureFilePath)));
                Assert.That(response.Message, Does.Contain(SourcePausePointConstants.NearbyCompiledMethodsEditedLinesPrefix));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.ResolveFailedEditedFileRecommendedNextAction));
                Assert.That(response.Warning, Is.Null.Or.Empty);
            }
        }

        /// <summary>
        /// What: without a verified snapshot to map lines through, a resolve failure keeps the
        /// compiled resolver's sentence, the nearby spans labelled as last compiled lines, and the
        /// next action that covers the path form and a compile.
        /// </summary>
        [Test]
        public void Enable_WhenResolveFailsWithoutAVerifiedSnapshot_KeepsTheCompiledLineGuidance()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.VerifiedSnapshotSource = (file, dllPath) => null;

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = FixtureFilePath,
                    Line = UnresolvableFixtureLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(response.Message, Does.StartWith("No sequence point found on or after line 14"));
                Assert.That(response.Message, Does.Contain(SourcePausePointConstants.NearbyCompiledMethodsPrefix));
                Assert.That(
                    response.Message,
                    Does.Not.Contain(SourcePausePointConstants.NearbyCompiledMethodsEditedLinesPrefix));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.ResolveFailedRecommendedNextAction));
            }
        }

        /// <summary>
        /// What: with a verified snapshot, a line past the end of the edited file is refused as not
        /// compiled instead of reaching the compiled resolver.
        /// </summary>
        [Test]
        public void Enable_WhenLineIsBeyondTheEndOfTheFile_RefusesLineNotCompiled()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                string fixtureSource = File.ReadAllText(
                    Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), FixtureFilePath));
                scope.Port.VerifiedSnapshotSource = (file, dllPath) => fixtureSource;

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = FixtureFilePath,
                    Line = LinePastTheEndOfTheFixture,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo("PAUSE_POINT_LINE_NOT_COMPILED"));
                Assert.That(response.Message, Does.Contain("beyond the end"));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(
                        string.Format(
                            SourcePausePointConstants.LineNotCompiledBeyondEndOfFileRecommendedNextActionFormat,
                            15)));
            }
        }

        /// <summary>
        /// What: a line inside a method hot reload added is refused with a message naming that
        /// method and the compile it needs, even when the file has no patched method and so no
        /// shim lookup.
        /// </summary>
        [Test]
        public void Enable_WhenTheLineIsInsideAnAddedMethod_SaysHotReloadAddedThatMethod()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = file => null;
                scope.Port.AddedMethodContainingLine = (file, line) =>
                    line == IntroducedTypeRequestedLine
                        ? new HotReloadAddedMethodAtLine("Ns.Owner.AddedStep()", "AddedStep", "Owner", null)
                        : null;

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = IntroducedTypeFilePath,
                    Line = IntroducedTypeRequestedLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        "Line 10 is inside 'Ns.Owner.AddedStep()', which hot reload added; pause points "
                        + "cannot be armed inside added methods until 'uloop compile', so it was refused "
                        + "instead of arming another method."));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.AddedMethodResolveFailureNextAction));
            }
        }

        /// <summary>
        /// What: a line outside every added method keeps the general resolve-failure guidance, so
        /// the added-method explanation cannot swallow an ordinary wrong line.
        /// </summary>
        [Test]
        public void Enable_WhenTheLineIsOutsideEveryAddedMethod_KeepsTheGeneralResolveGuidance()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.IntroducedTypeSourceFiles = new HashSet<string>();
                scope.Port.AddedMethodContainingLine = (file, line) =>
                    line == IntroducedTypeRequestedLine + 1
                        ? new HotReloadAddedMethodAtLine("Ns.Owner.AddedStep()", "AddedStep", "Owner", null)
                        : null;

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = IntroducedTypeFilePath,
                    Line = IntroducedTypeRequestedLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(response.Message, Does.Not.Contain("which hot reload added"));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.ResolveFailedRecommendedNextAction));
            }
        }

        /// <summary>
        /// What: a file the hot-reload side does not report as declaring an introduced type keeps
        /// the general resolve-failure guidance, so the new explanation cannot swallow the old one.
        /// </summary>
        [Test]
        public void Enable_WhenTheFileDeclaresNoIntroducedType_KeepsTheGeneralResolveGuidance()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.IntroducedTypeSourceFiles = new HashSet<string>();

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = IntroducedTypeFilePath,
                    Line = 10,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(response.Message, Does.Not.Contain("introduced without a compile"));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.ResolveFailedRecommendedNextAction));
            }
        }

        /// <summary>
        /// What: a file that declares an introduced type but also holds compiled methods keeps the
        /// general resolve guidance for edited-file lines and only gains a warning about the
        /// introduced type, because "this file has no compiled code" is false for such a file.
        /// </summary>
        [Test]
        public void Enable_WhenAnIntroducedTypeSharesTheFileWithCompiledMethods_KeepsTheGeneralResolveGuidance()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                string fixtureSource = ReadProjectSource(FixtureFilePath);
                scope.Port.VerifiedSnapshotSource = (file, dllPath) => fixtureSource;
                scope.Port.IntroducedTypeSourceFiles = new HashSet<string> { FixtureFilePath };

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = FixtureFilePath,
                    Line = UnresolvableFixtureLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.ResolveFailedEditedFileRecommendedNextAction));
                Assert.That(
                    response.Warnings,
                    Does.Contain(
                        string.Format(
                            SourcePausePointConstants.IntroducedTypeInFileWarningFormat,
                            FixtureFilePath)));
            }
        }

        /// <summary>
        /// What: a line in a patched method without PDB is refused as patched by hot reload before
        /// the introduced-type explanation, so the caller is told to compile rather than to hot
        /// reload a method that is already patched.
        /// </summary>
        [Test]
        public void Enable_WhenTheIntroducedTypeFileHasAPatchedMethodWithoutPdb_RefusesAsPatchedByHotReloadBeforeTheIntroducedTypeGuidance()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.IntroducedTypeSourceFiles = new HashSet<string> { IntroducedTypeFilePath };
                scope.Port.ShimLookupForFile = _ => CreatePdbUnavailableLookup(
                    PdbUnavailableProbeMethod(),
                    IntroducedTypeRequestedLine);

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = IntroducedTypeFilePath,
                    Line = IntroducedTypeRequestedLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo("PAUSE_POINT_PATCHED_BY_HOT_RELOAD"));
                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        string.Format(
                            SourcePausePointConstants.HotReloadPatchedMethodPdbUnavailableWarningFormat,
                            "PausePointEnableGuidanceTests.PdbUnavailableProbe",
                            IntroducedTypeRequestedLine)));
                Assert.That(response.Message, Does.Not.Contain("introduced without a compile"));
                Assert.That(response.RecommendedNextAction, Does.Contain("uloop compile"));
            }
        }

        /// <summary>
        /// What: a line inside a patched method whose source changed on disk after the patch, with
        /// no reload of the file recorded, is refused with the patched-source-changed code and
        /// recovery hint, and no marker is armed.
        /// </summary>
        [Test]
        public void Enable_LineInsidePatchedMethodWhoseSourceChangedOnDisk_RefusesWithPatchedSourceChanged()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateFixtureShimLookup(
                    FixtureStatementLine - 2,
                    FixtureClosingBraceLine);
                scope.Port.ShimSourceChangedOnDisk = _ => true;
                scope.Port.LatestReloadOfFile = _ => null;

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = FixtureFilePath,
                    Line = FixtureStatementLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodePatchedSourceChanged));
                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        string.Format(
                            SourcePausePointConstants.PatchedSourceChangedOnDiskMessageFormat,
                            FixtureFilePath,
                            FixtureStatementLine)));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.PatchedSourceChangedOnDiskHint));
                Assert.That(UloopPausePointRegistry.GetActiveCount(), Is.EqualTo(0));
            }
        }

        /// <summary>
        /// What: a line outside every patched method of a file whose source changed on disk is left
        /// to the compiled resolver, so the refusal cannot swallow an unpatched line.
        /// </summary>
        [Test]
        public void Enable_LineOutsidePatchedMethodWhoseSourceChangedOnDisk_FallsThroughToTheCompiledResolver()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateFixtureShimLookup(
                    LinePastTheEndOfTheFixture - 1,
                    LinePastTheEndOfTheFixture);
                scope.Port.ShimSourceChangedOnDisk = _ => true;

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = FixtureFilePath,
                    Line = FixtureStatementLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
                Assert.That(UloopPausePointRegistry.GetActiveCount(), Is.EqualTo(1));
            }
        }

        /// <summary>
        /// What: a line above a patched method whose nearest compiled statement lies inside that
        /// method is refused with the method's edited body range, and no marker is armed.
        /// </summary>
        [Test]
        public void Enable_LineAboveAPatchedMethod_RefusesWithTheEditedBodyRange()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateFixtureShimLookup(
                    FixtureStatementLine - 1,
                    FixtureClosingBraceLine);
                scope.Port.ActiveShimForMethod = method =>
                    method.Name == nameof(EnableBySourceLocationFixture.Add) ? method : null;

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = FixtureFilePath,
                    Line = FixtureBlankLineAboveMethod,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False, response.ErrorCode + " / " + response.Message);
                Assert.That(
                    response.ErrorCode,
                    Is.EqualTo(SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload));
                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        string.Format(
                            SourcePausePointConstants.HotReloadPatchedMethodRefusalMessageFormat,
                            FixtureBlankLineAboveMethod,
                            "EnableBySourceLocationFixture.Add",
                            FixtureStatementLine - 1,
                            FixtureClosingBraceLine)));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(
                        string.Format(
                            SourcePausePointConstants.HotReloadPatchedMethodRefusalNextActionFormat,
                            FixtureBlankLineAboveMethod,
                            "EnableBySourceLocationFixture.Add",
                            FixtureStatementLine - 1,
                            FixtureClosingBraceLine)));
                Assert.That(UloopPausePointRegistry.GetActiveCount(), Is.EqualTo(0));
            }
        }

        /// <summary>
        /// What: a blank line whose next statement is inside a hot-reload patched method is refused
        /// as patched by hot reload with the method's edited range, not as line-not-compiled, so the
        /// caller is not told to hot-reload a method that is already patched, and no marker is armed.
        /// </summary>
        [Test]
        public void Enable_BlankLineWhoseNextStatementIsInsideAPatchedMethod_RefusesWithTheEditedBodyRange()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.VerifiedSnapshotSource = (file, dllPath) => CreateSnapshotBeforeAddWasEdited();
                scope.Port.ShimLookupForFile = _ => CreateFixtureShimLookup(
                    FixtureBlankLineAboveMethod + 1,
                    FixtureClosingBraceLine);

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = FixtureFilePath,
                    Line = FixtureBlankLineAboveMethod,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False, response.ErrorCode + " / " + response.Message);
                Assert.That(
                    response.ErrorCode,
                    Is.EqualTo(SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload));
                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        string.Format(
                            SourcePausePointConstants.HotReloadPatchedMethodNextStatementRefusalMessageFormat,
                            FixtureBlankLineAboveMethod,
                            FixtureBlankLineAboveMethod + 1,
                            "EnableBySourceLocationFixture.Add",
                            FixtureBlankLineAboveMethod + 1,
                            FixtureClosingBraceLine)));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(
                        string.Format(
                            SourcePausePointConstants.HotReloadPatchedMethodRefusalNextActionFormat,
                            FixtureBlankLineAboveMethod,
                            "EnableBySourceLocationFixture.Add",
                            FixtureBlankLineAboveMethod + 1,
                            FixtureClosingBraceLine)));
                Assert.That(UloopPausePointRegistry.GetActiveCount(), Is.EqualTo(0));
            }
        }

        /// <summary>
        /// What: once the file changed on disk after the hot reload, the same blank line is refused
        /// as line-not-compiled naming its next statement, not with the patched method's range,
        /// because that range is in the coordinates of the source the hot reload compiled, and
        /// hot reloading again is what refreshes it.
        /// </summary>
        [Test]
        public void Enable_BlankLineWhoseNextStatementIsInsideAPatchedMethodWhoseFileChangedOnDisk_RefusesLineNotCompiled()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.VerifiedSnapshotSource = (file, dllPath) => CreateSnapshotBeforeAddWasEdited();
                scope.Port.ShimLookupForFile = _ => CreateFixtureShimLookup(
                    FixtureBlankLineAboveMethod + 1,
                    FixtureClosingBraceLine);
                scope.Port.ShimSourceChangedOnDisk = _ => true;
                AnswerTheFileChangedSinceTheLatestReload(scope.Port);

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = FixtureFilePath,
                    Line = FixtureBlankLineAboveMethod,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False, response.ErrorCode + " / " + response.Message);
                Assert.That(response.ErrorCode, Is.EqualTo("PAUSE_POINT_LINE_NOT_COMPILED"));
                Assert.That(response.Message, Does.Contain("the next statement, line " + (FixtureBlankLineAboveMethod + 1)));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.LineNotCompiledRecommendedNextAction));
                Assert.That(UloopPausePointRegistry.GetActiveCount(), Is.EqualTo(0));
            }
        }

        /// <summary>
        /// What: the same blank line whose next statement was added after the last compile, with no
        /// patched method in the file, is still refused as line-not-compiled naming that statement.
        /// </summary>
        [Test]
        public void Enable_BlankLineWhoseNextStatementIsUncompiledOutsideEveryPatchedMethod_RefusesLineNotCompiled()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.VerifiedSnapshotSource = (file, dllPath) => CreateSnapshotBeforeAddWasEdited();

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = FixtureFilePath,
                    Line = FixtureBlankLineAboveMethod,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False, response.ErrorCode + " / " + response.Message);
                Assert.That(response.ErrorCode, Is.EqualTo("PAUSE_POINT_LINE_NOT_COMPILED"));
                Assert.That(response.Message, Does.Contain("the next statement, line " + (FixtureBlankLineAboveMethod + 1)));
                Assert.That(UloopPausePointRegistry.GetActiveCount(), Is.EqualTo(0));
            }
        }

        /// <summary>
        /// What: a line below a patched method's edited body that resolves into that method is
        /// refused with the method's edited body range, not with --method guidance, and no marker
        /// is armed.
        /// </summary>
        [Test]
        public void Enable_LineBelowAPatchedMethodThatResolvesIntoIt_RefusesWithTheEditedBodyRange()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateFixtureShimLookup(
                    FixtureStatementLine - 1,
                    FixtureStatementLine - 1);
                scope.Port.ActiveShimForMethod = method =>
                    method.Name == nameof(EnableBySourceLocationFixture.Add) ? method : null;

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = FixtureFilePath,
                    Line = FixtureClosingBraceLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False, response.ErrorCode + " / " + response.Message);
                Assert.That(
                    response.ErrorCode,
                    Is.EqualTo(SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload));
                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        string.Format(
                            SourcePausePointConstants.HotReloadPatchedMethodRefusalMessageFormat,
                            FixtureClosingBraceLine,
                            "EnableBySourceLocationFixture.Add",
                            FixtureStatementLine - 1,
                            FixtureStatementLine - 1)));
                Assert.That(response.RecommendedNextAction, Does.Not.Contain("--method"));
                Assert.That(UloopPausePointRegistry.GetActiveCount(), Is.EqualTo(0));
            }
        }

        /// <summary>
        /// What: once the file changed on disk after the hot reload, a line below the patched method
        /// that resolves into it is refused as patched-source-changed, whose next action (reload
        /// the file) records the method's edited range again, and no marker is armed.
        /// </summary>
        [Test]
        public void Enable_LineBelowAPatchedMethodWhoseFileChangedOnDisk_RefusesWithPatchedSourceChanged()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateFixtureShimLookup(
                    FixtureStatementLine - 1,
                    FixtureStatementLine - 1);
                AnswerAddIsPatched(scope.Port);
                scope.Port.ShimSourceChangedOnDisk = _ => true;
                AnswerTheFileChangedSinceTheLatestReload(scope.Port);

                PausePointResponse response = EnableFixtureLine(FixtureClosingBraceLine);

                AssertRefusal(
                    response,
                    SourcePausePointConstants.ErrorCodePatchedSourceChanged,
                    string.Format(
                        SourcePausePointConstants.PatchedSourceChangedOnDiskMessageFormat,
                        FixtureFilePath,
                        FixtureClosingBraceLine),
                    SourcePausePointConstants.PatchedSourceChangedOnDiskHint);
            }
        }

        /// <summary>
        /// What: the blank line above a patched method, which rounds into it, is refused as
        /// patched-source-changed once the file changed since the latest reload, instead of the
        /// range-less text that sent the caller into the body only to be refused again.
        /// </summary>
        [Test]
        public void Enable_LineAboveAPatchedMethodWhoseFileChangedSinceTheReload_RefusesWithPatchedSourceChanged()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateFixtureShimLookup(
                    FixtureStatementLine - 1,
                    FixtureClosingBraceLine);
                AnswerAddIsPatched(scope.Port);
                scope.Port.ShimSourceChangedOnDisk = _ => true;
                AnswerTheFileChangedSinceTheLatestReload(scope.Port);

                PausePointResponse response = EnableFixtureLine(FixtureBlankLineAboveMethod);

                AssertRefusal(
                    response,
                    SourcePausePointConstants.ErrorCodePatchedSourceChanged,
                    string.Format(
                        SourcePausePointConstants.PatchedSourceChangedOnDiskMessageFormat,
                        FixtureFilePath,
                        FixtureBlankLineAboveMethod),
                    SourcePausePointConstants.PatchedSourceChangedOnDiskHint);
            }
        }

        /// <summary>
        /// What: a line of a patched method the latest reload skipped, while that reload read the
        /// file as it is, is refused as a body an earlier reload left running, naming the Methods[]
        /// row whose Reason says what to change, for a statement and for the blank line above it.
        /// </summary>
        [TestCase(FixtureStatementLine)]
        [TestCase(FixtureBlankLineAboveMethod)]
        public void Enable_LineOfAMethodTheLastReloadSkipped_RefusesAsLeftBehind(int line)
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => null;
                AnswerAddIsPatched(scope.Port);
                scope.Port.ShimSourceChangedOnDisk = _ => false;
                AnswerLeftBehindAdd(scope.Port, HotReloadUnappliedRowKind.Skipped);

                PausePointResponse response = EnableFixtureLine(line);

                AssertRefusal(
                    response,
                    SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload,
                    LeftBehindAddMessage(line, SourcePausePointConstants.HotReloadLeftBehindSkippedVerb),
                    SourcePausePointConstants.HotReloadLeftBehindMethodRefusalNextAction);
            }
        }

        /// <summary>
        /// What: the left-behind refusal does not depend on --method: a filter that names the
        /// skipped method gets the same refusal.
        /// </summary>
        [Test]
        public void Enable_LineOfAMethodTheLastReloadSkippedWithMethodFilter_RefusesAsLeftBehind()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => null;
                AnswerAddIsPatched(scope.Port);
                scope.Port.ShimSourceChangedOnDisk = _ => false;
                AnswerLeftBehindAdd(scope.Port, HotReloadUnappliedRowKind.Skipped);

                PausePointResponse response = EnableFixtureLine(FixtureStatementLine, nameof(EnableBySourceLocationFixture.Add));

                AssertRefusal(
                    response,
                    SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload,
                    LeftBehindAddMessage(FixtureStatementLine, SourcePausePointConstants.HotReloadLeftBehindSkippedVerb),
                    SourcePausePointConstants.HotReloadLeftBehindMethodRefusalNextAction);
            }
        }

        /// <summary>
        /// What: after a reload that could not resolve the file and so built no new generation, a
        /// line of a method with a Failed row is refused as left behind with "could not apply",
        /// though the older generation still lists the method elsewhere in the file.
        /// </summary>
        [Test]
        public void Enable_LineOfAMethodTheLastReloadCouldNotResolve_RefusesAsLeftBehind()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateFixtureShimLookup(
                    LinePastTheEndOfTheFixture - 1,
                    LinePastTheEndOfTheFixture);
                AnswerAddIsPatched(scope.Port);
                scope.Port.ShimSourceChangedOnDisk = _ => true;
                AnswerLeftBehindAdd(scope.Port, HotReloadUnappliedRowKind.Failed);

                PausePointResponse response = EnableFixtureLine(FixtureStatementLine);

                AssertRefusal(
                    response,
                    SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload,
                    LeftBehindAddMessage(FixtureStatementLine, SourcePausePointConstants.HotReloadLeftBehindFailedVerb),
                    SourcePausePointConstants.HotReloadLeftBehindMethodRefusalNextAction);
            }
        }

        /// <summary>
        /// What: a line inside the older generation's span, after a reload that read the file as it
        /// is but built no new generation, skips that stale span and is refused as left behind,
        /// not as patched-source-changed, whose reload would only skip the method again.
        /// </summary>
        [Test]
        public void Enable_LineInsideAStaleSpanAfterAReloadThatBuiltNoGeneration_RefusesAsLeftBehind()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateFixtureShimLookup(
                    FixtureStatementLine - 2,
                    FixtureClosingBraceLine);
                AnswerAddIsPatched(scope.Port);
                scope.Port.ShimSourceChangedOnDisk = _ => true;
                AnswerLeftBehindAdd(scope.Port, HotReloadUnappliedRowKind.Skipped);

                PausePointResponse response = EnableFixtureLine(FixtureStatementLine);

                AssertRefusal(
                    response,
                    SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload,
                    LeftBehindAddMessage(FixtureStatementLine, SourcePausePointConstants.HotReloadLeftBehindSkippedVerb),
                    SourcePausePointConstants.HotReloadLeftBehindMethodRefusalNextAction);
            }
        }

        /// <summary>
        /// What: the same stale span after a reload that could not resolve the file is refused as
        /// left behind with "could not apply", because reloading the same contents fails the same way.
        /// </summary>
        [Test]
        public void Enable_LineInsideAStaleSpanAfterAReloadThatCouldNotResolve_RefusesAsLeftBehind()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateFixtureShimLookup(
                    FixtureStatementLine - 2,
                    FixtureClosingBraceLine);
                AnswerAddIsPatched(scope.Port);
                scope.Port.ShimSourceChangedOnDisk = _ => true;
                AnswerLeftBehindAdd(scope.Port, HotReloadUnappliedRowKind.Failed);

                PausePointResponse response = EnableFixtureLine(FixtureStatementLine);

                AssertRefusal(
                    response,
                    SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload,
                    LeftBehindAddMessage(FixtureStatementLine, SourcePausePointConstants.HotReloadLeftBehindFailedVerb),
                    SourcePausePointConstants.HotReloadLeftBehindMethodRefusalNextAction);
            }
        }

        /// <summary>
        /// What: a patched method the latest reload read and left no row at all for, while its
        /// patch is not in that reload's generation, is refused as an earlier reload's body that
        /// only a compile replaces.
        /// </summary>
        [Test]
        public void Enable_LineOfAnEarlierPatchTheLastReloadDidNotReport_RefusesWithCompile()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => null;
                AnswerAddIsPatched(scope.Port);
                scope.Port.ShimSourceChangedOnDisk = _ => false;
                AnswerTheLatestReloadReadTheFile(scope.Port);
                scope.Port.UnappliedRowForMethod = (file, method) => null;

                PausePointResponse response = EnableFixtureLine(FixtureStatementLine);

                AssertRefusal(
                    response,
                    SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload,
                    EarlierPatchAddMessage(FixtureStatementLine),
                    SourcePausePointConstants.HotReloadEarlierPatchRefusalNextAction);
            }
        }

        /// <summary>
        /// What: a line inside the older generation's span, after a reload that could not parse the
        /// file and so kept the earlier patch and built no new generation, is refused as an earlier
        /// reload's body that names the '(file)' row and offers fixing its Reason and reloading,
        /// since a compile stops on the same error.
        /// </summary>
        [Test]
        public void Enable_LineInsideAStaleSpanAfterAReloadThatCouldNotParseTheFile_NamesTheFileRow()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateFixtureShimLookup(
                    FixtureStatementLine - 2,
                    FixtureClosingBraceLine);
                AnswerAddIsPatched(scope.Port);
                scope.Port.ShimSourceChangedOnDisk = _ => true;
                AnswerTheLatestReloadReadTheFile(
                    scope.Port,
                    new HotReloadUnappliedRow("(file)", HotReloadUnappliedRowKind.Failed));
                scope.Port.UnappliedRowForMethod = (file, method) => null;

                PausePointResponse response = EnableFixtureLine(FixtureStatementLine);

                AssertRefusal(
                    response,
                    SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload,
                    EarlierPatchLeftRowsAddMessage(FixtureStatementLine, "'(file)' (could not apply)"),
                    SourcePausePointConstants.HotReloadEarlierPatchLeftRowsRefusalNextAction);
            }
        }

        /// <summary>
        /// What: an earlier patch after a reload that left rows only for other labels lists every
        /// one of them in reported order, because a failing sibling keeps the whole file unapplied
        /// and the method's own row may carry another spelling of its parameter types.
        /// </summary>
        [Test]
        public void Enable_LineOfAnEarlierPatchAfterAReloadThatLeftOtherRows_ListsThemInOrder()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => null;
                AnswerAddIsPatched(scope.Port);
                scope.Port.ShimSourceChangedOnDisk = _ => false;
                AnswerTheLatestReloadReadTheFile(
                    scope.Port,
                    new HotReloadUnappliedRow("Fixture.Sibling()", HotReloadUnappliedRowKind.Failed),
                    new HotReloadUnappliedRow("Fixture.Survivor()", HotReloadUnappliedRowKind.Skipped));
                scope.Port.UnappliedRowForMethod = (file, method) => null;

                PausePointResponse response = EnableFixtureLine(FixtureStatementLine);

                AssertRefusal(
                    response,
                    SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload,
                    EarlierPatchLeftRowsAddMessage(
                        FixtureStatementLine,
                        "'Fixture.Sibling()' (could not apply), 'Fixture.Survivor()' (skipped)"),
                    SourcePausePointConstants.HotReloadEarlierPatchLeftRowsRefusalNextAction);
            }
        }

        /// <summary>
        /// What: a method in the latest generation whose edited range was not recorded keeps the
        /// range-less refusal, since its patch is that reload's, not an earlier one's.
        /// </summary>
        [Test]
        public void Enable_LineOfAPatchedMethodWithoutARecordedSpan_RefusesWithoutARange()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateFixtureShimLookup(0, 0);
                AnswerAddIsPatched(scope.Port);
                scope.Port.ShimSourceChangedOnDisk = _ => false;
                AnswerTheLatestReloadReadTheFile(scope.Port);
                scope.Port.UnappliedRowForMethod = (file, method) => null;

                PausePointResponse response = EnableFixtureLine(FixtureStatementLine);

                AssertRefusal(
                    response,
                    SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload,
                    string.Format(
                        SourcePausePointConstants.HotReloadPatchedMethodWithoutSpanRefusalMessageFormat,
                        FixtureStatementLine,
                        "EnableBySourceLocationFixture.Add"),
                    SourcePausePointConstants.HotReloadPatchedMethodWithoutSpanRefusalNextAction);
            }
        }

        /// <summary>
        /// What: with no reload of the file recorded, a patched method outside every shim lookup
        /// keeps the range-less refusal, since nothing says an earlier reload left it.
        /// </summary>
        [Test]
        public void Enable_LineOfAPatchedMethodWithNoReloadRecord_KeepsTheRangelessRefusal()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => null;
                AnswerAddIsPatched(scope.Port);
                scope.Port.ShimSourceChangedOnDisk = _ => false;
                scope.Port.LatestReloadOfFile = _ => null;
                scope.Port.UnappliedRowForMethod = (file, method) => null;

                PausePointResponse response = EnableFixtureLine(FixtureStatementLine);

                AssertRefusal(
                    response,
                    SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload,
                    string.Format(
                        SourcePausePointConstants.HotReloadPatchedMethodWithoutSpanRefusalMessageFormat,
                        FixtureStatementLine,
                        "EnableBySourceLocationFixture.Add"),
                    SourcePausePointConstants.HotReloadPatchedMethodWithoutSpanRefusalNextAction);
            }
        }

        /// <summary>
        /// What: a changed line after a reload that read the file as it is and skipped methods is
        /// refused as line-not-compiled listing those rows, with a next action that names their
        /// Reasons or a compile rather than another reload of the same contents.
        /// </summary>
        [Test]
        public void Enable_ChangedLineAfterAReloadThatSkippedMethods_ListsThemAndSuggestsCompile()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.VerifiedSnapshotSource = (file, dllPath) => CreateSnapshotBeforeAddWasEdited();
                scope.Port.ShimLookupForFile = _ => null;
                scope.Port.ShimSourceChangedOnDisk = _ => false;
                AnswerTheLatestReloadReadTheFile(
                    scope.Port,
                    new HotReloadUnappliedRow(FixtureAddRowLabel, HotReloadUnappliedRowKind.Skipped));

                PausePointResponse response = EnableFixtureLine(FixtureBlankLineAboveMethod);

                Assert.That(response.Success, Is.False, response.ErrorCode + " / " + response.Message);
                Assert.That(response.ErrorCode, Is.EqualTo("PAUSE_POINT_LINE_NOT_COMPILED"));
                Assert.That(response.Message, Does.Contain("the next statement, line " + (FixtureBlankLineAboveMethod + 1)));
                Assert.That(
                    response.Message,
                    Does.EndWith(
                        string.Format(
                            SourcePausePointConstants.LineNotCompiledLatestReloadLeftRowsSuffixFormat,
                            "'" + FixtureAddRowLabel + "' (skipped)")));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.LineNotCompiledLatestReloadLeftRowsRecommendedNextAction));
                Assert.That(UloopPausePointRegistry.GetActiveCount(), Is.EqualTo(0));
            }
        }

        /// <summary>
        /// What: a line inside the older generation's span of a method without PDB bytes, after a
        /// reload that read the file as it is but built no new generation, is refused as left behind
        /// rather than with the no-PDB text of that stale generation.
        /// </summary>
        [Test]
        public void Enable_LineInsideAStaleSpanWithoutPdbAfterAReloadThatBuiltNoGeneration_RefusesAsLeftBehind()
        {
            string diskSource = ReadProjectSource(GuidanceTestsFilePath);
            // Split so this search literal is not itself the line it finds.
            int requestedLine = FindLineNumberContaining(diskSource, "pdb-unavailable" + "-probe-unique") + 1;
            Assert.That(requestedLine, Is.GreaterThan(1));
            MethodBase probe = PdbUnavailableProbeMethod();
            HotReloadUnappliedRow probeRow = new HotReloadUnappliedRow(
                "io.github.hatayama.UnityCliLoop.Tests.Editor.PausePointEnableGuidanceTests.PdbUnavailableProbe()",
                HotReloadUnappliedRowKind.Skipped);

            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreatePdbUnavailableLookup(probe, requestedLine);
                scope.Port.ActiveShimForMethod = method => method == probe ? method : null;
                scope.Port.ShimSourceChangedOnDisk = _ => true;
                AnswerTheLatestReloadReadTheFile(scope.Port, probeRow);
                scope.Port.UnappliedRowForMethod = (file, method) => method == probe ? probeRow : null;

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = GuidanceTestsFilePath,
                    Line = requestedLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                AssertRefusal(
                    response,
                    SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload,
                    string.Format(
                        SourcePausePointConstants.HotReloadLeftBehindMethodRefusalMessageFormat,
                        requestedLine,
                        "PausePointEnableGuidanceTests.PdbUnavailableProbe",
                        SourcePausePointConstants.HotReloadLeftBehindSkippedVerb,
                        probeRow.Label),
                    SourcePausePointConstants.HotReloadLeftBehindMethodRefusalNextAction);
            }
        }

        /// <summary>
        /// What: enable resolve-failure on a real PDB fixture appends the nearest compiled
        /// method span from that file, labelled with the line numbers of the file on disk.
        /// </summary>
        [Test]
        public void Enable_WhenResolveFails_AppendsNearbyCompiledMethodSpans()
        {
            SourcePausePointResolveResult otherMethod = SourcePausePointResolver.Resolve(CompiledMethodSpanFixtureFile, 16);
            Assert.That(otherMethod.Success, Is.True, otherMethod.ErrorMessage);
            Assert.That(otherMethod.Resolution.CompiledMethodStartLine, Is.GreaterThan(0));
            Assert.That(otherMethod.Resolution.CompiledMethodEndLine, Is.GreaterThan(0));

            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                // Why the file's own text as the snapshot: the map is then the identity, so the
                // compiled span lines above are also the expected edited-file lines.
                string fixtureSource = ReadProjectSource(CompiledMethodSpanFixtureFile);
                scope.Port.VerifiedSnapshotSource = (file, dllPath) => fixtureSource;

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = CompiledMethodSpanFixtureFile,
                    Line = 19,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                string expectedMessage =
                    string.Format(
                        SourcePausePointConstants.ResolveFailedNoCompiledStatementInEditedFileMessageFormat,
                        19,
                        CompiledMethodSpanFixtureFile)
                    + SourcePausePointConstants.NearbyCompiledMethodsEditedLinesPrefix
                    + string.Format(
                        SourcePausePointConstants.NearbyCompiledMethodSpanFormat,
                        "CompiledMethodSpanFixture.OtherMethod",
                        otherMethod.Resolution.CompiledMethodStartLine,
                        otherMethod.Resolution.CompiledMethodEndLine)
                    + ".";
                Assert.That(response.Message, Is.EqualTo(expectedMessage));
            }
        }

        /// <summary>
        /// What: a line inside a patched method with no shim PDB is a distinct Kind, not
        /// NotInPatchedMethod.
        /// </summary>
        [Test]
        public void ShimResolve_WhenLineIsInPatchedMethodButPdbBytesAreMissing_ReturnsPatchedMethodPdbUnavailable()
        {
            SourcePausePointShimResolution resolution = SourcePausePointShimResolver.Resolve(
                CreatePdbUnavailableLookup(PdbUnavailableProbeMethod(), 10),
                GuidanceTestsFilePath,
                10);

            Assert.That(
                resolution.Kind,
                Is.EqualTo(SourcePausePointShimResolveKind.PatchedMethodPdbUnavailable));
            Assert.That(
                resolution.MethodDisplayName,
                Is.EqualTo("PausePointEnableGuidanceTests.PdbUnavailableProbe"));
        }

        /// <summary>
        /// What: a line inside a patched method whose shim lookup has no PDB bytes is refused as
        /// patched by hot reload, because the compiled body no longer runs and the patch cannot be
        /// resolved to a statement.
        /// </summary>
        [Test]
        public void Enable_WhenPatchedMethodHasNoPdbBytes_RefusesAsPatchedByHotReload()
        {
            string absolutePath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                GuidanceTestsFilePath);
            string diskSource = File.ReadAllText(absolutePath);
            // Split so this search literal is not itself the line it finds.
            int requestedLine = FindLineNumberContaining(
                diskSource,
                "pdb-unavailable" + "-probe-unique") + 1;
            Assert.That(requestedLine, Is.GreaterThan(1));

            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile =
                    _ => CreatePdbUnavailableLookup(PdbUnavailableProbeMethod(), requestedLine);
                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = GuidanceTestsFilePath,
                    Line = requestedLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo("PAUSE_POINT_PATCHED_BY_HOT_RELOAD"));
                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        string.Format(
                            SourcePausePointConstants.HotReloadPatchedMethodPdbUnavailableWarningFormat,
                            "PausePointEnableGuidanceTests.PdbUnavailableProbe",
                            requestedLine)));
                Assert.That(response.RecommendedNextAction, Does.Contain("uloop compile"));
                Assert.That(response.Message, Does.Contain("cannot be placed on the running code"));
            }
        }

        internal static int PdbUnavailableProbe()
        {
            // pdb-unavailable-probe-unique
            return 1;
        }

        private static string ReadProjectSource(string projectRelativeFile)
        {
            return File.ReadAllText(Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), projectRelativeFile));
        }

        private static int FindLineNumberContaining(string source, string fragment)
        {
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains(fragment))
                {
                    return index + 1;
                }
            }

            return -1;
        }

        private static MethodBase PdbUnavailableProbeMethod()
        {
            return typeof(PausePointEnableGuidanceTests).GetMethod(
                nameof(PdbUnavailableProbe),
                BindingFlags.Static | BindingFlags.NonPublic);
        }

        // A lookup with no PDB bytes, which is what makes the shim resolver report the line as
        // inside a patched method whose PDB is unavailable.
        private static HotReloadShimFileLookup CreatePdbUnavailableLookup(MethodBase patchedMethod, int line)
        {
            HotReloadShimMethodLookup[] methods =
            {
                new HotReloadShimMethodLookup(
                    patchedMethod,
                    patchedMethod,
                    false,
                    line,
                    line)
            };
            return new HotReloadShimFileLookup(
                Array.Empty<byte>(),
                null,
                null,
                methods);
        }

        private static PausePointResponse EnableFixtureLine(int line, string method = "")
        {
            return new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureFilePath,
                Line = line,
                Method = method,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });
        }

        private static void AssertRefusal(
            PausePointResponse response,
            string expectedErrorCode,
            string expectedMessage,
            string expectedNextAction)
        {
            Assert.That(response.Success, Is.False, response.ErrorCode + " / " + response.Message);
            Assert.That(response.ErrorCode, Is.EqualTo(expectedErrorCode));
            Assert.That(response.Message, Is.EqualTo(expectedMessage));
            Assert.That(response.RecommendedNextAction, Is.EqualTo(expectedNextAction));
            Assert.That(UloopPausePointRegistry.GetActiveCount(), Is.EqualTo(0));
        }

        private static void AnswerAddIsPatched(StubHotReloadPausePointPort port)
        {
            port.ActiveShimForMethod = method =>
                method.Name == nameof(EnableBySourceLocationFixture.Add) ? method : null;
        }

        // The latest hot reload of the fixture read it as it is on disk and left these rows.
        private static void AnswerTheLatestReloadReadTheFile(
            StubHotReloadPausePointPort port,
            params HotReloadUnappliedRow[] rows)
        {
            port.LatestReloadOfFile = _ => new HotReloadLatestFileReload(false, rows);
        }

        private static void AnswerTheFileChangedSinceTheLatestReload(StubHotReloadPausePointPort port)
        {
            port.LatestReloadOfFile = _ => new HotReloadLatestFileReload(true, Array.Empty<HotReloadUnappliedRow>());
        }

        // The latest hot reload read the fixture as it is and left Add with a row of this kind,
        // while Add keeps running the patch an earlier reload applied.
        private static void AnswerLeftBehindAdd(StubHotReloadPausePointPort port, HotReloadUnappliedRowKind kind)
        {
            HotReloadUnappliedRow row = new HotReloadUnappliedRow(FixtureAddRowLabel, kind);
            AnswerTheLatestReloadReadTheFile(port, row);
            port.UnappliedRowForMethod = (file, method) =>
                method.Name == nameof(EnableBySourceLocationFixture.Add) ? row : null;
        }

        private static string LeftBehindAddMessage(int line, string verb)
        {
            return string.Format(
                SourcePausePointConstants.HotReloadLeftBehindMethodRefusalMessageFormat,
                line,
                "EnableBySourceLocationFixture.Add",
                verb,
                FixtureAddRowLabel);
        }

        private static string EarlierPatchAddMessage(int line)
        {
            return string.Format(
                SourcePausePointConstants.HotReloadEarlierPatchRefusalMessageFormat,
                line,
                "EnableBySourceLocationFixture.Add");
        }

        private static string EarlierPatchLeftRowsAddMessage(int line, string describedRows)
        {
            return string.Format(
                SourcePausePointConstants.HotReloadEarlierPatchLeftRowsRefusalMessageFormat,
                line,
                "EnableBySourceLocationFixture.Add",
                describedRows);
        }

        // Plays the last compiled source of the fixture from before an edit that inserted the
        // blank line 8, dropped a trailing comment from Add's declaration, and rewrote its two
        // statements. Add keeps its signature, so hot reload patches it in place rather than
        // adding it, and on disk the blank line 8, the declaration on line 9, and the two
        // statements on lines 11-12 have no compiled counterpart.
        private static string CreateSnapshotBeforeAddWasEdited()
        {
            string onDisk = File.ReadAllText(
                Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), FixtureFilePath));
            string snapshot = onDisk
                .Replace(
                    "\n\n        public int Add(int left, int right)",
                    "\n        public int Add(int left, int right) // sums both operands")
                .Replace("int sum = left + right;", "int result = left + right;")
                .Replace("return sum;", "return result;");
            Assert.That(snapshot, Is.Not.EqualTo(onDisk));
            return snapshot;
        }

        // The test assembly stands in for a shim assembly: its PDB maps the fixture file, so the
        // fixture method resolves as a patched method whose shim is itself, delegated so the
        // resolver arms it directly rather than through a transplant rebuild.
        private static HotReloadShimFileLookup CreateFixtureShimLookup(int sourceStartLine, int sourceEndLine)
        {
            Assembly assembly = typeof(EnableBySourceLocationFixture).Assembly;
            MethodBase fixtureMethod = typeof(EnableBySourceLocationFixture).GetMethod(
                nameof(EnableBySourceLocationFixture.Add),
                BindingFlags.Instance | BindingFlags.Public);
            HotReloadShimMethodLookup[] methods =
            {
                new HotReloadShimMethodLookup(
                    fixtureMethod,
                    fixtureMethod,
                    true,
                    sourceStartLine,
                    sourceEndLine)
            };
            return new HotReloadShimFileLookup(
                File.ReadAllBytes(assembly.Location),
                File.ReadAllBytes(Path.ChangeExtension(assembly.Location, ".pdb")),
                assembly,
                methods);
        }

        private static int IndexOfWarning(IReadOnlyList<string> warnings, string expected)
        {
            for (int index = 0; index < warnings.Count; index++)
            {
                if (warnings[index] == expected)
                {
                    return index;
                }
            }

            return -1;
        }

        private sealed class FakePausePointPauseController : IUloopPausePointPauseController
        {
            public int PauseCount { get; private set; }
            public bool IsPlaying => true;
            public bool IsPaused => PauseCount > 0;

            public void Pause()
            {
                PauseCount++;
            }

            public void Resume()
            {
                PauseCount = 0;
            }
        }
    }
}
