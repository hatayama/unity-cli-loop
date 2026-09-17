using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers how the members hot reload added are spelled for matching against compiler messages.
    /// </summary>
    public class HotReloadActiveAddedMemberNamesTests
    {
        /// <summary>
        /// Verifies that an added method recorded under the display label the ledger uses is
        /// collected as the bare member name a compiler diagnostic quotes.
        /// </summary>
        [Test]
        public void Collect_AddedMethodLabel_HoldsTheBareMemberName()
        {
            HashSet<string> names = HotReloadActiveAddedMemberNames.Collect(
                new[] { new HotReloadAddedMemberInfo("Ns.Widget.Clear(System.Int32)", "Assets/Widget.cs", null) },
                new HotReloadAddedFieldDescription[0]);

            Assert.That(names, Is.EquivalentTo(new[] { "Clear" }));
        }

        /// <summary>
        /// Verifies that the worker spelling of the same member, which separates the type with
        /// '::', is reduced to the same bare name.
        /// </summary>
        [Test]
        public void Collect_AddedMethodWorkerKey_HoldsTheBareMemberName()
        {
            HashSet<string> names = HotReloadActiveAddedMemberNames.Collect(
                new[] { new HotReloadAddedMemberInfo("Ns.Widget::Clear()", "Assets/Widget.cs", null) },
                new HotReloadAddedFieldDescription[0]);

            Assert.That(names, Is.EquivalentTo(new[] { "Clear" }));
        }

        /// <summary>
        /// Verifies that an added property is collected under both its accessor name and the
        /// property name, because a diagnostic about the property quotes the property.
        /// </summary>
        [Test]
        public void Collect_AddedPropertyAccessor_HoldsTheAccessorAndThePropertyName()
        {
            HashSet<string> names = HotReloadActiveAddedMemberNames.Collect(
                new[] { new HotReloadAddedMemberInfo("Ns.Widget.get_Count()", "Assets/Widget.cs", null) },
                new HotReloadAddedFieldDescription[0]);

            Assert.That(names, Is.EquivalentTo(new[] { "get_Count", "Count" }));
        }

        /// <summary>
        /// Verifies that a setter accessor is reduced the same way as a getter, so a diagnostic
        /// about a write-only property is recognized too.
        /// </summary>
        [Test]
        public void Collect_AddedPropertySetter_HoldsTheAccessorAndThePropertyName()
        {
            HashSet<string> names = HotReloadActiveAddedMemberNames.Collect(
                new[] { new HotReloadAddedMemberInfo("Ns.Widget.set_Count(System.Int32)", "Assets/Widget.cs", null) },
                new HotReloadAddedFieldDescription[0]);

            Assert.That(names, Is.EquivalentTo(new[] { "set_Count", "Count" }));
        }

        /// <summary>
        /// Verifies that an added field is collected under its field name.
        /// </summary>
        [Test]
        public void Collect_AddedField_HoldsTheFieldName()
        {
            HashSet<string> names = HotReloadActiveAddedMemberNames.Collect(
                new HotReloadAddedMemberInfo[0],
                new[] { new HotReloadAddedFieldDescription("Assets/Widget.cs", "Ns.Widget", "count") });

            Assert.That(names, Is.EquivalentTo(new[] { "count" }));
        }
    }
}
