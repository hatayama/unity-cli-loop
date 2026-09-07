using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>
        /// Verifies that opening a replacement scope stops the resolver it took over from
        /// answering binds, and that closing the scope makes the original registry resolvable
        /// again through a live resolver.
        /// </summary>
        [Test]
        public void Holder_ReplacementScopeOpen_OnlyTheReplacementResolverAnswersBinds()
        {
            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                HotReloadIntroducedTypeRegistry outerRegistry = HotReloadIntroducedTypeHolder.Registry;
                HotReloadIntroducedTypeAssemblyResolver outerResolver = HotReloadIntroducedTypeHolder.Resolver;

                using (HotReloadIntroducedTypeHolder.BeginReplacement())
                {
                    HotReloadIntroducedTypeHolder.Initialize();
                    HotReloadIntroducedTypeAssemblyResolver innerResolver = HotReloadIntroducedTypeHolder.Resolver;
                    int innerBefore = innerResolver.ResolutionCount;
                    int outerBefore = outerResolver.ResolutionCount;

                    RequestUnknownAssembly();

                    Assert.That(innerResolver.ResolutionCount, Is.GreaterThan(innerBefore));
                    Assert.That(
                        outerResolver.ResolutionCount,
                        Is.EqualTo(outerBefore),
                        "The taken-over resolver must be unsubscribed, or two resolvers answer one bind.");
                }

                Assert.That(HotReloadIntroducedTypeHolder.Registry, Is.SameAs(outerRegistry));
                HotReloadIntroducedTypeAssemblyResolver restoredResolver = HotReloadIntroducedTypeHolder.Resolver;
                int restoredBefore = restoredResolver.ResolutionCount;

                RequestUnknownAssembly();

                Assert.That(restoredResolver.ResolutionCount, Is.GreaterThan(restoredBefore));
            }
        }

        private static void RequestUnknownAssembly()
        {
            Assert.Throws<FileNotFoundException>(() => Assembly.Load(
                new AssemblyName("MissingIntroducedTypeDependency" + Guid.NewGuid().ToString("N")
                    + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")));
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
