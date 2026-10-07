namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.GlobalUsingBase
{
    /// <summary>
    /// Base type reachable only through the test assembly's global using, so a derived
    /// fixture binds it without a file-level using directive. Public because the worker
    /// compilation cannot see internal types of the compiled assembly.
    /// </summary>
    public class HotReloadGlobalUsingBaseHost
    {
        public int BaseOffset => 10;
    }
}
