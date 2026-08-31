#nullable enable
using System;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public class InputSimulationRunInBackgroundScopeTests
    {
        [Test]
        public void Enable_Should_TemporarilyEnableRunInBackground_WhenDisabled()
        {
            // Verifies that input simulation enables background execution only while the scope is active.
            bool originalRunInBackground = UnityEngine.Application.runInBackground;

            try
            {
                UnityEngine.Application.runInBackground = false;

                using (InputSimulationRunInBackgroundScope scope = InputSimulationRunInBackgroundScope.Enable())
                {
                    Assert.IsTrue(UnityEngine.Application.runInBackground);
                }

                Assert.IsFalse(UnityEngine.Application.runInBackground);
            }
            finally
            {
                UnityEngine.Application.runInBackground = originalRunInBackground;
            }
        }

        [Test]
        public void Enable_Should_KeepRunInBackgroundEnabled_WhenAlreadyEnabled()
        {
            // Verifies that input simulation preserves projects that already run in background.
            bool originalRunInBackground = UnityEngine.Application.runInBackground;

            try
            {
                UnityEngine.Application.runInBackground = true;

                using (InputSimulationRunInBackgroundScope scope = InputSimulationRunInBackgroundScope.Enable())
                {
                    Assert.IsTrue(UnityEngine.Application.runInBackground);
                }

                Assert.IsTrue(UnityEngine.Application.runInBackground);
            }
            finally
            {
                UnityEngine.Application.runInBackground = originalRunInBackground;
            }
        }

        [Test]
        public void Dispose_Should_RestoreRunInBackground_WhenOperationFails()
        {
            // Verifies that failed input simulation cannot leave background execution enabled.
            bool originalRunInBackground = UnityEngine.Application.runInBackground;

            try
            {
                UnityEngine.Application.runInBackground = false;
                InputSimulationRunInBackgroundScope scope = InputSimulationRunInBackgroundScope.Enable();

                Assert.Throws<InvalidOperationException>(() =>
                {
                    using (scope)
                    {
                        throw new InvalidOperationException("test failure");
                    }
                });

                Assert.IsFalse(UnityEngine.Application.runInBackground);
            }
            finally
            {
                UnityEngine.Application.runInBackground = originalRunInBackground;
            }
        }
    }
}
