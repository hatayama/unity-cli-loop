namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What the group's shim compile stage decided: nothing can be applied because the group
    /// failed, nothing has to be applied, or entries are ready to patch.
    /// </summary>
    internal enum HotReloadGroupCompileOutcome
    {
        Failed,
        ReadyWithoutMethods,
        ReadyWithMethods
    }
}
