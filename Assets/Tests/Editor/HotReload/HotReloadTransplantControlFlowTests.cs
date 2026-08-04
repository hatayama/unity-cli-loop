using System.Reflection;
using System.Runtime.CompilerServices;

using HarmonyLib;
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Regression coverage for transplanting shim bodies whose IL carries branch labels
    /// and struct locals. Labels read from the shim without the patch ILGenerator are
    /// foreign to it and must be rebound, exactly like short-form locals.
    /// </summary>
    public class HotReloadTransplantControlFlowTests
    {
        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
        }

        // Why NoInlining: repo convention for patch-target fixtures — without it the
        // x64 Mono JIT can inline this tiny body into ApplyAndInvoke, so the assertions
        // would measure JIT inlining instead of patching.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Vector3 OriginalToCenter(Vector3Int position)
        {
            return Vector3.zero;
        }

        public static Vector3 ShimToCenterLoop(Vector3Int position)
        {
            Vector3 center = Vector3.zero;
            for (int i = 0; i < 1; i++)
            {
                center = new Vector3(position.x + 0.5f, position.y + 0.5f, position.z + 0.5f);
            }

            return center;
        }

        public static Vector3 ShimToCenterFieldWrites(Vector3Int position)
        {
            Vector3 center = Vector3.zero;
            for (int i = 0; i < 1; i++)
            {
                center.x = position.x + 0.5f;
                center.y = position.y + 0.5f;
                center.z = position.z + 0.5f;
            }

            return center;
        }

        public static Vector3 ShimToCenterReassign(Vector3Int position)
        {
            Vector3 center = Vector3.zero;
            center = new Vector3(position.x + 0.5f, position.y + 0.5f, position.z + 0.5f);
            return center;
        }

        public static Vector3 ShimToCenterSwitch(Vector3Int position)
        {
            Vector3 center = Vector3.zero;
            switch (position.x)
            {
                case 1:
                    center = new Vector3(position.x + 0.5f, position.y + 0.5f, position.z + 0.5f);
                    break;
                case 2:
                    center = Vector3.one;
                    break;
                case 3:
                    center = Vector3.up;
                    break;
                case 4:
                    center = Vector3.down;
                    break;
                default:
                    center = Vector3.left;
                    break;
            }

            return center;
        }

        private static Vector3 ApplyAndInvoke(string shimName)
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadTransplantControlFlowTests), nameof(OriginalToCenter));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadTransplantControlFlowTests), shimName);

            HotReloadPatchResult result = HotReloadPatcher.Apply(
                original, shim, HotReloadPatchShape.Transplant);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            return OriginalToCenter(new Vector3Int(1, 2, 3));
        }

        /// <summary>
        /// What: a struct-returning shim with a loop (backward branch) transplants and
        /// computes the edited value — the field-reported crash shape.
        /// </summary>
        [Test]
        public void Apply_StructReturnShimWithLoop_TransplantsAndComputes()
        {
            Assert.That(
                ApplyAndInvoke(nameof(ShimToCenterLoop)),
                Is.EqualTo(new Vector3(1.5f, 2.5f, 3.5f)));
        }

        /// <summary>
        /// What: control flow plus struct-local field writes (no ctor call) transplants —
        /// isolates the branch labels from the ctor-call IL shape.
        /// </summary>
        [Test]
        public void Apply_StructFieldWritesShimWithLoop_TransplantsAndComputes()
        {
            Assert.That(
                ApplyAndInvoke(nameof(ShimToCenterFieldWrites)),
                Is.EqualTo(new Vector3(1.5f, 2.5f, 3.5f)));
        }

        /// <summary>
        /// What: a single forward branch (debug-build return jump) lands on the correct
        /// target — pins against silent mis-branching when label indices happen to collide.
        /// </summary>
        [Test]
        public void Apply_StructCtorReassignShim_TransplantsAndComputes()
        {
            Assert.That(
                ApplyAndInvoke(nameof(ShimToCenterReassign)),
                Is.EqualTo(new Vector3(1.5f, 2.5f, 3.5f)));
        }

        /// <summary>
        /// What: a dense switch body transplants and picks the right arm — the shape
        /// Roslyn compiles to the multi-target switch opcode (Label[] operand).
        /// </summary>
        [Test]
        public void Apply_StructSwitchShim_TransplantsAndComputes()
        {
            Assert.That(
                ApplyAndInvoke(nameof(ShimToCenterSwitch)),
                Is.EqualTo(new Vector3(1.5f, 2.5f, 3.5f)));
        }
    }
}
