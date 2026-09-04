using System.Collections.Generic;

using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The read-only inputs of one file's apply pipeline, fixed once the worker has run.
    /// </summary>
    /// <remarks>
    /// Why: the gate, the first shim compile and the entry applier each took the same dozen
    /// values as separate parameters, so every stage signature grew with the pipeline. Passing
    /// them as one object keeps a stage's parameter list to what that stage itself computes.
    /// </remarks>
    internal sealed class HotReloadApplyContext
    {
        internal HotReloadApplyContext(
            string projectRoot,
            string assemblyName,
            string assemblyResolvePath,
            string projectRelativePath,
            string correlationId,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string[] defines,
            TransformWorkerInputDto workerInput,
            TransformWorkerOutputDto workerOutput,
            HashSet<string> snapshotLabels,
            HashSet<string> snapshotAddedLabels)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(assemblyResolvePath), "assemblyResolvePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(targetDllPath), "targetDllPath must not be empty.");
            Debug.Assert(compilationAssembly != null, "compilationAssembly must not be null.");
            Debug.Assert(defines != null, "defines must not be null.");
            Debug.Assert(workerInput != null, "workerInput must not be null.");
            Debug.Assert(workerOutput != null, "workerOutput must not be null.");
            Debug.Assert(snapshotLabels != null, "snapshotLabels must not be null.");
            Debug.Assert(snapshotAddedLabels != null, "snapshotAddedLabels must not be null.");

            ProjectRoot = projectRoot;
            AssemblyName = assemblyName;
            AssemblyResolvePath = assemblyResolvePath;
            ProjectRelativePath = projectRelativePath;
            CorrelationId = correlationId;
            CompilationAssembly = compilationAssembly;
            TargetDllPath = targetDllPath;
            Defines = defines;
            WorkerInput = workerInput;
            WorkerOutput = workerOutput;
            SnapshotLabels = snapshotLabels;
            SnapshotAddedLabels = snapshotAddedLabels;
        }

        internal string ProjectRoot { get; }

        internal string AssemblyName { get; }

        internal string AssemblyResolvePath { get; }

        internal string ProjectRelativePath { get; }

        internal string CorrelationId { get; }

        internal UnityCompilationAssembly CompilationAssembly { get; }

        internal string TargetDllPath { get; }

        internal string[] Defines { get; }

        internal TransformWorkerInputDto WorkerInput { get; }

        internal TransformWorkerOutputDto WorkerOutput { get; }

        // Patch labels already active for this file when its apply started. Snapshotted because
        // a multi-file run mutates the ledgers between files.
        internal HashSet<string> SnapshotLabels { get; }

        internal HashSet<string> SnapshotAddedLabels { get; }
    }
}
