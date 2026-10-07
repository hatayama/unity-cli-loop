namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.GlobalUsingBase
{
    /// <summary>
    /// Enum reachable only through the test assembly's global using, so a const of this type
    /// declared in another namespace has a value only in a compilation that carries that using.
    /// </summary>
    public enum HotReloadGlobalUsingMode
    {
        First = 1,
        Second = 2
    }
}
