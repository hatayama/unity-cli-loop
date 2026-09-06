using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for grouping the edited files of one run by the assembly they resolved to.
    /// </summary>
    public class HotReloadFileGroupPlannerTests
    {
        /// <summary>
        /// What: two files of the same assembly land in one group, in input order.
        /// </summary>
        [Test]
        public void Plan_TwoFilesOfOneAssembly_FormsOneGroup()
        {
            IReadOnlyList<HotReloadFileGroupPlan> groups = HotReloadFileGroupPlanner.Plan(
                new List<(int InputIndex, string AssemblyName, string ProjectRelativePath)>
                {
                    (0, "AssemblyA", "Assets/Scripts/First.cs"),
                    (1, "AssemblyA", "Assets/Scripts/Second.cs")
                });

            Assert.That(groups.Count, Is.EqualTo(1));
            Assert.That(groups[0].AssemblyName, Is.EqualTo("AssemblyA"));
            Assert.That(groups[0].InputIndexes, Is.EqualTo(new[] { 0, 1 }));
        }

        /// <summary>
        /// What: files of different assemblies form separate groups, ordered by the input index of
        /// the file that opened each group.
        /// </summary>
        [Test]
        public void Plan_FilesOfDifferentAssemblies_FormsGroupsInInputOrder()
        {
            IReadOnlyList<HotReloadFileGroupPlan> groups = HotReloadFileGroupPlanner.Plan(
                new List<(int InputIndex, string AssemblyName, string ProjectRelativePath)>
                {
                    (0, "AssemblyB", "Assets/Scripts/First.cs"),
                    (1, "AssemblyA", "Assets/Scripts/Second.cs"),
                    (2, "AssemblyB", "Assets/Scripts/Third.cs")
                });

            Assert.That(groups.Count, Is.EqualTo(2));
            Assert.That(groups[0].AssemblyName, Is.EqualTo("AssemblyB"));
            Assert.That(groups[0].InputIndexes, Is.EqualTo(new[] { 0, 2 }));
            Assert.That(groups[1].AssemblyName, Is.EqualTo("AssemblyA"));
            Assert.That(groups[1].InputIndexes, Is.EqualTo(new[] { 1 }));
        }

        /// <summary>
        /// What: the same file listed twice opens a second group of that assembly, because a
        /// repeated input is applied twice and one worker run cannot carry a path twice.
        /// </summary>
        [Test]
        public void Plan_RepeatedProjectRelativePath_OpensASecondGroup()
        {
            IReadOnlyList<HotReloadFileGroupPlan> groups = HotReloadFileGroupPlanner.Plan(
                new List<(int InputIndex, string AssemblyName, string ProjectRelativePath)>
                {
                    (0, "AssemblyA", "Assets/Scripts/First.cs"),
                    (1, "AssemblyA", "Assets/Scripts/Second.cs"),
                    (2, "AssemblyA", "Assets/Scripts/First.cs")
                });

            Assert.That(groups.Count, Is.EqualTo(2));
            Assert.That(groups[0].InputIndexes, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(groups[1].InputIndexes, Is.EqualTo(new[] { 2 }));
        }

        /// <summary>
        /// What: a repeated file joins a new group even when another assembly's file sits between
        /// the two occurrences, so the group order stays that of the opening files.
        /// </summary>
        [Test]
        public void Plan_RepeatedPathAcrossAnotherAssembly_KeepsGroupOrder()
        {
            IReadOnlyList<HotReloadFileGroupPlan> groups = HotReloadFileGroupPlanner.Plan(
                new List<(int InputIndex, string AssemblyName, string ProjectRelativePath)>
                {
                    (0, "AssemblyA", "Assets/Scripts/First.cs"),
                    (1, "AssemblyA", "Assets/Scripts/Second.cs"),
                    (2, "AssemblyB", "Assets/Scripts/Third.cs"),
                    (3, "AssemblyA", "Assets/Scripts/First.cs")
                });

            Assert.That(groups.Count, Is.EqualTo(3));
            Assert.That(groups[0].AssemblyName, Is.EqualTo("AssemblyA"));
            Assert.That(groups[0].InputIndexes, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(groups[1].AssemblyName, Is.EqualTo("AssemblyB"));
            Assert.That(groups[1].InputIndexes, Is.EqualTo(new[] { 2 }));
            Assert.That(groups[2].AssemblyName, Is.EqualTo("AssemblyA"));
            Assert.That(groups[2].InputIndexes, Is.EqualTo(new[] { 3 }));
        }

        /// <summary>
        /// What: an empty run produces no groups.
        /// </summary>
        [Test]
        public void Plan_NoFiles_ReturnsNoGroups()
        {
            IReadOnlyList<HotReloadFileGroupPlan> groups = HotReloadFileGroupPlanner.Plan(
                new List<(int InputIndex, string AssemblyName, string ProjectRelativePath)>());

            Assert.That(groups.Count, Is.EqualTo(0));
        }
    }
}
