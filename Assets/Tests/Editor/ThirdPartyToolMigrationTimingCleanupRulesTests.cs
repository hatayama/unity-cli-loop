using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies how timing migration removes unused PlayerLoopTiming declarations and reads parameter declarations.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTimingCleanupRulesTests
    {
        /// <summary>
        /// Verifies a PlayerLoopTiming declaration inside a block comment is left untouched.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenDeclarationIsInsideComment_KeepsSource()
        {
            string source = "/*\nPlayerLoopTiming timing;\n*/\nclass Runner { }\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo(source));
            Assert.That(replacementCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a PlayerLoopTiming field that is still referenced is not removed.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenDeclarationIsStillUsed_KeepsSource()
        {
            string source =
                "class Runner\n{\n    PlayerLoopTiming timing;\n    void Run() { Use(timing); }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo(source));
            Assert.That(replacementCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a closing bracket without a matching opening bracket yields minus one.
        /// </summary>
        [Test]
        public void FindOpeningAttributeBracket_WhenNoOpeningBracket_ReturnsMinusOne()
        {
            string source = "values]";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            int result = ThirdPartyToolMigrationTimingCleanupRules.FindOpeningAttributeBracket(
                source,
                source.IndexOf(']'),
                codeTextMask);

            Assert.That(result, Is.EqualTo(-1));
        }

        /// <summary>
        /// Verifies code before an index on the same line is detected.
        /// </summary>
        [Test]
        public void HasOnlyWhitespaceBeforeIndexOnLine_WhenCodePrecedesIndex_ReturnsFalse()
        {
            string source = "int first;\n    int count; [NonSerialized]";

            bool result = ThirdPartyToolMigrationTimingCleanupRules.HasOnlyWhitespaceBeforeIndexOnLine(
                source,
                source.IndexOf('['));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies blank parameter entries are skipped without consuming a parameter index.
        /// </summary>
        [Test]
        public void ReadLegacyPlayerLoopTimingParameterDeclarations_WhenParameterEntryIsBlank_SkipsItWithoutIndex()
        {
            LegacyPlayerLoopTimingParameterDeclaration[] result =
                ThirdPartyToolMigrationTimingCleanupRules.ReadLegacyPlayerLoopTimingParameterDeclarations(
                    new[] { "int value", "  ", "PlayerLoopTiming timing = PlayerLoopTiming.Update" });

            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[1].Index, Is.EqualTo(1));
            Assert.That(result[1].TypeName, Is.EqualTo("PlayerLoopTiming"));
            Assert.That(result[1].Name, Is.EqualTo("timing"));
            Assert.That(result[1].HasDefaultValue, Is.True);
        }
    }
}
