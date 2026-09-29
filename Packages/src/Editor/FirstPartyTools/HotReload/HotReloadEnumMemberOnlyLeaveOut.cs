using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What a default-selection group needs to know about one of its files to decide whether to
    /// leave it out.
    /// </summary>
    internal readonly struct HotReloadLeaveOutFile
    {
        internal HotReloadLeaveOutFile(string projectRelativePath, bool isDefaultSelected, bool declaresNewType)
        {
            ProjectRelativePath = projectRelativePath;
            IsDefaultSelected = isDefaultSelected;
            DeclaresNewType = declaresNewType;
        }

        internal string ProjectRelativePath { get; }

        internal bool IsDefaultSelected { get; }

        // Why the caller passes this instead of the worker output: only the prepare run plans new
        // types, so the transform output's introduced-type fields are empty for every file.
        internal bool DeclaresNewType { get; }
    }

    /// <summary>
    /// Decides which files of a default-selection group to leave out, and builds the worker input
    /// of the rerun without them.
    /// </summary>
    internal sealed class HotReloadEnumMemberOnlyLeaveOut
    {
        // Format: project-relative path, comma-separated enum member names.
        private const string LeftOutWarningFormat =
            "Left '{0}' out of this reload: --files was omitted and the file only adds enum members "
            + "({1}), which hot reload cannot add. Keeping it would build the enum from source and skip "
            + "the added members that pass the enum to or take it from compiled code or an introduced "
            + "type. Run 'uloop compile' to add the enum members.";

        /// <summary>
        /// Returns, in file order, the default-selected files whose only change is added enum
        /// members and that a skipped row of the first run names as the source side of a split
        /// type. Leaving such a file out loses nothing hot reload could apply, and lets the added
        /// members in the other files bind to the compiled enum.
        /// </summary>
        internal IReadOnlyList<string> FindLeftOutPaths(
            IReadOnlyList<HotReloadLeaveOutFile> files,
            TransformWorkerOutputDto firstPass,
            ISet<string> activePaths)
        {
            if (files == null || files.Count == 0)
            {
                throw new ArgumentException("A group must hold a file.", nameof(files));
            }

            if (firstPass == null)
            {
                throw new ArgumentNullException(nameof(firstPass));
            }

            if (activePaths == null)
            {
                throw new ArgumentNullException(nameof(activePaths));
            }

            HashSet<string> namedSourceSideFiles = CollectNamedSourceSideFiles(firstPass.skipped);
            if (namedSourceSideFiles.Count == 0)
            {
                return Array.Empty<string>();
            }

            HotReloadWorkerRowsByFile rows = HotReloadWorkerRowsByFile.Build(firstPass, CollectPaths(files));
            List<string> leftOutPaths = new List<string>();
            foreach (HotReloadLeaveOutFile file in files)
            {
                if (!file.IsDefaultSelected)
                {
                    continue;
                }

                // Why an active file stays: the sibling plan would bring it back, and its step
                // today is a compile that keeps its patches, not leaving it out.
                if (activePaths.Contains(file.ProjectRelativePath))
                {
                    continue;
                }

                if (!namedSourceSideFiles.Contains(file.ProjectRelativePath))
                {
                    continue;
                }

                if (!OnlyAddsEnumMembers(file, rows))
                {
                    continue;
                }

                leftOutPaths.Add(file.ProjectRelativePath);
            }

            return leftOutPaths;
        }

        /// <summary>
        /// Copies the first run's input without the left-out sources. Every other field is shared,
        /// so the rerun binds the same artifacts, siblings, exclusions and labels as the first run.
        /// </summary>
        internal TransformWorkerInputDto BuildRetryInput(
            TransformWorkerInputDto firstInput,
            IReadOnlyCollection<string> leftOutPaths)
        {
            if (firstInput == null || firstInput.sources == null)
            {
                throw new ArgumentNullException(nameof(firstInput));
            }

            if (leftOutPaths == null || leftOutPaths.Count == 0)
            {
                throw new ArgumentException("A rerun must leave out a file.", nameof(leftOutPaths));
            }

            HashSet<string> leftOutSet = new HashSet<string>(leftOutPaths, StringComparer.Ordinal);
            List<TransformWorkerSourceDto> remainingSources = new List<TransformWorkerSourceDto>();
            foreach (TransformWorkerSourceDto source in firstInput.sources)
            {
                if (!leftOutSet.Remove(source.projectRelativePath))
                {
                    remainingSources.Add(source);
                }
            }

            // Why throw before the rerun: a path outside the sources, or a rerun with no source,
            // would report a different set of files than the group splices its results into.
            if (leftOutSet.Count > 0)
            {
                throw new InvalidOperationException(
                    "A left-out file must be among the first run's sources: "
                    + string.Join(", ", leftOutSet));
            }

            if (remainingSources.Count == 0)
            {
                throw new InvalidOperationException("A rerun must keep at least one source.");
            }

            return new TransformWorkerInputDto
            {
                // Why never the operation: the rerun is a transform, like the isolation retry.
                sources = remainingSources.ToArray(),
                defines = firstInput.defines,
                referencePaths = firstInput.referencePaths,
                targetTypesAssemblyPath = firstInput.targetTypesAssemblyPath,
                targetAssemblyName = firstInput.targetAssemblyName,
                targetAssemblyMvid = firstInput.targetAssemblyMvid,
                excludedMethodKeys = firstInput.excludedMethodKeys,
                excludedAddedMethodKeys = firstInput.excludedAddedMethodKeys,
                assemblySourcePaths = firstInput.assemblySourcePaths,
                // Why the left-out files are not added as siblings: the transform reads siblings
                // only for const drift, which the left-out file's own notices already report.
                changedSiblingSourcePaths = firstInput.changedSiblingSourcePaths,
                introducedTypeArtifacts = firstInput.introducedTypeArtifacts,
                activeMethodLabels = firstInput.activeMethodLabels
            };
        }

        internal string FormatLeftOutWarning(string projectRelativePath, IReadOnlyList<string> addedEnumMemberNames)
        {
            return string.Format(
                LeftOutWarningFormat,
                projectRelativePath,
                string.Join(", ", addedEnumMemberNames));
        }

        // Why only the top-level reason: a composite row that carries the split in its detail
        // has an owner row whose top-level reason names the same split.
        private static HashSet<string> CollectNamedSourceSideFiles(TransformWorkerSkippedDto[] skipped)
        {
            // Why Ordinal: the worker echoes the project-relative paths the Editor sent, the same
            // strings the per-file rows are keyed by.
            HashSet<string> named = new HashSet<string>(StringComparer.Ordinal);
            if (skipped == null)
            {
                return named;
            }

            foreach (TransformWorkerSkippedDto row in skipped)
            {
                string[] sourceSideFiles = SourceSideFilesOf(row.reason);
                if (sourceSideFiles == null)
                {
                    continue;
                }

                named.UnionWith(sourceSideFiles);
            }

            return named;
        }

        // Why each code reads a different field: a carried-in row lists the run's files that build
        // the bound type from source in declaringFiles, while a compiled-signature row lists the
        // compiled API there and the run's source-side files in splitSourceFiles.
        private static string[] SourceSideFilesOf(TransformWorkerReasonDto reason)
        {
            switch (reason.code)
            {
                case HotReloadWorkerReasonCode.AddedMethodCallsIntroducedMemberBoundToCompiledType:
                    return reason.declaringFiles;
                case HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature:
                    return reason.splitSourceFiles;
                default:
                    return null;
            }
        }

        // Why unchanged rows and declaration drift do not count: neither applies anything, and
        // the left-out file still reports its drift through its own per-file notices.
        private static bool OnlyAddsEnumMembers(HotReloadLeaveOutFile file, HotReloadWorkerRowsByFile rows)
        {
            if (file.DeclaresNewType)
            {
                return false;
            }

            if (rows.EntriesFor(file.ProjectRelativePath).Count > 0
                || rows.SkippedFor(file.ProjectRelativePath).Count > 0)
            {
                return false;
            }

            TransformWorkerFileOutputDto output = rows.FileOutputFor(file.ProjectRelativePath);
            if (string.IsNullOrEmpty(output.sourceContentSha256) || IsNullOrEmpty(output.addedEnumMemberNames))
            {
                return false;
            }

            return IsNullOrEmpty(output.addedFieldNames)
                && IsNullOrEmpty(output.addedConstNames)
                && IsNullOrEmpty(output.removedMembers)
                && IsNullOrEmpty(output.removedMethodSignatures)
                && IsNullOrEmpty(output.parseErrors);
        }

        private static bool IsNullOrEmpty<T>(T[] values)
        {
            return values == null || values.Length == 0;
        }

        private static List<string> CollectPaths(IReadOnlyList<HotReloadLeaveOutFile> files)
        {
            List<string> paths = new List<string>(files.Count);
            foreach (HotReloadLeaveOutFile file in files)
            {
                paths.Add(file.ProjectRelativePath);
            }

            return paths;
        }
    }
}
