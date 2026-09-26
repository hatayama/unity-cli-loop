using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;

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
        /// Reads and writes the skip_visibility bit of Mono's native method record.
        /// </summary>
        private static class MonoSkipVisibilityFlag
        {
            // struct _MonoMethod: guint16 flags; guint16 iflags; guint32 token; MonoClass *klass;
            // MonoMethodSignature *signature; const char *name; then a bitfield word whose bits are
            // inline_info, inline_failure, wrapper_type:5, string_ctor, save_lmf, dynamic,
            // sre_method, is_generic, is_inflated, skip_visibility (bit 13).
            private const int FlagsOffset = 0;
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

            public static void SetOnEveryMethodOf(Assembly assembly)
            {
                const BindingFlags everyDeclared = BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (Type type in assembly.GetTypes())
                {
                    foreach (MethodInfo method in type.GetMethods(everyDeclared))
                    {
                        Set(method);
                    }

                    foreach (ConstructorInfo constructor in type.GetConstructors(everyDeclared))
                    {
                        Set(constructor);
                    }
                }
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
