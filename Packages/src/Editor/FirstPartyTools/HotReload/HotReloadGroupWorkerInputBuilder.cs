using System;
using System.Collections.Generic;
using System.IO;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the transform worker input that one assembly group sends to the worker.
    /// </summary>
    internal static class HotReloadGroupWorkerInputBuilder
    {
        internal static TransformWorkerInputDto BuildWorkerInput(
            IReadOnlyList<HotReloadGroupFile> files,
            HotReloadChangedSiblingScanResult siblingScan,
            HotReloadDomain domain)
        {
            HotReloadGroupFile firstFile = files[0];
            TransformWorkerSourceDto[] sources = new TransformWorkerSourceDto[files.Count];
            for (int index = 0; index < files.Count; index++)
            {
                HotReloadGroupFile file = files[index];
                sources[index] = new TransformWorkerSourceDto
                {
                    sourcePath = Path.GetFullPath(file.WorkerSourcePath),
                    projectRelativePath = file.ProjectRelativePath,
                    snapshotSource = file.SnapshotSource,
                    reappliedSibling = file.ReappliedSibling
                };
            }

            return new TransformWorkerInputDto
            {
                sources = sources,
                defines = firstFile.CompilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = HotReloadShimReferenceBuilder.BuildWorkerReferencePaths(
                    firstFile.CompilationAssembly,
                    firstFile.Home),
                targetTypesAssemblyPath = Path.GetFullPath(firstFile.Home.DllPath),
                // The retained records normalize an introduced type back to the generation of the
                // assembly that owns its source, so every run has to name that generation even
                // before it carries a record of its own.
                targetAssemblyName = firstFile.AssemblyName,
                targetAssemblyMvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(firstFile.Home.DllPath),
                assemblySourcePaths = HotReloadPatchTargetSupport.BuildAssemblySourcePaths(
                    firstFile.ProjectRoot,
                    firstFile.CompilationAssembly.sourceFiles),
                changedSiblingSourcePaths = siblingScan.ChangedSiblingAbsolutePaths,
                changedSiblingScanComplete = siblingScan.IsComplete,
                activeMethodLabels = CollectActiveMethodLabels(files, domain)
            };
        }

        // Why added members too: a skipped method keeps running the body an earlier reload
        // patched into it, and a skipped added member is deactivated but stays reachable, because
        // a patch this run leaves active can still call its earlier shim body. Either way the
        // skipped writer may still assign the field. Both lists hold display labels, which is
        // the form the worker's skipped rows use.
        private static string[] CollectActiveMethodLabels(
            IReadOnlyList<HotReloadGroupFile> files,
            HotReloadDomain domain)
        {
            List<string> labels = new List<string>();
            foreach (HotReloadGroupFile file in files)
            {
                labels.AddRange(domain.ListActiveMethodKeys(file.ProjectRelativePath));
                labels.AddRange(domain.ListActiveAddedMethodKeys(file.ProjectRelativePath));
            }

            return labels.ToArray();
        }
    }
}
