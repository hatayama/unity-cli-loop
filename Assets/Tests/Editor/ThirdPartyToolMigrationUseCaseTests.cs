using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the third-party tool migration use case delegates detection and apply to the migration port.
    /// </summary>
    public sealed class ThirdPartyToolMigrationUseCaseTests
    {
        private const string ProjectRoot = "<PROJECT_ROOT>";

        /// <summary>
        /// Verifies compile-error target detection returns the port's found flag and file list for the project root.
        /// </summary>
        [Test]
        public void TryDetectAutoScanTargetsFromCompileErrors_WhenPortFindsTargets_ReturnsPortTargets()
        {
            RecordingMigrationPort port = new RecordingMigrationPort();
            ThirdPartyToolMigrationUseCase useCase = new ThirdPartyToolMigrationUseCase(port);

            (bool Found, List<string> TargetFilePaths) result =
                useCase.TryDetectAutoScanTargetsFromCompileErrors(ProjectRoot);

            Assert.That(result.Found, Is.True);
            Assert.That(result.TargetFilePaths, Is.SameAs(port.DetectedTargets));
            Assert.That(port.Calls, Is.EqualTo(new[] { "Detect|<PROJECT_ROOT>" }));
        }

        /// <summary>
        /// Verifies the target existence check returns the port task for the project root and token.
        /// </summary>
        [Test]
        public void HasMigrationTargetsAsync_WhenCalled_ReturnsPortTaskForProjectRoot()
        {
            RecordingMigrationPort port = new RecordingMigrationPort();
            ThirdPartyToolMigrationUseCase useCase = new ThirdPartyToolMigrationUseCase(port);
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            Task<bool> hasTargetsTask = useCase.HasMigrationTargetsAsync(ProjectRoot, cancellationTokenSource.Token);

            Assert.That(hasTargetsTask, Is.SameAs(port.HasTargetsTask));
            Assert.That(port.Calls, Is.EqualTo(new[] { "HasTargets|<PROJECT_ROOT>" }));
            Assert.That(port.LastToken, Is.EqualTo(cancellationTokenSource.Token));
            cancellationTokenSource.Dispose();
        }

        /// <summary>
        /// Verifies applying the migration returns the port task and hands the caller's progress reporter to the port.
        /// </summary>
        [Test]
        public void ApplyMigrationAsync_WhenCalled_ReturnsPortTaskWithCallerProgress()
        {
            RecordingMigrationPort port = new RecordingMigrationPort();
            ThirdPartyToolMigrationUseCase useCase = new ThirdPartyToolMigrationUseCase(port);
            Progress<ThirdPartyToolMigrationProgress> progress = new Progress<ThirdPartyToolMigrationProgress>();

            Task<ThirdPartyToolMigrationResult> applyTask =
                useCase.ApplyMigrationAsync(ProjectRoot, progress, CancellationToken.None);

            Assert.That(applyTask, Is.SameAs(port.ApplyTask));
            Assert.That(port.LastProgress, Is.SameAs(progress));
            Assert.That(port.Calls, Is.EqualTo(new[] { "Apply|<PROJECT_ROOT>" }));
        }

        /// <summary>
        /// Test support type that records migration port calls and returns sentinel tasks.
        /// </summary>
        private sealed class RecordingMigrationPort : IThirdPartyToolMigrationPort
        {
            public List<string> Calls { get; } = new List<string>();
            public List<string> DetectedTargets { get; } = new List<string> { "Assets/Editor/SampleTool.cs" };
            public Task<bool> HasTargetsTask { get; } = Task.FromResult(true);
            public Task<ThirdPartyToolMigrationResult> ApplyTask { get; } =
                Task.FromResult(new ThirdPartyToolMigrationResult(1, 2, new[] { "Assets/Editor/SampleTool.cs" }));
            public IProgress<ThirdPartyToolMigrationProgress> LastProgress { get; private set; }
            public CancellationToken LastToken { get; private set; }

            public ThirdPartyToolMigrationPreview PreviewMigration(string projectRoot)
            {
                throw new NotSupportedException("Not used by these tests.");
            }

            public Task<ThirdPartyToolMigrationPreview> PreviewMigrationAsync(
                string projectRoot,
                IProgress<ThirdPartyToolMigrationProgress> progress,
                CancellationToken ct)
            {
                throw new NotSupportedException("Not used by these tests.");
            }

            public (bool Found, List<string> TargetFilePaths) TryDetectAutoScanTargetsFromCompileErrors(string projectRoot)
            {
                Calls.Add("Detect|" + projectRoot);
                return (true, DetectedTargets);
            }

            public Task<bool> HasMigrationTargetsAsync(string projectRoot, CancellationToken ct)
            {
                Calls.Add("HasTargets|" + projectRoot);
                LastToken = ct;
                return HasTargetsTask;
            }

            public ThirdPartyToolMigrationResult ApplyMigration(string projectRoot)
            {
                throw new NotSupportedException("Not used by these tests.");
            }

            public Task<ThirdPartyToolMigrationResult> ApplyMigrationAsync(
                string projectRoot,
                IProgress<ThirdPartyToolMigrationProgress> progress,
                CancellationToken ct)
            {
                Calls.Add("Apply|" + projectRoot);
                LastProgress = progress;
                return ApplyTask;
            }
        }
    }
}
