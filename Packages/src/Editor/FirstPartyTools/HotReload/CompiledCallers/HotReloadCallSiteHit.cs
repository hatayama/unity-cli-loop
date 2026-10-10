namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One compiled instruction that references a target method, reported under its logical owner.
    /// </summary>
    public sealed class HotReloadCallSiteHit
    {
        public string CallerAssemblyName;
        public HotReloadMetadataTypeName CallerTypeMetadataName;
        public string CallerMethodName;
        public string[] CallerParameterTypeFullNames;
        // Hot-reload code outside the analysis does not read these two.
        internal int CallerGenericArity;
        public string CallerMethodKey;
        public string TargetMethodKey;
        internal bool IsFunctionPointerLoad;
    }
}
