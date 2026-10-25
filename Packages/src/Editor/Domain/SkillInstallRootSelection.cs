namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Describes the saved skill install root together with the directories each choice resolves to.
    /// </summary>
    public readonly struct SkillInstallRootSelection
    {
        public readonly SkillInstallRoot SelectedRoot;
        public readonly string UnityProjectRootPath;

        // Null when no Git repository contains the Unity project.
        public readonly string GitRepositoryRootPath;

        public SkillInstallRootSelection(
            SkillInstallRoot selectedRoot,
            string unityProjectRootPath,
            string gitRepositoryRootPath)
        {
            System.Diagnostics.Debug.Assert(
                !string.IsNullOrEmpty(unityProjectRootPath),
                "unityProjectRootPath must not be null or empty");

            SelectedRoot = selectedRoot;
            UnityProjectRootPath = unityProjectRootPath;
            GitRepositoryRootPath = gitRepositoryRootPath;
        }

        public bool IsGitRepositoryRootAvailable => !string.IsNullOrEmpty(GitRepositoryRootPath);

        public bool IsSelectedRootAvailable =>
            SelectedRoot == SkillInstallRoot.UnityProjectRoot || IsGitRepositoryRootAvailable;

        /// <summary>
        /// Directory that receives agent skill folders, or null when the Git repository root is
        /// selected but no Git repository contains the Unity project.
        /// </summary>
        public string SelectedRootPath => SelectedRoot == SkillInstallRoot.GitRepositoryRoot
            ? GitRepositoryRootPath
            : UnityProjectRootPath;
    }
}
