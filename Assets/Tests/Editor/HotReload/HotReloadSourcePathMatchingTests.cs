using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Coverage for matching an absolute source path against a project-relative one, which the
    /// worker uses to recognize the real file behind an edited copy and to name a file in a reason.
    /// </summary>
    public class HotReloadSourcePathMatchingTests
    {
        /// <summary>
        /// What: an absolute path written with backslashes matches its project-relative form written
        /// with slashes.
        /// </summary>
        [Test]
        public void EndsWithProjectRelativePath_BackslashSeparatedAbsolutePath_Matches()
        {
            Assert.That(
                HotReloadSourcePathMatching.EndsWithProjectRelativePath("C:\\proj\\Assets\\A\\Foo.cs", "Assets/A/Foo.cs"),
                Is.True);
        }

        /// <summary>
        /// What: a file name that only ends with the relative path's characters does not match,
        /// because the match must start at a directory separator.
        /// </summary>
        [Test]
        public void EndsWithProjectRelativePath_NameThatOnlySharesTheTail_DoesNotMatch()
        {
            Assert.That(
                HotReloadSourcePathMatching.EndsWithProjectRelativePath("/p/Assets/A/AnOther.cs", "Other.cs"),
                Is.False);
        }

        /// <summary>
        /// What: an absolute path matches the project-relative path it ends with.
        /// </summary>
        [Test]
        public void EndsWithProjectRelativePath_SlashSeparatedAbsolutePath_Matches()
        {
            Assert.That(
                HotReloadSourcePathMatching.EndsWithProjectRelativePath("/p/Assets/A/Foo.cs", "Assets/A/Foo.cs"),
                Is.True);
        }

        /// <summary>
        /// What: another file under the same root is given in project-relative form with slashes,
        /// using the root a known absolute and project-relative pair anchors.
        /// </summary>
        [Test]
        public void ToProjectRelativeOrNull_FileUnderTheAnchoredRoot_ReturnsTheRelativePath()
        {
            Assert.That(
                HotReloadSourcePathMatching.ToProjectRelativeOrNull(
                    "C:\\proj\\Assets\\B\\Bar.cs",
                    "C:\\proj\\Assets\\A\\Foo.cs",
                    "Assets/A/Foo.cs"),
                Is.EqualTo("Assets/B/Bar.cs"));
        }

        /// <summary>
        /// What: a file outside the anchored root has no project-relative form.
        /// </summary>
        [Test]
        public void ToProjectRelativeOrNull_FileOutsideTheAnchoredRoot_ReturnsNull()
        {
            Assert.That(
                HotReloadSourcePathMatching.ToProjectRelativeOrNull(
                    "/elsewhere/Assets/B/Bar.cs",
                    "/proj/Assets/A/Foo.cs",
                    "Assets/A/Foo.cs"),
                Is.Null);
        }
    }
}
