using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the resolver that decides where one patched or unchanged row's method
    /// lives: the home of the file the row came from when the row names no assembly, and the
    /// artifact this domain retains when it names one.
    /// </summary>
    public class HotReloadEntryHomeResolverTests
    {
        // A project assembly name no introduced-type artifact can carry.
        private const string ProjectAssemblyName = "EntryHomeResolverFixtureAssembly";

        private const string ArtifactDllPath = "entry-home-resolver-artifact.dll";

        private HotReloadDomainTestScope _scope;

        private HotReloadDomainTestAccess _access;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            _access = new HotReloadDomainTestAccess();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
        }

        /// <summary>
        /// What: a row that names no home assembly resolves to the home of the file it was
        /// produced from, which is every row until an artifact serves the type.
        /// </summary>
        [Test]
        public void Resolve_RowNamesNoHomeAssembly_ReturnsTheFileHome()
        {
            HotReloadEntryHomeResolver resolver = CreateResolver();
            HotReloadTypeHome fileHome = HotReloadTypeHome.ScriptAssembliesUnderProject(
                ResolveProjectRoot(),
                ProjectAssemblyName);

            Assert.That(resolver.Resolve(fileHome, null), Is.SameAs(fileHome));
            Assert.That(resolver.Resolve(fileHome, string.Empty), Is.SameAs(fileHome));
        }

        /// <summary>
        /// What: a row that names an artifact this domain retains resolves to that artifact's
        /// home, so the method is looked for in the assembly serving the type rather than in the
        /// one the edited file belongs to.
        /// </summary>
        [Test]
        public void Resolve_RowNamesARetainedArtifact_ReturnsThatArtifactHome()
        {
            HotReloadIntroducedTypeArtifact artifact = ActivateArtifact();
            HotReloadEntryHomeResolver resolver = CreateResolver();
            HotReloadTypeHome fileHome = HotReloadTypeHome.ScriptAssembliesUnderProject(
                ResolveProjectRoot(),
                ProjectAssemblyName);

            HotReloadTypeHome home = resolver.Resolve(fileHome, artifact.Assembly.GetName().Name);

            Assert.That(home.Kind, Is.EqualTo(HotReloadTypeHomeKind.RetainedArtifact));
            Assert.That(home.DllPath, Is.EqualTo(artifact.DllPath));
        }

        /// <summary>
        /// What: a row that names an assembly no artifact of this domain carries is refused rather
        /// than resolved to a project assembly of that name, because a row only names an assembly
        /// at all when an artifact was supposed to serve its type.
        /// </summary>
        [Test]
        public void Resolve_RowNamesAnAssemblyNoArtifactCarries_Throws()
        {
            HotReloadEntryHomeResolver resolver = CreateResolver();
            HotReloadTypeHome fileHome = HotReloadTypeHome.ScriptAssembliesUnderProject(
                ResolveProjectRoot(),
                ProjectAssemblyName);

            Assert.Throws<InvalidOperationException>(
                () => resolver.Resolve(fileHome, "EntryHomeResolverUnknownAssembly"));
        }

        private HotReloadEntryHomeResolver CreateResolver()
        {
            return new HotReloadEntryHomeResolver(_access.Domain, ResolveProjectRoot());
        }

        private HotReloadIntroducedTypeArtifact ActivateArtifact()
        {
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                CreateArtifactAssembly(),
                ArtifactDllPath,
                "entry-home-resolver-artifact.pdb",
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    new HotReloadIntroducedTypeDescriptor(
                        "OriginalAssembly",
                        "original-mvid",
                        "Example.Introduced",
                        "Assets/EntryHomeResolverIntroduced.cs",
                        "entry-home-resolver-fingerprint",
                        "public class Introduced { }")
                });
            _access.Domain.IntroducedTypes.RegisterPrepared(artifact);
            _access.Domain.IntroducedTypes.Activate(artifact);
            return artifact;
        }

        // Why a generated name: an artifact assembly is compiled under a name of its own, so a
        // fixture that reused a project assembly's name would not resolve the way production does.
        private static Assembly CreateArtifactAssembly()
        {
            AssemblyName assemblyName = new AssemblyName("UloopIntroducedTypes_" + Guid.NewGuid().ToString("N"));
            return AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        }

        private static string ResolveProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }
    }
}
