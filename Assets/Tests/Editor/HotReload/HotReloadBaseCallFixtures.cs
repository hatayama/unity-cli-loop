using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Base type for the base-call fixtures: virtual, protected, out/ref, optional, params,
    /// generic, property and non-virtual members a derived body can reach through <c>base.</c>.
    /// </summary>
    public class HotReloadBaseCallBaseFixture
    {
        public virtual int Compute(int input) { return input + 100; }
        protected virtual int ProtectedCompute(int input) { return input + 200; }
        public virtual void Fill(int input, out int result, ref int counter) { result = input + 300; counter++; }
        public virtual int WithOptional(int input, int extra = 7) { return input + extra; }
        public virtual int WithParams(params int[] values) { return values.Length; }
        public virtual T Echo<T>(T value) { return value; }
        public virtual int Value { get { return 400; } }
        public int NonVirtual(int input) { return input + 500; }
        public virtual int Keyword(int @event) { return @event + 600; }

        // Why the symbol is never defined: the compiled caller drops these calls and their
        // argument evaluation, which a hot-reloaded body could not reproduce.
        [System.Diagnostics.Conditional("ULOOP_UNDEFINED_SYMBOL")]
        public virtual void Check(int value) { }

        [System.Diagnostics.Conditional("ULOOP_UNDEFINED_SYMBOL")]
        public virtual void CheckThroughOverride(int value) { }
    }

    /// <summary>
    /// Intermediate type that overrides only CheckThroughOverride, so every other base call from
    /// the derived fixture binds two types up.
    /// </summary>
    public class HotReloadBaseCallMiddleFixture : HotReloadBaseCallBaseFixture
    {
        // An override of a conditional method is conditional too, without carrying the attribute.
        public override void CheckThroughOverride(int value) { }
    }

    /// <summary>
    /// Methods that use <c>base.</c> in every shape hot reload distinguishes. Each "+ 61xx"
    /// number is the unique token a test replaces to edit exactly that method.
    /// </summary>
    public class HotReloadBaseCallDerivedFixture : HotReloadBaseCallMiddleFixture
    {
        private int _bonus = 3;
        public event System.Action Changed;

        // Why NoInlining on every patch target below: without it the JIT can inline an original
        // body into a test method compiled before the apply, so the test would still observe the
        // old value after hot reload patched the method (see HotReloadE2EFixtures).
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override int Compute(int input) { return base.Compute(input) + 6101; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallProtected(int input) { return base.ProtectedCompute(input) + 6102; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallFill(int input)
        {
            int counter = 0;
            base.Fill(input, out int result, ref counter);
            return result + counter + 6103;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallNonVirtual(int input) { return base.NonVirtual(input) + 6104; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallNested(int input) { return base.Compute(base.Compute(input)) + 6105; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override string ToString() { return base.ToString() + "6106"; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallBesidePrivateLambda(int input)
        {
            System.Func<int> readBonus = () => _bonus + 6107;
            return base.Compute(input) + readBonus();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadBaseProperty() { return base.Value + 6111; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int UseMethodGroup(int input)
        {
            System.Func<int, int> compute = base.Compute;
            return compute(input) + 6112;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallInLambda(int input)
        {
            System.Func<int, int> compute = value => base.Compute(value) + 6113;
            return compute(input);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallInLocalFunction(int input)
        {
            int Local(int value) { return base.Compute(value) + 6114; }
            return Local(input);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async System.Threading.Tasks.Task<int> CallInAsync(int input)
        {
            await System.Threading.Tasks.Task.Yield();
            return base.Compute(input) + 6115;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public System.Collections.Generic.IEnumerable<int> CallInIterator(int input)
        {
            yield return base.Compute(input) + 6116;
        }

        public int ViaGetter
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get { return base.Compute(1) + 6117; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallAndRaise(int input)
        {
            Changed?.Invoke();
            return base.Compute(input) + 6118;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallOptionalOmitted(int input) { return base.WithOptional(input) + 6121; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallNamed(int input) { return base.Compute(input: input) + 6122; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallParams(int input) { return base.WithParams(input, input) + 6123; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallGeneric(int input) { return base.Echo(input) + 6124; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallKeywordParameter(int input) { return base.Keyword(input) + 6125; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallParamsExpanded(int input) { return base.WithParams(input) + 6126; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallConditional(int input) { int count = input; base.Check(++count); return count + 6127; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallConditionalOverride(int input) { int count = input; base.CheckThroughOverride(++count); return count + 6128; }
    }

    /// <summary>Generic base type for a base call that binds through a constructed generic type.</summary>
    public class HotReloadBaseCallGenericBaseFixture<T>
    {
        public virtual T Pass(T value) { return value; }
    }

    /// <summary>Overrides a method of a constructed generic base type and calls the base implementation.</summary>
    public class HotReloadBaseCallGenericDerivedFixture : HotReloadBaseCallGenericBaseFixture<int>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override int Pass(int value) { return base.Pass(value) + 6131; }
    }

    /// <summary>
    /// A generic base whose overloads collide once T is int: Pick(T) / Pick(int) and
    /// Take(out T) / Take(ref int). Plain has no colliding overload.
    /// </summary>
    public class HotReloadBaseCallCollisionBaseFixture<T>
    {
        public virtual int Pick(T value) { return 1; }
        public virtual int Pick(int value) { return 2; }
        public virtual int Take(out T value) { value = default; return 3; }
        public virtual int Take(ref int value) { return 4; }
        public virtual int Plain(int value) { return value + 5; }
    }

    /// <summary>Calls the colliding overloads of a constructed generic base type.</summary>
    public class HotReloadBaseCallCollisionDerivedFixture : HotReloadBaseCallCollisionBaseFixture<int>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallPick(int input) { return base.Pick(input) + 6141; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallTakeByRef(int input) { int value = input; return base.Take(ref value) + 6142; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallPlain(int input) { return base.Plain(input) + 6143; }
    }
}
