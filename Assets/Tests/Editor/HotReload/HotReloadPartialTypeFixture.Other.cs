using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// The other part of <see cref="HotReloadPartialTypeFixture"/>. Most tests leave this file out of
    /// the run, so the members declared here are visible to the edited part only when hot reload reads
    /// this file itself.
    /// </summary>
    public partial class HotReloadPartialTypeFixture
    {
        private const int PartialTuning = 4;
        private static int _otherStaticSeed = 17;
        private int _otherSeed = 5;

        private int OtherPartProperty
        {
            get { return 11; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private int OtherPartValue()
        {
            return 7;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int OtherPartOwnMethod()
        {
            return PartialTuning - 1;
        }

        /// <summary>
        /// A type only this part declares, named in a method signature of the edited part.
        /// </summary>
        public sealed class OtherPartNested
        {
            public int Number = 9;
        }

        /// <summary>
        /// The other part of the nested partial type, holding the field the edited part reads.
        /// </summary>
        public partial class NestedPartial
        {
            private int _nestedSeed = 13;
        }
    }

    /// <summary>
    /// Not partial and not in the edited file: stays a compiled type in every run, so its
    /// parameter keeps naming the compiled copy of the fixture.
    /// </summary>
    public static class HotReloadPartialTypeFixtureConsumer
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Describe(HotReloadPartialTypeFixture fixture)
        {
            return fixture.OtherPartOwnMethod();
        }
    }
}
