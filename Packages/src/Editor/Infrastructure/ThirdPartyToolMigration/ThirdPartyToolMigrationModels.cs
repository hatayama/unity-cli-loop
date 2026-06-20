using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public string FilePath { get; }
        public string Content { get; }
    }

    internal readonly struct MigrationPlan
    {
        public static MigrationPlan Empty => new(
            new List<MigrationFileChange>(),
            0,
            MigrationProjectFingerprint.Empty);

        public MigrationPlan(
            List<MigrationFileChange> changes,
            int replacementCount,
            MigrationProjectFingerprint projectFingerprint)
        {
            Debug.Assert(changes != null, "changes must not be null");
            Debug.Assert(replacementCount >= 0, "replacementCount must not be negative");

            Changes = changes ?? throw new ArgumentNullException(nameof(changes));
            ReplacementCount = replacementCount;
            ProjectFingerprint = projectFingerprint;
            ChangedFilePaths = Changes
                .Select(change => change.FilePath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }

        public List<MigrationFileChange> Changes { get; }
        public int ReplacementCount { get; }
        public MigrationProjectFingerprint ProjectFingerprint { get; }
        public List<string> ChangedFilePaths { get; }
    }

    /// <summary>
    /// Captures candidate file state so cached migration plans are reused only while project files match.
    /// </summary>
    internal readonly struct MigrationProjectFingerprint
    {
        public static MigrationProjectFingerprint Empty => new(Array.Empty<MigrationFileFingerprint>());

        private readonly MigrationFileFingerprint[] _fileFingerprints;

        private MigrationProjectFingerprint(MigrationFileFingerprint[] fileFingerprints)
        {
            Debug.Assert(fileFingerprints != null, "fileFingerprints must not be null");

            _fileFingerprints = fileFingerprints;
        }

        public static MigrationProjectFingerprint CaptureFromInventory(ProjectFileInventory inventory)
        {
            Debug.Assert(inventory != null, "inventory must not be null");

            List<string> filePaths = new();
            filePaths.AddRange(inventory.CSharpFilePaths);
            filePaths.AddRange(inventory.AsmdefFilePaths);
            filePaths.AddRange(inventory.AsmrefFilePaths);
            foreach (string asmdefFilePath in inventory.AsmdefFilePaths)
            {
                filePaths.Add(asmdefFilePath + ".meta");
            }

            filePaths.Sort(StringComparer.Ordinal);

            MigrationFileFingerprint[] fileFingerprints = new MigrationFileFingerprint[filePaths.Count];
            for (int index = 0; index < filePaths.Count; index++)
            {
                fileFingerprints[index] = MigrationFileFingerprint.Capture(filePaths[index]);
            }

            return new MigrationProjectFingerprint(fileFingerprints);
        }

        public bool Matches(ProjectFileInventory inventory)
        {
            Debug.Assert(inventory != null, "inventory must not be null");

            MigrationProjectFingerprint currentFingerprint = CaptureFromInventory(inventory);
            return MatchesFingerprint(currentFingerprint);
        }

        private bool MatchesFingerprint(MigrationProjectFingerprint other)
        {
            MigrationFileFingerprint[] fileFingerprints = _fileFingerprints ?? Array.Empty<MigrationFileFingerprint>();
            MigrationFileFingerprint[] otherFileFingerprints =
                other._fileFingerprints ?? Array.Empty<MigrationFileFingerprint>();
            if (fileFingerprints.Length != otherFileFingerprints.Length)
            {
                return false;
            }

            for (int index = 0; index < fileFingerprints.Length; index++)
            {
                if (!fileFingerprints[index].HasSameValuesAs(otherFileFingerprints[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Captures one candidate file's identity and modification state for migration plan cache validation.
    /// </summary>
    internal readonly struct MigrationFileFingerprint
    {
        private const int ContentHashBufferSize = 8192;
        private const ulong ContentHashOffsetBasis = 14695981039346656037UL;
        private const ulong ContentHashPrime = 1099511628211UL;

        private MigrationFileFingerprint(
            string filePath,
            bool exists,
            long length,
            long lastWriteTimeUtcTicks,
            ulong contentHash)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(length >= 0, "length must not be negative");
            Debug.Assert(lastWriteTimeUtcTicks >= 0, "lastWriteTimeUtcTicks must not be negative");

            FilePath = filePath;
            Exists = exists;
            Length = length;
            LastWriteTimeUtcTicks = lastWriteTimeUtcTicks;
            ContentHash = contentHash;
        }

        public string FilePath { get; }
        public bool Exists { get; }
        public long Length { get; }
        public long LastWriteTimeUtcTicks { get; }
        public ulong ContentHash { get; }

        public static MigrationFileFingerprint Capture(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            FileInfo fileInfo = new(filePath);
            if (!fileInfo.Exists)
            {
                return new MigrationFileFingerprint(
                    filePath,
                    false,
                    0,
                    0,
                    0);
            }

            return new MigrationFileFingerprint(
                filePath,
                true,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc.Ticks,
                CaptureContentHash(filePath));
        }

        public bool HasSameValuesAs(MigrationFileFingerprint other)
        {
            return string.Equals(FilePath, other.FilePath, StringComparison.Ordinal) &&
                Exists == other.Exists &&
                Length == other.Length &&
                LastWriteTimeUtcTicks == other.LastWriteTimeUtcTicks &&
                ContentHash == other.ContentHash;
        }

        private static ulong CaptureContentHash(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            byte[] buffer = new byte[ContentHashBufferSize];
            ulong contentHash = ContentHashOffsetBasis;
            using FileStream stream = File.OpenRead(filePath);
            while (true)
            {
                int readByteCount = stream.Read(buffer, 0, buffer.Length);
                if (readByteCount == 0)
                {
                    return contentHash;
                }

                for (int index = 0; index < readByteCount; index++)
                {
                    unchecked
                    {
                        contentHash ^= buffer[index];
                        contentHash *= ContentHashPrime;
                    }
                }
            }
        }
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

            AsmdefDirectories = asmdefDirectories ??
                throw new ArgumentNullException(nameof(asmdefDirectories));
            AssemblyReferenceDirectories = assemblyReferenceDirectories ??
                throw new ArgumentNullException(nameof(assemblyReferenceDirectories));
            LegacyAssemblyDirectories = legacyAssemblyDirectories ??
                throw new ArgumentNullException(nameof(legacyAssemblyDirectories));
            AssemblyScopedLegacyDirectories = assemblyScopedLegacyDirectories ??
                throw new ArgumentNullException(nameof(assemblyScopedLegacyDirectories));
            AssemblyScopedCurrentToolContractsDirectories = assemblyScopedCurrentToolContractsDirectories ??
                throw new ArgumentNullException(nameof(assemblyScopedCurrentToolContractsDirectories));
            AssemblyScopedCurrentApplicationDirectories = assemblyScopedCurrentApplicationDirectories ??
                throw new ArgumentNullException(nameof(assemblyScopedCurrentApplicationDirectories));
            AssemblyScopedCurrentFirstPartyToolsDirectories = assemblyScopedCurrentFirstPartyToolsDirectories ??
                throw new ArgumentNullException(nameof(assemblyScopedCurrentFirstPartyToolsDirectories));
            AssemblyScopedLegacyAliasesByDirectory = assemblyScopedLegacyAliasesByDirectory ??
                throw new ArgumentNullException(nameof(assemblyScopedLegacyAliasesByDirectory));
            AssemblyScopedLegacyToolInfoAliasesByDirectory = assemblyScopedLegacyToolInfoAliasesByDirectory ??
                throw new ArgumentNullException(nameof(assemblyScopedLegacyToolInfoAliasesByDirectory));
            AssemblyScopedCurrentApplicationAliasesByDirectory =
                assemblyScopedCurrentApplicationAliasesByDirectory ??
                throw new ArgumentNullException(nameof(assemblyScopedCurrentApplicationAliasesByDirectory));
            AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory =
                assemblyScopedCurrentFirstPartyToolsAliasesByDirectory ??
                throw new ArgumentNullException(nameof(assemblyScopedCurrentFirstPartyToolsAliasesByDirectory));
            AssemblyDeclaredTypeNamesByDirectory = assemblyDeclaredTypeNamesByDirectory ??
                throw new ArgumentNullException(nameof(assemblyDeclaredTypeNamesByDirectory));
            ToolContractsReferenceAssemblyDirectories = toolContractsReferenceAssemblyDirectories ??
                throw new ArgumentNullException(nameof(toolContractsReferenceAssemblyDirectories));
            ApplicationReferenceAssemblyDirectories = applicationReferenceAssemblyDirectories ??
                throw new ArgumentNullException(nameof(applicationReferenceAssemblyDirectories));
            DomainReferenceAssemblyDirectories = domainReferenceAssemblyDirectories ??
                throw new ArgumentNullException(nameof(domainReferenceAssemblyDirectories));
            FirstPartyScreenshotReferenceAssemblyDirectories = firstPartyScreenshotReferenceAssemblyDirectories ??
                throw new ArgumentNullException(nameof(firstPartyScreenshotReferenceAssemblyDirectories));
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
