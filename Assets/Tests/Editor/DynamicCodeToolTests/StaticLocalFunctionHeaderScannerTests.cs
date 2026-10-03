using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies which static local function headers the scanner accepts, where each accepted header ends,
    /// and which shapes it rejects.
    /// </summary>
    public sealed class StaticLocalFunctionHeaderScannerTests
    {
        /// <summary>
        /// Verifies a block-bodied header ends at its opening brace.
        /// </summary>
        [Test]
        public void TrySkipHeader_WithABlockBody_EndsAtTheOpeningBrace()
        {
            const string source = "int Add(int a, int b) { return a + b; }";

            bool accepted = StaticLocalFunctionHeaderScanner.TrySkipHeader(
                source, 0, out bool isExpressionBody, out int headerEndIndex);

            Assert.That(accepted, Is.True);
            Assert.That(isExpressionBody, Is.False);
            Assert.That(headerEndIndex, Is.EqualTo(source.IndexOf('{')));
        }

        /// <summary>
        /// Verifies an expression-bodied header ends right after its arrow.
        /// </summary>
        [Test]
        public void TrySkipHeader_WithAnExpressionBody_EndsAfterTheArrow()
        {
            const string source = "  string Name() => \"x\";";

            bool accepted = StaticLocalFunctionHeaderScanner.TrySkipHeader(
                source, 0, out bool isExpressionBody, out int headerEndIndex);

            Assert.That(accepted, Is.True);
            Assert.That(isExpressionBody, Is.True);
            Assert.That(headerEndIndex, Is.EqualTo(source.IndexOf("=>") + 2));
        }

        /// <summary>
        /// Verifies nested generic return types, qualified names, and nested parentheses in the parameter list
        /// are skipped as one header.
        /// </summary>
        [Test]
        public void TrySkipHeader_WithGenericQualifiedTypesAndNestedParentheses_AcceptsTheHeader()
        {
            const string source = "System.Collections.Generic.List<Dictionary<string, int>> Build(int count = (1 + 2)) { }";

            bool accepted = StaticLocalFunctionHeaderScanner.TrySkipHeader(
                source, 0, out bool isExpressionBody, out int headerEndIndex);

            Assert.That(accepted, Is.True);
            Assert.That(isExpressionBody, Is.False);
            Assert.That(headerEndIndex, Is.EqualTo(source.IndexOf('{')));
        }

        /// <summary>
        /// Verifies a header followed by neither a block nor an arrow is rejected and leaves the end at the start.
        /// </summary>
        [Test]
        public void TrySkipHeader_WithoutABody_IsRejected()
        {
            const string source = "int Add(int a);";

            bool accepted = StaticLocalFunctionHeaderScanner.TrySkipHeader(
                source, 0, out bool isExpressionBody, out int headerEndIndex);

            Assert.That(accepted, Is.False);
            Assert.That(isExpressionBody, Is.False);
            Assert.That(headerEndIndex, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a name that is not followed by a parameter list is rejected.
        /// </summary>
        [Test]
        public void TrySkipHeader_WithoutAParameterList_IsRejected()
        {
            Assert.That(
                StaticLocalFunctionHeaderScanner.TrySkipHeader("int value = 1;", 0, out bool _, out int _),
                Is.False);
        }

        /// <summary>
        /// Verifies a parameter list that never closes is rejected.
        /// </summary>
        [Test]
        public void TrySkipHeader_WithAnUnclosedParameterList_IsRejected()
        {
            Assert.That(
                StaticLocalFunctionHeaderScanner.TrySkipHeader("int Add(int a, int b", 0, out bool _, out int _),
                Is.False);
        }

        /// <summary>
        /// Verifies generic arguments that never close are rejected.
        /// </summary>
        [Test]
        public void TrySkipHeader_WithUnclosedGenericArguments_IsRejected()
        {
            Assert.That(
                StaticLocalFunctionHeaderScanner.TrySkipHeader("List<int Build() { }", 0, out bool _, out int _),
                Is.False);
        }

        /// <summary>
        /// Verifies a token that cannot start an identifier is rejected.
        /// </summary>
        [Test]
        public void TrySkipHeader_WithAnInvalidIdentifierStart_IsRejected()
        {
            Assert.That(
                StaticLocalFunctionHeaderScanner.TrySkipHeader("1nt Add() { }", 0, out bool _, out int _),
                Is.False);
        }

        /// <summary>
        /// Verifies a header that runs out before its parameter list is rejected.
        /// </summary>
        [Test]
        public void TrySkipHeader_WhenTheSourceEndsAfterTheName_IsRejected()
        {
            Assert.That(
                StaticLocalFunctionHeaderScanner.TrySkipHeader("int Add   ", 0, out bool _, out int _),
                Is.False);
        }

        /// <summary>
        /// Verifies a parameter list with no return type or name in front of it is rejected.
        /// </summary>
        [Test]
        public void TrySkipHeader_WithoutAName_IsRejected()
        {
            Assert.That(
                StaticLocalFunctionHeaderScanner.TrySkipHeader("() => 1;", 0, out bool _, out int _),
                Is.False);
        }
    }
}
