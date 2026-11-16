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
    /// Internal class whose extension methods are public, declared in the same file as the host.
    /// The compiled assembly exports public members of an internal class, so the edited copy of
    /// this file and the compiled one both offer these methods to a reduced call.
    /// </summary>
    internal static class HotReloadInternalClassExtensionCallMath
    {
        public static int Thrice(this int value, int add)
        {
            return value * 3 + add;
        }

        // Why a string receiver: C# cannot form a delegate from an extension method on a value type.
        public static int LengthTimesTwo(this string value, int add)
        {
            return value.Length * 2 + add;
        }

        // Overloads with different parameter types: the call binds ambiguously against every
        // overload of both copies, not only against the twin of the overload it picks.
        public static int Step(this int value, int add)
        {
            return value * 3 + add;
        }

        public static int Step(this int value, string add)
        {
            return value + add.Length;
        }

        // Overloads that both apply to an int argument; the int one is the better match.
        public static int Pick(this int value, int add)
        {
            return value + add;
        }

        public static int Pick(this int value, long add)
        {
            return value + (int)add + 1000;
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

        public static int PublicStep(this int value, int add)
        {
            return value * 4 + add;
        }

        public static int PublicStep(this int value, string add)
        {
            return value + add.Length;
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
        public int ClosureCallsPublicExtensionOfInternalClass()
        {
            Func<int> read = () => _value.Thrice(5101);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsSameNamedExtensionOfSecondClass()
        {
            Func<int> read = () => _value.Thrice("ab") + 5701;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncCallsPublicExtensionOfInternalClass(int seed)
        {
            await Task.Yield();
            return seed.Thrice(5201);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureHoldsPublicExtensionOfInternalClassMethodGroup()
        {
            Func<int> read = () =>
            {
                Func<int, int> lengthTimesTwo = _label.LengthTimesTwo;
                return lengthTimesTwo(5301);
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsOverloadedExtensionOfInternalClass()
        {
            Func<int> read = () => _value.Step(5401);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsBetterOverloadOfExtensionOfInternalClass()
        {
            Func<int> read = () => _value.Pick(5501);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureReadsPrivateFieldAndCallsOverloadedPublicExtension()
        {
            Func<int> read = () => _value.PublicStep(5601);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureReadsPrivateFieldAndCallsPublicExtension()
        {
            Func<int> read = () => _value.PublicTwice(4601);
            return read();
        }
    }
}
