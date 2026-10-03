using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies which assemblies the fast scan marks as needing V3 assembly references.
    /// </summary>
    public sealed class ThirdPartyToolMigrationFastAssemblyRequirementCollectorTests
    {
        private const string AssemblyDirectoryKey = "<PROJECT_ROOT>/Assets/VendorTools";

        private const string LegacyToolSource =
            "using io.github.hatayama.uLoopMCP;\n" +
            "[McpTool] public sealed class HelloTool : AbstractUnityTool<HelloSchema, HelloResponse> {}";

        private const string CurrentApplicationSource =
            "using io.github.hatayama.UnityCliLoop.Application;\nclass C { MainThreadSwitcher switcher; }";

        private const string CurrentDomainSource =
            "using io.github.hatayama.UnityCliLoop.Domain;\nclass C { ServiceResult result; }";

        private const string BareDomainSource = "class C { ServiceResult result; }";

        private string _projectRoot;
        private HashSet<string> _legacyDirectories;
        private HashSet<string> _toolContractsDirectories;
        private HashSet<string> _applicationDirectories;
        private HashSet<string> _domainDirectories;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectRoot);
            _legacyDirectories = new HashSet<string>();
            _toolContractsDirectories = new HashSet<string>();
            _applicationDirectories = new HashSet<string>();
            _domainDirectories = new HashSet<string>();
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
        /// Verifies that a legacy custom tool marks only its assembly as a legacy assembly.
        /// </summary>
        [Test]
        public void CollectFastAssemblyReferenceRequirements_WhenSourceUsesLegacyToolApi_MarksLegacyAssembly()
        {
            CollectForSource(LegacyToolSource);

            Assert.That(_legacyDirectories, Is.EqualTo(new[] { AssemblyDirectoryKey }));
            Assert.That(_toolContractsDirectories, Is.Empty);
            Assert.That(_applicationDirectories, Is.Empty);
            Assert.That(_domainDirectories, Is.Empty);
        }

        /// <summary>
        /// Verifies that a current Application type imported by using requires both ToolContracts and Application references.
        /// </summary>
        [Test]
        public void CollectFastAssemblyReferenceRequirements_WhenSourceUsesCurrentApplicationType_RequiresToolContractsAndApplication()
        {
            CollectForSource(CurrentApplicationSource);

            Assert.That(_legacyDirectories, Is.Empty);
            Assert.That(_toolContractsDirectories, Is.EqualTo(new[] { AssemblyDirectoryKey }));
            Assert.That(_applicationDirectories, Is.EqualTo(new[] { AssemblyDirectoryKey }));
            Assert.That(_domainDirectories, Is.Empty);
        }

        /// <summary>
        /// Verifies that current Domain metadata imported by using requires both Domain and ToolContracts references.
        /// </summary>
        [Test]
        public void CollectFastAssemblyReferenceRequirements_WhenSourceUsesCurrentDomainMetadata_RequiresDomainAndToolContracts()
        {
            CollectForSource(CurrentDomainSource);

            Assert.That(_legacyDirectories, Is.Empty);
            Assert.That(_toolContractsDirectories, Is.EqualTo(new[] { AssemblyDirectoryKey }));
            Assert.That(_applicationDirectories, Is.Empty);
            Assert.That(_domainDirectories, Is.EqualTo(new[] { AssemblyDirectoryKey }));
        }

        /// <summary>
        /// Verifies that a cancelled async collection reads no source and records no requirement.
        /// </summary>
        [Test]
        public void CollectFastAssemblyReferenceRequirementsAsync_WhenTokenIsCancelled_RecordsNothing()
        {
            string toolDirectory = Path.Combine(_projectRoot, "Assets", "VendorTools");
            string toolPath = WriteSource(toolDirectory, "HelloTool.cs", LegacyToolSource);

            Task task = ThirdPartyToolMigrationFastAssemblyRequirementCollector.CollectFastAssemblyReferenceRequirementsAsync(
                new List<string> { toolPath },
                new List<string> { toolDirectory },
                new List<AssemblyReferenceDirectory>(),
                _projectRoot,
                new HashSet<string>(),
                new HashSet<string>(),
                new HashSet<string>(),
                new Dictionary<string, HashSet<string>>(),
                new Dictionary<string, HashSet<string>>(),
                new Dictionary<string, HashSet<string>>(),
                _legacyDirectories,
                _toolContractsDirectories,
                _applicationDirectories,
                _domainDirectories,
                new CancellationToken(true));

            Assert.That(task.IsCompleted, Is.True);
            Assert.That(_legacyDirectories, Is.Empty);
        }

        /// <summary>
        /// Verifies that the ToolContracts-scoped pass only records requirements for assemblies with a scoped ToolContracts using.
        /// </summary>
        [Test]
        public void CollectFastAssemblyScopedCurrentToolContractsRequirementsAsync_WhenFilesSpanAssemblies_RecordsOnlyScopedAssembly()
        {
            string scopedDirectory = Path.Combine(_projectRoot, "Assets", "ScopedTools");
            string otherDirectory = Path.Combine(_projectRoot, "Assets", "OtherTools");
            string scopedPath = WriteSource(scopedDirectory, "ScopedMetadata.cs", CurrentDomainSource);
            string otherPath = WriteSource(otherDirectory, "OtherMetadata.cs", CurrentDomainSource);

            Task task = CollectScopedToolContractsRequirements(
                new List<string> { scopedPath, otherPath },
                new List<string> { scopedDirectory, otherDirectory },
                new HashSet<string> { scopedDirectory },
                CancellationToken.None);

            Assert.That(task.IsCompleted, Is.True);
            Assert.That(_domainDirectories, Is.EqualTo(new[] { scopedDirectory }));
            Assert.That(_toolContractsDirectories, Is.EqualTo(new[] { scopedDirectory }));
        }

        /// <summary>
        /// Verifies that a cancelled ToolContracts-scoped pass records no requirement.
        /// </summary>
        [Test]
        public void CollectFastAssemblyScopedCurrentToolContractsRequirementsAsync_WhenTokenIsCancelled_RecordsNothing()
        {
            string scopedDirectory = Path.Combine(_projectRoot, "Assets", "ScopedTools");
            string scopedPath = WriteSource(scopedDirectory, "ScopedMetadata.cs", CurrentDomainSource);

            Task task = CollectScopedToolContractsRequirements(
                new List<string> { scopedPath },
                new List<string> { scopedDirectory },
                new HashSet<string> { scopedDirectory },
                new CancellationToken(true));

            Assert.That(task.IsCompleted, Is.True);
            Assert.That(_domainDirectories, Is.Empty);
            Assert.That(_toolContractsDirectories, Is.Empty);
        }

        /// <summary>
        /// Verifies that bare Domain metadata only requires a Domain reference inside an assembly with a scoped Domain using.
        /// </summary>
        [Test]
        public void CollectFastAssemblyScopedCurrentDomainRequirementsAsync_WhenFilesSpanAssemblies_RecordsOnlyScopedAssembly()
        {
            string scopedDirectory = Path.Combine(_projectRoot, "Assets", "ScopedTools");
            string otherDirectory = Path.Combine(_projectRoot, "Assets", "OtherTools");
            string scopedPath = WriteSource(scopedDirectory, "ScopedMetadata.cs", BareDomainSource);
            string otherPath = WriteSource(otherDirectory, "OtherMetadata.cs", BareDomainSource);

            Task task = ThirdPartyToolMigrationFastAssemblyRequirementCollector.CollectFastAssemblyScopedCurrentDomainRequirementsAsync(
                new List<string> { scopedPath, otherPath },
                new List<string> { scopedDirectory, otherDirectory },
                new List<AssemblyReferenceDirectory>(),
                _projectRoot,
                new HashSet<string> { scopedDirectory },
                _domainDirectories,
                CancellationToken.None);

            Assert.That(task.IsCompleted, Is.True);
            Assert.That(_domainDirectories, Is.EqualTo(new[] { scopedDirectory }));
        }

        /// <summary>
        /// Verifies that a cancelled Domain-scoped pass records no requirement.
        /// </summary>
        [Test]
        public void CollectFastAssemblyScopedCurrentDomainRequirementsAsync_WhenTokenIsCancelled_RecordsNothing()
        {
            string scopedDirectory = Path.Combine(_projectRoot, "Assets", "ScopedTools");
            string scopedPath = WriteSource(scopedDirectory, "ScopedMetadata.cs", BareDomainSource);

            Task task = ThirdPartyToolMigrationFastAssemblyRequirementCollector.CollectFastAssemblyScopedCurrentDomainRequirementsAsync(
                new List<string> { scopedPath },
                new List<string> { scopedDirectory },
                new List<AssemblyReferenceDirectory>(),
                _projectRoot,
                new HashSet<string> { scopedDirectory },
                _domainDirectories,
                new CancellationToken(true));

            Assert.That(task.IsCompleted, Is.True);
            Assert.That(_domainDirectories, Is.Empty);
        }

        private void CollectForSource(string source)
        {
            ThirdPartyToolMigrationFastAssemblyRequirementCollector.CollectFastAssemblyReferenceRequirements(
                source,
                AssemblyDirectoryKey,
                false,
                false,
                false,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                _legacyDirectories,
                _toolContractsDirectories,
                _applicationDirectories,
                _domainDirectories);
        }

        private Task CollectScopedToolContractsRequirements(
            List<string> csharpFilePaths,
            List<string> asmdefDirectories,
            HashSet<string> scopedToolContractsDirectories,
            CancellationToken ct)
        {
            return ThirdPartyToolMigrationFastAssemblyRequirementCollector.CollectFastAssemblyScopedCurrentToolContractsRequirementsAsync(
                csharpFilePaths,
                asmdefDirectories,
                new List<AssemblyReferenceDirectory>(),
                _projectRoot,
                scopedToolContractsDirectories,
                new HashSet<string>(),
                new HashSet<string>(),
                new Dictionary<string, HashSet<string>>(),
                new Dictionary<string, HashSet<string>>(),
                new Dictionary<string, HashSet<string>>(),
                _legacyDirectories,
                _toolContractsDirectories,
                _applicationDirectories,
                _domainDirectories,
                ct);
        }

        private static string WriteSource(string directory, string fileName, string source)
        {
            Directory.CreateDirectory(directory);
            string filePath = Path.Combine(directory, fileName);
            File.WriteAllText(filePath, source);
            return filePath;
        }
    }
}
