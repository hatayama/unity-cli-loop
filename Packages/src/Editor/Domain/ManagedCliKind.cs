namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Identifies the package manager that owns a detected CLI executable.
    /// </summary>
    public enum ManagedCliKind
    {
        None,
        Homebrew,
        Winget
    }
}
