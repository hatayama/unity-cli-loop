using System;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Callbacks an end-to-end test hangs on the stages of a run, to read a stage input or result
    /// the orchestrator result does not report.
    /// </summary>
    /// <remarks>
    /// Why callbacks that only record: they run inside the production stages, where a failed
    /// assertion would be turned into a failed row of the run instead of failing the test.
    /// </remarks>
    internal sealed class HotReloadIntroducedTypeStageProbe
    {
        /// <summary>Called with the worker input the preparation receives, before it runs.</summary>
        internal Action<TransformWorkerInputDto> BeforePrepare { get; set; }

        /// <summary>Called with the same worker input and the preparation's result, after it ran.</summary>
        internal Action<TransformWorkerInputDto, HotReloadIntroducedTypePreparationResult> AfterPrepare { get; set; }

        /// <summary>Called with the worker input of the transform run, before it runs.</summary>
        internal Action<TransformWorkerInputDto> BeforeTransform { get; set; }

        /// <summary>Called with the context the gate and the shim compilation run on.</summary>
        internal Action<HotReloadApplyContext> BeforeGate { get; set; }
    }
}
