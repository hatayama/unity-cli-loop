using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A partial type deriving from <see cref="HotReloadInternalMemberHost"/>, so its edited bodies can
    /// reach the protected members of a compiled base type.
    /// </summary>
    public partial class HotReloadPartialDerivedFixture : HotReloadInternalMemberHost
    {
        private int _seed = 1000;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int DerivedValue()
        {
            return 9;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureValue()
        {
            Func<int> read = () => 30;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureSeedValue()
        {
            Func<int> read = () => _seed;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public IEnumerable<int> IteratorValues()
        {
            yield return _seed;
        }
    }
}
