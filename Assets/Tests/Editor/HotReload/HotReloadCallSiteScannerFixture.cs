using System;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled call-site targets and callers for <see cref="HotReloadCallSiteScanner"/> tests.
    /// </summary>
    public static class HotReloadCallSiteScannerFixture
    {
        public static int CalledFromOrdinaryMethod()
        {
            return 1;
        }

        public static int OrdinaryCaller()
        {
            return CalledFromOrdinaryMethod();
        }

        public static int NeverCalled()
        {
            return 2;
        }

        public static int CalledOnlyViaDelegate()
        {
            return 3;
        }

        public static Func<int> CaptureDelegate()
        {
            Func<int> captured = CalledOnlyViaDelegate;
            return captured;
        }

        public static int CalledFromAsyncMethod()
        {
            return 4;
        }

        public static async Task<int> AsyncCaller()
        {
            await Task.Yield();
            return CalledFromAsyncMethod();
        }
    }
}
