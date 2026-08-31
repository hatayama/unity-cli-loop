using System.Linq;
using System.Text;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies deterministic line-ending normalization for generated skill files.
    /// </summary>
    [TestFixture]
    public class SkillFileContentNormalizerTests
    {
        // Tests that CRLF and lone CR line endings become LF in single-byte text files.
        [Test]
        public void NormalizeSkillFileContent_WhenSingleByteTextUsesCarriageReturns_ReturnsLfContent()
        {
            byte[] sourceBytes = Encoding.UTF8.GetBytes("line1\r\nline2\rline3\n");
            byte[] expectedBytes = Encoding.UTF8.GetBytes("line1\nline2\nline3\n");

            byte[] actualBytes = SkillFileContentNormalizer.NormalizeSkillFileContent("reference.md", sourceBytes);

            Assert.That(actualBytes, Is.EqualTo(expectedBytes));
        }

        // Tests that UTF-16 little-endian text keeps its BOM and encoding while line endings become LF.
        [Test]
        public void NormalizeSkillFileContent_WhenUtf16LittleEndianTextUsesCrlf_PreservesEncoding()
        {
            byte[] sourceBytes = Encoding.Unicode.GetPreamble()
                .Concat(Encoding.Unicode.GetBytes("line1\r\nline2\r\n"))
                .ToArray();
            byte[] expectedBytes = Encoding.Unicode.GetPreamble()
                .Concat(Encoding.Unicode.GetBytes("line1\nline2\n"))
                .ToArray();

            byte[] actualBytes = SkillFileContentNormalizer.NormalizeSkillFileContent("install.ps1", sourceBytes);

            Assert.That(actualBytes, Is.EqualTo(expectedBytes));
        }

        // Tests that UTF-16 big-endian text keeps its BOM and encoding while line endings become LF.
        [Test]
        public void NormalizeSkillFileContent_WhenUtf16BigEndianTextUsesCrlf_PreservesEncoding()
        {
            byte[] sourceBytes = Encoding.BigEndianUnicode.GetPreamble()
                .Concat(Encoding.BigEndianUnicode.GetBytes("line1\r\nline2\r\n"))
                .ToArray();
            byte[] expectedBytes = Encoding.BigEndianUnicode.GetPreamble()
                .Concat(Encoding.BigEndianUnicode.GetBytes("line1\nline2\n"))
                .ToArray();

            byte[] actualBytes = SkillFileContentNormalizer.NormalizeSkillFileContent("install.ps1", sourceBytes);

            Assert.That(actualBytes, Is.EqualTo(expectedBytes));
        }

        // Tests that an explicit big-endian BOM wins over byte patterns that resemble little-endian CR.
        [Test]
        public void NormalizeSkillFileContent_WhenUtf16BigEndianBomContainsAmbiguousCodeUnit_PreservesEncoding()
        {
            byte[] sourceBytes = Encoding.BigEndianUnicode.GetPreamble()
                .Concat(Encoding.BigEndianUnicode.GetBytes("\u0D00\r\nline"))
                .ToArray();
            byte[] expectedBytes = Encoding.BigEndianUnicode.GetPreamble()
                .Concat(Encoding.BigEndianUnicode.GetBytes("\u0D00\nline"))
                .ToArray();

            byte[] actualBytes = SkillFileContentNormalizer.NormalizeSkillFileContent("reference.md", sourceBytes);

            Assert.That(actualBytes, Is.EqualTo(expectedBytes));
        }

        // Tests that BOM-less UTF-16 little-endian text still uses line-ending heuristics.
        [Test]
        public void NormalizeSkillFileContent_WhenUtf16LittleEndianHasNoBom_NormalizesCrlf()
        {
            byte[] sourceBytes = Encoding.Unicode.GetBytes("line1\r\nline2\r\n");
            byte[] expectedBytes = Encoding.Unicode.GetBytes("line1\nline2\n");

            byte[] actualBytes = SkillFileContentNormalizer.NormalizeSkillFileContent("reference.md", sourceBytes);

            Assert.That(actualBytes, Is.EqualTo(expectedBytes));
        }

        // Tests that BOM-less UTF-16 big-endian text still uses line-ending heuristics.
        [Test]
        public void NormalizeSkillFileContent_WhenUtf16BigEndianHasNoBom_NormalizesCrlf()
        {
            byte[] sourceBytes = Encoding.BigEndianUnicode.GetBytes("line1\r\nline2\r\n");
            byte[] expectedBytes = Encoding.BigEndianUnicode.GetBytes("line1\nline2\n");

            byte[] actualBytes = SkillFileContentNormalizer.NormalizeSkillFileContent("reference.md", sourceBytes);

            Assert.That(actualBytes, Is.EqualTo(expectedBytes));
        }

        // Tests that files outside the text extension allowlist keep their original bytes.
        [Test]
        public void NormalizeSkillFileContent_WhenExtensionIsNotText_ReturnsOriginalContent()
        {
            byte[] sourceBytes = Encoding.UTF8.GetBytes("line1\r\nline2\r\n");

            byte[] actualBytes = SkillFileContentNormalizer.NormalizeSkillFileContent("image.png", sourceBytes);

            Assert.That(actualBytes, Is.EqualTo(sourceBytes));
        }

        // Tests that NUL-containing binary data is not rewritten even when its extension is textual.
        [Test]
        public void NormalizeSkillFileContent_WhenTextExtensionContainsBinaryData_ReturnsOriginalContent()
        {
            byte[] sourceBytes = { 0x41, 0x42, 0x00, 0x43, 0x0D, 0x44 };

            byte[] actualBytes = SkillFileContentNormalizer.NormalizeSkillFileContent("reference.md", sourceBytes);

            Assert.That(actualBytes, Is.EqualTo(sourceBytes));
        }

        // Tests that LF-only text content remains byte-identical.
        [Test]
        public void NormalizeSkillFileContent_WhenTextAlreadyUsesLf_ReturnsOriginalContent()
        {
            byte[] sourceBytes = Encoding.UTF8.GetBytes("line1\nline2\n");

            byte[] actualBytes = SkillFileContentNormalizer.NormalizeSkillFileContent("reference.md", sourceBytes);

            Assert.That(actualBytes, Is.EqualTo(sourceBytes));
        }
    }
}
