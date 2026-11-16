namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A second static class with an extension method of the same name as one in the
    /// extension-call fixture file. Extension lookup puts both classes' methods into one method
    /// group, so a call that binds ambiguously to the fixture's twins also lists this method.
    /// It lives in its own file so the worker sees it only once, from the compiled assembly.
    /// </summary>
    public static class HotReloadSecondExtensionCallMath
    {
        public static int Thrice(this int value, string add)
        {
            return value + add.Length;
        }
    }
}
