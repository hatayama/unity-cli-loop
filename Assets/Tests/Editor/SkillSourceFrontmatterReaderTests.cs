using System.IO;
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

        /// <summary>
        /// Verifies that content without a frontmatter block yields no tool name, skill name, or description.
        /// </summary>
        [Test]
        public void ParseFrontmatterFields_WhenFrontmatterIsMissing_ReturnNull()
        {
            string content = "name: uloop-compile\ntoolName: compile\ndescription: Compile.\n";

            Assert.That(SkillSourceFrontmatterReader.ParseToolNameFromFrontmatter(content), Is.Null);
            Assert.That(SkillSourceFrontmatterReader.ParseNameFromFrontmatter(content), Is.Null);
            Assert.That(SkillSourceFrontmatterReader.ParseDescriptionFromFrontmatter(content), Is.Null);
        }

        /// <summary>
        /// Verifies that an internal flag outside a frontmatter block does not mark the skill as internal.
        /// </summary>
        [Test]
        public void IsInternalSkill_WhenFrontmatterIsMissing_ReturnsFalse()
        {
            Assert.That(SkillSourceFrontmatterReader.IsInternalSkill("internal: true\n"), Is.False);
        }

        /// <summary>
        /// Verifies that frontmatter without a name field yields no skill name.
        /// </summary>
        [Test]
        public void ParseNameFromFrontmatter_WhenNameIsMissing_ReturnsNull()
        {
            string content = "---\ntoolName: compile\n---\n";

            Assert.That(SkillSourceFrontmatterReader.ParseNameFromFrontmatter(content), Is.Null);
        }

        /// <summary>
        /// Verifies that a skill name without the uloop- prefix and without a toolName cannot be resolved to a tool.
        /// </summary>
        [TestCase(null)]
        [TestCase("custom-skill")]
        public void ResolveToolNameForSkillSource_WhenSkillNameLacksPrefix_ReturnsNull(string skillName)
        {
            Assert.That(SkillSourceFrontmatterReader.ResolveToolNameForSkillSource(skillName, null), Is.Null);
        }

        /// <summary>
        /// Verifies that without a toolName field the skill name decides the match, even when the directory name would match.
        /// </summary>
        [TestCase("uloop-compile", "other-directory", true)]
        [TestCase("uloop-get-logs", "uloop-compile", false)]
        public void SkillContentMatchesTool_WhenOnlyNameIsDeclared_UsesSkillName(
            string skillName,
            string directoryName,
            bool expected)
        {
            string content = "---\nname: " + skillName + "\n---\n";
            string skillDirectory = Path.Combine("project-root", ".claude", "skills", directoryName);

            bool matches = SkillSourceFrontmatterReader.SkillContentMatchesTool(content, skillDirectory, "compile");

            Assert.That(matches, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies that content without frontmatter matches a tool only through the uloop- prefixed directory name.
        /// </summary>
        [TestCase("uloop-compile", true)]
        [TestCase("compile", false)]
        public void SkillContentMatchesTool_WhenFrontmatterIsMissing_UsesDirectoryName(string directoryName, bool expected)
        {
            string skillDirectory = Path.Combine("project-root", ".claude", "skills", directoryName);

            bool matches = SkillSourceFrontmatterReader.SkillContentMatchesTool("plain text", skillDirectory, "compile");

            Assert.That(matches, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies that an explicit toolName field wins over the uloop- prefixed skill name.
        /// </summary>
        [Test]
        public void GetToolNameFromSkillContent_WhenToolNameIsDeclared_ReturnsToolName()
        {
            string content = "---\nname: uloop-get-logs\ntoolName: compile\n---\n";

            Assert.That(SkillSourceFrontmatterReader.GetToolNameFromSkillContent(content), Is.EqualTo("compile"));
        }

        /// <summary>
        /// Verifies that a skill name without the uloop- prefix yields no tool name when toolName is absent.
        /// </summary>
        [Test]
        public void GetToolNameFromSkillContent_WhenNameLacksPrefix_ReturnsNull()
        {
            string content = "---\nname: custom-skill\n---\n";

            Assert.That(SkillSourceFrontmatterReader.GetToolNameFromSkillContent(content), Is.Null);
        }
    }
}
