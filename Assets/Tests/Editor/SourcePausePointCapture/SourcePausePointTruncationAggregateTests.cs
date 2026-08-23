using System;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies count-cap drops and preview-clipped entries share one truncation aggregate.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointTruncationAggregateTests
    {
        /// <summary>
        /// What: preview clipping alone puts that variable name on the aggregate with count 1.
        /// </summary>
        [Test]
        public void CaptureFrame_WhenOnlyPreviewIsClipped_ReportsThatNameAndCountOne()
        {
            string longValue = new string('a', SourcePausePointConstants.MaxCapturedVariableValueLength + 10);
            object[] locals = { "longText", longValue, "hp", 42 };

            (UloopPausePointCapturedVariableFrame frame, List<UloopCapturedVariable> variables, bool truncated) =
                SourcePausePointCapture.CaptureFrame(null, Array.Empty<object>(), locals);

            Assert.That(truncated, Is.True);
            Assert.That(frame.Truncated, Is.True);
            Assert.That(frame.TruncatedVariableCount, Is.EqualTo(1));
            Assert.That(frame.TruncatedVariableNames, Is.EqualTo(new[] { "longText" }));
            Assert.That(variables.Find(variable => variable.Name == "longText").Truncated, Is.True);
            Assert.That(variables.Find(variable => variable.Name == "hp").Truncated, Is.False);
        }

        /// <summary>
        /// What: count-cap overflow alone reports count 25 and local50 through local69 in order.
        /// </summary>
        [Test]
        public void CaptureFrame_WhenOnlyCountCapDropsVariables_ReportsTwentyNamesAndExactCount()
        {
            object[] locals = new object[150];
            for (int index = 0; index < 75; index++)
            {
                locals[index * 2] = $"local{index}";
                locals[index * 2 + 1] = index;
            }

            (UloopPausePointCapturedVariableFrame frame, _, bool truncated) =
                SourcePausePointCapture.CaptureFrame(null, Array.Empty<object>(), locals);

            Assert.That(truncated, Is.True);
            Assert.That(frame.TruncatedVariableCount, Is.EqualTo(25));
            Assert.That(
                frame.TruncatedVariableNames,
                Is.EqualTo(new[]
                {
                    "local50",
                    "local51",
                    "local52",
                    "local53",
                    "local54",
                    "local55",
                    "local56",
                    "local57",
                    "local58",
                    "local59",
                    "local60",
                    "local61",
                    "local62",
                    "local63",
                    "local64",
                    "local65",
                    "local66",
                    "local67",
                    "local68",
                    "local69"
                }));
        }

        /// <summary>
        /// What: preview clipping plus count-cap drops unions names in capture order.
        /// </summary>
        [Test]
        public void CaptureFrame_WhenPreviewClipAndCountCapCombine_UnionsNamesInCaptureOrder()
        {
            object[] locals = new object[106];
            locals[0] = "longText";
            locals[1] = new string('a', SourcePausePointConstants.MaxCapturedVariableValueLength + 10);
            for (int index = 1; index < 53; index++)
            {
                locals[index * 2] = $"local{index}";
                locals[index * 2 + 1] = index;
            }

            (UloopPausePointCapturedVariableFrame frame, _, bool truncated) =
                SourcePausePointCapture.CaptureFrame(null, Array.Empty<object>(), locals);

            Assert.That(truncated, Is.True);
            Assert.That(frame.TruncatedVariableCount, Is.EqualTo(4));
            Assert.That(
                frame.TruncatedVariableNames,
                Is.EqualTo(new[] { "longText", "local50", "local51", "local52" }));
        }

        /// <summary>
        /// What: no clipping and no count-cap drop leaves the aggregate empty.
        /// </summary>
        [Test]
        public void CaptureFrame_WhenNothingIsTruncated_ReportsEmptyAggregate()
        {
            object[] locals = { "speed", 5, "damage", 3 };

            (UloopPausePointCapturedVariableFrame frame, _, bool truncated) =
                SourcePausePointCapture.CaptureFrame(null, Array.Empty<object>(), locals);

            Assert.That(truncated, Is.False);
            Assert.That(frame.Truncated, Is.False);
            Assert.That(frame.TruncatedVariableCount, Is.EqualTo(0));
            Assert.That(frame.TruncatedVariableNames, Is.Empty);
        }

        /// <summary>
        /// What: a clipped value with an empty name still increments the aggregate count.
        /// </summary>
        [Test]
        public void CaptureFrame_WhenClippedValueHasEmptyName_CountsItWithoutReportingAName()
        {
            string longValue = new string('a', SourcePausePointConstants.MaxCapturedVariableValueLength + 10);
            object[] locals = { "", longValue };

            LogAssert.Expect(LogType.Assert, "name must not be null or empty");

            (UloopPausePointCapturedVariableFrame frame, _, bool truncated) =
                SourcePausePointCapture.CaptureFrame(null, Array.Empty<object>(), locals);

            Assert.That(truncated, Is.True);
            Assert.That(frame.Truncated, Is.True);
            Assert.That(frame.TruncatedVariableCount, Is.EqualTo(1));
            Assert.That(frame.TruncatedVariableNames, Is.Empty);
        }

        /// <summary>
        /// What: more than 20 preview clips keep the first 20 names and the exact total count.
        /// </summary>
        [Test]
        public void CaptureFrame_WhenPreviewClipsExceedNameCap_ReportsFirstTwentyNamesAndExactCount()
        {
            string longValue = new string('a', SourcePausePointConstants.MaxCapturedVariableValueLength + 10);
            object[] locals = new object[42];
            for (int index = 0; index < 21; index++)
            {
                locals[index * 2] = $"clip{index}";
                locals[index * 2 + 1] = longValue;
            }

            (UloopPausePointCapturedVariableFrame frame, _, bool truncated) =
                SourcePausePointCapture.CaptureFrame(null, Array.Empty<object>(), locals);

            Assert.That(truncated, Is.True);
            Assert.That(frame.TruncatedVariableCount, Is.EqualTo(21));
            Assert.That(
                frame.TruncatedVariableNames,
                Is.EqualTo(new[]
                {
                    "clip0",
                    "clip1",
                    "clip2",
                    "clip3",
                    "clip4",
                    "clip5",
                    "clip6",
                    "clip7",
                    "clip8",
                    "clip9",
                    "clip10",
                    "clip11",
                    "clip12",
                    "clip13",
                    "clip14",
                    "clip15",
                    "clip16",
                    "clip17",
                    "clip18",
                    "clip19"
                }));
        }
    }
}
