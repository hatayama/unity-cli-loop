using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Fixture for hot reload of compiled indexer accessor bodies. Each accessor body holds a unique
    /// number the tests edit, so one edit touches exactly one accessor.
    /// </summary>
    public class HotReloadIndexerSingle
    {
        internal int Stored;

        public int this[int i]
        {
            get => Stored + i + 1100;
            set => Stored = value + i + 1150;
        }
    }

    public class HotReloadIndexerOverloads
    {
        internal int Stored;

        public int this[int i]
        {
            get => i + 1200;
            set => Stored = value + 1220;
        }

        public int this[string s]
        {
            get => s.Length + 1250;
            set => Stored = value + 1270;
        }
    }

    public class HotReloadIndexerNamed
    {
        [IndexerName("Cell")]
        public int this[int i]
        {
            get => i + 1300;
            set { }
        }
    }

    public class HotReloadIndexerTwoParameters
    {
        public int this[int x, int y]
        {
            get => x + y + 1400;
            set { }
        }
    }

    public class HotReloadIndexerParams
    {
        internal int Stored;

        public int this[params int[] xs]
        {
            get => xs.Length + 1800;
            set => Stored = value + xs.Length + 1850;
        }
    }

    public class HotReloadIndexerDefaultParameter
    {
        internal int Stored;

        public int this[int i, int j = 0]
        {
            get => i + j + 1900;
            set => Stored = value + i + j + 1950;
        }
    }

    public class HotReloadIndexerExpressionBodied
    {
        public int this[int i] => i + 1500;
    }

    public class HotReloadIndexerBase
    {
        public virtual int this[int i]
        {
            get { return i; }
            set { }
        }
    }

    public class HotReloadIndexerBaseCaller : HotReloadIndexerBase
    {
        public override int this[int i]
        {
            get => base[i] + 1600;
            set { base[i] = value; }
        }
    }

    public interface IHotReloadIndexerContract
    {
        int this[int i] { get; }
    }

    public class HotReloadIndexerExplicitInterface : IHotReloadIndexerContract
    {
        int IHotReloadIndexerContract.this[int i]
        {
            get { return i + 1700; }
        }
    }
}
