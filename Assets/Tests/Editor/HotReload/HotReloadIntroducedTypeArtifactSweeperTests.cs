using System;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers which introduced-type artifacts of earlier domains the sweep deletes and which files it
    /// keeps, laid out under a temporary project root.
    /// </summary>
    public class HotReloadIntroducedTypeArtifactSweeperTests
    {
        private string _projectRoot;
        private string _currentSessionId;

        private string ArtifactsRoot =>
            Path.Combine(_projectRoot, HotReloadConstants.IntroducedTypeArtifactsRelativeDirectory);

        private string PublicizedRoot =>
            Path.Combine(_projectRoot, HotReloadConstants.PublicizedRefsRelativeDirectory);

        private string ExposedRoot =>
            Path.Combine(_projectRoot, HotReloadConstants.InternalsExposedRefsRelativeDirectory);

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "uloop-artifact-sweep-" + NewId());
            Directory.CreateDirectory(_projectRoot);
            _currentSessionId = NewId();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: session directories that earlier domains left are deleted, while the current domain's
        /// session directory and its artifact stay.
        /// </summary>
        [Test]
        public void Sweep_DeletesEarlierSessionDirectoriesAndKeepsTheCurrentOne()
        {
            string earlierSession = Path.GetDirectoryName(CreateArtifactDirectory(NewId(), NewId()));
            string currentArtifact = CreateArtifactDirectory(_currentSessionId, NewId());

            CreateSweeper().Sweep();

            Assert.That(Directory.Exists(earlierSession), Is.False);
            Assert.That(Directory.Exists(currentArtifact), Is.True);
        }

        /// <summary>
        /// What: a session directory whose name differs from the current session id only in case is
        /// kept, since a case-insensitive file system resolves the current session to it.
        /// </summary>
        [Test]
        public void Sweep_KeepsTheCurrentSessionDirectoryWhenItsNameDiffersOnlyInCase()
        {
            string currentArtifact = CreateArtifactDirectory(_currentSessionId.ToUpperInvariant(), NewId());

            CreateSweeper().Sweep();

            Assert.That(Directory.Exists(currentArtifact), Is.True);
        }

        /// <summary>
        /// What: entries under the artifact directory that are not session directories of an earlier
        /// domain stay: directories that tests name freely, a GUID written in another format, and a
        /// plain file.
        /// </summary>
        [Test]
        public void Sweep_KeepsEntriesThatAreNotSessionDirectories()
        {
            string testSession = CreateArtifactDirectory("compiler-tests", NewId());
            string hyphenatedGuidSession = CreateArtifactDirectory(Guid.NewGuid().ToString("D"), NewId());
            string plainFile = Path.Combine(ArtifactsRoot, NewId());
            File.WriteAllText(plainFile, "file");

            CreateSweeper().Sweep();

            Assert.That(Directory.Exists(testSession), Is.True);
            Assert.That(Directory.Exists(hyphenatedGuidSession), Is.True);
            Assert.That(File.Exists(plainFile), Is.True);
        }

        /// <summary>
        /// What: copies made from artifacts of earlier domains are deleted from both reference caches,
        /// including a copy whose artifact directory is already gone and a temporary file a copy was
        /// being written through.
        /// </summary>
        [Test]
        public void Sweep_DeletesArtifactCopiesOfEarlierSessionsFromBothCaches()
        {
            string earlierArtifactId = NewId();
            CreateArtifactDirectory(NewId(), earlierArtifactId);
            string publicizedCopy = CreateCacheFile(PublicizedRoot, ArtifactCopyName(earlierArtifactId));
            string exposedCopy = CreateCacheFile(ExposedRoot, ArtifactCopyName(earlierArtifactId));
            string temporaryCopy = CreateCacheFile(
                PublicizedRoot,
                ArtifactCopyName(earlierArtifactId) + ".tmp-" + NewId());
            string orphanCopy = CreateCacheFile(ExposedRoot, ArtifactCopyName(NewId()));

            CreateSweeper().Sweep();

            Assert.That(File.Exists(publicizedCopy), Is.False);
            Assert.That(File.Exists(exposedCopy), Is.False);
            Assert.That(File.Exists(temporaryCopy), Is.False);
            Assert.That(File.Exists(orphanCopy), Is.False);
        }

        /// <summary>
        /// What: the sweep keeps copies made from artifacts of the current domain, copies of other
        /// assemblies, and files whose names only resemble an artifact copy.
        /// </summary>
        [Test]
        public void Sweep_KeepsCopiesOfCurrentArtifactsAndOfOtherAssemblies()
        {
            string currentArtifactId = NewId();
            CreateArtifactDirectory(_currentSessionId, currentArtifactId);
            string currentCopy = CreateCacheFile(PublicizedRoot, ArtifactCopyName(currentArtifactId));
            string currentExposedCopy = CreateCacheFile(ExposedRoot, ArtifactCopyName(currentArtifactId));
            string otherAssemblyCopy = CreateCacheFile(PublicizedRoot, "Assembly-CSharp-" + NewId() + ".dll");
            string nonHexIdentifier = CreateCacheFile(
                PublicizedRoot,
                HotReloadConstants.IntroducedTypeArtifactAssemblyNamePrefix + new string('z', 32) + "-" + NewId() + ".dll");
            string missingSeparator = CreateCacheFile(
                ExposedRoot,
                HotReloadConstants.IntroducedTypeArtifactAssemblyNamePrefix + NewId() + ".dll");

            CreateSweeper().Sweep();

            Assert.That(File.Exists(currentCopy), Is.True);
            Assert.That(File.Exists(currentExposedCopy), Is.True);
            Assert.That(File.Exists(otherAssemblyCopy), Is.True);
            Assert.That(File.Exists(nonHexIdentifier), Is.True);
            Assert.That(File.Exists(missingSeparator), Is.True);
        }

        /// <summary>
        /// What: a session directory that cannot be deleted because it is still in use is skipped,
        /// and the sweep still deletes the other leftovers.
        /// </summary>
        [Test]
        public void Sweep_WhenDeletingADirectoryThrowsIOException_ContinuesWithTheRest()
        {
            string lockedSessionId = NewId();
            string lockedSession = Path.GetDirectoryName(CreateArtifactDirectory(lockedSessionId, NewId()));
            string otherSession = Path.GetDirectoryName(CreateArtifactDirectory(NewId(), NewId()));
            string orphanCopy = CreateCacheFile(PublicizedRoot, ArtifactCopyName(NewId()));
            HotReloadIntroducedTypeArtifactSweeper sweeper = new HotReloadIntroducedTypeArtifactSweeper(
                _projectRoot,
                _currentSessionId,
                deleteDirectory: path =>
                {
                    if (Path.GetFileName(path) == lockedSessionId)
                    {
                        throw new IOException("The directory is in use.");
                    }

                    Directory.Delete(path, recursive: true);
                });

            sweeper.Sweep();

            Assert.That(Directory.Exists(lockedSession), Is.True);
            Assert.That(Directory.Exists(otherSession), Is.False);
            Assert.That(File.Exists(orphanCopy), Is.False);
        }

        /// <summary>
        /// What: a copy the operating system refuses to delete is skipped, and the sweep still deletes
        /// the other copies.
        /// </summary>
        [Test]
        public void Sweep_WhenDeletingACopyIsDenied_ContinuesWithTheRest()
        {
            string deniedCopyName = ArtifactCopyName(NewId());
            string deniedCopy = CreateCacheFile(PublicizedRoot, deniedCopyName);
            string otherCopy = CreateCacheFile(ExposedRoot, ArtifactCopyName(NewId()));
            HotReloadIntroducedTypeArtifactSweeper sweeper = new HotReloadIntroducedTypeArtifactSweeper(
                _projectRoot,
                _currentSessionId,
                deleteFile: path =>
                {
                    if (Path.GetFileName(path) == deniedCopyName)
                    {
                        throw new UnauthorizedAccessException("Access to the path is denied.");
                    }

                    File.Delete(path);
                });

            sweeper.Sweep();

            Assert.That(File.Exists(deniedCopy), Is.True);
            Assert.That(File.Exists(otherCopy), Is.False);
        }

        /// <summary>
        /// What: with no artifact directory at all, as before a domain prepares its first batch, the
        /// sweep still deletes every artifact copy in both caches and keeps other assemblies' copies.
        /// </summary>
        [Test]
        public void Sweep_WhenNoArtifactDirectoryExists_StillDeletesTheArtifactCopies()
        {
            string publicizedCopy = CreateCacheFile(PublicizedRoot, ArtifactCopyName(NewId()));
            string exposedCopy = CreateCacheFile(ExposedRoot, ArtifactCopyName(NewId()));
            string otherAssemblyCopy = CreateCacheFile(PublicizedRoot, "Assembly-CSharp-" + NewId() + ".dll");

            CreateSweeper().Sweep();

            Assert.That(File.Exists(publicizedCopy), Is.False);
            Assert.That(File.Exists(exposedCopy), Is.False);
            Assert.That(File.Exists(otherAssemblyCopy), Is.True);
        }

        /// <summary>
        /// What: a project where none of the swept directories exists is left as it is, without the
        /// sweep creating any of them.
        /// </summary>
        [Test]
        public void Sweep_WhenNoSweptDirectoryExists_CreatesNothing()
        {
            CreateSweeper().Sweep();

            Assert.That(Directory.Exists(Path.Combine(_projectRoot, "Library")), Is.False);
        }

        /// <summary>
        /// What: a current session id that is not a GUID is rejected, since the sweep would otherwise
        /// take the current domain's session directory for an earlier one.
        /// </summary>
        [Test]
        public void Constructor_RejectsASessionIdThatIsNotAGuid()
        {
            Assert.Throws<ArgumentException>(
                () => new HotReloadIntroducedTypeArtifactSweeper(_projectRoot, string.Empty));
            Assert.Throws<ArgumentException>(
                () => new HotReloadIntroducedTypeArtifactSweeper(_projectRoot, "compiler-tests"));
        }

        /// <summary>
        /// What: a project root that is not an absolute path is rejected, so the sweep never resolves
        /// the directories against whatever the working directory happens to be.
        /// </summary>
        [Test]
        public void Constructor_RejectsAProjectRootThatIsNotAbsolute()
        {
            Assert.Throws<ArgumentException>(
                () => new HotReloadIntroducedTypeArtifactSweeper("project", _currentSessionId));
            Assert.Throws<ArgumentException>(
                () => new HotReloadIntroducedTypeArtifactSweeper(string.Empty, _currentSessionId));
        }

        private HotReloadIntroducedTypeArtifactSweeper CreateSweeper()
        {
            return new HotReloadIntroducedTypeArtifactSweeper(_projectRoot, _currentSessionId);
        }

        // Lays an artifact out the way preparation writes one, so the sweep meets a real batch.
        private string CreateArtifactDirectory(string sessionId, string artifactId)
        {
            string directory = Path.Combine(ArtifactsRoot, sessionId, artifactId);
            Directory.CreateDirectory(directory);
            string assemblyName = HotReloadConstants.IntroducedTypeArtifactAssemblyNamePrefix + artifactId;
            File.WriteAllText(Path.Combine(directory, assemblyName + ".dll"), "dll");
            File.WriteAllText(Path.Combine(directory, assemblyName + ".pdb"), "pdb");
            return directory;
        }

        private string CreateCacheFile(string cacheRoot, string fileName)
        {
            Directory.CreateDirectory(cacheRoot);
            string path = Path.Combine(cacheRoot, fileName);
            File.WriteAllText(path, "copy");
            return path;
        }

        private string ArtifactCopyName(string artifactId)
        {
            return HotReloadConstants.IntroducedTypeArtifactAssemblyNamePrefix + artifactId + "-" + NewId() + ".dll";
        }

        private string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
