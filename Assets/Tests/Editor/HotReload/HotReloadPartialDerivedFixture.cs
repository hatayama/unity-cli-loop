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

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async System.Threading.Tasks.Task<int> AsyncValue()
        {
            await System.Threading.Tasks.Task.CompletedTask;
            return 50;
        }
    }
}
