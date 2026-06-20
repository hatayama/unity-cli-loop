using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal readonly struct MigrationFileChange
    {
        public MigrationFileChange(string filePath, string content)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(content != null, "content must not be null");

            FilePath = filePath;
            Content = content ?? string.Empty;
        }

        public string FilePath { get; }
        public string Content { get; }
    }

    internal readonly struct MigrationPlan
    {
        public static MigrationPlan Empty => new(new List<MigrationFileChange>(), 0);

        public MigrationPlan(List<MigrationFileChange> changes, int replacementCount)
        {
            Debug.Assert(changes != null, "changes must not be null");
            Debug.Assert(replacementCount >= 0, "replacementCount must not be negative");

            Changes = changes ?? new List<MigrationFileChange>();
            ReplacementCount = replacementCount;
            ChangedFilePaths = Changes
                .Select(change => change.FilePath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }

        public List<MigrationFileChange> Changes { get; }
        public int ReplacementCount { get; }
        public List<string> ChangedFilePaths { get; }
    }

    internal sealed class MigrationProgressCounter
    {
        private readonly IProgress<ThirdPartyToolMigrationProgress> _progress;
        private readonly int _totalItemCount;
        private int _processedItemCount;

        public MigrationProgressCounter(
            int totalItemCount,
            IProgress<ThirdPartyToolMigrationProgress> progress)
        {
            Debug.Assert(totalItemCount >= 0, "totalItemCount must not be negative");
            Debug.Assert(progress != null, "progress must not be null");

            _totalItemCount = totalItemCount;
            _progress = progress;
            Report();
        }

        public async Task ReportProcessedItemAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            _processedItemCount++;
            Report();
            if (_processedItemCount % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize != 0)
            {
                return;
            }

            await Task.Yield();
        }

        public void ReportComplete()
        {
            _processedItemCount = _totalItemCount;
            Report();
        }

        private void Report()
        {
            _progress.Report(
                new ThirdPartyToolMigrationProgress(
                    Math.Min(_processedItemCount, _totalItemCount),
                    _totalItemCount));
        }
    }

    internal readonly struct MigrationAssemblyUsage
    {
        public MigrationAssemblyUsage(
            List<string> asmdefDirectories,
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
            HashSet<string> legacyAssemblyDirectories,
            HashSet<string> assemblyScopedLegacyDirectories,
            HashSet<string> assemblyScopedCurrentToolContractsDirectories,
            HashSet<string> assemblyScopedCurrentApplicationDirectories,
            HashSet<string> assemblyScopedCurrentFirstPartyToolsDirectories,
            Dictionary<string, string[]> assemblyScopedLegacyAliasesByDirectory,
            Dictionary<string, string[]> assemblyScopedLegacyToolInfoAliasesByDirectory,
            Dictionary<string, string[]> assemblyScopedCurrentApplicationAliasesByDirectory,
            Dictionary<string, string[]> assemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
            Dictionary<string, string[]> assemblyDeclaredTypeNamesByDirectory,
            HashSet<string> toolContractsReferenceAssemblyDirectories,
            HashSet<string> applicationReferenceAssemblyDirectories,
            HashSet<string> domainReferenceAssemblyDirectories,
            HashSet<string> firstPartyScreenshotReferenceAssemblyDirectories)
        {
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
            Debug.Assert(
                assemblyReferenceDirectories != null,
                "assemblyReferenceDirectories must not be null");
            Debug.Assert(legacyAssemblyDirectories != null, "legacyAssemblyDirectories must not be null");
            Debug.Assert(
                assemblyScopedLegacyDirectories != null,
                "assemblyScopedLegacyDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentToolContractsDirectories != null,
                "assemblyScopedCurrentToolContractsDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentApplicationDirectories != null,
                "assemblyScopedCurrentApplicationDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentFirstPartyToolsDirectories != null,
                "assemblyScopedCurrentFirstPartyToolsDirectories must not be null");
            Debug.Assert(
                assemblyScopedLegacyAliasesByDirectory != null,
                "assemblyScopedLegacyAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyScopedLegacyToolInfoAliasesByDirectory != null,
                "assemblyScopedLegacyToolInfoAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyScopedCurrentApplicationAliasesByDirectory != null,
                "assemblyScopedCurrentApplicationAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyScopedCurrentFirstPartyToolsAliasesByDirectory != null,
                "assemblyScopedCurrentFirstPartyToolsAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyDeclaredTypeNamesByDirectory != null,
                "assemblyDeclaredTypeNamesByDirectory must not be null");
            Debug.Assert(
                toolContractsReferenceAssemblyDirectories != null,
                "toolContractsReferenceAssemblyDirectories must not be null");
            Debug.Assert(
                applicationReferenceAssemblyDirectories != null,
                "applicationReferenceAssemblyDirectories must not be null");
            Debug.Assert(
                domainReferenceAssemblyDirectories != null,
                "domainReferenceAssemblyDirectories must not be null");
            Debug.Assert(
                firstPartyScreenshotReferenceAssemblyDirectories != null,
                "firstPartyScreenshotReferenceAssemblyDirectories must not be null");

            AsmdefDirectories = asmdefDirectories ?? new List<string>();
            AssemblyReferenceDirectories = assemblyReferenceDirectories ?? new List<AssemblyReferenceDirectory>();
            LegacyAssemblyDirectories = legacyAssemblyDirectories ?? new HashSet<string>(StringComparer.Ordinal);
            AssemblyScopedLegacyDirectories = assemblyScopedLegacyDirectories ??
                new HashSet<string>(StringComparer.Ordinal);
            AssemblyScopedCurrentToolContractsDirectories = assemblyScopedCurrentToolContractsDirectories ??
                new HashSet<string>(StringComparer.Ordinal);
            AssemblyScopedCurrentApplicationDirectories = assemblyScopedCurrentApplicationDirectories ??
                new HashSet<string>(StringComparer.Ordinal);
            AssemblyScopedCurrentFirstPartyToolsDirectories = assemblyScopedCurrentFirstPartyToolsDirectories ??
                new HashSet<string>(StringComparer.Ordinal);
            AssemblyScopedLegacyAliasesByDirectory = assemblyScopedLegacyAliasesByDirectory ??
                new Dictionary<string, string[]>(StringComparer.Ordinal);
            AssemblyScopedLegacyToolInfoAliasesByDirectory = assemblyScopedLegacyToolInfoAliasesByDirectory ??
                new Dictionary<string, string[]>(StringComparer.Ordinal);
            AssemblyScopedCurrentApplicationAliasesByDirectory =
                assemblyScopedCurrentApplicationAliasesByDirectory ??
                new Dictionary<string, string[]>(StringComparer.Ordinal);
            AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory =
                assemblyScopedCurrentFirstPartyToolsAliasesByDirectory ??
                new Dictionary<string, string[]>(StringComparer.Ordinal);
            AssemblyDeclaredTypeNamesByDirectory = assemblyDeclaredTypeNamesByDirectory ??
                new Dictionary<string, string[]>(StringComparer.Ordinal);
            ToolContractsReferenceAssemblyDirectories = toolContractsReferenceAssemblyDirectories ??
                new HashSet<string>(StringComparer.Ordinal);
            ApplicationReferenceAssemblyDirectories = applicationReferenceAssemblyDirectories ??
                new HashSet<string>(StringComparer.Ordinal);
            DomainReferenceAssemblyDirectories = domainReferenceAssemblyDirectories ??
                new HashSet<string>(StringComparer.Ordinal);
            FirstPartyScreenshotReferenceAssemblyDirectories = firstPartyScreenshotReferenceAssemblyDirectories ??
                new HashSet<string>(StringComparer.Ordinal);
        }

        public List<string> AsmdefDirectories { get; }
        public List<AssemblyReferenceDirectory> AssemblyReferenceDirectories { get; }
        public HashSet<string> LegacyAssemblyDirectories { get; }
        public HashSet<string> AssemblyScopedLegacyDirectories { get; }
        public HashSet<string> AssemblyScopedCurrentToolContractsDirectories { get; }
        public HashSet<string> AssemblyScopedCurrentApplicationDirectories { get; }
        public HashSet<string> AssemblyScopedCurrentFirstPartyToolsDirectories { get; }
        public Dictionary<string, string[]> AssemblyScopedLegacyAliasesByDirectory { get; }
        public Dictionary<string, string[]> AssemblyScopedLegacyToolInfoAliasesByDirectory { get; }
        public Dictionary<string, string[]> AssemblyScopedCurrentApplicationAliasesByDirectory { get; }
        public Dictionary<string, string[]> AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory { get; }
        public Dictionary<string, string[]> AssemblyDeclaredTypeNamesByDirectory { get; }
        public HashSet<string> ToolContractsReferenceAssemblyDirectories { get; }
        public HashSet<string> ApplicationReferenceAssemblyDirectories { get; }
        public HashSet<string> DomainReferenceAssemblyDirectories { get; }
        public HashSet<string> FirstPartyScreenshotReferenceAssemblyDirectories { get; }
    }

    internal readonly struct AssemblyReferenceDirectory
    {
        public AssemblyReferenceDirectory(string sourceDirectory, string targetAssemblyDirectory)
        {
            Debug.Assert(!string.IsNullOrEmpty(sourceDirectory), "sourceDirectory must not be null or empty");
            Debug.Assert(
                !string.IsNullOrEmpty(targetAssemblyDirectory),
                "targetAssemblyDirectory must not be null or empty");

            SourceDirectory = sourceDirectory;
            TargetAssemblyDirectory = targetAssemblyDirectory;
        }

        public string SourceDirectory { get; }
        public string TargetAssemblyDirectory { get; }
    }
}
