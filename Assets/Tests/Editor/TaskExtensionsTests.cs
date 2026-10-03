using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies fire-and-forget tasks report their failures instead of dropping them.
    /// </summary>
    public sealed class TaskExtensionsTests
    {
        /// <summary>
        /// Verifies that forgetting an already faulted task logs its exception.
        /// </summary>
        [Test]
        public void Forget_WhenTaskIsFaulted_LogsException()
        {
            Task faulted = Task.FromException(new InvalidOperationException("forgotten task failure"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: forgotten task failure"));

            faulted.Forget();

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// Verifies that forgetting a task that completed successfully logs nothing.
        /// </summary>
        [Test]
        public void Forget_WhenTaskCompletedSuccessfully_LogsNothing()
        {
            Task.CompletedTask.Forget();

            LogAssert.NoUnexpectedReceived();
        }
    }
}
