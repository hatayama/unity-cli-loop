using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using HarmonyLib;
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

        /// <summary>
        /// What: a ref stand-in is redirected to the ref overload, not to an out overload whose
        /// parameter has the same by-reference type once the base's type argument is substituted.
        /// </summary>
        [Test]
        public void RedirectToBaseMethods_BaseWithOutAndRefOverloads_PicksTheRefOverload()
        {
            MethodInfo shimMethod = ShimMethod(typeof(HotReloadBaseCallRefKindShim), nameof(HotReloadBaseCallRefKindShim.CallsByRef));
            List<CodeInstruction> transplanted = PatchProcessor.GetOriginalInstructions(shimMethod);

            HotReloadBaseCallStandIns.RedirectToBaseMethods(
                transplanted,
                shimMethod,
                typeof(HotReloadBaseCallRefKindDerived).GetMethod(nameof(HotReloadBaseCallRefKindDerived.Run)));

            MethodInfo redirected = transplanted
                .Select(instruction => instruction.operand as MethodInfo)
                .Single(method => method != null && method.DeclaringType == typeof(HotReloadBaseCallRefKindBase<int>));
            Assert.That(redirected.GetParameters()[0].IsOut, Is.False, "The ref call must not run the out overload.");
        }

        /// <summary>
        /// What: a stand-in that matches two base overloads (Pick(T) and Pick(int) on Base&lt;int&gt;) fails
        /// the preflight with a "matches more than one" message instead of picking one.
        /// </summary>
        [Test]
        public void DescribeUnresolvableCallOrNull_StandInMatchingTwoOverloads_ReportsMoreThanOneMatch()
        {
            string error = HotReloadBaseCallStandIns.DescribeUnresolvableCallOrNull(
                ShimMethod(typeof(HotReloadBaseCallCollidingShim), nameof(HotReloadBaseCallCollidingShim.CallsPick)),
                typeof(HotReloadBaseCallCollidingDerived).GetMethod(nameof(HotReloadBaseCallCollidingDerived.Run)),
                HotReloadPatchShape.Transplant);

            Assert.That(error, Does.Contain("matches more than one method"));
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

    /// <summary>
    /// A base whose out overload, declared first, takes the same int&amp; as the ref overload once
    /// T is int.
    /// </summary>
    public class HotReloadBaseCallRefKindBase<T>
    {
        public virtual int Take(out T value) { value = default; return 1; }
        public virtual int Take(ref int value) { return 2; }
    }

    /// <summary>The patched type whose base declares the out and ref overloads.</summary>
    public class HotReloadBaseCallRefKindDerived : HotReloadBaseCallRefKindBase<int>
    {
        public int Run(int input) { return input; }
    }

    /// <summary>A hand-written shim whose stand-in targets the ref overload one type up.</summary>
    public static class HotReloadBaseCallRefKindShim
    {
        public static int __uloopBaseCall__d1__Take__0(HotReloadBaseCallRefKindDerived self, ref int value) { throw new InvalidOperationException(); }
        public static int CallsByRef(HotReloadBaseCallRefKindDerived self, int input) { int value = input; return __uloopBaseCall__d1__Take__0(self, ref value); }
    }

    /// <summary>A base whose Pick overloads have the same parameter type once T is int.</summary>
    public class HotReloadBaseCallCollidingBase<T>
    {
        public virtual int Pick(T value) { return 1; }
        public virtual int Pick(int value) { return 2; }
    }

    /// <summary>The patched type whose base declares the colliding overloads.</summary>
    public class HotReloadBaseCallCollidingDerived : HotReloadBaseCallCollidingBase<int>
    {
        public int Run(int input) { return input; }
    }

    /// <summary>A hand-written shim whose stand-in matches both Pick overloads one type up.</summary>
    public static class HotReloadBaseCallCollidingShim
    {
        public static int __uloopBaseCall__d1__Pick__0(HotReloadBaseCallCollidingDerived self, int value) { throw new InvalidOperationException(); }
        public static int CallsPick(HotReloadBaseCallCollidingDerived self, int input) { return __uloopBaseCall__d1__Pick__0(self, input); }
    }

    /// <summary>A shim type that declares no stand-in.</summary>
    public static class HotReloadBaseCallShimWithoutStandIns
    {
        public static int Plain(HotReloadBaseCallDerivedFixture self, int input) { return input; }
    }
}
