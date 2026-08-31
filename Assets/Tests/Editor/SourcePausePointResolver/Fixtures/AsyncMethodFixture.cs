// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointResolverTests.
// Do not reformat or edit this file; add a new fixture file instead.
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointResolverFixtures
{
    internal sealed class AsyncMethodFixture
    {
        public async Task<int> ComputeAsync(int seed)
        {
            int doubled = seed * 2;
            await Task.Yield();
            int tripled = doubled + seed;
            return tripled;
        }
    }
}
