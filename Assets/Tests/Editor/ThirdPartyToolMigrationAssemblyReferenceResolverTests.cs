using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies how asmdef and asmref files are resolved into assembly directories for migration.
    /// </summary>
    public sealed class ThirdPartyToolMigrationAssemblyReferenceResolverTests
    {
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, true);
            }
        }

        /// <summary>
        /// Verifies that an asmref pointing at an unknown assembly is ignored while a matching asmref is resolved.
        /// </summary>
        [Test]
        public void CreateAssemblyReferenceDirectories_WhenAsmrefReferencesUnknownAssembly_SkipsIt()
        {
            string vendorDirectory = Path.Combine(_projectRoot, "Assets", "Vendor");
            string extensionDirectory = Path.Combine(_projectRoot, "Assets", "Extension");
            string asmdefPath = WriteFile(Path.Combine(vendorDirectory, "Vendor.Editor.asmdef"), "{ \"name\": \"Vendor.Editor\" }");
            string unknownAsmrefPath = WriteFile(
                Path.Combine(_projectRoot, "Assets", "Unknown", "Unknown.asmref"),
                "{ \"reference\": \"Missing.Assembly\" }");
            string extensionAsmrefPath = WriteFile(
                Path.Combine(extensionDirectory, "Extension.asmref"),
                "{ \"reference\": \"Vendor.Editor\" }");

            List<AssemblyReferenceDirectory> directories =
                ThirdPartyToolMigrationAssemblyReferenceResolver.CreateAssemblyReferenceDirectories(
                    new List<string> { asmdefPath },
                    new List<string> { unknownAsmrefPath, extensionAsmrefPath });

            Assert.That(directories.Select(directory => directory.SourceDirectory), Is.EqualTo(new[] { extensionDirectory }));
            Assert.That(directories.Select(directory => directory.TargetAssemblyDirectory), Is.EqualTo(new[] { vendorDirectory }));
        }

        /// <summary>
        /// Verifies that only references to known asmdefs are collected and asmdefs without references are skipped.
        /// </summary>
        [Test]
        public void CreateReferencedAssemblyDirectoriesByDirectory_WhenReferencesMixKnownAndUnknown_KeepsOnlyKnownDirectories()
        {
            string baseDirectory = Path.Combine(_projectRoot, "Assets", "Base");
            string consumerDirectory = Path.Combine(_projectRoot, "Assets", "Consumer");
            WriteFile(Path.Combine(baseDirectory, "Base.asmdef"), "{ \"name\": \"Vendor.Base\" }");
            WriteFile(
                Path.Combine(consumerDirectory, "Consumer.asmdef"),
                "{ \"name\": \"Vendor.Consumer\", \"references\": [ \"Missing.Assembly\", \"Vendor.Base\" ] }");

            Dictionary<string, string[]> referencedDirectories =
                ThirdPartyToolMigrationAssemblyReferenceResolver.CreateReferencedAssemblyDirectoriesByDirectory(
                    new List<string> { baseDirectory, consumerDirectory });

            Assert.That(referencedDirectories.Keys, Is.EqualTo(new[] { consumerDirectory }));
            Assert.That(referencedDirectories[consumerDirectory], Is.EqualTo(new[] { baseDirectory }));
        }

        /// <summary>
        /// Verifies that asmdef directories that do not exist contribute no asmdef files.
        /// </summary>
        [Test]
        public void GetAsmdefFilePathsFromDirectories_WhenDirectoryDoesNotExist_SkipsIt()
        {
            string existingDirectory = Path.Combine(_projectRoot, "Assets", "Vendor");
            string asmdefPath = WriteFile(Path.Combine(existingDirectory, "Vendor.asmdef"), "{ \"name\": \"Vendor\" }");
            string missingDirectory = Path.Combine(_projectRoot, "Assets", "Missing");

            List<string> asmdefFilePaths =
                ThirdPartyToolMigrationAssemblyReferenceResolver.GetAsmdefFilePathsFromDirectories(
                    new List<string> { missingDirectory, existingDirectory });

            Assert.That(asmdefFilePaths, Is.EqualTo(new[] { asmdefPath }));
        }

        /// <summary>
        /// Verifies that the async resolver keeps matching asmrefs, drops unknown or directory-less ones, and orders deepest first.
        /// </summary>
        [Test]
        public void CreateAssemblyReferenceDirectoriesAsync_WhenAsmrefsMixValidAndInvalid_ReturnsValidOnesDeepestFirst()
        {
            string vendorDirectory = Path.Combine(_projectRoot, "Assets", "Vendor");
            string asmdefPath = Path.Combine(vendorDirectory, "Vendor.Editor.asmdef");
            string shallowDirectory = Path.Combine(_projectRoot, "Assets", "Ext");
            string deepDirectory = Path.Combine(_projectRoot, "Assets", "Ext", "Nested", "Deeper");
            string shallowAsmrefPath = Path.Combine(shallowDirectory, "Shallow.asmref");
            string unknownAsmrefPath = Path.Combine(_projectRoot, "Assets", "Unknown", "Unknown.asmref");
            string directoryLessAsmrefPath = "DirectoryLess.asmref";
            string deepAsmrefPath = Path.Combine(deepDirectory, "Deep.asmref");
            Dictionary<string, string> sources = new Dictionary<string, string>
            {
                { asmdefPath, "{ \"name\": \"Vendor.Editor\" }" },
                { shallowAsmrefPath, "{ \"reference\": \"Vendor.Editor\" }" },
                { unknownAsmrefPath, "{ \"reference\": \"Missing.Assembly\" }" },
                { directoryLessAsmrefPath, "{ \"reference\": \"Vendor.Editor\" }" },
                { deepAsmrefPath, "{ \"reference\": \"Vendor.Editor\" }" }
            };
            List<string> readPaths = new List<string>();
            List<ThirdPartyToolMigrationProgress> reports = new List<ThirdPartyToolMigrationProgress>();

            Task<List<AssemblyReferenceDirectory>> task =
                ThirdPartyToolMigrationAssemblyReferenceResolver.CreateAssemblyReferenceDirectoriesAsync(
                    new List<string> { asmdefPath },
                    new List<string> { shallowAsmrefPath, unknownAsmrefPath, directoryLessAsmrefPath, deepAsmrefPath },
                    CreateRecordingCache(sources, readPaths),
                    new MigrationProgressCounter(5, new RecordingProgress(reports)),
                    CancellationToken.None);

            List<AssemblyReferenceDirectory> directories = GetCompletedResult(task);
            Assert.That(
                directories.Select(directory => directory.SourceDirectory),
                Is.EqualTo(new[] { deepDirectory, shallowDirectory }));
            Assert.That(
                directories.Select(directory => directory.TargetAssemblyDirectory),
                Is.EqualTo(new[] { vendorDirectory, vendorDirectory }));
            Assert.That(reports[reports.Count - 1].ProcessedItemCount, Is.EqualTo(5));
        }

        /// <summary>
        /// Verifies that a cancelled async resolve returns an empty result without reading any assembly file.
        /// </summary>
        [Test]
        public void CreateAssemblyReferenceDirectoriesAsync_WhenTokenIsCancelled_ReadsNothing()
        {
            string asmdefPath = Path.Combine(_projectRoot, "Assets", "Vendor", "Vendor.Editor.asmdef");
            string asmrefPath = Path.Combine(_projectRoot, "Assets", "Ext", "Ext.asmref");
            Dictionary<string, string> sources = new Dictionary<string, string>
            {
                { asmdefPath, "{ \"name\": \"Vendor.Editor\" }" },
                { asmrefPath, "{ \"reference\": \"Vendor.Editor\" }" }
            };
            List<string> readPaths = new List<string>();
            List<ThirdPartyToolMigrationProgress> reports = new List<ThirdPartyToolMigrationProgress>();

            Task<List<AssemblyReferenceDirectory>> task =
                ThirdPartyToolMigrationAssemblyReferenceResolver.CreateAssemblyReferenceDirectoriesAsync(
                    new List<string> { asmdefPath },
                    new List<string> { asmrefPath },
                    CreateRecordingCache(sources, readPaths),
                    new MigrationProgressCounter(2, new RecordingProgress(reports)),
                    new CancellationToken(true));

            List<AssemblyReferenceDirectory> directories = GetCompletedResult(task);
            Assert.That(directories, Is.Empty);
            Assert.That(readPaths, Is.Empty);
        }

        /// <summary>
        /// Verifies that the async asmdef map skips an asmdef path without a directory without reading it.
        /// </summary>
        [Test]
        public void CreateAsmdefDirectoryMapAsync_WhenAsmdefPathHasNoDirectory_SkipsItWithoutReading()
        {
            string directoryLessAsmdefPath = "DirectoryLess.asmdef";
            string vendorDirectory = Path.Combine(_projectRoot, "Assets", "Vendor");
            string asmdefPath = Path.Combine(vendorDirectory, "Vendor.Editor.asmdef");
            Dictionary<string, string> sources = new Dictionary<string, string>
            {
                { directoryLessAsmdefPath, "{ \"name\": \"DirectoryLess\" }" },
                { asmdefPath, "{ \"name\": \"Vendor.Editor\" }" }
            };
            List<string> readPaths = new List<string>();
            List<ThirdPartyToolMigrationProgress> reports = new List<ThirdPartyToolMigrationProgress>();

            Task<Dictionary<string, string>> task =
                ThirdPartyToolMigrationAssemblyReferenceResolver.CreateAsmdefDirectoryMapAsync(
                    new List<string> { directoryLessAsmdefPath, asmdefPath },
                    CreateRecordingCache(sources, readPaths),
                    new MigrationProgressCounter(2, new RecordingProgress(reports)),
                    CancellationToken.None);

            Dictionary<string, string> directoriesByReference = GetCompletedResult(task);
            Assert.That(directoriesByReference.Keys, Is.EqualTo(new[] { "Vendor.Editor" }));
            Assert.That(directoriesByReference["Vendor.Editor"], Is.EqualTo(vendorDirectory));
            Assert.That(readPaths, Is.EqualTo(new[] { asmdefPath }));
            Assert.That(reports[reports.Count - 1].ProcessedItemCount, Is.EqualTo(2));
        }

        private static string WriteFile(string filePath, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, content);
            return filePath;
        }

        private static ThirdPartyToolMigrationSourceFileCache CreateRecordingCache(
            Dictionary<string, string> sources,
            List<string> readPaths)
        {
            return new ThirdPartyToolMigrationSourceFileCache(filePath =>
            {
                readPaths.Add(filePath);
                return sources[filePath];
            });
        }

        private static T GetCompletedResult<T>(Task<T> task)
        {
            Assert.That(task.IsCompleted, Is.True);
            return task.GetAwaiter().GetResult();
        }

        private sealed class RecordingProgress : IProgress<ThirdPartyToolMigrationProgress>
        {
            private readonly List<ThirdPartyToolMigrationProgress> _reports;

            public RecordingProgress(List<ThirdPartyToolMigrationProgress> reports)
            {
                _reports = reports;
            }

            public void Report(ThirdPartyToolMigrationProgress value)
            {
                _reports.Add(value);
            }
        }
    }
}
