using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Public class whose public overload takes any value while private overloads take Memory or
    /// ArraySegment. Code outside the class can call only the public one, while a patch compiled
    /// against a copy of the assembly with every member public would see the private ones too.
    /// </summary>
    public static class HotReloadHiddenOverloads
    {
        public static int Pick(object value)
        {
            return 1;
        }

        private static int Pick(Memory<int> memory)
        {
            return memory.Length;
        }

        private static int Pick(ArraySegment<int> segment)
        {
            return segment.Count;
        }
    }

    /// <summary>
    /// Public class whose public overload takes any value while a private one takes a params array of
    /// Memory, which one Memory argument fits better in its expanded form.
    /// </summary>
    public static class HotReloadHiddenParamsOverloads
    {
        public static int Pick(object value)
        {
            return 1;
        }

        private static int Pick(params Memory<int>[] memories)
        {
            return memories.Length + 10;
        }
    }

    /// <summary>
    /// Public class with a public constructor taking any value and private ones taking Memory or
    /// ArraySegment, for the same reason as HotReloadHiddenOverloads.
    /// </summary>
    public sealed class HotReloadHiddenOverloadBox
    {
        public HotReloadHiddenOverloadBox(object value)
        {
            Value = 1;
        }

        private HotReloadHiddenOverloadBox(Memory<int> memory)
        {
            Value = memory.Length;
        }

        private HotReloadHiddenOverloadBox(ArraySegment<int> segment)
        {
            Value = segment.Count;
        }

        public int Value { get; }
    }

    /// <summary>
    /// Public class with a public indexer taking any value and private ones taking Memory or
    /// ArraySegment, for the same reason as HotReloadHiddenOverloads.
    /// </summary>
    public sealed class HotReloadHiddenOverloadIndexed
    {
        public int this[object value] => 1;

        private int this[Memory<int> memory] => memory.Length;

        private int this[ArraySegment<int> segment] => segment.Count;
    }

    /// <summary>
    /// Public extension class with a public extension method taking any value and private ones
    /// taking Memory or ArraySegment, for the same reason as HotReloadHiddenOverloads.
    /// </summary>
    public static class HotReloadHiddenOverloadExtensions
    {
        public static int Choose(this int value, object other)
        {
            return 1;
        }

        private static int Choose(this int value, Memory<int> memory)
        {
            return memory.Length;
        }

        private static int Choose(this int value, ArraySegment<int> segment)
        {
            return segment.Count;
        }
    }

    /// <summary>
    /// Public extension class with two public extension methods taking any value or a string and a
    /// private one taking ArraySegment, so Unity's compiler chooses between two reachable overloads.
    /// </summary>
    public static class HotReloadHiddenOverloadPairExtensions
    {
        public static int Prefer(this int value, object other)
        {
            return 1;
        }

        public static int Prefer(this int value, string other)
        {
            return 2;
        }

        private static int Prefer(this int value, ArraySegment<int> segment)
        {
            return segment.Count;
        }
    }

    /// <summary>
    /// Base class whose public field a derived class hides with a private one. Code outside the
    /// derived class reads the base field, while a patch that sees every member public would read
    /// the derived one.
    /// </summary>
    public class HotReloadHiddenFieldBase
    {
        public int Count = 1;
    }

    /// <summary>
    /// Derived class hiding the public field of its base with a private one.
    /// </summary>
    public sealed class HotReloadHiddenFieldDerived : HotReloadHiddenFieldBase
    {
#pragma warning disable CS0414 // Read only through a patch that would wrongly see it.
        private new int Count = 2;
#pragma warning restore CS0414
    }

    /// <summary>
    /// Base class whose public overload takes any value while a protected one takes ArraySegment. A
    /// derived class may call the protected one on itself but not on a value typed as this base.
    /// </summary>
    public class HotReloadProtectedOverloadBase
    {
        public int Pick(object value)
        {
            return 1;
        }

        protected int Pick(ArraySegment<int> segment)
        {
            return segment.Count;
        }
    }

    /// <summary>
    /// Derived class whose sites call the base's overloads on itself or on a value typed as the base.
    /// </summary>
    public sealed class HotReloadProtectedOverloadDerived : HotReloadProtectedOverloadBase
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int DerivedEditedInOwnStatements()
        {
            return 7051;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int DerivedEditedInLambda()
        {
            Func<int> read = () => 7061;
            return read();
        }
    }

    /// <summary>
    /// Compiled host whose sites the hidden overload tests edit. Each body's 4-digit token is unique
    /// in this file and is the value the tests replace.
    /// </summary>
    public sealed class HotReloadHiddenOverloadHost
    {
#pragma warning disable CS0414 // Read only by the bodies the tests edit in.
        private int[] _ints = { 1, 2 };
#pragma warning restore CS0414

        private int Own(object value)
        {
            return 1;
        }

        private int Own(ArraySegment<int> segment)
        {
            return segment.Count;
        }

        private int Own(Memory<int> memory)
        {
            return memory.Length;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int EditedInOwnStatements()
        {
            return 7001;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int EditedInLambda()
        {
            Func<int> read = () => 7011;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> EditedInAsync()
        {
            await Task.Yield();
            return 7021;
        }

        /// <summary>
        /// Type nested in the host, which may call the host's private members.
        /// </summary>
        public sealed class Nested
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public int NestedEditedInOwnStatements()
            {
                return 7031;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public int NestedEditedInLambda()
            {
                Func<int> read = () => 7041;
                return read();
            }
        }
    }
}
