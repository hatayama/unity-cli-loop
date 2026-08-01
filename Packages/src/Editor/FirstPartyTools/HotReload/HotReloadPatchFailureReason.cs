namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Why a hot-reload transplant patch could not be applied.
    /// </summary>
    internal enum HotReloadPatchFailureReason
    {
        None,
        UnpatchableAbstract,
        UnpatchableExtern,
        UnpatchableOpenGeneric,
        UnpatchableBurstCompiled,
        UnpatchableValueType,
        NullMethod,
        NullShimMethod,
    }
}
