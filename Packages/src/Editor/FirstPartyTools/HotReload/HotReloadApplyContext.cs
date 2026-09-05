using System.Collections.Generic;

using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The read-only inputs of one group's apply pipeline, fixed once the worker has run.
    /// </summary>
    /// <remarks>
    /// Why: the gate, the first shim compile and the entry applier each took the same dozen
    /// values as separate parameters, so every stage signature grew with the pipeline. Passing
    /// them as one object keeps a stage's parameter list to what that stage itself computes.
    /// The file-specific half lives in HotReloadGroupFile, one per edited file of the group.
    /// </remarks>
    internal sealed class HotReloadApplyContext
    {
        internal HotReloadApplyContext(
            string projectRoot,
            string assemblyName,
            string correlationId,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string[] defines,
            TransformWorkerInputDto workerInput,
            TransformWorkerOutputDto workerOutput,
            IReadOnlyList<HotReloadGroupFile> files)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(targetDllPath), "targetDllPath must not be empty.");
            Debug.Assert(compilationAssembly != null, "compilationAssembly must not be null.");
            Debug.Assert(defines != null, "defines must not be null.");
            Debug.Assert(workerInput != null, "workerInput must not be null.");
            Debug.Assert(workerOutput != null, "workerOutput must not be null.");
            Debug.Assert(files != null && files.Count > 0, "A group must hold a file.");

            ProjectRoot = projectRoot;
            AssemblyName = assemblyName;
            CorrelationId = correlationId;
            CompilationAssembly = compilationAssembly;
            TargetDllPath = targetDllPath;
            Defines = defines;
            WorkerInput = workerInput;
            WorkerOutput = workerOutput;
            Files = files;

            List<(string ProjectRelativePath, string AssemblyResolvePath)> filePaths =
                new List<(string ProjectRelativePath, string AssemblyResolvePath)>(files.Count);
            List<string> projectRelativePaths = new List<string>(files.Count);
            List<TransformWorkerRemovedMethodSignatureDto> removedMethodSignatures =
                new List<TransformWorkerRemovedMethodSignatureDto>();
            foreach (HotReloadGroupFile file in files)
            {
                Debug.Assert(file.FileOutput != null, "Every file must carry its worker output row.");
                filePaths.Add((file.ProjectRelativePath, file.AssemblyResolvePath));
                projectRelativePaths.Add(file.ProjectRelativePath);
                if (file.FileOutput.removedMethodSignatures != null)
                {
                    removedMethodSignatures.AddRange(file.FileOutput.removedMethodSignatures);
                }
            }

            GroupFilePaths = new HotReloadGroupFilePaths(filePaths);
            ProjectRelativePaths = projectRelativePaths;
            RemovedMethodSignatures = removedMethodSignatures.ToArray();
        }

        internal string ProjectRoot { get; }

        internal string AssemblyName { get; }

        internal string CorrelationId { get; }

        internal UnityCompilationAssembly CompilationAssembly { get; }

        internal string TargetDllPath { get; }

        internal string[] Defines { get; }

        internal TransformWorkerInputDto WorkerInput { get; }

        internal TransformWorkerOutputDto WorkerOutput { get; }

        // The edited files of this group, in the order they were sent to the worker.
        internal IReadOnlyList<HotReloadGroupFile> Files { get; }

        // Resolves the file identity a worker row carries into the path its outcomes report.
        internal HotReloadGroupFilePaths GroupFilePaths { get; }

        internal IReadOnlyList<string> ProjectRelativePaths { get; }

        // Every signature the group's files removed. Why joined: the call-site scan that gates a
        // replacement runs once for the group, so it must see what the whole edit took away.
        internal TransformWorkerRemovedMethodSignatureDto[] RemovedMethodSignatures { get; }
    }
}
