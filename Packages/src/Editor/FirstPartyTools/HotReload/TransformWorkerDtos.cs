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
    }

    [Serializable]
    internal sealed class TransformWorkerSkippedDto
    {
        public string method;
        public string reason;
    }
}
