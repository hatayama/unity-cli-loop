/// <summary>
/// Hands out the shim type and shim method names of one worker run.
/// </summary>
// Why one object for the whole group rather than a counter per file: two files of a group may
// declare types of the same name, so both counters must keep running across units for the names
// they produce to stay unique.
internal sealed class ShimNameAllocator
{
    private int _shimTypeCounter;
    private int _shimMethodCounter;

    /// <summary>The next shim type name for a host type, consuming one type number.</summary>
    internal string NextShimTypeName(string hostTypeName)
    {
        string shimTypeName = hostTypeName + "_UloopHotReloadShims_" + _shimTypeCounter;
        _shimTypeCounter++;
        return shimTypeName;
    }

    /// <summary>The next shim method name for a member, consuming one method number.</summary>
    internal string NextShimMethodName(string memberName)
    {
        string shimMethodName = memberName + "__shim" + _shimMethodCounter;
        _shimMethodCounter++;
        return shimMethodName;
    }
}
