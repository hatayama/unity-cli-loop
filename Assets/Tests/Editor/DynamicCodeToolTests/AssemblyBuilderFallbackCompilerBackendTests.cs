using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Guards how the AssemblyBuilder fallback waits for a build that may never report completion.
    /// </summary>
    [TestFixture]
    public sealed class AssemblyBuilderFallbackCompilerBackendTests
    {
        /// <summary>
        /// Verifies that a cancelled wait returns with a cancellation even when the build never reports
        /// that it finished, instead of holding the dynamic-code execution forever.
        /// </summary>
        [Test]
        public void AwaitBuildCompletionAsync_WhenCancelledAndBuildNeverFinishes_EndsWithCancellation()
        {
            TaskCompletionSource<CompilerMessage[]> neverFinishingBuild = new TaskCompletionSource<CompilerMessage[]>();
            using CancellationTokenSource cancellation = new CancellationTokenSource();

            Task<CompilerMessage[]> waiting =
                AssemblyBuilderFallbackCompilerBackend.AwaitBuildCompletionAsync(neverFinishingBuild.Task, cancellation.Token);
            cancellation.Cancel();
            bool completed = ((IAsyncResult)waiting).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));

            Assert.That(completed, Is.True, "The wait never ended after the cancellation.");
            Assert.That(waiting.IsCanceled, Is.True);
        }
    }
}
