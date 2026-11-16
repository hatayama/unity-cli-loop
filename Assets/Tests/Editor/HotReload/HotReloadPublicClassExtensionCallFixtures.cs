using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Struct receiver for the extension methods below, read by value or by reference.
    /// </summary>
    public struct HotReloadExtensionCallPoint
    {
        public int X;

        public HotReloadExtensionCallPoint(int x)
        {
            X = x;
        }
    }

    /// <summary>
    /// Public class whose extension methods are internal, declared in the same file as the host,
    /// so a lambda or async body that calls one needs an accessor.
    /// </summary>
    public static class HotReloadPublicClassInternalExtensions
    {
        internal static int TriplePlus(this int value, int add)
        {
            return value * 3 + add;
        }

        internal static int PlusOne(this int value, int add)
        {
            return value + add + 1;
        }

        internal static int NullableLengthPlus(this string value, int add)
        {
            return value == null ? add + 1000 : value.Length + add;
        }

        internal static int GenericEcho<T>(this T value, int add)
        {
            return add;
        }

        internal static int RefPointRead(this ref HotReloadExtensionCallPoint value, int add)
        {
            return value.X + add;
        }

        internal static int InPointRead(this in HotReloadExtensionCallPoint value, int add)
        {
            return value.X + add;
        }

        internal static int PointRead(this HotReloadExtensionCallPoint value, int add)
        {
            return value.X + add;
        }
    }

    /// <summary>
    /// Compiled host whose lambdas and async methods call internal extension methods of a public
    /// class. Each body's 4-digit token is unique in this file and is the value the tests edit.
    /// </summary>
    public sealed class HotReloadPublicClassExtensionCallHost
    {
        private int _value = 3;

        private string _label = "abc";

        private string _missingLabel = null;

        private HotReloadExtensionCallPoint _point = new HotReloadExtensionCallPoint(5);

        private string _order = "";

        private int _receiverCalls;

        public string Order => _order;

        public int ReceiverCalls => _receiverCalls;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtension()
        {
            Func<int> read = () => _value.TriplePlus(7101);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncCallsInternalExtension()
        {
            await Task.Yield();
            return _value.TriplePlus(7111);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionInStaticForm()
        {
            Func<int> read = () => HotReloadPublicClassInternalExtensions.TriplePlus(_value, 7121);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureChainsInternalExtensions()
        {
            Func<int> read = () => _value.TriplePlus(7131).PlusOne(1);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionOnCallResult()
        {
            Func<int> read = () => NextReceiver().TriplePlus(RecordArgument(7141));
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionConditionally()
        {
            Func<int> read = () => _label?.NullableLengthPlus(7151) ?? 0;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsGenericInternalExtension()
        {
            Func<int> read = () => _value.GenericEcho(7161);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsRefInternalExtension()
        {
            Func<int> read = () => _point.RefPointRead(7171);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInInternalExtension()
        {
            Func<int> read = () => _point.InPointRead(7181);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionOnStructByValue()
        {
            Func<int> read = () => _point.PointRead(7191);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionOnNullReceiver()
        {
            Func<int> read = () => _missingLabel.NullableLengthPlus(7201);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureHoldsInternalExtensionMethodGroup()
        {
            Func<int> read = () =>
            {
                Func<int, int> lengthPlus = _label.NullableLengthPlus;
                return lengthPlus(7211);
            };
            return read();
        }

        private int NextReceiver()
        {
            _order += "r";
            _receiverCalls++;
            return _value;
        }

        private int RecordArgument(int add)
        {
            _order += "a";
            return add;
        }
    }
}
