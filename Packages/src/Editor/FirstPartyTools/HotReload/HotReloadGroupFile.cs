using System.Collections.Generic;

using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One edited file inside a group: what the run resolved for it, where its results go, and
    /// the per-file findings the group pipeline fills in as it proceeds.
    /// </summary>
    /// <remarks>
    /// Why per file (not per run): a group is transformed and compiled as a whole, but the unit
    /// that gets applied and reported is still the single file, so every stage needs the file its
    /// rows belong to. The assembly-level fields are repeated per file because assembly resolution
    /// runs before the run knows which files group together.
    /// </remarks>
    internal sealed class HotReloadGroupFile
    {
        internal HotReloadGroupFile(
            string assemblyResolvePath,
            string workerSourcePath,
            string projectRelativePath,
            string assemblyName,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string projectRoot,
            HotReloadFileSinks sinks,
            HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence = null)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyResolvePath), "assemblyResolvePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(workerSourcePath), "workerSourcePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be empty.");
            Debug.Assert(compilationAssembly != null, "compilationAssembly must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(targetDllPath), "targetDllPath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be empty.");
            Debug.Assert(sinks != null, "sinks must not be null.");

            AssemblyResolvePath = assemblyResolvePath;
            WorkerSourcePath = workerSourcePath;
            ProjectRelativePath = projectRelativePath;
            AssemblyName = assemblyName;
            CompilationAssembly = compilationAssembly;
            TargetDllPath = targetDllPath;
            ProjectRoot = projectRoot;
            Sinks = sinks;
            NewSourceMembershipEvidence = newSourceMembershipEvidence;
        }

        internal static HotReloadGroupFile ForActiveSibling(
            HotReloadGroupFile template,
            string projectRelativePath,
            string workerSourcePath,
            HotReloadFileSinks sinks)
        {
            Debug.Assert(template != null, "template must not be null.");
            return new HotReloadGroupFile(
                projectRelativePath,
                workerSourcePath,
                projectRelativePath,
                template.AssemblyName,
                template.CompilationAssembly,
                template.TargetDllPath,
                template.ProjectRoot,
                sinks,
                null);
        }

        // The path the caller asked to reload, used as the outcome file path.
        internal string AssemblyResolvePath { get; }

        // The path the worker reads the edited text from; a test override copy may differ.
        internal string WorkerSourcePath { get; }

        internal string ProjectRelativePath { get; }

        internal string AssemblyName { get; }

        internal UnityCompilationAssembly CompilationAssembly { get; }

        internal string TargetDllPath { get; }

        internal string ProjectRoot { get; }

        internal HotReloadFileSinks Sinks { get; }

        // Null for a source already present in CompilationPipeline.sourceFiles.
        internal HotReloadNewSourceMembershipEvidence NewSourceMembershipEvidence { get; }

        // Verified snapshot text of this file, or null when it has no baseline.
        internal string SnapshotSource { get; set; }

        // Patch labels already active for this file when the group's apply started. Snapshotted
        // because a run mutates the ledgers between files.
        internal HashSet<string> SnapshotLabels { get; set; }

        internal HashSet<string> SnapshotAddedLabels { get; set; }

        // Set when a group-level stage already failed this file, so the apply loop must leave
        // its generations alone and keep the patches of the previous run in place.
        internal bool SkipApply { get; set; }

        // This file's row set inside the group worker output.
        internal TransformWorkerFileOutputDto FileOutput { get; set; }

        internal int UnchangedMethodCount { get; set; }

        internal int RevertedUnchangedCount { get; set; }

        // Added field and const display names to commit for this file, as resolved by the stage
        // that produced the entries actually applied (first pass, gate retry or isolation retry).
        internal string[] AddedFieldNames { get; set; }

        internal string[] AddedConstNames { get; set; }

        // Added field names this file actually committed while its generation was cleared,
        // so the run reports what the ledger now holds. Why only that path: a file the group
        // never applied (a group-level failure, a preflight failure) committed nothing, and
        // reporting names it did not write would make the response disagree with the ledger.
        internal string[] ClearedAddedFieldNames { get; set; }
    }
}
