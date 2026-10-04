using System;
using System.Threading.Tasks;

using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Awaits a task that a test expects to finish, failing the test when it ends canceled instead.
    /// Unity Test Framework records an async test that ends Canceled as passed, so without this a
    /// cancellation leaking out of the code under test would hide every assertion after the await.
    /// Tests that expect a cancellation catch it themselves and do not use these methods.
    /// </summary>
    internal static class UncanceledAwaits
    {
        internal static async Task<T> AwaitValueAsync<T>(Task<T> task)
        {
            try
            {
                return await task;
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("The awaited task was canceled instead of returning a value.");
                return default;
            }
        }

        internal static async Task AwaitCompletionAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("The awaited task was canceled instead of completing.");
            }
        }
    }
}
