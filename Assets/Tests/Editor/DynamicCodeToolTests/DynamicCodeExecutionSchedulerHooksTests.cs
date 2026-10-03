using System.Collections.Generic;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies the scheduler hooks call each assigned hook and do nothing for hooks left unassigned.
    /// </summary>
    public sealed class DynamicCodeExecutionSchedulerHooksTests
    {
        /// <summary>
        /// Verifies every invoke method calls its assigned hook, passing the warning message through.
        /// </summary>
        [Test]
        public async Task InvokeMethods_WithAssignedHooks_CallEachHook()
        {
            List<string> calls = new List<string>();
            DynamicCodeExecutionSchedulerHooks hooks = new DynamicCodeExecutionSchedulerHooks
            {
                AfterYieldSemaphoreAcquired = () => calls.Add("yield"),
                AfterBackgroundExecutionStatePublishedAsync = () =>
                {
                    calls.Add("published");
                    return Task.CompletedTask;
                },
                AfterBusySemaphoreProbeFailedAsync = () =>
                {
                    calls.Add("busy");
                    return Task.CompletedTask;
                },
                LogWarning = message => calls.Add("warn:" + message)
            };

            hooks.InvokeAfterYieldSemaphoreAcquired();
            await hooks.InvokeAfterBackgroundExecutionStatePublishedAsync();
            await hooks.InvokeAfterBusySemaphoreProbeFailedAsync();
            hooks.InvokeLogWarning("careful");

            Assert.That(calls, Is.EqualTo(new[] { "yield", "published", "busy", "warn:careful" }));
        }

        /// <summary>
        /// Verifies every invoke method completes without a hook assigned.
        /// </summary>
        [Test]
        public void InvokeMethods_WithoutHooks_CompleteWithoutCallingAnything()
        {
            DynamicCodeExecutionSchedulerHooks hooks = new DynamicCodeExecutionSchedulerHooks();

            hooks.InvokeAfterYieldSemaphoreAcquired();
            Task published = hooks.InvokeAfterBackgroundExecutionStatePublishedAsync();
            Task busy = hooks.InvokeAfterBusySemaphoreProbeFailedAsync();
            hooks.InvokeLogWarning("ignored");

            Assert.That(published.IsCompleted, Is.True);
            Assert.That(busy.IsCompleted, Is.True);
        }
    }
}
