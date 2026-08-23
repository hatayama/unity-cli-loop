using System;
using System.Collections.Generic;

using NUnit.Framework;

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
        /// What: count-cap overflow alone keeps 20 reported names and the exact discarded count.
        /// </summary>
        [Test]
        public void CaptureFrame_WhenOnlyCountCapDropsVariables_ReportsTwentyNamesAndExactCount()
        {
            int discarded = SourcePausePointConstants.MaxTruncatedVariableNamesReported + 5;
            int localCount = SourcePausePointConstants.MaxCapturedVariableCount + discarded;
            object[] locals = new object[localCount * 2];
            for (int index = 0; index < localCount; index++)
            {
                locals[index * 2] = $"local{index}";
                locals[index * 2 + 1] = index;
            }

            (UloopPausePointCapturedVariableFrame frame, _, bool truncated) =
                SourcePausePointCapture.CaptureFrame(null, Array.Empty<object>(), locals);

            Assert.That(truncated, Is.True);
            Assert.That(frame.TruncatedVariableCount, Is.EqualTo(discarded));
            Assert.That(
                frame.TruncatedVariableNames.Count,
                Is.EqualTo(SourcePausePointConstants.MaxTruncatedVariableNamesReported));
            Assert.That(
                frame.TruncatedVariableNames[0],
                Is.EqualTo($"local{SourcePausePointConstants.MaxCapturedVariableCount}"));
        }

        /// <summary>
        /// What: preview clipping plus count-cap drops unions names in capture order.
        /// </summary>
        [Test]
        public void CaptureFrame_WhenPreviewClipAndCountCapCombine_UnionsNamesInCaptureOrder()
        {
            int discarded = 3;
            int localCount = SourcePausePointConstants.MaxCapturedVariableCount + discarded;
            object[] locals = new object[localCount * 2];
            locals[0] = "longText";
            locals[1] = new string('a', SourcePausePointConstants.MaxCapturedVariableValueLength + 10);
            for (int index = 1; index < localCount; index++)
            {
                locals[index * 2] = $"local{index}";
                locals[index * 2 + 1] = index;
            }

            (UloopPausePointCapturedVariableFrame frame, _, bool truncated) =
                SourcePausePointCapture.CaptureFrame(null, Array.Empty<object>(), locals);

            Assert.That(truncated, Is.True);
            Assert.That(frame.TruncatedVariableCount, Is.EqualTo(1 + discarded));
            Assert.That(
                frame.TruncatedVariableNames,
                Is.EqualTo(new[]
                {
                    "longText",
                    $"local{SourcePausePointConstants.MaxCapturedVariableCount}",
                    $"local{SourcePausePointConstants.MaxCapturedVariableCount + 1}",
                    $"local{SourcePausePointConstants.MaxCapturedVariableCount + 2}"
                }));
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
    }
}
