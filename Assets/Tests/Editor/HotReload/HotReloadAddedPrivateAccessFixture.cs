using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host for added-method private-field access. Direct reads (no lambda)
    /// reproduce the FieldAccessException that the closure-wrapped existing tests miss.
    /// </summary>
    public class HotReloadAddedPrivateAccessFixture
    {
        private int _instanceSecret = 7;

        private static int _staticSecret = 11;

        private static int _staticWritable;

        private const int PrivateConstThree = 3;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadInstanceSecret()
        {
            return _instanceSecret;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadStaticSecret()
        {
            return _staticSecret;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadPrivateConst()
        {
            return PrivateConstThree;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadStaticWritable()
        {
            return _staticWritable;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ResetStaticWritable()
        {
            _staticWritable = 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private int PrivateInstanceSeven()
        {
            return _instanceSecret;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ExistingCaller(int value)
        {
            return value;
        }
    }
}
