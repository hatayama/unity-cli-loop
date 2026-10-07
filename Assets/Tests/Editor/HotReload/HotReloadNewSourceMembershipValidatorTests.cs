using System;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the early refusals of new-source membership capture that happen before any Unity asset lookup.
    /// </summary>
    public sealed class HotReloadNewSourceMembershipValidatorTests
    {
        private const string SourcePath = "Assets/Example/NewSource.cs";

        /// <summary>
        /// Verifies that capture refuses with the Editor's not-ready reason and produces no evidence while the Editor is compiling.
        /// </summary>
        [Test]
        public void TryCapture_WhenEditorIsCompiling_ReturnsNotReadyReasonWithoutEvidence()
        {
            HotReloadStubEditorStateSnapshotCapture capture = new HotReloadStubEditorStateSnapshotCapture(
                () => new HotReloadEditorStateSnapshot(true, false, false));

            HotReloadFailureDescription failure = HotReloadNewSourceMembershipValidator.TryCapture(
                capture,
                CreateMissingProjectRoot(),
                SourcePath,
                "Example.Assembly",
                null,
                "Library/ScriptAssemblies/Example.Assembly.dll",
                out HotReloadNewSourceMembershipEvidence evidence);

            Assert.That(
                failure?.Message,
                Is.EqualTo("The Editor is compiling, so new source membership is not ready. Compile the project first and retry hot reload."));
            Assert.That(failure.Kinds, Is.EqualTo(HotReloadFailureKinds.EditorNotReady));
            Assert.That(evidence, Is.Null);
        }

        /// <summary>
        /// Verifies that capture refuses with the boundary collector's failure when the new source's directory does not exist on disk.
        /// </summary>
        [Test]
        public void TryCapture_WhenSourceDirectoryIsMissing_ReturnsBoundaryFailureWithoutEvidence()
        {
            HotReloadStubEditorStateSnapshotCapture capture = new HotReloadStubEditorStateSnapshotCapture(
                () => new HotReloadEditorStateSnapshot(false, false, false));

            HotReloadFailureDescription failure = HotReloadNewSourceMembershipValidator.TryCapture(
                capture,
                CreateMissingProjectRoot(),
                SourcePath,
                "Example.Assembly",
                null,
                "Library/ScriptAssemblies/Example.Assembly.dll",
                out HotReloadNewSourceMembershipEvidence evidence);

            Assert.That(
                failure?.Message,
                Is.EqualTo("The new source membership boundary is not available on disk. Compile the project and retry hot reload."));
            Assert.That(failure.Kinds, Is.EqualTo(HotReloadFailureKinds.Declaration));
            Assert.That(evidence, Is.Null);
        }

        // A unique path that is never created, so the directory check fails without touching the disk.
        private static string CreateMissingProjectRoot()
        {
            return Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
        }
    }
}
