namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One compiled instruction that references a target method, reported under its logical owner.
    /// </summary>
    internal sealed class HotReloadCallSiteHit
    {
        public string CallerAssemblyName;
        public HotReloadMetadataTypeName CallerTypeMetadataName;
        public string CallerMethodName;
        public string[] CallerParameterTypeFullNames;
        public int CallerGenericArity;
        public string CallerMethodKey;
        public string TargetMethodKey;
        public bool IsFunctionPointerLoad;
    }
}
