using System;
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the editor-owned introduced type registry and resolver pair that activation runs through.
    /// </summary>
    public class HotReloadIntroducedTypeActivationTests
    {
        /// <summary>
        /// Verifies that the resolver the production holder exposes resolves what the registry it
        /// exposes activated, so both sides of the pair are the same instance.
        /// </summary>
        [Test]
        public void Holder_AfterInitialize_ResolverAndRegistryShareOneInstance()
        {
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact();

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                HotReloadIntroducedTypeHolder.Registry.RegisterPrepared(artifact);
                HotReloadIntroducedTypeHolder.Registry.Activate(artifact);

                Assert.That(
                    HotReloadIntroducedTypeHolder.Resolver.ResolveExact(artifact.AssemblyFullName),
                    Is.SameAs(artifact.Assembly));
            }
        }

        /// <summary>
        /// Verifies that closing a replacement scope puts the original pair back, so a type
        /// activated inside the scope is no longer resolvable afterwards.
        /// </summary>
        [Test]
        public void Holder_ReplacementScopeClosed_RestoresTheOriginalPair()
        {
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact();

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                HotReloadIntroducedTypeRegistry scoped = HotReloadIntroducedTypeHolder.Registry;
                scoped.RegisterPrepared(artifact);
                scoped.Activate(artifact);
            }

            Assert.That(
                HotReloadIntroducedTypeHolder.Resolver.ResolveExact(artifact.AssemblyFullName),
                Is.Null);
        }

        private static HotReloadIntroducedTypeArtifact CreateArtifact()
        {
            Assembly assembly = typeof(HotReloadIntroducedTypeActivationTests).Assembly;
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    new HotReloadIntroducedTypeDescriptor(
                        "OriginalAssembly",
                        "original-mvid",
                        "Example.Introduced",
                        "Assets/Example.cs",
                        "fingerprint",
                        "public class Introduced { }")
                };
            return new HotReloadIntroducedTypeArtifact(assembly, "artifact.dll", "artifact.pdb", descriptors);
        }
    }
}
