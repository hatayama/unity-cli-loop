/// <summary>
/// Where a use of an internal member of an unpassed type runs and how it is named. The guard reads it,
/// with its own reach rules, to choose the change the skip reason ends with.
/// </summary>
internal enum UnpassedInternalMemberUseForm
{
    /// <summary>
    /// A use in an async or iterator state machine or a delegating shim, where neither moving it nor
    /// qualifying it brings it in, or a use named with its receiver in the method's own statements.
    /// </summary>
    OutOfReach,

    /// <summary>A bare name in the method's own statements.</summary>
    BareName,

    /// <summary>
    /// A use inside a lambda, local function or query, or next to one that works with a value the
    /// worker could not resolve, which runs outside the patched method however it is qualified.
    /// </summary>
    InsideClosure
}
