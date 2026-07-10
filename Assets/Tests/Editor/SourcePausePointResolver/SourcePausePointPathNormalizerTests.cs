using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies PDB document URL vs. project-relative path comparison across separator styles.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointPathNormalizerTests
    {
        [Test]
        public void ToForwardSlashes_WhenPathContainsBackslashes_ReplacesThemWithForwardSlashes()
        {
            // Verifies backslash-separated Windows paths are normalized to forward slashes.
            string result = SourcePausePointPathNormalizer.ToForwardSlashes(@"Assets\Scripts\Foo.cs");

            Assert.That(result, Is.EqualTo("Assets/Scripts/Foo.cs"));
        }

        [Test]
        public void PathsReferToSameFile_WhenPathsAreIdentical_ReturnsTrue()
        {
            // Verifies an exact project-relative match is recognized.
            bool result = SourcePausePointPathNormalizer.PathsReferToSameFile(
                "Assets/Scripts/Foo.cs", "Assets/Scripts/Foo.cs");

            Assert.That(result, Is.True);
        }

        [Test]
        public void PathsReferToSameFile_WhenDocumentUrlIsAbsoluteWithBackslashes_ReturnsTrue()
        {
            // Verifies a Windows-style absolute PDB document URL still matches a relative input path.
            bool result = SourcePausePointPathNormalizer.PathsReferToSameFile(
                @"C:\Project\Assets\Scripts\Foo.cs", "Assets/Scripts/Foo.cs");

            Assert.That(result, Is.True);
        }

        [Test]
        public void PathsReferToSameFile_WhenDocumentUrlIsAbsoluteWithForwardSlashes_ReturnsTrue()
        {
            // Verifies a Unix-style absolute PDB document URL still matches a relative input path.
            bool result = SourcePausePointPathNormalizer.PathsReferToSameFile(
                "/Users/dev/Project/Assets/Scripts/Foo.cs", "Assets/Scripts/Foo.cs");

            Assert.That(result, Is.True);
        }

        [Test]
        public void PathsReferToSameFile_WhenDocumentUrlReferencesADifferentFile_ReturnsFalse()
        {
            // Verifies a document for an unrelated file is not treated as a match.
            bool result = SourcePausePointPathNormalizer.PathsReferToSameFile(
                "Assets/Scripts/Bar.cs", "Assets/Scripts/Foo.cs");

            Assert.That(result, Is.False);
        }
    }
}
