namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike
{
    /// <summary>
    /// Known methods used by PR-2 matcher / patcher EditMode tests. Overloads exist so the
    /// matcher can prove parameter-type discrimination (hand-written production code must not
    /// introduce overloads; test fixtures may).
    /// </summary>
    public class HotReloadCoreFixture
    {
        public int VoidHits;

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
    }

    /// <summary>
    /// Hand-written static shims that mirror the production transplant shape
    /// (instance methods become static with a leading <c>instance</c> argument). Generated
    /// shims mirror user signatures verbatim; repo style rules apply to hand-written code only.
    /// </summary>
    public static class HotReloadHandwrittenShims
    {
        public static int Add__shim0(HotReloadCoreFixture instance, int left, int right)
        {
            return left + right + 100;
        }

        public static int AddThree__shim0(HotReloadCoreFixture instance, int left, int right, int extra)
        {
            return left + right + extra + 1000;
        }

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
    }
}
