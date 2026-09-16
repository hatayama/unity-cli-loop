using System;
using System.Collections.Generic;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The inputs an isolation retry inherits unchanged from the run that triggered it: the first
    /// worker input, the assembly it targets, and the identity used for logging.
    /// </summary>
    internal sealed class HotReloadIsolationRetryContext
    {
        internal HotReloadIsolationRetryContext(
            TransformWorkerInputDto workerInput,
            UnityCompilationAssembly compilationAssembly,
            HotReloadTypeHome home,
            string[] defines,
            TransformWorkerSkippedDto[] firstPassSkipped,
            HotReloadGroupFilePaths groupFilePaths,
            string correlationId,
            IReadOnlyList<HotReloadTypeHome> introducedTypeArtifactHomes)
        {
            WorkerInput = workerInput;
            CompilationAssembly = compilationAssembly;
            Home = home;
            Defines = defines;
            FirstPassSkipped = firstPassSkipped;
            GroupFilePaths = groupFilePaths;
            CorrelationId = correlationId;
            IntroducedTypeArtifactHomes =
                introducedTypeArtifactHomes ?? Array.Empty<HotReloadTypeHome>();
        }

        internal TransformWorkerInputDto WorkerInput { get; }
        internal UnityCompilationAssembly CompilationAssembly { get; }
        // Where the retried group's patch target types live.
        internal HotReloadTypeHome Home { get; }
        internal string[] Defines { get; }

        // The first-pass skip rows the retry compares against, so only skips new to the retry surface.
        internal TransformWorkerSkippedDto[] FirstPassSkipped { get; }

        internal HotReloadGroupFilePaths GroupFilePaths { get; }
        internal string CorrelationId { get; }

        // The retained artifact assemblies the retry's shim compile references, resolved once by
        // the run that triggered the retry: the retry compiles the same group against them.
        internal IReadOnlyList<HotReloadTypeHome> IntroducedTypeArtifactHomes { get; }
    }
}
