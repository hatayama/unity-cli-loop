using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The stages one group run calls out to, so a test can observe the production pipeline's
    /// order instead of reimplementing it.
    /// </summary>
    internal sealed class HotReloadGroupProcessorDependencies
    {
        private HotReloadGroupProcessorDependencies(
            Func<IReadOnlyList<HotReloadGroupFile>, bool> validateNewSourceMembership,
            Func<IReadOnlyList<HotReloadGroupFile>, TransformWorkerInputDto, CancellationToken,
                Task<HotReloadIntroducedTypePreparationResult>> prepareIntroducedTypes,
            Func<TransformWorkerInputDto, CancellationToken, Task<TransformWorkerClientResult>> runWorker,
            Func<HotReloadApplyContext, CancellationToken, Task<HotReloadGroupGateAndCompileResult>> gateAndCompile,
            Func<HotReloadApplyContext, HotReloadShimCompileResult, TransformWorkerEntryDto[],
                IReadOnlyList<HotReloadPreparedGroupFile>> prepareGroupEntries,
            Func<HotReloadApplyContext, HotReloadShimCompileResult, IReadOnlyList<HotReloadPreparedGroupFile>,
                IReadOnlyList<HotReloadFileProcessResult>> applyPreparedEntries)
        {
            ValidateNewSourceMembership = validateNewSourceMembership
                ?? throw new ArgumentNullException(nameof(validateNewSourceMembership));
            PrepareIntroducedTypes = prepareIntroducedTypes
                ?? throw new ArgumentNullException(nameof(prepareIntroducedTypes));
            RunWorker = runWorker ?? throw new ArgumentNullException(nameof(runWorker));
            GateAndCompile = gateAndCompile ?? throw new ArgumentNullException(nameof(gateAndCompile));
            PrepareGroupEntries = prepareGroupEntries
                ?? throw new ArgumentNullException(nameof(prepareGroupEntries));
            ApplyPreparedEntries = applyPreparedEntries
                ?? throw new ArgumentNullException(nameof(applyPreparedEntries));
        }

        /// <summary>
        /// Confirms the group's files still belong to the assembly they were resolved against.
        /// </summary>
        internal Func<IReadOnlyList<HotReloadGroupFile>, bool> ValidateNewSourceMembership { get; }

        /// <summary>
        /// Compiles the types this run introduces into a retained artifact before the transform run.
        /// </summary>
        internal Func<IReadOnlyList<HotReloadGroupFile>, TransformWorkerInputDto, CancellationToken,
            Task<HotReloadIntroducedTypePreparationResult>> PrepareIntroducedTypes { get; }

        /// <summary>
        /// Runs the transform worker for the group.
        /// </summary>
        internal Func<TransformWorkerInputDto, CancellationToken, Task<TransformWorkerClientResult>> RunWorker { get; }

        /// <summary>
        /// Runs the signature-change gate and the group's shim compile.
        /// </summary>
        internal Func<HotReloadApplyContext, CancellationToken, Task<HotReloadGroupGateAndCompileResult>>
            GateAndCompile { get; }

        /// <summary>
        /// Resolves every file of the group against the compiled shim before anything is mutated.
        /// </summary>
        internal Func<HotReloadApplyContext, HotReloadShimCompileResult, TransformWorkerEntryDto[],
            IReadOnlyList<HotReloadPreparedGroupFile>> PrepareGroupEntries { get; }

        /// <summary>
        /// Applies the resolved files: the commit boundary of a group run.
        /// </summary>
        internal Func<HotReloadApplyContext, HotReloadShimCompileResult, IReadOnlyList<HotReloadPreparedGroupFile>,
            IReadOnlyList<HotReloadFileProcessResult>> ApplyPreparedEntries { get; }

        /// <summary>
        /// The production stages, bound to the services of one domain.
        /// </summary>
        internal static HotReloadGroupProcessorDependencies CreateProduction(
            HotReloadGroupStageCollaborators collaborators)
        {
            return new HotReloadGroupProcessorDependencies(
                files => HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure(collaborators, files),
                (files, input, ct) =>
                    HotReloadIntroducedTypePreparation.PrepareAsync(collaborators, files, input, ct),
                collaborators.TransformWorkerClient.RunAsync,
                (context, ct) => HotReloadGroupProcessor.GateAndCompileAsync(collaborators, context, ct),
                (context, compileResult, entriesToPatch) => HotReloadGroupEntryPreparation.PrepareGroup(
                    collaborators, context, compileResult, entriesToPatch),
                collaborators.EntryApplier.ApplyPreparedEntries);
        }

        internal static HotReloadGroupProcessorDependencies Create(
            Func<IReadOnlyList<HotReloadGroupFile>, bool> validateNewSourceMembership,
            Func<IReadOnlyList<HotReloadGroupFile>, TransformWorkerInputDto, CancellationToken,
                Task<HotReloadIntroducedTypePreparationResult>> prepareIntroducedTypes,
            Func<TransformWorkerInputDto, CancellationToken, Task<TransformWorkerClientResult>> runWorker,
            Func<HotReloadApplyContext, CancellationToken, Task<HotReloadGroupGateAndCompileResult>> gateAndCompile,
            Func<HotReloadApplyContext, HotReloadShimCompileResult, TransformWorkerEntryDto[],
                IReadOnlyList<HotReloadPreparedGroupFile>> prepareGroupEntries,
            Func<HotReloadApplyContext, HotReloadShimCompileResult, IReadOnlyList<HotReloadPreparedGroupFile>,
                IReadOnlyList<HotReloadFileProcessResult>> applyPreparedEntries)
        {
            return new HotReloadGroupProcessorDependencies(
                validateNewSourceMembership,
                prepareIntroducedTypes,
                runWorker,
                gateAndCompile,
                prepareGroupEntries,
                applyPreparedEntries);
        }

    }
}
