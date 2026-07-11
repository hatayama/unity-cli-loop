namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Why a source pause point could not be patched into a resolved method.
    /// </summary>
    internal enum SourcePausePointPatchFailureReason
    {
        None,
        AssemblyNotLoaded,
        UnpatchableAbstract,
        UnpatchableExtern,
        UnpatchableOpenGeneric,
        UnpatchableBurstCompiled,
    }
}
