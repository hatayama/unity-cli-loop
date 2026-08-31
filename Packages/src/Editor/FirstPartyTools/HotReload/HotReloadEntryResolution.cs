using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

using Assembly = System.Reflection.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Preflight resolution of worker entries: bind, match, shim lookup, and CheckPatchable
    /// with no registry writes and no Harmony Patch.
    /// </summary>
    internal static class HotReloadEntryResolution
    {
        internal static Result ResolveEntries(
            string assemblyName,
            string filePath,
            Assembly shimAssembly,
            TransformWorkerEntryDto[] entriesToPatch)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be empty.");
            Debug.Assert(shimAssembly != null, "shimAssembly must not be null.");
            Debug.Assert(entriesToPatch != null, "entriesToPatch must not be null.");

            Dictionary<string, string> bindFailures = HotReloadEntryApplier.BindShimAccessors(shimAssembly);
            List<ResolvedEntry> resolvedEntries = new List<ResolvedEntry>();
            for (int index = 0; index < entriesToPatch.Length; index++)
            {
                (ResolvedEntry resolved, HotReloadMethodOutcome failure) = TryResolveEntry(
                    entriesToPatch[index],
                    assemblyName,
                    shimAssembly,
                    bindFailures,
                    filePath);
                if (failure != null)
                {
                    return Result.Failed(
                        BuildAtomicFailureOutcomes(entriesToPatch, index, failure, filePath));
                }

                resolvedEntries.Add(resolved);
            }

            return Result.Succeeded(resolvedEntries);
        }

        // Why every non-failed entry, including those already resolved: a later
        // preflight failure must not drop earlier methods from the response.
        private static List<HotReloadMethodOutcome> BuildAtomicFailureOutcomes(
            TransformWorkerEntryDto[] entries,
            int failedIndex,
            HotReloadMethodOutcome failure,
            string filePath)
        {
            Debug.Assert(entries != null, "entries must not be null.");
            Debug.Assert(failedIndex >= 0, "failedIndex must not be negative.");
            Debug.Assert(failedIndex < entries.Length, "failedIndex must be inside entries.");
            Debug.Assert(failure != null, "failure must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be empty.");

            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            for (int index = 0; index < failedIndex; index++)
            {
                outcomes.Add(CreateAtomicSkipOutcome(entries[index], filePath));
            }

            outcomes.Add(failure);
            AppendAtomicSkipOutcomes(outcomes, entries, failedIndex + 1, filePath);
            return outcomes;
        }

        internal static void AppendAtomicSkipOutcomes(
            List<HotReloadMethodOutcome> outcomes,
            TransformWorkerEntryDto[] entries,
            int startIndex,
            string filePath)
        {
            Debug.Assert(outcomes != null, "outcomes must not be null.");
            Debug.Assert(entries != null, "entries must not be null.");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative.");
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be empty.");

            for (int index = startIndex; index < entries.Length; index++)
            {
                outcomes.Add(CreateAtomicSkipOutcome(entries[index], filePath));
            }
        }

        private static HotReloadMethodOutcome CreateAtomicSkipOutcome(
            TransformWorkerEntryDto entry,
            string filePath)
        {
            return HotReloadMethodOutcome.Skipped(
                FormatEntryLabel(entry),
                HotReloadConstants.AtomicFileSkipReason,
                filePath);
        }

        private static (ResolvedEntry Resolved, HotReloadMethodOutcome Failure) TryResolveEntry(
            TransformWorkerEntryDto entry,
            string assemblyName,
            Assembly shimAssembly,
            IReadOnlyDictionary<string, string> bindFailures,
            string filePath)
        {
            string methodLabel = FormatEntryLabel(entry);
            if (entry.patchKind == HotReloadConstants.PatchKindAddedMethod)
            {
                return TryResolveAddedMethod(entry, methodLabel, shimAssembly, bindFailures, filePath);
            }

            return TryResolveExistingMethod(
                entry,
                methodLabel,
                assemblyName,
                shimAssembly,
                bindFailures,
                filePath);
        }

        private static (ResolvedEntry Resolved, HotReloadMethodOutcome Failure) TryResolveAddedMethod(
            TransformWorkerEntryDto entry,
            string methodLabel,
            Assembly shimAssembly,
            IReadOnlyDictionary<string, string> bindFailures,
            string filePath)
        {
            if (bindFailures.TryGetValue(entry.shimTypeName ?? string.Empty, out string bindFailureReason))
            {
                return (null, HotReloadMethodOutcome.Failed(methodLabel, bindFailureReason, filePath));
            }

            (MethodInfo shimMethod, string shimError) = FindShimMethod(shimAssembly, entry);
            if (shimMethod == null)
            {
                return (null, HotReloadMethodOutcome.Failed(methodLabel, shimError, filePath));
            }

            return (
                new ResolvedEntry(
                    entry,
                    methodLabel,
                    filePath,
                    HotReloadPatchShape.Transplant,
                    originalMethod: null,
                    shimMethod,
                    isAddedMethod: true),
                null);
        }

        private static (ResolvedEntry Resolved, HotReloadMethodOutcome Failure) TryResolveExistingMethod(
            TransformWorkerEntryDto entry,
            string methodLabel,
            string assemblyName,
            Assembly shimAssembly,
            IReadOnlyDictionary<string, string> bindFailures,
            string filePath)
        {
            HotReloadPatchShape patchShape = entry.patchKind == HotReloadConstants.PatchKindDelegation
                ? HotReloadPatchShape.Delegation
                : HotReloadPatchShape.Transplant;
            if (patchShape == HotReloadPatchShape.Delegation
                && bindFailures.TryGetValue(entry.shimTypeName ?? string.Empty, out string bindFailureReason))
            {
                return (null, HotReloadMethodOutcome.Failed(methodLabel, bindFailureReason, filePath));
            }

            string[] parameterTypeFullNames = entry.parameterTypeFullNames ?? Array.Empty<string>();
            HotReloadMethodMatchResult matchResult = HotReloadMethodMatcher.Resolve(
                assemblyName,
                entry.typeMetadataName,
                entry.methodName,
                parameterTypeFullNames,
                entry.genericArity);
            if (!matchResult.Success)
            {
                return (null, HotReloadMethodOutcome.Failed(methodLabel, matchResult.ErrorMessage, filePath));
            }

            methodLabel = HotReloadPatcher.FormatMethodKey(matchResult.Method);
            (MethodInfo shimMethod, string shimError) = FindShimMethod(shimAssembly, entry);
            if (shimMethod == null)
            {
                return (null, HotReloadMethodOutcome.Failed(methodLabel, shimError, filePath));
            }

            HotReloadPatchResult patchability = HotReloadPatcher.CheckPatchable(matchResult.Method);
            if (!patchability.Success)
            {
                return (null, HotReloadMethodOutcome.Failed(methodLabel, patchability.ErrorMessage, filePath));
            }

            return (
                new ResolvedEntry(
                    entry,
                    methodLabel,
                    filePath,
                    patchShape,
                    matchResult.Method,
                    shimMethod,
                    isAddedMethod: false),
                null);
        }

        private static (MethodInfo ShimMethod, string ErrorMessage) FindShimMethod(
            Assembly shimAssembly,
            TransformWorkerEntryDto entry)
        {
            Type shimType = FindShimType(shimAssembly, entry.shimTypeName);
            if (shimType == null)
            {
                return (null, "Shim type not found in compiled shim assembly: " + entry.shimTypeName);
            }

            MethodInfo shimMethod = shimType.GetMethod(
                entry.shimMethodName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (shimMethod == null)
            {
                shimMethod = shimType.GetMethod(
                    entry.shimMethodName,
                    BindingFlags.Public | BindingFlags.Static);
            }

            if (shimMethod == null)
            {
                return (null, "Shim method not found: " + shimType.Name + "." + entry.shimMethodName);
            }

            return (shimMethod, null);
        }

        private static Type FindShimType(Assembly shimAssembly, string shimTypeName)
        {
            if (string.IsNullOrEmpty(shimTypeName))
            {
                return null;
            }

            Type direct = shimAssembly.GetType(shimTypeName);
            if (direct != null)
            {
                return direct;
            }

            foreach (Type candidate in shimAssembly.GetTypes())
            {
                if (candidate.Name == shimTypeName)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string FormatEntryLabel(TransformWorkerEntryDto entry)
        {
            return HotReloadPatcher.FormatMethodKeyParts(
                entry.typeMetadataName,
                entry.methodName,
                entry.parameterTypeFullNames ?? Array.Empty<string>(),
                entry.genericArity);
        }

        /// <summary>
        /// One preflight-resolved entry ready for registry writes and Harmony Patch.
        /// </summary>
        internal sealed class ResolvedEntry
        {
            public TransformWorkerEntryDto Entry { get; }
            public string MethodLabel { get; }
            public string FilePath { get; }
            public HotReloadPatchShape PatchShape { get; }
            public MethodBase OriginalMethod { get; }
            public MethodInfo ShimMethod { get; }
            public bool IsAddedMethod { get; }

            public ResolvedEntry(
                TransformWorkerEntryDto entry,
                string methodLabel,
                string filePath,
                HotReloadPatchShape patchShape,
                MethodBase originalMethod,
                MethodInfo shimMethod,
                bool isAddedMethod)
            {
                Entry = entry;
                MethodLabel = methodLabel;
                FilePath = filePath;
                PatchShape = patchShape;
                OriginalMethod = originalMethod;
                ShimMethod = shimMethod;
                IsAddedMethod = isAddedMethod;
            }
        }

        /// <summary>
        /// All-resolved entries for apply, or the Failed + AtomicFileSkip outcomes for a file.
        /// </summary>
        internal sealed class Result
        {
            public bool AllResolved { get; }
            public IReadOnlyList<ResolvedEntry> ResolvedEntries { get; }
            public IReadOnlyList<HotReloadMethodOutcome> FailureOutcomes { get; }

            private Result(
                bool allResolved,
                IReadOnlyList<ResolvedEntry> resolvedEntries,
                IReadOnlyList<HotReloadMethodOutcome> failureOutcomes)
            {
                AllResolved = allResolved;
                ResolvedEntries = resolvedEntries ?? Array.Empty<ResolvedEntry>();
                FailureOutcomes = failureOutcomes ?? Array.Empty<HotReloadMethodOutcome>();
            }

            public static Result Succeeded(List<ResolvedEntry> resolvedEntries)
            {
                return new Result(
                    true,
                    resolvedEntries,
                    Array.Empty<HotReloadMethodOutcome>());
            }

            public static Result Failed(List<HotReloadMethodOutcome> failureOutcomes)
            {
                return new Result(
                    false,
                    Array.Empty<ResolvedEntry>(),
                    failureOutcomes);
            }
        }
    }
}
