using System;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>Implements an internal interface from another file explicitly.</summary>
    public class HotReloadHiddenRunnerFixture : IHotReloadHiddenRunner
    {
        int IHotReloadHiddenRunner.Run() { return 8301; }

        public int RunThroughInterface()
        {
            IHotReloadHiddenRunner runner = this;
            return runner.Run() + 8306;
        }

        public int RunThroughExtension()
        {
            return this.Doubled() + 8307;
        }

        public int RunThroughIndexer()
        {
            return new HotReloadHiddenRunnerIndexed()[this] + 8308;
        }
    }

    /// <summary>Implements an internal interface nested in a type from another file explicitly.</summary>
    public class HotReloadHiddenNestedInterfaceFixture : HotReloadHiddenOuter.IInner
    {
        int HotReloadHiddenOuter.IInner.Run() { return 8302; }
    }

    /// <summary>Holds a nested type that implements an internal interface from another file explicitly.</summary>
    public class HotReloadHiddenImplementerHolder
    {
        /// <summary>Nested implementer of the internal interface.</summary>
        internal class Nested : IHotReloadHiddenRunner
        {
            int IHotReloadHiddenRunner.Run() { return 8303; }
        }
    }

    /// <summary>Implements a property of an internal interface from another file explicitly.</summary>
    public class HotReloadHiddenValueFixture : IHotReloadHiddenValue
    {
        int IHotReloadHiddenValue.Value => 8305;
    }

    /// <summary>Implements an indexer of an internal interface from another file explicitly.</summary>
    public class HotReloadHiddenIndexedFixture : IHotReloadHiddenIndexed
    {
        int IHotReloadHiddenIndexed.this[int index] => index + 8309;
    }

    /// <summary>Implements an event of an internal interface from another file explicitly.</summary>
    public class HotReloadHiddenNotifierFixture : IHotReloadHiddenNotifier
    {
        private Action _changed;

        event Action IHotReloadHiddenNotifier.Changed
        {
            add { _changed += value; Touched = 8310; }
            remove { _changed -= value; }
        }

        public int Touched { get; private set; }
    }

    /// <summary>Implements a method whose signature names this type itself, of an internal interface from another file.</summary>
    public class HotReloadHiddenCopierFixture : IHotReloadHiddenCopier
    {
        public int Mark { get; set; }

        HotReloadHiddenCopierFixture IHotReloadHiddenCopier.Copy(HotReloadHiddenCopierFixture value, ref int count)
        {
            count += 8311;
            return value;
        }
    }

    /// <summary>Class beside the implementers whose edit leaves them unedited.</summary>
    public class HotReloadHiddenNeighborFixture
    {
        public int Value() { return 8304; }
    }
}
