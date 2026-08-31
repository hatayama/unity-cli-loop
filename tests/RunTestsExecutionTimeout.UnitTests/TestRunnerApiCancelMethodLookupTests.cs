using System.Reflection;
using NUnit.Framework;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.UnitTests
{
    /// <summary>
    /// Pure unit coverage for TestRunnerApi cancel method reflection lookup.
    /// </summary>
    [TestFixture]
    public class TestRunnerApiCancelMethodLookupTests
    {
        [Test]
        public void Resolve_WhenInternalCancelAndParameterlessIsRunActiveExist_ShouldFindBoth()
        {
            // Verifies TF 1.3.9-shaped APIs (internal CancelTestRun + parameterless IsRunActive) resolve.
            (MethodInfo cancel, MethodInfo isRunActive, string log) =
                TestRunnerApiCancelMethodLookup.Resolve(typeof(Tf139ShapedApi));

            Assert.That(cancel, Is.Not.Null);
            Assert.That(isRunActive, Is.Not.Null);
            Assert.That(log, Is.Null);
            Assert.That((bool)cancel.Invoke(null, new object[] { "guid" }), Is.True);
            Assert.That((bool)isRunActive.Invoke(null, null), Is.True);
        }

        [Test]
        public void Resolve_WhenCancelIsPublic_ShouldStillFindCancelTestRun()
        {
            // Verifies TF 1.4+/Unity 6000 public CancelTestRun(string) is found with Public|NonPublic flags.
            (MethodInfo cancel, MethodInfo isRunActive, string log) =
                TestRunnerApiCancelMethodLookup.Resolve(typeof(Tf14PublicCancelApi));

            Assert.That(cancel, Is.Not.Null);
            Assert.That(isRunActive, Is.Not.Null);
            Assert.That(log, Is.Null);
        }

        [Test]
        public void Resolve_WhenOnlyGuidIsRunActiveExists_ShouldOmitIsRunActiveAndLog()
        {
            // Verifies TF 2.0 IsRunActive(string) is treated as unavailable parameterless lookup.
            (MethodInfo cancel, MethodInfo isRunActive, string log) =
                TestRunnerApiCancelMethodLookup.Resolve(typeof(Tf20GuidIsRunActiveApi));

            Assert.That(cancel, Is.Not.Null);
            Assert.That(isRunActive, Is.Null);
            Assert.That(log, Does.Contain("IsRunActive()"));
        }

        [Test]
        public void Resolve_WhenCancelMissing_ShouldReturnNullCancelAndFallbackLog()
        {
            // Verifies missing CancelTestRun falls back to Option B with a clear log.
            (MethodInfo cancel, MethodInfo isRunActive, string log) =
                TestRunnerApiCancelMethodLookup.Resolve(typeof(NoCancelApi));

            Assert.That(cancel, Is.Null);
            Assert.That(isRunActive, Is.Not.Null);
            Assert.That(log, Does.Contain("CancelTestRun(string)"));
        }

        private static class Tf139ShapedApi
        {
            internal static bool CancelTestRun(string guid)
            {
                return guid == "guid";
            }

            internal static bool IsRunActive()
            {
                return true;
            }
        }

        private static class Tf14PublicCancelApi
        {
            public static bool CancelTestRun(string guid)
            {
                return !string.IsNullOrEmpty(guid);
            }

            public static bool IsRunActive()
            {
                return false;
            }
        }

        private static class Tf20GuidIsRunActiveApi
        {
            public static bool CancelTestRun(string guid)
            {
                return !string.IsNullOrEmpty(guid);
            }

            public static bool IsRunActive(string guid)
            {
                return !string.IsNullOrEmpty(guid);
            }
        }

        private static class NoCancelApi
        {
            public static bool IsRunActive()
            {
                return false;
            }
        }
    }
}
