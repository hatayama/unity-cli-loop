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
        /// Verifies two unused declarations in a row are both removed, including the attribute line of the second one.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenSecondDeclarationHasAttribute_RemovesBothDeclarations()
        {
            string source =
                "class Runner\n{\n    PlayerLoopTiming first;\n    [NonSerialized]\n    PlayerLoopTiming second;\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo("class Runner\n{\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies removing an unused declaration keeps the blank line and indentation of the member that follows it.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenMemberFollows_KeepsItsIndentation()
        {
            string source =
                "class Runner\n{\n    PlayerLoopTiming timing;\n\n    void Run() { }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo("class Runner\n{\n\n    void Run() { }\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an attribute that shares its line with other code is removed together with the unused declaration it annotates.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenAttributeSharesLineWithCode_RemovesAttribute()
        {
            string source =
                "class Runner\n{\n    int count; [NonSerialized]\n    PlayerLoopTiming timing;\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo("class Runner\n{\n    int count;\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an unused declaration whose attribute follows another removed declaration on the same line is removed too.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenAttributeFollowsRemovedDeclaration_RemovesBothDeclarations()
        {
            string source =
                "class Runner\n{\n    PlayerLoopTiming first; [NonSerialized]\n    PlayerLoopTiming second;\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo("class Runner\n{\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies code kept before a removed declaration's inline attribute keeps its line when the next declaration is also removed.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenKeptCodePrecedesRemovedDeclarations_KeepsItsLine()
        {
            string source =
                "class Runner\n{\n    int count; [NonSerialized]\n    PlayerLoopTiming first; [NonSerialized]\n" +
                "    PlayerLoopTiming second;\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo("class Runner\n{\n    int count;\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(2));
        }

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
