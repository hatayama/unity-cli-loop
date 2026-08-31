namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Provides an inactive conditional region immediately before a hot-reloadable method.
    /// </summary>
    public sealed class HotReloadDisabledTextFixture
    {
        private bool _plainField;
#if ULOOP_TEST_NEVER_DEFINED_SYMBOL
        private bool _uloopDisabledGuardedField;

        private void UloopDisabledGuardedMethod()
        {
            _uloopDisabledGuardedField = true;
        }
#endif

        public bool GuardedNeighborMethod()
        {
            bool guardedNeighbor = _plainField;
            return guardedNeighbor;
        }
    }
}
