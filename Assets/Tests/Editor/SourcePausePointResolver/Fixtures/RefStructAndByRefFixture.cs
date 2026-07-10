// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointResolverTests.
// Do not reformat or edit this file; add a new fixture file instead.
namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointResolverFixtures
{
    internal ref struct CustomRefStruct
    {
        public int Value;
    }

    internal ref struct GenericRefStruct<T>
    {
        public T Value;
    }

    internal sealed class RefStructAndByRefFixture
    {
        // ref/out/in parameters and ref-struct/Span locals exist here specifically to verify
        // the resolver excludes non-capturable locals and parameters.
        public int Combine(int value, ref int accumulator, out int doubled, in int multiplier)
        {
            CustomRefStruct customRefStruct = new CustomRefStruct { Value = value };
            GenericRefStruct<int> genericRefStruct = new GenericRefStruct<int> { Value = value };
            System.Span<int> span = stackalloc int[1];
            int result = customRefStruct.Value * multiplier;
            doubled = result * 2;
            accumulator += result;
            span[0] = result;
            return span[0];
        }
    }
}
