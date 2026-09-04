// FROZEN FIXTURE: line numbers are read from its compiled portable PDB.
// Do not reformat or edit this file; add another fixture method instead.
namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    internal sealed class SourcePausePointPostLineSiteFixture
    {
        public int AssignWhenTrue(bool condition)
        {
            int flag = 0;
            if (condition) flag = 1;
            return flag;
        }

        public int ReturnWhenTrue(bool condition)
        {
            if (condition) return 1;
            return 0;
        }

        public async System.Threading.Tasks.Task<int> AwaitThenAssign()
        {
            int flag = 0;
            await System.Threading.Tasks.Task.Yield(); flag = 1;
            return flag;
        }
    }
}
