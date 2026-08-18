using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;

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
            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = SpanFixtureFile,
                Line = 9,
                Method = "Target",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            Assert.That(response.ResolvedMethod, Does.Contain("Target"));
            Assert.That(response.ResolvedMethod, Does.Not.Contain("OtherMethod"));
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
            Assert.That(string.IsNullOrEmpty(response.ResolvedMethod), Is.True);
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
