using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike;
using Assembly = System.Reflection.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies native grants on generated assemblies without modifying the test assembly.
    /// </summary>
    public sealed class HotReloadMonoInternalAccessGrantTests
    {
        private const string ReadInternal =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadInternalAccessFixture.Read()";
        private const BindingFlags AllDeclared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>
        /// Verifies the current supported Editor passes the read-only layout probe.
        /// </summary>
        [Test]
        public void Probe_CurrentEditorLayout_IsAvailable()
        {
            HotReloadMonoInternalAccessGrant grant = HotReloadMonoInternalAccessGrant.ForThisProcess();
            Assert.That(grant.IsAvailable, Is.True, grant.UnavailableReason);
            Assert.That(grant.UnavailableReason, Is.Empty);
        }

        /// <summary>
        /// Verifies a misplaced token offset disables grants without reading an arbitrary pointer.
        /// </summary>
        [Test]
        public void Probe_MismatchedTokenOffset_IsUnavailable()
        {
            HotReloadMonoInternalAccessGrant grant =
                new HotReloadMonoInternalAccessGrant(new HotReloadMonoMethodLayout(tokenOffset: 2));
            Assert.That(grant.IsAvailable, Is.False);
            Assert.That(grant.UnavailableReason, Does.Contain("token"));
        }

        /// <summary>
        /// Verifies every header field is independently checked before the native name pointer.
        /// </summary>
        [TestCase(2, 2, 4, "flags")]
        [TestCase(0, 0, 4, "implementation flags")]
        [TestCase(0, 2, 0, "token")]
        public void Probe_InvalidHeaderFields_IsUnavailable(
            int flagsOffset, int implementationFlagsOffset, int tokenOffset, string field)
        {
            HotReloadMonoInternalAccessGrant grant = new HotReloadMonoInternalAccessGrant(
                new HotReloadMonoMethodLayout(flagsOffset, implementationFlagsOffset, tokenOffset));
            Assert.That(grant.IsAvailable, Is.False);
            Assert.That(grant.UnavailableReason, Does.Contain(field));
        }

        /// <summary>
        /// Verifies an unavailable grant refuses its caller before changing any native flags.
        /// </summary>
        [Test]
        public void Grant_Unavailable_ThrowsBeforeWriting()
        {
            AvailableGrant();
            HotReloadMonoInternalAccessGrant grant =
                new HotReloadMonoInternalAccessGrant(new HotReloadMonoMethodLayout(tokenOffset: 2));
            MethodInfo method = typeof(HotReloadInternalAccessFixture).GetMethod("Read", AllDeclared);
            List<NativeFlags> before = new List<NativeFlags> { new NativeFlags(method) };

            Assert.Throws<InvalidOperationException>(() => grant.Grant(typeof(HotReloadInternalAccessFixture).Assembly));

            AssertUnchanged(before);
        }

        /// <summary>
        /// Verifies a null assembly is rejected independently of availability.
        /// </summary>
        [Test]
        public void Grant_NullAssembly_IsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => HotReloadMonoInternalAccessGrant.ForThisProcess().Grant(null));
        }

        /// <summary>
        /// Verifies the production grant allows direct access from a generated assembly.
        /// </summary>
        [Test]
        public async Task Grant_DirectInternalAccess_ReturnsExpectedValue()
        {
            Type snippet = await CompileSnippetAsync("Direct",
                "public static class GrantCandidate { public static int Read() => " + ReadInternal + "; }");
            List<NativeFlags> before = Snapshot(snippet.Assembly);

            HotReloadInternalAccessGrantResult result = AvailableGrant().Grant(snippet.Assembly);

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(result.MethodCount, Is.EqualTo(before.Count));
            Assert.That(result.Reason, Is.Empty);
            Assert.That(snippet.GetMethod("Read").Invoke(null, null), Is.EqualTo(21));
            AssertGranted(before);
        }

        /// <summary>
        /// Verifies all records are validated before any native write, regardless of enumeration order.
        /// </summary>
        [Test]
        public async Task Grant_LaterMethodValidationFails_WritesNothing()
        {
            Type snippet = await CompileSnippetAsync("LaterValidation",
                "public static class GrantCandidate { public static int A() => " + ReadInternal
                + "; public static int LongerName() => 1; }");
            List<NativeFlags> before = Snapshot(snippet.Assembly);
            HotReloadMonoInternalAccessGrant grant =
                new HotReloadMonoInternalAccessGrant(new HotReloadMonoMethodLayout(maxNameBytes: 8));
            Assert.That(grant.IsAvailable, Is.True, grant.UnavailableReason);

            HotReloadInternalAccessGrantResult result = grant.Grant(snippet.Assembly);

            Assert.That(result.Success, Is.False);
            Assert.That(result.MethodCount, Is.Zero);
            Assert.That(result.Reason, Is.Not.Empty);
            AssertUnchanged(before);
            TargetInvocationException error = Assert.Throws<TargetInvocationException>(
                () => snippet.GetMethod("A").Invoke(null, null));
            Assert.That(error.InnerException, Is.TypeOf<MethodAccessException>());
        }

        /// <summary>
        /// Verifies async, iterator, and generic closure methods receive the same grant.
        /// </summary>
        [Test]
        public async Task Grant_GeneratedMethods_ReachInternals()
        {
            Type snippet = await CompileSnippetAsync("Generated",
                "public static class GrantCandidate { "
                + "public static async System.Threading.Tasks.Task<int> ReadAsync() { "
                + "int value = await System.Threading.Tasks.Task.FromResult(0); return " + ReadInternal + " + value; } "
                + "public static System.Collections.Generic.IEnumerable<int> Iterate() { yield return " + ReadInternal + "; } "
                + "public static int Closure<T>(T value) { System.Func<int> read = () => "
                + ReadInternal + " + (value == null ? 0 : 1); return read(); } }");
            List<NativeFlags> before = Snapshot(snippet.Assembly);
            Assert.That(snippet.Assembly.GetTypes().Length, Is.GreaterThan(1));

            HotReloadInternalAccessGrantResult result = AvailableGrant().Grant(snippet.Assembly);

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(result.MethodCount, Is.EqualTo(before.Count));
            AssertGranted(before);
            Task<int> readTask = (Task<int>)snippet.GetMethod("ReadAsync").Invoke(null, null);
            Assert.That(await readTask, Is.EqualTo(21));
            IEnumerable<int> values = (IEnumerable<int>)snippet.GetMethod("Iterate").Invoke(null, null);
            Assert.That(new List<int>(values), Is.EqualTo(new[] { 21 }));
            MethodInfo closure = snippet.GetMethod("Closure").MakeGenericMethod(typeof(string));
            Assert.That(closure.Invoke(null, new object[] { "value" }), Is.EqualTo(22));
        }

        /// <summary>
        /// Verifies both reference-type and value-type generic instantiations inherit the grant.
        /// </summary>
        [Test]
        public async Task Grant_GenericMethod_ReferenceAndValueArgumentsWork()
        {
            Type snippet = await CompileSnippetAsync("Generic",
                "public static class GrantCandidate { public static int Read<T>() => " + ReadInternal + "; }");
            HotReloadInternalAccessGrantResult result = AvailableGrant().Grant(snippet.Assembly);
            Assert.That(result.Success, Is.True, result.Reason);
            MethodInfo definition = snippet.GetMethod("Read");
            Assert.That(definition.MakeGenericMethod(typeof(string)).Invoke(null, null), Is.EqualTo(21));
            Assert.That(definition.MakeGenericMethod(typeof(int)).Invoke(null, null), Is.EqualTo(21));
        }

        /// <summary>
        /// Verifies instance constructors and static field initialization are granted before first use.
        /// </summary>
        [Test]
        public async Task Grant_ConstructorsAndTypeInitializer_ReachInternals()
        {
            Type snippet = await CompileSnippetAsync("Constructors",
                "public class GrantCandidate { private int value = " + ReadInternal
                + "; private static int seed = " + ReadInternal
                + "; public GrantCandidate() { } public int Read() => value + seed; }");
            List<NativeFlags> before = Snapshot(snippet.Assembly);
            Assert.That(snippet.TypeInitializer, Is.Not.Null);
            Assert.That(before.Exists(flags => flags.Method.Name == ".cctor"), Is.True);
            Assert.That(before.Exists(flags => flags.Method.Name == ".ctor"), Is.True);

            HotReloadInternalAccessGrantResult result = AvailableGrant().Grant(snippet.Assembly);

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(result.MethodCount, Is.EqualTo(before.Count));
            AssertGranted(before);
            Assert.That(snippet.GetMethod("Read").Invoke(Activator.CreateInstance(snippet), null), Is.EqualTo(42));
        }

        /// <summary>
        /// Verifies a separately compiled ungranted Release caller cannot inline the granted callee.
        /// </summary>
        [Test]
        public async Task Grant_ReleaseInlining_UnflaggedCallerReachesInternalMember()
        {
            // Why Ignore, not Assume: batchmode -runTests exits with 2 on an inconclusive test even when
            // nothing failed, which fails the CI job that runs the Editor directly under Debug.
            if (CompilationPipeline.codeOptimization != CodeOptimization.Release)
            {
                Assert.Ignore("Release is required to exercise the JIT inlining boundary.");
            }

            string workName = "GrantInlining-" + Guid.NewGuid().ToString("N");
            // A referencing assembly binds by identity, not by this test's unique directory.
            string calleeAssemblyName = "GrantInliningCallee_" + Guid.NewGuid().ToString("N");
            Type callee = await HotReloadSpikeS1PublicizedAccessTests.CompileAndLoadSnippetAsync(
                workName,
                "public static class GrantCandidate { "
                + "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)] "
                + "public static int Read() => " + ReadInternal + "; }",
                "GrantCandidate", Array.Empty<string>(), calleeAssemblyName);
            HotReloadInternalAccessGrantResult result = AvailableGrant().Grant(callee.Assembly);
            Assert.That(result.Success, Is.True, result.Reason);

            Type caller = await CompileCallerAsync(workName, calleeAssemblyName);
            Assert.That(caller.GetMethod("TargetAssembly").Invoke(null, null), Is.SameAs(callee.Assembly));
            MethodInfo call = caller.GetMethod("Call");
            Assert.That(ReadWord(call) & HotReloadMonoMethodLayout.SkipVisibilityBit, Is.Zero);
            Assert.That(callee.GetMethod("Read").MethodImplementationFlags & MethodImplAttributes.NoInlining,
                Is.EqualTo(MethodImplAttributes.NoInlining));
            Assert.That(call.Invoke(null, null), Is.EqualTo(63));
        }

        /// <summary>
        /// Verifies the native method name is decoded as UTF-8 rather than the Windows ANSI code page.
        /// </summary>
        [Test]
        public async Task Grant_Utf8MethodName_IsValidated()
        {
            Type snippet = await CompileSnippetAsync("Utf8",
                "public static class GrantCandidate { public static int 読む() => " + ReadInternal + "; }");
            HotReloadInternalAccessGrantResult result = AvailableGrant().Grant(snippet.Assembly);
            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(snippet.GetMethod("読む").Invoke(null, null), Is.EqualTo(21));
        }

        /// <summary>
        /// Verifies repeated grants preserve all bits except the two intentionally enabled flags.
        /// </summary>
        [Test]
        public async Task Grant_RepeatedCall_PreservesOtherFlags()
        {
            Type snippet = await CompileSnippetAsync("Repeated",
                "public static class GrantCandidate { public static int Read() => " + ReadInternal + "; }");
            List<NativeFlags> before = Snapshot(snippet.Assembly);
            HotReloadMonoInternalAccessGrant grant = AvailableGrant();

            HotReloadInternalAccessGrantResult first = grant.Grant(snippet.Assembly);
            HotReloadInternalAccessGrantResult second = grant.Grant(snippet.Assembly);

            Assert.That(first.Success, Is.True, first.Reason);
            Assert.That(second.Success, Is.True, second.Reason);
            Assert.That(second.MethodCount, Is.EqualTo(first.MethodCount));
            AssertGranted(before);
            Assert.That(snippet.GetMethod("Read").Invoke(null, null), Is.EqualTo(21));
        }

        /// <summary>
        /// Verifies failed type enumeration yields a refusal with no granted method count.
        /// </summary>
        [Test]
        public void Grant_TypeEnumerationFailure_ReturnsRefused()
        {
            HotReloadInternalAccessGrantResult result = AvailableGrant().Grant(new UnloadableAssembly());
            Assert.That(result.Success, Is.False);
            Assert.That(result.MethodCount, Is.Zero);
            Assert.That(result.Reason, Is.Not.Empty);
        }

        /// <summary>
        /// Verifies success cannot represent a negative number of written method records.
        /// </summary>
        [Test]
        public void Granted_NegativeCount_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HotReloadInternalAccessGrantResult.Granted(-1));
        }

        /// <summary>
        /// Verifies refusal cannot lose the reason and success always has an empty reason.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void Refused_EmptyReason_IsRejected(string reason)
        {
            Assert.Throws<ArgumentException>(() => HotReloadInternalAccessGrantResult.Refused(reason));
            HotReloadInternalAccessGrantResult empty = HotReloadInternalAccessGrantResult.Granted(0);
            Assert.That(empty.Success, Is.True);
            Assert.That(empty.MethodCount, Is.Zero);
            Assert.That(empty.Reason, Is.Empty);
        }

        private static HotReloadMonoInternalAccessGrant AvailableGrant()
        {
            HotReloadMonoInternalAccessGrant grant = HotReloadMonoInternalAccessGrant.ForThisProcess();
            Assert.That(grant.IsAvailable, Is.True, grant.UnavailableReason);
            return grant;
        }

        private static Task<Type> CompileSnippetAsync(string suffix, string source)
        {
            return HotReloadSpikeS1PublicizedAccessTests.CompileAndLoadSnippetAsync(
                "Grant-" + suffix + "-" + Guid.NewGuid().ToString("N"),
                source, "GrantCandidate", Array.Empty<string>());
        }

        private static async Task<Type> CompileCallerAsync(string calleeWorkName, string calleeAssemblyName)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string calleePath = Path.Combine(projectRoot, "Library", "UloopHotReloadSpike",
                calleeWorkName, calleeAssemblyName + ".dll");
            string source = "public static class GrantCaller { public static int Call() { "
                + "int sum = 0; for (int index = 0; index < 3; index++) sum += GrantCandidate.Read(); return sum; } "
                + "public static System.Reflection.Assembly TargetAssembly() => typeof(GrantCandidate).Assembly; }";
            HotReloadIntroducedTypeDescriptor descriptor = new HotReloadIntroducedTypeDescriptor(
                "GrantFixture", "grant-mvid", "GrantCaller", "Assets/GrantCaller.cs", "caller", source);
            HotReloadIntroducedTypeCompilationRequest request = HotReloadIntroducedTypeCompilationRequest.CreateBatch(
                new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "grant-caller").Create(),
                new[] { descriptor }, new[] { typeof(object).Assembly.Location, calleePath }, Array.Empty<string>());
            HotReloadIntroducedTypeCompilerResult result =
                await new HotReloadIntroducedTypeCompiler(
                        new HotReloadRoslynCompilerEnvironment(), new FakeInternalAccessGrant(isAvailable: false))
                    .CompileAsync(request, CancellationToken.None);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result.Artifact.Assembly.GetType("GrantCaller", true);
        }

        private static List<NativeFlags> Snapshot(Assembly assembly)
        {
            AvailableGrant();
            List<NativeFlags> snapshots = new List<NativeFlags>();
            foreach (Type type in assembly.GetTypes())
            {
                foreach (MethodInfo method in type.GetMethods(AllDeclared))
                {
                    snapshots.Add(new NativeFlags(method));
                }

                foreach (ConstructorInfo constructor in type.GetConstructors(AllDeclared))
                {
                    snapshots.Add(new NativeFlags(constructor));
                }
            }

            return snapshots;
        }

        private static void AssertUnchanged(List<NativeFlags> snapshots)
        {
            foreach (NativeFlags before in snapshots)
            {
                Assert.That(ReadWord(before.Method), Is.EqualTo(before.Word), before.Method.Name);
                Assert.That(ReadImplementationFlags(before.Method), Is.EqualTo(before.ImplementationFlags),
                    before.Method.Name);
            }
        }

        private static void AssertGranted(List<NativeFlags> snapshots)
        {
            foreach (NativeFlags before in snapshots)
            {
                Assert.That(ReadWord(before.Method),
                    Is.EqualTo(before.Word | HotReloadMonoMethodLayout.SkipVisibilityBit), before.Method.Name);
                Assert.That(ReadImplementationFlags(before.Method),
                    Is.EqualTo(before.ImplementationFlags | (ushort)MethodImplAttributes.NoInlining), before.Method.Name);
            }
        }

        private static int ReadWord(MethodBase method)
        {
            return Marshal.ReadInt32(method.MethodHandle.Value, 8 + 3 * IntPtr.Size);
        }

        private static ushort ReadImplementationFlags(MethodBase method)
        {
            return (ushort)Marshal.ReadInt16(method.MethodHandle.Value, 2);
        }

        private sealed class NativeFlags
        {
            internal MethodBase Method { get; }
            internal int Word { get; }
            internal ushort ImplementationFlags { get; }

            internal NativeFlags(MethodBase method)
            {
                Method = method;
                Word = ReadWord(method);
                ImplementationFlags = ReadImplementationFlags(method);
            }
        }

        private sealed class UnloadableAssembly : Assembly
        {
            public override Type[] GetTypes()
            {
                throw new ReflectionTypeLoadException(new Type[] { null },
                    new Exception[] { new TypeLoadException("Fixture type cannot be loaded.") });
            }
        }
    }
}
