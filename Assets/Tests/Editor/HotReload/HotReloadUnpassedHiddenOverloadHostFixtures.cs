using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.ShimReferenceForeignInternal;

using static io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadHiddenOverloads;
using static io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadUnpassedHostTakeOverloads;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host whose sites the unpassed hidden overload tests edit. The types its edited bodies
    /// use are declared in other files of this assembly, which those tests do not give the reload.
    /// Each body's 4-digit token is unique in this file and is the value the tests replace.
    /// </summary>
    public sealed class HotReloadUnpassedHiddenOverloadHost
    {
#pragma warning disable CS0414 // Read only by the bodies the tests edit in.
        private int[] _ints = { 1, 2 };
#pragma warning restore CS0414

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int EditedInOwnStatements()
        {
            return 7101;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int EditedInLambda()
        {
            Func<int> read = () => 7111;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> EditedInAsync()
        {
            await Task.Yield();
            return 7121;
        }
    }

    /// <summary>
    /// Compiled host derived from a base whose file the unpassed hidden overload tests do not give the
    /// reload, so its protected overloads come from the compiled assembly.
    /// </summary>
    public sealed class HotReloadUnpassedProtectedOverloadDerived : HotReloadProtectedOverloadBase
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int DerivedEditedInOwnStatements()
        {
            return 7151;
        }
    }

    /// <summary>
    /// Compiled host derived from a base whose file the unpassed hidden overload tests do not give the
    /// reload; its nested type reaches the base's static overloads through the outer type.
    /// </summary>
    public sealed class HotReloadUnpassedOuterHost : HotReloadUnpassedHiddenOuterBase
    {
        /// <summary>
        /// Type nested in the host, whose site calls the outer type's inherited overloads by name.
        /// </summary>
        public sealed class Nested
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public int NestedEditedInOwnStatements()
            {
                return 7161;
            }
        }
    }
}
