using System.Collections.Generic;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class AutoUsingResolverTests
    {
        [Test]
        public void AddAssemblyReferenceIfMissing_WhenAssemblyIdentityAlreadyExistsUnderDifferentPath_ShouldSkipDuplicate()
        {
            List<string> currentReferences = new List<string>
            {
                "/reference/System.Runtime.dll"
            };
            List<string> assemblyReferencesToAdd = new List<string>();

            AutoUsingResolver.AddAssemblyReferenceIfMissing(
                assemblyReferencesToAdd,
                currentReferences,
                "/loaded/System.Runtime.dll");

            Assert.That(assemblyReferencesToAdd, Is.Empty);
        }

        [Test]
        public void AddAssemblyReferenceIfMissing_WhenAssemblyIdentityIsNew_ShouldAddReference()
        {
            List<string> currentReferences = new List<string>
            {
                "/reference/System.Runtime.dll"
            };
            List<string> assemblyReferencesToAdd = new List<string>();

            AutoUsingResolver.AddAssemblyReferenceIfMissing(
                assemblyReferencesToAdd,
                currentReferences,
                "/loaded/System.Collections.Immutable.dll");

            Assert.That(assemblyReferencesToAdd, Has.Count.EqualTo(1));
            Assert.That(assemblyReferencesToAdd[0], Is.EqualTo("/loaded/System.Collections.Immutable.dll"));
        }
    }
}
