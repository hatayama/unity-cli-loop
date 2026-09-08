using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Tests the conversions between the three spellings of a nested type name.
    /// </summary>
    public sealed class HotReloadTypeNamesTests
    {
        /// <summary>
        /// What: a metadata name converts every nesting step to the reflection separator, so the
        /// result can be handed to Assembly.GetType or compared with Type.FullName.
        /// </summary>
        [Test]
        public void ToReflectionName_WithNestedType_ReplacesEveryNestingSeparator()
        {
            HotReloadMetadataTypeName metadataName = new HotReloadMetadataTypeName("Ns.Outer/Middle/Inner");

            HotReloadReflectionTypeName reflectionName = metadataName.ToReflectionName();

            Assert.That(reflectionName.Value, Is.EqualTo("Ns.Outer+Middle+Inner"));
        }

        /// <summary>
        /// What: a metadata name converts every nesting step to a dot for display, leaving the
        /// namespace separators it already has untouched.
        /// </summary>
        [Test]
        public void ToDisplayShortName_WithNestedType_ReadsAsCSharpSourceWould()
        {
            HotReloadMetadataTypeName metadataName = new HotReloadMetadataTypeName("Ns.Outer/Inner");

            string displayName = metadataName.ToDisplayShortName();

            Assert.That(displayName, Is.EqualTo("Ns.Outer.Inner"));
        }

        /// <summary>
        /// What: a top-level type spells the same in all three worlds, so both conversions leave
        /// it unchanged.
        /// </summary>
        [Test]
        public void Conversions_WithTopLevelType_ChangeNothing()
        {
            HotReloadMetadataTypeName metadataName = new HotReloadMetadataTypeName("Ns.TopLevel");

            Assert.That(metadataName.ToReflectionName().Value, Is.EqualTo("Ns.TopLevel"));
            Assert.That(metadataName.ToDisplayShortName(), Is.EqualTo("Ns.TopLevel"));
        }

        /// <summary>
        /// What: two names are equal only when their values match exactly, because type names are
        /// compared ordinally everywhere they are used as keys.
        /// </summary>
        [Test]
        public void Equals_ComparesTheValueOrdinally()
        {
            HotReloadMetadataTypeName name = new HotReloadMetadataTypeName("Ns.Outer/Inner");
            HotReloadMetadataTypeName same = new HotReloadMetadataTypeName("Ns.Outer/Inner");
            HotReloadMetadataTypeName differentCase = new HotReloadMetadataTypeName("ns.outer/inner");
            HotReloadMetadataTypeName differentSeparator = new HotReloadMetadataTypeName("Ns.Outer+Inner");

            Assert.That(name.Equals(same), Is.True);
            Assert.That(name.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            Assert.That(name.Equals(differentCase), Is.False);
            Assert.That(name.Equals(differentSeparator), Is.False);
        }
    }
}
