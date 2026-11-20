using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NUnit.Framework;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike
{
    /// <summary>
    /// Spike S8 for hot reload: proves that a body emitted by a Harmony transpiler can call an
    /// overridden virtual base method non-virtually with OpCodes.Call on the Editor Mono runtime,
    /// which is what lets a transplanted shim body run `base.M(args)` from the patched method.
    /// </summary>
    public class HotReloadSpikeS8BaseCallRedirectTests
    {
        // Test-scoped id: sharing the production hot reload id would let this suite's
        // UnpatchAll remove real hot reload patches from the Editor domain.
        private const string HarmonyId = "io.github.hatayama.uloop.hot-reload-spike-s8";

        [TearDown]
        public void TearDown()
        {
            new Harmony(HarmonyId).UnpatchAll(HarmonyId);
        }

        /// <summary>
        /// What: a transpiled override that calls its own overridden base method with a
        /// non-virtual call runs the base body instead of recursing into itself.
        /// </summary>
        [Test]
        public void Transpiler_NonVirtualCallToOverriddenBaseMethod_RunsTheBaseBody()
        {
            PatchWithTranspiler(
                typeof(SpikeS8Derived).GetMethod(nameof(SpikeS8Derived.Virt)),
                nameof(SpikeS8Transpilers.CallBaseVirt));

            Assert.That(new SpikeS8Derived().Virt(1), Is.EqualTo(1001));
        }

        /// <summary>
        /// What: a transpiled method can call a protected base method non-virtually, running the
        /// base body rather than the derived override.
        /// </summary>
        [Test]
        public void Transpiler_NonVirtualCallToProtectedBaseMethod_RunsTheBaseBody()
        {
            PatchWithTranspiler(
                typeof(SpikeS8Derived).GetMethod(nameof(SpikeS8Derived.CallProtected)),
                nameof(SpikeS8Transpilers.CallBaseProtectedVirt));

            Assert.That(new SpikeS8Derived().CallProtected(1), Is.EqualTo(2001));
        }

        /// <summary>
        /// What: a transpiled override can call a base method declared in another assembly
        /// (System.Object.ToString) non-virtually.
        /// </summary>
        [Test]
        public void Transpiler_NonVirtualCallToOtherAssemblyBaseMethod_RunsTheBaseBody()
        {
            PatchWithTranspiler(
                typeof(SpikeS8Derived).GetMethod(nameof(SpikeS8Derived.ToString), Type.EmptyTypes),
                nameof(SpikeS8Transpilers.CallObjectToString));

            Assert.That(new SpikeS8Derived().ToString(), Is.EqualTo(typeof(SpikeS8Derived).FullName));
        }

        /// <summary>
        /// What: a transpiled override can call the base method of a constructed generic base
        /// type non-virtually.
        /// </summary>
        [Test]
        public void Transpiler_NonVirtualCallOnGenericBaseType_RunsTheBaseBody()
        {
            PatchWithTranspiler(
                typeof(SpikeS8GenericDerived).GetMethod(nameof(SpikeS8GenericDerived.Pass)),
                nameof(SpikeS8Transpilers.CallGenericBasePass));

            Assert.That(new SpikeS8GenericDerived().Pass(5), Is.EqualTo(5));
        }

        /// <summary>
        /// What: transplanting a donor body's IL and swapping only the operand of its call to a
        /// stand-in method (keeping OpCodes.Call) makes the transplanted body run the base method.
        /// </summary>
        [Test]
        public void Transpiler_OperandSwapOnDonorIl_RunsTheBaseBody()
        {
            PatchWithTranspiler(
                typeof(SpikeS8Derived).GetMethod(nameof(SpikeS8Derived.Virt)),
                nameof(SpikeS8Transpilers.TransplantDonorWithSwappedStandIn));

            Assert.That(new SpikeS8Derived().Virt(1), Is.EqualTo(1008));
        }

        private static void PatchWithTranspiler(MethodInfo targetMethod, string transpilerMethodName)
        {
            MethodInfo transpilerMethod = AccessTools.Method(typeof(SpikeS8Transpilers), transpilerMethodName);
            Assert.That(targetMethod, Is.Not.Null, "Target method not found.");
            Assert.That(transpilerMethod, Is.Not.Null, $"Transpiler not found: {transpilerMethodName}");

            Harmony harmony = new(HarmonyId);
            harmony.Patch(targetMethod, transpiler: new HarmonyMethod(transpilerMethod));
        }

        // Each transpiler discards the original instructions and emits the whole replacement body,
        // the same way the production transplant transpiler does.
        private static class SpikeS8Transpilers
        {
            public static IEnumerable<CodeInstruction> CallBaseVirt(IEnumerable<CodeInstruction> instructions)
            {
                return CallBaseWithOneArgument(typeof(SpikeS8Base).GetMethod(nameof(SpikeS8Base.Virt)));
            }

            public static IEnumerable<CodeInstruction> CallBaseProtectedVirt(IEnumerable<CodeInstruction> instructions)
            {
                return CallBaseWithOneArgument(
                    typeof(SpikeS8Base).GetMethod("ProtectedVirt", BindingFlags.Instance | BindingFlags.NonPublic));
            }

            public static IEnumerable<CodeInstruction> CallObjectToString(IEnumerable<CodeInstruction> instructions)
            {
                return new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, typeof(object).GetMethod(nameof(object.ToString), Type.EmptyTypes)),
                    new CodeInstruction(OpCodes.Ret),
                };
            }

            public static IEnumerable<CodeInstruction> CallGenericBasePass(IEnumerable<CodeInstruction> instructions)
            {
                return CallBaseWithOneArgument(typeof(SpikeS8GenericDerived).BaseType.GetMethod("Pass"));
            }

            public static IEnumerable<CodeInstruction> TransplantDonorWithSwappedStandIn(
                IEnumerable<CodeInstruction> instructions,
                ILGenerator generator)
            {
                MethodInfo donor = typeof(SpikeS8Donor).GetMethod(nameof(SpikeS8Donor.Body));
                MethodInfo standIn = typeof(SpikeS8Donor).GetMethod(nameof(SpikeS8Donor.StandIn));
                MethodInfo baseMethod = typeof(SpikeS8Base).GetMethod(nameof(SpikeS8Base.Virt));

                // Why rebind: donor locals and labels belong to the donor body, so they must be
                // redeclared on this patch's generator exactly as the production transplant does.
                List<CodeInstruction> transplanted = new(PatchProcessor.GetOriginalInstructions(donor));
                HotReloadPatchIlRebind.RebindShortFormLocals(donor, generator, transplanted);
                HotReloadPatchIlRebind.RebindLabels(generator, transplanted);
                foreach (CodeInstruction instruction in transplanted)
                {
                    if (instruction.opcode == OpCodes.Call && Equals(instruction.operand, standIn))
                    {
                        instruction.operand = baseMethod;
                    }
                }

                return transplanted;
            }

            private static IEnumerable<CodeInstruction> CallBaseWithOneArgument(MethodInfo baseMethod)
            {
                return new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Call, baseMethod),
                    new CodeInstruction(OpCodes.Ret),
                };
            }
        }
    }

    public class SpikeS8Base
    {
        public virtual int Virt(int input) { return input + 1000; }
        protected virtual int ProtectedVirt(int input) { return input + 2000; }
    }

    public class SpikeS8Derived : SpikeS8Base
    {
        // Why NoInlining on every patch target below: without it the Mono JIT can inline the
        // original body into a test method compiled before the patch, so the assertions would
        // measure inlining instead of the redirected base call.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override int Virt(int input) { return -1; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected override int ProtectedVirt(int input) { return -2; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallProtected(int input) { return -3; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override string ToString() { return "derived"; }
    }

    public class SpikeS8GenericBase<T>
    {
        public virtual T Pass(T value) { return value; }
    }

    public class SpikeS8GenericDerived : SpikeS8GenericBase<int>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override int Pass(int value) { return -4; }
    }

    public static class SpikeS8Donor
    {
        public static int StandIn(SpikeS8Derived self, int input) { throw new InvalidOperationException(); }

        public static int Body(SpikeS8Derived self, int input) { return StandIn(self, input) + 7; }
    }
}
