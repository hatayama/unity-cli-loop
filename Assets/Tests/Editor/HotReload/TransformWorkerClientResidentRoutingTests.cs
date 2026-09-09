using System;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Routing of <see cref="TransformWorkerClient"/> over the resident worker host: which host
    /// outcomes are final for the caller and which one falls back to a one-shot worker process.
    /// </summary>
    public class TransformWorkerClientResidentRoutingTests
    {
        private const int DefaultTimeoutMilliseconds = 30_000;
        private const int ShortTimeoutMilliseconds = 300;

        private ScriptedChannelFactory _factory;
        private TransformWorkerHost _host;
        private bool _bootstrapFails;

        private IDisposable _hostScope;

        [SetUp]
        public void SetUp()
        {
            _factory = new ScriptedChannelFactory();
            _bootstrapFails = false;
            UseHost(DefaultTimeoutMilliseconds);
        }

        [TearDown]
        public void TearDown()
        {
            _hostScope.Dispose();
            _host.Shutdown("test teardown");
            _factory.DisposeAll();
        }

        /// <summary>
        /// A resident run that completes returns success without starting a second process.
        /// </summary>
        [Test]
        public async Task RunAsync_ResidentCompletes_ReturnsSuccessWithoutOneShot()
        {
            _factory.Enqueue(ScriptStep.Succeed);
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildE2EFixtureInput();

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files.Length, Is.EqualTo(input.sources.Length));
            Assert.That(_host.LaunchCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A worker-reported failure is final for the caller; no one-shot runs and the resident
        /// process stays alive for the next request.
        /// </summary>
        [Test]
        public async Task RunAsync_ResidentReportsWorkerFailure_ReturnsFailureWithoutOneShot()
        {
            _factory.Enqueue(ScriptStep.Fail);
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildE2EFixtureInput();

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("exited with code 1"));
            Assert.That(_host.LaunchCount, Is.EqualTo(1));
            Assert.That(_factory.Channels[0].HasExited, Is.False);
        }

        /// <summary>
        /// Two broken conversations in a row fall back to a single one-shot worker process,
        /// which serves the request from the real worker.
        /// </summary>
        [Test]
        public async Task RunAsync_ResidentRetryExhausted_FallsBackToOneShotOnce()
        {
            _factory.Enqueue(ScriptStep.Crash);
            _factory.Enqueue(ScriptStep.Crash);
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildE2EFixtureInput();

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.entries.Length, Is.GreaterThan(0));
            Assert.That(_host.LaunchCount, Is.EqualTo(2));
        }

        /// <summary>
        /// A worker that never answers ends the request as a failure instead of doubling the
        /// wait with a one-shot retry.
        /// </summary>
        [Test]
        public async Task RunAsync_ResidentTimedOut_ReturnsFailureWithoutOneShot()
        {
            UseHost(ShortTimeoutMilliseconds);
            _factory.Enqueue(ScriptStep.Hang);
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildE2EFixtureInput();

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("did not answer within"));
            Assert.That(_host.LaunchCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A bootstrap failure is reported as-is; no process is launched and no one-shot runs.
        /// </summary>
        [Test]
        public async Task RunAsync_ResidentBootstrapFailed_ReturnsFailureWithoutOneShot()
        {
            _bootstrapFails = true;
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildE2EFixtureInput();

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("bootstrap failed for test"));
            Assert.That(_host.LaunchCount, Is.EqualTo(0));
        }

        /// <summary>
        /// A resident run whose output omits the mandatory preparation fields is rejected, so the
        /// resident path meets the same output checks the one-shot path does.
        /// </summary>
        [Test]
        public async Task RunAsync_ResidentCompletesWithoutPreparationOutput_ReturnsFailure()
        {
            _factory.Enqueue(ScriptStep.Succeed);
            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                operation = "prepareIntroducedTypes",
                targetAssemblyName = "Assembly",
                targetAssemblyMvid = "mvid",
                sources = new[]
                {
                    new TransformWorkerSourceDto { projectRelativePath = "Assets/Edited.cs" }
                }
            };

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("introducedTypes"));
            Assert.That(_host.LaunchCount, Is.EqualTo(1));
        }

        private void UseHost(int responseTimeoutMilliseconds)
        {
            _host = new TransformWorkerHost(ResolveFakeTargetAsync, _factory.Start, responseTimeoutMilliseconds);
            _hostScope?.Dispose();
            _hostScope = HotReloadServicesTestScope.BeginWithWorkerHost(_host);
        }

        private Task<TransformWorkerLaunchTarget> ResolveFakeTargetAsync(CancellationToken ct)
        {
            if (_bootstrapFails)
            {
                return Task.FromResult(TransformWorkerLaunchTarget.Failure("bootstrap failed for test"));
            }

            return Task.FromResult(TransformWorkerLaunchTarget.Resolved("/worker/fake", "dotnet"));
        }
    }
}
