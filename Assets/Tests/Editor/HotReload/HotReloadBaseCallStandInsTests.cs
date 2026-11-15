using System;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Coverage for the Editor preflight on base-call stand-ins: a shim whose stand-ins all resolve
    /// to base methods of the patched type passes, and a missing base method or a delegating shim
    /// with a stand-in is refused before any patch is applied.
    /// </summary>
    public class HotReloadBaseCallStandInsTests
    {
        /// <summary>What: a transplant shim whose stand-in resolves two types up passes the preflight.</summary>
        [Test]
        public void DescribeUnresolvableCallOrNull_ResolvableStandIn_ReturnsNull()
        {
            string error = HotReloadBaseCallStandIns.DescribeUnresolvableCallOrNull(
                ShimMethod(typeof(HotReloadBaseCallHandWrittenShim), nameof(HotReloadBaseCallHandWrittenShim.CallsResolvable)),
                OriginalCompute(),
                HotReloadPatchShape.Transplant);

            Assert.That(error, Is.Null);
        }

        /// <summary>What: a stand-in naming a base method that does not exist is refused, naming that method.</summary>
        [Test]
        public void DescribeUnresolvableCallOrNull_MissingBaseMethod_NamesTheMethod()
        {
            string error = HotReloadBaseCallStandIns.DescribeUnresolvableCallOrNull(
                ShimMethod(typeof(HotReloadBaseCallHandWrittenShim), nameof(HotReloadBaseCallHandWrittenShim.CallsMissing)),
                OriginalCompute(),
                HotReloadPatchShape.Transplant);

            Assert.That(error, Is.Not.Null);
            Assert.That(error, Does.Contain("Missing"));
        }

        /// <summary>What: a delegating shim that calls a stand-in is refused, since its IL is never redirected.</summary>
        [Test]
        public void DescribeUnresolvableCallOrNull_DelegationShimWithStandIn_IsRefused()
        {
            string error = HotReloadBaseCallStandIns.DescribeUnresolvableCallOrNull(
                ShimMethod(typeof(HotReloadBaseCallHandWrittenShim), nameof(HotReloadBaseCallHandWrittenShim.CallsResolvable)),
                OriginalCompute(),
                HotReloadPatchShape.Delegation);

            Assert.That(error, Is.Not.Null);
        }

        /// <summary>What: a shim type that declares no stand-in passes the preflight whatever its patch shape.</summary>
        [Test]
        public void DescribeUnresolvableCallOrNull_ShimTypeWithoutStandIns_ReturnsNull()
        {
            string error = HotReloadBaseCallStandIns.DescribeUnresolvableCallOrNull(
                ShimMethod(typeof(HotReloadBaseCallShimWithoutStandIns), nameof(HotReloadBaseCallShimWithoutStandIns.Plain)),
                OriginalCompute(),
                HotReloadPatchShape.Delegation);

            Assert.That(error, Is.Null);
        }

        /// <summary>
        /// What: a stand-in resolves to the non-generic base method even when the base type also
        /// declares a generic overload with the same parameter types.
        /// </summary>
        [Test]
        public void DescribeUnresolvableCallOrNull_BaseWithGenericOverload_ResolvesTheNonGenericMethod()
        {
            string error = HotReloadBaseCallStandIns.DescribeUnresolvableCallOrNull(
                ShimMethod(typeof(HotReloadBaseCallOverloadShim), nameof(HotReloadBaseCallOverloadShim.CallsOverloaded)),
                typeof(HotReloadBaseCallOverloadDerived).GetMethod(nameof(HotReloadBaseCallOverloadDerived.Run)),
                HotReloadPatchShape.Transplant);

            Assert.That(error, Is.Null);
        }

        private static MethodInfo ShimMethod(Type shimType, string methodName)
        {
            MethodInfo method = shimType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "Shim method missing: " + methodName);
            return method;
        }

        private static MethodBase OriginalCompute()
        {
            return typeof(HotReloadBaseCallDerivedFixture).GetMethod(nameof(HotReloadBaseCallDerivedFixture.Compute));
        }
    }

    /// <summary>
    /// A hand-written shim shaped like the worker's output: stand-ins named by the shared scheme
    /// and shim methods that call them.
    /// </summary>
    public static class HotReloadBaseCallHandWrittenShim
    {
        public static int __uloopBaseCall__d2__Compute__0(HotReloadBaseCallDerivedFixture self, int input) { throw new InvalidOperationException(); }
        public static int __uloopBaseCall__d1__Missing__1(HotReloadBaseCallDerivedFixture self, int input) { throw new InvalidOperationException(); }
        public static int CallsResolvable(HotReloadBaseCallDerivedFixture self, int input) { return __uloopBaseCall__d2__Compute__0(self, input); }
        public static int CallsMissing(HotReloadBaseCallDerivedFixture self, int input) { return __uloopBaseCall__d1__Missing__1(self, input); }
    }

    /// <summary>A base type declaring a non-generic method and a generic overload with the same parameter types.</summary>
    public class HotReloadBaseCallOverloadBase
    {
        public virtual int Overloaded(int input) { return input; }
        public int Overloaded<T>(int input) { return input + 1; }
    }

    /// <summary>The patched type whose base declares both overloads.</summary>
    public class HotReloadBaseCallOverloadDerived : HotReloadBaseCallOverloadBase
    {
        public int Run(int input) { return input; }
    }

    /// <summary>A hand-written shim whose stand-in targets the non-generic overload one type up.</summary>
    public static class HotReloadBaseCallOverloadShim
    {
        public static int __uloopBaseCall__d1__Overloaded__0(HotReloadBaseCallOverloadDerived self, int input) { throw new InvalidOperationException(); }
        public static int CallsOverloaded(HotReloadBaseCallOverloadDerived self, int input) { return __uloopBaseCall__d1__Overloaded__0(self, input); }
    }

    /// <summary>A shim type that declares no stand-in.</summary>
    public static class HotReloadBaseCallShimWithoutStandIns
    {
        public static int Plain(HotReloadBaseCallDerivedFixture self, int input) { return input; }
    }
}
