using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how the raycast layer mask resolver treats blank and repeated names and layer definitions with
    /// an empty name, an out-of-range index, or a duplicate name.
    /// </summary>
    public sealed class RaycastLayerMaskResolverEdgeTests
    {
        /// <summary>
        /// Verifies blank parts between commas are skipped rather than reported as unknown layers.
        /// </summary>
        [Test]
        public void Resolve_WithBlankParts_SkipsThem()
        {
            RaycastLayerMaskResolution resolution = RaycastLayerMaskResolver.Resolve(
                " ,Default, ,Water,",
                Layers(Layer("Default", 0), Layer("Water", 4)));

            Assert.That(resolution.IsValid, Is.True);
            Assert.That(resolution.LayerNames, Is.EqualTo(new[] { "Default", "Water" }));
            Assert.That(resolution.Mask, Is.EqualTo((1 << 0) | (1 << 4)));
        }

        /// <summary>
        /// Verifies a name that only matches an out-of-range layer definition is reported as unknown.
        /// </summary>
        [Test]
        public void Resolve_WithANameOnlyOnAnOutOfRangeLayer_ReportsItAsUnknown()
        {
            RaycastLayerMaskResolution resolution = RaycastLayerMaskResolver.Resolve(
                "Beyond",
                Layers(Layer("Default", 0), Layer("Beyond", 32)));

            Assert.That(resolution.IsValid, Is.False);
            Assert.That(resolution.InvalidLayerNames, Is.EqualTo(new[] { "Beyond" }));
            Assert.That(resolution.Mask, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies the list of valid layer names leaves out definitions with an empty name or an out-of-range index.
        /// </summary>
        [Test]
        public void Resolve_WithInvalidLayerDefinitions_ListsOnlyTheValidNames()
        {
            RaycastLayerMaskResolution resolution = RaycastLayerMaskResolver.Resolve(
                "",
                Layers(Layer("", 3), Layer("Negative", -1), Layer("Beyond", 32), Layer("Default", 0)));

            Assert.That(resolution.ValidLayerNames, Is.EqualTo(new[] { "Default" }));
        }

        /// <summary>
        /// Verifies a layer name defined twice resolves to the first definition.
        /// </summary>
        [Test]
        public void Resolve_WithADuplicateLayerDefinition_UsesTheFirstIndex()
        {
            RaycastLayerMaskResolution resolution = RaycastLayerMaskResolver.Resolve(
                "Water",
                Layers(Layer("Water", 4), Layer("Water", 9)));

            Assert.That(resolution.IsValid, Is.True);
            Assert.That(resolution.Mask, Is.EqualTo(1 << 4));
        }

        /// <summary>
        /// Verifies names read back from a mask leave out out-of-range layer definitions.
        /// </summary>
        [Test]
        public void CreateLayerNamesFromMask_WithAnOutOfRangeLayer_LeavesItOut()
        {
            List<string> names = RaycastLayerMaskResolver.CreateLayerNamesFromMask(
                1,
                Layers(Layer("Beyond", 32), Layer("Default", 0)));

            Assert.That(names, Is.EqualTo(new[] { "Default" }));
        }

        private static RaycastLayerDefinition Layer(string name, int index)
        {
            return new RaycastLayerDefinition { Name = name, Index = index };
        }

        private static List<RaycastLayerDefinition> Layers(params RaycastLayerDefinition[] layers)
        {
            return new List<RaycastLayerDefinition>(layers);
        }
    }
}
