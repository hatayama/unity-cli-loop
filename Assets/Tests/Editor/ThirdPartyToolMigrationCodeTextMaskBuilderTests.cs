using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies migration code masking ignores preprocessor text and bounds char literals.
    /// </summary>
    public sealed class ThirdPartyToolMigrationCodeTextMaskBuilderTests
    {
        [Test]
        public void CreateCodeCharacters_WhenRegionNameContainsApostrophe_KeepsFollowingCodeUnmasked()
        {
            // Verifies a #region title apostrophe does not mask the rest of the file as a char literal.
            string source = "#region Bob's helpers\npublic class Tool {}\n";

            bool[] codeCharacters = ThirdPartyToolMigrationCodeTextMaskBuilder.CreateCodeCharacters(source);

            int codeStartIndex = source.IndexOf("public class Tool");
            Assert.That(codeStartIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(AreAllCodeCharacters(codeCharacters, codeStartIndex, "public class Tool".Length), Is.True);
        }

        [Test]
        public void CreateCodeCharacters_WhenWarningContainsApostrophe_KeepsFollowingCodeUnmasked()
        {
            // Verifies a #warning apostrophe does not mask the following source as a char literal.
            string source = "#warning Don't call this API\npublic class Tool {}\n";

            bool[] codeCharacters = ThirdPartyToolMigrationCodeTextMaskBuilder.CreateCodeCharacters(source);

            int codeStartIndex = source.IndexOf("public class Tool");
            Assert.That(codeStartIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(AreAllCodeCharacters(codeCharacters, codeStartIndex, "public class Tool".Length), Is.True);
        }

        [Test]
        public void CreateCodeCharacters_WhenValidCharLiteral_MasksOnlyTheLiteral()
        {
            // Verifies a real char literal stays masked while the following statement remains code.
            string source = "char c = 'a';\nint x = 1;\n";

            bool[] codeCharacters = ThirdPartyToolMigrationCodeTextMaskBuilder.CreateCodeCharacters(source);

            int literalIndex = source.IndexOf("'a'");
            Assert.That(codeCharacters[literalIndex], Is.False);
            Assert.That(codeCharacters[literalIndex + 1], Is.False);
            Assert.That(codeCharacters[literalIndex + 2], Is.False);
            Assert.That(codeCharacters[source.IndexOf("int x")], Is.True);
        }

        [Test]
        public void CreateCodeCharacters_WhenApostropheDoesNotCloseSoon_TreatsItAsCode()
        {
            // Verifies a long unmatched apostrophe is not treated as an open-ended char literal.
            string source = "var name = Bob's helpers;\nint x = 1;\n";

            bool[] codeCharacters = ThirdPartyToolMigrationCodeTextMaskBuilder.CreateCodeCharacters(source);

            Assert.That(codeCharacters[source.IndexOf("int x")], Is.True);
            Assert.That(AreAllCodeCharacters(codeCharacters, source.IndexOf("helpers"), "helpers".Length), Is.True);
        }

        private static bool AreAllCodeCharacters(bool[] codeCharacters, int startIndex, int length)
        {
            for (int index = startIndex; index < startIndex + length; index++)
            {
                if (!codeCharacters[index])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
