using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure ledger coverage for added-field ReplaceForFile, type lookup, nested-type keys,
    /// aggregation, and ClearAll.
    /// </summary>
    public class HotReloadAddedFieldRegistryTests
    {
        private const string FileOne = "Assets/Tests/Editor/HotReload/FileOne.cs";
        private const string FileTwo = "Assets/Tests/Editor/HotReload/FileTwo.cs";
        private const string HostType = "Ns.Host";
        private const string NestedCecilType = "Ns.Outer/Inner";
        private const string NestedReflectionType = "Ns.Outer+Inner";

        [TearDown]
        public void TearDown()
        {
            HotReloadAddedFieldRegistry.ClearAll();
        }

        /// <summary>
        /// What: ReplaceForFile overwrites that file's fields so a second write drops names
        /// the new list no longer contains.
        /// </summary>
        [Test]
        public void ReplaceForFile_ReplacesThatFile()
        {
            HotReloadAddedFieldRegistry.ReplaceForFile(
                FileOne,
                new[] { HostType + ".oldField", HostType + ".keptField" });
            HotReloadAddedFieldRegistry.ReplaceForFile(
                FileOne,
                new[] { HostType + ".keptField", HostType + ".newField" });

            IReadOnlyList<string> fields = HotReloadAddedFieldRegistry.GetFieldsForType(HostType);
            Assert.That(fields, Is.EqualTo(new[] { "keptField", "newField" }));
        }

        /// <summary>
        /// What: GetFieldsForType unions simple names from every file and sorts them ordinal.
        /// </summary>
        [Test]
        public void GetFieldsForType_AggregatesAcrossFiles()
        {
            HotReloadAddedFieldRegistry.ReplaceForFile(FileOne, new[] { HostType + ".zeta" });
            HotReloadAddedFieldRegistry.ReplaceForFile(FileTwo, new[] { HostType + ".alpha" });

            IReadOnlyList<string> fields = HotReloadAddedFieldRegistry.GetFieldsForType(HostType);
            Assert.That(fields, Is.EqualTo(new[] { "alpha", "zeta" }));
        }

        /// <summary>
        /// What: a Cecil nested-type query (Outer/Inner) hits fields stored under the
        /// reflection key (Outer+Inner).
        /// </summary>
        [Test]
        public void GetFieldsForType_CecilNestedType_HitsReflectionStoredKey()
        {
            HotReloadAddedFieldRegistry.ReplaceForFile(
                FileOne,
                new[] { NestedReflectionType + ".count" });

            IReadOnlyList<string> byCecil = HotReloadAddedFieldRegistry.GetFieldsForType(NestedCecilType);
            IReadOnlyList<string> byReflection = HotReloadAddedFieldRegistry.GetFieldsForType(NestedReflectionType);
            Assert.That(byCecil, Is.EqualTo(new[] { "count" }));
            Assert.That(byReflection, Is.EqualTo(new[] { "count" }));
        }

        /// <summary>
        /// What: ClearAll drops every file so a later lookup returns empty.
        /// </summary>
        [Test]
        public void ClearAll_DropsEveryFile()
        {
            HotReloadAddedFieldRegistry.ReplaceForFile(FileOne, new[] { HostType + ".count" });
            HotReloadAddedFieldRegistry.ReplaceForFile(FileTwo, new[] { NestedReflectionType + ".label" });

            HotReloadAddedFieldRegistry.ClearAll();

            Assert.That(HotReloadAddedFieldRegistry.GetFieldsForType(HostType), Is.Empty);
            Assert.That(HotReloadAddedFieldRegistry.GetFieldsForType(NestedReflectionType), Is.Empty);
        }

        /// <summary>
        /// What: DescribeAll lists two files' fields in path, type, then field order, and
        /// drops a file replaced with an empty list.
        /// </summary>
        [Test]
        public void DescribeAll_TwoFiles_ThenEmptyReplace_DropsThatFile()
        {
            HotReloadAddedFieldRegistry.ReplaceForFile(
                FileTwo,
                new[] { HostType + ".zeta", NestedReflectionType + ".label" });
            HotReloadAddedFieldRegistry.ReplaceForFile(
                FileOne,
                new[] { HostType + ".beta", HostType + ".alpha" });

            IReadOnlyList<HotReloadAddedFieldDescription> first = HotReloadAddedFieldRegistry.DescribeAll();
            Assert.That(first.Count, Is.EqualTo(4));
            Assert.That(first[0].ProjectRelativePath, Is.EqualTo(FileOne.Replace('\\', '/')));
            Assert.That(first[0].TypeName, Is.EqualTo(HostType));
            Assert.That(first[0].FieldName, Is.EqualTo("alpha"));
            Assert.That(first[1].ProjectRelativePath, Is.EqualTo(FileOne.Replace('\\', '/')));
            Assert.That(first[1].TypeName, Is.EqualTo(HostType));
            Assert.That(first[1].FieldName, Is.EqualTo("beta"));
            Assert.That(first[2].ProjectRelativePath, Is.EqualTo(FileTwo.Replace('\\', '/')));
            Assert.That(first[2].TypeName, Is.EqualTo(HostType));
            Assert.That(first[2].FieldName, Is.EqualTo("zeta"));
            Assert.That(first[3].ProjectRelativePath, Is.EqualTo(FileTwo.Replace('\\', '/')));
            Assert.That(first[3].TypeName, Is.EqualTo(NestedReflectionType));
            Assert.That(first[3].FieldName, Is.EqualTo("label"));

            HotReloadAddedFieldRegistry.ReplaceForFile(FileOne, Array.Empty<string>());

            IReadOnlyList<HotReloadAddedFieldDescription> afterEmpty = HotReloadAddedFieldRegistry.DescribeAll();
            Assert.That(afterEmpty.Count, Is.EqualTo(2));
            Assert.That(afterEmpty[0].ProjectRelativePath, Is.EqualTo(FileTwo.Replace('\\', '/')));
            Assert.That(afterEmpty[0].TypeName, Is.EqualTo(HostType));
            Assert.That(afterEmpty[0].FieldName, Is.EqualTo("zeta"));
            Assert.That(afterEmpty[1].TypeName, Is.EqualTo(NestedReflectionType));
            Assert.That(afterEmpty[1].FieldName, Is.EqualTo("label"));
        }

        /// <summary>
        /// What: an empty ReplaceForFile deactivates that file and leaves another file intact.
        /// </summary>
        [Test]
        public void ReplaceForFile_EmptyList_RemovesOnlyThatFile()
        {
            HotReloadAddedFieldRegistry.ReplaceForFile(FileOne, new[] { HostType + ".alpha" });
            HotReloadAddedFieldRegistry.ReplaceForFile(FileTwo, new[] { HostType + ".beta" });

            HotReloadAddedFieldRegistry.ReplaceForFile(FileOne, Array.Empty<string>());

            Assert.That(HotReloadAddedFieldRegistry.GetFieldsForType(HostType), Is.EqualTo(new[] { "beta" }));
        }
    }
}
