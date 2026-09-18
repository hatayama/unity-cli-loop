// FROZEN FIXTURE: content and line numbers are asserted by PausePointMethodFilterTests.
// Do not reformat or edit this file; add a new fixture file instead.
namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointResolverFixtures
{
    internal sealed class AddedMethodScopeOwner
    {
        public int Advance(int value)
        {
            int advanced = value + 1;
            return advanced;
        }
    }

    internal sealed class AddedMethodScopeHelper
    {
        public int Step(int value)
        {
            int stepped = value + 2;
            return stepped;
        }
    }
}
