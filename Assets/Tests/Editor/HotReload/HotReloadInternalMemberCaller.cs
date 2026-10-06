using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A plain type that calls an internal member of another plain type. The visibility repro tests
    /// pass it on its own, next to an edit of a partial type, or as a sibling a run brought back.
    /// </summary>
    public class HotReloadInternalMemberCaller
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallsInternal()
        {
            return HotReloadInternalMemberHost.InternalStaticValue();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PlainValue()
        {
            return 1;
        }

        public int CallerProperty
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get { return 30; }
        }
    }
}
