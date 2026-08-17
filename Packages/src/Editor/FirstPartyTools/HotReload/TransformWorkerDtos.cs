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

        // Added-method keys whose shim bodies failed the first compile. Distinct from
        // excludedMethodKeys so a healthy added shim is not dropped when an existing method fails
        // (G1), while a broken added body can still be excluded together with its callers.
        public string[] excludedAddedMethodKeys;

        // Verified snapshot text for edited-method detection. Null = no baseline, patch all methods.
        // Why pass text (not a path): avoids an IO race between orchestrator verification and worker
        // read that would crash the whole file under the no-try-catch policy.
        public string snapshotSource;

        // Project-relative forward-slash path baked into #line document names so shim compile
        // diagnostics map back to the user's file (not the temp HotReloadShim.cs path).
        public string projectRelativePath;

        // Absolute paths of every source file in the edited file's compilation assembly.
        // The worker scans these for global using directives. Null/omitted is treated as empty.
        public string[] assemblySourcePaths;
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

        // Members present in the snapshot (or compiled assembly for fields in later PRs) but
        // absent from the edited source. Null/omitted deserializes as empty after client coalesce.
        public TransformWorkerRemovedMemberDto[] removedMembers;

        // True when any emitted shim type contains Harmony accessor delegates. Drives Harmony
        // reference injection; patchKind "addedMethod" entries can also need accessors (B2).
        public bool hasAccessorDelegates;

        // True when any emitted shim body rewrites an added-field access to HotReloadAddedFieldStore.
        // Drives ToolContracts assembly injection at both the first compile and isolation retry.
        public bool hasAddedFieldRewrites;

        // Source-level names ("Ns.Type.field") of fields this reload added via RegisterStore /
        // RegisterConst. Null/omitted deserializes as empty after client coalesce.
        public string[] addedFieldNames;

        // Compiled identities of methods that left the edited file (or whose return type changed).
        // Null/omitted deserializes as empty after client coalesce.
        public TransformWorkerRemovedMethodSignatureDto[] removedMethodSignatures;
    }

    [Serializable]
    internal sealed class TransformWorkerRemovedMethodSignatureDto
    {
        public string typeMetadataName;
        public string methodName;
        public string[] parameterTypeFullNames;

        // Open generic arity. 0 for non-generic methods so existing keys stay unchanged.
        public int genericArity;
    }

    [Serializable]
    internal sealed class TransformWorkerRemovedMemberDto
    {
        // "method" | "field"
        public string kind;
        public string name;
    }

    [Serializable]
    internal sealed class TransformWorkerUnchangedMethodDto
    {
        public string typeMetadataName;
        public string methodName;
        public string[] parameterTypeFullNames;

        // Open generic arity. 0 for non-generic methods so existing keys stay unchanged.
        public int genericArity;
    }

    [Serializable]
    internal sealed class TransformWorkerEntryDto
    {
        public string typeMetadataName;
        public string methodName;
        public string[] parameterTypeFullNames;

        // Open generic arity. 0 for non-generic methods so existing keys stay unchanged.
        public int genericArity;

        public string shimTypeName;
        public string shimMethodName;

        // "transplant" | "delegation" | "addedMethod". Null/empty is treated as transplant by the orchestrator.
        public string patchKind;

        // Method keys of added methods this entry's body calls. Null/omitted is empty.
        // Isolation retry excludes these callers instead of dropping the added-method shim (G1).
        public string[] calledAddedMethodKeys;

        // 1-based, both ends inclusive, within the original edited source file (not shimSource).
        // Used to attribute shim compile errors whose #line-mapped locations fall in this method.
        public int sourceStartLine;
        public int sourceEndLine;

        // Null/empty when the method is not a one-shot lifecycle method and is not only called
        // from them inside this file.
        public string lifecycleNote;

        // True when this addedMethod entry replaces a compiled method whose return type changed.
        public bool replacesCompiledMethod;
    }

    [Serializable]
    internal sealed class TransformWorkerSkippedDto
    {
        public string method;
        public string reason;
    }
}
