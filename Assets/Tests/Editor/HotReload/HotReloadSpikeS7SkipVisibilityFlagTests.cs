using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using CodeOptimization = UnityEditor.Compilation.CodeOptimization;
using CompilationPipeline = UnityEditor.Compilation.CompilationPipeline;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike
{
    /// <summary>
    /// Spike S7 for hot reload: asks whether code in a separately loaded assembly can reach
    /// private and internal members of another assembly when the Mono runtime's per-method
    /// skip_visibility flag is set before the method is JIT-compiled. S1 pinned that such code
    /// throws FieldAccessException when invoked directly and that IgnoresAccessChecksTo is
    /// ignored. If this flag lifts the check, an introduced type could keep its declared
    /// accessibility and use internals of its own assembly without a transplant.
    /// </summary>
    /// <remarks>
    /// The flag lives in Mono's native MonoMethod record, reached through
    /// RuntimeMethodHandle.Value. The layout is taken from the open-source Mono runtime
    /// (mono/metadata/class-internals.h, struct _MonoMethod) and validated against reflection
    /// before any write, so a layout that moved fails the probe test instead of corrupting memory.
    /// </remarks>
    public class HotReloadSpikeS7SkipVisibilityFlagTests
    {
        // Same snippet S1 invokes directly and pins as throwing FieldAccessException.
        private const string DirectSnippetSource = @"public static class SpikeS7DirectSnippet
{
    public static int PokePrivateMembers(
        io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikePrivateAccessFixture instance,
        int delta)
    {
        instance._counter = instance._counter + delta;
        instance.BumpByOne();
        return instance._counter;
    }

    public static int ReadInternalType()
    {
        return io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikeInternalFixture.SecretSeed();
    }
}
";

        // The private accesses live in compiler-generated methods (MoveNext of the state machine
        // and the closure's method), which are separate MonoMethods that need the flag as well.
        private const string GeneratedMethodsSnippetSource = @"public static class SpikeS7GeneratedSnippet
{
    public static async System.Threading.Tasks.Task<int> PokeAsync(
        io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikePrivateAccessFixture instance,
        int delta)
    {
        await System.Threading.Tasks.Task.Yield();
        instance._counter = instance._counter + delta;
        instance.BumpByOne();
        return instance._counter;
    }

    public static int PokeThroughClosure(
        io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikePrivateAccessFixture instance,
        int delta)
    {
        System.Func<int> read = () => instance._counter + delta;
        return read();
    }
}
";

        // Generic methods are instantiated per type argument into inflated MonoMethods, and a
        // closure inside a generic method lives in a generic display class; both are asked
        // whether they inherit the flag set on the generic definition.
        private const string GenericSnippetSource = @"public static class SpikeS7GenericSnippet
{
    public static int ReadCounter<T>(
        io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikePrivateAccessFixture instance)
    {
        return instance._counter;
    }

    public static int ReadCounterThroughGenericClosure<T>(
        io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikePrivateAccessFixture instance,
        T unused)
    {
        System.Func<int> read = () => instance._counter + (unused == null ? 0 : 1);
        return read();
    }

    public static int CountInternalItems()
    {
        System.Collections.Generic.List<io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikeInternalFixture> items =
            new System.Collections.Generic.List<io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikeInternalFixture>();
        items.Add(new io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikeInternalFixture());
        return items.Count;
    }
}
";

        // A type in the loaded assembly that derives from an internal class and implements an
        // internal interface of another assembly: the type loader, not the JIT, decides whether
        // this loads, and the flag is per method.
        private const string InheritanceSnippetSource = @"public class SpikeS7DerivedSnippet
    : io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikeInternalBaseFixture,
      io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.ISpikeInternalContract
{
    public override int Value()
    {
        return base.Value() + 1;
    }

    public int Contract()
    {
        return 30;
    }

    public static int Run()
    {
        SpikeS7DerivedSnippet instance = new SpikeS7DerivedSnippet();
        io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.ISpikeInternalContract contract = instance;
        return instance.Value() + contract.Contract();
    }
}
";

        // Overrides of internal virtual and abstract members of another assembly, reached the way
        // compiled code reaches them: through a method of the base type.
        private const string OverrideSnippetSource = @"public class SpikeS7VirtualOverrideSnippet
    : io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikeInternalBaseFixture
{
    public override int Value()
    {
        return 70;
    }
}

public class SpikeS7AbstractOverrideSnippet
    : io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikeInternalAbstractBaseFixture
{
    public override int Area()
    {
        return 12;
    }
}

public static class SpikeS7OverrideSnippet
{
    public static int VirtualThroughBase()
    {
        return new SpikeS7VirtualOverrideSnippet().ValueThroughBase();
    }

    public static int AbstractThroughBase()
    {
        return new SpikeS7AbstractOverrideSnippet().AreaThroughBase();
    }
}
";

        // A method left without the flag calls a tiny flagged method whose body reaches an
        // internal member of another assembly. The JIT may inline the callee into the caller, and
        // the question is whether the access check then runs in the unflagged caller's context.
        // AggressiveInlining makes the inlining attempt certain rather than heuristic.
        private const string InliningSnippetSource = @"public static class SpikeS7InliningSnippet
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static int TinyInternalRead()
    {
        return io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikeInternalFixture.SecretSeed();
    }

    public static int UnflaggedCaller()
    {
        int sum = 0;
        for (int index = 0; index < 3; index++)
        {
            sum += TinyInternalRead();
        }

        return sum;
    }
}
";

        /// <summary>
        /// What: the MonoMethod record behind a method handle carries the method's metadata flags
        /// and token at offsets 0 and 4 and its name pointer at the offset the Mono source gives,
        /// and the skip_visibility bit is clear on an ordinary compiled method.
        /// </summary>
        [Test]
        public void MonoMethodLayout_MatchesReflectionOnThisRuntime()
        {
            MethodInfo method = typeof(SpikePrivateAccessFixture).GetMethod(
                nameof(SpikePrivateAccessFixture.ReplaceableCompute));

            // Read-only first: the offsets must be proven against this runtime's MonoMethod
            // before any test writes to it.
            MonoSkipVisibilityFlag.AssertLayoutMatches(method);

            Assert.That(MonoSkipVisibilityFlag.IsSet(method), Is.False, "An ordinary method must not skip visibility checks.");
        }

        /// <summary>
        /// What: the two bits right below skip_visibility in the MonoMethod bitfield word read as
        /// the Mono source orders them: is_generic (bit 11) is set only on a generic method
        /// definition and is_inflated (bit 12) only on a constructed generic method. A probe that
        /// checks them proves the bit order next to bit 13, which the field checks cannot.
        /// </summary>
        [Test]
        public void MonoMethodBitfield_GenericAndInflatedBitsSitRightBelowSkipVisibility()
        {
            MethodInfo ordinary = typeof(SpikePrivateAccessFixture).GetMethod(
                nameof(SpikePrivateAccessFixture.ReplaceableCompute));
            MethodInfo definition = typeof(MonoBitfieldFixture).GetMethod(nameof(MonoBitfieldFixture.Echo));
            MethodInfo constructed = definition.MakeGenericMethod(typeof(int));
            MonoSkipVisibilityFlag.AssertLayoutMatches(ordinary);
            MonoSkipVisibilityFlag.AssertLayoutMatches(definition);
            MonoSkipVisibilityFlag.AssertLayoutMatches(constructed);

            Assert.That(MonoSkipVisibilityFlag.IsBitSet(ordinary, 11), Is.False, "An ordinary method is not generic.");
            Assert.That(MonoSkipVisibilityFlag.IsBitSet(ordinary, 12), Is.False, "An ordinary method is not inflated.");
            Assert.That(MonoSkipVisibilityFlag.IsBitSet(definition, 11), Is.True, "A generic method definition is generic.");
            Assert.That(MonoSkipVisibilityFlag.IsBitSet(definition, 12), Is.False, "A generic method definition is not inflated.");
            Assert.That(MonoSkipVisibilityFlag.IsBitSet(constructed, 12), Is.True, "A constructed generic method is inflated.");
            Assert.That(MonoSkipVisibilityFlag.IsSet(constructed), Is.False, "A constructed method must not skip visibility checks.");
        }

        /// <summary>
        /// What: with the flag set on every method of the snippet assembly before the first call,
        /// the snippet S1 pins as throwing FieldAccessException writes a private field, calls a
        /// private method and calls a method of an internal type of another assembly.
        /// </summary>
        [Test]
        public async Task DirectSnippet_WithFlagSetBeforeFirstCall_ReachesPrivateAndInternalMembers()
        {
            Type snippetType = await HotReloadSpikeS1PublicizedAccessTests.CompileAndLoadSnippetAsync(
                "S7-direct", DirectSnippetSource, "SpikeS7DirectSnippet", new List<string>());
            MonoSkipVisibilityFlag.SetOnEveryMethodOf(snippetType.Assembly);

            Func<SpikePrivateAccessFixture, int, int> poke = (Func<SpikePrivateAccessFixture, int, int>)snippetType
                .GetMethod("PokePrivateMembers")
                .CreateDelegate(typeof(Func<SpikePrivateAccessFixture, int, int>));
            Func<int> readInternal = (Func<int>)snippetType
                .GetMethod("ReadInternalType")
                .CreateDelegate(typeof(Func<int>));

            SpikePrivateAccessFixture fixture = new();
            Assert.That(poke(fixture, 5), Is.EqualTo(16), "10 (initial) + 5 (delta) + 1 (BumpByOne).");
            Assert.That(fixture.CounterForAssert, Is.EqualTo(16));
            Assert.That(readInternal(), Is.EqualTo(21));
        }

        /// <summary>
        /// What: the flag also works for compiler-generated methods, an async state machine's
        /// MoveNext and a lambda closure, when it is set on every method of the assembly,
        /// nested generated types included.
        /// </summary>
        [Test]
        public async Task GeneratedMethods_WithFlagSetOnNestedTypes_ReachPrivateMembers()
        {
            Type snippetType = await HotReloadSpikeS1PublicizedAccessTests.CompileAndLoadSnippetAsync(
                "S7-generated", GeneratedMethodsSnippetSource, "SpikeS7GeneratedSnippet", new List<string>());
            MonoSkipVisibilityFlag.SetOnEveryMethodOf(snippetType.Assembly);

            Func<SpikePrivateAccessFixture, int, Task<int>> pokeAsync = (Func<SpikePrivateAccessFixture, int, Task<int>>)snippetType
                .GetMethod("PokeAsync")
                .CreateDelegate(typeof(Func<SpikePrivateAccessFixture, int, Task<int>>));
            Func<SpikePrivateAccessFixture, int, int> pokeThroughClosure = (Func<SpikePrivateAccessFixture, int, int>)snippetType
                .GetMethod("PokeThroughClosure")
                .CreateDelegate(typeof(Func<SpikePrivateAccessFixture, int, int>));

            SpikePrivateAccessFixture asyncFixture = new();
            Assert.That(await pokeAsync(asyncFixture, 5), Is.EqualTo(16), "10 (initial) + 5 (delta) + 1 (BumpByOne).");
            Assert.That(pokeThroughClosure(new SpikePrivateAccessFixture(), 5), Is.EqualTo(15), "10 (initial) + 5 (delta).");
        }

        /// <summary>
        /// What: a first call that failed the access check leaves nothing cached, so setting the
        /// flag afterwards still lets the next call reach the private members.
        /// </summary>
        [Test]
        public async Task DirectSnippet_FlagSetAfterAFailedFirstCall_NextCallSucceeds()
        {
            Type snippetType = await HotReloadSpikeS1PublicizedAccessTests.CompileAndLoadSnippetAsync(
                "S7-late", DirectSnippetSource, "SpikeS7DirectSnippet", new List<string>());
            Func<SpikePrivateAccessFixture, int, int> poke = (Func<SpikePrivateAccessFixture, int, int>)snippetType
                .GetMethod("PokePrivateMembers")
                .CreateDelegate(typeof(Func<SpikePrivateAccessFixture, int, int>));
            Assert.Throws<FieldAccessException>(
                () => poke(new SpikePrivateAccessFixture(), 5),
                "Precondition: without the flag the snippet fails the access check, as S1 pins.");

            MonoSkipVisibilityFlag.SetOnEveryMethodOf(snippetType.Assembly);

            Assert.That(poke(new SpikePrivateAccessFixture(), 5), Is.EqualTo(16));
        }

        /// <summary>
        /// What: with the flag set on the generic definitions, instantiations over a reference
        /// type and a value type, a closure inside a generic method, and a generic collection of
        /// an internal type of another assembly all reach the private and internal members.
        /// </summary>
        [Test]
        public async Task GenericSnippet_WithFlagSet_ReachesPrivateAndInternalMembers()
        {
            Type snippetType = await HotReloadSpikeS1PublicizedAccessTests.CompileAndLoadSnippetAsync(
                "S7-generic", GenericSnippetSource, "SpikeS7GenericSnippet", new List<string>());
            MonoSkipVisibilityFlag.SetOnEveryMethodOf(snippetType.Assembly);

            MethodInfo readCounter = snippetType.GetMethod("ReadCounter");
            MethodInfo readThroughClosure = snippetType.GetMethod("ReadCounterThroughGenericClosure");
            Func<SpikePrivateAccessFixture, int> readOverString = (Func<SpikePrivateAccessFixture, int>)readCounter
                .MakeGenericMethod(typeof(string))
                .CreateDelegate(typeof(Func<SpikePrivateAccessFixture, int>));
            Func<SpikePrivateAccessFixture, int> readOverInt = (Func<SpikePrivateAccessFixture, int>)readCounter
                .MakeGenericMethod(typeof(int))
                .CreateDelegate(typeof(Func<SpikePrivateAccessFixture, int>));
            Func<SpikePrivateAccessFixture, int, int> closureOverInt = (Func<SpikePrivateAccessFixture, int, int>)readThroughClosure
                .MakeGenericMethod(typeof(int))
                .CreateDelegate(typeof(Func<SpikePrivateAccessFixture, int, int>));
            Func<int> countInternalItems = (Func<int>)snippetType
                .GetMethod("CountInternalItems")
                .CreateDelegate(typeof(Func<int>));

            Assert.That(readOverString(new SpikePrivateAccessFixture()), Is.EqualTo(10), "Instantiation over a reference type.");
            Assert.That(readOverInt(new SpikePrivateAccessFixture()), Is.EqualTo(10), "Instantiation over a value type.");
            Assert.That(closureOverInt(new SpikePrivateAccessFixture(), 3), Is.EqualTo(11), "Closure in a generic method.");
            Assert.That(countInternalItems(), Is.EqualTo(1), "Generic collection of an internal type.");
        }

        /// <summary>
        /// What: a type that derives from an internal class and implements an internal interface
        /// of another assembly loads, and with the flag set its methods call the internal base.
        /// </summary>
        [Test]
        public async Task InheritanceSnippet_WithFlagSet_DerivesFromInternalBaseAndInterface()
        {
            Type snippetType = await HotReloadSpikeS1PublicizedAccessTests.CompileAndLoadSnippetAsync(
                "S7-inherit", InheritanceSnippetSource, "SpikeS7DerivedSnippet", new List<string>());
            MonoSkipVisibilityFlag.SetOnEveryMethodOf(snippetType.Assembly);

            Func<int> run = (Func<int>)snippetType
                .GetMethod("Run")
                .CreateDelegate(typeof(Func<int>));

            Assert.That(run(), Is.EqualTo(38), "7 (base) + 1 (override) + 30 (interface).");
        }

        /// <summary>
        /// What: with the flag set, overrides from another assembly of an internal virtual member
        /// and of an internal abstract member fill the base slots, so a method of the base type
        /// that calls the virtual reaches the override. The C# compiler emits both base members
        /// with the strict (check-access-on-override) flag.
        /// </summary>
        [Test]
        public async Task OverrideSnippet_WithFlagSet_OverridesInternalVirtualAndAbstractMembers()
        {
            Type snippetType = await HotReloadSpikeS1PublicizedAccessTests.CompileAndLoadSnippetAsync(
                "S7-override", OverrideSnippetSource, "SpikeS7OverrideSnippet", new List<string>());
            MonoSkipVisibilityFlag.SetOnEveryMethodOf(snippetType.Assembly);

            Func<int> virtualThroughBase = (Func<int>)snippetType
                .GetMethod("VirtualThroughBase")
                .CreateDelegate(typeof(Func<int>));
            Func<int> abstractThroughBase = (Func<int>)snippetType
                .GetMethod("AbstractThroughBase")
                .CreateDelegate(typeof(Func<int>));

            Assert.That(virtualThroughBase(), Is.EqualTo(70), "7 would mean the override did not fill the slot.");
            Assert.That(abstractThroughBase(), Is.EqualTo(12));
        }

        /// <summary>
        /// What: a method without the flag that inlines a tiny flagged method reaching an internal
        /// member of another assembly throws MethodAccessException, because the JIT checks the
        /// inlined body against the caller's flag. Ungranted code such as a shim therefore cannot
        /// call granted code as it stands.
        /// </summary>
        [Test]
        public async Task InliningSnippet_UnflaggedCallerInliningAFlaggedTinyMethod_ThrowsMethodAccessException()
        {
            AssumeReleaseCodeOptimization();
            Type snippetType = await HotReloadSpikeS1PublicizedAccessTests.CompileAndLoadSnippetAsync(
                "S7-inlining", InliningSnippetSource, "SpikeS7InliningSnippet", new List<string>());
            MethodInfo caller = snippetType.GetMethod("UnflaggedCaller");
            MonoSkipVisibilityFlag.SetOnEveryMethodOfExcept(snippetType.Assembly, caller);
            Assert.That(MonoSkipVisibilityFlag.IsSet(caller), Is.False, "Precondition: the caller stays unflagged.");

            Func<int> call = (Func<int>)caller.CreateDelegate(typeof(Func<int>));

            Assert.Throws<MethodAccessException>(() => call());
        }

        /// <summary>
        /// What: setting NoInlining in the MonoMethod implementation flags of the flagged methods
        /// as well keeps the JIT from inlining them, even over AggressiveInlining, so the
        /// unflagged caller calls the callee, which runs under its own flag and returns the value.
        /// </summary>
        [Test]
        public async Task InliningSnippet_FlaggedTinyMethodMarkedNoInlining_UnflaggedCallerReachesInternalMember()
        {
            AssumeReleaseCodeOptimization();
            Type snippetType = await HotReloadSpikeS1PublicizedAccessTests.CompileAndLoadSnippetAsync(
                "S7-noinlining", InliningSnippetSource, "SpikeS7InliningSnippet", new List<string>());
            MethodInfo caller = snippetType.GetMethod("UnflaggedCaller");
            MethodInfo callee = snippetType.GetMethod("TinyInternalRead");
            MonoSkipVisibilityFlag.SetOnEveryMethodOfExcept(snippetType.Assembly, caller);
            MonoSkipVisibilityFlag.SetNoInlining(callee);

            Func<int> call = (Func<int>)caller.CreateDelegate(typeof(Func<int>));

            Assert.That(call(), Is.EqualTo(63), "Three calls returning 21 each.");
            Assert.That(
                callee.MethodImplementationFlags & MethodImplAttributes.NoInlining,
                Is.EqualTo(MethodImplAttributes.NoInlining),
                "Reflection reads the implementation flags back from the same record.");
        }

        private static void AssumeReleaseCodeOptimization()
        {
            Assume.That(
                CompilationPipeline.codeOptimization,
                Is.EqualTo(CodeOptimization.Release),
                "Debug code optimization turns JIT inlining off, which would make this pass trivially.");
        }

        /// <summary>
        /// Holds one generic method whose definition and constructed form the bitfield probe reads.
        /// </summary>
        private static class MonoBitfieldFixture
        {
            public static T Echo<T>(T value)
            {
                return value;
            }
        }

        /// <summary>
        /// Reads and writes the skip_visibility bit of Mono's native method record.
        /// </summary>
        private static class MonoSkipVisibilityFlag
        {
            // struct _MonoMethod: guint16 flags; guint16 iflags; guint32 token; MonoClass *klass;
            // MonoMethodSignature *signature; const char *name; then a bitfield word whose bits are
            // inline_info, inline_failure, wrapper_type:5, string_ctor, save_lmf, dynamic,
            // sre_method, is_generic, is_inflated, skip_visibility (bit 13).
            private const int FlagsOffset = 0;
            private const int ImplFlagsOffset = 2;
            private const int TokenOffset = 4;
            private static readonly int NameOffset = 8 + (3 * IntPtr.Size) - IntPtr.Size;
            private static readonly int BitfieldOffset = 8 + (3 * IntPtr.Size);
            private const int SkipVisibilityBit = 1 << 13;

            public static void AssertLayoutMatches(MethodBase method)
            {
                IntPtr record = method.MethodHandle.Value;
                Assert.That(record, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(
                    (ushort)Marshal.ReadInt16(record, FlagsOffset),
                    Is.EqualTo((ushort)method.Attributes),
                    "flags must sit at offset 0.");
                Assert.That(
                    (ushort)Marshal.ReadInt16(record, ImplFlagsOffset),
                    Is.EqualTo((ushort)method.MethodImplementationFlags),
                    "iflags must sit at offset 2.");
                Assert.That(
                    Marshal.ReadInt32(record, TokenOffset),
                    Is.EqualTo(method.MetadataToken),
                    "token must sit at offset 4.");
                Assert.That(
                    Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(record, NameOffset)),
                    Is.EqualTo(method.Name),
                    "name pointer must sit right before the bitfield word.");
            }

            public static bool IsSet(MethodBase method)
            {
                return (Marshal.ReadInt32(method.MethodHandle.Value, BitfieldOffset) & SkipVisibilityBit) != 0;
            }

            public static bool IsBitSet(MethodBase method, int bit)
            {
                return (Marshal.ReadInt32(method.MethodHandle.Value, BitfieldOffset) & (1 << bit)) != 0;
            }

            public static void SetOnEveryMethodOf(Assembly assembly)
            {
                SetOnEveryMethodOfExcept(assembly, null);
            }

            public static void SetOnEveryMethodOfExcept(Assembly assembly, MethodBase excluded)
            {
                const BindingFlags everyDeclared = BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (Type type in assembly.GetTypes())
                {
                    foreach (MethodInfo method in type.GetMethods(everyDeclared))
                    {
                        if (method.Equals(excluded))
                        {
                            continue;
                        }

                        Set(method);
                    }

                    foreach (ConstructorInfo constructor in type.GetConstructors(everyDeclared))
                    {
                        Set(constructor);
                    }
                }
            }

            public static void SetNoInlining(MethodBase method)
            {
                AssertLayoutMatches(method);
                IntPtr record = method.MethodHandle.Value;
                short implFlags = Marshal.ReadInt16(record, ImplFlagsOffset);
                Marshal.WriteInt16(record, ImplFlagsOffset, (short)(implFlags | (short)MethodImplAttributes.NoInlining));
            }

            private static void Set(MethodBase method)
            {
                AssertLayoutMatches(method);
                IntPtr record = method.MethodHandle.Value;
                int word = Marshal.ReadInt32(record, BitfieldOffset);
                Marshal.WriteInt32(record, BitfieldOffset, word | SkipVisibilityBit);
            }
        }
    }
}
