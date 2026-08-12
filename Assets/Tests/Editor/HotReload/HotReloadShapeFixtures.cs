namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure-computation shapes that reproduce the annotated-vs-plain
    /// <c>SyntaxFactory.AreEquivalent</c> asymmetry used by the hot-reload baseline compare bug.
    /// Three shapes historically returned False (long single return / unchecked multi-statement /
    /// switch); two controls historically returned True (short single / expression-bodied).
    /// </summary>
    internal class HotReloadShapeFixture
    {
        public int ShortSingle()
        {
            return 1;
        }

        public int ExpressionBodied() => 2;

        // Why >90 chars in one return: Annotation on StatementSyntax makes AreEquivalent return
        // False for this shape even when the snapshot text is identical.
        public string LongSingleReturn()
        {
            return "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-long-single-return-body-over-ninety-chars";
        }

        public int UncheckedLongBody(int seed)
        {
            unchecked
            {
                int a = seed * 17;
                int b = a + 31;
                int c = b * 13;
                int d = c ^ (a << 3);
                int e = d + b + c;
                return e ^ (seed + 0x5f3759df);
            }
        }

        public string SwitchMessage(int code)
        {
            switch (code)
            {
                case 0:
                    return "zero";
                case 1:
                    return "one";
                case 2:
                    return "two";
                default:
                    return "other";
            }
        }
    }

    /// <summary>
    /// Why overloads here: production code forbids them, but the arity-normalization test needs
    /// <c>F(int)</c> vs <c>F&lt;T&gt;(int)</c> in one compiled type so the worker binds both symbols.
    /// </summary>
    internal class HotReloadKeyNormalizationFixture
    {
        public int F(int x)
        {
            return x;
        }

        public int F<T>(int x)
        {
            return x + 1;
        }
    }
}
