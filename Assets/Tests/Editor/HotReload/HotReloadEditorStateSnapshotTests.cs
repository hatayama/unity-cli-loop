using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers which Editor states stop hot reload, and whether each one passes once the Editor
    /// settles or needs the reader to fix something.
    /// </summary>
    public sealed class HotReloadEditorStateSnapshotTests
    {
        /// <summary>
        /// What: a compile in progress is the Editor not being ready, because it ends on its own.
        /// </summary>
        [Test]
        public void GetNotReadyFailure_WhenCompiling_IsEditorNotReady()
        {
            HotReloadFailureDescription failure =
                new HotReloadEditorStateSnapshot(isCompiling: true, isUpdating: false, scriptCompilationFailed: false)
                    .GetNotReadyFailure();

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.Kinds, Is.EqualTo(HotReloadFailureKinds.EditorNotReady));
            Assert.That(failure.Message, Does.Contain("compiling"));
        }

        /// <summary>
        /// What: an asset import in progress is the Editor not being ready, because it ends on its own.
        /// </summary>
        [Test]
        public void GetNotReadyFailure_WhenImporting_IsEditorNotReady()
        {
            HotReloadFailureDescription failure =
                new HotReloadEditorStateSnapshot(isCompiling: false, isUpdating: true, scriptCompilationFailed: false)
                    .GetNotReadyFailure();

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.Kinds, Is.EqualTo(HotReloadFailureKinds.EditorNotReady));
            Assert.That(failure.Message, Does.Contain("importing assets"));
        }

        /// <summary>
        /// What: a failed last compile is something the reader has to fix, because the compile
        /// errors stay until someone fixes them and waiting never clears them.
        /// </summary>
        [Test]
        public void GetNotReadyFailure_WhenTheLastCompileFailed_IsDeclaration()
        {
            HotReloadFailureDescription failure =
                new HotReloadEditorStateSnapshot(isCompiling: false, isUpdating: false, scriptCompilationFailed: true)
                    .GetNotReadyFailure();

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.Kinds, Is.EqualTo(HotReloadFailureKinds.Declaration));
            Assert.That(failure.Message, Does.Contain("Fix the compile errors"));
        }

        /// <summary>
        /// What: a compile that started after a failed one is still the Editor not being ready,
        /// because the compile in progress may clear the errors the last one left.
        /// </summary>
        [Test]
        public void GetNotReadyFailure_WhenCompilingAfterAFailedCompile_IsEditorNotReady()
        {
            HotReloadFailureDescription failure =
                new HotReloadEditorStateSnapshot(isCompiling: true, isUpdating: false, scriptCompilationFailed: true)
                    .GetNotReadyFailure();

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.Kinds, Is.EqualTo(HotReloadFailureKinds.EditorNotReady));
            Assert.That(failure.Message, Does.Contain("compiling"));
        }

        /// <summary>
        /// What: an Editor that is neither compiling, importing, nor left with compile errors stops nothing.
        /// </summary>
        [Test]
        public void GetNotReadyFailure_WhenReady_IsNull()
        {
            HotReloadFailureDescription failure =
                new HotReloadEditorStateSnapshot(isCompiling: false, isUpdating: false, scriptCompilationFailed: false)
                    .GetNotReadyFailure();

            Assert.That(failure, Is.Null);
        }

        /// <summary>
        /// What: a compile in progress refuses a request before the transform, with the reason that
        /// the domain reload after the compile would discard the reload.
        /// </summary>
        [Test]
        public void GetBusyFailure_WhenCompiling_IsEditorNotReady()
        {
            HotReloadFailureDescription failure =
                new HotReloadEditorStateSnapshot(isCompiling: true, isUpdating: false, scriptCompilationFailed: false)
                    .GetBusyFailure();

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.Kinds, Is.EqualTo(HotReloadFailureKinds.EditorNotReady));
            Assert.That(failure.Message, Is.EqualTo(HotReloadConstants.EditorCompilingBeforeTransformReason));
        }

        /// <summary>
        /// What: an asset import in progress refuses a request before the transform.
        /// </summary>
        [Test]
        public void GetBusyFailure_WhenImporting_IsEditorNotReady()
        {
            HotReloadFailureDescription failure =
                new HotReloadEditorStateSnapshot(isCompiling: false, isUpdating: true, scriptCompilationFailed: false)
                    .GetBusyFailure();

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.Kinds, Is.EqualTo(HotReloadFailureKinds.EditorNotReady));
            Assert.That(failure.Message, Is.EqualTo(HotReloadConstants.EditorImportingBeforeTransformReason));
        }

        /// <summary>
        /// What: a failed last compile is not a busy Editor: the loaded assemblies are the last good
        /// build, and waiting does not clear the errors.
        /// </summary>
        [Test]
        public void GetBusyFailure_WhenOnlyTheLastCompileFailed_IsNull()
        {
            HotReloadFailureDescription failure =
                new HotReloadEditorStateSnapshot(isCompiling: false, isUpdating: false, scriptCompilationFailed: true)
                    .GetBusyFailure();

            Assert.That(failure, Is.Null);
        }

        /// <summary>
        /// What: an idle Editor refuses nothing before the transform.
        /// </summary>
        [Test]
        public void GetBusyFailure_WhenReady_IsNull()
        {
            HotReloadFailureDescription failure =
                new HotReloadEditorStateSnapshot(isCompiling: false, isUpdating: false, scriptCompilationFailed: false)
                    .GetBusyFailure();

            Assert.That(failure, Is.Null);
        }
    }
}
