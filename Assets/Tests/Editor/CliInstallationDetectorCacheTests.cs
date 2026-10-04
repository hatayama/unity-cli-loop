using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies how the CLI installation detector caches, refreshes, and invalidates detection results.
    /// </summary>
    public sealed class CliInstallationDetectorCacheTests
    {
        private const string DetectedVersion = "3.6.0";
        private const string DetectedPath = "<PROJECT_ROOT>/bin/uloop";

        /// <summary>
        /// Verifies a refresh stores the detected version, path, and dispatcher flag and marks the check completed.
        /// </summary>
        [Test]
        public async Task RefreshCliVersionAsync_WhenDetectionSucceeds_CachesDetection()
        {
            CountingDetection detection = new(new CliInstallationDetection(DetectedVersion, DetectedPath, true));
            CliInstallationDetector detector = CreateDetector(detection);

            await detector.RefreshCliVersionAsync(CancellationToken.None);

            Assert.That(detector.IsCheckCompleted(), Is.True);
            Assert.That(detector.IsCliInstalled(), Is.True);
            Assert.That(detector.GetCachedCliVersion(), Is.EqualTo(DetectedVersion));
            Assert.That(detector.GetCachedCliExecutablePath(), Is.EqualTo(DetectedPath));
            Assert.That(detector.GetCachedCliIsDispatcher(), Is.True);
            Assert.That(detection.CallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a refresh hands the caller's token to the detection so cancelling it can stop the CLI process.
        /// </summary>
        [Test]
        public async Task RefreshCliVersionAsync_PassesTheCallerTokenToDetection()
        {
            CountingDetection detection = new(new CliInstallationDetection(DetectedVersion, DetectedPath));
            CliInstallationDetector detector = CreateDetector(detection);
            using CancellationTokenSource cancellation = new();

            await detector.RefreshCliVersionAsync(cancellation.Token);

            Assert.That(detection.ReceivedTokens, Is.EqualTo(new[] { cancellation.Token }));
        }

        /// <summary>
        /// Verifies a forced refresh hands the caller's token to the detection so cancelling it can stop the CLI process.
        /// </summary>
        [Test]
        public async Task ForceRefreshCliVersionAsync_PassesTheCallerTokenToDetection()
        {
            CountingDetection detection = new(new CliInstallationDetection(DetectedVersion, DetectedPath));
            CliInstallationDetector detector = CreateDetector(detection);
            using CancellationTokenSource cancellation = new();

            await detector.ForceRefreshCliVersionAsync(cancellation.Token);

            Assert.That(detection.ReceivedTokens, Is.EqualTo(new[] { cancellation.Token }));
        }

        /// <summary>
        /// Verifies a completed check without a detected version reports the CLI as not installed.
        /// </summary>
        [Test]
        public async Task RefreshCliVersionAsync_WhenNoVersionIsDetected_ReportsNotInstalled()
        {
            CountingDetection detection = new(new CliInstallationDetection(null, DetectedPath));
            CliInstallationDetector detector = CreateDetector(detection);

            await detector.RefreshCliVersionAsync(CancellationToken.None);

            Assert.That(detector.IsCheckCompleted(), Is.True);
            Assert.That(detector.IsCliInstalled(), Is.False);
            Assert.That(detector.GetCachedCliExecutablePath(), Is.EqualTo(DetectedPath));
            Assert.That(detector.GetCachedCliIsDispatcher(), Is.False);
        }

        /// <summary>
        /// Verifies a second refresh reuses the cached result instead of detecting again.
        /// </summary>
        [Test]
        public async Task RefreshCliVersionAsync_WhenCacheIsInitialized_DoesNotDetectAgain()
        {
            CountingDetection detection = new(new CliInstallationDetection(DetectedVersion, DetectedPath));
            CliInstallationDetector detector = CreateDetector(detection);
            await detector.RefreshCliVersionAsync(CancellationToken.None);

            await detector.RefreshCliVersionAsync(CancellationToken.None);

            Assert.That(detection.CallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a refresh requested while another refresh is pending returns without starting a second detection.
        /// </summary>
        [Test]
        public async Task RefreshCliVersionAsync_WhenRefreshIsPending_DoesNotStartSecondDetection()
        {
            PendingDetection detection = new();
            CliInstallationDetector detector = CreateDetector(detection);
            Task firstRefresh = detector.RefreshCliVersionAsync(CancellationToken.None);

            Task secondRefresh = detector.RefreshCliVersionAsync(CancellationToken.None);

            Assert.That(secondRefresh.IsCompleted, Is.True);
            Assert.That(detection.CallCount, Is.EqualTo(1));
            Assert.That(detector.IsCheckCompleted(), Is.False);

            detection.Complete(new CliInstallationDetection(DetectedVersion, DetectedPath));
            await firstRefresh;

            Assert.That(detector.GetCachedCliVersion(), Is.EqualTo(DetectedVersion));
        }

        /// <summary>
        /// Verifies a failed detection clears the in-progress flag so the next refresh detects again.
        /// </summary>
        [Test]
        public async Task RefreshCliVersionAsync_WhenDetectionThrows_AllowsNextRefresh()
        {
            ThrowOnceDetection detection = new(new CliInstallationDetection(DetectedVersion, DetectedPath));
            CliInstallationDetector detector = CreateDetector(detection);

            // Awaited in try / catch instead of Assert.ThrowsAsync, which blocks the main thread in this NUnit.
            try
            {
                await detector.RefreshCliVersionAsync(CancellationToken.None);
                Assert.Fail("Expected the detection failure to propagate.");
            }
            catch (InvalidOperationException exception)
            {
                Assert.That(exception.Message, Is.EqualTo(ThrowOnceDetection.ErrorMessage));
            }

            Assert.That(detector.IsCheckCompleted(), Is.False);

            await detector.RefreshCliVersionAsync(CancellationToken.None);

            Assert.That(detection.CallCount, Is.EqualTo(2));
            Assert.That(detector.GetCachedCliVersion(), Is.EqualTo(DetectedVersion));
        }

        /// <summary>
        /// Verifies a forced refresh detects again and replaces an already cached result.
        /// </summary>
        [Test]
        public async Task ForceRefreshCliVersionAsync_WhenCacheIsInitialized_ReplacesCachedDetection()
        {
            SequenceDetection detection = new(
                new CliInstallationDetection(DetectedVersion, DetectedPath, true),
                new CliInstallationDetection("3.7.0", "<PROJECT_ROOT>/other/uloop"));
            CliInstallationDetector detector = CreateDetector(detection);
            await detector.RefreshCliVersionAsync(CancellationToken.None);

            await detector.ForceRefreshCliVersionAsync(CancellationToken.None);

            Assert.That(detection.CallCount, Is.EqualTo(2));
            Assert.That(detector.GetCachedCliVersion(), Is.EqualTo("3.7.0"));
            Assert.That(detector.GetCachedCliExecutablePath(), Is.EqualTo("<PROJECT_ROOT>/other/uloop"));
            Assert.That(detector.GetCachedCliIsDispatcher(), Is.False);
            Assert.That(detector.IsCheckCompleted(), Is.True);
        }

        /// <summary>
        /// Verifies a forced refresh fills the cache even when no refresh ran before.
        /// </summary>
        [Test]
        public async Task ForceRefreshCliVersionAsync_WhenCacheIsEmpty_CachesDetection()
        {
            CountingDetection detection = new(new CliInstallationDetection(DetectedVersion, DetectedPath, true));
            CliInstallationDetector detector = CreateDetector(detection);

            await detector.ForceRefreshCliVersionAsync(CancellationToken.None);

            Assert.That(detector.IsCheckCompleted(), Is.True);
            Assert.That(detector.GetCachedCliVersion(), Is.EqualTo(DetectedVersion));
            Assert.That(detector.GetCachedCliIsDispatcher(), Is.True);
        }

        /// <summary>
        /// Verifies invalidating the cache hides the cached result and lets the next refresh detect again.
        /// </summary>
        [Test]
        public async Task InvalidateCache_WhenCacheIsInitialized_ClearsResultAndAllowsRefresh()
        {
            CountingDetection detection = new(new CliInstallationDetection(DetectedVersion, DetectedPath, true));
            CliInstallationDetector detector = CreateDetector(detection);
            await detector.RefreshCliVersionAsync(CancellationToken.None);

            detector.InvalidateCache();

            Assert.That(detector.IsCheckCompleted(), Is.False);
            Assert.That(detector.IsCliInstalled(), Is.False);
            Assert.That(detector.GetCachedCliVersion(), Is.Null);
            Assert.That(detector.GetCachedCliExecutablePath(), Is.Null);
            Assert.That(detector.GetCachedCliIsDispatcher(), Is.False);

            await detector.RefreshCliVersionAsync(CancellationToken.None);

            Assert.That(detection.CallCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies invalidating the cache during a pending refresh lets a new refresh start its own detection.
        /// </summary>
        [Test]
        public async Task InvalidateCache_WhenRefreshIsPending_AllowsNewRefresh()
        {
            PendingDetection detection = new();
            CliInstallationDetector detector = CreateDetector(detection);
            Task firstRefresh = detector.RefreshCliVersionAsync(CancellationToken.None);

            detector.InvalidateCache();
            Task secondRefresh = detector.RefreshCliVersionAsync(CancellationToken.None);

            Assert.That(detection.CallCount, Is.EqualTo(2));

            detection.Complete(new CliInstallationDetection(DetectedVersion, DetectedPath));
            await firstRefresh;
            await secondRefresh;

            Assert.That(detector.GetCachedCliVersion(), Is.EqualTo(DetectedVersion));
        }

        private static CliInstallationDetector CreateDetector(IFakeDetection detection)
        {
            CliInstallationDetector detector = new(new UnusedPinReader());
            detector.SetDetectionForTesting(detection.DetectAsync);
            return detector;
        }

        private interface IFakeDetection
        {
            Task<CliInstallationDetection> DetectAsync(CancellationToken ct);
        }

        private sealed class CountingDetection : IFakeDetection
        {
            private readonly CliInstallationDetection _result;

            public CountingDetection(CliInstallationDetection result)
            {
                _result = result;
            }

            public int CallCount { get; private set; }

            public List<CancellationToken> ReceivedTokens { get; } = new();

            public Task<CliInstallationDetection> DetectAsync(CancellationToken ct)
            {
                CallCount++;
                ReceivedTokens.Add(ct);
                return Task.FromResult(_result);
            }
        }

        private sealed class SequenceDetection : IFakeDetection
        {
            private readonly CliInstallationDetection[] _results;

            public SequenceDetection(params CliInstallationDetection[] results)
            {
                _results = results;
            }

            public int CallCount { get; private set; }

            public Task<CliInstallationDetection> DetectAsync(CancellationToken ct)
            {
                CliInstallationDetection result = _results[CallCount];
                CallCount++;
                return Task.FromResult(result);
            }
        }

        private sealed class ThrowOnceDetection : IFakeDetection
        {
            public const string ErrorMessage = "detection failed in this test";

            private readonly CliInstallationDetection _result;

            public ThrowOnceDetection(CliInstallationDetection result)
            {
                _result = result;
            }

            public int CallCount { get; private set; }

            public Task<CliInstallationDetection> DetectAsync(CancellationToken ct)
            {
                CallCount++;
                if (CallCount == 1)
                {
                    return Task.FromException<CliInstallationDetection>(new InvalidOperationException(ErrorMessage));
                }

                return Task.FromResult(_result);
            }
        }

        // Every call shares one task that the test completes itself, so nothing outlives the test.
        private sealed class PendingDetection : IFakeDetection
        {
            private readonly TaskCompletionSource<CliInstallationDetection> _completion = new();

            public int CallCount { get; private set; }

            public Task<CliInstallationDetection> DetectAsync(CancellationToken ct)
            {
                CallCount++;
                return _completion.Task;
            }

            public void Complete(CliInstallationDetection result)
            {
                _completion.SetResult(result);
            }
        }

        private sealed class UnusedPinReader : ICliPinReader
        {
            public CliPinLoadResult LoadPackagePin()
            {
                throw new InvalidOperationException("The cache tests never read the pin.");
            }

            public DispatcherBootstrapPinLoadResult LoadDispatcherBootstrapPin()
            {
                throw new InvalidOperationException("The cache tests never read the pin.");
            }

            public string LoadMinimumDispatcherVersionOrThrow()
            {
                throw new InvalidOperationException("The cache tests never read the pin.");
            }
        }
    }
}
