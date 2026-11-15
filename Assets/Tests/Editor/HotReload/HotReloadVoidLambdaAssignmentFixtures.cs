using System;
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host whose methods assign a private-setter property and a private static property
    /// from lambdas and expression-bodied local functions. Each 4-digit token is unique in this
    /// file and is the value the tests edit.
    /// </summary>
    public sealed class HotReloadVoidLambdaAssignmentHost
    {
        public int Value { get; private set; }

        private static int StaticValue { get; set; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int AssignInActionLambda()
        {
            Action assign = () => Value = 5101;
            assign();
            return Value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CompoundAssignInActionLambda()
        {
            Value = 0;
            Action assign = () => Value += 5201;
            assign();
            return Value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int IncrementInActionLambda()
        {
            Value = 5301;
            Action increment = () => Value++;
            increment();
            return Value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PrefixIncrementInActionLambda()
        {
            Value = 6001;
            Action increment = () => ++Value;
            increment();
            return Value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int AssignInFuncLambda()
        {
            Func<int> assign = () => Value = 5401;
            return assign();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int AssignInArrowLocalFunction()
        {
            void Assign() => Value = 5501;
            Assign();
            return Value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int AssignInValueReturningArrowLocalFunction()
        {
            int Assign() => Value = 6101;
            return Assign();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int AssignInStatementLambda()
        {
            Action assign = () => { Value = 5601; };
            assign();
            return Value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int StaticAssignInActionLambda()
        {
            Action assign = () => StaticValue = 5701;
            assign();
            return StaticValue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int StaticCompoundAssignInActionLambda()
        {
            StaticValue = 0;
            Action assign = () => StaticValue += 5801;
            assign();
            return StaticValue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int StaticIncrementInActionLambda()
        {
            StaticValue = 5901;
            Action increment = () => StaticValue++;
            increment();
            return StaticValue;
        }
    }
}
