using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies scalar normalization for SKILL.md frontmatter fields.
    /// </summary>
    [TestFixture]
    public class SkillSourceFrontmatterReaderTests
    {
        // Tests that string scalar fields strip surrounding whitespace and single or double quotes.
        [Test]
        public void ParseScalarFields_WhenQuoted_StripsWhitespaceAndQuotes()
        {
            string content =
                "---\n" +
                "name:   'uloop-compile'   \n" +
                "toolName:   \"compile\"   \n" +
                "description:   'Compile the project.'   \n" +
                "---\n";

            Assert.That(
                SkillSourceFrontmatterReader.ParseNameFromFrontmatter(content),
                Is.EqualTo("uloop-compile"));
            Assert.That(
                SkillSourceFrontmatterReader.ParseToolNameFromFrontmatter(content),
                Is.EqualTo("compile"));
            Assert.That(
                SkillSourceFrontmatterReader.ParseDescriptionFromFrontmatter(content),
                Is.EqualTo("Compile the project."));
        }

        // Tests that tool names preserve their original casing after scalar normalization.
        [Test]
        public void ParseToolNameFromFrontmatter_WhenQuoted_PreservesCasing()
        {
            string content = "---\ntoolName: 'Compile'\n---\n";

            string toolName = SkillSourceFrontmatterReader.ParseToolNameFromFrontmatter(content);

            Assert.That(toolName, Is.EqualTo("Compile"));
        }

        // Tests that internal flags accept quoted true values without changing case-insensitive semantics.
        [TestCase("\"TrUe\"")]
        [TestCase("'TrUe'")]
        [TestCase("   \"TrUe\"   ")]
        public void IsInternalSkill_WhenTrueIsQuoted_ReturnsTrue(string scalar)
        {
            string content = $"---\ninternal: {scalar}\n---\n";

            bool isInternal = SkillSourceFrontmatterReader.IsInternalSkill(content);

            Assert.That(isInternal, Is.True);
        }

        // Tests that quoted false values remain non-internal after scalar normalization.
        [TestCase("\"FALSE\"")]
        [TestCase("'false'")]
        public void IsInternalSkill_WhenFalseIsQuoted_ReturnsFalse(string scalar)
        {
            string content = $"---\ninternal: {scalar}\n---\n";

            bool isInternal = SkillSourceFrontmatterReader.IsInternalSkill(content);

            Assert.That(isInternal, Is.False);
        }
    }
}
