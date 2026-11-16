namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Fixture for hot reload of compiled init accessor bodies: a class and a record whose init
    /// writes an ordinary field, plus init bodies that assign a readonly field or an init-only
    /// property, which a shim compiled outside the type cannot assign. Each body holds a unique
    /// number the tests edit.
    /// </summary>
    public class HotReloadInitClass
    {
        internal int XField;

        public int X
        {
            get { return XField; }
            init { XField = value + 1700; }
        }

        public int SetNowInit
        {
            get { return XField; }
            set { XField = value + 1750; }
        }
    }

    public record HotReloadInitRecord(int A)
    {
        internal int BField;

        public int B
        {
            get { return BField; }
            init { BField = value + 1800; }
        }
    }

    public class HotReloadInitWrites
    {
        internal readonly int Locked;
        internal int Plain;

        public int Other { get; init; }

        public int LockedValue => Locked;

        public int AssignReadonly
        {
            get { return Locked; }
            init { Locked = value + 2100; }
        }

        public int CompoundReadonly
        {
            get { return Locked; }
            init { Locked += value + 2200; }
        }

        public int IncrementReadonly
        {
            get { return Locked; }
            init { Locked++; Plain = value + 2300; }
        }

        public int DeconstructReadonly
        {
            get { return Locked; }
            init { (Locked, Plain) = (value, 2400); }
        }

        public int OutReadonly
        {
            get { return Locked; }
            init { int.TryParse("2500", out Locked); Plain = value; }
        }

        public int AssignInitOnly
        {
            get { return Other; }
            init { Other = value + 2600; }
        }

        public int AssignPlain
        {
            get { return Plain; }
            init { Plain = value + 2700; }
        }

        internal HotReloadInitChild Child;

        public int BuildChild
        {
            get { return Child.Value; }
            init { Child = new HotReloadInitChild { Value = value + 2800 } with { Value = value + 2850 }; }
        }
    }

    public class HotReloadInitIndexer
    {
        internal int Stored;

        public int this[int i]
        {
            get { return Stored; }
            init { Stored = value + i + 2900; }
        }
    }

    public record HotReloadInitChild
    {
        public int Value { get; init; }
    }
}
