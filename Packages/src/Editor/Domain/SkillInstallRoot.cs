namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Selects which directory receives project-local agent skill folders (.claude/skills, ...).
    /// </summary>
    public enum SkillInstallRoot
    {
        UnityProjectRoot,
        GitRepositoryRoot
    }
}
