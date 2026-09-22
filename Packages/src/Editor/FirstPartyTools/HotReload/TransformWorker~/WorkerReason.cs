using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// A reason this worker reports, as the code that identifies it plus the values its sentence
// needs. The English sentence is built on the Editor side, so this process never spells one out.
internal sealed class WorkerReason
{
    public HotReloadWorkerReasonCode Code { get; set; }

    // Values the sentence substitutes, in the order they appear in it. Null when it takes none.
    public string[] Args { get; set; }

    // The fragment a composed reason ends with. Null when the code does not compose.
    public WorkerReason Detail { get; set; }

    // Metadata names of the compiled types the sentence names, which only the Editor can resolve
    // to their files. Null when the reason names none.
    public string[] TypeMetadataNames { get; set; }

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

    // A reason whose sentence names compiled types the Editor still has to resolve to files. The
    // Editor appends the sentence's last value from those files, so args leave it out.
    internal static WorkerReason NamingCompiledTypes(
        HotReloadWorkerReasonCode code,
        string[] typeMetadataNames,
        params string[] args)
    {
        return new WorkerReason
        {
            Code = code,
            Args = args,
            TypeMetadataNames = typeMetadataNames
        };
    }

    // The same as Composite for a sentence that also substitutes values of its own. It is a
    // separate name because an overload would let a caller drop the args by accident.
    internal static WorkerReason CompositeWithArgs(
        HotReloadWorkerReasonCode code,
        WorkerReason detail,
        params string[] args)
    {
        return new WorkerReason
        {
            Code = code,
            Args = args != null && args.Length > 0 ? args : null,
            Detail = detail
        };
    }
}
