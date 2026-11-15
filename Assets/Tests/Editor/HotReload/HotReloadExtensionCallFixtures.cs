using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Internal extension method the extension-call tests reach from edited bodies.
    /// </summary>
    internal static class HotReloadInternalExtensionCallMath
    {
        internal static int Twice(this int value, int add)
        {
            return value * 2 + add;
        }

        // Why a string receiver: C# cannot form a delegate from an extension method on a value type.
        internal static int LengthPlus(this string value, int add)
        {
            return value.Length + add;
        }
    }

    /// <summary>
    /// Public extension method the extension-call tests use as the accessible control, beside an
    /// internal one whose class is visible, so a call naming the class reaches the call check.
    /// </summary>
    public static class HotReloadPublicExtensionCallMath
    {
        public static int PublicTwice(this int value, int add)
        {
            return value * 2 + add;
        }

        internal static int InternalTwice(this int value, int add)
        {
            return value * 2 + add;
        }
    }

    /// <summary>
    /// Compiled host whose methods call extension methods in the shapes the extension-call tests
    /// edit. Each body's 4-digit token is unique in this file and is the value the tests edit.
    /// </summary>
    public sealed class HotReloadExtensionCallHost
    {
        private int _value = 3;

        private string _label = "abc";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureReadsPrivateFieldAndCallsInternalExtension()
        {
            Func<int> read = () => _value.Twice(4101);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsOnlyInternalExtension(int seed)
        {
            Func<int> read = () => seed.Twice(4201);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncCallsInternalExtension(int seed)
        {
            await Task.Yield();
            return seed.Twice(4301);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int OwnStatementCallsInternalExtension()
        {
            return _value.Twice(4401);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionInStaticForm()
        {
            Func<int> read = () => HotReloadInternalExtensionCallMath.Twice(_value, 4501);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionOfPublicClassInStaticForm()
        {
            Func<int> read = () => HotReloadPublicExtensionCallMath.InternalTwice(_value, 4701);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureHoldsInternalExtensionMethodGroup()
        {
            Func<int> read = () =>
            {
                Func<int, int> lengthPlus = _label.LengthPlus;
                return lengthPlus(4901);
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureHoldsPrivateMethodGroup()
        {
            Func<int> read = () =>
            {
                Func<int, int> addSeven = AddSeven;
                return addSeven(5001);
            };
            return read();
        }

        private int AddSeven(int value)
        {
            return value + 7;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureReadsPrivateFieldAndCallsPublicExtension()
        {
            Func<int> read = () => _value.PublicTwice(4601);
            return read();
        }
    }
}
