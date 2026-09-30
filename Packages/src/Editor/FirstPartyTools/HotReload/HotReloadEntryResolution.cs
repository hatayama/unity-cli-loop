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
        // Why bindFailures is passed in: one shim assembly serves every file of a group, so its
        // accessor binders run once for the group instead of once per file.
        // Why addedCallees is passed in: a body can call an added member another file of the group
        // declares, so one file's entries alone cannot name every call.
        internal static Result ResolveEntries(
            HotReloadTypeHome fileHome,
            HotReloadEntryHomeResolver homeResolver,
            string filePath,
            Assembly shimAssembly,
            TransformWorkerEntryDto[] entriesToPatch,
            Dictionary<string, string> bindFailures,
            HotReloadAddedCalleeIndex addedCallees)
        {
            Debug.Assert(fileHome != null, "fileHome must not be null.");
            Debug.Assert(homeResolver != null, "homeResolver must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be empty.");
            Debug.Assert(shimAssembly != null, "shimAssembly must not be null.");
            Debug.Assert(entriesToPatch != null, "entriesToPatch must not be null.");
            Debug.Assert(bindFailures != null, "bindFailures must not be null.");
            Debug.Assert(addedCallees != null, "addedCallees must not be null.");

            List<ResolvedEntry> resolvedEntries = new List<ResolvedEntry>();
            for (int index = 0; index < entriesToPatch.Length; index++)
            {
                ResolvedEntryOutcome entryOutcome = TryResolveEntry(
                    entriesToPatch[index],
                    fileHome,
                    homeResolver,
                    shimAssembly,
                    bindFailures,
                    addedCallees,
                    filePath);
                if (entryOutcome.IsFailure)
                {
                    return Result.Failed(
                        BuildAtomicFailureOutcomes(entriesToPatch, index, entryOutcome.Failure, filePath));
                }

                resolvedEntries.Add(entryOutcome.Resolved);
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

        private static ResolvedEntryOutcome TryResolveEntry(
            TransformWorkerEntryDto entry,
            HotReloadTypeHome fileHome,
            HotReloadEntryHomeResolver homeResolver,
            Assembly shimAssembly,
            IReadOnlyDictionary<string, string> bindFailures,
            HotReloadAddedCalleeIndex addedCallees,
            string filePath)
        {
            string methodLabel = FormatEntryLabel(entry);
            // Why a call the group declares no added member for fails the file like a missing shim
            // does: every added member an applied body calls comes from the same worker output, so
            // such a call means the worker and this Editor disagree, and recording the entry
            // without it would hide the call from the check for retired callees.
            (IReadOnlyList<HotReloadCalledAddedMember> calledAddedMembers, string calleeError) =
                addedCallees.Resolve(entry);
            if (calleeError != null)
            {
                return ResolvedEntryOutcome.Failed(
                    HotReloadMethodOutcome.Failed(methodLabel, calleeError, filePath));
            }

            if (entry.patchKind == HotReloadConstants.PatchKindAddedMethod)
            {
                return TryResolveAddedMethod(
                    entry,
                    methodLabel,
                    shimAssembly,
                    bindFailures,
                    calledAddedMembers,
                    filePath);
            }

            return TryResolveExistingMethod(
                entry,
                methodLabel,
                fileHome,
                homeResolver,
                shimAssembly,
                bindFailures,
                calledAddedMembers,
                filePath);
        }

        private static ResolvedEntryOutcome TryResolveAddedMethod(
            TransformWorkerEntryDto entry,
            string methodLabel,
            Assembly shimAssembly,
            IReadOnlyDictionary<string, string> bindFailures,
            IReadOnlyList<HotReloadCalledAddedMember> calledAddedMembers,
            string filePath)
        {
            if (bindFailures.TryGetValue(entry.shimTypeName ?? string.Empty, out string bindFailureReason))
            {
                return ResolvedEntryOutcome.Failed(
                    HotReloadMethodOutcome.Failed(methodLabel, bindFailureReason, filePath));
            }

            (MethodInfo shimMethod, string shimError) = FindShimMethod(shimAssembly, entry);
            if (shimMethod == null)
            {
                return ResolvedEntryOutcome.Failed(
                    HotReloadMethodOutcome.Failed(methodLabel, shimError, filePath));
            }

            (FieldInfo invocationCounter, string counterError) = FindInvocationCounter(shimMethod);
            if (invocationCounter == null)
            {
                return ResolvedEntryOutcome.Failed(
                    HotReloadMethodOutcome.Failed(methodLabel, counterError, filePath));
            }

            return ResolvedEntryOutcome.Succeeded(
                new ResolvedEntry(
                    entry,
                    methodLabel,
                    filePath,
                    HotReloadPatchShape.Transplant,
                    originalMethod: null,
                    shimMethod,
                    isAddedMethod: true,
                    invocationCounter,
                    calledAddedMembers));
        }

        // Why a missing counter fails the file like a missing shim does: the counter is what
        // --status reports as the member's InvocationCount, and a shim without it means the worker
        // and this Editor disagree on the shape of an added-member shim.
        private static (FieldInfo InvocationCounter, string ErrorMessage) FindInvocationCounter(
            MethodInfo shimMethod)
        {
            Type shimType = shimMethod.DeclaringType;
            string counterName = shimMethod.Name + HotReloadConstants.AddedMemberInvocationCounterSuffix;
            FieldInfo counter = shimType.GetField(
                counterName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (counter == null)
            {
                return (null, "Invocation counter not found: " + shimType.Name + "." + counterName);
            }

            if (!HotReloadAddedMemberInfo.IsReadableInvocationCounter(counter))
            {
                return (null, "Invocation counter is not a static long: " + shimType.Name + "." + counterName);
            }

            return (counter, null);
        }

        private static ResolvedEntryOutcome TryResolveExistingMethod(
            TransformWorkerEntryDto entry,
            string methodLabel,
            HotReloadTypeHome fileHome,
            HotReloadEntryHomeResolver homeResolver,
            Assembly shimAssembly,
            IReadOnlyDictionary<string, string> bindFailures,
            IReadOnlyList<HotReloadCalledAddedMember> calledAddedMembers,
            string filePath)
        {
            HotReloadPatchShape patchShape = entry.patchKind == HotReloadConstants.PatchKindDelegation
                ? HotReloadPatchShape.Delegation
                : HotReloadPatchShape.Transplant;
            if (patchShape == HotReloadPatchShape.Delegation
                && bindFailures.TryGetValue(entry.shimTypeName ?? string.Empty, out string bindFailureReason))
            {
                return ResolvedEntryOutcome.Failed(
                    HotReloadMethodOutcome.Failed(methodLabel, bindFailureReason, filePath));
            }

            string[] parameterTypeFullNames = entry.parameterTypeFullNames ?? Array.Empty<string>();
            // Why the row decides the home: the method an entry replaces can live in an
            // introduced-type artifact this domain retains rather than in the assembly the
            // edited file belongs to, and only the row knows which.
            HotReloadTypeHome home = homeResolver.Resolve(fileHome, entry.homeAssemblyName);
            HotReloadMethodMatchResult matchResult = HotReloadMethodMatcher.Resolve(
                home,
                entry.typeMetadataName,
                entry.methodName,
                parameterTypeFullNames,
                entry.genericArity);
            if (!matchResult.Success)
            {
                return ResolvedEntryOutcome.Failed(
                    HotReloadMethodOutcome.Failed(methodLabel, matchResult.ErrorMessage, filePath));
            }

            methodLabel = HotReloadMethodKeys.FormatMethodLabel(matchResult.Method);
            (MethodInfo shimMethod, string shimError) = FindShimMethod(shimAssembly, entry);
            if (shimMethod == null)
            {
                return ResolvedEntryOutcome.Failed(
                    HotReloadMethodOutcome.Failed(methodLabel, shimError, filePath));
            }

            HotReloadPatchResult patchability = HotReloadPatcher.CheckPatchable(matchResult.Method);
            if (!patchability.Success)
            {
                return ResolvedEntryOutcome.Failed(
                    HotReloadMethodOutcome.Failed(methodLabel, patchability.ErrorMessage, filePath));
            }

            return ResolvedEntryOutcome.Succeeded(
                new ResolvedEntry(
                    entry,
                    methodLabel,
                    filePath,
                    patchShape,
                    matchResult.Method,
                    shimMethod,
                    isAddedMethod: false,
                    invocationCounter: null,
                    calledAddedMembers));
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

        // Why internal: the added-callee index records calls in this same label, the one an added
        // member is registered under, so the two cannot drift apart.
        internal static string FormatEntryLabel(TransformWorkerEntryDto entry)
        {
            return HotReloadMethodKeys.FormatMethodLabelParts(
                new HotReloadMetadataTypeName(entry.typeMetadataName),
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

            /// <summary>
            /// The counter declared beside an added method's shim, or null for an entry that
            /// patches an existing method.
            /// </summary>
            public FieldInfo InvocationCounter { get; }

            /// <summary>The added members this entry's body calls; empty when it calls none.</summary>
            public IReadOnlyList<HotReloadCalledAddedMember> CalledAddedMembers { get; }

            public ResolvedEntry(
                TransformWorkerEntryDto entry,
                string methodLabel,
                string filePath,
                HotReloadPatchShape patchShape,
                MethodBase originalMethod,
                MethodInfo shimMethod,
                bool isAddedMethod,
                FieldInfo invocationCounter,
                IReadOnlyList<HotReloadCalledAddedMember> calledAddedMembers)
            {
                Debug.Assert(calledAddedMembers != null, "calledAddedMembers must not be null.");

                Entry = entry;
                MethodLabel = methodLabel;
                FilePath = filePath;
                PatchShape = patchShape;
                OriginalMethod = originalMethod;
                ShimMethod = shimMethod;
                IsAddedMethod = isAddedMethod;
                InvocationCounter = invocationCounter;
                CalledAddedMembers = calledAddedMembers;
            }
        }

        /// <summary>
        /// One entry's preflight result: the resolved entry, or the outcome that failed it.
        /// </summary>
        private sealed class ResolvedEntryOutcome
        {
            public ResolvedEntry Resolved { get; }
            public HotReloadMethodOutcome Failure { get; }

            public bool IsFailure => Failure != null;

            private ResolvedEntryOutcome(ResolvedEntry resolved, HotReloadMethodOutcome failure)
            {
                Resolved = resolved;
                Failure = failure;
            }

            public static ResolvedEntryOutcome Succeeded(ResolvedEntry resolved)
            {
                Debug.Assert(resolved != null, "resolved must not be null.");

                return new ResolvedEntryOutcome(resolved, null);
            }

            public static ResolvedEntryOutcome Failed(HotReloadMethodOutcome failure)
            {
                Debug.Assert(failure != null, "failure must not be null.");

                return new ResolvedEntryOutcome(null, failure);
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
