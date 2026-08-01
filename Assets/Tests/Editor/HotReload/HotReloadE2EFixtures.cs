namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Base type used by the e2e fixture to exercise the worker's base-call skip path.
    /// </summary>
    public class HotReloadE2EBase
    {
        protected int BaseSeed()
        {
            return 1;
        }
    }

    /// <summary>
    /// Compiled fixture whose on-disk source path is passed as <c>files[]</c> to the
    /// orchestrator. Edited copies for worker input live under
    /// <c>Library/UloopHotReload/TestSources/</c> (never under Assets).
    /// </summary>
    public class HotReloadE2EFixture : HotReloadE2EBase
    {
        private int _secret = 10;

        public int SecretForAssert => _secret;

        // Sentinel body: hot reload must replace this with a private-touching shim that returns
        // _secret + delta + 100.
        public int ComputeWithPrivate(int delta)
        {
            return _secret + delta;
        }

        // Contains base. — worker must skip with an explicit reason (not an error).
        public int CallsBase()
        {
            return base.BaseSeed() + 1;
        }

        // Edited copy will call a non-existent helper so shim compile fails with a new-member hint.
        public int CallsMissingHelper(int value)
        {
            return value;
        }
    }
}
