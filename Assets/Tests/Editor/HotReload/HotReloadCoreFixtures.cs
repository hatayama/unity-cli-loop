using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Known methods used by PR-2 matcher / patcher EditMode tests. Overloads exist so the
    /// matcher can prove parameter-type discrimination (hand-written production code must not
    /// introduce overloads; test fixtures may).
    /// </summary>
    public class HotReloadCoreFixture
    {
        public int VoidHits;

        // Public surface the JIT-legal delegation shim may touch (no private/internal access).
        public int PublicSeed = 10;

        public int Add(int left, int right)
        {
            return left + right;
        }

        public int Add(int left, int right, int extra)
        {
            return left + right + extra;
        }

        public static string StaticPing()
        {
            return "original";
        }

        public void VoidBump()
        {
            VoidHits = -1;
        }

        // Sentinel return proves the original body ran before a transplant replaces it.
        public int ReplaceableCompute(int delta)
        {
            return -1 * delta;
        }

        // Delegation target: sentinel proves the original async body ran before forwarding.
        public async Task<int> ReplaceableComputeAsync(int delta)
        {
            await Task.Yield();
            return -1 * delta;
        }
    }

    /// <summary>
    /// Hand-written static shims that mirror the production transplant shape
    /// (instance methods become static with a leading <c>instance</c> argument). Generated
    /// shims mirror user signatures verbatim; repo style rules apply to hand-written code only.
    /// </summary>
    public static class HotReloadHandwrittenShims
    {
        public static string StaticPing__shim0()
        {
            return "patched";
        }

        public static void VoidBump__shim0(HotReloadCoreFixture instance)
        {
            instance.VoidHits = 7;
        }

        public static int ReplaceableCompute__shim0(HotReloadCoreFixture instance, int delta)
        {
            return delta + 42;
        }

        public static int ReplaceableCompute__shim1(HotReloadCoreFixture instance, int delta)
        {
            return delta + 99;
        }

        // JIT-legal async shim: touches only public fixture members so normal JIT succeeds.
        public static async Task<int> ReplaceableComputeAsync__shim0(
            HotReloadCoreFixture instance,
            int delta)
        {
            await Task.Yield();
            return instance.PublicSeed + delta + 1;
        }
    }
}
