using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using CecilFieldAttributes = Mono.Cecil.FieldAttributes;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies accessibility refusals through the worker preparation entry point.
    /// </summary>
    public sealed class TransformWorkerIntroducedTypeAccessibilityTests
    {
        private const string FixtureNamespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload";

        /// <summary>
        /// Verifies an inaccessible compiled base does not hide Unity object ancestry.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_InternalUnityObjectBase_IsRefused()
        {
            TransformWorkerInputDto input = CreateInput(
                "public class Introduced : HotReloadInternalMonoBehaviourBase { }");
            TransformWorkerFileOutputDto output = await PrepareAsync(input);

            Assert.That(output.introducedTypes, Is.Empty);
            Assert.That(HotReloadWorkerReasonTestText.RenderAll(output.introducedTypeDiagnostics),
                Has.Some.Contains("Unity object introduced type requires a compile"));
        }

        /// <summary>
        /// Verifies source-defined base chains still refuse Unity object descendants.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_SameBatchUnityObjectBase_IsRefused()
        {
            TransformWorkerFileOutputDto output = await PrepareAsync(CreateInput(
                "public class NewBase : UnityEngine.MonoBehaviour { } public class Introduced : NewBase { }"));

            Assert.That(output.introducedTypes, Is.Empty);
            Assert.That(output.introducedTypeDiagnostics, Has.Length.EqualTo(2));
            Assert.That(HotReloadWorkerReasonTestText.RenderAll(output.introducedTypeDiagnostics),
                Has.All.Contains("Unity object introduced type requires a compile"));
        }

        /// <summary>
        /// Verifies each assembly-constrained override is refused and names its owning member once.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_CompiledInternalOverride_IsRefused(
            [Values("internal", "protected internal", "private protected")] string access,
            [Values("method", "property", "indexer", "event")] string memberKind)
        {
            string baseName = "HotReload" + AccessPrefix(access) + "OverrideBase";
            TransformWorkerFileOutputDto output = await PrepareAsync(CreateInput(
                "public class Introduced : " + baseName + " { "
                + MemberDeclaration(memberKind, access + " override", true) + " }"));

            Assert.That(output.introducedTypes, Is.Empty);
            Assert.That(output.introducedTypeDiagnostics, Has.Length.EqualTo(1));
            string[] reasons = HotReloadWorkerReasonTestText.RenderAll(output.introducedTypeDiagnostics);
            Assert.That(reasons[0], Does.StartWith("Internal override in an introduced type requires a compile: "));
            Assert.That(output.introducedTypeDiagnostics[0].args[0], Is.EqualTo(FixtureNamespace + ".Introduced"));
            Assert.That(output.introducedTypeDiagnostics[0].args[1], Is.EqualTo(MemberName(memberKind)));
        }

        /// <summary>
        /// Verifies the same member kinds compile in one artifact and dispatch through the new base.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_SameBatchInternalOverride_Compiles(
            [Values("internal", "protected internal", "private protected")] string access,
            [Values("method", "property", "indexer", "event")] string memberKind)
        {
            TransformWorkerInputDto input = CreateInput(
                "public class NewBase { protected int Count; "
                + MemberDeclaration(memberKind, access + " virtual", false)
                + " public int Invoke() { " + Invocation(memberKind) + " } } "
                + "public class Introduced : NewBase { "
                + MemberDeclaration(memberKind, access + " override", true) + " }");
            TransformWorkerClientResult prepared = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                input, CancellationToken.None);
            Assert.That(prepared.Success, Is.True, prepared.ErrorMessage);
            Assert.That(prepared.Output.files[0].introducedTypeDiagnostics, Is.Empty);
            Assert.That(prepared.Output.files[0].introducedTypes, Has.Length.EqualTo(2));

            HotReloadIntroducedTypeCompilerResult compiled = await CompileAsync(input, prepared.Output.files);
            Assert.That(compiled.Success, Is.True, compiled.ErrorMessage);
            Type derived = compiled.Artifact.Assembly.GetType(FixtureNamespace + ".Introduced", true);
            Assert.That(derived.GetMethod("Invoke").Invoke(Activator.CreateInstance(derived), null), Is.EqualTo(7));
        }

        /// <summary>
        /// Verifies public and protected overrides remain legal across the compiled assembly boundary.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_PublicAndProtectedOverrides_RemainSupported()
        {
            TransformWorkerInputDto input = CreateInput(
                "public class Introduced : HotReloadVisibleOverrideBase { "
                + "public override int Read() => 7; protected override int Value => 9; }");
            TransformWorkerClientResult prepared = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                input, CancellationToken.None);
            Assert.That(prepared.Success, Is.True, prepared.ErrorMessage);
            Assert.That(prepared.Output.files[0].introducedTypeDiagnostics, Is.Empty);

            HotReloadIntroducedTypeCompilerResult compiled = await CompileAsync(input, prepared.Output.files);
            Assert.That(compiled.Success, Is.True, compiled.ErrorMessage);
            Type derived = compiled.Artifact.Assembly.GetType(FixtureNamespace + ".Introduced", true);
            HotReloadVisibleOverrideBase instance = (HotReloadVisibleOverrideBase)Activator.CreateInstance(derived);
            Assert.That(instance.Read(), Is.EqualTo(7));
            Assert.That(instance.InvokeValue(), Is.EqualTo(9));
        }

        /// <summary>
        /// Verifies a compiled internal module-initializer polyfill cannot bypass the existing refusal.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_InternalModuleInitializerAttribute_IsRefused()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("InternalModuleInitializer");
            string targetPath = Path.Combine(directory, "InitializerTarget.dll");
            string mvid = CreateModuleInitializerTarget(targetPath);
            string sourcePath = Path.Combine(directory, "Introduced.cs");
            File.WriteAllText(sourcePath,
                "public class Introduced { [System.Runtime.CompilerServices.ModuleInitializer] "
                + "public static void Initialize() { } }");
            TransformWorkerFileOutputDto output = await PrepareAsync(
                TransformWorkerIntroducedTypeTestInputs.CreatePreparationInput(
                    sourcePath, targetPath, "InitializerTarget", mvid, Array.Empty<string>(), Array.Empty<string>()));

            Assert.That(output.introducedTypes, Is.Empty);
            Assert.That(HotReloadWorkerReasonTestText.RenderAll(output.introducedTypeDiagnostics),
                Has.Some.Contains("Module initializer introduced type requires a compile"));
        }

        /// <summary>
        /// Verifies internal const drift is read from a changed sibling rather than folded from metadata.
        /// </summary>
        [TestCase(2, true)]
        [TestCase(1, false)]
        public async Task PrepareIntroducedTypes_ChangedInternalConstInAnotherFile_IsRejected(
            int sourceValue, bool expectedRefusal)
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("InternalConstDrift");
            string targetPath = Path.Combine(directory, "ConstDriftTarget.dll");
            string mvid = CreateInternalConstTarget(targetPath);
            string sourcePath = Path.Combine(directory, "Introduced.cs");
            string siblingPath = Path.Combine(directory, "Existing.cs");
            File.WriteAllText(sourcePath,
                "namespace Example { public class Introduced { public int Read() => Existing.Value; } }");
            File.WriteAllText(siblingPath,
                "namespace Example { internal class Existing { internal const int Value = "
                + sourceValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; } }");
            TransformWorkerFileOutputDto output = await PrepareAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateConstDriftInputWithSiblings(
                    sourcePath, targetPath, mvid, new[] { siblingPath }));

            Assert.That(output.introducedTypes.Length, Is.EqualTo(expectedRefusal ? 0 : 1));
            if (expectedRefusal)
            {
                Assert.That(HotReloadWorkerReasonTestText.RenderAll(output.introducedTypeDiagnostics),
                    Has.Some.Contains("Changed const requires a compile: Example.Existing.Value"));
            }
            else
            {
                Assert.That(output.introducedTypeDiagnostics, Is.Empty);
            }
        }

        private static TransformWorkerInputDto CreateInput(string declarations)
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory(
                "Accessibility-" + Guid.NewGuid().ToString("N"));
            string sourcePath = Path.Combine(directory, "Introduced.cs");
            string emptyPath = Path.Combine(directory, "Empty.cs");
            File.WriteAllText(sourcePath, "namespace " + FixtureNamespace + " { " + declarations + " }");
            File.WriteAllText(emptyPath, string.Empty);
            return TransformWorkerIntroducedTypeTestInputs.CreateInput(sourcePath, emptyPath);
        }

        private static async Task<TransformWorkerFileOutputDto> PrepareAsync(TransformWorkerInputDto input)
        {
            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                input, CancellationToken.None);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].parseErrors, Is.Empty);
            return result.Output.files[0];
        }

        private static Task<HotReloadIntroducedTypeCompilerResult> CompileAsync(
            TransformWorkerInputDto input, TransformWorkerFileOutputDto[] files)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadIntroducedTypeCompilationRequest request = HotReloadIntroducedTypeCompilationRequest.CreateBatch(
                new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "accessibility").Create(),
                TransformWorkerIntroducedTypeTestInputs.CreateDescriptors(files),
                input.referencePaths, input.defines);
            return new HotReloadIntroducedTypeCompiler(new HotReloadRoslynCompilerEnvironment())
                .CompileAsync(request, CancellationToken.None);
        }

        private static string AccessPrefix(string access)
        {
            return access switch
            {
                "internal" => "Internal",
                "protected internal" => "ProtectedInternal",
                "private protected" => "PrivateProtected",
                _ => throw new ArgumentOutOfRangeException(nameof(access))
            };
        }

        private static string MemberDeclaration(string kind, string modifiers, bool derived)
        {
            string value = derived ? "7" : "1";
            return modifiers + " " + (kind switch
            {
                "method" => "int Read() => " + value + ";",
                "property" => "int Value => " + value + ";",
                "indexer" => "int this[int index] => " + value + ";",
                "event" => "event System.Action Changed { add { "
                    + (derived ? "Count += 7;" : string.Empty) + " } remove { } }",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            });
        }

        private static string Invocation(string kind)
        {
            return kind switch
            {
                "method" => "return Read();",
                "property" => "return Value;",
                "indexer" => "return this[0];",
                "event" => "Changed += () => { }; return Count;",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        private static string MemberName(string kind)
        {
            return kind switch
            {
                "method" => "Read",
                "property" => "Value",
                "indexer" => "this[]",
                "event" => "Changed",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        private static string CreateModuleInitializerTarget(string path)
        {
            using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("InitializerTarget", new Version(1, 0, 0, 0)),
                "InitializerTarget", ModuleKind.Dll);
            TypeDefinition attribute = new TypeDefinition(
                "System.Runtime.CompilerServices", "ModuleInitializerAttribute",
                CecilTypeAttributes.NotPublic | CecilTypeAttributes.Sealed,
                assembly.MainModule.ImportReference(typeof(Attribute)));
            MethodDefinition constructor = new MethodDefinition(
                ".ctor", CecilMethodAttributes.Public | CecilMethodAttributes.SpecialName
                | CecilMethodAttributes.RTSpecialName, assembly.MainModule.TypeSystem.Void);
            constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            attribute.Methods.Add(constructor);
            assembly.MainModule.Types.Add(attribute);
            assembly.Write(path);
            return assembly.MainModule.Mvid.ToString();
        }

        private static string CreateInternalConstTarget(string path)
        {
            using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("ConstDriftTarget", new Version(1, 0, 0, 0)),
                "ConstDriftTarget", ModuleKind.Dll);
            TypeDefinition existing = new TypeDefinition(
                "Example", "Existing", CecilTypeAttributes.NotPublic,
                assembly.MainModule.TypeSystem.Object);
            existing.Fields.Add(new FieldDefinition("Value",
                CecilFieldAttributes.Assembly | CecilFieldAttributes.Static
                | CecilFieldAttributes.Literal | CecilFieldAttributes.HasDefault,
                assembly.MainModule.TypeSystem.Int32) { Constant = 1 });
            assembly.MainModule.Types.Add(existing);
            assembly.Write(path);
            return assembly.MainModule.Mvid.ToString();
        }
    }
}
