using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike
{
    /// <summary>
    /// Holds private members that the S1 spike snippet must reach through a publicized
    /// reference copy of this test assembly.
    /// </summary>
    public class SpikePrivateAccessFixture
    {
        private int _counter = 10;

        public int CounterForAssert => _counter;

        private void BumpByOne()
        {
            _counter++;
        }

        // Optional enum default forces Cecil to resolve netstandard while writing a publicized
        // copy; without a working resolver Unity 6 fails publicize with AssemblyResolutionException.
        public static bool ContainsWithComparison(
            string source,
            string value,
            StringComparison comparisonType = StringComparison.OrdinalIgnoreCase)
        {
            return source.IndexOf(value, comparisonType) >= 0;
        }

        // Why NoInlining on both patch targets below: these tests verify detour mechanics,
        // and without the attribute the x64 Mono JIT can inline the tiny original bodies into
        // a test method that was JIT-compiled before the patch was applied, so the assertions
        // would measure JIT inlining instead of patching.

        // Transplant target for S1: a Harmony transpiler replaces this body with the IL of a
        // snippet method compiled outside Unity; -1 is a sentinel proving the original body ran.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReplaceableCompute(int delta)
        {
            return -1 * delta;
        }

        // Delegation target for the S1 async pin: a Harmony transpiler replaces this body with
        // a call to an accessor-rewritten async shim; -1 is a sentinel proving the original ran.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> ReplaceableComputeAsync(int delta)
        {
            await Task.Yield();
            return -1 * delta;
        }
    }

    /// <summary>
    /// Internal type the S1 spike snippet must reach, proving that type-level accessibility
    /// (not only member-level) is bypassed for code compiled against a publicized copy.
    /// </summary>
    internal class SpikeInternalFixture
    {
        internal static int SecretSeed()
        {
            return 21;
        }
    }
}
