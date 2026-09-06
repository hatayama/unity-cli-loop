using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;

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
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

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

            TransformWorkerInputDto input = CreateInput(firstSourcePath, secondSourcePath);
            TransformWorkerClientResult result = await TransformWorkerClient.RunAsync(
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
            Assert.That(
                result.Output.files[1].introducedTypes,
                Is.Empty);
            Assert.That(
                result.Output.files[1].introducedTypeDiagnostics,
                Has.Some.Contains("Non-public"));
            Assert.That(
                result.Output.files[1].introducedTypeDiagnostics,
                Has.Some.Contains("Generic"));
            Assert.That(
                result.Output.files[1].introducedTypeDiagnostics,
                Has.Some.Contains("Nested"));
        }

        /// <summary>
        /// Verifies that a parse-failed row remains isolated from the shared semantic compilation
        /// while a sibling file still emits its independently valid introduced type.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ParseFailedSibling_DoesNotEnterSharedAnalysis()
        {
            string directory = CreateSourceDirectory("ParseIsolation");
            string validSourcePath = Path.Combine(directory, "Valid.cs");
            string invalidSourcePath = Path.Combine(directory, "Invalid.cs");
            File.WriteAllText(validSourcePath, "namespace Example { public class ValidIntroduced { } }");
            File.WriteAllText(invalidSourcePath, "namespace Example { public class BrokenIntroduced { ");

            TransformWorkerClientResult result = await TransformWorkerClient.RunAsync(
                CreateInput(validSourcePath, invalidSourcePath),
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
            string directory = CreateSourceDirectory("NamespaceAliases");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "namespace Outer { using Alias = System.String; namespace Inner { using Alias = System.Int32; public class Aliased { public Alias Value; } } }");
            File.WriteAllText(secondSourcePath, "namespace Example { public class Other { } }");

            TransformWorkerInputDto input = CreateInput(firstSourcePath, secondSourcePath);
            TransformWorkerClientResult result = await TransformWorkerClient.RunAsync(
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
            string directory = CreateSourceDirectory("GlobalNamespaceSources");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "using System; using Alias = System.IDisposable; public class GlobalFirst { public Alias Create() { return null; } }");
            File.WriteAllText(
                secondSourcePath,
                "using System; using Alias = System.ICloneable; public class GlobalSecond { public Alias Create() { return null; } }");

            TransformWorkerInputDto input = CreateInput(firstSourcePath, secondSourcePath);
            TransformWorkerClientResult workerResult = await TransformWorkerClient.RunAsync(
                input,
                CancellationToken.None);

            Assert.That(workerResult.Success, Is.True, workerResult.ErrorMessage);
            Assert.That(workerResult.Output.files[0].introducedTypes, Has.Length.EqualTo(1));
            Assert.That(workerResult.Output.files[1].introducedTypes, Has.Length.EqualTo(1));
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                CreateDescriptors(workerResult.Output.files);
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
                new HotReloadIntroducedTypeCompilerEnvironment());

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
            string directory = CreateSourceDirectory("GlobalUsingAlias");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "global using Alias = System.IDisposable; namespace GlobalAliasFixture { public class First { public Alias Create() { return null; } } }");
            File.WriteAllText(
                secondSourcePath,
                "namespace GlobalAliasFixture { public class Second { public Alias Create() { return null; } } }");

            TransformWorkerInputDto input = CreateInput(firstSourcePath, secondSourcePath);
            TransformWorkerClientResult workerResult = await TransformWorkerClient.RunAsync(
                input,
                CancellationToken.None);

            List<HotReloadIntroducedTypeDescriptor> descriptors = CreateDescriptors(workerResult.Output.files);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadIntroducedTypeCompilationRequest request =
                HotReloadIntroducedTypeCompilationRequest.CreateBatch(
                    new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "global-using-alias").Create(),
                    descriptors,
                    input.referencePaths,
                    input.defines);
            HotReloadIntroducedTypeCompilerResult compileResult = await new HotReloadIntroducedTypeCompiler(
                new HotReloadIntroducedTypeCompilerEnvironment()).CompileAsync(request, CancellationToken.None);

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
                new HotReloadIntroducedTypeCompilerEnvironment()).CompileAsync(subsetRequest, CancellationToken.None);

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
            string directory = CreateSourceDirectory("RootUsingScope");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "using ScopeGlobal; using static ScopeStatic.Helpers; using ExtensionGlobal; namespace ScopeGlobal { public class Bound { } } namespace ScopeFixture.ScopeGlobal { public class Bound { } } namespace ScopeStatic { public static class Helpers { public static int Value() { return 7; } } } namespace ScopeFixture.ScopeStatic { public static class Helpers { public static int Value() { return 9; } } } namespace ExtensionGlobal { public static class Extensions { public static int ExtensionValue(this string value) { return 17; } } } namespace ScopeFixture.ExtensionGlobal { public static class Extensions { public static int ExtensionValue(this string value) { return 19; } } } namespace ScopeFixture { public class Consumer { public Bound Create() { return null; } public int GetValue() { return Value(); } public int GetExtensionValue() { return \"test\".ExtensionValue(); } } }");
            File.WriteAllText(secondSourcePath, "namespace ScopeFixture { public class Other { } }");

            TransformWorkerInputDto input = CreateInput(firstSourcePath, secondSourcePath);
            TransformWorkerClientResult workerResult = await TransformWorkerClient.RunAsync(
                input,
                CancellationToken.None);

            List<HotReloadIntroducedTypeDescriptor> descriptors = CreateDescriptors(workerResult.Output.files);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadIntroducedTypeCompilationRequest request =
                HotReloadIntroducedTypeCompilationRequest.CreateBatch(
                    new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "root-using-scope").Create(),
                    descriptors,
                    input.referencePaths,
                    input.defines);
            HotReloadIntroducedTypeCompilerResult compileResult = await new HotReloadIntroducedTypeCompiler(
                new HotReloadIntroducedTypeCompilerEnvironment()).CompileAsync(request, CancellationToken.None);

            Assert.That(workerResult.Success, Is.True, workerResult.ErrorMessage);
            Assert.That(compileResult.Success, Is.True, compileResult.ErrorMessage);
            Type consumer = compileResult.Artifact.Assembly.GetType("ScopeFixture.Consumer");
            Assert.That(consumer.GetMethod("Create").ReturnType.FullName, Is.EqualTo("ScopeGlobal.Bound"));
            Assert.That(consumer.GetMethod("GetValue").Invoke(Activator.CreateInstance(consumer), null), Is.EqualTo(7));
            Assert.That(consumer.GetMethod("GetExtensionValue").Invoke(Activator.CreateInstance(consumer), null), Is.EqualTo(17));
        }

        /// <summary>
        /// Verifies that every unsupported declaration category produces an owning-file
        /// diagnostic and cannot become an introduced artifact descriptor.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_UnsupportedSemanticCategories_ReportDiagnostics()
        {
            string directory = CreateSourceDirectory("UnsupportedCategories");
            string firstSourcePath = Path.Combine(directory, "Unsupported.cs");
            string secondSourcePath = Path.Combine(directory, "Other.cs");
            File.WriteAllText(
                firstSourcePath,
                "namespace System.Runtime.CompilerServices { internal sealed class ModuleInitializerAttribute : System.Attribute { } } "
                + "namespace Example { internal class Hidden { } public class Generic<T> { } public partial class Partial { } public ref struct RefLike { } public unsafe class UnsafeType { public int* Value; } public class ObjectType : UnityEngine.Object { } [System.Serializable] public class SerializableType { } public static class InitializerType { [System.Runtime.CompilerServices.ModuleInitializer] public static void Initialize() { } } public delegate void AddedDelegate(); public class Outer { public class Nested { } } }");
            File.WriteAllText(secondSourcePath, "namespace Example { public class Other { } }");

            TransformWorkerClientResult result = await TransformWorkerClient.RunAsync(
                CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Is.Empty);
            Assert.That(result.Output.files[0].introducedTypeDiagnostics, Has.Some.Contains("Non-public"));
            Assert.That(result.Output.files[0].introducedTypeDiagnostics, Has.Some.Contains("Generic"));
            Assert.That(result.Output.files[0].introducedTypeDiagnostics, Has.Some.Contains("Partial"));
            Assert.That(result.Output.files[0].introducedTypeDiagnostics, Has.Some.Contains("Ref-like"));
            Assert.That(result.Output.files[0].introducedTypeDiagnostics, Has.Some.Contains("Unsafe"));
            Assert.That(result.Output.files[0].introducedTypeDiagnostics, Has.Some.Contains("Unity object"));
            Assert.That(result.Output.files[0].introducedTypeDiagnostics, Has.Some.Contains("Serializable"));
            Assert.That(result.Output.files[0].introducedTypeDiagnostics, Has.Some.Contains("Module initializer"));
            Assert.That(result.Output.files[0].introducedTypeDiagnostics, Has.Some.Contains("Delegate"));
            Assert.That(result.Output.files[0].introducedTypeDiagnostics, Has.Some.Contains("Nested"));
        }

        /// <summary>
        /// Verifies that an outer declaration containing an excluded nested declaration is
        /// rejected so the generated artifact cannot retain the nested implementation.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_OuterContainingNestedDeclaration_IsRejectedBeforeArtifactCompile()
        {
            string directory = CreateSourceDirectory("NestedArtifactRejection");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "namespace System.Runtime.CompilerServices { internal sealed class ModuleInitializerAttribute : System.Attribute { } } "
                + "namespace Example { public class Outer { public static class Nested { [System.Runtime.CompilerServices.ModuleInitializer] public static void Initialize() { } } } public class Safe { } }");
            File.WriteAllText(secondSourcePath, "namespace Example { public class Other { } }");

            TransformWorkerInputDto input = CreateInput(firstSourcePath, secondSourcePath);
            TransformWorkerClientResult workerResult = await TransformWorkerClient.RunAsync(
                input,
                CancellationToken.None);

            // The rejection has to be observable in the worker output before any artifact is
            // compiled, otherwise a later assertion about the assembly could pass for the wrong
            // reason - because compilation happened to drop the type rather than because the
            // outer declaration was refused.
            Assert.That(workerResult.Success, Is.True, workerResult.ErrorMessage);
            Assert.That(workerResult.Output.files[0].introducedTypes, Has.Length.EqualTo(1));
            Assert.That(workerResult.Output.files[0].introducedTypes[0].metadataName, Is.EqualTo("Example.Safe"));
            Assert.That(workerResult.Output.files[0].introducedTypeDiagnostics, Has.Some.Contains("Nested"));

            List<HotReloadIntroducedTypeDescriptor> descriptors = CreateDescriptors(workerResult.Output.files);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadIntroducedTypeCompilationRequest request =
                HotReloadIntroducedTypeCompilationRequest.CreateBatch(
                    new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "nested-artifact-rejection").Create(),
                    descriptors,
                    input.referencePaths,
                    input.defines);
            HotReloadIntroducedTypeCompilerResult compileResult = await new HotReloadIntroducedTypeCompiler(
                new HotReloadIntroducedTypeCompilerEnvironment()).CompileAsync(request, CancellationToken.None);

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
            string directory = CreateSourceDirectory("Fingerprints");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "using Alias = System.IDisposable; using System.Text; namespace Example { public class Fingerprint { public Alias Create() { return null; } } } namespace Unrelated { using Other = System.Text; class Ignore { } }");
            File.WriteAllText(secondSourcePath, "namespace Unrelated { using Other = System.String; public class OtherType { } }");
            TransformWorkerClientResult first = await TransformWorkerClient.RunAsync(
                CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            File.WriteAllText(
                firstSourcePath,
                "using Alias = System.IDisposable; using System.Text; namespace Example { public class Fingerprint { public Alias Create() { return null; } } public class LaterIntroduced { } } namespace Unrelated { using Other = System.Text; class Ignore { } }");
            TransformWorkerClientResult laterType = await TransformWorkerClient.RunAsync(
                CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            File.WriteAllText(
                firstSourcePath,
                "using Alias = System.IDisposable; using System.Text; namespace Example { public class Fingerprint { public Alias Create() { return null; } } } namespace Unrelated { using Other = System.IO; class Ignore { } }");
            TransformWorkerClientResult unrelatedUsing = await TransformWorkerClient.RunAsync(
                CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            File.WriteAllText(
                firstSourcePath,
                "using Alias = System.IDisposable; using System.Text; namespace Example { /* trivia */ public class Fingerprint { public Alias Create() { return null; } } } namespace Unrelated { using Other = System.IO; class Ignore { } }");
            TransformWorkerClientResult trivia = await TransformWorkerClient.RunAsync(
                CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            File.WriteAllText(
                firstSourcePath,
                "using Alias = System.ICloneable; using System.Text; namespace Example { public class Fingerprint { public Alias Create() { return null; } } }");
            TransformWorkerClientResult aliasChanged = await TransformWorkerClient.RunAsync(
                CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            File.WriteAllText(
                firstSourcePath,
                "using Alias = System.IDisposable; using System.Text; namespace Example { public class Fingerprint { public Alias Create() { return null; } } }");
            TransformWorkerInputDto definesChangedInput = CreateInput(firstSourcePath, secondSourcePath);
            definesChangedInput.defines = new[] { "CHANGED_DEFINE" };
            TransformWorkerClientResult definesChanged = await TransformWorkerClient.RunAsync(
                definesChangedInput,
                CancellationToken.None);

            Assert.That(first.Success, Is.True, first.ErrorMessage);
            Assert.That(laterType.Success, Is.True, laterType.ErrorMessage);
            Assert.That(unrelatedUsing.Success, Is.True, unrelatedUsing.ErrorMessage);
            Assert.That(trivia.Success, Is.True, trivia.ErrorMessage);
            Assert.That(aliasChanged.Success, Is.True, aliasChanged.ErrorMessage);
            Assert.That(definesChanged.Success, Is.True, definesChanged.ErrorMessage);
            Assert.That(
                trivia.Output.files[0].introducedTypes[0].declarationFingerprint,
                Is.EqualTo(first.Output.files[0].introducedTypes[0].declarationFingerprint));
            Assert.That(
                unrelatedUsing.Output.files[0].introducedTypes[0].declarationFingerprint,
                Is.EqualTo(first.Output.files[0].introducedTypes[0].declarationFingerprint));
            Assert.That(laterType.Output.files[0].introducedTypes, Has.Length.EqualTo(2));
            Assert.That(
                laterType.Output.files[0].introducedTypes[0].declarationFingerprint,
                Is.EqualTo(first.Output.files[0].introducedTypes[0].declarationFingerprint));
            Assert.That(
                aliasChanged.Output.files[0].introducedTypes[0].declarationFingerprint,
                Is.Not.EqualTo(first.Output.files[0].introducedTypes[0].declarationFingerprint));
            Assert.That(
                definesChanged.Output.files[0].introducedTypes[0].declarationFingerprint,
                Is.Not.EqualTo(first.Output.files[0].introducedTypes[0].declarationFingerprint));
        }

        /// <summary>
        /// Verifies that a fingerprint retains each semantic dependency at its stable declaration
        /// traversal position when aliases exchange their bound types without changing tokens.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_Fingerprint_DistinguishesAliasBindingExchange()
        {
            string directory = CreateSourceDirectory("AliasBindingExchange");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "using Left = System.Int32; using Right = System.String; namespace Example { public class Sample { public Left A; public Right B; } }");
            File.WriteAllText(secondSourcePath, "namespace Example { public class Other { } }");
            TransformWorkerClientResult before = await TransformWorkerClient.RunAsync(
                CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            File.WriteAllText(
                firstSourcePath,
                "using Left = System.String; using Right = System.Int32; namespace Example { public class Sample { public Left A; public Right B; } }");
            TransformWorkerClientResult after = await TransformWorkerClient.RunAsync(
                CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            Assert.That(before.Success, Is.True, before.ErrorMessage);
            Assert.That(after.Success, Is.True, after.ErrorMessage);
            Assert.That(
                after.Output.files[0].introducedTypes[0].declarationFingerprint,
                Is.Not.EqualTo(before.Output.files[0].introducedTypes[0].declarationFingerprint));
        }

        /// <summary>
        /// Verifies that a changed const does not reject an introduced type that does not refer to it.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_UnreferencedChangedExistingConst_DoesNotRejectIntroducedType()
        {
            string directory = CreateSourceDirectory("UnreferencedChangedExistingConst");
            string sourcePath = Path.Combine(directory, "Edited.cs");
            string targetAssemblyPath = Path.Combine(directory, "ConstDriftTarget.dll");
            string targetAssemblyMvid = CreateConstDriftTargetAssembly(targetAssemblyPath, 1);
            File.WriteAllText(
                sourcePath,
                "namespace Example { public class Existing { public const int Value = 2; } public class Introduced { public int Get() { return 3; } } }");

            TransformWorkerClientResult result = await TransformWorkerClient.RunAsync(
                CreateConstDriftInput(sourcePath, targetAssemblyPath, targetAssemblyMvid),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Has.Length.EqualTo(1));
            Assert.That(result.Output.files[0].introducedTypes[0].metadataName, Is.EqualTo("Example.Introduced"));
            Assert.That(result.Output.files[0].introducedTypeDiagnostics, Is.Empty);
        }

        /// <summary>
        /// Verifies that a referenced existing const with the same source and metadata value remains supported.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_UnchangedReferencedExistingConst_RemainsSupported()
        {
            string directory = CreateSourceDirectory("UnchangedExistingConst");
            string sourcePath = Path.Combine(directory, "Edited.cs");
            string targetAssemblyPath = Path.Combine(directory, "ConstDriftTarget.dll");
            string targetAssemblyMvid = CreateConstDriftTargetAssembly(targetAssemblyPath, 1);
            File.WriteAllText(
                sourcePath,
                "namespace Example { public class Existing { public const int Value = 1; } public class Introduced { public int Get() { return Existing.Value; } } }");

            TransformWorkerClientResult result = await TransformWorkerClient.RunAsync(
                CreateConstDriftInput(sourcePath, targetAssemblyPath, targetAssemblyMvid),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Has.Length.EqualTo(1));
            Assert.That(result.Output.files[0].introducedTypes[0].metadataName, Is.EqualTo("Example.Introduced"));
            Assert.That(result.Output.files[0].introducedTypeDiagnostics, Is.Empty);
        }

        /// <summary>
        /// Verifies that the fingerprint serialization retains token boundaries for expressions
        /// whose unseparated token text would otherwise collide.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_Fingerprint_DistinguishesTokenBoundaries()
        {
            string directory = CreateSourceDirectory("TokenBoundaries");
            string firstSourcePath = Path.Combine(directory, "First.cs");
            string secondSourcePath = Path.Combine(directory, "Second.cs");
            File.WriteAllText(
                firstSourcePath,
                "namespace Example { public class TokenBoundary { private int a; private int b; public int Value() { return a + ++b; } } }");
            File.WriteAllText(secondSourcePath, "namespace Example { public class Other { } }");
            TransformWorkerClientResult before = await TransformWorkerClient.RunAsync(
                CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            File.WriteAllText(
                firstSourcePath,
                "namespace Example { public class TokenBoundary { private int a; private int b; public int Value() { return a++ + b; } } }");
            TransformWorkerClientResult after = await TransformWorkerClient.RunAsync(
                CreateInput(firstSourcePath, secondSourcePath),
                CancellationToken.None);

            Assert.That(before.Success, Is.True, before.ErrorMessage);
            Assert.That(after.Success, Is.True, after.ErrorMessage);
            Assert.That(
                after.Output.files[0].introducedTypes[0].declarationFingerprint,
                Is.Not.EqualTo(before.Output.files[0].introducedTypes[0].declarationFingerprint));
        }

        private static string CreateSourceDirectory(string name)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, "Library", "UloopHotReload", "TestSources", "IntroducedTypes", name);
            Directory.CreateDirectory(directory);
            return directory;
        }

        /// <summary>
        /// Verifies that no introduced type is reported when the target assembly cannot be read,
        /// because every declaration would otherwise look absent from it and be treated as new.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_UnreadableTargetAssembly_ReportsNoIntroducedType()
        {
            string directory = CreateSourceDirectory("UnreadableTarget");
            string sourcePath = Path.Combine(directory, "Edited.cs");
            string targetAssemblyPath = Path.Combine(directory, "Unreadable.dll");
            File.WriteAllText(targetAssemblyPath, "this is not an assembly");
            File.WriteAllText(sourcePath, "namespace Example { public class Introduced { } }");

            TransformWorkerClientResult result = await TransformWorkerClient.RunAsync(
                CreateConstDriftInput(sourcePath, targetAssemblyPath, Guid.NewGuid().ToString()),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.files[0].introducedTypes, Is.Empty);
            Assert.That(
                result.Output.files[0].introducedTypeDiagnostics,
                Has.Some.Contains("target assembly"));
        }

        private static TransformWorkerInputDto CreateInput(string firstSourcePath, string secondSourcePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                TestAssemblyName + ".dll");
            Assert.That(File.Exists(targetDllPath), Is.True, "Test assembly DLL must exist.");
            UnityEditor.Compilation.Assembly compilationAssembly = FindCompilationAssembly();
            return new TransformWorkerInputDto
            {
                operation = "prepareIntroducedTypes",
                sources = new[]
                {
                    new TransformWorkerSourceDto
                    {
                        sourcePath = firstSourcePath,
                        projectRelativePath = "Assets/First.cs"
                    },
                    new TransformWorkerSourceDto
                    {
                        sourcePath = secondSourcePath,
                        projectRelativePath = "Assets/Second.cs"
                    }
                },
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = BuildAbsoluteReferencePaths(compilationAssembly.allReferences, targetDllPath),
                targetTypesAssemblyPath = targetDllPath,
                targetAssemblyName = TestAssemblyName,
                targetAssemblyMvid = typeof(TransformWorkerIntroducedTypeTests).Assembly.ManifestModule.ModuleVersionId.ToString(),
                assemblySourcePaths = Array.Empty<string>(),
                changedSiblingSourcePaths = Array.Empty<string>()
            };
        }

        private static TransformWorkerInputDto CreateConstDriftInput(
            string sourcePath,
            string targetAssemblyPath,
            string targetAssemblyMvid)
        {
            UnityEditor.Compilation.Assembly compilationAssembly = FindCompilationAssembly();
            return new TransformWorkerInputDto
            {
                operation = "prepareIntroducedTypes",
                sources = new[]
                {
                    new TransformWorkerSourceDto
                    {
                        sourcePath = sourcePath,
                        projectRelativePath = "Assets/Edited.cs"
                    }
                },
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = BuildAbsoluteReferencePaths(compilationAssembly.allReferences, targetAssemblyPath),
                targetTypesAssemblyPath = targetAssemblyPath,
                targetAssemblyName = "ConstDriftTarget",
                targetAssemblyMvid = targetAssemblyMvid,
                assemblySourcePaths = Array.Empty<string>(),
                changedSiblingSourcePaths = Array.Empty<string>()
            };
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

        private static UnityEditor.Compilation.Assembly FindCompilationAssembly()
        {
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == TestAssemblyName)
                {
                    return assembly;
                }
            }

            Assert.Fail("Compilation assembly was not found.");
            return null;
        }

        private static List<HotReloadIntroducedTypeDescriptor> CreateDescriptors(
            TransformWorkerFileOutputDto[] files)
        {
            List<HotReloadIntroducedTypeDescriptor> descriptors = new List<HotReloadIntroducedTypeDescriptor>();
            foreach (TransformWorkerFileOutputDto file in files)
            {
                foreach (TransformWorkerIntroducedTypeDto introducedType in file.introducedTypes)
                {
                    descriptors.Add(
                        new HotReloadIntroducedTypeDescriptor(
                            introducedType.originalAssemblyName,
                            introducedType.originalAssemblyMvid,
                            introducedType.metadataName,
                            introducedType.ownerProjectRelativePath,
                            introducedType.declarationFingerprint,
                            introducedType.source));
                }
            }

            return descriptors;
        }

        private static string[] BuildAbsoluteReferencePaths(string[] allReferences, string targetDllPath)
        {
            List<string> paths = new List<string>();
            foreach (string reference in allReferences)
            {
                if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                {
                    paths.Add(Path.GetFullPath(reference));
                }
            }

            string targetPath = Path.GetFullPath(targetDllPath);
            if (!paths.Contains(targetPath))
            {
                paths.Add(targetPath);
            }

            return paths.ToArray();
        }
    }
}
