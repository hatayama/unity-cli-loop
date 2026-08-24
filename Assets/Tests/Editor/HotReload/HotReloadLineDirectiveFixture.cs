using System;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host for #line placement tests. Statements keep unique tokens so tests can
    /// assert the directive sits immediately before the first statement token.
    /// </summary>
    public sealed class HotReloadLineDirectiveFixture
    {
        private float _value;

        public float LeadingComments()
        {
            // one-line comment above the local
            // second one-line comment
            float leadingComments = 1.000f;
            return leadingComments;
        }

        public float MultiLineComment()
        {
            /*
             * block comment
             * spans three lines
             */
            float multiLineComment = 1.000f;
            return multiLineComment;
        }

        public float TrailingSameLineComment()
        {
            float trailingSameLine = 1.000f; // stays on the statement line
            return trailingSameLine;
        }

        [Obsolete("line-directive attribute fixture")]
        public float AttributedMember()
        {
            float attributedMember = 1.000f;
            return attributedMember;
        }

        public float MultiLineStatement()
        {
            float multiLineStatement = Math.Max(
                1.000f,
                0f);
            return multiLineStatement;
        }

        public float RegionWrapped()
        {
            #region LineDirectiveRegion
            float regionWrapped = 1.000f;
            #endregion
            return regionWrapped;
        }

        public float CommentThenBlankLine()
        {
            // comment then a blank line

            float commentThenBlankLine = 1.000f;
            return commentThenBlankLine;
        }
    }
}
