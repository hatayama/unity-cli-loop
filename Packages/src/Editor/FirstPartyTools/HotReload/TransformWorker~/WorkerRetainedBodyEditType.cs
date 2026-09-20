// A type of one edited file that a retained artifact serves and whose ordinary method bodies this
// edit changed. The declaration stays in the binding tree so the edited bodies can be transformed
// against it, and these rows are what tell the emit stages that the patch belongs on the retained
// assembly and covers only the methods the fingerprint comparison named.
internal sealed class WorkerRetainedBodyEditType
{
    public string MetadataName { get; set; }

    // Identity of the assembly the type was declared for before it was served from an artifact.
    // The artifact is indexed under it, so both halves are needed to find the retained type.
    public string OriginalAssemblyName { get; set; }

    public string OriginalAssemblyMvid { get; set; }

    // Syntax method keys of the ordinary methods whose body changed. Every other method of the
    // type still runs the body the retained assembly holds.
    public string[] ChangedMethodKeys { get; set; }

    // Syntax property keys of the properties whose getter is their only accessor with a body and
    // whose body changed. Kept apart from the method keys because the two are spelled in
    // namespaces of their own, and a key read out of the wrong one would name a member the emit
    // stage never looks for.
    public string[] ChangedGetterPropertyKeys { get; set; }
}
