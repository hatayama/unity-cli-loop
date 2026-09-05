using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One group of edited files that a single worker run and a single shim assembly cover:
    /// the files of a run that resolved to the same compilation assembly.
    /// </summary>
    internal sealed class HotReloadFileGroupPlan
    {
        public string AssemblyName { get; }

        /// <summary>Indexes into the run's file list, in input order.</summary>
        public IReadOnlyList<int> InputIndexes { get; }

        public HotReloadFileGroupPlan(string assemblyName, IReadOnlyList<int> inputIndexes)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be empty.");
            Debug.Assert(inputIndexes != null && inputIndexes.Count > 0, "A group must hold a file.");
            AssemblyName = assemblyName;
            InputIndexes = inputIndexes;
        }
    }

    /// <summary>
    /// Groups the edited files of one run by the assembly they resolved to, so each group can be
    /// transformed in one worker run and hosted by one shim assembly.
    /// </summary>
    internal static class HotReloadFileGroupPlanner
    {
        public static IReadOnlyList<HotReloadFileGroupPlan> Plan(
            IReadOnlyList<(int InputIndex, string AssemblyName, string ProjectRelativePath)> files)
        {
            Debug.Assert(files != null, "files must not be null.");

            List<GroupBuilder> builders = new List<GroupBuilder>();
            foreach ((int InputIndex, string AssemblyName, string ProjectRelativePath) file in files)
            {
                Debug.Assert(!string.IsNullOrEmpty(file.AssemblyName), "assemblyName must not be empty.");
                Debug.Assert(
                    !string.IsNullOrEmpty(file.ProjectRelativePath),
                    "projectRelativePath must not be empty.");

                GroupBuilder builder = FindOpenGroup(builders, file.AssemblyName, file.ProjectRelativePath);
                if (builder == null)
                {
                    builder = new GroupBuilder(file.AssemblyName);
                    builders.Add(builder);
                }

                builder.Add(file.InputIndex, file.ProjectRelativePath);
            }

            List<HotReloadFileGroupPlan> groups = new List<HotReloadFileGroupPlan>();
            foreach (GroupBuilder builder in builders)
            {
                groups.Add(builder.Build());
            }

            return groups;
        }

        // The most recent group of this assembly that can still take the file. Why the most recent
        // one only: a repeated input path is applied twice, and one worker run cannot carry the
        // same path twice, so a repeat has to open a new group rather than join an earlier one.
        private static GroupBuilder FindOpenGroup(
            List<GroupBuilder> builders,
            string assemblyName,
            string projectRelativePath)
        {
            for (int index = builders.Count - 1; index >= 0; index--)
            {
                GroupBuilder builder = builders[index];
                if (!string.Equals(builder.AssemblyName, assemblyName, StringComparison.Ordinal))
                {
                    continue;
                }

                return builder.Contains(projectRelativePath) ? null : builder;
            }

            return null;
        }

        private sealed class GroupBuilder
        {
            private readonly List<int> inputIndexes = new List<int>();
            // Why platform-aware: on Windows two spellings that differ only in case name the
            // same physical file, and one worker run cannot parse the same source twice.
            private readonly HashSet<string> projectRelativePaths =
                new HashSet<string>(HotReloadSourcePathNormalizer.ProjectRelativePathComparer());

            public string AssemblyName { get; }

            public GroupBuilder(string assemblyName)
            {
                AssemblyName = assemblyName;
            }

            public bool Contains(string projectRelativePath)
            {
                return projectRelativePaths.Contains(projectRelativePath);
            }

            public void Add(int inputIndex, string projectRelativePath)
            {
                inputIndexes.Add(inputIndex);
                projectRelativePaths.Add(projectRelativePath);
            }

            public HotReloadFileGroupPlan Build()
            {
                return new HotReloadFileGroupPlan(AssemblyName, inputIndexes);
            }
        }
    }
}
