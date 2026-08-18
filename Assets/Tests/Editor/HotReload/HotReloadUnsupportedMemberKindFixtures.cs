using System;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled types for worker tests that assert ctor/operator/event-accessor Skipped rows
    /// and the local-function parent-method pin. Unique assignment tokens are replaced in
    /// snapshots; do not reuse them across types.
    /// </summary>
    public sealed class HotReloadUnsupportedKindCtorFixture
    {
        public int Marker;

        public HotReloadUnsupportedKindCtorFixture()
        {
            Marker = 11;
        }

        public HotReloadUnsupportedKindCtorFixture(int value)
        {
            Marker = value;
        }

        public int Read()
        {
            return Marker;
        }
    }

    public sealed class HotReloadUnsupportedKindStaticCtorEdited
    {
        public static int Marker;

        static HotReloadUnsupportedKindStaticCtorEdited()
        {
            Marker = 21;
        }

        public static int Read()
        {
            return Marker;
        }
    }

    public sealed class HotReloadUnsupportedKindStaticCtorUnedited
    {
        public static int Marker;

        static HotReloadUnsupportedKindStaticCtorUnedited()
        {
            Marker = 22;
        }

        public static int Read()
        {
            return Marker;
        }
    }

    public sealed class HotReloadUnsupportedKindOperatorFixture
    {
        public int Marker;

        public static HotReloadUnsupportedKindOperatorFixture operator +(
            HotReloadUnsupportedKindOperatorFixture left,
            HotReloadUnsupportedKindOperatorFixture right)
        {
            left.Marker = 31;
            return left;
        }

        public static HotReloadUnsupportedKindOperatorFixture operator -(
            HotReloadUnsupportedKindOperatorFixture left,
            HotReloadUnsupportedKindOperatorFixture right)
        {
            left.Marker = 32;
            return left;
        }

        public int Read()
        {
            return Marker;
        }
    }

    public sealed class HotReloadUnsupportedKindConversionFixture
    {
        public int Marker;

        public static implicit operator int(HotReloadUnsupportedKindConversionFixture value)
        {
            value.Marker = 41;
            return value.Marker;
        }

        public static explicit operator long(HotReloadUnsupportedKindConversionFixture value)
        {
            value.Marker = 42;
            return value.Marker;
        }

        public int Read()
        {
            return Marker;
        }
    }

    public sealed class HotReloadUnsupportedKindEventFixture
    {
        public int Marker;

        public event Action Edited
        {
            add { Marker = 51; }
            remove { }
        }

        public event Action Unedited
        {
            add { Marker = 52; }
            remove { }
        }

        public int Read()
        {
            return Marker;
        }
    }

    /// <summary>
    /// Expression-bodied constructor host. Token 61 is unique in this file.
    /// </summary>
    public sealed class HotReloadUnsupportedKindExpressionCtorFixture
    {
        public int Marker;

        public HotReloadUnsupportedKindExpressionCtorFixture() => Marker = 61;

        public int Read()
        {
            return Marker;
        }
    }

    /// <summary>
    /// Expression-bodied operator host. Token 71 is unique in this file.
    /// </summary>
    public sealed class HotReloadUnsupportedKindExpressionOperatorFixture
    {
        public int Marker;

        public static int operator *(
            HotReloadUnsupportedKindExpressionOperatorFixture left,
            HotReloadUnsupportedKindExpressionOperatorFixture right) => left.Marker = 71;

        public int Read()
        {
            return Marker;
        }
    }

    /// <summary>
    /// Expression-bodied conversion host. Token 81 is unique in this file.
    /// </summary>
    public sealed class HotReloadUnsupportedKindExpressionConversionFixture
    {
        public int Marker;

        public static implicit operator bool(HotReloadUnsupportedKindExpressionConversionFixture value) =>
            (value.Marker = 81) != 0;

        public int Read()
        {
            return Marker;
        }
    }

    /// <summary>
    /// Expression-bodied event-accessor host. Tokens 91 / 92 are unique in this file.
    /// </summary>
    public sealed class HotReloadUnsupportedKindExpressionEventFixture
    {
        public int Marker;

        public event Action ArrowEdited
        {
            add => Marker = 91;
            remove => Marker = 92;
        }

        public int Read()
        {
            return Marker;
        }
    }

    public sealed class HotReloadLocalFunctionParentFixture
    {
        public int Compute(int x)
        {
            int Local()
            {
                return 41;
            }

            return Local() + x;
        }
    }
}
