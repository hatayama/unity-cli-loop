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
        public const string PublicizedRefsRelativeDirectory = "Library/UloopHotReload/PublicizedRefs";

        // Worker binaries are keyed by SHA256 of the TransformWorker~/TransformWorker.cs source.
        public const string WorkerCacheRelativeDirectory = "Library/UloopHotReload/Worker";

        // EditMode e2e tests place edited source copies here so AssetDatabase is never provoked.
        public const string TestSourcesRelativeDirectory = "Library/UloopHotReload/TestSources";

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
            "Adding new members requires a real compile (uloop compile); hot reload only replaces existing method bodies.";

        // PR-6: delegation shims compile and load, but the delegation patcher lands in PR-7.
        // Keep a single present-tense reason so PR-7 can replace this constant and branch wholesale.
        public const string DelegationPatchNotWiredSkipReason =
            "Rewritten for delegation patching, which is not wired yet; method left unpatched.";

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

        public const string SmallMethodInliningRiskWarning =
            "This method is small enough (or marked [AggressiveInlining]) that the Mono JIT may "
            + "already have inlined it into callers; those call sites will keep running the old "
            + "body until they are recompiled.";

        public const string StaleAssemblyHint =
            "The loaded assembly no longer matches the compiled assembly on disk (a script compile "
            + "or domain reload may have happened). Hot reload is not needed — use uloop compile, "
            + "or wait for compilation to finish and retry.";

        public const string AssemblyNotLoadedHint =
            "The target assembly is not currently loaded in this AppDomain. Ensure the code path "
            + "that loads it has run, then retry.";

        // Transplant discards the original IL and any prior transpiler output on that method,
        // so a source pause point on a hot-reloaded method stops firing until domain reload
        // (or until the method is reverted and re-armed).
        public const string PausePointInteractionWarning =
            "Hot reload transplant replaces the method body and discards other transpilers on "
            + "that method; a pause point on a hot-reloaded method will not fire until the patch "
            + "is reverted or a domain reload restores the original IL.";
    }
}
