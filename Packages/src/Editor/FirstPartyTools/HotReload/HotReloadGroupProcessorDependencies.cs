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
        private static HotReloadGroupProcessorDependencies current = CreateProduction();

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

        internal static HotReloadGroupProcessorDependencies Current => current;

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

        internal static HotReloadGroupProcessorDependencies CreateProduction()
        {
            return new HotReloadGroupProcessorDependencies(
                HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                HotReloadIntroducedTypePreparation.PrepareAsync,
                TransformWorkerClient.RunAsync,
                HotReloadGroupProcessor.GateAndCompileAsync,
                HotReloadGroupEntryPreparation.PrepareGroup,
                // A lambda, not a method group: the static field below is built once per domain
                // reload, and a captured instance would keep applying into the domain that was
                // installed then, while the transpilers read whichever domain is installed now.
                (context, compileResult, preparedFiles) =>
                    HotReloadCompositionRoot.Services.EntryApplier.ApplyPreparedEntries(
                        context, compileResult, preparedFiles));
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

        /// <summary>
        /// Installs a replacement for the duration of the returned scope and puts the previous
        /// one back when it closes.
        /// </summary>
        internal static IDisposable BeginReplacement(HotReloadGroupProcessorDependencies replacement)
        {
            if (replacement == null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            HotReloadGroupProcessorDependencies previous = current;
            current = replacement;
            return new ReplacementScope(previous);
        }

        private sealed class ReplacementScope : IDisposable
        {
            private readonly HotReloadGroupProcessorDependencies previous;
            private bool restored;

            public ReplacementScope(HotReloadGroupProcessorDependencies previous)
            {
                this.previous = previous;
            }

            public void Dispose()
            {
                if (restored)
                {
                    return;
                }

                restored = true;
                current = previous;
            }
        }
    }
}
