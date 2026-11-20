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

        public int RefArgumentReadonly
        {
            get { return Locked; }
            init { Overwrite(ref Locked, value + 3400); }
        }

        public int PreIncrementReadonly
        {
            get { return Locked; }
            init { ++Locked; Plain = value + 3410; }
        }

        public int PreDecrementReadonly
        {
            get { return Locked; }
            init { --Locked; Plain = value + 3420; }
        }

        public int PostDecrementReadonly
        {
            get { return Locked; }
            init { Locked--; Plain = value + 3430; }
        }

        private static void Overwrite(ref int target, int replacement)
        {
            target = replacement;
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

    public struct HotReloadInitPoint
    {
        public int x;
    }

    // Why public readonly fields on a public type: the shim writes such a field directly, while an
    // internal one is reached through a field reference, so only this shape shows the writes the
    // shim cannot compile.
    public class HotReloadInitBox
    {
        public int Value;
    }

    public struct HotReloadInitHolder
    {
        public HotReloadInitBox Box;
    }

    public class HotReloadInitPublicWrites
    {
        public readonly HotReloadInitPoint Position;
        public readonly HotReloadInitHolder Holder;
        public readonly int Locked;
        public int Plain;

        public int MemberOfReadonlyStruct
        {
            get { return Position.x; }
            init { Position.x = value + 3500; }
        }

        public int IncrementMemberOfReadonlyStruct
        {
            get { return Position.x; }
            init { Position.x++; Plain = value + 3510; }
        }

        public int MemberOfParenthesizedReadonlyStruct
        {
            get { return Position.x; }
            init { (Position).x = value + 3540; }
        }

        public int ThroughReferenceInReadonlyStruct
        {
            get { return Holder.Box.Value; }
            init { Holder.Box.Value = value + 3530; }
        }

        public int RefToReadonly
        {
            get { return Locked; }
            init { ref int slot = ref Locked; slot = value + 3520; }
        }
    }

    public interface IHotReloadInitContract
    {
        int Value { get; init; }
    }

    public class HotReloadInitExplicitInterface : IHotReloadInitContract
    {
        internal int Stored;

        int IHotReloadInitContract.Value
        {
            get { return Stored; }
            init { Stored = value + 3600; }
        }
    }

    public record HotReloadInitChild
    {
        public int Value { get; init; }
    }
}
