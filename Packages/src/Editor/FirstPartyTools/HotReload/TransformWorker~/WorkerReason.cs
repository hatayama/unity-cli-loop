using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// Keep in sync with TransformWorkerDtos.cs TransformWorkerReasonDto.
// A reason this worker reports, as the code that identifies it plus the values its sentence
// needs. The English sentence is built on the Editor side, so this process never spells one out.
internal sealed class WorkerReason
{
    public HotReloadWorkerReasonCode Code { get; set; }

    // Values the sentence substitutes, in the order they appear in it. Null when it takes none.
    public string[] Args { get; set; }

    // The fragment a composed reason ends with. Null when the code does not compose.
    public WorkerReason Detail { get; set; }

    internal static WorkerReason Of(HotReloadWorkerReasonCode code, params string[] args)
    {
        return new WorkerReason
        {
            Code = code,
            Args = args != null && args.Length > 0 ? args : null
        };
    }

    internal static WorkerReason Composite(HotReloadWorkerReasonCode code, WorkerReason detail)
    {
        return new WorkerReason { Code = code, Detail = detail };
    }
}
