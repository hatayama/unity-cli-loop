namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled caller in the cross-assembly fixture, so a scan of the E2E host's method has one
    /// caller dll to refuse.
    /// </summary>
    public static class HotReloadCallerNoteBackfillE2ECaller
    {
        public static int Call()
        {
            return new HotReloadCallerNoteBackfillE2EFixture().Read();
        }
    }
}
