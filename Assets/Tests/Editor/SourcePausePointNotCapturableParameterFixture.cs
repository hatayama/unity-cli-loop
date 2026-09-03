namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    // Read back through Mono.Cecil from Library/ScriptAssemblies/UnityCLILoop.Tests.Editor.dll,
    // so this fixture must stay in the UnityCLILoop.Tests.Editor assembly: a nested test asmdef
    // compiles into a different dll that the Cecil test does not open.
    internal sealed class SourcePausePointNotCapturableParameterFixture
    {
        // ref/out/in and ref-struct parameters exist here specifically to verify that capture
        // reports them as not capturable instead of dropping them silently.
        public int Combine(
            int value,
            ref int accumulator,
            out int doubled,
            in int multiplier,
            System.Span<int> scratch)
        {
            doubled = value * 2;
            accumulator += doubled;
            scratch[0] = accumulator;
            return scratch[0] * multiplier;
        }

        // The leading parameter is byref here specifically so skipFirstParameter is observable:
        // skipping it changes the reported list, which a fixture with a capturable first
        // parameter could never show.
        public int CombineLeadingByRef(ref int accumulator, int value, in int multiplier)
        {
            accumulator += value;
            return accumulator * multiplier;
        }

        // The capturable-only counterpart, so the empty not-capturable list is asserted against
        // a method that really has nothing to report.
        public int Add(int left, int right)
        {
            return left + right;
        }
    }
}
