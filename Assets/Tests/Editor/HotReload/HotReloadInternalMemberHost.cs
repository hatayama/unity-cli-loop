using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A plain type whose non-public members the visibility repro tests call from edited bodies.
    /// No test passes this file to a run, so the worker can only see the type as compiled.
    /// </summary>
    public class HotReloadInternalMemberHost
    {
        internal int InternalField = 3;

        internal int InternalProperty
        {
            get { return 4; }
        }

        internal int InternalSettableProperty { get; set; } = 20;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int InternalStaticValue()
        {
            return 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal int InternalInstanceValue()
        {
            return 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected int ProtectedValue()
        {
            return 5;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected internal int ProtectedInternalValue()
        {
            return 6;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private protected int PrivateProtectedValue()
        {
            return 7;
        }
    }

    /// <summary>
    /// An internal type the visibility repro tests name from edited bodies.
    /// </summary>
    internal static class HotReloadInternalOnlyType
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Value()
        {
            return 8;
        }
    }
}
