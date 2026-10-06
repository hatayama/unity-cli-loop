using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// The part of a partial type that hot-reload tests edit. Several bodies name members that only
    /// HotReloadPartialTypeFixture.Other.cs declares, so a run given this file alone has to read that
    /// other part to bind them.
    /// </summary>
    public partial class HotReloadPartialTypeFixture
    {
        private int _ownSeed = 2;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int OwnOnly()
        {
            return 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallsOwnPartMembers()
        {
            return OwnOnly() + _ownSeed;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadsOtherPartField()
        {
            return _otherSeed;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallsOtherPartMethod()
        {
            return OtherPartValue();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int TakesOtherPartNested(OtherPartNested value)
        {
            return value.Number;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PassesThisToCompiledApi()
        {
            return HotReloadPartialTypeFixtureConsumer.Describe(this);
        }

        public int ReadsOtherPartProperty
        {
            get { return OtherPartProperty; }
        }

        /// <summary>
        /// A partial type nested in the fixture whose other part holds the field it reads.
        /// </summary>
        public partial class NestedPartial
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public int ReadsOtherPartNestedField()
            {
                return _nestedSeed;
            }
        }

        /// <summary>
        /// A non-partial type nested in the fixture that reads a static field only the outer type's
        /// other part declares.
        /// </summary>
        public sealed class NestedPlain
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public int ReadsOuterOtherPartStatic()
            {
                return _otherStaticSeed;
            }
        }
    }

    /// <summary>
    /// A second declaration of the same partial type in the same file.
    /// </summary>
    public partial class HotReloadPartialTypeFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int SecondBlockMethod()
        {
            return 21;
        }
    }

    /// <summary>
    /// A partial struct, which hot reload skips for being a struct rather than for being partial.
    /// </summary>
    public partial struct HotReloadPartialStructFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int StructValue()
        {
            return 1;
        }
    }
}
