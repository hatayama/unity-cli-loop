using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the line map between the last compiled source snapshot and the edited file on disk.
    /// </summary>
    [TestFixture]
    public sealed class PausePointEditedLineMapTests
    {
        private static readonly string[] MethodSourceLines =
        {
            "class A",
            "{",
            "    void M()",
            "    {",
            "        int x = 1;",
            "        int y = 2;",
            "    }",
            "}",
        };

        /// <summary>
        /// What: identical sources form an identity map where every line maps to itself on both sides.
        /// </summary>
        [Test]
        public void BuildOrNull_IdenticalSources_IsIdentityAndMapsEveryLineToItself()
        {
            string source = Join(MethodSourceLines);

            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(source, source);

            Assert.That(map, Is.Not.Null);
            Assert.That(map.IsIdentity, Is.True);
            Assert.That(map.CompiledLineCount, Is.EqualTo(MethodSourceLines.Length));
            Assert.That(map.EditedLineCount, Is.EqualTo(MethodSourceLines.Length));
            for (int line = 1; line <= MethodSourceLines.Length; line++)
            {
                Assert.That(map.ToCompiledLineOrZero(line), Is.EqualTo(line));
                Assert.That(map.ToEditedLineOrZero(line), Is.EqualTo(line));
            }
        }

        /// <summary>
        /// What: three lines inserted at the top shift every later line by three, and the inserted
        /// lines have no compiled line.
        /// </summary>
        [Test]
        public void BuildOrNull_LinesInsertedAbove_ShiftsEveryLaterLineByTheInsertCount()
        {
            string compiled = Join(MethodSourceLines);
            string edited = Join(new[] { "// a", "// b", "// c" }.Concat(MethodSourceLines));

            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(compiled, edited);

            Assert.That(map.IsIdentity, Is.False);
            Assert.That(map.CompiledLineCount, Is.EqualTo(MethodSourceLines.Length));
            Assert.That(map.EditedLineCount, Is.EqualTo(MethodSourceLines.Length + 3));
            for (int editedLine = 1; editedLine <= 3; editedLine++)
            {
                Assert.That(map.ToCompiledLineOrZero(editedLine), Is.EqualTo(0));
            }

            for (int editedLine = 4; editedLine <= map.EditedLineCount; editedLine++)
            {
                Assert.That(map.ToCompiledLineOrZero(editedLine), Is.EqualTo(editedLine - 3));
            }

            for (int compiledLine = 1; compiledLine <= map.CompiledLineCount; compiledLine++)
            {
                Assert.That(map.ToEditedLineOrZero(compiledLine), Is.EqualTo(compiledLine + 3));
            }
        }

        /// <summary>
        /// What: two lines deleted at the top shift every later line backward by two, and the
        /// deleted compiled lines have no edited line.
        /// </summary>
        [Test]
        public void BuildOrNull_LinesDeletedAbove_ShiftsEveryLaterLineBackward()
        {
            string compiled = Join(new[] { "using System;", "using System.Linq;" }.Concat(MethodSourceLines));
            string edited = Join(MethodSourceLines);

            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(compiled, edited);

            Assert.That(map.CompiledLineCount, Is.EqualTo(MethodSourceLines.Length + 2));
            Assert.That(map.EditedLineCount, Is.EqualTo(MethodSourceLines.Length));
            Assert.That(map.ToEditedLineOrZero(1), Is.EqualTo(0));
            Assert.That(map.ToEditedLineOrZero(2), Is.EqualTo(0));
            for (int compiledLine = 3; compiledLine <= map.CompiledLineCount; compiledLine++)
            {
                Assert.That(map.ToEditedLineOrZero(compiledLine), Is.EqualTo(compiledLine - 2));
            }

            for (int editedLine = 1; editedLine <= map.EditedLineCount; editedLine++)
            {
                Assert.That(map.ToCompiledLineOrZero(editedLine), Is.EqualTo(editedLine + 2));
            }
        }

        /// <summary>
        /// What: a compiled source that is only the leading part of the edited file is not an
        /// identity map, even though every compiled line is in the common prefix.
        /// </summary>
        [Test]
        public void BuildOrNull_CompiledIsALeadingPartOfEdited_IsNotIdentity()
        {
            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(
                Join(new[] { "a();", "b();" }),
                Join(new[] { "a();", "b();", "c();" }));

            Assert.That(map.IsIdentity, Is.False);
            Assert.That(map.ToCompiledLineOrZero(1), Is.EqualTo(1));
            Assert.That(map.ToCompiledLineOrZero(2), Is.EqualTo(2));
            Assert.That(map.ToCompiledLineOrZero(3), Is.EqualTo(0));
        }

        /// <summary>
        /// What: a changed statement is unmapped on both sides while every other line keeps its line.
        /// </summary>
        [Test]
        public void BuildOrNull_ChangedStatement_LeavesOnlyThatLineUnmappedOnBothSides()
        {
            string[] editedLines = (string[])MethodSourceLines.Clone();
            editedLines[4] = "        int x = 10;";

            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(Join(MethodSourceLines), Join(editedLines));

            Assert.That(map.IsIdentity, Is.False);
            Assert.That(map.ToCompiledLineOrZero(5), Is.EqualTo(0));
            Assert.That(map.ToEditedLineOrZero(5), Is.EqualTo(0));
            foreach (int line in new[] { 1, 2, 3, 4, 6, 7, 8 })
            {
                Assert.That(map.ToCompiledLineOrZero(line), Is.EqualTo(line));
                Assert.That(map.ToEditedLineOrZero(line), Is.EqualTo(line));
            }
        }

        /// <summary>
        /// What: a statement inserted inside a method is unmapped, and the lines around it keep
        /// their compiled lines.
        /// </summary>
        [Test]
        public void BuildOrNull_InsertedStatementInsideMethod_LeavesInsertedLineUnmappedAndKeepsNeighbors()
        {
            List<string> editedLines = MethodSourceLines.ToList();
            editedLines.Insert(5, "        x += 1;");

            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(Join(MethodSourceLines), Join(editedLines));

            Assert.That(map.ToCompiledLineOrZero(6), Is.EqualTo(0));
            Assert.That(map.ToCompiledLineOrZero(5), Is.EqualTo(5));
            Assert.That(map.ToCompiledLineOrZero(7), Is.EqualTo(6));
            Assert.That(map.ToEditedLineOrZero(6), Is.EqualTo(7));
            Assert.That(map.ToEditedLineOrZero(8), Is.EqualTo(9));
        }

        /// <summary>
        /// What: a removed statement leaves its compiled line unmapped, and the lines around it keep
        /// their edited lines.
        /// </summary>
        [Test]
        public void BuildOrNull_RemovedStatement_LeavesCompiledLineUnmappedAndKeepsNeighbors()
        {
            List<string> editedLines = MethodSourceLines.ToList();
            editedLines.RemoveAt(4);

            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(Join(MethodSourceLines), Join(editedLines));

            Assert.That(map.ToEditedLineOrZero(5), Is.EqualTo(0));
            Assert.That(map.ToEditedLineOrZero(4), Is.EqualTo(4));
            Assert.That(map.ToEditedLineOrZero(6), Is.EqualTo(5));
            Assert.That(map.ToCompiledLineOrZero(5), Is.EqualTo(6));
        }

        /// <summary>
        /// What: indentation changes and mixed CRLF / LF line endings do not unmap any line.
        /// </summary>
        [Test]
        public void BuildOrNull_WhitespaceOnlyDifference_MapsAsEqual()
        {
            string compiled = string.Join("\r\n", MethodSourceLines);
            string[] reindented = MethodSourceLines.Select(line => "\t" + line.Trim() + "  ").ToArray();
            string edited = string.Join("\r\n", reindented.Take(4)) + "\n" + string.Join("\n", reindented.Skip(4));

            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(compiled, edited);

            Assert.That(map.IsIdentity, Is.True);
            for (int line = 1; line <= MethodSourceLines.Length; line++)
            {
                Assert.That(map.ToCompiledLineOrZero(line), Is.EqualTo(line));
            }
        }

        /// <summary>
        /// What: a line whose only change is a trailing comment is unmapped. This pins the known
        /// limit of comparing trimmed text: the line is refused rather than guessed.
        /// </summary>
        [Test]
        public void BuildOrNull_TrailingCommentOnlyChange_LeavesLineUnmapped()
        {
            string[] editedLines = (string[])MethodSourceLines.Clone();
            editedLines[4] = "        int x = 1; // note";

            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(Join(MethodSourceLines), Join(editedLines));

            Assert.That(map.ToCompiledLineOrZero(5), Is.EqualTo(0));
            Assert.That(map.ToCompiledLineOrZero(4), Is.EqualTo(4));
            Assert.That(map.ToCompiledLineOrZero(6), Is.EqualTo(6));
        }

        /// <summary>
        /// What: the mapped pairs are monotonic, so a later edited line never maps to an earlier
        /// compiled line, for a shift, a brace-heavy change, and a swap of two statements.
        /// </summary>
        [Test]
        public void BuildOrNull_MappedPairsAreMonotonic()
        {
            string[][] editedCases =
            {
                new[] { "// pad" }.Concat(MethodSourceLines).ToArray(),
                new[] { "class A", "{", "    void M()", "    {", "        {", "        }", "        int y = 2;", "    }", "}", "}" },
                new[] { "class A", "{", "    void M()", "    {", "        int y = 2;", "        int x = 1;", "    }", "}" },
            };

            foreach (string[] editedLines in editedCases)
            {
                PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(Join(MethodSourceLines), Join(editedLines));

                Assert.That(map.EditedLineCount, Is.EqualTo(editedLines.Length));
                int previousCompiledLine = 0;
                int mappedPairCount = 0;
                for (int editedLine = 1; editedLine <= map.EditedLineCount; editedLine++)
                {
                    int compiledLine = map.ToCompiledLineOrZero(editedLine);
                    if (compiledLine == 0)
                    {
                        continue;
                    }

                    Assert.That(compiledLine, Is.GreaterThan(previousCompiledLine));
                    Assert.That(map.ToEditedLineOrZero(compiledLine), Is.EqualTo(editedLine));
                    previousCompiledLine = compiledLine;
                    mappedPairCount++;
                }

                Assert.That(mappedPairCount, Is.GreaterThanOrEqualTo(MethodSourceLines.Length - 1));
            }
        }

        /// <summary>
        /// What: a changed middle section larger than the diff cell cap returns null so the caller
        /// falls back instead of running an unbounded diff.
        /// </summary>
        [Test]
        public void BuildOrNull_MidSectionExceedsMaxDiffCells_ReturnsNull()
        {
            string compiled = Join(Enumerable.Range(0, 2001).Select(index => "compiled" + index));
            string edited = Join(Enumerable.Range(0, 2001).Select(index => "edited" + index));

            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(compiled, edited);

            Assert.That(map, Is.Null);
        }

        /// <summary>
        /// What: the diff cell cap counts only the changed middle section, so a large file with one
        /// changed line still gets a map where only that line is unmapped.
        /// </summary>
        [Test]
        public void BuildOrNull_LargeFileWithOneChangedLine_ReturnsAMap()
        {
            const int changedIndex = 1000;
            string[] compiledLines = Enumerable.Range(0, 2001).Select(index => "line" + index).ToArray();
            string[] editedLines = (string[])compiledLines.Clone();
            editedLines[changedIndex] = "changed";

            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(Join(compiledLines), Join(editedLines));

            Assert.That(map, Is.Not.Null);
            Assert.That(map.ToCompiledLineOrZero(changedIndex + 1), Is.EqualTo(0));
            Assert.That(map.ToCompiledLineOrZero(changedIndex), Is.EqualTo(changedIndex));
            Assert.That(map.ToCompiledLineOrZero(changedIndex + 2), Is.EqualTo(changedIndex + 2));
        }

        /// <summary>
        /// What: a source that ends with a newline counts only its physical lines, so the empty tail
        /// after the last newline is not a line.
        /// </summary>
        [Test]
        public void BuildOrNull_TrailingNewline_DoesNotCountTheEmptyTailAsALine()
        {
            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull("a\nb\n", "a\nb");

            Assert.That(map.CompiledLineCount, Is.EqualTo(2));
            Assert.That(map.EditedLineCount, Is.EqualTo(2));
            Assert.That(map.IsIdentity, Is.True);
        }

        /// <summary>
        /// What: the next mapped edited line is the line itself when it is mapped, the first later
        /// mapped line otherwise, and zero when no mapped line follows.
        /// </summary>
        [Test]
        public void NextMappedEditedLineOrZero_ReturnsTheLineItselfWhenMapped_AndTheNextMappedLineOtherwise()
        {
            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(
                Join(new[] { "a();", "b();" }),
                Join(new[] { "a();", "x();", "b();", "y();" }));

            Assert.That(map.NextMappedEditedLineOrZero(1), Is.EqualTo(1));
            Assert.That(map.NextMappedEditedLineOrZero(2), Is.EqualTo(3));
            Assert.That(map.NextMappedEditedLineOrZero(3), Is.EqualTo(3));
            Assert.That(map.NextMappedEditedLineOrZero(4), Is.EqualTo(0));
            Assert.That(map.NextMappedEditedLineOrZero(5), Is.EqualTo(0));
        }

        /// <summary>
        /// What: the first unmapped statement in an edited range skips unmapped blank, brace, and
        /// comment lines, and returns zero when the range holds no unmapped statement.
        /// </summary>
        [Test]
        public void FirstUnmappedStatementLineOrZero_SkipsTrivialLines_AndReturnsTheFirstUnmappedStatement()
        {
            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(
                Join(new[] { "a();", "b();" }),
                Join(new[] { "a();", "", "}", "// c", "x = 1;", "b();" }));

            Assert.That(map.FirstUnmappedStatementLineOrZero(1, 7), Is.EqualTo(5));
            Assert.That(map.FirstUnmappedStatementLineOrZero(2, 5), Is.EqualTo(0));
            Assert.That(map.FirstUnmappedStatementLineOrZero(5, 5), Is.EqualTo(0));
            Assert.That(map.FirstUnmappedStatementLineOrZero(6, 7), Is.EqualTo(0));
            Assert.That(map.EditedLineTextOrEmpty(5), Is.EqualTo("x = 1;"));
            Assert.That(map.CompiledLineTextOrEmpty(2), Is.EqualTo("b();"));
            Assert.That(map.EditedLineTextOrEmpty(7), Is.Empty);
        }

        /// <summary>
        /// What: blank lines, brace / parenthesis / semicolon-only lines, and line comments are
        /// trivial; lines holding code are not.
        /// </summary>
        [Test]
        public void IsTrivialLine_TreatsBlankBracesSemicolonsAndLineComments_AsTrivial()
        {
            foreach (string trivial in new[] { "", "   ", "{", "  }", "});", ";", "()", "// note", "\t// x" })
            {
                Assert.That(PausePointEditedLineMap.IsTrivialLine(trivial), Is.True, trivial);
            }

            foreach (string statement in new[] { "return;", "int a;", "} else {", "Foo();" })
            {
                Assert.That(PausePointEditedLineMap.IsTrivialLine(statement), Is.False, statement);
            }
        }

        private static string Join(IEnumerable<string> lines)
        {
            return string.Join("\n", lines);
        }
    }
}
