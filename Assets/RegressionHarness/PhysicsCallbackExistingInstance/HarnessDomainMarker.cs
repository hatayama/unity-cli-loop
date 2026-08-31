namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    // A static readonly value is re-initialized whenever the AppDomain reloads, so comparing this
    // value before and after a EditorUtility.RequestScriptReload() call is a deterministic way to
    // detect that the reload has actually completed -- unlike polling for IPC responsiveness alone,
    // which can observe the pre-reload domain still answering before the reload has even started
    // (RequestScriptReload queues the reload for a later editor update, it does not run inline).
    public static class HarnessDomainMarker
    {
        public static readonly string Id = System.Guid.NewGuid().ToString();
    }
}
