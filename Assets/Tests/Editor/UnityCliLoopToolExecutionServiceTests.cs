using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests tool execution coordination helpers.
    /// </summary>
    public sealed class UnityCliLoopToolExecutionServiceTests
    {
        [Test]
        public void CreateBusyException_WhenDispatcherReportsBackgroundThread_DoesNotCaptureEditorState()
        {
            // Verifies busy responses can be created before the request reaches Unity's main thread.
            CapturingMainThreadDispatcher dispatcher = new CapturingMainThreadDispatcher();
            MainThreadSwitcher.RegisterService(dispatcher);

            try
            {
                UnityCliLoopToolBusyException exception =
                    UnityCliLoopToolExecutionService.CreateBusyException("running-tool", "requested-tool");

                Assert.That(exception.RunningToolName, Is.EqualTo("running-tool"));
                Assert.That(exception.RequestedToolName, Is.EqualTo("requested-tool"));
                Assert.That(exception.IsPlaying, Is.False);
                Assert.That(exception.IsPaused, Is.False);
            }
            finally
            {
                RestoreEditorMainThreadDispatcher();
            }
        }

        private static void RestoreEditorMainThreadDispatcher()
        {
            EditorMainThreadDispatcher dispatcher = new EditorMainThreadDispatcher();
            MainThreadSwitcher.RegisterService(dispatcher);
            dispatcher.Initialize();
        }

        private sealed class CapturingMainThreadDispatcher : IMainThreadDispatcher
        {
            public bool IsMainThread => false;

            public void Initialize()
            {
            }

            public void AddContinuation(System.Action continuation)
            {
                Assert.That(continuation, Is.Not.Null);
            }
        }
    }
}
