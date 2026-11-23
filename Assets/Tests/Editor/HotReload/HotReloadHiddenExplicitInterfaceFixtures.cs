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
        private int _lastWritten;

        int IHotReloadHiddenIndexed.this[int index]
        {
            get { return index + 8309; }
            set { _lastWritten = value + 8312; }
        }
    }

    /// <summary>Implements an init-only property of an internal interface from another file explicitly.</summary>
    public class HotReloadHiddenInitValueFixture : IHotReloadHiddenInitValue
    {
        private int _stored;

        int IHotReloadHiddenInitValue.Value
        {
            get { return _stored; }
            init { _stored = value + 8313; }
        }
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

    /// <summary>Implements two same-signature members of an internal interface from another file.</summary>
    public class HotReloadHiddenSiblingsFixture : IHotReloadHiddenSiblings
    {
        int IHotReloadHiddenSiblings.Run() { return 8314; }

        int IHotReloadHiddenSiblings.Stop() { return 8315; }
    }

    /// <summary>Implements overloads of an internal interface from another file that differ by parameter type.</summary>
    public class HotReloadHiddenOverloadsFixture : IHotReloadHiddenOverloads
    {
        int IHotReloadHiddenOverloads.Take(int value) { return value + 8316; }

        int IHotReloadHiddenOverloads.Take(string value) { return value.Length + 8317; }
    }

    /// <summary>Class beside the implementers whose edit leaves them unedited.</summary>
    public class HotReloadHiddenNeighborFixture
    {
        public int Value() { return 8304; }
    }
}
