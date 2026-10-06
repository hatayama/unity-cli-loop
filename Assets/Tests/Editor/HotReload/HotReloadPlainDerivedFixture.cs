using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A plain type deriving from <see cref="HotReloadInternalMemberHost"/>: the control for the
    /// partial type that derives from it.
    /// </summary>
    public class HotReloadPlainDerivedFixture : HotReloadInternalMemberHost
    {
        private int _seed = 1000;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int DerivedValue()
        {
            return 10;
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureSeedPlusValue()
        {
            Func<int> read = () => this._seed;
            return read() + 7;
        }

        public int DerivedProperty
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get { return 40; }
        }
    }
}
