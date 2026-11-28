using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// The partial-type counterpart of <see cref="HotReloadSameNameExtensionCaller"/>.
    /// </summary>
    public partial class HotReloadSameNameExtensionPartialCaller
    {
        private int _seed = 50;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int OwnStatementsValue()
        {
            return 10;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int LambdaValue()
        {
            Func<int> read = () => 20;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ConditionalAccessLambdaValue()
        {
            Func<int> read = () => 30;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int MemberAccessReceiverLambdaValue()
        {
            Func<int> read = () => this._seed + 10;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int IntOverloadLambdaValue()
        {
            Func<int> read = () => 70;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncValue()
        {
            await Task.CompletedTask;
            return 40;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public IEnumerable<int> IteratorValues()
        {
            yield return 50;
        }
    }
}
