using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled type with a field and property accessors that only its own code may use. Tests
    /// edit <see cref="CopyInto"/> to write another instance through them and read back which
    /// instance received the values.
    /// </summary>
    // Why public: accessor-delegate plans compile in a separate shim assembly.
    public sealed class HotReloadWriteTargetHost
    {
        private int _stored;

        public int Tally { get; private set; }

        public int Hidden { private get; set; }

        public int Stored => _stored;

        // Why a ref-returning property without a setter: a write to it goes through the reference
        // its public getter returns, so it calls no setter at all.
        public ref int StoredSlot => ref _stored;

        // Why a private ref-returning property: a write to it calls only the getter, which only
        // this type may call, so the write needs an accessor the rewrite cannot build.
        private ref int PrivateSlot => ref _stored;

        public int HiddenValue => Hidden;

        // Why a private getter beside a public setter: only a read through the indexer needs an
        // accessor, and the accessor rewrite has none for an indexer.
        public int this[int index]
        {
            private get { return _stored + index; }
            set { _stored = value + index; }
        }

        // Why NoInlining: tests edit this body and observe the new one through a direct call,
        // which an inlined copy at the call site would not.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void CopyInto(HotReloadWriteTargetHost other)
        {
            other._stored = 0;
            other.Tally = 0;
            other.Hidden = 0;
        }
    }

    /// <summary>
    /// Compiled type whose indexer returns a reference and has no setter, the shape of the
    /// indexer of <see cref="System.Span{T}"/>. Tests write its slots through the reference the
    /// indexer returns.
    /// </summary>
    public sealed class HotReloadWriteTargetSlots
    {
        private readonly int[] _values = new int[2];

        public ref int this[int index] => ref _values[index];
    }
}
