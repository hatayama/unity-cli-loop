using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using Assembly = System.Reflection.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike
{
    /// <summary>
    /// Spike S4 for hot reload: checks the first premise of the plan that keeps an introduced
    /// type's identity while treating it as a compiled type. An introduced type lives in an
    /// artifact assembly the domain loaded from bytes, so nothing about it is on the script
    /// assembly path. These tests build such an assembly the way production does
    /// (external Roslyn to dll + pdb, then Assembly.Load(dllBytes, pdbBytes)), describe it with
    /// a RetainedArtifact type home, and then drive the existing resolution and patching path:
    /// HotReloadMethodMatcher resolves a method through the Cecil metadata token and the Mvid
    /// guard, HotReloadPatcher.CheckPatchable accepts it, a Harmony transpiler replaces its
    /// body, and UnpatchAll restores it. Private methods take the same route.
    /// </summary>
    public class HotReloadSpikeS4ArtifactPatchTests
    {
        // Test-scoped id: sharing the production hot reload id would let this suite's
        // UnpatchAll remove real hot reload patches from the Editor domain.
        private const string HarmonyId = "io.github.hatayama.uloop.hot-reload-spike-s4";

        private const string IntroducedTypeMetadataName = "SpikeS4.Introduced";

        // Why NoInlining inside the snippet: these tests measure detour mechanics, and the
        // Mono JIT can inline these tiny bodies into the caller before the patch is applied.
        private const string IntroducedTypeSource = @"using System.Runtime.CompilerServices;

namespace SpikeS4
{
    public class Introduced
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute()
        {
            return 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private int Secret()
        {
            return 5;
        }

        public int CallSecret()
        {
            return Secret();
        }
    }
}
";

        [TearDown]
        public void TearDown()
        {
            new Harmony(HarmonyId).UnpatchAll(HarmonyId);
        }

        /// <summary>What: the existing matcher resolves a public method of an assembly loaded
        /// from bytes when its home is a retained artifact, and the patcher accepts it.</summary>
        [Test]
        public async Task MethodMatcher_ResolvesMethodInsideRetainedArtifactHome()
        {
            LoadedArtifact artifact = await CompileAndLoadArtifactAsync("SpikeS4Artifact_Matcher");

            HotReloadMethodMatchResult matchResult = HotReloadMethodMatcher.Resolve(
                artifact.Home,
                IntroducedTypeMetadataName,
                "Compute",
                Array.Empty<string>(),
                0);

            Assert.That(
                matchResult.Success,
                Is.True,
                $"Resolve failed: {matchResult.FailureReason} {matchResult.ErrorMessage}");
            Assert.That(matchResult.Method.DeclaringType.Assembly, Is.SameAs(artifact.Assembly));
            HotReloadPatchResult patchable = HotReloadPatcher.CheckPatchable(matchResult.Method);
            Assert.That(
                patchable.Success,
                Is.True,
                $"CheckPatchable rejected the artifact method: {patchable.FailureReason} {patchable.ErrorMessage}");
        }

        /// <summary>What: a Harmony transpiler replaces the body of a method that lives in a
        /// retained artifact assembly.</summary>
        [Test]
        public async Task Transpiler_ReplacesBodyOfRetainedArtifactMethod()
        {
            LoadedArtifact artifact = await CompileAndLoadArtifactAsync("SpikeS4Artifact_Patch");
            MethodBase computeMethod = ResolveOrFail(artifact, "Compute");
            object instance = Activator.CreateInstance(artifact.IntroducedType);

            Assert.That(computeMethod.Invoke(instance, Array.Empty<object>()), Is.EqualTo(1));

            PatchWithReturnTwo(computeMethod);

            Assert.That(computeMethod.Invoke(instance, Array.Empty<object>()), Is.EqualTo(2));
        }

        /// <summary>What: unpatching restores the original body of a retained artifact method.</summary>
        [Test]
        public async Task UnpatchAll_RestoresRetainedArtifactMethod()
        {
            LoadedArtifact artifact = await CompileAndLoadArtifactAsync("SpikeS4Artifact_Unpatch");
            MethodBase computeMethod = ResolveOrFail(artifact, "Compute");
            object instance = Activator.CreateInstance(artifact.IntroducedType);
            PatchWithReturnTwo(computeMethod);
            Assert.That(computeMethod.Invoke(instance, Array.Empty<object>()), Is.EqualTo(2));

            new Harmony(HarmonyId).UnpatchAll(HarmonyId);

            Assert.That(computeMethod.Invoke(instance, Array.Empty<object>()), Is.EqualTo(1));
        }

        /// <summary>What: a private method of a retained artifact assembly resolves and detours
        /// through the same path, so its caller observes the replaced body.</summary>
        [Test]
        public async Task MethodMatcher_ResolvesPrivateMethodInsideRetainedArtifactHome()
        {
            LoadedArtifact artifact = await CompileAndLoadArtifactAsync("SpikeS4Artifact_Private");
            MethodBase secretMethod = ResolveOrFail(artifact, "Secret");
            MethodInfo callSecretMethod = artifact.IntroducedType.GetMethod(
                "CallSecret",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(callSecretMethod, Is.Not.Null, "CallSecret was not found on the artifact type.");
            object instance = Activator.CreateInstance(artifact.IntroducedType);
            Assert.That(callSecretMethod.Invoke(instance, Array.Empty<object>()), Is.EqualTo(5));

            PatchWithReturnTwo(secretMethod);

            Assert.That(callSecretMethod.Invoke(instance, Array.Empty<object>()), Is.EqualTo(2));
        }

        /// <summary>
        /// Transpiler that discards the original instructions and returns the constant 2, so a
        /// changed return value proves the detour ran rather than the original body.
        /// </summary>
        public static IEnumerable<CodeInstruction> ReturnTwoTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            return new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldc_I4_2),
                new CodeInstruction(OpCodes.Ret)
            };
        }

        private static void PatchWithReturnTwo(MethodBase method)
        {
            MethodInfo transpiler = typeof(HotReloadSpikeS4ArtifactPatchTests).GetMethod(
                nameof(ReturnTwoTranspiler),
                BindingFlags.Public | BindingFlags.Static);
            new Harmony(HarmonyId).Patch(method, transpiler: new HarmonyMethod(transpiler));
        }

        private static MethodBase ResolveOrFail(LoadedArtifact artifact, string methodName)
        {
            HotReloadMethodMatchResult matchResult = HotReloadMethodMatcher.Resolve(
                artifact.Home,
                IntroducedTypeMetadataName,
                methodName,
                Array.Empty<string>(),
                0);
            Assert.That(
                matchResult.Success,
                Is.True,
                $"Resolve of '{methodName}' failed: {matchResult.FailureReason} {matchResult.ErrorMessage}");
            return matchResult.Method;
        }

        /// <summary>
        /// Compiles the introduced-type snippet with the external Roslyn compiler into a dll and
        /// a pdb, loads it exactly the way the production loader does (two-argument
        /// Assembly.Load, so carrying a pdb is part of what this spike checks), and describes it
        /// with a retained-artifact home. The assembly name matches the work directory name so
        /// each test owns a distinct assembly in this Editor session.
        /// </summary>
        private static async Task<LoadedArtifact> CompileAndLoadArtifactAsync(string workSubdirectoryName)
        {
            string workRootPath = PrepareCleanDirectory(workSubdirectoryName);
            string sourcePath = Path.Combine(workRootPath, "Introduced.cs");
            File.WriteAllText(sourcePath, IntroducedTypeSource);
            string dllPath = Path.Combine(workRootPath, workSubdirectoryName + ".dll");
            string pdbPath = Path.ChangeExtension(dllPath, ".pdb");

            List<string> references = new() { typeof(object).Assembly.Location };
            string methodImplAssemblyLocation = typeof(MethodImplAttribute).Assembly.Location;
            if (!references.Contains(methodImplAssemblyLocation))
            {
                references.Add(methodImplAssemblyLocation);
            }

            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(
                externalCompilerPaths,
                Is.Not.Null,
                "External compiler paths could not be resolved for this Unity installation.");

            RoslynCompilerOptions compilerOptions = new(new List<string>(), false, emitDebugCode: true);
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(120));
            DynamicCompilationBackendResult result = await RoslynCompilerBackend.CompileAsync(
                sourcePath,
                dllPath,
                references,
                externalCompilerPaths,
                compilerOptions,
                cts.Token,
                () => { },
                () => { },
                () => { });

            AssertNoCompileErrors(result.CompilerMessages);
            Assert.That(File.Exists(dllPath), Is.True, $"Artifact dll was not produced: {dllPath}");
            Assert.That(File.Exists(pdbPath), Is.True, $"Artifact pdb was not produced: {pdbPath}");

            Assembly artifactAssembly = Assembly.Load(File.ReadAllBytes(dllPath), File.ReadAllBytes(pdbPath));
            Type introducedType = artifactAssembly.GetType(IntroducedTypeMetadataName);
            Assert.That(introducedType, Is.Not.Null, "The introduced type was not found in the artifact assembly.");
            HotReloadTypeHome home = HotReloadTypeHome.RetainedArtifact(
                workSubdirectoryName,
                dllPath,
                artifactAssembly);
            return new LoadedArtifact(artifactAssembly, introducedType, home);
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

            Assert.That(errors, Is.Empty, "Artifact compilation failed:\n" + string.Join("\n", errors));
        }

        /// <summary>
        /// One compiled and loaded artifact assembly together with the home that names it, so a
        /// test can resolve methods and construct instances from the same load.
        /// </summary>
        private sealed class LoadedArtifact
        {
            public Assembly Assembly { get; }
            public Type IntroducedType { get; }
            public HotReloadTypeHome Home { get; }

            public LoadedArtifact(Assembly assembly, Type introducedType, HotReloadTypeHome home)
            {
                Assembly = assembly;
                IntroducedType = introducedType;
                Home = home;
            }
        }
    }
}
