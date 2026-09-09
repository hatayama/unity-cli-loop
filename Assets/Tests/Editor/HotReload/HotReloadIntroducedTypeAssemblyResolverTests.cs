using System;
using System.IO;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the suspend and resume contract a replacement scope relies on to keep at most one
    /// introduced type resolver subscribed to assembly binds.
    /// </summary>
    public class HotReloadIntroducedTypeAssemblyResolverTests
    {
        /// <summary>
        /// Verifies that a suspended resolver stops being asked to answer binds.
        /// </summary>
        [Test]
        public void SuspendedResolver_DoesNotSeeBinds()
        {
            HotReloadIntroducedTypeAssemblyResolver resolver = CreateResolver();
            try
            {
                resolver.Suspend();
                int before = resolver.ResolutionCount;

                RequestUnknownAssembly();

                Assert.That(
                    resolver.ResolutionCount,
                    Is.EqualTo(before),
                    "A suspended resolver must be unsubscribed, or two resolvers answer one bind.");
            }
            finally
            {
                resolver.Dispose();
            }
        }

        /// <summary>
        /// Verifies that resuming a suspended resolver puts it back in the bind path exactly once.
        /// </summary>
        [Test]
        public void ResumedResolver_SeesBindsAgainOnce()
        {
            HotReloadIntroducedTypeAssemblyResolver resolver = CreateResolver();
            try
            {
                // The subscribed delta is measured first rather than assumed to be one, because
                // how many times a failed bind raises the event is the runtime's business.
                int subscribedBefore = resolver.ResolutionCount;
                RequestUnknownAssembly();
                int subscribedDelta = resolver.ResolutionCount - subscribedBefore;
                Assert.That(subscribedDelta, Is.GreaterThan(0));

                resolver.Suspend();
                resolver.Resume();
                // A second Resume must not subscribe the handler twice.
                resolver.Resume();
                int resumedBefore = resolver.ResolutionCount;

                RequestUnknownAssembly();

                Assert.That(
                    resolver.ResolutionCount - resumedBefore,
                    Is.EqualTo(subscribedDelta),
                    "A resumed resolver must answer a bind as often as it did before, not twice.");
            }
            finally
            {
                resolver.Dispose();
            }
        }

        /// <summary>
        /// Verifies that a disposed resolver refuses to be resumed instead of silently
        /// resubscribing a handler that can no longer answer.
        /// </summary>
        [Test]
        public void DisposedResolver_RefusesSuspendAndResume()
        {
            HotReloadIntroducedTypeAssemblyResolver resolver = CreateResolver();
            resolver.Dispose();

            Assert.Throws<ObjectDisposedException>(() => resolver.Resume());
            Assert.Throws<ObjectDisposedException>(() => resolver.Suspend());
        }

        private static HotReloadIntroducedTypeAssemblyResolver CreateResolver()
        {
            return new HotReloadIntroducedTypeAssemblyResolver(new HotReloadIntroducedTypeRegistry());
        }

        private static void RequestUnknownAssembly()
        {
            Assert.Throws<FileNotFoundException>(() => Assembly.Load(
                new AssemblyName("MissingIntroducedTypeDependency" + Guid.NewGuid().ToString("N")
                    + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")));
        }
    }
}
