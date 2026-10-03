using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies the loaded-assembly type index answers empty or unknown names with empty lists rather than
    /// null, so callers can iterate the result directly.
    /// </summary>
    public sealed class AssemblyTypeIndexLookupMissTests
    {
        private const string UnknownTypeName = "NoSuchTypeForAssemblyTypeIndexLookupMissTests";

        /// <summary>
        /// Verifies a type name no loaded assembly defines has no namespaces.
        /// </summary>
        [Test]
        public void FindNamespacesForType_WithAnUnknownType_ReturnsAnEmptyList()
        {
            Assert.That(AssemblyTypeIndex.Instance.FindNamespacesForType(UnknownTypeName), Is.Empty);
        }

        /// <summary>
        /// Verifies an empty type name has no assembly locations.
        /// </summary>
        [Test]
        public void FindAssemblyLocationsForType_WithAnEmptyName_ReturnsAnEmptyList()
        {
            Assert.That(AssemblyTypeIndex.Instance.FindAssemblyLocationsForType(string.Empty), Is.Empty);
        }

        /// <summary>
        /// Verifies a type name no loaded assembly defines has no assembly locations.
        /// </summary>
        [Test]
        public void FindAssemblyLocationsForType_WithAnUnknownType_ReturnsAnEmptyList()
        {
            Assert.That(AssemblyTypeIndex.Instance.FindAssemblyLocationsForType(UnknownTypeName), Is.Empty);
        }

        /// <summary>
        /// Verifies an empty identifier has no assembly locations.
        /// </summary>
        [Test]
        public void FindAssemblyLocationsForIdentifier_WithAnEmptyIdentifier_ReturnsAnEmptyList()
        {
            Assert.That(AssemblyTypeIndex.Instance.FindAssemblyLocationsForIdentifier(string.Empty), Is.Empty);
        }
    }
}
