using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Shared literals for the hot-reload pipeline (cache paths, Harmony id, patchability heuristics).
    /// </summary>
    internal static class HotReloadConstants
    {
        public const string HarmonyId = "io.github.hatayama.uloop.hot-reload";

        public const string ScriptAssembliesRelativeDirectory = "Library/ScriptAssemblies";
        public const string CompiledAssemblyExtension = ".dll";

        // Publicized reference copies are keyed by assembly name + Mvid so a recompiled assembly
        // never reuses a stale visibility rewrite.
        // "fmt2" = publicize-format generation. The cache key is assembly name + MVID only, so a
        // rule change (event backing fields stay non-public since fmt2) must move the directory —
        // otherwise a cache written under the old rule keeps poisoning shim compiles until the
        // assembly happens to recompile.
        public const string PublicizedRefsRelativeDirectory = "Library/UloopHotReload/PublicizedRefs/fmt2";

        // Worker binaries are keyed by SHA256 of the TransformWorker~/TransformWorker.cs source.
        public const string WorkerCacheRelativeDirectory = "Library/UloopHotReload/Worker";

        // EditMode e2e tests place edited source copies here so AssetDatabase is never provoked.
        public const string TestSourcesRelativeDirectory = "Library/UloopHotReload/TestSources";

        // Per-assembly source snapshots keyed by assembly name + Mvid; file names are SHA256 of
        // the project-relative source path (slash-normalized) so separators and MAX_PATH never
        // affect the on-disk layout. Adoption is decided at use time by PDB document checksum.
        public const string SourceSnapshotRelativeDirectory = "Library/UloopHotReload/SourceSnapshot";

        // Package-relative path of the out-of-process transform worker source (tilde dir = Unity-ignored).
        public const string WorkerSourcePackageRelativePath =
            "Editor/FirstPartyTools/HotReload/TransformWorker~/TransformWorker.cs";

        public const string WorkerDllFileName = "worker.dll";
        public const string WorkerRuntimeConfigFileName = "worker.runtimeconfig.json";
        public const string WorkerRoslynDirectorySidecarFileName = "roslyn-directory.txt";
        public const string WorkerResponseFileName = "worker.rsp";

        public const int WorkerProcessTimeoutMilliseconds = 120_000;

        public const string BurstCompileAttributeFullName = "Unity.Burst.BurstCompileAttribute";

        public const string NewMemberCompileHint =
            "Same-file added methods are applied by hot reload; cross-file references and unsupported "
            + "member kinds still require a real compile (uloop compile).";

        // Wire value for TransformWorkerEntryDto.patchKind when the worker emits a shim for a
        // method that exists only in the edited source. Keep in sync with PatchKinds.AddedMethod
        // in TransformWorker~/TransformWorker.cs.
        public const string PatchKindAddedMethod = "addedMethod";

        // Wire values for TransformWorkerRemovedMemberDto.kind.
        public const string RemovedMemberKindMethod = "method";
        public const string RemovedMemberKindField = "field";

        public const string MissingUsingCompileHint =
            "This can mean a missing using or global using (hot reload collects global usings from the edited file's assembly).";

        // Wire value for TransformWorkerEntryDto.patchKind when the worker rewrote inaccessible
        // accesses into accessor delegates.
        public const string PatchKindDelegation = "delegation";

        // Name of the parameterless public static binder the worker emits into delegation shim
        // types. Wire contract with TransformWorker's EmitBindAccessorsMethod — the worker is a
        // standalone source file that cannot reference this constant, so keep both in sync.
        public const string ShimBindAccessorsMethodName = "__BindAccessors";

        /// <summary>
        /// Returns whether a ScriptAssemblies DLL is a project assembly that may be publicized.
        /// Engine / test-runner / system assemblies under ScriptAssemblies must stay untouched —
        /// some are not rewriteable managed images.
        /// </summary>
        public static bool IsPublicizableProjectAssemblyFileName(string fileNameWithoutExtension)
        {
            if (string.IsNullOrEmpty(fileNameWithoutExtension))
            {
                return false;
            }

            if (fileNameWithoutExtension.StartsWith("UnityEngine", StringComparison.Ordinal)
                || fileNameWithoutExtension.StartsWith("UnityEditor", StringComparison.Ordinal)
                || fileNameWithoutExtension.StartsWith("Unity.", StringComparison.Ordinal)
                || fileNameWithoutExtension.StartsWith("System.", StringComparison.Ordinal)
                || fileNameWithoutExtension == "System"
                || fileNameWithoutExtension == "mscorlib"
                || fileNameWithoutExtension == "netstandard"
                || fileNameWithoutExtension == "Mono.Security")
            {
                return false;
            }

            return true;
        }

        // Same heuristic threshold as pause point: small IL bodies may already be inlined by Mono,
        // in which case a Harmony detour on the original method will not reach existing call sites.
        public const int SmallMethodInliningRiskThresholdBytes = 32;

        public const string StaleAssemblyHint =
            "The loaded assembly no longer matches the compiled assembly on disk (a script compile "
            + "or domain reload may have happened). Hot reload is not needed — use uloop compile, "
            + "or wait for compilation to finish and retry.";

        public const string AssemblyNotLoadedHint =
            "The target assembly is not currently loaded in this AppDomain. Ensure the code path "
            + "that loads it has run, then retry.";

        // Format: file name, assembly name. Emitted per file when PDB-validated snapshot is absent.
        public const string NoVerifiedSourceSnapshotWarningFormat =
            "No verified source snapshot for {0} (assembly {1}); patching all methods. "
            + "Run uloop compile to establish a baseline for edited-method detection.";

        // Format: file name, assembly name. Emitted when syntax-method key collision disables baseline.
        public const string BaselineDisabledByDuplicateKeysWarningFormat =
            "Baseline comparison disabled for {0} (assembly {1}): the file contains methods with "
            + "colliding signature keys; patching all methods.";

        // Format: comma-separated "{id} (now line {N}: {text})" entries.
        public const string RetargetedPausePointsMessageFormat =
            "Armed pause points were re-targeted onto the hot-reload patched bodies: {0}";

        // Format: id, old line text, new line text.
        public const string RetargetLineDriftWarningFormat =
            "Pause point {0} now targets a different statement (was: \"{1}\", now: \"{2}\"). "
            + "Re-enable it at the intended line if this is not what you want.";

        // Format: comma-separated expired marker ids.
        public const string ExpiredPausePointsNotRetargetedMessageFormat =
            "Expired pause points were not re-targeted and will not fire: {0}";

        // Format: id, resolved line number, resolved line text.
        public const string RetargetedPausePointIdDetailFormat =
            "{0} (now line {1}: {2})";

        // Format: count of Methods entries that carry a LifecycleNote.
        public const string LifecycleNotesAggregatedMessageFormat =
            "{0} patched method(s) have one-shot lifecycle notes; see Methods[].LifecycleNote.";
    }
}
