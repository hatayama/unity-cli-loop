// FROZEN FIXTURE: content and line numbers are asserted by PausePointEditedLineRemapTests.
// Do not reformat or edit this file; add a new fixture file instead.
namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointResolverFixtures
{
    internal sealed class EditedLineRemapRoundForwardFixture
    {
        public int CommentTarget(int value)
        {
            // uniqueRemapCommentProbe
            int x = value;
            return x;
        }

        public int CommentOther(int value)
        {
            // uniqueRemapCommentProbe
            return value;
        }
    }
}
