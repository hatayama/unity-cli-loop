using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Mono.Cecil;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies internals-only copies preserve language access boundaries and cache isolation.
    /// </summary>
    public sealed class InternalsExposedReferenceTests
    {
        /// <summary>Verifies type visibility loses only its assembly restriction.</summary>
        [TestCase(TypeAttributes.NotPublic, TypeAttributes.Public)]
        [TestCase(TypeAttributes.Public, TypeAttributes.Public)]
        [TestCase(TypeAttributes.NestedPublic, TypeAttributes.NestedPublic)]
        [TestCase(TypeAttributes.NestedPrivate, TypeAttributes.NestedPrivate)]
        [TestCase(TypeAttributes.NestedFamily, TypeAttributes.NestedFamily)]
        [TestCase(TypeAttributes.NestedAssembly, TypeAttributes.NestedPublic)]
        [TestCase(TypeAttributes.NestedFamORAssem, TypeAttributes.NestedPublic)]
        [TestCase(TypeAttributes.NestedFamANDAssem, TypeAttributes.NestedFamily)]
        public void ExposeInternals_TypeVisibility_ChangesOnlyAssemblyRestriction(
            TypeAttributes visibility, TypeAttributes expected)
        {
            using InternalsExposureTestImage fixture = new InternalsExposureTestImage(root =>
            {
                if (visibility == TypeAttributes.NotPublic || visibility == TypeAttributes.Public)
                {
                    root.Attributes = visibility | TypeAttributes.BeforeFieldInit | TypeAttributes.Serializable;
                    return;
                }

                root.NestedTypes.Add(new TypeDefinition("", "Nested",
                    visibility | TypeAttributes.BeforeFieldInit | TypeAttributes.Serializable,
                    root.Module.TypeSystem.Object));
            });
            string copyPath = Expose(fixture);
            using AssemblyDefinition copy = AssemblyDefinition.ReadAssembly(copyPath);
            TypeDefinition type = copy.MainModule.GetTypes().Single(candidate =>
                candidate.Name == (visibility < TypeAttributes.NestedPublic ? "Candidate" : "Nested"));
            Assert.That(type.Attributes, Is.EqualTo(expected | TypeAttributes.BeforeFieldInit | TypeAttributes.Serializable));
            Assert.That(copy.MainModule.Mvid, Is.EqualTo(fixture.Definition.MainModule.Mvid));
        }

        /// <summary>Verifies fields, methods, and accessors keep every non-access flag.</summary>
        [TestCase(FieldAttributes.CompilerControlled, FieldAttributes.CompilerControlled)]
        [TestCase(FieldAttributes.Private, FieldAttributes.Private)]
        [TestCase(FieldAttributes.Family, FieldAttributes.Family)]
        [TestCase(FieldAttributes.Public, FieldAttributes.Public)]
        [TestCase(FieldAttributes.Assembly, FieldAttributes.Public)]
        [TestCase(FieldAttributes.FamORAssem, FieldAttributes.Public)]
        [TestCase(FieldAttributes.FamANDAssem, FieldAttributes.Family)]
        public void ExposeInternals_MemberVisibility_ChangesOnlyAssemblyRestriction(
            FieldAttributes visibility, FieldAttributes expected)
        {
            using InternalsExposureTestImage fixture = new InternalsExposureTestImage(type =>
            {
                type.Fields.Add(new FieldDefinition("Value", visibility | FieldAttributes.Static | FieldAttributes.InitOnly,
                    type.Module.TypeSystem.Int32));
                InternalsExposureTestImage.AddReadMethod(type, "Read", (MethodAttributes)visibility);
                MethodDefinition getter = InternalsExposureTestImage.AddReadMethod(type, "get_Number", (MethodAttributes)visibility);
                getter.IsSpecialName = true;
                type.Properties.Add(new PropertyDefinition("Number", PropertyAttributes.None, type.Module.TypeSystem.Int32)
                {
                    GetMethod = getter
                });
            });
            using AssemblyDefinition copy = AssemblyDefinition.ReadAssembly(Expose(fixture));
            TypeDefinition candidate = copy.MainModule.GetType("Candidate");
            Assert.That(candidate.Fields.Single().Attributes,
                Is.EqualTo(expected | FieldAttributes.Static | FieldAttributes.InitOnly));
            Assert.That(candidate.Methods.Single(method => method.Name == "Read").Attributes,
                Is.EqualTo((MethodAttributes)expected | MethodAttributes.Static | MethodAttributes.HideBySig));
            Assert.That(candidate.Properties.Single().GetMethod.Attributes,
                Is.EqualTo((MethodAttributes)expected | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName));
        }

        /// <summary>Verifies module metadata is not publicized by an internals-only rewrite.</summary>
        [Test]
        public void ExposeInternals_PreservesModuleAndNonVisibilityFlags()
        {
            using InternalsExposureTestImage fixture = new InternalsExposureTestImage();
            using AssemblyDefinition copy = AssemblyDefinition.ReadAssembly(Expose(fixture));
            Assert.That(copy.MainModule.Types[0].Name, Is.EqualTo("<Module>"));
            Assert.That(copy.MainModule.Types[0].Attributes, Is.EqualTo(fixture.Definition.MainModule.Types[0].Attributes));
            Assert.That(copy.Name.FullName, Is.EqualTo(fixture.Definition.Name.FullName));
        }

        /// <summary>Verifies field-like events expose accessors without exposing private storage.</summary>
        [Test]
        public void ExposeInternals_EventAccessorsAreVisibleAndBackingFieldStaysPrivate()
        {
            using InternalsExposureTestImage fixture = new InternalsExposureTestImage(type =>
            {
                TypeReference action = type.Module.ImportReference(typeof(Action));
                type.Fields.Add(new FieldDefinition("Changed", FieldAttributes.Private | FieldAttributes.Static, action));
                MethodDefinition add = AddEventAccessor(type, "add_Changed", action);
                MethodDefinition remove = AddEventAccessor(type, "remove_Changed", action);
                type.Events.Add(new EventDefinition("Changed", EventAttributes.None, action)
                {
                    AddMethod = add, RemoveMethod = remove
                });
            });
            using AssemblyDefinition copy = AssemblyDefinition.ReadAssembly(Expose(fixture));
            TypeDefinition candidate = copy.MainModule.GetType("Candidate");
            Assert.That(candidate.Fields.Single().IsPrivate, Is.True);
            Assert.That(candidate.Events.Single().AddMethod.IsPublic, Is.True);
            Assert.That(candidate.Events.Single().RemoveMethod.IsPublic, Is.True);
        }

        /// <summary>Verifies MVID cache hits, zero-byte recovery, and precise stale-cache deletion.</summary>
        [Test]
        public void ExposeInternals_CacheUsesMvidAndRegeneratesZeroLengthFile()
        {
            using InternalsExposureTestImage fixture = new InternalsExposureTestImage();
            string first = Expose(fixture);
            Assert.That(first.Replace('\\', '/'), Does.Contain("/InternalsExposedRefs/fmt1/"));
            DateTime marker = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(first, marker);
            Assert.That(Expose(fixture), Is.EqualTo(first));
            Assert.That(File.GetLastWriteTimeUtc(first), Is.EqualTo(marker));
            File.WriteAllBytes(first, Array.Empty<byte>());
            Assert.That(Expose(fixture), Is.EqualTo(first));
            Assert.That(new FileInfo(first).Length, Is.GreaterThan(0));

            string sibling = Path.Combine(Path.GetDirectoryName(first),
                fixture.Definition.Name.Name + "-Sibling-" + Guid.NewGuid().ToString("N") + ".dll");
            File.WriteAllBytes(sibling, new byte[] { 1 });
            fixture.Definition.MainModule.Mvid = Guid.NewGuid();
            fixture.Definition.Write(fixture.DllPath);
            string second = Expose(fixture);
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(File.Exists(first), Is.False);
            Assert.That(File.Exists(sibling), Is.True);
        }

        /// <summary>Verifies fully publicized copies cannot poison the internals-only cache.</summary>
        [Test]
        public void ExposeInternals_DoesNotReuseFullyPublicizedCache()
        {
            using InternalsExposureTestImage fixture = new InternalsExposureTestImage(type =>
                InternalsExposureTestImage.AddReadMethod(type, "Secret", MethodAttributes.Private));
            string publicized = ReferencePublicizer.GetOrCreatePublicizedCopy(fixture.Home, SearchDirectories());
            string exposed = Expose(fixture);
            Assert.That(exposed, Is.Not.EqualTo(publicized));
            using AssemblyDefinition fullCopy = AssemblyDefinition.ReadAssembly(publicized);
            using AssemblyDefinition internalCopy = AssemblyDefinition.ReadAssembly(exposed);
            Assert.That(fullCopy.MainModule.GetType("Candidate").Methods.Single(method => method.Name == "Secret").IsPublic, Is.True);
            Assert.That(internalCopy.MainModule.GetType("Candidate").Methods.Single(method => method.Name == "Secret").IsPrivate, Is.True);
        }

        /// <summary>Verifies private-protected members become usable only through inheritance.</summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task ExposeInternals_PrivateProtectedMember_PreservesInheritanceRequirement(bool derived)
        {
            using InternalsExposureTestImage fixture = new InternalsExposureTestImage(type =>
                InternalsExposureTestImage.AddReadMethod(type, "Read", MethodAttributes.FamANDAssem));
            string source = derived
                ? "public class Consumer : Candidate { public int Call() => Read(); }"
                : "public class Consumer { public int Call() => Candidate.Read(); }";
            DynamicCompilationBackendResult result = await CompileConsumerAsync(fixture, Expose(fixture), source);
            bool hasErrors = result.CompilerMessages.Any(message => message.type == CompilerMessageType.Error);
            Assert.That(hasErrors, Is.EqualTo(!derived), string.Join("\n", result.CompilerMessages.Select(message => message.message)));
        }

        private static IReadOnlyCollection<string> SearchDirectories()
        {
            return new[] { Path.GetDirectoryName(typeof(object).Assembly.Location) };
        }

        private static string Expose(InternalsExposureTestImage fixture)
        {
            return ReferencePublicizer.GetOrCreateInternalsExposedCopy(fixture.Home, SearchDirectories());
        }

        private static MethodDefinition AddEventAccessor(TypeDefinition type, string name, TypeReference action)
        {
            MethodDefinition accessor = new MethodDefinition(name,
                MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.SpecialName,
                type.Module.TypeSystem.Void);
            accessor.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, action));
            accessor.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ret));
            type.Methods.Add(accessor);
            return accessor;
        }

        private static async Task<DynamicCompilationBackendResult> CompileConsumerAsync(
            InternalsExposureTestImage fixture, string reference, string source)
        {
            string directory = Path.GetDirectoryName(fixture.DllPath);
            string sourcePath = Path.Combine(directory, "Consumer.cs");
            File.WriteAllText(sourcePath, source);
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            return await RoslynCompilerBackend.CompileAsync(sourcePath, Path.Combine(directory, "Consumer.dll"),
                new List<string> { typeof(object).Assembly.Location, reference }, ExternalCompilerPathResolver.Resolve(),
                new RoslynCompilerOptions(new List<string>(), false, emitDebugCode: false), cancellation.Token,
                () => { }, () => { }, () => { });
        }
    }
}
