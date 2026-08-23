// FROZEN FIXTURE: content and line numbers are asserted by PausePointEditedLineRemapTests
// and SourcePausePointResolverTests.
// Do not reformat or edit this file; add a new fixture file instead.
namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointResolverFixtures
{
    internal sealed class EditedLineRemapFixture
    {
        public int UniqueTarget(int value)
        {
            int uniqueRemapProbe = value + 1;
            return uniqueRemapProbe;
        }

        public int UniqueOther(int value)
        {
            int uniqueRemapProbe = value + 1;
            return uniqueRemapProbe;
        }

        public int DuplicateTarget(int value)
        {
            _ = 12345;
            int skip = 0;
            _ = 12345;
            return skip;
        }

        public int DuplicateOther(int value)
        {
            _ = 12345;
            return value;
        }

        public int ZeroMatchOther(int value)
        {
            int zeroMatchOnlyHere = value;
            return zeroMatchOnlyHere;
        }
    }
}
