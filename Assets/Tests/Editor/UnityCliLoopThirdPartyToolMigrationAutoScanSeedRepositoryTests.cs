using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies argument validation of the migration auto-scan seed repository.
    /// </summary>
    public sealed class UnityCliLoopThirdPartyToolMigrationAutoScanSeedRepositoryTests
    {
        /// <summary>
        /// Verifies that storing a null seed list is rejected before anything is written to the session.
        /// </summary>
        [Test]
        public void StoreSeedFilePaths_WhenFilePathsIsNull_ThrowsArgumentNullException()
        {
            UnityCliLoopThirdPartyToolMigrationAutoScanSeedRepository repository =
                new UnityCliLoopThirdPartyToolMigrationAutoScanSeedRepository();

            ArgumentNullException exception =
                Assert.Throws<ArgumentNullException>(() => repository.StoreSeedFilePaths(null));

            Assert.That(exception.ParamName, Is.EqualTo("filePaths"));
        }
    }
}
