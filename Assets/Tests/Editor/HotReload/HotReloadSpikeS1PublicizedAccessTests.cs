using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Mono.Cecil;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using Assembly = System.Reflection.Assembly;
using CecilFieldAttributes = Mono.Cecil.FieldAttributes;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike
{
    /// <summary>
    /// Spike S1 for hot reload: establishes how generated shims can access private/internal
    /// members of other assemblies on the Editor Mono runtime. Compile time: shims compile
    /// against a publicized reference copy of the target assembly (same assembly name, all
    /// members public). Runtime: this Mono enforces IL accessibility when a method is
    /// JIT-compiled — invoking a private-poking snippet throws FieldAccessException,
    /// IgnoresAccessChecksToAttribute is ignored, and even Harmony-patching such a method
    /// throws at patch time because Harmony must JIT-compile the patch target to detour it
    /// (all pinned by tests below). Shim IL containing inaccessible references must therefore
    /// only ever run inside skip-visibility DynamicMethods. Two mechanisms achieve that and
    /// are proven here: (1) transplanting the shim method's IL into the original method's
    /// Harmony replacement via a transpiler, so the shim itself is never JIT-compiled, and
    /// (2) rewriting private accesses to Harmony AccessTools accessor delegates, which keeps
    /// the shim IL JIT-legal — including inside async state machine bodies.
    /// </summary>
    public class HotReloadSpikeS1PublicizedAccessTests
    {
        private const string HarmonyId = "io.github.hatayama.uloop.hot-reload";
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string FixtureTypeFullName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikePrivateAccessFixture";
        private const string InternalFixtureTypeFullName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikeInternalFixture";

        // The snippet body plays the role of a generated hot reload shim: it is compiled outside
        // Unity against the publicized copy only, then loaded into the Editor Mono runtime.
        private const string SnippetBodySource = @"public static class SpikeS1Snippet
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

        // Simulates the accessor rewrite the transform worker applies to private accesses: the
        // snippet only references its own public delegate field, so every method in it is
        // JIT-legal; the skip-visibility access lives inside the delegate Harmony builds.
        private const string AccessorSnippetSource = @"public static class SpikeS1AccessorSnippet
{
    public static global::HarmonyLib.AccessTools.FieldRef<
        io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikePrivateAccessFixture, int> CounterRef;

    public static int PokeViaAccessor(
        io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikePrivateAccessFixture instance,
        int delta)
    {
        CounterRef(instance) = CounterRef(instance) + delta;
        return CounterRef(instance);
    }
}
";

        // Async variant of the accessor rewrite: the private access lives in the compiler-
        // generated MoveNext body, which stays JIT-legal because it only calls the delegate.
        private const string AsyncAccessorSnippetSource = @"public static class SpikeS1AsyncAccessorSnippet
{
    public static global::HarmonyLib.AccessTools.FieldRef<
        io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikePrivateAccessFixture, int> CounterRef;

    public static async System.Threading.Tasks.Task<int> PokeViaAccessorAsync(
        io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikePrivateAccessFixture instance,
        int delta)
    {
        await System.Threading.Tasks.Task.Yield();
        CounterRef(instance) = CounterRef(instance) + delta;
        return CounterRef(instance);
    }
}
";

        // Mirrors PokePrivateMembers as an async method with DIRECT private accesses; a pinned
        // test shows the compiler-generated MoveNext fails JIT accessibility checks, which is
        // why async bodies need the accessor rewrite instead of raw member access.
        private const string AsyncSnippetBodySource = @"public static class SpikeS1AsyncSnippet
{
    public static async System.Threading.Tasks.Task<int> PokePrivateMembersAsync(
        io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikePrivateAccessFixture instance,
        int delta)
    {
        await System.Threading.Tasks.Task.Yield();
        instance._counter = instance._counter + delta;
        instance.BumpByOne();
        return instance._counter;
    }
}
";

        // The runtime would match IgnoresAccessChecksToAttribute by full name if it supported
        // it, so the snippet declares it locally; a pinned test below shows this Mono ignores it.
        private const string IgnoresAccessChecksToPreamble = @"using System.Runtime.CompilerServices;

[assembly: IgnoresAccessChecksTo(""UnityCLILoop.Tests.Editor.HotReload"")]

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName)
        {
            AssemblyName = assemblyName;
        }

        public string AssemblyName { get; }
    }
}

