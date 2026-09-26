namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled referencing type whose body is edited to name an enum member the same reload adds.
    /// </summary>
    public static class HotReloadSiblingEnumUser
    {
        public static int ReadSiblingEnum()
        {
            return (int)HotReloadSiblingEnum.Second;
        }
    }
}
