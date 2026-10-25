using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies when the skill install location picker can be used and what it says about the location.
    /// </summary>
    [TestFixture]
    public sealed class SkillsInstallRootTextTests
    {
        /// <summary>
        /// Verifies the picker is disabled when no Git repository exists and the Unity project root is chosen, since there is nothing else to pick.
        /// </summary>
        [Test]
        public void IsPickerEnabled_WithoutGitRepository_WhenUnityRootIsChosen_IsDisabled()
        {
            SkillInstallRootSelection selection = new(SkillInstallRoot.UnityProjectRoot, "<PROJECT_ROOT>", null);

            Assert.That(SkillsInstallRootText.IsPickerEnabled(selection, canEdit: true), Is.False);
        }

        /// <summary>
        /// Verifies the picker stays usable when the chosen Git repository root is unavailable, so the user can switch back.
        /// </summary>
        [Test]
        public void IsPickerEnabled_WithoutGitRepository_WhenGitRootIsChosen_IsEnabled()
        {
            SkillInstallRootSelection selection = new(SkillInstallRoot.GitRepositoryRoot, "<PROJECT_ROOT>", null);

            Assert.That(SkillsInstallRootText.IsPickerEnabled(selection, canEdit: true), Is.True);
            Assert.That(
                SkillsInstallRootText.BuildLocationText(selection),
                Is.EqualTo(SkillsInstallRootText.UnavailableLocationText));
        }

        /// <summary>
        /// Verifies the picker follows the caller's edit permission when both roots are available.
        /// </summary>
        [Test]
        public void IsPickerEnabled_WithGitRepository_FollowsCanEdit()
        {
            SkillInstallRootSelection selection = new(
                SkillInstallRoot.UnityProjectRoot,
                "<PROJECT_ROOT>/unity",
                "<PROJECT_ROOT>");

            Assert.That(SkillsInstallRootText.IsPickerEnabled(selection, canEdit: true), Is.True);
            Assert.That(SkillsInstallRootText.IsPickerEnabled(selection, canEdit: false), Is.False);
        }
    }
}
