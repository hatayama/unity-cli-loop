using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Input payload written for the out-of-process transform worker (UTF-8 JSON, no BOM).
    /// </summary>
    [Serializable]
    internal sealed class TransformWorkerInputDto
    {
        public string sourcePath;
        public string[] defines;
        public string[] referencePaths;
        public string targetTypesAssemblyPath;

        // Method keys (see HotReloadOrchestrator.BuildMethodKey) already reported Failed from a
        // first compile round; the retry worker run drops these methods entirely.
        public string[] excludedMethodKeys;

        // Verified snapshot text for edited-method detection. Null = no baseline, patch all methods.
        // Why pass text (not a path): avoids an IO race between orchestrator verification and worker
        // read that would crash the whole file under the no-try-catch policy.
        public string snapshotSource;
    }

    /// <summary>
    /// Output payload read from the out-of-process transform worker.
    /// </summary>
    [Serializable]
    internal sealed class TransformWorkerOutputDto
    {
        public string shimSource;
        public TransformWorkerEntryDto[] entries;
        public TransformWorkerSkippedDto[] skipped;
        public string[] parseErrors;
        public string[] declarationDriftWarnings;
        public int unchangedMethodCount;
    }

    [Serializable]
    internal sealed class TransformWorkerEntryDto
    {
        public string typeMetadataName;
        public string methodName;
        public string[] parameterTypeFullNames;
        public string shimTypeName;
        public string shimMethodName;

        // "transplant" | "delegation". Null/empty is treated as transplant by the orchestrator.
        public string patchKind;

        // 1-based, both ends inclusive, within TransformWorkerOutputDto.shimSource; 0 when the
        // shim method declaration could not be located while re-parsing the emitted source.
        public int shimSourceStartLine;
        public int shimSourceEndLine;
    }

    [Serializable]
    internal sealed class TransformWorkerSkippedDto
    {
        public string method;
        public string reason;
    }
}
