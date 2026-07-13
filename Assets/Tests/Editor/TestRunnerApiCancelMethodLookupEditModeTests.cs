using System.Reflection;

using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// EditMode coverage that the cancel-method lookup resolves against the real TestRunnerApi type.
    /// </summary>
    public sealed class TestRunnerApiCancelMethodLookupEditModeTests
    {
        [Test]
        public void Resolve_AgainstRealTestRunnerApi_ShouldFindCancelTestRunAndParameterlessIsRunActive()
        {
            // Verifies TF 1.3.9's real TestRunnerApi exposes CancelTestRun(string) and IsRunActive()
            // to Public|NonPublic lookup so Option A wiring does not silently fall back in this repo.
            (MethodInfo cancelTestRun, MethodInfo isRunActive, string log) =
                TestRunnerApiCancelMethodLookup.Resolve(typeof(TestRunnerApi));

            Assert.That(cancelTestRun, Is.Not.Null, "CancelTestRun(string) must resolve on TestRunnerApi");
            Assert.That(isRunActive, Is.Not.Null, "parameterless IsRunActive() must resolve on TestRunnerApi");
            Assert.That(log, Is.Null);
            Assert.That(cancelTestRun.IsStatic, Is.True);
            Assert.That(isRunActive.IsStatic, Is.True);
        }
    }
}
