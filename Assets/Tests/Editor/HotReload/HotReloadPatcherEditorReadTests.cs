using System;
using System.Reflection;
using System.Runtime.InteropServices;

using HarmonyLib;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the patcher's refusals that return before any registry write or Harmony call.
    /// </summary>
    public sealed class HotReloadPatcherEditorReadTests
    {
        /// <summary>
        /// Fails loudly if a refusal path reaches Harmony, which none of these paths may do.
        /// </summary>
        private sealed class HarmonyMustNotBeCalled : IHotReloadHarmony
        {
            public void Patch(MethodBase original, HarmonyMethod transpiler)
            {
                throw new InvalidOperationException("Harmony must not be reached on a refusal path.");
            }

            public void Unpatch(MethodBase original, HarmonyPatchType patchType, string harmonyId)
            {
                throw new InvalidOperationException("Harmony must not be reached on a refusal path.");
            }

            public void UnpatchAll(string harmonyId)
            {
                throw new InvalidOperationException("Harmony must not be reached on a refusal path.");
            }
        }

        /// <summary>An abstract method has no body for the patcher to replace.</summary>
        private abstract class AbstractPatchTarget
        {
            public abstract int Compute();
        }

        /// <summary>Methods that are only reflected over, never called.</summary>
        private static class PatchTargets
        {
            // Never called: only its missing IL body is inspected, so the library is never loaded.
            [DllImport("uloop-test-nonexistent-library")]
            internal static extern int NativeCompute();

            internal static T Echo<T>(T value)
            {
                return value;
            }

            internal static int Compute(int value)
            {
                return value + 1;
            }
        }

        /// <summary>
        /// Verifies that an abstract method is refused as unpatchable abstract.
        /// </summary>
        [Test]
        public void CheckPatchable_WhenMethodIsAbstract_ReturnsUnpatchableAbstract()
        {
            MethodInfo method = typeof(AbstractPatchTarget).GetMethod(nameof(AbstractPatchTarget.Compute));

            HotReloadPatchResult result = HotReloadPatcher.CheckPatchable(method);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(HotReloadPatchFailureReason.UnpatchableAbstract));
        }

        /// <summary>
        /// Verifies that a method without an IL body is refused as unpatchable extern.
        /// </summary>
        [Test]
        public void CheckPatchable_WhenMethodHasNoILBody_ReturnsUnpatchableExtern()
        {
            MethodInfo method = GetPatchTargetMethod(nameof(PatchTargets.NativeCompute));

            HotReloadPatchResult result = HotReloadPatcher.CheckPatchable(method);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(HotReloadPatchFailureReason.UnpatchableExtern));
        }

        /// <summary>
        /// Verifies that a generic method definition is refused as unpatchable open generic.
        /// </summary>
        [Test]
        public void CheckPatchable_WhenMethodIsOpenGeneric_ReturnsUnpatchableOpenGeneric()
        {
            MethodInfo method = GetPatchTargetMethod(nameof(PatchTargets.Echo));

            HotReloadPatchResult result = HotReloadPatcher.CheckPatchable(method);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(HotReloadPatchFailureReason.UnpatchableOpenGeneric));
        }

        /// <summary>
        /// Verifies that applying without a target method is refused as a null method.
        /// </summary>
        [Test]
        public void Apply_WhenMethodIsNull_ReturnsNullMethodFailure()
        {
            HotReloadPatcher patcher = CreatePatcher();

            HotReloadPatchResult result = patcher.Apply(
                null,
                GetPatchTargetMethod(nameof(PatchTargets.Compute)),
                HotReloadPatchShape.Transplant,
                CreateNeverGeneratedPath());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(HotReloadPatchFailureReason.NullMethod));
        }

        /// <summary>
        /// Verifies that applying without a shim method is refused as a null shim.
        /// </summary>
        [Test]
        public void Apply_WhenShimIsNull_ReturnsNullShimFailure()
        {
            HotReloadPatcher patcher = CreatePatcher();

            HotReloadPatchResult result = patcher.Apply(
                GetPatchTargetMethod(nameof(PatchTargets.Compute)),
                null,
                HotReloadPatchShape.Transplant,
                CreateNeverGeneratedPath());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(HotReloadPatchFailureReason.NullShimMethod));
        }

        /// <summary>
        /// Verifies that applying a patchable method for a file the domain has no generation for fails and names the file.
        /// </summary>
        [Test]
        public void Apply_WhenFileHasNoGeneration_ReturnsApplyFailedNamingTheFile()
        {
            HotReloadPatcher patcher = CreatePatcher();
            MethodInfo method = GetPatchTargetMethod(nameof(PatchTargets.Compute));
            string filePath = CreateNeverGeneratedPath();

            HotReloadPatchResult result = patcher.Apply(method, method, HotReloadPatchShape.Transplant, filePath);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(HotReloadPatchFailureReason.ApplyFailed));
            Assert.That(result.ErrorMessage, Does.Contain("has no hot-reload generation for '" + filePath + "'"));
        }

        private static HotReloadPatcher CreatePatcher()
        {
            return new HotReloadPatcher(HotReloadCompositionRoot.Services.Domain, new HarmonyMustNotBeCalled());
        }

        // A unique path no apply run has ever created a generation for.
        private static string CreateNeverGeneratedPath()
        {
            return "Assets/UloopNeverGenerated" + Guid.NewGuid().ToString("N") + ".cs";
        }

        private static MethodInfo GetPatchTargetMethod(string name)
        {
            return typeof(PatchTargets).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        }
    }
}
