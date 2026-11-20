using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host whose methods write private fields from lambdas in the shapes the accessor
    /// rewrite handles through field references. Each 4-digit token is unique in this file and is
    /// the value the tests edit.
    /// </summary>
    public sealed class HotReloadClosureFieldWriteHost
    {
        private static int s_count;

        private int _value;

        private int _other;

        private List<int> _list;

        private int[] _cells = new int[3];

        private HotReloadClosureFieldWriteHost _next;

        private int Amount { get; set; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int StaticFieldAssignInLambda()
        {
            Action assign = () => { s_count = 7101; };
            assign();
            return s_count;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int StaticFieldCompoundAssignInLambda()
        {
            s_count = 0;
            Action assign = () => { s_count += 7201; };
            assign();
            return s_count;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int StaticFieldIncrementInLambda()
        {
            s_count = 7301;
            Action increment = () => s_count++;
            increment();
            return s_count;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int FieldCoalesceAssignInLambda()
        {
            _list = null;
            Action assign = () => { _list ??= new List<int> { 7401 }; };
            assign();
            return _list[0];
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int FieldAssignmentValueUsedInLambda()
        {
            Func<int> assign = () => _value = 7501;
            int returned = assign();
            // Why both: the returned value pins the assignment's result and _value pins the store.
            return returned * 10000 + _value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int FieldDeconstructionInLambda()
        {
            Action assign = () => { (_value, _other) = (7601, 17); };
            assign();
            return _value * 100 + _other;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CompoundAssignThroughFieldReceiverInLambda()
        {
            _next = new HotReloadClosureFieldWriteHost();
            Action assign = () => { _next.Amount += 7701; };
            assign();
            return _next.Amount;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ArrayFieldElementWriteInLambda()
        {
            Action assign = () => { _cells[1] = 7801; };
            assign();
            return _cells[1];
        }
    }
}
