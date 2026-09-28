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

        // Why a line start rather than a space: the space after the namespace brace is trailing
        // trivia of that brace, so a declaration written on the same line starts its artifact
        // line, and a stray token in front of the rewritten modifiers would show up before it.
        private static readonly string LineStart = Environment.NewLine;

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

        /// <summary>
        /// Verifies that a public declaration reaches the artifact source exactly as it was written.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_PublicType_PreservesArtifactSource()
        {
            const string declaration = "public sealed class Plain { public int Read() => 1; }";
            TransformWorkerFileOutputDto output = await PrepareAsync(CreateInput(declaration));

            Assert.That(output.introducedTypes, Has.Length.EqualTo(1));
            Assert.That(output.introducedTypes[0].source, Does.Contain(LineStart + declaration + " "));
            Assert.That(output.introducedTypes[0].source, Does.Not.Contain("public public"));
        }

        /// <summary>
        /// Verifies that an internal or modifier-less declaration is made public in its artifact
        /// source while every other modifier keeps its token and its place.
        /// </summary>
        [TestCase("internal static class Ordered { }", "public static class Ordered { }")]
        [TestCase("static internal class Ordered { }", "static public class Ordered { }")]
        [TestCase("internal sealed class Ordered { }", "public sealed class Ordered { }")]
        [TestCase("abstract internal class Ordered { }", "abstract public class Ordered { }")]
        [TestCase("sealed class Ordered { }", "public sealed class Ordered { }")]
        [TestCase("static class Ordered { }", "public static class Ordered { }")]
        [TestCase("internal readonly struct Ordered { }", "public readonly struct Ordered { }")]
        [TestCase("readonly struct Ordered { }", "public readonly struct Ordered { }")]
        public async Task PrepareIntroducedTypes_ModifierOrder_PreservesOtherTokens(string declaration, string expected)
        {
            TransformWorkerFileOutputDto output = await PrepareAsync(CreateInput(declaration));

            Assert.That(output.introducedTypes, Has.Length.EqualTo(1), DescribeDiagnostics(output));
            Assert.That(output.introducedTypes[0].metadataName, Is.EqualTo(FixtureNamespace + ".Ordered"));
            Assert.That(output.introducedTypes[0].source, Does.Contain(LineStart + expected + " "));
            Assert.That(output.introducedTypes[0].source, Does.Not.Contain("internal"));
        }

        /// <summary>
        /// Verifies that promoting an attributed internal declaration keeps its documentation,
        /// attributes, comments and alias binding, with and without an enclosing namespace, and that
        /// the artifact compiled from it declares a public type.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task PrepareIntroducedTypes_AttributedInternalType_PreservesTriviaAndBinding(bool namespaced)
        {
            const string declaration =
                "    /// <summary>Kept documentation.</summary>\n"
                + "    [System.Obsolete(\"kept\")] /* between */ internal class Attributed\n"
                + "    {\n"
                + "        public Alias Create() { return null; }\n"
                + "    }\n";
            string source = "using Alias = System.IDisposable;\n"
                + (namespaced ? "namespace AttributedExample\n{\n" + declaration + "}\n" : declaration);
            TransformWorkerInputDto input = CreateInputFromSource(source);
            TransformWorkerClientResult prepared = await RunWorkerAsync(input);
            TransformWorkerFileOutputDto output = prepared.Output.files[0];

            Assert.That(output.introducedTypes, Has.Length.EqualTo(1), DescribeDiagnostics(output));
            Assert.That(
                output.introducedTypes[0].source,
                Does.Contain(declaration.Replace("internal class", "public class", StringComparison.Ordinal)));
            Assert.That(output.introducedTypes[0].source, Does.Contain("using Alias = System.IDisposable;"));
            HotReloadIntroducedTypeCompilerResult compiled = await CompileAsync(input, prepared.Output.files);
            Assert.That(compiled.Success, Is.True, compiled.ErrorMessage);
            Type attributed = compiled.Artifact.Assembly.GetType(
                (namespaced ? "AttributedExample." : string.Empty) + "Attributed", true);
            Assert.That(attributed.IsPublic, Is.True);
            Assert.That(attributed.GetMethod("Create").ReturnType, Is.EqualTo(typeof(IDisposable)));
        }

        /// <summary>
        /// Verifies that internal enums, interfaces and structs are introduced as public types.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_InternalTypeKinds_AreIntroduced()
        {
            TransformWorkerFileOutputDto output = await PrepareAsync(CreateInput(
                "internal enum HiddenKind { One } internal interface IHiddenShape { } "
                + "internal struct HiddenValue { }"));

            AssertPromoted(output,
                "HiddenKind", "public enum HiddenKind",
                "IHiddenShape", "public interface IHiddenShape",
                "HiddenValue", "public struct HiddenValue");
        }

        /// <summary>
        /// Verifies that enums, interfaces, structs and classes declared without any modifier get a
        /// public modifier in front of their keyword and are introduced.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ModifierlessTypeKinds_AreIntroduced()
        {
            TransformWorkerFileOutputDto output = await PrepareAsync(CreateInput(
                "enum ModifierlessKind { One } interface IModifierlessShape { } "
                + "struct ModifierlessValue { } class ModifierlessClass { }"));

            AssertPromoted(output,
                "ModifierlessKind", "public enum ModifierlessKind",
                "IModifierlessShape", "public interface IModifierlessShape",
                "ModifierlessValue", "public struct ModifierlessValue",
                "ModifierlessClass", "public class ModifierlessClass");
        }

        /// <summary>
        /// Verifies that a file-local declaration is never introduced: a compiler that knows the
        /// modifier gets the file-local reason, and an older compiler rejects the syntax before
        /// planning, which leaves nothing to introduce either.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_FileLocal_IsRejectedForTheAvailableLanguageVersion()
        {
            TransformWorkerClientResult result = await RunWorkerAsync(
                CreateInputFromSource("namespace Example { file class Local { } }"));
            TransformWorkerFileOutputDto output = result.Output.files[0];

            Assert.That(output.introducedTypes, Is.Empty);
            if (output.parseErrors.Length > 0)
            {
                // An Editor whose bundled compiler predates file-local types reads 'file' as a
                // stray identifier, so the file never reaches planning. This branch proves nothing
                // about the file-local reason itself.
                Assert.That(string.Join("\n", output.parseErrors), Does.Contain("CS0116"));
                Assert.That(output.introducedTypeDiagnostics, Is.Empty);
                return;
            }

            string[] reasons = HotReloadWorkerReasonTestText.RenderAll(output.introducedTypeDiagnostics);
            Assert.That(reasons, Has.Length.EqualTo(1));
            Assert.That(reasons[0], Does.StartWith("File-local introduced type requires a compile: "));
            Assert.That(reasons[0], Does.Contain("Local"));
        }

        /// <summary>
        /// Verifies that the declaration fingerprint follows the modifiers the user wrote: an internal
        /// declaration keeps a fingerprint of its own even though its artifact source says public.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ArtifactPromotion_DoesNotChangeSourceFingerprint()
        {
            TransformWorkerFileOutputDto internalOutput = await PrepareAsync(CreateInput("internal class Promoted { }"));
            TransformWorkerFileOutputDto publicOutput = await PrepareAsync(CreateInput("public class Promoted { }"));

            Assert.That(internalOutput.introducedTypes, Has.Length.EqualTo(1), DescribeDiagnostics(internalOutput));
            Assert.That(publicOutput.introducedTypes, Has.Length.EqualTo(1), DescribeDiagnostics(publicOutput));
            Assert.That(internalOutput.introducedTypes[0].source, Does.Contain(LineStart + "public class Promoted { } "));
            Assert.That(
                internalOutput.introducedTypes[0].declarationFingerprint,
                Is.Not.EqualTo(publicOutput.introducedTypes[0].declarationFingerprint));
        }

        /// <summary>
        /// Verifies that a modifier-less declaration deriving from an internal MonoBehaviour base is
        /// refused as a Unity object rather than introduced.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ModifierlessUnityObjectDerivedType_IsRefused()
        {
            TransformWorkerFileOutputDto output = await PrepareAsync(CreateInput(
                "class Introduced : HotReloadInternalMonoBehaviourBase { }"));

            Assert.That(output.introducedTypes, Is.Empty);
            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(output.introducedTypeDiagnostics),
                Is.EqualTo(new[] { "Unity object introduced type requires a compile: " + FixtureNamespace + ".Introduced" }));
        }

        /// <summary>
        /// Verifies that promoting an internal declaration keeps its line endings and its non-ASCII
        /// identifier, comment and string literal exactly as written.
        /// </summary>
        [TestCase("\n")]
        [TestCase("\r\n")]
        public async Task PrepareIntroducedTypes_InternalRewrite_PreservesLineEndingsAndUnicode(string newline)
        {
            string declaration = string.Join(newline,
                "    // café",
                "    internal sealed class Grüße",
                "    {",
                "        public string Read() => \"naïve\";",
                "    }") + newline;
            TransformWorkerFileOutputDto output = await PrepareAsync(CreateInputFromSource(
                "namespace UnicodeExample" + newline + "{" + newline + declaration + "}" + newline));

            Assert.That(output.introducedTypes, Has.Length.EqualTo(1), DescribeDiagnostics(output));
            Assert.That(output.introducedTypes[0].metadataName, Is.EqualTo("UnicodeExample.Grüße"));
            Assert.That(
                output.introducedTypes[0].source,
                Does.Contain(declaration.Replace("internal sealed", "public sealed", StringComparison.Ordinal)));
        }

        /// <summary>
        /// Verifies that only the access modifier the defines leave active is rewritten, that the
        /// artifact compiled with those defines declares a public type either way, and that the two
        /// define sets give the declaration different fingerprints.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ConditionalModifiers_UseActiveTokens()
        {
            const string declaration =
                "#if HOT_RELOAD_PUBLIC_BRANCH\n"
                + "    public\n"
                + "#else\n"
                + "    internal\n"
                + "#endif\n"
                + "    sealed class Switched { }\n";
            string source = "namespace SwitchedExample\n{\n" + declaration + "}\n";

            TransformWorkerIntroducedTypeDto publicBranch = await PrepareSwitchedAsync(source, true);
            TransformWorkerIntroducedTypeDto internalBranch = await PrepareSwitchedAsync(source, false);

            Assert.That(publicBranch.source, Does.Contain(declaration));
            Assert.That(
                internalBranch.source,
                Does.Contain(declaration.Replace("    internal\n", "    public\n", StringComparison.Ordinal)));
            Assert.That(internalBranch.declarationFingerprint, Is.Not.EqualTo(publicBranch.declarationFingerprint));
        }

        /// <summary>
        /// Verifies that an internal declaration of each unsupported shape is refused for its shape,
        /// now that its accessibility no longer refuses it first.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_InternalUnsupportedShapes_KeepSpecificReasons()
        {
            TransformWorkerFileOutputDto output = await PrepareAsync(CreateInputFromSource(
                "namespace System.Runtime.CompilerServices { internal sealed class ModuleInitializerAttribute : System.Attribute { } } "
                + "namespace ShapeExample { "
                + "internal class Generic<T> { } "
                + "internal partial class Partial { } "
                + "internal record Point(int X); "
                + "internal ref struct RefLike { } "
                + "internal unsafe class UnsafeType { public int* Value; } "
                + "internal class ObjectType : UnityEngine.Object { } "
                + "[System.Serializable] internal class SerializableType { } "
                + "internal static class InitializerType { [System.Runtime.CompilerServices.ModuleInitializer] internal static void Initialize() { } } "
                + "internal class Outer { public class Nested { } } "
                + "internal delegate void AddedDelegate(); }"));

            Assert.That(
                MetadataNames(output),
                Is.EqualTo(new[] { "System.Runtime.CompilerServices.ModuleInitializerAttribute" }));
            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(output.introducedTypeDiagnostics),
                Is.EquivalentTo(new[]
                {
                    "Generic introduced type requires a compile: ShapeExample.Generic`1",
                    "Partial introduced type requires a compile: ShapeExample.Partial",
                    "Record introduced type requires a compile: ShapeExample.Point",
                    "Ref-like introduced type requires a compile: ShapeExample.RefLike",
                    "Unsafe introduced type requires a compile: ShapeExample.UnsafeType",
                    "Unity object introduced type requires a compile: ShapeExample.ObjectType",
                    "Serializable introduced type requires a compile: ShapeExample.SerializableType",
                    "Module initializer introduced type requires a compile: ShapeExample.InitializerType",
                    "Nested declaration inside an introduced type requires a compile: ShapeExample.Outer/Nested",
                    "Delegate introduced type requires a compile: ShapeExample.AddedDelegate"
                }));
        }

        private static async Task<TransformWorkerIntroducedTypeDto> PrepareSwitchedAsync(string source, bool publicBranch)
        {
            TransformWorkerInputDto input = CreateInputFromSource(source);
            if (publicBranch)
            {
                List<string> defines = new List<string>(input.defines) { "HOT_RELOAD_PUBLIC_BRANCH" };
                input.defines = defines.ToArray();
            }

            TransformWorkerClientResult prepared = await RunWorkerAsync(input);
            TransformWorkerFileOutputDto output = prepared.Output.files[0];
            Assert.That(output.parseErrors, Is.Empty);
            Assert.That(output.introducedTypes, Has.Length.EqualTo(1), DescribeDiagnostics(output));
            HotReloadIntroducedTypeCompilerResult compiled = await CompileAsync(input, prepared.Output.files);
            Assert.That(compiled.Success, Is.True, compiled.ErrorMessage);
            Assert.That(compiled.Artifact.Assembly.GetType("SwitchedExample.Switched", true).IsPublic, Is.True);
            return output.introducedTypes[0];
        }

        // Pairs of a simple name and the declaration head its artifact source must carry.
        private static void AssertPromoted(TransformWorkerFileOutputDto output, params string[] namesAndHeads)
        {
            Assert.That(output.introducedTypeDiagnostics, Is.Empty, DescribeDiagnostics(output));
            Assert.That(output.introducedTypes, Has.Length.EqualTo(namesAndHeads.Length / 2));
            for (int index = 0; index < namesAndHeads.Length; index += 2)
            {
                TransformWorkerIntroducedTypeDto introduced = FindIntroduced(output, namesAndHeads[index]);
                Assert.That(introduced.source, Does.Contain(LineStart + namesAndHeads[index + 1] + " "));
                Assert.That(introduced.source, Does.Not.Contain("internal"));
            }
        }

        private static TransformWorkerIntroducedTypeDto FindIntroduced(TransformWorkerFileOutputDto output, string simpleName)
        {
            foreach (TransformWorkerIntroducedTypeDto introduced in output.introducedTypes)
            {
                if (introduced.metadataName == FixtureNamespace + "." + simpleName)
                {
                    return introduced;
                }
            }

            Assert.Fail(simpleName + " must be introduced.");
            return null;
        }

        private static string[] MetadataNames(TransformWorkerFileOutputDto output)
        {
            string[] names = new string[output.introducedTypes.Length];
            for (int index = 0; index < names.Length; index++)
            {
                names[index] = output.introducedTypes[index].metadataName;
            }

            return names;
        }

        private static string DescribeDiagnostics(TransformWorkerFileOutputDto output)
        {
            return string.Join("\n", HotReloadWorkerReasonTestText.RenderAll(output.introducedTypeDiagnostics));
        }

        // Writes the source as it is, for cases that need their own namespaces, directives or
        // line endings rather than the declarations wrapped in the fixture namespace.
        private static TransformWorkerInputDto CreateInputFromSource(string source)
        {
            string directory = TransformWorkerIntroducedTypeTestInputs.CreateSourceDirectory(
                "Accessibility-" + Guid.NewGuid().ToString("N"));
            string sourcePath = Path.Combine(directory, "Introduced.cs");
            string emptyPath = Path.Combine(directory, "Empty.cs");
            File.WriteAllText(sourcePath, source);
            File.WriteAllText(emptyPath, string.Empty);
            return TransformWorkerIntroducedTypeTestInputs.CreateInput(sourcePath, emptyPath);
        }

        private static async Task<TransformWorkerClientResult> RunWorkerAsync(TransformWorkerInputDto input)
        {
            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                input, CancellationToken.None);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
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
            return new HotReloadIntroducedTypeCompiler(
                    new HotReloadRoslynCompilerEnvironment(), new FakeInternalAccessGrant(isAvailable: false))
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
