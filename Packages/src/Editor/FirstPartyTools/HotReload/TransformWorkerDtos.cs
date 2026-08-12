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

        // Project-relative forward-slash path baked into #line document names so shim compile
        // diagnostics map back to the user's file (not the temp HotReloadShim.cs path).
        public string projectRelativePath;
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

        // Identities of methods left untouched because they match the verified snapshot.
        // Null/empty means none (or no baseline). UnchangedTotal is derived from Length.
        public TransformWorkerUnchangedMethodDto[] unchangedMethods;

        // True when snapshotSource was provided but a duplicate syntax-method key on either side
        // disabled baseline comparison (silent patch-all fallback). False when snapshotSource is null.
        public bool baselineDisabledByDuplicateKeys;
    }

    [Serializable]
    internal sealed class TransformWorkerUnchangedMethodDto
    {
        public string typeMetadataName;
        public string methodName;
        public string[] parameterTypeFullNames;
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

        // 1-based, both ends inclusive, within the original edited source file (not shimSource).
        // Used to attribute shim compile errors whose #line-mapped locations fall in this method.
        public int sourceStartLine;
        public int sourceEndLine;

        // Null/empty when the method is not a one-shot lifecycle method and is not only called
        // from them inside this file.
        public string lifecycleNote;
    }

    [Serializable]
    internal sealed class TransformWorkerSkippedDto
    {
        public string method;
        public string reason;
    }
}
