using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Pure-logic coverage for shim PDB sequence-point selection.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointSequencePointSelectorTests
    {
        /// <summary>
        /// What: the diagnosed fake line-108 SP (offset 187, before later line 106/107)
        /// is dropped and the real body.Move SP (offset 256) is selected.
        /// </summary>
        [Test]
        public void SelectIndex_PatchedApplyMoveFakeSequencePoint_PicksRealMoveLine()
        {
            List<SourcePausePointSequencePointCandidate> points =
                new List<SourcePausePointSequencePointCandidate>
                {
                    new SourcePausePointSequencePointCandidate(100, 186, false),
                    new SourcePausePointSequencePointCandidate(108, 187, false),
                    new SourcePausePointSequencePointCandidate(106, 203, false),
                    new SourcePausePointSequencePointCandidate(107, 233, false),
                    new SourcePausePointSequencePointCandidate(108, 256, false)
                };

            int selected = SourcePausePointSequencePointSelector.SelectIndex(points, 108, 109);

            Assert.That(selected, Is.EqualTo(4));
            Assert.That(points[selected].StartLine, Is.EqualTo(108));
            Assert.That(points[selected].Offset, Is.EqualTo(256));
        }

        /// <summary>
        /// What: a while/for line with two legitimate SPs still selects the loop head
        /// (first matching StartLine), not the later condition-evaluation SP.
        /// </summary>
        [Test]
        public void SelectIndex_WhileLoopSameLine_KeepsFirstSequencePoint()
        {
            List<SourcePausePointSequencePointCandidate> points =
                new List<SourcePausePointSequencePointCandidate>
                {
                    new SourcePausePointSequencePointCandidate(10, 20, false),
                    new SourcePausePointSequencePointCandidate(11, 40, false),
                    new SourcePausePointSequencePointCandidate(10, 80, false)
                };

            int selected = SourcePausePointSequencePointSelector.SelectIndex(points, 10, 20);

            Assert.That(selected, Is.EqualTo(0));
            Assert.That(points[selected].Offset, Is.EqualTo(20));
        }

        /// <summary>
        /// What: a request between statements still picks the next real StartLine.
        /// </summary>
        [Test]
        public void SelectIndex_NoInversion_RoundsForwardToNextLine()
        {
            List<SourcePausePointSequencePointCandidate> points =
                new List<SourcePausePointSequencePointCandidate>
                {
                    new SourcePausePointSequencePointCandidate(5, 0, false),
                    new SourcePausePointSequencePointCandidate(10, 12, false),
                    new SourcePausePointSequencePointCandidate(15, 24, false)
                };

            int selected = SourcePausePointSequencePointSelector.SelectIndex(points, 8, 20);

            Assert.That(selected, Is.EqualTo(1));
            Assert.That(points[selected].StartLine, Is.EqualTo(10));
        }

        /// <summary>
        /// What: a for-body request still selects the body SP even though the increment
        /// SP has a smaller StartLine and a larger IL offset.
        /// </summary>
        [Test]
        public void SelectIndex_ForLoopBody_KeepsBodyDespiteLaterIncrementOnHeaderLine()
        {
            List<SourcePausePointSequencePointCandidate> points =
                new List<SourcePausePointSequencePointCandidate>
                {
                    new SourcePausePointSequencePointCandidate(9, 0, false),
                    new SourcePausePointSequencePointCandidate(10, 10, false),
                    new SourcePausePointSequencePointCandidate(13, 30, false),
                    new SourcePausePointSequencePointCandidate(10, 50, false),
                    new SourcePausePointSequencePointCandidate(15, 60, false)
                };

            int selected = SourcePausePointSequencePointSelector.SelectIndex(points, 12, 16);

            Assert.That(selected, Is.EqualTo(2));
            Assert.That(points[selected].StartLine, Is.EqualTo(13));
            Assert.That(points[selected].Offset, Is.EqualTo(30));
        }

        /// <summary>
        /// What: an outer for-increment SP after the inner header's partner does not
        /// drop the inner header (init) when the request is the inner for line.
        /// </summary>
        [Test]
        public void SelectIndex_NestedForHeaders_KeepsInnerHeaderDespiteOuterIncrementWitness()
        {
            List<SourcePausePointSequencePointCandidate> points =
                new List<SourcePausePointSequencePointCandidate>
                {
                    new SourcePausePointSequencePointCandidate(10, 10, false),
                    new SourcePausePointSequencePointCandidate(12, 40, false),
                    new SourcePausePointSequencePointCandidate(14, 60, false),
                    new SourcePausePointSequencePointCandidate(12, 80, false),
                    new SourcePausePointSequencePointCandidate(10, 200, false)
                };

            int selected = SourcePausePointSequencePointSelector.SelectIndex(points, 12, 16);

            Assert.That(selected, Is.EqualTo(1));
            Assert.That(points[selected].StartLine, Is.EqualTo(12));
            Assert.That(points[selected].Offset, Is.EqualTo(40));
        }

        /// <summary>
        /// What: hidden 0xFEEFEE points neither win nor invert a later real SP.
        /// </summary>
        [Test]
        public void SelectIndex_HiddenPoints_AreIgnoredForInversionAndSelection()
        {
            List<SourcePausePointSequencePointCandidate> points =
                new List<SourcePausePointSequencePointCandidate>
                {
                    new SourcePausePointSequencePointCandidate(16707566, 98, true),
                    new SourcePausePointSequencePointCandidate(12, 100, false)
                };

            int selected = SourcePausePointSequencePointSelector.SelectIndex(points, 12, 20);

            Assert.That(selected, Is.EqualTo(1));
            Assert.That(points[selected].Offset, Is.EqualTo(100));
        }
    }
}
