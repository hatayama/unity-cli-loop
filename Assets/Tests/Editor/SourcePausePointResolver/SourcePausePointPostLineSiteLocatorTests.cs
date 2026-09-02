using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Pure-logic coverage for post-line capture site selection: where "after line N" lands
    /// for assignments, branches, returns, loop headers, and throw statements.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointPostLineSiteLocatorTests
    {
        private static SourcePausePointInstructionCandidate Instruction(
            int offset,
            SourcePausePointInstructionFlow flow = SourcePausePointInstructionFlow.Next)
        {
            return new SourcePausePointInstructionCandidate(offset, flow);
        }

        private static SourcePausePointSequencePointCandidate Point(int line, int offset, bool hidden = false)
        {
            return new SourcePausePointSequencePointCandidate(line, offset, hidden);
        }

        /// <summary>
        /// What: an assignment statement that falls through into the next line's sequence
        /// point lands on that next instruction, keeping the assignment's own last
        /// instruction as the local-scope probe offset.
        /// </summary>
        [Test]
        public void Locate_AssignmentFallsThrough_LandsOnNextSequencePointStart()
        {
            List<SourcePausePointInstructionCandidate> instructions = new List<SourcePausePointInstructionCandidate>
            {
                Instruction(0), Instruction(1), Instruction(2), // line 10: ldarg, ldarg, add
                Instruction(3),                                 // line 10: stloc
                Instruction(4), Instruction(5),                 // line 11: ldloc, stloc tmp
                Instruction(6, SourcePausePointInstructionFlow.Return),
            };
            List<SourcePausePointSequencePointCandidate> points = new List<SourcePausePointSequencePointCandidate>
            {
                Point(10, 0),
                Point(11, 4),
            };

            SourcePausePointPostLineSite site = SourcePausePointPostLineSiteLocator.Locate(instructions, points, 0);

            Assert.That(site.Kind, Is.EqualTo(SourcePausePointPostLineSiteKind.Fallthrough));
            Assert.That(site.InstructionIndex, Is.EqualTo(4));
            Assert.That(site.ScopeOffset, Is.EqualTo(3));
        }

        /// <summary>
        /// What: an if-condition line ends in a conditional branch, so the capture lands
        /// before that branch (after the condition evaluated, on both outcomes).
        /// </summary>
        [Test]
        public void Locate_ConditionalBranchLine_LandsBeforeTheBranchInstruction()
        {
            List<SourcePausePointInstructionCandidate> instructions = new List<SourcePausePointInstructionCandidate>
            {
                Instruction(0), Instruction(1),                                   // line 10: ldarg, ldc
                Instruction(2, SourcePausePointInstructionFlow.ConditionalBranch), // line 10: brfalse
                Instruction(3),                                                   // line 12: then-body
                Instruction(4, SourcePausePointInstructionFlow.Return),           // line 14
            };
            List<SourcePausePointSequencePointCandidate> points = new List<SourcePausePointSequencePointCandidate>
            {
                Point(10, 0),
                Point(12, 3),
                Point(14, 4),
            };

            SourcePausePointPostLineSite site = SourcePausePointPostLineSiteLocator.Locate(instructions, points, 0);

            Assert.That(site.Kind, Is.EqualTo(SourcePausePointPostLineSiteKind.BeforeControlTransfer));
            Assert.That(site.InstructionIndex, Is.EqualTo(2));
            Assert.That(site.ScopeOffset, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a return statement ends in an unconditional branch to the method epilogue,
        /// so the capture lands before that branch instead of on the shared closing brace.
        /// </summary>
        [Test]
        public void Locate_ReturnStatement_LandsBeforeTheEpilogueBranch()
        {
            List<SourcePausePointInstructionCandidate> instructions = new List<SourcePausePointInstructionCandidate>
            {
                Instruction(0),                                            // line 10: ldloc
                Instruction(1),                                            // line 10: stloc ret
                Instruction(2, SourcePausePointInstructionFlow.Branch),    // line 10: br epilogue
                Instruction(3),                                            // line 11: ldloc ret
                Instruction(4, SourcePausePointInstructionFlow.Return),    // line 11: ret
            };
            List<SourcePausePointSequencePointCandidate> points = new List<SourcePausePointSequencePointCandidate>
            {
                Point(10, 0),
                Point(11, 3),
            };

            SourcePausePointPostLineSite site = SourcePausePointPostLineSiteLocator.Locate(instructions, points, 0);

            Assert.That(site.Kind, Is.EqualTo(SourcePausePointPostLineSiteKind.BeforeControlTransfer));
            Assert.That(site.InstructionIndex, Is.EqualTo(2));
        }

        /// <summary>
        /// What: the closing-brace sequence point ends in ret, so post-line on it lands
        /// before the ret (the same instruction pre-line would use).
        /// </summary>
        [Test]
        public void Locate_LastSequencePointEndsInRet_LandsBeforeRet()
        {
            List<SourcePausePointInstructionCandidate> instructions = new List<SourcePausePointInstructionCandidate>
            {
                Instruction(0),
                Instruction(1),
                Instruction(2, SourcePausePointInstructionFlow.Return),
            };
            List<SourcePausePointSequencePointCandidate> points = new List<SourcePausePointSequencePointCandidate>
            {
                Point(10, 0),
                Point(11, 1),
            };

            SourcePausePointPostLineSite site = SourcePausePointPostLineSiteLocator.Locate(instructions, points, 1);

            Assert.That(site.Kind, Is.EqualTo(SourcePausePointPostLineSiteKind.BeforeControlTransfer));
            Assert.That(site.InstructionIndex, Is.EqualTo(2));
        }

        /// <summary>
        /// What: two statements on one line that fall through into each other are treated as
        /// one range, so the capture lands after the second statement.
        /// </summary>
        [Test]
        public void Locate_TwoStatementsOnOneLine_ExtendsThroughSameLineFallthrough()
        {
            List<SourcePausePointInstructionCandidate> instructions = new List<SourcePausePointInstructionCandidate>
            {
                Instruction(0), Instruction(1),   // line 10: a = 1
                Instruction(2), Instruction(3),   // line 10: b = 2
                Instruction(4),                   // line 11
                Instruction(5, SourcePausePointInstructionFlow.Return),
            };
            List<SourcePausePointSequencePointCandidate> points = new List<SourcePausePointSequencePointCandidate>
            {
                Point(10, 0),
                Point(10, 2),
                Point(11, 4),
            };

            SourcePausePointPostLineSite site = SourcePausePointPostLineSiteLocator.Locate(instructions, points, 0);

            Assert.That(site.Kind, Is.EqualTo(SourcePausePointPostLineSiteKind.Fallthrough));
            Assert.That(site.InstructionIndex, Is.EqualTo(4));
            Assert.That(site.ScopeOffset, Is.EqualTo(3));
        }

        /// <summary>
        /// What: a for-loop initializer on the header line ends in a branch to the condition,
        /// so the range is not extended into the same-line condition sequence point.
        /// </summary>
        [Test]
        public void Locate_ForLoopHeader_StopsAtInitializerBranch()
        {
            List<SourcePausePointInstructionCandidate> instructions = new List<SourcePausePointInstructionCandidate>
            {
                Instruction(0), Instruction(1),                                   // line 10: i = 0
                Instruction(2, SourcePausePointInstructionFlow.Branch),           // line 10: br cond
                Instruction(3),                                                   // line 12: body
                Instruction(4), Instruction(5),                                   // line 10: i++
                Instruction(6), Instruction(7),                                   // line 10: cond
                Instruction(8, SourcePausePointInstructionFlow.ConditionalBranch), // line 10: brtrue body
                Instruction(9, SourcePausePointInstructionFlow.Return),           // line 14
            };
            List<SourcePausePointSequencePointCandidate> points = new List<SourcePausePointSequencePointCandidate>
            {
                Point(10, 0),
                Point(12, 3),
                Point(10, 4),
                Point(10, 6),
                Point(14, 9),
            };

            SourcePausePointPostLineSite site = SourcePausePointPostLineSiteLocator.Locate(instructions, points, 0);

            Assert.That(site.Kind, Is.EqualTo(SourcePausePointPostLineSiteKind.BeforeControlTransfer));
            Assert.That(site.InstructionIndex, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a hidden sequence point is still a range boundary, so an await line ends
        /// at the compiler-generated continuation instead of running into it.
        /// </summary>
        [Test]
        public void Locate_HiddenSequencePointBoundary_EndsRangeAtHiddenPoint()
        {
            List<SourcePausePointInstructionCandidate> instructions = new List<SourcePausePointInstructionCandidate>
            {
                Instruction(0), Instruction(1),                                   // line 10: await setup
                Instruction(2, SourcePausePointInstructionFlow.ConditionalBranch), // line 10: brtrue continue
                Instruction(3),                                                   // hidden: state save
                Instruction(4, SourcePausePointInstructionFlow.Return),           // hidden: ret
                Instruction(5),                                                   // line 11
                Instruction(6, SourcePausePointInstructionFlow.Return),
            };
            List<SourcePausePointSequencePointCandidate> points = new List<SourcePausePointSequencePointCandidate>
            {
                Point(10, 0),
                Point(0, 3, hidden: true),
                Point(11, 5),
            };

            SourcePausePointPostLineSite site = SourcePausePointPostLineSiteLocator.Locate(instructions, points, 0);

            Assert.That(site.Kind, Is.EqualTo(SourcePausePointPostLineSiteKind.BeforeControlTransfer));
            Assert.That(site.InstructionIndex, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a statement that always throws has no post-line state, so the locator
        /// reports AlwaysThrows instead of an instruction index.
        /// </summary>
        [Test]
        public void Locate_ThrowStatement_ReportsAlwaysThrows()
        {
            List<SourcePausePointInstructionCandidate> instructions = new List<SourcePausePointInstructionCandidate>
            {
                Instruction(0),
                Instruction(1, SourcePausePointInstructionFlow.Throw),
                Instruction(2),
                Instruction(3, SourcePausePointInstructionFlow.Return),
            };
            List<SourcePausePointSequencePointCandidate> points = new List<SourcePausePointSequencePointCandidate>
            {
                Point(10, 0),
                Point(11, 2),
            };

            SourcePausePointPostLineSite site = SourcePausePointPostLineSiteLocator.Locate(instructions, points, 0);

            Assert.That(site.Kind, Is.EqualTo(SourcePausePointPostLineSiteKind.AlwaysThrows));
            Assert.That(site.InstructionIndex, Is.EqualTo(-1));
        }

        /// <summary>
        /// What: sequence points that belong to other files are ignored when computing the
        /// range boundary only if they are excluded by the caller; the locator itself uses
        /// every point it is given, so a same-offset point does not create an empty range.
        /// </summary>
        [Test]
        public void Locate_SelectedPointIsLastInMethod_RangeRunsToBodyEnd()
        {
            List<SourcePausePointInstructionCandidate> instructions = new List<SourcePausePointInstructionCandidate>
            {
                Instruction(0),
                Instruction(1),
                Instruction(2, SourcePausePointInstructionFlow.Return),
            };
            List<SourcePausePointSequencePointCandidate> points = new List<SourcePausePointSequencePointCandidate>
            {
                Point(10, 0),
            };

            SourcePausePointPostLineSite site = SourcePausePointPostLineSiteLocator.Locate(instructions, points, 0);

            Assert.That(site.Kind, Is.EqualTo(SourcePausePointPostLineSiteKind.BeforeControlTransfer));
            Assert.That(site.InstructionIndex, Is.EqualTo(2));
        }
    }
}