";

        /// <summary>
        /// What: the Cecil publicizer produces a copy that keeps the original assembly name and
        /// exposes formerly private/internal types and members as public.
        /// </summary>
        [Test]
        public void PublicizedCopy_KeepsAssemblyNameAndExposesPrivateMembers()
        {
            string workRootPath = PrepareCleanDirectory("S1-publicize");
            string publicizedDllPath = Path.Combine(workRootPath, TestAssemblyName + ".Publicized.dll");
            WritePublicizedCopy(ResolveTestAssemblyDllPath(), publicizedDllPath);

            using AssemblyDefinition publicizedAssembly = AssemblyDefinition.ReadAssembly(publicizedDllPath);
            Assert.That(
                publicizedAssembly.Name.Name,
                Is.EqualTo(TestAssemblyName),
                "The publicized copy must keep the original assembly name so runtime type identity resolves to the already-loaded original assembly.");

            TypeDefinition fixtureType = publicizedAssembly.MainModule.GetType(FixtureTypeFullName);
            Assert.That(fixtureType, Is.Not.Null, $"Type not found in publicized copy: {FixtureTypeFullName}");
            Assert.That(FindField(fixtureType, "_counter").IsPublic, Is.True, "_counter must be public in the publicized copy.");
            Assert.That(FindMethod(fixtureType, "BumpByOne").IsPublic, Is.True, "BumpByOne must be public in the publicized copy.");

            TypeDefinition internalFixtureType = publicizedAssembly.MainModule.GetType(InternalFixtureTypeFullName);
            Assert.That(internalFixtureType, Is.Not.Null, $"Type not found in publicized copy: {InternalFixtureTypeFullName}");
            Assert.That(internalFixtureType.IsPublic, Is.True, "SpikeInternalFixture must be public in the publicized copy.");
            Assert.That(FindMethod(internalFixtureType, "SecretSeed").IsPublic, Is.True, "SecretSeed must be public in the publicized copy.");
        }

        [TearDown]
        public void TearDown()
        {
            new Harmony(HarmonyId).UnpatchAll(HarmonyId);
        }

        /// <summary>
        /// What: transplanting the snippet method's IL into an original method via a Harmony
        /// transpiler lets the snippet body read/write private members and internal types at
        /// runtime, because the body executes inside Harmony's skip-visibility DynamicMethod
        /// and the snippet method itself is never JIT-compiled.
        /// </summary>
        [Test]
        public async Task Snippet_TransplantedIntoOriginalMethod_AccessesPrivateMembersAtRuntime()
        {
            Type snippetType = await CompileAndLoadSnippetAsync(
                "S1-transplant", SnippetBodySource, "SpikeS1Snippet", new List<string>());
            MethodInfo pokeMethod = snippetType.GetMethod("PokePrivateMembers", BindingFlags.Public | BindingFlags.Static);
            Assert.That(pokeMethod, Is.Not.Null, "PokePrivateMembers not found.");

            // The static instance method pair (this, delta) / (instance, delta) occupies
            // identical argument slots, so the IL transplants without any rewriting.
            _transplantSourceMethod = pokeMethod;
            MethodInfo originalMethod = AccessTools.Method(
                typeof(SpikePrivateAccessFixture), nameof(SpikePrivateAccessFixture.ReplaceableCompute));
            MethodInfo transpilerMethod = typeof(HotReloadSpikeS1PublicizedAccessTests).GetMethod(
                nameof(ReplaceWithTransplantSourceTranspiler), BindingFlags.Public | BindingFlags.Static);
            Harmony harmony = new(HarmonyId);
            harmony.Patch(originalMethod, transpiler: new HarmonyMethod(transpilerMethod));

            SpikePrivateAccessFixture fixture = new();
            int returnedValue = fixture.ReplaceableCompute(5);
            Assert.That(returnedValue, Is.EqualTo(16), "Transplanted body must observe 10 (initial) + 5 (delta) + 1 (BumpByOne).");
            Assert.That(fixture.CounterForAssert, Is.EqualTo(16), "The original instance must observe the writes made by the transplanted body.");
        }

        /// <summary>
        /// What: a snippet whose private access is rewritten to a Harmony FieldRef accessor
        /// delegate is JIT-legal and reads/writes the private field at runtime, because the
        /// skip-visibility access lives inside the delegate Harmony builds.
        /// </summary>
        [Test]
        public async Task Snippet_UsingFieldRefAccessor_AccessesPrivateFieldAtRuntime()
        {
            List<string> harmonyReference = new() { typeof(Harmony).Assembly.Location };
            Type snippetType = await CompileAndLoadSnippetAsync(
                "S1-accessor", AccessorSnippetSource, "SpikeS1AccessorSnippet", harmonyReference);
            BindAccessorField(snippetType);

            MethodInfo pokeMethod = snippetType.GetMethod("PokeViaAccessor", BindingFlags.Public | BindingFlags.Static);
            Assert.That(pokeMethod, Is.Not.Null, "PokeViaAccessor not found.");
            Func<SpikePrivateAccessFixture, int, int> poke =
                (Func<SpikePrivateAccessFixture, int, int>)pokeMethod.CreateDelegate(typeof(Func<SpikePrivateAccessFixture, int, int>));

            SpikePrivateAccessFixture fixture = new();
            int returnedValue = poke(fixture, 5);
            Assert.That(returnedValue, Is.EqualTo(15), "Accessor snippet must observe 10 (initial) + 5 (delta).");
            Assert.That(fixture.CounterForAssert, Is.EqualTo(15), "The original instance must observe the writes made through the accessor.");
        }

        /// <summary>
        /// What: the accessor rewrite also works inside async methods, where the access lives
        /// in the compiler-generated MoveNext body — proving the mechanism that covers async
        /// bodies, which raw private access cannot (see the pinned async failure below).
        /// </summary>
        [Test]
        public async Task AsyncSnippet_UsingFieldRefAccessor_AccessesPrivateFieldAtRuntime()
        {
            List<string> harmonyReference = new() { typeof(Harmony).Assembly.Location };
            Type snippetType = await CompileAndLoadSnippetAsync(
                "S1-accessor-async", AsyncAccessorSnippetSource, "SpikeS1AsyncAccessorSnippet", harmonyReference);
            BindAccessorField(snippetType);

            MethodInfo pokeAsyncMethod = snippetType.GetMethod("PokeViaAccessorAsync", BindingFlags.Public | BindingFlags.Static);
            Assert.That(pokeAsyncMethod, Is.Not.Null, "PokeViaAccessorAsync not found.");
            Func<SpikePrivateAccessFixture, int, Task<int>> pokeAsync =
                (Func<SpikePrivateAccessFixture, int, Task<int>>)pokeAsyncMethod.CreateDelegate(typeof(Func<SpikePrivateAccessFixture, int, Task<int>>));

            SpikePrivateAccessFixture fixture = new();
            int returnedValue = await pokeAsync(fixture, 5);
            Assert.That(returnedValue, Is.EqualTo(15), "Async accessor snippet must observe 10 (initial) + 5 (delta).");
            Assert.That(fixture.CounterForAssert, Is.EqualTo(15), "The original instance must observe the writes made through the accessor.");
        }

        /// <summary>
        /// What: an async snippet with DIRECT private accesses fails with FieldAccessException
        /// when its compiler-generated MoveNext body is JIT-compiled on first execution —
        /// pinning why async bodies need the accessor rewrite.
        /// </summary>
        [Test]
        public async Task AsyncSnippet_InvokedDirectly_ThrowsFieldAccessException()
        {
            Type snippetType = await CompileAndLoadSnippetAsync(
                "S1-enforce-async", AsyncSnippetBodySource, "SpikeS1AsyncSnippet", new List<string>());

            MethodInfo pokeAsyncMethod = snippetType.GetMethod("PokePrivateMembersAsync", BindingFlags.Public | BindingFlags.Static);
            Assert.That(pokeAsyncMethod, Is.Not.Null, "PokePrivateMembersAsync not found.");
            Func<SpikePrivateAccessFixture, int, Task<int>> pokeAsync =
                (Func<SpikePrivateAccessFixture, int, Task<int>>)pokeAsyncMethod.CreateDelegate(typeof(Func<SpikePrivateAccessFixture, int, Task<int>>));

            SpikePrivateAccessFixture fixture = new();
            // The async stub runs the first MoveNext synchronously, so the JIT failure surfaces
            // as a synchronous throw from the delegate call, not as a faulted task.
            Assert.Throws<FieldAccessException>(() => pokeAsync(fixture, 5));
        }

        /// <summary>
        /// What: Harmony-patching a private-poking snippet method itself throws
        /// FieldAccessException at patch time, because Harmony must JIT-compile the patch
        /// target to detour it — pinning why the shim is transplanted instead of patched.
        /// </summary>
        [Test]
        public async Task SelfPatchingSnippetMethod_ThrowsFieldAccessExceptionAtPatchTime()
        {
            Type snippetType = await CompileAndLoadSnippetAsync(
                "S1-selfpatch", SnippetBodySource, "SpikeS1Snippet", new List<string>());
            MethodInfo pokeMethod = snippetType.GetMethod("PokePrivateMembers", BindingFlags.Public | BindingFlags.Static);
            Assert.That(pokeMethod, Is.Not.Null, "PokePrivateMembers not found.");
            MethodInfo transpilerMethod = typeof(HotReloadSpikeS1PublicizedAccessTests).GetMethod(
                nameof(IdentityTranspiler), BindingFlags.Public | BindingFlags.Static);

            Harmony harmony = new(HarmonyId);
            Assert.Throws<FieldAccessException>(
                () => harmony.Patch(pokeMethod, transpiler: new HarmonyMethod(transpilerMethod)));
        }

        /// <summary>
        /// What: invoking the snippet directly fails at JIT time with FieldAccessException,
        /// pinning the runtime enforcement that shapes the whole access design on this Mono.
        /// </summary>
        [Test]
        public async Task Snippet_InvokedDirectly_ThrowsFieldAccessException()
        {
            Type snippetType = await CompileAndLoadSnippetAsync(
                "S1-enforce", SnippetBodySource, "SpikeS1Snippet", new List<string>());

            MethodInfo pokeMethod = snippetType.GetMethod("PokePrivateMembers", BindingFlags.Public | BindingFlags.Static);
            Assert.That(pokeMethod, Is.Not.Null, "PokePrivateMembers not found.");
            Func<SpikePrivateAccessFixture, int, int> poke =
                (Func<SpikePrivateAccessFixture, int, int>)pokeMethod.CreateDelegate(typeof(Func<SpikePrivateAccessFixture, int, int>));

            SpikePrivateAccessFixture fixture = new();
            Assert.Throws<FieldAccessException>(() => poke(fixture, 5));
        }

        /// <summary>
        /// What: this Mono does not honor IgnoresAccessChecksToAttribute — the snippet still
        /// fails with FieldAccessException. Pinned so a future Unity upgrade that starts
        /// honoring the attribute surfaces as a design-simplification opportunity.
        /// </summary>
        [Test]
        public async Task Snippet_WithIgnoresAccessChecksTo_StillThrowsFieldAccessException()
        {
            Type snippetType = await CompileAndLoadSnippetAsync(
                "S1-iact", IgnoresAccessChecksToPreamble + SnippetBodySource, "SpikeS1Snippet", new List<string>());

            MethodInfo pokeMethod = snippetType.GetMethod("PokePrivateMembers", BindingFlags.Public | BindingFlags.Static);
            Assert.That(pokeMethod, Is.Not.Null, "PokePrivateMembers not found.");
            Func<SpikePrivateAccessFixture, int, int> poke =
                (Func<SpikePrivateAccessFixture, int, int>)pokeMethod.CreateDelegate(typeof(Func<SpikePrivateAccessFixture, int, int>));

            SpikePrivateAccessFixture fixture = new();
            Assert.Throws<FieldAccessException>(() => poke(fixture, 5));
        }

        /// <summary>
        /// Identity transpiler: used by the patch-time pin to show that even a no-op patch of a
        /// private-poking method fails, because Harmony must JIT-compile the patch target.
        /// </summary>
        public static IEnumerable<CodeInstruction> IdentityTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            return instructions;
        }

        // Transpilers are resolved statically by Harmony, so the transplant source is handed
        // over through this field instead of a parameter.
        private static MethodInfo _transplantSourceMethod;

        /// <summary>
        /// Transpiler that discards the original instructions and emits the transplant source
        /// method's IL instead; Harmony compiles the result into the skip-visibility
        /// DynamicMethod that replaces the patched method.
        /// </summary>
        public static IEnumerable<CodeInstruction> ReplaceWithTransplantSourceTranspiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            return PatchProcessor.GetOriginalInstructions(_transplantSourceMethod, generator);
        }

        /// <summary>
        /// Plays the role of the hot reload infrastructure wiring accessor delegates into a
        /// freshly loaded shim assembly: binds the snippet's CounterRef field to a Harmony
        /// FieldRef for the fixture's private _counter field.
        /// </summary>
        private static void BindAccessorField(Type snippetType)
        {
            FieldInfo counterRefField = snippetType.GetField("CounterRef", BindingFlags.Public | BindingFlags.Static);
            Assert.That(counterRefField, Is.Not.Null, "CounterRef field not found in the accessor snippet.");
            counterRefField.SetValue(null, AccessTools.FieldRefAccess<SpikePrivateAccessFixture, int>("_counter"));
        }

        /// <summary>
        /// Compiles the given snippet source with the external Roslyn compiler against mscorlib
        /// and the publicized copy only, loads the produced assembly into the Editor domain, and
        /// returns the named snippet type.
        /// </summary>
        private static async Task<Type> CompileAndLoadSnippetAsync(
            string workSubdirectoryName,
            string snippetSource,
            string snippetTypeName,
            IReadOnlyList<string> extraReferencePaths)
        {
            string workRootPath = PrepareCleanDirectory(workSubdirectoryName);
            string publicizedDllPath = Path.Combine(workRootPath, TestAssemblyName + ".Publicized.dll");
            WritePublicizedCopy(ResolveTestAssemblyDllPath(), publicizedDllPath);

            string snippetSourcePath = Path.Combine(workRootPath, "SpikeS1Snippet.cs");
            File.WriteAllText(snippetSourcePath, snippetSource);
            string snippetDllPath = Path.Combine(workRootPath, "SpikeS1Snippet.dll");

            // Only mscorlib, the publicized copy, and explicitly requested extras: any leak of
            // the original assembly into the reference set would let the snippet compile
            // against private members legally and invalidate the spike result.
            List<string> references = new()
            {
                typeof(object).Assembly.Location,
                publicizedDllPath
            };
            references.AddRange(extraReferencePaths);

            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(externalCompilerPaths, Is.Not.Null, "External compiler paths could not be resolved for this Unity installation.");

            RoslynCompilerOptions compilerOptions = new(new List<string>(), false);
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(120));
            DynamicCompilationBackendResult result = await RoslynCompilerBackend.CompileAsync(
                snippetSourcePath,
                snippetDllPath,
                references,
                externalCompilerPaths,
                compilerOptions,
                cts.Token,
                () => { },
                () => { },
                () => { });

            Assert.That(
                result.BackendKind == DynamicCompilationBackendKind.SharedRoslynWorker
                || result.BackendKind == DynamicCompilationBackendKind.OneShotRoslyn,
                Is.True,
                $"Snippet must be compiled by the external Roslyn compiler, not a fallback that injects Unity's own reference set. Actual: {result.BackendKind}");
            AssertNoCompileErrors(result.CompilerMessages);
            Assert.That(File.Exists(snippetDllPath), Is.True, $"Snippet dll was not produced: {snippetDllPath}");

            Assembly snippetAssembly = Assembly.Load(File.ReadAllBytes(snippetDllPath));
            Type snippetType = snippetAssembly.GetType(snippetTypeName);
            Assert.That(snippetType, Is.Not.Null, $"{snippetTypeName} type not found in the loaded snippet assembly.");
            return snippetType;
        }

        private static string ResolveTestAssemblyDllPath()
        {
            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dllPath = Path.Combine(projectRootPath, "Library", "ScriptAssemblies", TestAssemblyName + ".dll");
            Assert.That(File.Exists(dllPath), Is.True, $"Test assembly dll not found: {dllPath}");
            return dllPath;
        }

        private static string PrepareCleanDirectory(string subdirectoryName)
        {
            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string workRootPath = Path.Combine(projectRootPath, "Library", "UloopHotReloadSpike", subdirectoryName);
            if (Directory.Exists(workRootPath))
            {
                Directory.Delete(workRootPath, true);
            }

            Directory.CreateDirectory(workRootPath);
            return workRootPath;
        }

        private static void WritePublicizedCopy(string sourceDllPath, string outputDllPath)
        {
            // InMemory read: the source dll is the currently loaded script assembly, so the copy
            // must not keep a file handle on it.
            ReaderParameters readerParameters = new() { InMemory = true };
            using AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(sourceDllPath, readerParameters);
            foreach (ModuleDefinition module in assemblyDefinition.Modules)
            {
                foreach (TypeDefinition type in module.GetTypes())
                {
                    // <Module> is a metadata artifact whose visibility must stay untouched.
                    if (type.Name == "<Module>")
                    {
                        continue;
                    }

                    PublicizeType(type);
                }
            }

            assemblyDefinition.Write(outputDllPath);
        }

        private static void PublicizeType(TypeDefinition type)
        {
            if (type.IsNested)
            {
                type.Attributes = (type.Attributes & ~CecilTypeAttributes.VisibilityMask) | CecilTypeAttributes.NestedPublic;
            }
            else
            {
                type.Attributes = (type.Attributes & ~CecilTypeAttributes.VisibilityMask) | CecilTypeAttributes.Public;
            }

            foreach (FieldDefinition field in type.Fields)
            {
                field.Attributes = (field.Attributes & ~CecilFieldAttributes.FieldAccessMask) | CecilFieldAttributes.Public;
            }

            foreach (MethodDefinition method in type.Methods)
            {
                method.Attributes = (method.Attributes & ~CecilMethodAttributes.MemberAccessMask) | CecilMethodAttributes.Public;
            }
        }

        private static FieldDefinition FindField(TypeDefinition type, string fieldName)
        {
            foreach (FieldDefinition field in type.Fields)
            {
                if (field.Name == fieldName)
                {
                    return field;
                }
            }

            Assert.Fail($"Field not found: {type.FullName}.{fieldName}");
            return null;
        }

        private static MethodDefinition FindMethod(TypeDefinition type, string methodName)
        {
            foreach (MethodDefinition method in type.Methods)
            {
                if (method.Name == methodName)
                {
                    return method;
                }
            }

            Assert.Fail($"Method not found: {type.FullName}.{methodName}");
            return null;
        }

        private static void AssertNoCompileErrors(CompilerMessage[] compilerMessages)
        {
            List<string> errors = new();
            foreach (CompilerMessage compilerMessage in compilerMessages)
            {
                if (compilerMessage.type == CompilerMessageType.Error)
                {
                    errors.Add(compilerMessage.message);
                }
            }

            Assert.That(errors, Is.Empty, "Snippet compilation failed:\n" + string.Join("\n", errors));
        }
    }
}
