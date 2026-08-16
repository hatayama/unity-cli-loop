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

        public static int CallGenericHostTarget()
        {
            GenericHost<int> host = new GenericHost<int>();
            return host.Target();
        }

        public static int GenericMethodTarget<T>()
        {
            return 1;
        }

        public static int CallGenericMethodTarget()
        {
            return GenericMethodTarget<int>();
        }

        public static Func<int> CaptureGenericMethodTarget()
        {
            Func<int> captured = GenericMethodTarget<int>;
            return captured;
        }

        public static int SelfRecursive(int remaining)
        {
            if (remaining <= 0)
            {
                return 0;
            }

            return SelfRecursive(remaining - 1);
        }

        public static async Task<int> AsyncSelfRecursive(int remaining)
        {
            if (remaining <= 0)
            {
                return 0;
            }

            return await AsyncSelfRecursive(remaining - 1);
        }

        public static int CalledFromGenericArityCaller()
        {
            return 5;
        }

        public static int Caller(int value)
        {
            return value;
        }

        public static int Caller<T>(int value)
        {
            CalledFromGenericArityCaller();
            return value;
        }

        public static int CalledFromCrossAssembly()
        {
            return 6;
        }
    }

    /// <summary>
    /// Open generic host so call sites go through a constructed <c>GenericHost&lt;int&gt;</c>.
    /// </summary>
    public class GenericHost<T>
    {
        public int Target()
        {
            return 1;
        }
    }
}
