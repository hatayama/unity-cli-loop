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
    internal sealed class HotReloadEntryApplier
    {
        private readonly HotReloadDomain _domain;
        private readonly HotReloadPatcher _patcher;
        private readonly HotReloadFileEntryApplier _fileEntryApplier;

        internal HotReloadEntryApplier(
            HotReloadDomain domain,
            HotReloadPatcher patcher,
            HotReloadFileEntryApplier fileEntryApplier)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(patcher != null, "patcher must not be null.");
            Debug.Assert(fileEntryApplier != null, "fileEntryApplier must not be null.");
            _domain = domain;
            _patcher = patcher;
            _fileEntryApplier = fileEntryApplier;
        }

        /// <summary>
        /// Applies the group's prepared files against the one compiled shim assembly, and returns
        /// one result per file in the order the files were sent to the worker.
        /// </summary>
        /// <remarks>
        /// Why per file: the shim assembly is shared, but a generation, an added-field ledger and
        /// an apply result all belong to a single file, and a file whose entries cannot be
        /// resolved must not stop its siblings from being applied. The whole group is prepared
        /// (bound and resolved) by an earlier stage, so nothing is mutated while a sibling can
        /// still fail preflight.
        /// </remarks>
        internal IReadOnlyList<HotReloadFileProcessResult> ApplyPreparedEntries(
            HotReloadApplyContext context,
            HotReloadShimCompileResult compileResult,
            IReadOnlyList<HotReloadPreparedGroupFile> preparedFiles)
        {
            Debug.Assert(context != null, "context must not be null.");
            Debug.Assert(compileResult != null, "compileResult must not be null.");
            Debug.Assert(preparedFiles != null, "preparedFiles must not be null.");

            List<HotReloadFileProcessResult> results =
                new List<HotReloadFileProcessResult>(preparedFiles.Count);
            foreach (HotReloadPreparedGroupFile prepared in preparedFiles)
            {
                results.Add(ApplyPreparedFile(context, compileResult, prepared));
            }

            return results;
        }

        private HotReloadFileProcessResult ApplyPreparedFile(
            HotReloadApplyContext context,
            HotReloadShimCompileResult compileResult,
            HotReloadPreparedGroupFile prepared)
        {
            HotReloadGroupFile file = prepared.File;
            if (prepared.Kind == HotReloadGroupFilePreparationKind.SkippedByGroup)
            {
                return _fileEntryApplier.BuildUnappliedResult(file);
            }

            if (prepared.Kind == HotReloadGroupFilePreparationKind.NoEntriesToApply)
            {
                _fileEntryApplier.ClearFileGeneration(context, file);
                return _fileEntryApplier.BuildUnappliedResult(file);
            }

            if (prepared.Kind == HotReloadGroupFilePreparationKind.ResolutionFailed)
            {
                return _fileEntryApplier.BuildResolutionFailedResult(
                    context, file, prepared.Resolution);
            }

            return _fileEntryApplier.ApplyResolvedFileAndBuildResult(
                context, file, compileResult, prepared.Entries, prepared.Resolution);
        }

        /// <summary>
        /// Peels leftover Harmony patches when the source again matches the verified baseline.
        /// Resolve failures are silent: unchanged identities already matched compile-time IL.
        /// A method Harmony could not restore becomes that method's Failed outcome instead of
        /// aborting the peel, so the remaining unchanged methods still get reverted.
        /// Rows whose method name no live patch carries are skipped without being resolved.
        /// Returns how many Revert calls actually removed a live patch.
        /// </summary>
        /// <param name="resolveMethod">
        /// How a row is resolved; production passes HotReloadMethodMatcher.Resolve, and tests
        /// count the calls.
        /// </param>
        internal int RevertUnchangedPatches(
            HotReloadTypeHome fileHome,
            HotReloadEntryHomeResolver homeResolver,
            TransformWorkerUnchangedMethodDto[] unchangedMethods,
            List<HotReloadMethodOutcome> outcomes,
            string assemblyResolvePath,
            HotReloadMethodResolver resolveMethod)
        {
            Debug.Assert(fileHome != null, "fileHome must not be null.");
            Debug.Assert(homeResolver != null, "homeResolver must not be null.");
            Debug.Assert(unchangedMethods != null, "unchangedMethods must not be null.");
            Debug.Assert(outcomes != null, "outcomes must not be null.");
            Debug.Assert(resolveMethod != null, "resolveMethod must not be null.");

            // Why once before the loop: the peel only removes patches, so the names collected here
            // stay a superset of the methods that still hold one while the rows are walked.
            HashSet<string> patchedMethodNames =
                HotReloadUnchangedPeelFilter.CollectPatchedMethodNames(_domain.ListGenerations());
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

                // Why the row decides the home: a method of a type an artifact serves is patched
                // on that artifact, so the leftover patch to peel is only findable there.
                // Why before the name check: a row naming an assembly no artifact carries must stop
                // the run whether or not a patch could be peeled, as it did when every row was
                // resolved.
                HotReloadTypeHome home = homeResolver.Resolve(fileHome, unchanged.homeAssemblyName);

                // Why by name before resolving: resolving a row reads the whole compiled assembly,
                // and a row can only be peeled when the method it resolves to holds a live patch.
                // That method carries the row's name, so a name no live patch has cannot lead to a
                // peel. The name is only a prefilter: a row that passes is still resolved exactly,
                // type, parameters and arity included.
                if (!patchedMethodNames.Contains(unchanged.methodName))
                {
                    continue;
                }

                // Why pass unchanged.genericArity: Caller(int) and Caller<T>(int) share name
                // and parameters. Arity 0 would resolve the generic unchanged row to the
                // non-generic sibling and peel its live patch.
                HotReloadMethodMatchResult matchResult = resolveMethod(
                    home,
                    unchanged.typeMetadataName,
                    unchanged.methodName,
                    unchanged.parameterTypeFullNames,
                    unchanged.genericArity);
                if (!matchResult.Success)
                {
                    continue;
                }

                HotReloadRevertOutcome revertOutcome = _patcher.Revert(
                    matchResult.Method,
                    out string revertFailureReason);
                if (revertOutcome == HotReloadRevertOutcome.Reverted)
                {
                    revertedCount++;
                    continue;
                }

                if (revertOutcome == HotReloadRevertOutcome.UnpatchFailed)
                {
                    outcomes.Add(
                        HotReloadMethodOutcome.Failed(
                            HotReloadMethodKeys.FormatMethodLabel(matchResult.Method),
                            revertFailureReason,
                            assemblyResolvePath));
                }
            }

            return revertedCount;
        }

        /// <summary>
        /// Peels every file's leftover patches on the methods the worker reported unchanged, and
        /// records per file how many patches that removed.
        /// </summary>
        internal void RevertUnchangedPatchesPerFile(
            IReadOnlyList<HotReloadGroupFile> files,
            HotReloadWorkerRowsByFile rows)
        {
            Debug.Assert(files != null, "files must not be null.");
            Debug.Assert(files.Count > 0, "A group run must have at least one file.");

            HotReloadEntryHomeResolver homeResolver =
                new HotReloadEntryHomeResolver(_domain, files[0].ProjectRoot);
            foreach (HotReloadGroupFile file in files)
            {
                // Why a file left unapplied is left alone: a file the shim compile refused keeps
                // the previous run's patches, and the patch on a method this run reports unchanged
                // is one of them. Peeling it would drop what this run never replaced, which is the
                // one thing the file-atomic isolation exists to prevent.
                if (file.SkipApply)
                {
                    continue;
                }

                IReadOnlyList<TransformWorkerUnchangedMethodDto> fileUnchanged =
                    rows.UnchangedFor(file.ProjectRelativePath);
                TransformWorkerUnchangedMethodDto[] unchangedMethods =
                    new TransformWorkerUnchangedMethodDto[fileUnchanged.Count];
                for (int index = 0; index < fileUnchanged.Count; index++)
                {
                    unchangedMethods[index] = fileUnchanged[index];
                }

                file.RevertedUnchangedCount = RevertUnchangedPatches(
                    file.Home,
                    homeResolver,
                    unchangedMethods,
                    file.Sinks.Outcomes,
                    file.AssemblyResolvePath,
                    HotReloadMethodMatcher.Resolve);
            }
        }
        /// <summary>
        /// Invokes each shim type's binder (emitted when the type carries at least one accessor
        /// delegate) once, before any patch is applied, so no shim body or closure that calls an
        /// accessor can run with unbound accessor delegates. Returns bind failures keyed
        /// by shim type name; an entry of a failed type, whatever its patch kind, fails its whole
        /// file (that entry is Failed, the file's other entries Skipped) instead of being patched
        /// or registered.
        /// Internal so tests can pin the failure contract directly — an end-to-end bind failure
        /// cannot be fabricated once shim compilation has succeeded against the same assembly.
        /// </summary>
        internal Dictionary<string, string> BindShimAccessors(Assembly shimAssembly)
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
