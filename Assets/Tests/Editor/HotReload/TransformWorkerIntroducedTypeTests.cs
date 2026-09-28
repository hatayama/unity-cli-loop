using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using Mono.Cecil;
using CecilFieldAttributes = Mono.Cecil.FieldAttributes;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies the dedicated worker operation that plans immutable introduced type artifacts.
    /// </summary>
    public class TransformWorkerIntroducedTypeTests
    {
        // A second file the fingerprint cases never edit, so only the first file introduces a type.
        private const string UnrelatedSecondSource = "namespace Unrelated { public class Untouched { } }";

        // Reopens a type the target assembly already holds, so the nested declaration is the only
        // thing this file introduces and the outer declaration is never refused.
        private const string CompiledOuterWithNestedSource =
            "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload { internal sealed "
            + "class HotReloadCrossFileAddedMemberHolder { public class NestedProbe { } } }";

        /// <summary>
        /// Verifies that preparation preserves input file order, emits supported top-level types,
        /// and reports unsupported declarations on their owning file.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_EmitsSupportedTypesAndPerFileDiagnostics()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, "Library", "UloopHotReload", "TestSources", "IntroducedTypes");
            Directory.CreateDirectory(directory);
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "using Alias = System.IDisposable; namespace Example.Introduced { public class NewClass { public Alias Create() { return null; } } public struct NewStruct { } public enum NewEnum { One } public interface INew { } public static class Helpers { } }");
            File.WriteAllText(
                secondSourcePath,
                "namespace Example.Introduced { internal class Hidden { } public class Generic<T> { } internal class Outer { public class Nested { } } }");

            TransformWorkerInputDto input = TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath);
            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                input,
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files, Has.Length.EqualTo(2));
            Assert.That(result.Output.files[0].projectRelativePath, Is.EqualTo("Assets/First.cs"));
            Assert.That(result.Output.files[1].projectRelativePath, Is.EqualTo("Assets/Second.cs"));
            Assert.That(result.Output.files[0].introducedTypes, Has.Length.EqualTo(5));
            Assert.That(
                result.Output.files[0].introducedTypes[0].source,
                Does.Contain("namespace Example.Introduced"));
            Assert.That(
                result.Output.files[0].introducedTypes[0].source,
                Does.Contain("using Alias"));
            Assert.That(result.Output.files[1].introducedTypes, Has.Length.EqualTo(1));
            Assert.That(result.Output.files[1].introducedTypes[0].metadataName, Is.EqualTo("Example.Introduced.Hidden"));
            Assert.That(
                result.Output.files[1].introducedTypes[0].source,
                Does.Contain(Environment.NewLine + "public class Hidden { } "));
            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(result.Output.files[1].introducedTypeDiagnostics),
                Is.EquivalentTo(new[]
                {
                    "Generic introduced type requires a compile: Example.Introduced.Generic`1",
                    "Nested declaration inside an introduced type requires a compile: Example.Introduced.Outer/Nested"
                }));
        }

        /// <summary>
        /// Verifies that a nested declaration inside a type this run introduces is reported once,
        /// by the outer declaration that is refused because of it, rather than a second time on
        /// its own account.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_NestedInsideAnIntroducedType_ReportsOnlyTheOuterRefusal()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("NestedDiagnostics");
            string introducedOuterPath = Path.Combine(directory, "IntroducedOuter.cs");
            string compiledOuterPath = Path.Combine(directory, "CompiledOuter.cs");
            File.WriteAllText(
                introducedOuterPath,
                "namespace Example { public class OuterIntroduced { public class NestedProbe { } } }");
            File.WriteAllText(compiledOuterPath, CompiledOuterWithNestedSource);

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(introducedOuterPath, compiledOuterPath),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(result.Output.files[0].introducedTypeDiagnostics),
                Is.EqualTo(
                    new[]
                    {
                        "Nested declaration inside an introduced type requires a compile: "
                        + "Example.OuterIntroduced/NestedProbe"
                    }));
        }

        /// <summary>
        /// Verifies that a nested declaration added to a type the target assembly already holds
        /// is still reported on its own, because no outer refusal covers it.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_NestedInsideACompiledType_ReportsTheNestedRefusal()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("NestedDiagnostics");
            string introducedOuterPath = Path.Combine(directory, "IntroducedOuter.cs");
            string compiledOuterPath = Path.Combine(directory, "CompiledOuter.cs");
            File.WriteAllText(
                introducedOuterPath,
                "namespace Example { public class OuterIntroduced { public class NestedProbe { } } }");
            File.WriteAllText(compiledOuterPath, CompiledOuterWithNestedSource);

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(introducedOuterPath, compiledOuterPath),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(result.Output.files[1].introducedTypeDiagnostics),
                Is.EqualTo(
                    new[]
                    {
                        "Nested type requires a compile: "
                        + "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload."
                        + "HotReloadCrossFileAddedMemberHolder/NestedProbe"
                    }));
        }

        /// <summary>
        /// Verifies that a parse-failed row remains isolated from the shared semantic compilation
        /// while a sibling file still emits its independently valid introduced type.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ParseFailedSibling_DoesNotEnterSharedAnalysis()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("ParseIsolation");
            string validSourcePath = Path.Combine(directory, "Valid.cs");
            string invalidSourcePath = Path.Combine(directory, "Invalid.cs");
            File.WriteAllText(validSourcePath, "namespace Example { public class ValidIntroduced { } }");
            File.WriteAllText(invalidSourcePath, "namespace Example { public class BrokenIntroduced { ");

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(validSourcePath, invalidSourcePath),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Has.Length.EqualTo(1));
            Assert.That(result.Output.files[1].introducedTypes, Is.Empty);
            Assert.That(result.Output.files[1].parseErrors, Is.Not.Empty);
        }

        /// <summary>
        /// Verifies that nested namespace alias scopes are retained in emitted source instead of
        /// being flattened into duplicate aliases at one namespace level.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_NestedAliasScopes_PreservesNamespaceHierarchy()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("NamespaceAliases");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "namespace Outer { using Alias = System.String; namespace Inner { using Alias = System.Int32; public class Aliased { public Alias Value; } } }");
            File.WriteAllText(secondSourcePath, "namespace Example { public class Other { } }");

            TransformWorkerInputDto input = TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath);
            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                input,
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            string source = result.Output.files[0].introducedTypes[0].source;
            Assert.That(source, Does.Contain("namespace Outer"));
            Assert.That(source, Does.Contain("namespace Inner"));
            Assert.That(source, Does.Contain("using Alias = System.String"));
            Assert.That(source, Does.Contain("using Alias = System.Int32"));
        }

        /// <summary>
        /// Verifies that two global-namespace declarations retain their independent alias
        /// bindings through worker preparation, source composition, and Roslyn compilation.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_GlobalNamespaceSources_CompileWithoutChangingAliasBindings()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("GlobalNamespaceSources");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "using System; using Alias = System.IDisposable; public class GlobalFirst { public Alias Create() { return null; } }");
            File.WriteAllText(
                secondSourcePath,
                "using System; using Alias = System.ICloneable; public class GlobalSecond { public Alias Create() { return null; } }");

            TransformWorkerInputDto input = TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath);
            TransformWorkerClientResult workerResult = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                input,
                CancellationToken.None);

            Assert.That(workerResult.Success, Is.True, workerResult.ErrorMessage);
            Assert.That(workerResult.Output.files[0].introducedTypes, Has.Length.EqualTo(1));
            Assert.That(workerResult.Output.files[1].introducedTypes, Has.Length.EqualTo(1));
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                TransformWorkerIntroducedTypeTestInputs.CreateDescriptors(workerResult.Output.files);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadIntroducedTypeArtifactPathFactory factory =
                new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "global-namespace-sources");
            HotReloadIntroducedTypeCompilationRequest request =
                HotReloadIntroducedTypeCompilationRequest.CreateBatch(
                    factory.Create(),
                    descriptors,
                    input.referencePaths,
                    input.defines);
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(
                new HotReloadRoslynCompilerEnvironment(),
                new FakeInternalAccessGrant(isAvailable: false));

            HotReloadIntroducedTypeCompilerResult compileResult = await compiler.CompileAsync(
                request,
                CancellationToken.None);

            Assert.That(compileResult.Success, Is.True, compileResult.ErrorMessage);
            Assert.That(compileResult.Artifact.Assembly.GetType("GlobalFirst"), Is.Not.Null);
            Assert.That(compileResult.Artifact.Assembly.GetType("GlobalSecond"), Is.Not.Null);
        }

        /// <summary>
        /// Verifies that one global alias remains valid when worker preparation copies it into
        /// each independently compiled introduced-type source tree.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_GlobalUsingAlias_CompilesTwoIntroducedTypes()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("GlobalUsingAlias");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "global using Alias = System.IDisposable; namespace GlobalAliasFixture { public class First { public Alias Create() { return null; } } }");
            File.WriteAllText(
                secondSourcePath,
                "namespace GlobalAliasFixture { public class Second { public Alias Create() { return null; } } }");

            TransformWorkerInputDto input = TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath);
            TransformWorkerClientResult workerResult = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                input,
                CancellationToken.None);

            List<HotReloadIntroducedTypeDescriptor> descriptors = TransformWorkerIntroducedTypeTestInputs.CreateDescriptors(workerResult.Output.files);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadIntroducedTypeCompilationRequest request =
                HotReloadIntroducedTypeCompilationRequest.CreateBatch(
                    new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "global-using-alias").Create(),
                    descriptors,
                    input.referencePaths,
                    input.defines);
            HotReloadIntroducedTypeCompilerResult compileResult = await new HotReloadIntroducedTypeCompiler(
                new HotReloadRoslynCompilerEnvironment(),
                new FakeInternalAccessGrant(isAvailable: false)).CompileAsync(request, CancellationToken.None);

            Assert.That(workerResult.Success, Is.True, workerResult.ErrorMessage);
            Assert.That(descriptors, Has.Count.EqualTo(2));
            Assert.That(compileResult.Success, Is.True, compileResult.ErrorMessage);
            Assert.That(compileResult.Artifact.Assembly.GetType("GlobalAliasFixture.First"), Is.Not.Null);
            Assert.That(compileResult.Artifact.Assembly.GetType("GlobalAliasFixture.Second"), Is.Not.Null);

            HotReloadIntroducedTypeCompilationRequest subsetRequest =
                HotReloadIntroducedTypeCompilationRequest.CreateBatch(
                    new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "global-using-alias-subset").Create(),
                    new[] { descriptors[1] },
                    input.referencePaths,
                    input.defines);
            HotReloadIntroducedTypeCompilerResult subsetCompileResult = await new HotReloadIntroducedTypeCompiler(
                new HotReloadRoslynCompilerEnvironment(),
                new FakeInternalAccessGrant(isAvailable: false)).CompileAsync(subsetRequest, CancellationToken.None);

            Assert.That(subsetCompileResult.Success, Is.True, subsetCompileResult.ErrorMessage);
            Assert.That(subsetCompileResult.Artifact.Assembly.GetType("GlobalAliasFixture.Second"), Is.Not.Null);
        }

        /// <summary>
        /// Verifies that root imports remain in compilation-unit scope when a namespace contains
        /// a relative namespace with the same name as the imported global namespace.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_RootUsing_KeepsGlobalNamespaceBinding()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("RootUsingScope");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "using ScopeGlobal; using static ScopeStatic.Helpers; using ExtensionGlobal; namespace ScopeGlobal { public class Bound { } } namespace ScopeFixture.ScopeGlobal { public class Bound { } } namespace ScopeStatic { public static class Helpers { public static int Value() { return 7; } } } namespace ScopeFixture.ScopeStatic { public static class Helpers { public static int Value() { return 9; } } } namespace ExtensionGlobal { public static class Extensions { public static int ExtensionValue(this string value) { return 17; } } } namespace ScopeFixture.ExtensionGlobal { public static class Extensions { public static int ExtensionValue(this string value) { return 19; } } } namespace ScopeFixture { public class Consumer { public Bound Create() { return null; } public int GetValue() { return Value(); } public int GetExtensionValue() { return \"test\".ExtensionValue(); } } }");
            File.WriteAllText(secondSourcePath, "namespace ScopeFixture { public class Other { } }");

            TransformWorkerInputDto input = TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath);
            TransformWorkerClientResult workerResult = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                input,
                CancellationToken.None);

            List<HotReloadIntroducedTypeDescriptor> descriptors = TransformWorkerIntroducedTypeTestInputs.CreateDescriptors(workerResult.Output.files);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadIntroducedTypeCompilationRequest request =
                HotReloadIntroducedTypeCompilationRequest.CreateBatch(
                    new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "root-using-scope").Create(),
                    descriptors,
                    input.referencePaths,
                    input.defines);
            HotReloadIntroducedTypeCompilerResult compileResult = await new HotReloadIntroducedTypeCompiler(
                new HotReloadRoslynCompilerEnvironment(),
                new FakeInternalAccessGrant(isAvailable: false)).CompileAsync(request, CancellationToken.None);

            Assert.That(workerResult.Success, Is.True, workerResult.ErrorMessage);
            Assert.That(compileResult.Success, Is.True, compileResult.ErrorMessage);
            Type consumer = compileResult.Artifact.Assembly.GetType("ScopeFixture.Consumer");
            Assert.That(consumer.GetMethod("Create").ReturnType.FullName, Is.EqualTo("ScopeGlobal.Bound"));
            Assert.That(consumer.GetMethod("GetValue").Invoke(Activator.CreateInstance(consumer), null), Is.EqualTo(7));
            Assert.That(consumer.GetMethod("GetExtensionValue").Invoke(Activator.CreateInstance(consumer), null), Is.EqualTo(17));
        }

        /// <summary>
        /// Verifies that every unsupported declaration category produces an owning-file
        /// diagnostic and cannot become an introduced artifact descriptor, while the internal
        /// declarations beside them are introduced.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_UnsupportedSemanticCategories_ReportDiagnostics()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("UnsupportedCategories");
            string firstSourcePath = Path.Combine(directory, "Unsupported.cs");
            string secondSourcePath = Path.Combine(directory, "Other.cs");
            File.WriteAllText(
                firstSourcePath,
                "namespace System.Runtime.CompilerServices { internal sealed class ModuleInitializerAttribute : System.Attribute { } } "
                + "namespace Example { internal class Hidden { } public class Generic<T> { } public partial class Partial { } public ref struct RefLike { } public unsafe class UnsafeType { public int* Value; } public class ObjectType : UnityEngine.Object { } [System.Serializable] public class SerializableType { } public static class InitializerType { [System.Runtime.CompilerServices.ModuleInitializer] public static void Initialize() { } } public delegate void AddedDelegate(); public class Outer { public class Nested { } } }");
            File.WriteAllText(secondSourcePath, "namespace Example { public class Other { } }");

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                IntroducedMetadataNames(result.Output.files[0]),
                Is.EquivalentTo(new[] { "System.Runtime.CompilerServices.ModuleInitializerAttribute", "Example.Hidden" }));
            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(result.Output.files[0].introducedTypeDiagnostics),
                Is.EquivalentTo(new[]
                {
                    "Generic introduced type requires a compile: Example.Generic`1",
                    "Partial introduced type requires a compile: Example.Partial",
                    "Ref-like introduced type requires a compile: Example.RefLike",
                    "Unsafe introduced type requires a compile: Example.UnsafeType",
                    "Unity object introduced type requires a compile: Example.ObjectType",
                    "Serializable introduced type requires a compile: Example.SerializableType",
                    "Module initializer introduced type requires a compile: Example.InitializerType",
                    "Nested declaration inside an introduced type requires a compile: Example.Outer/Nested",
                    "Delegate introduced type requires a compile: Example.AddedDelegate"
                }));
        }

        /// <summary>
        /// Verifies that an outer declaration containing an excluded nested declaration is
        /// rejected so the generated artifact cannot retain the nested implementation.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_OuterContainingNestedDeclaration_IsRejectedBeforeArtifactCompile()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("NestedArtifactRejection");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "namespace System.Runtime.CompilerServices { internal sealed class ModuleInitializerAttribute : System.Attribute { } } "
                + "namespace Example { public class Outer { public static class Nested { [System.Runtime.CompilerServices.ModuleInitializer] public static void Initialize() { } } } public class Safe { } }");
            File.WriteAllText(secondSourcePath, "namespace Example { public class Other { } }");

            TransformWorkerInputDto input = TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath);
            TransformWorkerClientResult workerResult = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                input,
                CancellationToken.None);

            // The rejection has to be observable in the worker output before any artifact is
            // compiled, otherwise a later assertion about the assembly could pass for the wrong
            // reason - because compilation happened to drop the type rather than because the
            // outer declaration was refused.
            Assert.That(workerResult.Success, Is.True, workerResult.ErrorMessage);
            Assert.That(
                IntroducedMetadataNames(workerResult.Output.files[0]),
                Is.EquivalentTo(new[] { "System.Runtime.CompilerServices.ModuleInitializerAttribute", "Example.Safe" }));
            Assert.That(HotReloadWorkerReasonTestText.RenderAll(workerResult.Output.files[0].introducedTypeDiagnostics), Has.Some.Contains("Nested"));

            List<HotReloadIntroducedTypeDescriptor> descriptors = TransformWorkerIntroducedTypeTestInputs.CreateDescriptors(workerResult.Output.files);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadIntroducedTypeCompilationRequest request =
                HotReloadIntroducedTypeCompilationRequest.CreateBatch(
                    new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "nested-artifact-rejection").Create(),
                    descriptors,
                    input.referencePaths,
                    input.defines);
            HotReloadIntroducedTypeCompilerResult compileResult = await new HotReloadIntroducedTypeCompiler(
                new HotReloadRoslynCompilerEnvironment(),
                new FakeInternalAccessGrant(isAvailable: false)).CompileAsync(request, CancellationToken.None);

            Assert.That(compileResult.Success, Is.True, compileResult.ErrorMessage);
            Assert.That(compileResult.Artifact.Assembly.GetType("Example.Safe"), Is.Not.Null);
            Assert.That(compileResult.Artifact.Assembly.GetType("Example.Outer"), Is.Null);
        }

        /// <summary>
        /// Verifies that fingerprints ignore trivia and unrelated namespace imports while they
        /// retain token boundaries, defines, and semantically referenced alias identities.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_Fingerprint_TracksOnlyDefinitionInputs()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("Fingerprints");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "using Alias = System.IDisposable; using System.Text; namespace Example { public class Fingerprint { public Alias Create() { return null; } } } namespace Unrelated { using Other = System.Text; class Ignore { } }");
            File.WriteAllText(secondSourcePath, "namespace Unrelated { using Other = System.String; public class OtherType { } }");
            TransformWorkerClientResult first = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            File.WriteAllText(
                firstSourcePath,
                "using Alias = System.IDisposable; using System.Text; namespace Example { public class Fingerprint { public Alias Create() { return null; } } public class LaterIntroduced { } } namespace Unrelated { using Other = System.Text; class Ignore { } }");
            TransformWorkerClientResult laterType = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            File.WriteAllText(
                firstSourcePath,
                "using Alias = System.IDisposable; using System.Text; namespace Example { public class Fingerprint { public Alias Create() { return null; } } } namespace Unrelated { using Other = System.IO; class Ignore { } }");
            TransformWorkerClientResult unrelatedUsing = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            File.WriteAllText(
                firstSourcePath,
                "using Alias = System.IDisposable; using System.Text; namespace Example { /* trivia */ public class Fingerprint { public Alias Create() { return null; } } } namespace Unrelated { using Other = System.IO; class Ignore { } }");
            TransformWorkerClientResult trivia = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            File.WriteAllText(
                firstSourcePath,
                "using Alias = System.ICloneable; using System.Text; namespace Example { public class Fingerprint { public Alias Create() { return null; } } }");
            TransformWorkerClientResult aliasChanged = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            File.WriteAllText(
                firstSourcePath,
                "using Alias = System.IDisposable; using System.Text; namespace Example { public class Fingerprint { public Alias Create() { return null; } } }");
            TransformWorkerInputDto definesChangedInput = TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath);
            definesChangedInput.defines = new[] { "CHANGED_DEFINE" };
            TransformWorkerClientResult definesChanged = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                definesChangedInput,
                CancellationToken.None);

            Assert.That(first.Success, Is.True, first.ErrorMessage);
            Assert.That(laterType.Success, Is.True, laterType.ErrorMessage);
            Assert.That(unrelatedUsing.Success, Is.True, unrelatedUsing.ErrorMessage);
            Assert.That(trivia.Success, Is.True, trivia.ErrorMessage);
            Assert.That(aliasChanged.Success, Is.True, aliasChanged.ErrorMessage);
            Assert.That(definesChanged.Success, Is.True, definesChanged.ErrorMessage);
            string baseline = FingerprintOf(first, "Example.Fingerprint");
            Assert.That(FingerprintOf(trivia, "Example.Fingerprint"), Is.EqualTo(baseline));
            Assert.That(FingerprintOf(unrelatedUsing, "Example.Fingerprint"), Is.EqualTo(baseline));
            Assert.That(
                IntroducedMetadataNames(laterType.Output.files[0]),
                Is.EquivalentTo(new[] { "Example.Fingerprint", "Example.LaterIntroduced", "Unrelated.Ignore" }));
            Assert.That(FingerprintOf(laterType, "Example.Fingerprint"), Is.EqualTo(baseline));
            Assert.That(FingerprintOf(aliasChanged, "Example.Fingerprint"), Is.Not.EqualTo(baseline));
            Assert.That(FingerprintOf(definesChanged, "Example.Fingerprint"), Is.Not.EqualTo(baseline));
        }

        private static string[] IntroducedMetadataNames(TransformWorkerFileOutputDto file)
        {
            return file.introducedTypes.Select(introduced => introduced.metadataName).ToArray();
        }

        private static string FingerprintOf(TransformWorkerClientResult result, string metadataName)
        {
            TransformWorkerIntroducedTypeDto introduced = result.Output.files[0].introducedTypes
                .FirstOrDefault(candidate => candidate.metadataName == metadataName);
            Assert.That(introduced, Is.Not.Null, metadataName + " must be introduced.");
            return introduced.declarationFingerprint;
        }

        /// <summary>
        /// Verifies that the fingerprint of a declaration that uses a compiled type is the same
        /// whether that type is bound from the target assembly or from its source in the same run.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_CompiledTypeBoundFromSource_KeepsTheFingerprint()
        {
            // Why the caller is a parameter and not a `new Caller()`: the target assembly is built
            // without a constructor, so a construction would fail to bind against metadata and the
            // fingerprint would differ for that reason instead of the identity under test.
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture
                .CreateWithSiblingSourceAsync(
                    "CompiledTypeFromSource",
                    "namespace Example { public class Retained { public int Compute(Caller caller) { return caller.Read(); } } }",
                    "namespace Example { public class Caller { public int Read() { return 0; } } }");

            TransformWorkerClientResult alone = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                fixture.BuildPrepareInput(),
                CancellationToken.None);
            TransformWorkerClientResult grouped = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                fixture.BuildPrepareGroupInput(),
                CancellationToken.None);

            Assert.That(alone.Success, Is.True, alone.ErrorMessage);
            Assert.That(grouped.Success, Is.True, grouped.ErrorMessage);
            Assert.That(
                FindRetainedFingerprint(grouped, fixture.ProjectRelativePath),
                Is.EqualTo(FindRetainedFingerprint(alone, fixture.ProjectRelativePath)),
                "A compiled type bound from source must fingerprint as the same dependency.");
        }

        private static string FindRetainedFingerprint(
            TransformWorkerClientResult result,
            string projectRelativePath)
        {
            foreach (TransformWorkerFileOutputDto file in result.Output.files)
            {
                if (file.projectRelativePath != projectRelativePath)
                {
                    continue;
                }

                foreach (TransformWorkerIntroducedTypeDto introducedType in file.introducedTypes)
                {
                    if (introducedType.metadataName == "Example.Retained")
                    {
                        return introducedType.declarationFingerprint;
                    }
                }
            }

            Assert.Fail("Planning did not report Example.Retained for " + projectRelativePath + ".");
            return null;
        }

        /// <summary>
        /// Verifies that a fingerprint retains each semantic dependency at its stable declaration
        /// traversal position when aliases exchange their bound types without changing tokens.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_Fingerprint_DistinguishesAliasBindingExchange()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("AliasBindingExchange");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "using Left = System.Int32; using Right = System.String; namespace Example { public class Sample { public Left A; public Right B; } }");
            File.WriteAllText(secondSourcePath, "namespace Example { public class Other { } }");
            TransformWorkerClientResult before = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            File.WriteAllText(
                firstSourcePath,
                "using Left = System.String; using Right = System.Int32; namespace Example { public class Sample { public Left A; public Right B; } }");
            TransformWorkerClientResult after = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            Assert.That(before.Success, Is.True, before.ErrorMessage);
            Assert.That(after.Success, Is.True, after.ErrorMessage);
            Assert.That(
                after.Output.files[0].introducedTypes[0].declarationFingerprint,
                Is.Not.EqualTo(before.Output.files[0].introducedTypes[0].declarationFingerprint));
        }

        /// <summary>
        /// Verifies that an introduced type referencing a changed const from a compiled existing
        /// type is rejected instead of compiling the artifact with the metadata assembly's old value.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ChangedReferencedExistingConst_IsRejected()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("ChangedExistingConst");
            string sourcePath = Path.Combine(directory, "Edited.cs");
            string targetAssemblyPath = Path.Combine(directory, "ConstDriftTarget.dll");
            string targetAssemblyMvid = CreateConstDriftTargetAssembly(targetAssemblyPath, 1);
            File.WriteAllText(
                sourcePath,
                "namespace Example { public class Existing { public const int Value = 2; } public class Introduced { public int Get() { return Existing.Value; } } public class Safe { } }");

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateConstDriftInput(sourcePath, targetAssemblyPath, targetAssemblyMvid),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Has.Length.EqualTo(1));
            Assert.That(result.Output.files[0].introducedTypes[0].metadataName, Is.EqualTo("Example.Safe"));
            Assert.That(HotReloadWorkerReasonTestText.RenderAll(result.Output.files[0].introducedTypeDiagnostics), Has.Some.Contains("Existing.Value"));
        }

        /// <summary>
        /// Verifies that a const changed in another edited file, one this run does not transform,
        /// still rejects the introduced type that reads it, because planning would otherwise fold
        /// the value the target assembly was compiled with into the artifact.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ChangedConstInAnotherFile_IsRejected()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("ChangedConstInAnotherFile");
            string sourcePath = Path.Combine(directory, "Edited.cs");
            string siblingPath = Path.Combine(directory, "Sibling.cs");
            string targetAssemblyPath = Path.Combine(directory, "ConstDriftTarget.dll");
            string targetAssemblyMvid = CreateConstDriftTargetAssembly(targetAssemblyPath, 1);
            File.WriteAllText(
                sourcePath,
                "namespace Example { public class Introduced { public int Get() { return Existing.Value; } } }");
            File.WriteAllText(
                siblingPath,
                "namespace Example { public class Existing { public const int Value = 2; } }");

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateConstDriftInputWithSiblings(
                    sourcePath,
                    targetAssemblyPath,
                    targetAssemblyMvid,
                    new[] { siblingPath }),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Is.Empty);
            Assert.That(HotReloadWorkerReasonTestText.RenderAll(result.Output.files[0].introducedTypeDiagnostics), Has.Some.Contains("Existing.Value"));
        }

        /// <summary>
        /// Verifies that a const changed in another edited file does not reject an introduced type
        /// that only names it inside nameof, because nameof yields the identifier and never folds
        /// the value into the artifact.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ChangedConstNamedOnlyInNameof_IsNotRejected()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("ChangedConstNamedOnlyInNameof");
            string sourcePath = Path.Combine(directory, "Edited.cs");
            string siblingPath = Path.Combine(directory, "Sibling.cs");
            string targetAssemblyPath = Path.Combine(directory, "ConstDriftTarget.dll");
            string targetAssemblyMvid = CreateConstDriftTargetAssembly(targetAssemblyPath, 1);
            File.WriteAllText(
                sourcePath,
                "namespace Example { public class Introduced { public string Get() { return nameof(Existing.Value); } } }");
            File.WriteAllText(
                siblingPath,
                "namespace Example { public class Existing { public const int Value = 2; } }");

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateConstDriftInputWithSiblings(
                    sourcePath,
                    targetAssemblyPath,
                    targetAssemblyMvid,
                    new[] { siblingPath }),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Has.Length.EqualTo(1));
            Assert.That(result.Output.files[0].introducedTypes[0].metadataName, Is.EqualTo("Example.Introduced"));
            Assert.That(HotReloadWorkerReasonTestText.RenderAll(result.Output.files[0].introducedTypeDiagnostics), Is.Empty);
        }

        /// <summary>
        /// Verifies that a const in another edited file whose value cannot be read at all rejects
        /// the introduced type that reads it, because the artifact compile does not see that file
        /// and would succeed against the value still in the target assembly.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_UnreadableConstInAnotherFile_IsRejected()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("UnreadableConstInAnotherFile");
            string sourcePath = Path.Combine(directory, "Edited.cs");
            string siblingPath = Path.Combine(directory, "Sibling.cs");
            string targetAssemblyPath = Path.Combine(directory, "ConstDriftTarget.dll");
            string targetAssemblyMvid = CreateConstDriftTargetAssembly(targetAssemblyPath, 1);
            File.WriteAllText(
                sourcePath,
                "namespace Example { public class Introduced { public int Get() { return Existing.Value; } } }");
            File.WriteAllText(
                siblingPath,
                "namespace Example { public class Existing { public const int Value = ; } }");

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateConstDriftInputWithSiblings(
                    sourcePath,
                    targetAssemblyPath,
                    targetAssemblyMvid,
                    new[] { siblingPath }),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Is.Empty);
            Assert.That(HotReloadWorkerReasonTestText.RenderAll(result.Output.files[0].introducedTypeDiagnostics), Has.Some.Contains("Existing.Value"));
        }

        /// <summary>
        /// Verifies that a changed const does not reject an introduced type that does not refer to it.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_UnreferencedChangedExistingConst_DoesNotRejectIntroducedType()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("UnreferencedChangedExistingConst");
            string sourcePath = Path.Combine(directory, "Edited.cs");
            string targetAssemblyPath = Path.Combine(directory, "ConstDriftTarget.dll");
            string targetAssemblyMvid = CreateConstDriftTargetAssembly(targetAssemblyPath, 1);
            File.WriteAllText(
                sourcePath,
                "namespace Example { public class Existing { public const int Value = 2; } public class Introduced { public int Get() { return 3; } } }");

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateConstDriftInput(sourcePath, targetAssemblyPath, targetAssemblyMvid),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Has.Length.EqualTo(1));
            Assert.That(result.Output.files[0].introducedTypes[0].metadataName, Is.EqualTo("Example.Introduced"));
            Assert.That(HotReloadWorkerReasonTestText.RenderAll(result.Output.files[0].introducedTypeDiagnostics), Is.Empty);
        }

        /// <summary>
        /// Verifies that a referenced existing const with the same source and metadata value remains supported.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_UnchangedReferencedExistingConst_RemainsSupported()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("UnchangedExistingConst");
            string sourcePath = Path.Combine(directory, "Edited.cs");
            string targetAssemblyPath = Path.Combine(directory, "ConstDriftTarget.dll");
            string targetAssemblyMvid = CreateConstDriftTargetAssembly(targetAssemblyPath, 1);
            File.WriteAllText(
                sourcePath,
                "namespace Example { public class Existing { public const int Value = 1; } public class Introduced { public int Get() { return Existing.Value; } } }");

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateConstDriftInput(sourcePath, targetAssemblyPath, targetAssemblyMvid),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Has.Length.EqualTo(1));
            Assert.That(result.Output.files[0].introducedTypes[0].metadataName, Is.EqualTo("Example.Introduced"));
            Assert.That(HotReloadWorkerReasonTestText.RenderAll(result.Output.files[0].introducedTypeDiagnostics), Is.Empty);
        }

        /// <summary>
        /// Verifies that the fingerprint serialization retains token boundaries for expressions
        /// whose unseparated token text would otherwise collide.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_Fingerprint_DistinguishesTokenBoundaries()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("TokenBoundaries");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "namespace Example { public class TokenBoundary { private int a; private int b; public int Value() { return a + ++b; } } }");
            File.WriteAllText(secondSourcePath, "namespace Example { public class Other { } }");
            TransformWorkerClientResult before = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            File.WriteAllText(
                firstSourcePath,
                "namespace Example { public class TokenBoundary { private int a; private int b; public int Value() { return a++ + b; } } }");
            TransformWorkerClientResult after = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            Assert.That(before.Success, Is.True, before.ErrorMessage);
            Assert.That(after.Success, Is.True, after.ErrorMessage);
            Assert.That(
                after.Output.files[0].introducedTypes[0].declarationFingerprint,
                Is.Not.EqualTo(before.Output.files[0].introducedTypes[0].declarationFingerprint));
        }

        /// <summary>
        /// Verifies that editing only a method body reads as a body difference, while changing its
        /// signature reads as a declaration change, and that an unedited rerun stays identical.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_Fingerprint_SeparatesBodyEditsFromSignatureEdits()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("FingerprintBodyEdits");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(secondSourcePath, UnrelatedSecondSource);

            string baseline = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class BodyEdit { public int Value(int x) { return x + 1; } } }");
            string rerun = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class BodyEdit { public int Value(int x) { return x + 1; } } }");
            string bodyEdited = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class BodyEdit { public int Value(int x) { return x + 2; } } }");
            string signatureEdited = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class BodyEdit { public int Value(int x, int y) { return x + 1; } } }");

            Assert.That(rerun, Is.EqualTo(baseline));
            HotReloadIntroducedTypeFingerprintComparison unchanged = CompareFingerprints(baseline, rerun);
            Assert.That(unchanged.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.Identical));

            HotReloadIntroducedTypeFingerprintComparison bodyOnly = CompareFingerprints(baseline, bodyEdited);
            Assert.That(bodyOnly.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.BodyOnly));
            Assert.That(bodyOnly.ChangedBodyKeys, Has.Count.EqualTo(1));
            Assert.That(bodyOnly.ChangedBodyKeys[0], Does.Contain("Value"));

            HotReloadIntroducedTypeFingerprintComparison signatureChanged = CompareFingerprints(baseline, signatureEdited);
            Assert.That(
                signatureChanged.Kind,
                Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged));
            Assert.That(signatureChanged.Details, Has.Some.StartsWith("removed:"));
            Assert.That(signatureChanged.Details, Has.Some.StartsWith("added:"));
        }

        /// <summary>
        /// Verifies that reordering members, which leaves every member hash untouched, still reads
        /// as a declaration change: enum values and field initialization both depend on that order.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_Fingerprint_ReorderedMembers_ReportOrderChanged()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("FingerprintMemberOrder");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(secondSourcePath, UnrelatedSecondSource);

            string enumBaseline = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public enum Sample { A, B } }");
            string enumReordered = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public enum Sample { B, A } }");
            string fieldsBaseline = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Ordered { public int A = 1; public int B = 2; } }");
            string fieldsReordered = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Ordered { public int B = 2; public int A = 1; } }");

            Assert.That(enumReordered, Is.Not.EqualTo(enumBaseline));
            Assert.That(fieldsReordered, Is.Not.EqualTo(fieldsBaseline));
            HotReloadIntroducedTypeFingerprintComparison enumComparison = CompareFingerprints(enumBaseline, enumReordered);
            Assert.That(
                enumComparison.Kind,
                Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged));
            Assert.That(enumComparison.Details, Contains.Item("order"));
            HotReloadIntroducedTypeFingerprintComparison fieldComparison = CompareFingerprints(fieldsBaseline, fieldsReordered);
            Assert.That(
                fieldComparison.Kind,
                Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged));
            Assert.That(fieldComparison.Details, Contains.Item("order"));
        }

        /// <summary>
        /// Verifies that edits to the observable surface of a type - a field initializer, a default
        /// argument, a const value, an enum value, and the type header - never read as body edits.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_Fingerprint_SurfaceEdits_ReportDeclarationChanged()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("FingerprintSurfaceEdits");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(secondSourcePath, UnrelatedSecondSource);

            string valuesBaseline = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Surface { public const int Limit = 1; public int Count = 1;"
                + " public int Value(int x = 1) { return x; } } }");
            string initializerEdited = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Surface { public const int Limit = 1; public int Count = 2;"
                + " public int Value(int x = 1) { return x; } } }");
            string defaultArgumentEdited = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Surface { public const int Limit = 1; public int Count = 1;"
                + " public int Value(int x = 2) { return x; } } }");
            string constEdited = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Surface { public const int Limit = 2; public int Count = 1;"
                + " public int Value(int x = 1) { return x; } } }");
            string enumValueBaseline = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public enum Surface { First = 1 } }");
            string enumValueEdited = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public enum Surface { First = 2 } }");
            string headerBaseline = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Surface { public int Value() { return 1; } } }");
            string headerEdited = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Surface : System.IDisposable { public int Value() { return 1; }"
                + " public void Dispose() { } } }");

            AssertDeclarationChanged(valuesBaseline, initializerEdited);
            AssertDeclarationChanged(valuesBaseline, defaultArgumentEdited);
            AssertDeclarationChanged(valuesBaseline, constEdited);
            AssertDeclarationChanged(enumValueBaseline, enumValueEdited);
            HotReloadIntroducedTypeFingerprintComparison headerComparison = CompareFingerprints(headerBaseline, headerEdited);
            Assert.That(
                headerComparison.Kind,
                Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged));
            Assert.That(headerComparison.Details, Contains.Item("header"));
        }

        /// <summary>
        /// Verifies that rewriting a body without changing the declaration - an expression body
        /// turned into a block, an accessor body, and an alias a body depends on - stays body only.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_Fingerprint_BodyRewrites_StayBodyOnly()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("FingerprintBodyRewrites");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(secondSourcePath, UnrelatedSecondSource);

            string expressionBodied = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Bodies { public int Value() => 1; } }");
            string blockBodied = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Bodies { public int Value() { return 1; } } }");
            string accessorBaseline = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Bodies { public int Value { get { return 1; } } } }");
            string accessorEdited = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Bodies { public int Value { get { return 2; } } } }");
            string aliasBaseline = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "using Alias = System.IDisposable; namespace Example { public class Bodies"
                + " { public object Create() { Alias value = null; return value; } } }");
            string aliasRebound = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "using Alias = System.ICloneable; namespace Example { public class Bodies"
                + " { public object Create() { Alias value = null; return value; } } }");

            AssertBodyOnly(expressionBodied, blockBodied);
            AssertBodyOnly(accessorBaseline, accessorEdited);
            AssertBodyOnly(aliasBaseline, aliasRebound);
        }

        /// <summary>
        /// Verifies that a type declaring the same signature twice, which cannot compile, still
        /// produces a readable fingerprint instead of failing the worker on a duplicate member key.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_Fingerprint_DuplicateMemberSignature_StillProducesAFingerprint()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("FingerprintDuplicateMembers");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(secondSourcePath, UnrelatedSecondSource);

            string fingerprint = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Duplicated { public int Value() { return 1; }"
                + " public int Value() { return 2; } } }");

            HotReloadIntroducedTypeFingerprint parsed = ParseFingerprint(fingerprint);
            Assert.That(parsed.Members.Count, Is.EqualTo(2));
            Assert.That(parsed.Members[1].Key, Is.Not.EqualTo(parsed.Members[0].Key));
        }

        /// <summary>
        /// Verifies that an accessor written as an expression body follows the same rule as a
        /// method: rewriting it as a block is a body change, not a declaration change.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_Fingerprint_AccessorExpressionBody_StaysBodyOnly()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("FingerprintAccessorArrow");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(secondSourcePath, UnrelatedSecondSource);

            string expressionAccessor = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Bodies { public int Value { get => 1; } } }");
            string blockAccessor = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Bodies { public int Value { get { return 1; } } } }");

            AssertBodyOnly(expressionAccessor, blockAccessor);
        }

        /// <summary>
        /// Verifies that a comment written inside a parameter type does not change the fingerprint,
        /// because the member key must describe the same tokens the hashes read.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_Fingerprint_CommentInsideAParameterType_IsIdentical()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("FingerprintTypeComment");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(secondSourcePath, UnrelatedSecondSource);

            string plain = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Commented { public void Take(System.String value) { } } }");
            string commented = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Commented { public void Take(System./*note*/String value) { } } }");

            HotReloadIntroducedTypeFingerprintComparison comparison = CompareFingerprints(plain, commented);
            Assert.That(comparison.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.Identical));
        }

        /// <summary>
        /// Verifies that a line comment inside a declaration still yields a readable fingerprint,
        /// because a member key carrying that comment's line break cannot be recorded at all.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_Fingerprint_LineCommentInsideADeclaration_StillProducesAFingerprint()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("FingerprintLineComment");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(secondSourcePath, UnrelatedSecondSource);

            string plain = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Commented { public void Take(System.String value) { } } }");
            string commented = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "namespace Example { public class Commented { public void Take(System.//note\n"
                + "String value) { } } }");

            HotReloadIntroducedTypeFingerprintComparison comparison = CompareFingerprints(plain, commented);
            Assert.That(comparison.Kind, Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.Identical));
        }

        /// <summary>
        /// Verifies that swapping what two aliases bind to inside one body changes the fingerprint,
        /// because the body records where each dependency is used and not just which ones it uses.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_Fingerprint_SwappedAliasesInOneBody_ReportBodyOnly()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("FingerprintAliasSwap");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(secondSourcePath, UnrelatedSecondSource);

            string baseline = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "using First = System.IDisposable; using Second = System.ICloneable;"
                + " namespace Example { public class Bodies { public object Create()"
                + " { First first = null; Second second = null; return first ?? (object)second; } } }");
            string swapped = await RunFingerprintAsync(
                firstSourcePath,
                secondSourcePath,
                "using First = System.ICloneable; using Second = System.IDisposable;"
                + " namespace Example { public class Bodies { public object Create()"
                + " { First first = null; Second second = null; return first ?? (object)second; } } }");

            AssertBodyOnly(baseline, swapped);
        }

        private static void AssertDeclarationChanged(string baseline, string edited)
        {
            HotReloadIntroducedTypeFingerprintComparison comparison = CompareFingerprints(baseline, edited);
            Assert.That(
                comparison.Kind,
                Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged),
                "Expected a declaration change for: " + edited);
        }

        private static void AssertBodyOnly(string baseline, string edited)
        {
            HotReloadIntroducedTypeFingerprintComparison comparison = CompareFingerprints(baseline, edited);
            Assert.That(
                comparison.Kind,
                Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.BodyOnly),
                "Expected a body-only change for: " + edited);
        }

        private static HotReloadIntroducedTypeFingerprintComparison CompareFingerprints(string baseline, string edited)
        {
            return HotReloadIntroducedTypeFingerprint.Compare(ParseFingerprint(baseline), ParseFingerprint(edited));
        }

        private static HotReloadIntroducedTypeFingerprint ParseFingerprint(string text)
        {
            bool parsed = HotReloadIntroducedTypeFingerprint.TryParse(
                text,
                out HotReloadIntroducedTypeFingerprint value);
            Assert.That(parsed, Is.True, "Fingerprint is not in the canonical form: " + text);
            return value;
        }

        private static async Task<string> RunFingerprintAsync(
            string firstSourcePath,
            string secondSourcePath,
            string source)
        {
            File.WriteAllText(firstSourcePath, source);
            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Has.Length.EqualTo(1));
            return result.Output.files[0].introducedTypes[0].declarationFingerprint;
        }

        /// <summary>
        /// Verifies that no introduced type is reported when the target assembly cannot be read,
        /// because every declaration would otherwise look absent from it and be treated as new.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_UnreadableTargetAssembly_ReportsNoIntroducedType()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("UnreadableTarget");
            string sourcePath = Path.Combine(directory, "Edited.cs");
            string targetAssemblyPath = Path.Combine(directory, "Unreadable.dll");
            File.WriteAllText(targetAssemblyPath, "this is not an assembly");
            File.WriteAllText(sourcePath, "namespace Example { public class Introduced { } }");

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateConstDriftInput(sourcePath, targetAssemblyPath, Guid.NewGuid().ToString()),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Is.Empty);
            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(result.Output.files[0].introducedTypeDiagnostics),
                Has.Some.Contains("target assembly"));
        }

        /// <summary>
        /// Verifies that no introduced type is reported when a reference other than the target
        /// exists but is not readable metadata, because the boundary checks would then answer
        /// from error types instead of the real base types and attributes.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_UnreadableReference_ReportsNoIntroducedType()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("UnreadableReference");
            string sourcePath = Path.Combine(directory, "Edited.cs");
            string targetAssemblyPath = Path.Combine(directory, "ConstDriftTarget.dll");
            string targetAssemblyMvid = CreateConstDriftTargetAssembly(targetAssemblyPath, 1);
            string brokenReferencePath = Path.Combine(directory, "BrokenReference.dll");
            File.WriteAllText(brokenReferencePath, "this is not an assembly");
            File.WriteAllText(sourcePath, "namespace Example { public class Introduced { } }");

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreatePreparationInput(
                    sourcePath,
                    targetAssemblyPath,
                    "ConstDriftTarget",
                    targetAssemblyMvid,
                    new[] { brokenReferencePath },
                    Array.Empty<string>()),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Is.Empty);
            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(result.Output.files[0].introducedTypeDiagnostics),
                Has.Some.Contains("its references"));
        }

        /// <summary>
        /// Verifies that no introduced type is reported when the request names a module version
        /// id the analysed target assembly does not carry, because the descriptors would then
        /// claim an assembly generation the planning never looked at.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_TargetAssemblyMvidMismatch_ReportsNoIntroducedType()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("TargetMvidMismatch");
            string sourcePath = Path.Combine(directory, "Edited.cs");
            string targetAssemblyPath = Path.Combine(directory, "ConstDriftTarget.dll");
            CreateConstDriftTargetAssembly(targetAssemblyPath, 1);
            File.WriteAllText(sourcePath, "namespace Example { public class Introduced { } }");

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreateConstDriftInput(sourcePath, targetAssemblyPath, Guid.NewGuid().ToString()),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Is.Empty);
            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(result.Output.files[0].introducedTypeDiagnostics),
                Has.Some.Contains("identity does not match"));
        }

        /// <summary>
        /// Verifies that a declaration whose metadata name already exists in a different
        /// referenced assembly is still introduced, because only the target assembly decides
        /// whether the type is already compiled.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_SameMetadataNameInOtherAssembly_IsStillIntroduced()
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory("SameNameOtherAssembly");
            string sourcePath = Path.Combine(directory, "Edited.cs");
            string targetAssemblyPath = Path.Combine(directory, "SameNameTarget.dll");
            string otherAssemblyPath = Path.Combine(directory, "SameNameOther.dll");
            string targetAssemblyMvid = TransformWorkerIntroducedTypeTestInputs.CreateAssemblyWithType(
                targetAssemblyPath,
                "SameNameTarget",
                "Example",
                "Unrelated");
            TransformWorkerIntroducedTypeTestInputs.CreateAssemblyWithType(otherAssemblyPath, "SameNameOther", "Example", "Shared");
            File.WriteAllText(sourcePath, "namespace Example { public class Shared { } }");

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                TransformWorkerIntroducedTypeTestInputs.CreatePreparationInput(
                    sourcePath,
                    targetAssemblyPath,
                    "SameNameTarget",
                    targetAssemblyMvid,
                    new[] { otherAssemblyPath },
                    Array.Empty<string>()),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Has.Length.EqualTo(1));
            Assert.That(
                result.Output.files[0].introducedTypes[0].metadataName,
                Is.EqualTo("Example.Shared"));
            Assert.That(
                result.Output.files[0].introducedTypes[0].originalAssemblyName,
                Is.EqualTo("SameNameTarget"));
        }

        private static string CreateConstDriftTargetAssembly(string path, int constantValue)
        {
            AssemblyNameDefinition assemblyName = new AssemblyNameDefinition(
                "ConstDriftTarget",
                new Version(1, 0, 0, 0));
            using (AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                assemblyName,
                "ConstDriftTarget",
                ModuleKind.Dll))
            {
                TypeDefinition existingType = new TypeDefinition(
                    "Example",
                    "Existing",
                    CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                    assembly.MainModule.TypeSystem.Object);
                FieldDefinition valueField = new FieldDefinition(
                    "Value",
                    CecilFieldAttributes.Public | CecilFieldAttributes.Static | CecilFieldAttributes.Literal | CecilFieldAttributes.HasDefault,
                    assembly.MainModule.TypeSystem.Int32)
                {
                    Constant = constantValue
                };
                existingType.Fields.Add(valueField);
                assembly.MainModule.Types.Add(existingType);
                assembly.Write(path);
            }

            using (ModuleDefinition module = ModuleDefinition.ReadModule(path))
            {
                return module.Mvid.ToString();
            }
        }

    }
}
