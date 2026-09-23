using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host whose methods use a private nested type by its simple name. Tests edit the
    /// bodies and expect the patched copies, which live outside this type, to still find it.
    /// </summary>
    public sealed class HotReloadNestedTypeHost
    {
        private readonly struct Cell
        {
            public readonly int Value;

            public Cell(int value)
            {
                Value = value;
            }

            public static Cell Of(int value)
            {
                return new Cell(value);
            }
        }

        // Why NoInlining: end-to-end tests edit this body and read the new value back through a
        // direct call, which an inlined copy at the call site would not observe.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Sum()
        {
            Cell cell = new Cell(1);
            return cell.Value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Cell Make(int value)
        {
            return Cell.Of(value);
        }
    }
}
