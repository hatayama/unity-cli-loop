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
        /// Verifies a line holding several adjacent attributes above an unused declaration is removed with it.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenAttributeLineHasAdjacentAttributes_RemovesWholeLine()
        {
            string source =
                "class Runner\n{\n    [A][B]\n    PlayerLoopTiming timing;\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo("class Runner\n{\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a line holding several space-separated attributes above an unused declaration is removed with it.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenAttributeLineHasSpacedAttributes_RemovesWholeLine()
        {
            string source =
                "class Runner\n{\n    [A] [B]\n    PlayerLoopTiming timing;\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo("class Runner\n{\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a single-attribute line above a line of several attributes is removed together with the unused declaration.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenSeveralAttributeLinesPrecedeDeclaration_RemovesAllOfThem()
        {
            string source =
                "class Runner\n{\n    [A]\n    [B] [C]\n    PlayerLoopTiming timing;\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo("class Runner\n{\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies removing a declaration with an inline attribute keeps CRLF line breaks intact and leaves no lone carriage return.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenAttributeSharesLineWithCodeInCrLfSource_KeepsCrLf()
        {
            string source =
                "class Runner\r\n{\r\n    int count; [NonSerialized]\r\n    PlayerLoopTiming timing;\r\n}\r\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo("class Runner\r\n{\r\n    int count;\r\n}\r\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
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

        /// <summary>
        /// Verifies a statement that declares two timings is kept when only the first one is unused, so a declaration
        /// that is still used is not removed.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenSecondDeclaratorIsUsed_KeepsTheStatement()
        {
            string source =
                "class Runner\n{\n    PlayerLoopTiming first = PlayerLoopTiming.Update, second = PlayerLoopTiming.FixedUpdate;\n    void Run() { Use(second); }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo(source));
            Assert.That(replacementCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a statement whose second declarator has no initializer is kept when that declarator is used.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenUsedSecondDeclaratorHasNoInitializer_KeepsTheStatement()
        {
            string source =
                "class Runner\n{\n    PlayerLoopTiming first = PlayerLoopTiming.Update, second;\n    void Run() { Use(second); }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo(source));
            Assert.That(replacementCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a statement that declares two timings is removed whole when neither of them is used.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenNoDeclaratorIsUsed_RemovesTheStatement()
        {
            string source =
                "class Runner\n{\n    private PlayerLoopTiming early = PlayerLoopTiming.EarlyUpdate, late = PlayerLoopTiming.PostLateUpdate;\n    void Run() { }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo("class Runner\n{\n    void Run() { }\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an unused declaration whose initializer contains commas inside parentheses is removed.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenAnUnusedInitializerHasCommas_RemovesTheStatement()
        {
            string source =
                "class Runner\n{\n    PlayerLoopTiming timing = (PlayerLoopTiming)Mathf.Clamp(value, 0, 6);\n    void Run() { }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo("class Runner\n{\n    void Run() { }\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a statement whose declarators cannot be read apart (a comma inside generic arguments) is kept.
        /// </summary>
        [Test]
        public void RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode_WhenTheDeclaratorsCannotBeRead_KeepsTheStatement()
        {
            string source =
                "class Runner\n{\n    PlayerLoopTiming timing = Pick<Alpha, Beta>();\n    void Run() { }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCleanupRules.RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo(source));
            Assert.That(replacementCount, Is.EqualTo(0));
        }
    }
}
