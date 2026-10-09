using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Starts the transform worker and runs one prepare and one transform request for the first
    /// usable target, so the first run after a reload finds the worker started and its Roslyn
    /// paths compiled. The answers are discarded.
    /// </summary>
    internal sealed class HotReloadTransformWorkerWarmUpItem : IHotReloadWarmUpItem
    {
        internal const string ItemName = "transform_worker";

        private readonly Func<TransformWorkerInputDto, CancellationToken, Task<TransformWorkerHostResult>> _sendRequest;

        internal HotReloadTransformWorkerWarmUpItem(
            Func<TransformWorkerInputDto, CancellationToken, Task<TransformWorkerHostResult>> sendRequest)
        {
            Debug.Assert(sendRequest != null, "sendRequest must not be null.");
            _sendRequest = sendRequest;
        }

        public string Name => ItemName;

        public async Task RunAsync(HotReloadWarmUpContext context, CancellationToken ct)
        {
            HotReloadTransformWorkerWarmUpRequest request = GatherOnMainThread(context);
            if (request == null)
            {
                return;
            }

            // Why the pool: reading the MVID loads the whole dll, and each source costs a
            // File.Exists. Why no ct for Task.Run: with a cancelled ct the work would never run,
            // and the token would be observed only as a TaskCanceledException here instead of
            // before the first request.
            TransformWorkerInputDto prepareInput =
                await Task.Run(() => request.BuildPrepareInput()).ConfigureAwait(false);
            if (prepareInput == null)
            {
                return;
            }

            ct.ThrowIfCancellationRequested();
            // Why None: cancelling a request mid-way kills the worker this item exists to warm.
            TransformWorkerHostResult prepared =
                await _sendRequest(prepareInput, CancellationToken.None).ConfigureAwait(false);

            // Why the token before the kind: a warm-up stopped during the prepare (a run, a reload
            // or a compile) is reported cancelled, not done, whatever the worker answered.
            ct.ThrowIfCancellationRequested();
            // Why stop here: a worker that could not start would make the transform wait for the
            // same failure again.
            if (prepared.Kind != TransformWorkerHostResultKind.Completed)
            {
                return;
            }

            await _sendRequest(request.BuildTransformInput(prepareInput), CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Main thread only: reads the compilation assembly. Returns the request material of the
        /// first target Unity lists, or null when Unity lists none of them.
        /// </summary>
        internal static HotReloadTransformWorkerWarmUpRequest GatherOnMainThread(HotReloadWarmUpContext context)
        {
            foreach (HotReloadWarmUpTarget target in context.Targets)
            {
                UnityCompilationAssembly assembly = HotReloadCompilationAssemblies.FindByName(target.AssemblyName);
                // A name left in the ledger from an older layout, or an empty list during a compile.
                if (assembly == null)
                {
                    continue;
                }

                // Why no check for an assembly without sources: Unity lists none, and one would
                // give no source on disk, which the prepare input already ends on.
                string[] sourceFiles = assembly.sourceFiles;
                HotReloadTypeHome home = HotReloadTypeHome.ScriptAssemblies(target.AssemblyName, target.DllPath);
                return new HotReloadTransformWorkerWarmUpRequest(
                    target.AssemblyName,
                    Path.GetFullPath(home.DllPath),
                    sourceFiles,
                    HotReloadPatchTargetSupport.BuildAssemblySourcePaths(context.ProjectRoot, sourceFiles),
                    // The same fallback as the run's input, so both send the same defines.
                    assembly.defines ?? Array.Empty<string>(),
                    HotReloadShimReferenceBuilder.BuildWorkerReferencePaths(context.ProjectRoot, assembly, home));
            }

            return null;
        }
    }

    /// <summary>
    /// One target's request material, read from Unity on the main thread.
    /// </summary>
    internal sealed class HotReloadTransformWorkerWarmUpRequest
    {
        internal HotReloadTransformWorkerWarmUpRequest(
            string assemblyName,
            string dllPath,
            string[] sourceFiles,
            string[] assemblySourcePaths,
            string[] defines,
            string[] referencePaths)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(dllPath), "dllPath must not be null or empty.");
            Debug.Assert(sourceFiles != null, "sourceFiles must not be null.");
            Debug.Assert(assemblySourcePaths != null, "assemblySourcePaths must not be null.");
            Debug.Assert(
                assemblySourcePaths?.Length == sourceFiles?.Length,
                "assemblySourcePaths must hold one path per source file.");
            Debug.Assert(defines != null, "defines must not be null.");
            Debug.Assert(referencePaths != null, "referencePaths must not be null.");
            AssemblyName = assemblyName;
            DllPath = dllPath;
            SourceFiles = sourceFiles;
            AssemblySourcePaths = assemblySourcePaths;
            Defines = defines;
            ReferencePaths = referencePaths;
        }

        internal string AssemblyName { get; }

        /// <summary>Full path of the compiled dll.</summary>
        internal string DllPath { get; }

        /// <summary>Project-relative source paths, as Unity lists them.</summary>
        internal string[] SourceFiles { get; }

        /// <summary>Absolute source paths, one per <see cref="SourceFiles"/> entry and in the same order.</summary>
        internal string[] AssemblySourcePaths { get; }

        internal string[] Defines { get; }

        internal string[] ReferencePaths { get; }

        /// <summary>
        /// Any thread. The prepare request for the first source on disk, or null when none of them
        /// is. Throws the IO exception of a dll that cannot be read.
        /// </summary>
        internal TransformWorkerInputDto BuildPrepareInput()
        {
            // Why look for one on disk: the source paths are the project-relative paths joined to
            // the project root, so a package outside the project may list sources that are not there.
            int index = Array.FindIndex(AssemblySourcePaths, File.Exists);
            if (index < 0)
            {
                return null;
            }

            return new TransformWorkerInputDto
            {
                operation = HotReloadConstants.PrepareIntroducedTypesOperation,
                sources = new[]
                {
                    new TransformWorkerSourceDto
                    {
                        sourcePath = AssemblySourcePaths[index],
                        projectRelativePath = SourceFiles[index]
                    }
                },
                defines = Defines,
                referencePaths = ReferencePaths,
                targetTypesAssemblyPath = DllPath,
                targetAssemblyName = AssemblyName,
                targetAssemblyMvid = HotReloadAssemblyMvid.Read(DllPath),
                assemblySourcePaths = AssemblySourcePaths
            };
        }

        /// <summary>
        /// The transform request for the same material as <paramref name="prepareInput"/>. Why no
        /// snapshot source: without a baseline the worker treats every method as edited, which
        /// is enough to compile its transform paths, and the answer is discarded.
        /// </summary>
        internal TransformWorkerInputDto BuildTransformInput(TransformWorkerInputDto prepareInput)
        {
            Debug.Assert(prepareInput != null, "prepareInput must not be null.");
            return new TransformWorkerInputDto
            {
                sources = prepareInput.sources,
                defines = prepareInput.defines,
                referencePaths = prepareInput.referencePaths,
                targetTypesAssemblyPath = prepareInput.targetTypesAssemblyPath,
                targetAssemblyName = prepareInput.targetAssemblyName,
                targetAssemblyMvid = prepareInput.targetAssemblyMvid,
                assemblySourcePaths = prepareInput.assemblySourcePaths
            };
        }
    }
}
