using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies the static local function header scanner rejects headers that end before they are complete.
    /// </summary>
    public sealed class StaticLocalFunctionHeaderScannerTruncatedTests
    {
        /// <summary>
        /// Verifies a header cut off inside its return type, generic arguments, name, or parameter list is rejected
        /// and leaves the header end at the start index.
        /// </summary>
        [TestCase("x int F(", TestName = "TrySkipHeader_WithAnUnclosedParameterList_IsRejected")]
        [TestCase("x int. ", TestName = "TrySkipHeader_EndingAfterAQualifierDot_IsRejected")]
        [TestCase("x List<int F(", TestName = "TrySkipHeader_WithUnclosedGenericArguments_IsRejected")]
        [TestCase("x int F", TestName = "TrySkipHeader_EndingInsideTheName_IsRejected")]
        public void TrySkipHeader_WhenTheHeaderIsTruncated_IsRejected(string source)
        {
            bool accepted = StaticLocalFunctionHeaderScanner.TrySkipHeader(
                source, 1, out bool isExpressionBody, out int headerEndIndex);

            Assert.That(accepted, Is.False);
            Assert.That(isExpressionBody, Is.False);
            Assert.That(headerEndIndex, Is.EqualTo(1));
        }
    }
}
