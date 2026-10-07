/// <summary>
/// What keeps a use of an internal member of an unpassed type out of the patched method's reach,
/// which decides the change its skip reason ends with.
/// </summary>
internal enum UnpassedInternalMemberUseForm
{
    /// <summary>
    /// Only a compile brings the use in: it runs in an async or iterator state machine or a
    /// delegating shim, or it is a kind of use hot reload does not patch in place.
    /// </summary>
    OutOfReach,

    /// <summary>A bare name in the method's own statements, which a qualified name would reach.</summary>
    BareName,

    /// <summary>
    /// A use inside a lambda, local function or query, or next to one that works with a value the
    /// worker could not resolve, which runs outside the patched method however it is qualified.
    /// </summary>
    InsideClosure
}
