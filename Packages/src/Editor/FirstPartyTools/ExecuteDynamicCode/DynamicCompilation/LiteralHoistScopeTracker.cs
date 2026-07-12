using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Tracks static local function bodies so literal hoisting does not inject outer-scope bindings.
    /// </summary>
    internal sealed class LiteralHoistScopeTracker
    {
        private readonly Stack<int> _suppressedBodyBraceDepths = new();
        private int _braceDepth;
        private bool _pendingBlockBody;
        private bool _inExpressionBody;

        internal bool ShouldSuppressLiteralHoisting =>
            _inExpressionBody || _suppressedBodyBraceDepths.Count > 0;

        internal void OnOpenBrace()
        {
            _braceDepth++;
            if (_pendingBlockBody)
            {
                _suppressedBodyBraceDepths.Push(_braceDepth);
                _pendingBlockBody = false;
            }
        }

        internal void OnCloseBrace()
        {
            if (_suppressedBodyBraceDepths.Count > 0
                && _suppressedBodyBraceDepths.Peek() == _braceDepth)
            {
                _suppressedBodyBraceDepths.Pop();
            }

            _braceDepth--;
        }

        internal void OnSemicolon()
        {
            if (_inExpressionBody)
            {
                _inExpressionBody = false;
            }
        }

        internal bool TryConsumeStaticLocalFunctionHeader(
            string source,
            int index,
            StringBuilder rewrittenSource,
            ref int nextIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(rewrittenSource != null, "rewrittenSource must not be null");

            if (_braceDepth < 1)
            {
                return false;
            }

            if (!IsKeywordAt(source, index, "static"))
            {
                return false;
            }

            int scanIndex = index + "static".Length;
            if (!StaticLocalFunctionHeaderScanner.TrySkipHeader(source, scanIndex, out bool isExpressionBody, out int headerEndIndex))
            {
                return false;
            }

            rewrittenSource.Append(source, index, headerEndIndex - index);
            if (isExpressionBody)
            {
                _inExpressionBody = true;
            }
            else
            {
                _pendingBlockBody = true;
            }

            nextIndex = headerEndIndex;
            return true;
        }

        private static bool IsKeywordAt(string source, int index, string keyword)
        {
            if (index < 0 || index + keyword.Length > source.Length)
            {
                return false;
            }

            if (!HasIdentifierBoundaryBefore(source, index))
            {
                return false;
            }

            for (int offset = 0; offset < keyword.Length; offset++)
            {
                if (source[index + offset] != keyword[offset])
                {
                    return false;
                }
            }

            return HasIdentifierBoundaryAfter(source, index + keyword.Length);
        }

        private static bool HasIdentifierBoundaryBefore(string source, int index)
        {
            if (index <= 0)
            {
                return true;
            }

            char previous = source[index - 1];
            return !char.IsLetterOrDigit(previous) && previous != '_';
        }

        private static bool HasIdentifierBoundaryAfter(string source, int index)
        {
            if (index >= source.Length)
            {
                return true;
            }

            char next = source[index];
            return !char.IsLetterOrDigit(next) && next != '_';
        }
    }
}
