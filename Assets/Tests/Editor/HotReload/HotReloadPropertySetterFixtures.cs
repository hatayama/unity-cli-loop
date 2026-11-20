namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Properties whose set accessor bodies hot reload patches: block, static, expression-bodied,
    /// and lambda-capturing setters, plus the getter-only, init, and re-typed shapes that stay Skipped.
    /// Every property name is unique in this file because tests match rows by method-name substring.
    /// </summary>
    public class HotReloadPropertySetterFixture
    {
        private int _stored;
        private static int _staticStored;
        private int _arrowStored;

        public int Stored
        {
            get { return _stored; }
            set { _stored = value + 1001; }
        }

        public static int StaticStored
        {
            get { return _staticStored; }
            set { _staticStored = value + 2002; }
        }

        public int ArrowStored
        {
            get => _arrowStored;
            set => _arrowStored = value + 3003;
        }

        public int GetterOnly
        {
            get { return _stored + 4004; }
        }

        public int InitStored
        {
            get { return _stored; }
            init { _stored = value + 5005; }
        }

        public int LambdaStored
        {
            get { return _stored; }
            set
            {
                System.Func<int, int> add = input => input + _stored + 6006;
                _stored = add(value);
            }
        }

        public int Widened
        {
            get { return 0; }
            set { System.GC.KeepAlive(value + 1212); }
        }
    }

    /// <summary>
    /// A property typed as a nested enum, which a shim outside the type can only name qualified.
    /// </summary>
    public class HotReloadPropertySetterNestedTypeFixture
    {
        public enum Mode
        {
            Off,
            On,
            Boost
        }

        public static int LastNested;

        public Mode NestedMode
        {
            get { return (Mode)(LastNested % 3); }
            set { LastNested = (int)value + 1616; }
        }

        public System.Collections.Generic.List<Mode> NestedModes
        {
            get { return new System.Collections.Generic.List<Mode> { Mode.On }; }
            set { LastNested = value.Count + 1717; }
        }
    }

    /// <summary>
    /// A struct host, whose setter stays Skipped because struct methods are not patched.
    /// </summary>
    public struct HotReloadPropertySetterStructFixture
    {
        private int _stored;

        public int StructStored
        {
            get { return _stored; }
            set { _stored = value + 7007; }
        }
    }

    /// <summary>
    /// A generic host, whose setter stays Skipped because generic types are not patched.
    /// </summary>
    public class HotReloadPropertySetterGenericFixture<T>
    {
        private int _stored;

        public int GenericStored
        {
            get { return _stored; }
            set { _stored = value + 1313; }
        }
    }

    /// <summary>
    /// Records what the interface default setter last stored, since an interface holds no state.
    /// </summary>
    public static class HotReloadPropertySetterInterfaceSink
    {
        public static int Last;
    }

    /// <summary>
    /// An interface default property, whose setter is patched like any other setter.
    /// </summary>
    public interface IHotReloadPropertySetterInterfaceFixture
    {
        int Sink
        {
            get { return 0; }
            set { HotReloadPropertySetterInterfaceSink.Last = value + 8008; }
        }
    }

    /// <summary>
    /// Takes the interface default property as is, so assignments run the default setter.
    /// </summary>
    public sealed class HotReloadPropertySetterInterfaceImpl : IHotReloadPropertySetterInterfaceFixture
    {
    }

    /// <summary>
    /// The contract the explicit-implementation fixture implements.
    /// </summary>
    public interface IHotReloadPropertySetterExplicitContract
    {
        int Contract { get; set; }
    }

    /// <summary>
    /// An explicit interface implementation, whose setter stays Skipped.
    /// </summary>
    public class HotReloadPropertySetterExplicitFixture : IHotReloadPropertySetterExplicitContract
    {
        private int _stored;

        int IHotReloadPropertySetterExplicitContract.Contract
        {
            get { return _stored; }
            set { _stored = value + 9009; }
        }
    }

    /// <summary>
    /// The contract whose property the same-name fixture implements explicitly beside its own.
    /// </summary>
    public interface IHotReloadPropertySetterTwinContract
    {
        int Twin { get; set; }
    }

    /// <summary>
    /// An ordinary property and an explicit implementation with the same name, each with a bodied
    /// setter, so their rows must stay apart.
    /// </summary>
    public class HotReloadPropertySetterTwinFixture : IHotReloadPropertySetterTwinContract
    {
        private int _ordinary;
        private int _explicit;

        public int Twin
        {
            get { return _ordinary; }
            set { _ordinary = value + 1414; }
        }

        int IHotReloadPropertySetterTwinContract.Twin
        {
            get { return _explicit; }
            set { _explicit = value + 1515; }
        }
    }

    /// <summary>
    /// The base whose property the derived fixture's setter reaches through base.
    /// </summary>
    public class HotReloadPropertySetterBaseFixture
    {
        public virtual int Chained { get; set; }
    }

    /// <summary>
    /// A setter that calls base, which stays Skipped.
    /// </summary>
    public class HotReloadPropertySetterDerivedFixture : HotReloadPropertySetterBaseFixture
    {
        public override int Chained
        {
            get { return base.Chained; }
            set { base.Chained = value + 1111; }
        }
    }
}
