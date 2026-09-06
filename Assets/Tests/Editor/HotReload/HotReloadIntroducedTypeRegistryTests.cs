using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

using NUnit.Framework;

using Mono.Cecil;

using CecilFieldAttributes = Mono.Cecil.FieldAttributes;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers prepared and active lifecycle isolation for introduced type artifacts.
    /// </summary>
    public class HotReloadIntroducedTypeRegistryTests
    {
        /// <summary>
        /// Verifies that a prepared artifact stays invisible until activation and remains visible
        /// after its temporary resolver scope closes.
        /// </summary>
        [Test]
        public void PreparedArtifact_ActivatesOnlyAtExplicitCommit()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact("one");
            HotReloadIntroducedTypeAssemblyResolver resolver =
                new HotReloadIntroducedTypeAssemblyResolver(registry);

            registry.RegisterPrepared(artifact);
            using (resolver.RegisterPrepared(artifact))
            {
                Assert.That(registry.PreparedCount, Is.EqualTo(1));
                Assert.That(registry.ActiveCount, Is.EqualTo(0));
                Assert.That(resolver.ResolveExact(artifact.AssemblyFullName), Is.EqualTo(artifact.Assembly));
                registry.Activate(artifact);
            }

            Assert.That(registry.PreparedCount, Is.EqualTo(0));
            Assert.That(registry.ActiveCount, Is.EqualTo(1));
            Assert.That(resolver.ResolveExact(artifact.AssemblyFullName), Is.EqualTo(artifact.Assembly));
            resolver.Dispose();
        }

        /// <summary>
        /// Verifies that same-name assembly requests do not resolve unless their full identity is exact.
        /// </summary>
        [Test]
        public void ResolveExact_DifferentAssemblyIdentity_ReturnsNull()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact("one");
            HotReloadIntroducedTypeAssemblyResolver resolver =
                new HotReloadIntroducedTypeAssemblyResolver(registry);
            registry.RegisterPrepared(artifact);
            registry.Activate(artifact);

            Assert.That(resolver.ResolveExact(artifact.AssemblyFullName + ".different"), Is.Null);
            resolver.Dispose();
        }

        /// <summary>
        /// Verifies that an unchanged active definition is reused instead of becoming a new artifact.
        /// </summary>
        [Test]
        public void TryFindActive_SameDefinition_ReturnsExistingArtifact()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact("same");
            registry.RegisterPrepared(artifact);
            registry.Activate(artifact);
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor> { CreateDescriptor("same") };

            bool found = registry.TryFindActive(descriptors, out HotReloadIntroducedTypeArtifact foundArtifact);

            Assert.That(found, Is.True);
            Assert.That(foundArtifact, Is.SameAs(artifact));
        }

        /// <summary>
        /// Verifies that moving an otherwise identical declaration to another source owner does
        /// not reuse the artifact that belongs to the original owner.
        /// </summary>
        [Test]
        public void TryFindActive_OwnerChanged_DoesNotReuseArtifact()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact("same");
            registry.RegisterPrepared(artifact);
            registry.Activate(artifact);
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    new HotReloadIntroducedTypeDescriptor(
                        "OriginalAssembly",
                        "original-mvid",
                        "Example.Introduced",
                        "Assets/Moved.cs",
                        "same",
                        "public class Introduced { }")
                };

            bool found = registry.TryFindActive(descriptors, out HotReloadIntroducedTypeArtifact foundArtifact);

            Assert.That(found, Is.False);
            Assert.That(foundArtifact, Is.Null);
        }

        /// <summary>
        /// Verifies that an activation conflict preserves the prepared candidate and existing
        /// active identity without publishing a partial artifact.
        /// </summary>
        [Test]
        public void Activate_ConflictingAssemblyIdentity_PreservesPreparedAndActiveState()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            HotReloadIntroducedTypeArtifact active = CreateArtifact("active");
            HotReloadIntroducedTypeArtifact candidate = new HotReloadIntroducedTypeArtifact(
                active.Assembly,
                "candidate.dll",
                "candidate.pdb",
                new List<HotReloadIntroducedTypeDescriptor> { CreateOtherDescriptor("candidate") });
            registry.RegisterPrepared(active);
            registry.Activate(active);
            registry.RegisterPrepared(candidate);

            Assert.Throws<InvalidOperationException>(() => registry.Activate(candidate));

            Assert.That(registry.ActiveCount, Is.EqualTo(1));
            Assert.That(registry.PreparedCount, Is.EqualTo(1));
            Assert.That(registry.TryResolveActiveAssembly(active.AssemblyFullName, out HotReloadIntroducedTypeArtifact resolved), Is.True);
            Assert.That(resolved, Is.SameAs(active));
        }

        /// <summary>
        /// Verifies that an active type can be reused from a request that also contains a new
        /// type, independently of the request ordering.
        /// </summary>
        [Test]
        public void TryFindActive_MixedRequest_ReturnsExistingTypeArtifact()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact("same");
            registry.RegisterPrepared(artifact);
            registry.Activate(artifact);
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    CreateOtherDescriptor("new"),
                    CreateDescriptor("same")
                };

            bool found = registry.TryFindActiveDescriptor(descriptors[1], out HotReloadIntroducedTypeArtifact foundArtifact);

            Assert.That(found, Is.True);
            Assert.That(foundArtifact, Is.SameAs(artifact));
            Assert.That(registry.TryFindActive(descriptors, out HotReloadIntroducedTypeArtifact wholeArtifact), Is.False);
            Assert.That(wholeArtifact, Is.Null);
        }

        /// <summary>
        /// Verifies that the AppDomain resolver returns one active artifact assembly for field,
        /// base, interface, and method-signature references in a separately loaded real DLL.
        /// </summary>
        [Test]
        public void AssemblyResolve_ActiveArtifact_ResolvesAllReferencedTypeShapes()
        {
            string directory = Path.Combine(
                Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")),
                "Library",
                "UloopHotReload",
                "ResolverFixtures",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                RunResolvedTypeShapeAssertions(directory);
            }
            finally
            {
                DeleteFixtureDirectory(directory);
            }
        }

        // The fixture DLLs are loaded from bytes rather than from disk, so the directory can be
        // removed even though the assemblies stay loaded for the rest of the session.
        private static void DeleteFixtureDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException exception)
            {
                // A leftover fixture directory must not turn into a test failure of its own.
                UnityEngine.Debug.Log("Resolver fixture directory could not be removed: " + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                UnityEngine.Debug.Log("Resolver fixture directory could not be removed: " + exception.Message);
            }
        }

        private static void RunResolvedTypeShapeAssertions(string directory)
        {
            string dependencyName = "ResolverDependency" + Guid.NewGuid().ToString("N");
            string consumerName = "ResolverConsumer" + Guid.NewGuid().ToString("N");
            string dependencyPath = Path.Combine(directory, dependencyName + ".dll");
            string consumerPath = Path.Combine(directory, consumerName + ".dll");
            WriteDependencyAssembly(dependencyPath, dependencyName);
            WriteConsumerAssembly(consumerPath, consumerName, dependencyName);
            Assembly dependencyAssembly = Assembly.Load(File.ReadAllBytes(dependencyPath));
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                dependencyAssembly,
                dependencyPath,
                Path.ChangeExtension(dependencyPath, ".pdb"),
                new List<HotReloadIntroducedTypeDescriptor> { CreateDescriptor("dependency") });
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            registry.RegisterPrepared(artifact);
            registry.Activate(artifact);

            HotReloadIntroducedTypeAssemblyResolver resolver =
                new HotReloadIntroducedTypeAssemblyResolver(registry);
            using (resolver)
            {
                Assembly consumerAssembly = Assembly.Load(File.ReadAllBytes(consumerPath));
                Type sharedType = dependencyAssembly.GetType("ResolverFixture.Shared");
                Type baseType = dependencyAssembly.GetType("ResolverFixture.Base");
                Type interfaceType = dependencyAssembly.GetType("ResolverFixture.Contract");
                Type fieldType = consumerAssembly.GetType("ResolverFixture.FieldHolder").GetField("Value").FieldType;
                Type derivedBaseType = consumerAssembly.GetType("ResolverFixture.Derived").BaseType;
                Type implementedInterface = consumerAssembly.GetType("ResolverFixture.Implementation").GetInterfaces()[0];
                Type returnType = consumerAssembly.GetType("ResolverFixture.MethodHolder").GetMethod("Create").ReturnType;

                Assert.That(fieldType, Is.SameAs(sharedType));
                Assert.That(derivedBaseType, Is.SameAs(baseType));
                Assert.That(implementedInterface, Is.SameAs(interfaceType));
                Assert.That(returnType, Is.SameAs(sharedType));
                int beforeUnknownRequest = resolver.ResolutionCount;
                Assert.Throws<FileNotFoundException>(() => Assembly.Load(
                    new AssemblyName("MissingResolverDependency" + Guid.NewGuid().ToString("N")
                        + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")));
                Assert.That(resolver.ResolutionCount, Is.GreaterThan(beforeUnknownRequest));
            }

            int countAfterDispose = resolver.ResolutionCount;
            Assert.Throws<FileNotFoundException>(() => Assembly.Load(
                new AssemblyName("MissingResolverDependency" + Guid.NewGuid().ToString("N")
                    + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")));
            Assert.That(resolver.ResolutionCount, Is.EqualTo(countAfterDispose));
        }

        /// <summary>
        /// Verifies that an artifact retains an immutable descriptor snapshot when the caller
        /// changes the list used to prepare it.
        /// </summary>
        [Test]
        public void Artifact_DescriptorInputChanges_RetainsImmutableSnapshot()
        {
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor> { CreateDescriptor("original") };
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                typeof(HotReloadIntroducedTypeRegistryTests).Assembly,
                "artifact.dll",
                "artifact.pdb",
                descriptors);

            descriptors.Clear();

            Assert.That(artifact.Descriptors, Has.Count.EqualTo(1));
            Assert.That(artifact.Descriptors[0].DeclarationFingerprint, Is.EqualTo("original"));
        }

        /// <summary>
        /// Verifies that discarding a failed prepared candidate does not remove an independently
        /// active artifact from the resolver or registry.
        /// </summary>
        [Test]
        public void DiscardPrepared_FailedCandidate_PreservesActiveArtifact()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            HotReloadIntroducedTypeArtifact active = CreateArtifact("active");
            HotReloadIntroducedTypeArtifact candidate = CreateArtifactWithAssembly(
                CreateDynamicAssembly("DiscardCandidate"),
                CreateOtherDescriptor("candidate"));
            registry.RegisterPrepared(active);
            registry.Activate(active);
            registry.RegisterPrepared(candidate);

            using (HotReloadIntroducedTypeAssemblyResolver resolver =
                new HotReloadIntroducedTypeAssemblyResolver(registry))
            {
                using (resolver.RegisterPrepared(candidate))
                {
                    registry.DiscardPrepared(candidate);
                }

                Assert.That(registry.ActiveCount, Is.EqualTo(1));
                Assert.That(registry.PreparedCount, Is.EqualTo(0));
                Assert.That(resolver.ResolveExact(active.AssemblyFullName), Is.SameAs(active.Assembly));
            }
        }

        /// <summary>
        /// Verifies that an artifact rejects missing assembly and descriptor inputs before it can
        /// enter either the prepared or active registry state.
        /// </summary>
        [Test]
        public void Artifact_RequiredInputsMissing_ThrowsBeforeConstruction()
        {
            List<HotReloadIntroducedTypeDescriptor> validDescriptors =
                new List<HotReloadIntroducedTypeDescriptor> { CreateDescriptor("valid") };

            Assert.Throws<ArgumentNullException>(() => new HotReloadIntroducedTypeArtifact(
                null,
                "artifact.dll",
                "artifact.pdb",
                validDescriptors));
            Assert.Throws<ArgumentNullException>(() => new HotReloadIntroducedTypeArtifact(
                typeof(HotReloadIntroducedTypeRegistryTests).Assembly,
                "artifact.dll",
                "artifact.pdb",
                null));
            Assert.Throws<ArgumentException>(() => new HotReloadIntroducedTypeArtifact(
                typeof(HotReloadIntroducedTypeRegistryTests).Assembly,
                "artifact.dll",
                "artifact.pdb",
                new List<HotReloadIntroducedTypeDescriptor>()));
            Assert.Throws<ArgumentException>(() => new HotReloadIntroducedTypeArtifact(
                typeof(HotReloadIntroducedTypeRegistryTests).Assembly,
                "artifact.dll",
                "artifact.pdb",
                new List<HotReloadIntroducedTypeDescriptor> { null }));
        }

        /// <summary>
        /// Verifies that a type identity conflict in a different artifact assembly leaves the
        /// candidate prepared and every existing active mapping unchanged.
        /// </summary>
        [Test]
        public void Activate_TypeIdentityConflictAcrossAssemblies_PreservesState()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            HotReloadIntroducedTypeArtifact active = CreateArtifactWithAssembly(
                CreateDynamicAssembly("ActiveArtifact"),
                CreateDescriptor("active"));
            HotReloadIntroducedTypeArtifact candidate = CreateArtifactWithAssembly(
                CreateDynamicAssembly("CandidateArtifact"),
                CreateDescriptor("candidate"));
            registry.RegisterPrepared(active);
            registry.Activate(active);
            registry.RegisterPrepared(candidate);

            Assert.Throws<InvalidOperationException>(() => registry.Activate(candidate));

            Assert.That(registry.ActiveCount, Is.EqualTo(1));
            Assert.That(registry.PreparedCount, Is.EqualTo(1));
            Assert.That(registry.TryResolveActiveAssembly(active.AssemblyFullName, out HotReloadIntroducedTypeArtifact resolved), Is.True);
            Assert.That(resolved, Is.SameAs(active));
            Assert.That(registry.TryResolveActiveAssembly(candidate.AssemblyFullName, out HotReloadIntroducedTypeArtifact unexpected), Is.False);
            Assert.That(unexpected, Is.Null);
        }

        /// <summary>
        /// Verifies that activation publishes every descriptor mapping and removes the prepared
        /// membership only after the complete artifact becomes active.
        /// </summary>
        [Test]
        public void Activate_MultipleDescriptors_PublishesEveryTypeAndRemovesPrepared()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    CreateDescriptor("first"),
                    CreateOtherDescriptor("second")
                };
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                typeof(HotReloadIntroducedTypeRegistryTests).Assembly,
                "artifact.dll",
                "artifact.pdb",
                descriptors);
            registry.RegisterPrepared(artifact);

            registry.Activate(artifact);

            Assert.That(registry.PreparedCount, Is.EqualTo(0));
            Assert.That(registry.ActiveCount, Is.EqualTo(1));
            Assert.That(registry.TryFindActiveDescriptor(descriptors[0], out HotReloadIntroducedTypeArtifact first), Is.True);
            Assert.That(registry.TryFindActiveDescriptor(descriptors[1], out HotReloadIntroducedTypeArtifact second), Is.True);
            Assert.That(first, Is.SameAs(artifact));
            Assert.That(second, Is.SameAs(artifact));
        }

        /// <summary>
        /// Verifies that a duplicate descriptor request cannot match an active artifact whose
        /// same-sized definition set contains a distinct second type.
        /// </summary>
        [Test]
        public void TryFindActive_DuplicateRequest_DoesNotMatchDistinctDefinitionSet()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            List<HotReloadIntroducedTypeDescriptor> activeDescriptors =
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    CreateDescriptor("first"),
                    CreateOtherDescriptor("second")
                };
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                typeof(HotReloadIntroducedTypeRegistryTests).Assembly,
                "artifact.dll",
                "artifact.pdb",
                activeDescriptors);
            registry.RegisterPrepared(artifact);
            registry.Activate(artifact);
            List<HotReloadIntroducedTypeDescriptor> duplicateRequest =
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    CreateDescriptor("first"),
                    CreateDescriptor("first")
                };

            bool found = registry.TryFindActive(duplicateRequest, out HotReloadIntroducedTypeArtifact foundArtifact);

            Assert.That(found, Is.False);
            Assert.That(foundArtifact, Is.Null);
        }

        /// <summary>
        /// Verifies that an already active artifact cannot return to Prepared and leaves both
        /// lifecycle counts and its existing active mapping unchanged.
        /// </summary>
        [Test]
        public void RegisterPrepared_AlreadyActiveArtifact_RejectsWithoutStateChange()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact("active");
            registry.RegisterPrepared(artifact);
            registry.Activate(artifact);

            Assert.Throws<InvalidOperationException>(() => registry.RegisterPrepared(artifact));

            Assert.That(registry.PreparedCount, Is.EqualTo(0));
            Assert.That(registry.ActiveCount, Is.EqualTo(1));
            Assert.That(registry.TryResolveActiveAssembly(artifact.AssemblyFullName, out HotReloadIntroducedTypeArtifact resolved), Is.True);
            Assert.That(resolved, Is.SameAs(artifact));
        }

        /// <summary>
        /// Verifies that activating an artifact that was never prepared is rejected before any
        /// mutation and leaves both lifecycle counts and the existing active mapping unchanged.
        /// </summary>
        [Test]
        public void Activate_NeverPreparedArtifact_RejectsWithoutStateChange()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            HotReloadIntroducedTypeArtifact active = CreateArtifact("active");
            registry.RegisterPrepared(active);
            registry.Activate(active);
            HotReloadIntroducedTypeArtifact neverPrepared = CreateArtifactWithAssembly(
                CreateDynamicAssembly("NeverPrepared"),
                CreateOtherDescriptor("never-prepared"));

            Assert.Throws<InvalidOperationException>(() => registry.Activate(neverPrepared));

            Assert.That(registry.PreparedCount, Is.EqualTo(0));
            Assert.That(registry.ActiveCount, Is.EqualTo(1));
            Assert.That(
                registry.TryResolveActiveAssembly(neverPrepared.AssemblyFullName, out HotReloadIntroducedTypeArtifact rejected),
                Is.False);
            Assert.That(rejected, Is.Null);
            Assert.That(
                registry.TryResolveActiveAssembly(active.AssemblyFullName, out HotReloadIntroducedTypeArtifact resolved),
                Is.True);
            Assert.That(resolved, Is.SameAs(active));
        }

        /// <summary>
        /// Verifies that a discarded candidate loses its prepared membership, so a later
        /// activation of the same artifact is rejected without publishing its type identity.
        /// </summary>
        [Test]
        public void Activate_DiscardedPreparedArtifact_RejectsWithoutStateChange()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            HotReloadIntroducedTypeDescriptor descriptor = CreateOtherDescriptor("discarded");
            HotReloadIntroducedTypeArtifact candidate = CreateArtifactWithAssembly(
                CreateDynamicAssembly("Discarded"),
                descriptor);
            registry.RegisterPrepared(candidate);
            registry.DiscardPrepared(candidate);

            Assert.Throws<InvalidOperationException>(() => registry.Activate(candidate));

            Assert.That(registry.PreparedCount, Is.EqualTo(0));
            Assert.That(registry.ActiveCount, Is.EqualTo(0));
            Assert.That(
                registry.TryFindActiveDescriptor(descriptor, out HotReloadIntroducedTypeArtifact found),
                Is.False);
            Assert.That(found, Is.Null);
        }

        private static Assembly CreateDynamicAssembly(string prefix)
        {
            AssemblyName assemblyName = new AssemblyName(prefix + Guid.NewGuid().ToString("N"));
            return AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        }

        private static HotReloadIntroducedTypeArtifact CreateArtifactWithAssembly(
            Assembly assembly,
            HotReloadIntroducedTypeDescriptor descriptor)
        {
            return new HotReloadIntroducedTypeArtifact(
                assembly,
                "artifact.dll",
                "artifact.pdb",
                new List<HotReloadIntroducedTypeDescriptor> { descriptor });
        }

        private static void WriteDependencyAssembly(string path, string assemblyName)
        {
            AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)),
                assemblyName,
                ModuleKind.Dll);
            ModuleDefinition module = assembly.MainModule;
            module.Types.Add(new TypeDefinition(
                "ResolverFixture",
                "Base",
                CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                module.TypeSystem.Object));
            module.Types.Add(new TypeDefinition(
                "ResolverFixture",
                "Shared",
                CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                module.TypeSystem.Object));
            module.Types.Add(new TypeDefinition(
                "ResolverFixture",
                "Contract",
                CecilTypeAttributes.Public | CecilTypeAttributes.Interface | CecilTypeAttributes.Abstract,
                module.TypeSystem.Object));
            assembly.Write(path);
            assembly.Dispose();
        }

        private static void WriteConsumerAssembly(string path, string assemblyName, string dependencyName)
        {
            AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)),
                assemblyName,
                ModuleKind.Dll);
            ModuleDefinition module = assembly.MainModule;
            AssemblyNameReference dependencyReference = new AssemblyNameReference(
                dependencyName,
                new Version(1, 0, 0, 0));
            module.AssemblyReferences.Add(dependencyReference);
            TypeReference baseReference = new TypeReference("ResolverFixture", "Base", module, dependencyReference);
            TypeReference sharedReference = new TypeReference("ResolverFixture", "Shared", module, dependencyReference);
            TypeReference contractReference = new TypeReference("ResolverFixture", "Contract", module, dependencyReference);
            TypeDefinition fieldHolder = new TypeDefinition(
                "ResolverFixture",
                "FieldHolder",
                CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                module.TypeSystem.Object);
            fieldHolder.Fields.Add(new FieldDefinition("Value", CecilFieldAttributes.Public, sharedReference));
            module.Types.Add(fieldHolder);
            module.Types.Add(new TypeDefinition(
                "ResolverFixture",
                "Derived",
                CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                baseReference));
            TypeDefinition implementation = new TypeDefinition(
                "ResolverFixture",
                "Implementation",
                CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                module.TypeSystem.Object);
            implementation.Interfaces.Add(new InterfaceImplementation(contractReference));
            module.Types.Add(implementation);
            TypeDefinition methodHolder = new TypeDefinition(
                "ResolverFixture",
                "MethodHolder",
                CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                module.TypeSystem.Object);
            MethodDefinition method = new MethodDefinition(
                "Create",
                CecilMethodAttributes.Public | CecilMethodAttributes.Static,
                sharedReference);
            method.Body.GetILProcessor().Append(method.Body.GetILProcessor().Create(Mono.Cecil.Cil.OpCodes.Ldnull));
            method.Body.GetILProcessor().Append(method.Body.GetILProcessor().Create(Mono.Cecil.Cil.OpCodes.Ret));
            methodHolder.Methods.Add(method);
            module.Types.Add(methodHolder);
            assembly.Write(path);
            assembly.Dispose();
        }

        private static HotReloadIntroducedTypeArtifact CreateArtifact(string fingerprint)
        {
            Assembly assembly = typeof(HotReloadIntroducedTypeRegistryTests).Assembly;
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor> { CreateDescriptor(fingerprint) };
            return new HotReloadIntroducedTypeArtifact(assembly, "artifact.dll", "artifact.pdb", descriptors);
        }

        private static HotReloadIntroducedTypeDescriptor CreateDescriptor(string fingerprint)
        {
            return new HotReloadIntroducedTypeDescriptor(
                "OriginalAssembly",
                "original-mvid",
                "Example.Introduced",
                "Assets/Example.cs",
                fingerprint,
                "public class Introduced { }");
        }

        private static HotReloadIntroducedTypeDescriptor CreateOtherDescriptor(string fingerprint)
        {
            return new HotReloadIntroducedTypeDescriptor(
                "OriginalAssembly",
                "original-mvid",
                "Example.OtherIntroduced",
                "Assets/Other.cs",
                fingerprint,
                "public class OtherIntroduced { }");
        }
    }
}
