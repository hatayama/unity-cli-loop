using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Assembly = System.Reflection.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Applies worker entries: bind accessors, Harmony patch/revert, added-method register.
    /// </summary>
    internal static class HotReloadEntryApplier
    {
        /// <summary>
        /// Applies the group's entries file by file against the one compiled shim assembly, and
        /// returns one result per file in the order the files were sent to the worker.
        /// </summary>
        /// <remarks>
        /// Why per file: the shim assembly is shared, but a generation, an added-field ledger and
        /// an apply result all belong to a single file, and a file whose entries cannot be
        /// resolved must not stop its siblings from being applied.
        /// </remarks>
        internal static IReadOnlyList<HotReloadFileProcessResult> ApplyGroupAndBuildResults(
            HotReloadApplyContext context,
            HotReloadShimCompileResult compileResult,
            TransformWorkerEntryDto[] entriesToPatch)
        {
            Debug.Assert(context != null, "context must not be null.");
            Debug.Assert(compileResult != null, "compileResult must not be null.");
            Debug.Assert(entriesToPatch != null, "entriesToPatch must not be null.");

            Dictionary<string, List<TransformWorkerEntryDto>> entriesByFile =
                HotReloadWorkerRowsByFile.GroupEntriesBySourceFile(entriesToPatch, context.ProjectRelativePaths);
            // Why once for the group: every shim type of the group lives in this one assembly, so
            // binding per file would re-run the same binders and hide which file first failed.
            Dictionary<string, string> bindFailures = BindShimAccessors(compileResult.Assembly);
            List<HotReloadFileProcessResult> results =
                new List<HotReloadFileProcessResult>(context.Files.Count);
            foreach (HotReloadGroupFile file in context.Files)
            {
                if (file.SkipApply)
                {
                    results.Add(HotReloadFileEntryApplier.BuildUnappliedResult(file));
                    continue;
                }

                List<TransformWorkerEntryDto> fileEntries = entriesByFile[file.ProjectRelativePath];
                if (fileEntries.Count == 0)
                {
                    HotReloadFileEntryApplier.ClearFileGeneration(context, file);
                    results.Add(HotReloadFileEntryApplier.BuildUnappliedResult(file));
                    continue;
                }

                results.Add(
                    HotReloadFileEntryApplier.ApplyFileAndBuildResult(
                        context,
                        file,
                        compileResult,
                        fileEntries.ToArray(),
                        bindFailures));
            }

            return results;
        }

        // Why only here and the empty-entries deactivation: a failed worker or shim compile
        // returns empty AddedFieldNames while leaving existing patches, so writing the ledger
        // from the run response would wipe added fields that are still live.
        internal static void CommitAddedFieldsForFile(string projectRelativePath, string[] addedFieldNames)
        {
            HotReloadAddedFieldRegistry.ReplaceForFile(
                projectRelativePath,
                addedFieldNames ?? Array.Empty<string>());
        }

        // Peels leftover Harmony patches when the source again matches the verified baseline.
        // Resolve failures are silent: unchanged identities already matched compile-time IL.
        // Returns how many Revert calls actually removed a live patch.
        internal static int RevertUnchangedPatches(
            string assemblyName,
            TransformWorkerUnchangedMethodDto[] unchangedMethods)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(unchangedMethods != null, "unchangedMethods must not be null.");

            int revertedCount = 0;
            for (int index = 0; index < unchangedMethods.Length; index++)
            {
                TransformWorkerUnchangedMethodDto unchanged = unchangedMethods[index];
                if (unchanged == null
                    || string.IsNullOrEmpty(unchanged.typeMetadataName)
                    || string.IsNullOrEmpty(unchanged.methodName)
                    || unchanged.parameterTypeFullNames == null)
                {
                    continue;
                }

                // Why pass unchanged.genericArity: Caller(int) and Caller<T>(int) share name
                // and parameters. Arity 0 would resolve the generic unchanged row to the
                // non-generic sibling and peel its live patch.
                HotReloadMethodMatchResult matchResult = HotReloadMethodMatcher.Resolve(
                    assemblyName,
                    unchanged.typeMetadataName,
                    unchanged.methodName,
                    unchanged.parameterTypeFullNames,
                    unchanged.genericArity);
                if (!matchResult.Success)
                {
                    continue;
                }

                if (HotReloadPatcher.Revert(matchResult.Method))
                {
                    revertedCount++;
                }
            }

            return revertedCount;
        }

        /// <summary>
        /// Invokes each shim type's binder (emitted when the type carries at least one accessor
        /// delegate) once, before any patch is applied, so no delegation shim or added-method
        /// accessor rewrite can run with unbound accessor delegates. Returns bind failures keyed
        /// by shim type name; every delegation entry and added-method entry in a failed type
        /// becomes Failed instead of being patched or registered.
        /// Internal so tests can pin the failure contract directly — an end-to-end bind failure
        /// cannot be fabricated once shim compilation has succeeded against the same assembly.
        /// </summary>
        internal static Dictionary<string, string> BindShimAccessors(Assembly shimAssembly)
        {
            Debug.Assert(shimAssembly != null, "shimAssembly must not be null.");

            Dictionary<string, string> failureReasonByShimTypeName = new Dictionary<string, string>();
            foreach (Type shimType in shimAssembly.GetTypes())
            {
                MethodInfo bindMethod = shimType.GetMethod(
                    HotReloadConstants.ShimBindAccessorsMethodName,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null);
                if (bindMethod == null)
                {
                    continue;
                }

                try
                {
                    bindMethod.Invoke(null, null);
                }
                catch (TargetInvocationException invocationException)
                {
                    // Approved deviation from the no-try-catch rule: a bind failure (the source
                    // references a member the compiled assembly does not have yet) is an expected
                    // per-type outcome that must fail that type's methods with a remediation hint,
                    // not crash the whole hot-reload run. Nothing is swallowed — the cause becomes
                    // the Failed reason for every affected method.
                    Exception cause = invocationException.InnerException ?? invocationException;
                    failureReasonByShimTypeName[shimType.Name] =
                        "Accessor binding failed for shim type '" + shimType.Name + "': "
                        + cause.Message + " Run 'uloop compile' and retry.";
                }
            }

            return failureReasonByShimTypeName;
        }
    }
}
