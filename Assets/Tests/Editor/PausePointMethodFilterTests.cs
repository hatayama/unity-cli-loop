using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies enable-pause-point --method keeps file:line resolution inside the named method.
    /// </summary>
    [TestFixture]
    public sealed class PausePointMethodFilterTests
    {
        private const string SpanFixtureFile =
            "Assets/Tests/Editor/SourcePausePointResolver/Fixtures/CompiledMethodSpanFixture.cs";

        private const string AddedScopeFixtureFile =
            "Assets/Tests/Editor/SourcePausePointResolver/Fixtures/AddedMethodScopeFixture.cs";

        // Inside AddedMethodScopeOwner.Advance, the only compiled method of that type.
        private const int OwnerAdvanceStatementLine = 9;

        // The opening brace of AddedMethodScopeHelper.Step, where a forward rounding from the
        // Owner type lands.
        private const int HelperStepOpenBraceLine = 17;

        // Inside AddedMethodScopeHelper.Step, the only compiled method named Step.
        private const int HelperStepStatementLine = 18;

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
        /// What: --method matching the compiled method arms that method.
        /// </summary>
        [Test]
        public void Enable_WhenMethodFilterMatches_ResolvesIntendedMethod()
        {
            SourcePausePointResolveResult expected = SourcePausePointResolver.Resolve(SpanFixtureFile, 9, "Target");
            Assert.That(expected.Success, Is.True, expected.ErrorMessage);

            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = SpanFixtureFile,
                Line = 9,
                Method = "Target",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            Assert.That(response.ResolvedMethod, Is.EqualTo(expected.Resolution.MethodDisplayName));
        }

        /// <summary>
        /// What: --method that has no sequence point on or after the line fails instead of
        /// arming a neighboring method, and the message lists nearby compiled spans.
        /// </summary>
        [Test]
        public void Enable_WhenMethodFilterDoesNotMatch_FailsInsteadOfArmingNeighbor()
        {
            SourcePausePointResolveResult otherMethod = SourcePausePointResolver.Resolve(SpanFixtureFile, 16);
            Assert.That(otherMethod.Success, Is.True, otherMethod.ErrorMessage);

            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = SpanFixtureFile,
                Line = 16,
                Method = "Target",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            Assert.That(response.ResolvedMethod, Is.EqualTo(string.Empty));
            string expectedMessage =
                string.Format(
                    SourcePausePointConstants.NoMethodNamedWithSequencePointMessageFormat,
                    "Target",
                    16)
                + SourcePausePointConstants.NearbyCompiledMethodsPrefix
                + string.Format(
                    SourcePausePointConstants.NearbyCompiledMethodSpanFormat,
                    "CompiledMethodSpanFixture.OtherMethod",
                    otherMethod.Resolution.CompiledMethodStartLine,
                    otherMethod.Resolution.CompiledMethodEndLine)
                + ".";
            Assert.That(response.Message, Is.EqualTo(expectedMessage));
        }

        /// <summary>
        /// What: a line that also falls inside an added method's edited range arms the compiled
        /// method --method names when that method's last compiled span holds the line, because the
        /// caller passed a last-compiled-source line as the resolve failure told them to.
        /// </summary>
        [Test]
        public void Enable_WhenTheMethodCompiledSpanHoldsALineInsideAnAddedMethod_ArmsTheCompiledMethod()
        {
            SourcePausePointResolveResult expected =
                SourcePausePointResolver.Resolve(AddedScopeFixtureFile, HelperStepStatementLine, "Step");
            Assert.That(expected.Success, Is.True, expected.ErrorMessage);

            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = file => null;
                scope.Port.AddedMethodContainingLine = (file, line) =>
                    line == HelperStepStatementLine ? "Ns.Owner.AddedStep()" : null;

                PausePointResponse response = EnableInAddedScopeFixture(HelperStepStatementLine, "Step");

                Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
                Assert.That(response.ResolvedMethod, Is.EqualTo(expected.Resolution.MethodDisplayName));
                Assert.That(response.ResolvedLine, Is.EqualTo(HelperStepStatementLine));
                Assert.That(response.LineBasis, Is.EqualTo("LastCompiledSource"));
            }
        }

        /// <summary>
        /// What: --method that names only the added method itself has no compiled span, so the
        /// line inside that added method is still refused with the added-method explanation.
        /// </summary>
        [Test]
        public void Enable_WhenTheMethodNamesOnlyTheAddedMethod_RefusesAsAddedMethod()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = file => null;
                scope.Port.AddedMethodContainingLine = (file, line) =>
                    line == HelperStepStatementLine ? "Ns.Owner.AddedStep()" : null;

                PausePointResponse response = EnableInAddedScopeFixture(HelperStepStatementLine, "AddedStep");

                AssertRefusedAsAddedMethod(response, HelperStepStatementLine, "Ns.Owner.AddedStep()");
            }
        }

        /// <summary>
        /// What: a same-named compiled method of another type later in the file does not take a
        /// line inside an added method when no compiled span of that name holds the line, so the
        /// forward rounding cannot arm the other type's method.
        /// </summary>
        [Test]
        public void Enable_WhenOnlyALaterSameNamedMethodOfAnotherTypeIsCompiled_RefusesAsAddedMethod()
        {
            SourcePausePointResolveResult forwardRounding =
                SourcePausePointResolver.Resolve(AddedScopeFixtureFile, OwnerAdvanceStatementLine, "Step");
            Assert.That(forwardRounding.Success, Is.True, forwardRounding.ErrorMessage);
            Assert.That(forwardRounding.Resolution.ResolvedLine, Is.EqualTo(HelperStepOpenBraceLine));

            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = file => null;
                scope.Port.AddedMethodContainingLine = (file, line) =>
                    line == OwnerAdvanceStatementLine ? "Ns.Owner.Step()" : null;

                PausePointResponse response = EnableInAddedScopeFixture(OwnerAdvanceStatementLine, "Step");

                AssertRefusedAsAddedMethod(response, OwnerAdvanceStatementLine, "Ns.Owner.Step()");
            }
        }

        /// <summary>
        /// What: the span holding the requested line is the one that decides, and a resolved line
        /// is accepted only while it stays inside that span.
        /// </summary>
        [Test]
        public void AddedMethodScope_AcceptsOnlyTheSpanHoldingTheLineAndResolvedLinesInsideIt()
        {
            SourcePausePointCompiledMethodSpan first = new SourcePausePointCompiledMethodSpan(5, 8);
            SourcePausePointCompiledMethodSpan second = new SourcePausePointCompiledMethodSpan(12, 15);
            SourcePausePointCompiledMethodSpan[] spans = { first, second };

            Assert.That(PausePointAddedMethodScope.FindSpanContainingLineOrNull(spans, 13), Is.SameAs(second));
            Assert.That(PausePointAddedMethodScope.FindSpanContainingLineOrNull(spans, 10), Is.Null);
            Assert.That(PausePointAddedMethodScope.FindSpanContainingLineOrNull(spans, 5), Is.SameAs(first));
            Assert.That(PausePointAddedMethodScope.IsLineInsideSpan(second, 15), Is.True);
            Assert.That(PausePointAddedMethodScope.IsLineInsideSpan(second, 16), Is.False);
            Assert.That(PausePointAddedMethodScope.IsLineInsideSpan(second, 11), Is.False);
        }

        private static PausePointResponse EnableInAddedScopeFixture(int line, string method)
        {
            return new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = AddedScopeFixtureFile,
                Line = line,
                Method = method,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });
        }

        private static void AssertRefusedAsAddedMethod(PausePointResponse response, int line, string addedMethod)
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            Assert.That(
                response.Message,
                Does.StartWith("Line " + line + " is inside '" + addedMethod + "', which hot reload added"));
            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(SourcePausePointConstants.AddedMethodResolveFailureNextAction));
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
