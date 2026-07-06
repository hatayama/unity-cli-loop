using System;
using System.Collections.Generic;
using System.Diagnostics;

using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyReferenceResolver;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyScopedNameMap;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Accumulates assembly-scoped facts used by migration planning and fast target scanning.
    /// </summary>
    internal sealed class ThirdPartyToolMigrationAssemblyUsageScanState
    {
        private readonly string _projectRoot;

        public ThirdPartyToolMigrationAssemblyUsageScanState(
            string projectRoot,
            List<string> asmdefDirectories,
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
            Debug.Assert(
                assemblyReferenceDirectories != null,
                "assemblyReferenceDirectories must not be null");

            _projectRoot = projectRoot;
            AsmdefDirectories = asmdefDirectories;
            AssemblyReferenceDirectories = assemblyReferenceDirectories;
        }

        public List<string> AsmdefDirectories { get; }
        public List<AssemblyReferenceDirectory> AssemblyReferenceDirectories { get; }
        public HashSet<string> LegacyAssemblyDirectories { get; } = new(StringComparer.Ordinal);
        public HashSet<string> AssemblyScopedLegacyDirectories { get; } = new(StringComparer.Ordinal);
        public HashSet<string> AssemblyScopedCurrentToolContractsDirectories { get; } = new(StringComparer.Ordinal);
        public HashSet<string> AssemblyScopedCurrentApplicationDirectories { get; } = new(StringComparer.Ordinal);
        public HashSet<string> AssemblyScopedCurrentDomainDirectories { get; } = new(StringComparer.Ordinal);
        public HashSet<string> AssemblyScopedCurrentFirstPartyToolsDirectories { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> AssemblyScopedLegacyAliasesByDirectory { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> AssemblyScopedLegacyToolInfoAliasesByDirectory { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> AssemblyScopedCurrentApplicationAliasesByDirectory { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> AssemblyScopedCurrentDomainAliasesByDirectory { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> AssemblyDeclaredTypeNamesByDirectory { get; } =
            new(StringComparer.Ordinal);
        public HashSet<string> RegistrarAssemblyDirectories { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ToolContractsReferenceAssemblyDirectories { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ApplicationReferenceAssemblyDirectories { get; } = new(StringComparer.Ordinal);
        public HashSet<string> DomainReferenceAssemblyDirectories { get; } = new(StringComparer.Ordinal);
        public HashSet<string> FirstPartyScreenshotReferenceAssemblyDirectories { get; } =
            new(StringComparer.Ordinal);

        public bool HasReferenceRequirements =>
            ToolContractsReferenceAssemblyDirectories.Count > 0 ||
            ApplicationReferenceAssemblyDirectories.Count > 0 ||
            DomainReferenceAssemblyDirectories.Count > 0 ||
            FirstPartyScreenshotReferenceAssemblyDirectories.Count > 0;

        public bool RecordInitialSourceFacts(string source, string csharpFilePath)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(csharpFilePath), "csharpFilePath must not be null or empty");

            if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
            {
                return false;
            }

            string assemblyDirectory = FindAssemblyDirectory(csharpFilePath);
            AddAssemblyScopedNames(
                AssemblyDeclaredTypeNamesByDirectory,
                assemblyDirectory,
                ThirdPartyToolMigrationRules.GetDeclaredTypeNames(source));
            RecordLegacySourceFacts(
                source,
                assemblyDirectory,
                recordGlobalUsingAsLegacyAssemblyTarget: false);
            RecordCurrentDomainSourceFacts(source, assemblyDirectory);
            RecordCurrentToolContractsSourceFacts(source, assemblyDirectory);
            RecordCurrentApplicationSourceFacts(source, assemblyDirectory);
            RecordCurrentFirstPartyToolsSourceFacts(source, assemblyDirectory);
            RecordRegistrarSourceFacts(source, assemblyDirectory);
            RecordDomainMetadataSourceFacts(source, assemblyDirectory);
            return true;
        }

        public bool RecordTargetScanInitialSourceFacts(string source, string csharpFilePath)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(csharpFilePath), "csharpFilePath must not be null or empty");

            if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
            {
                return false;
            }

            string assemblyDirectory = FindAssemblyDirectory(csharpFilePath);
            AddAssemblyScopedNames(
                AssemblyDeclaredTypeNamesByDirectory,
                assemblyDirectory,
                ThirdPartyToolMigrationRules.GetDeclaredTypeNames(source));
            RecordLegacySourceFacts(
                source,
                assemblyDirectory,
                recordGlobalUsingAsLegacyAssemblyTarget: true);
            RecordCurrentDomainSourceFacts(source, assemblyDirectory);
            RecordCurrentToolContractsSourceFacts(source, assemblyDirectory);
            RecordCurrentApplicationSourceFacts(source, assemblyDirectory);
            RecordCurrentFirstPartyToolsSourceFacts(source, assemblyDirectory);
            return true;
        }

        public void RecordReferenceRequirements(string source, string csharpFilePath)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(csharpFilePath), "csharpFilePath must not be null or empty");

            if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
            {
                return;
            }

            string assemblyDirectory = FindAssemblyDirectory(csharpFilePath);
            string[] legacyAssemblyAliases = GetAssemblyScopedNames(
                AssemblyScopedLegacyAliasesByDirectory,
                assemblyDirectory);
            string[] assemblyDeclaredTypeNames = GetAssemblyScopedNames(
                AssemblyDeclaredTypeNamesByDirectory,
                assemblyDirectory);
            bool hasLegacyCSharpApi = ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source);
            RecordToolContractsRequirement(
                source,
                assemblyDirectory,
                legacyAssemblyAliases,
                assemblyDeclaredTypeNames,
                hasLegacyCSharpApi);
            RecordRegistrarDomainReturnRequirement(source, assemblyDirectory, legacyAssemblyAliases);
            RecordDomainContractRequirement(source, assemblyDirectory);
            RecordFirstPartyScreenshotRequirement(
                source,
                assemblyDirectory,
                legacyAssemblyAliases,
                assemblyDeclaredTypeNames,
                hasLegacyCSharpApi);
            RecordEditorWindowCaptureTimeoutRequirement(
                source,
                assemblyDirectory,
                legacyAssemblyAliases,
                assemblyDeclaredTypeNames,
                hasLegacyCSharpApi);
        }

        public MigrationAssemblyUsage CreateUsage()
        {
            AddRegistrarApplicationReferences();
            return CreateMigrationAssemblyUsage(
                AsmdefDirectories,
                AssemblyReferenceDirectories,
                LegacyAssemblyDirectories,
                AssemblyScopedLegacyDirectories,
                AssemblyScopedCurrentToolContractsDirectories,
                AssemblyScopedCurrentApplicationDirectories,
                AssemblyScopedCurrentDomainDirectories,
                AssemblyScopedCurrentFirstPartyToolsDirectories,
                AssemblyScopedLegacyAliasesByDirectory,
                AssemblyScopedLegacyToolInfoAliasesByDirectory,
                AssemblyScopedCurrentApplicationAliasesByDirectory,
                AssemblyScopedCurrentDomainAliasesByDirectory,
                AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                AssemblyDeclaredTypeNamesByDirectory,
                ToolContractsReferenceAssemblyDirectories,
                ApplicationReferenceAssemblyDirectories,
                DomainReferenceAssemblyDirectories,
                FirstPartyScreenshotReferenceAssemblyDirectories);
        }

        public MigrationAssemblyUsage CreateReferenceRequirementUsage()
        {
            return new MigrationAssemblyUsage(
                AsmdefDirectories,
                AssemblyReferenceDirectories,
                LegacyAssemblyDirectories,
                AssemblyScopedLegacyDirectories,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                CreateAssemblyScopedLegacyAliasesByDirectory(AssemblyScopedLegacyAliasesByDirectory),
                CreateAssemblyScopedLegacyAliasesByDirectory(AssemblyScopedLegacyToolInfoAliasesByDirectory),
                CreateAssemblyScopedNamesByDirectory(AssemblyScopedCurrentApplicationAliasesByDirectory),
                CreateAssemblyScopedNamesByDirectory(AssemblyScopedCurrentDomainAliasesByDirectory),
                CreateAssemblyScopedNamesByDirectory(AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory),
                CreateAssemblyScopedNamesByDirectory(AssemblyDeclaredTypeNamesByDirectory),
                ToolContractsReferenceAssemblyDirectories,
                ApplicationReferenceAssemblyDirectories,
                DomainReferenceAssemblyDirectories,
                FirstPartyScreenshotReferenceAssemblyDirectories);
        }

        private string FindAssemblyDirectory(string csharpFilePath)
        {
            return FindNearestAssemblyDirectory(
                csharpFilePath,
                AsmdefDirectories,
                AssemblyReferenceDirectories,
                _projectRoot);
        }

        private void RecordLegacySourceFacts(
            string source,
            string assemblyDirectory,
            bool recordGlobalUsingAsLegacyAssemblyTarget)
        {
            if (ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source))
            {
                LegacyAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsLegacyGlobalUsing(source))
            {
                if (recordGlobalUsingAsLegacyAssemblyTarget)
                {
                    LegacyAssemblyDirectories.Add(assemblyDirectory);
                }

                AssemblyScopedLegacyDirectories.Add(assemblyDirectory);
                AddAssemblyScopedLegacyAliases(
                    AssemblyScopedLegacyAliasesByDirectory,
                    assemblyDirectory,
                    ThirdPartyToolMigrationRules.GetLegacyGlobalNamespaceAliases(source));
            }

            if (ThirdPartyToolMigrationRules.ContainsLegacyGlobalToolInfoTypeAlias(source))
            {
                AddAssemblyScopedLegacyAliases(
                    AssemblyScopedLegacyToolInfoAliasesByDirectory,
                    assemblyDirectory,
                    ThirdPartyToolMigrationRules.GetLegacyGlobalToolInfoTypeAliases(source));
            }
        }

        private void RecordCurrentDomainSourceFacts(string source, string assemblyDirectory)
        {
            if (ThirdPartyToolMigrationRules.ContainsCurrentDomainGlobalUsing(source))
            {
                AssemblyScopedCurrentDomainDirectories.Add(assemblyDirectory);
                DomainReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsCurrentDomainUsing(source))
            {
                DomainReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsCurrentDomainNamespaceAlias(source))
            {
                DomainReferenceAssemblyDirectories.Add(assemblyDirectory);
                AddAssemblyScopedNames(
                    AssemblyScopedCurrentDomainAliasesByDirectory,
                    assemblyDirectory,
                    ThirdPartyToolMigrationRules.GetCurrentDomainGlobalNamespaceAliases(source));
            }
        }

        private void RecordCurrentToolContractsSourceFacts(string source, string assemblyDirectory)
        {
            if (ThirdPartyToolMigrationRules.ContainsCurrentToolContractsGlobalUsing(source))
            {
                AssemblyScopedCurrentToolContractsDirectories.Add(assemblyDirectory);
            }
        }

        private void RecordCurrentApplicationSourceFacts(string source, string assemblyDirectory)
        {
            if (ThirdPartyToolMigrationRules.ContainsCurrentApplicationGlobalUsing(source))
            {
                AssemblyScopedCurrentApplicationDirectories.Add(assemblyDirectory);
                ApplicationReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsCurrentApplicationUsing(source))
            {
                ApplicationReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsCurrentApplicationNamespaceAlias(source))
            {
                ApplicationReferenceAssemblyDirectories.Add(assemblyDirectory);
                AddAssemblyScopedNames(
                    AssemblyScopedCurrentApplicationAliasesByDirectory,
                    assemblyDirectory,
                    ThirdPartyToolMigrationRules.GetCurrentApplicationGlobalNamespaceAliases(source));
            }
        }

        private void RecordCurrentFirstPartyToolsSourceFacts(string source, string assemblyDirectory)
        {
            if (ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyToolsGlobalUsing(source))
            {
                AssemblyScopedCurrentFirstPartyToolsDirectories.Add(assemblyDirectory);
                FirstPartyScreenshotReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyToolsNamespaceAlias(source))
            {
                FirstPartyScreenshotReferenceAssemblyDirectories.Add(assemblyDirectory);
                AddAssemblyScopedNames(
                    AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                    assemblyDirectory,
                    ThirdPartyToolMigrationRules.GetCurrentFirstPartyToolsGlobalNamespaceAliases(source));
            }
        }

        private void RecordRegistrarSourceFacts(string source, string assemblyDirectory)
        {
            if (ThirdPartyToolMigrationRules.ContainsLegacyRegistrarApi(source))
            {
                ToolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsCurrentRegistrarApi(source))
            {
                RegistrarAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsRegistrarDomainReturnApi(source))
            {
                ToolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source))
            {
                ToolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
            }
        }

        private void RecordDomainMetadataSourceFacts(string source, string assemblyDirectory)
        {
            if (ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApi(source))
            {
                DomainReferenceAssemblyDirectories.Add(assemblyDirectory);
                ToolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
            }
        }

        private void RecordToolContractsRequirement(
            string source,
            string assemblyDirectory,
            string[] legacyAssemblyAliases,
            string[] assemblyDeclaredTypeNames,
            bool hasLegacyCSharpApi)
        {
            string[] currentApplicationAssemblyAliases = GetAssemblyScopedNames(
                AssemblyScopedCurrentApplicationAliasesByDirectory,
                assemblyDirectory);
            bool hasCurrentApplicationSourceTarget =
                ThirdPartyToolMigrationRules.ContainsCurrentApplicationApiForAssembly(
                    source,
                    AssemblyScopedCurrentApplicationDirectories.Contains(assemblyDirectory),
                    currentApplicationAssemblyAliases,
                    assemblyDeclaredTypeNames);
            if (!ContainsToolContractsReferenceRequirement(
                    source,
                    assemblyDirectory,
                    legacyAssemblyAliases,
                    currentApplicationAssemblyAliases,
                    assemblyDeclaredTypeNames,
                    hasLegacyCSharpApi,
                    hasCurrentApplicationSourceTarget))
            {
                return;
            }

            ToolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
        }

        private bool ContainsToolContractsReferenceRequirement(
            string source,
            string assemblyDirectory,
            string[] legacyAssemblyAliases,
            string[] currentApplicationAssemblyAliases,
            string[] assemblyDeclaredTypeNames,
            bool hasLegacyCSharpApi,
            bool hasCurrentApplicationSourceTarget)
        {
            bool canUseLegacyAssemblyApi =
                AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) ||
                hasLegacyCSharpApi ||
                ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source) ||
                AssemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory) ||
                AssemblyScopedCurrentApplicationDirectories.Contains(assemblyDirectory);
            return ThirdPartyToolMigrationRules.ContainsLegacyRegistrarApiForAssembly(
                    source,
                    AssemblyScopedLegacyDirectories.Contains(assemblyDirectory),
                    legacyAssemblyAliases) ||
                ThirdPartyToolMigrationRules.ContainsLegacyApplicationApiForAssembly(
                    source,
                    canUseLegacyAssemblyApi,
                    legacyAssemblyAliases,
                    currentApplicationAssemblyAliases,
                    assemblyDeclaredTypeNames) ||
                hasCurrentApplicationSourceTarget;
        }

        private void RecordRegistrarDomainReturnRequirement(
            string source,
            string assemblyDirectory,
            string[] legacyAssemblyAliases)
        {
            if (!ThirdPartyToolMigrationRules.ContainsRegistrarDomainReturnApiForAssembly(
                    source,
                    AssemblyScopedLegacyDirectories.Contains(assemblyDirectory),
                    legacyAssemblyAliases))
            {
                return;
            }

            ToolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
        }

        private void RecordDomainContractRequirement(string source, string assemblyDirectory)
        {
            string[] currentDomainAssemblyAliases = GetAssemblyScopedNames(
                AssemblyScopedCurrentDomainAliasesByDirectory,
                assemblyDirectory);
            string[] currentDomainNamespaceAliases =
                ThirdPartyToolMigrationAliasRules.GetCombinedCurrentDomainNamespaceAliases(
                    source,
                    currentDomainAssemblyAliases);
            if (!ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApiForAssembly(
                    source,
                    AssemblyScopedCurrentDomainDirectories.Contains(assemblyDirectory)) &&
                !ThirdPartyToolMigrationRules.ContainsCurrentDomainContractAliasReference(
                    source,
                    currentDomainNamespaceAliases))
            {
                return;
            }

            ToolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
        }

        private void RecordFirstPartyScreenshotRequirement(
            string source,
            string assemblyDirectory,
            string[] legacyAssemblyAliases,
            string[] assemblyDeclaredTypeNames,
            bool hasLegacyCSharpApi)
        {
            string[] currentFirstPartyToolsAssemblyAliases = GetAssemblyScopedNames(
                AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                assemblyDirectory);
            bool hasCurrentFirstPartyScreenshotReferenceRequirement =
                ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotApiForAssembly(
                    source,
                    AssemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory),
                    currentFirstPartyToolsAssemblyAliases,
                    assemblyDeclaredTypeNames);
            if (ContainsFirstPartyScreenshotToolContractsRequirement(
                source,
                assemblyDirectory,
                legacyAssemblyAliases,
                currentFirstPartyToolsAssemblyAliases,
                assemblyDeclaredTypeNames,
                hasLegacyCSharpApi,
                hasCurrentFirstPartyScreenshotReferenceRequirement))
            {
                ToolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (hasCurrentFirstPartyScreenshotReferenceRequirement)
            {
                FirstPartyScreenshotReferenceAssemblyDirectories.Add(assemblyDirectory);
            }
        }

        private bool ContainsFirstPartyScreenshotToolContractsRequirement(
            string source,
            string assemblyDirectory,
            string[] legacyAssemblyAliases,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames,
            bool hasLegacyCSharpApi,
            bool hasCurrentFirstPartyScreenshotReferenceRequirement)
        {
            bool hasAssemblyScopedCurrentToolContractsUsing =
                AssemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory);
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing =
                AssemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory);
            return ThirdPartyToolMigrationRules.ContainsLegacyFirstPartyScreenshotApiForAssembly(
                    source,
                    AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) ||
                    hasLegacyCSharpApi ||
                    ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source) ||
                    hasAssemblyScopedCurrentToolContractsUsing,
                    legacyAssemblyAliases,
                    assemblyDeclaredTypeNames) ||
                ThirdPartyToolMigrationRules.ContainsLegacyEditorWindowCaptureUtilityMigrationForAssembly(
                    source,
                    AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) || hasLegacyCSharpApi,
                    hasAssemblyScopedCurrentToolContractsUsing,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing,
                    legacyAssemblyAliases,
                    currentFirstPartyToolsAssemblyAliases,
                    assemblyDeclaredTypeNames) ||
                ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotContractApiForAssembly(
                    source,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing,
                    currentFirstPartyToolsAssemblyAliases,
                    assemblyDeclaredTypeNames) ||
                hasCurrentFirstPartyScreenshotReferenceRequirement;
        }

        private void RecordEditorWindowCaptureTimeoutRequirement(
            string source,
            string assemblyDirectory,
            string[] legacyAssemblyAliases,
            string[] assemblyDeclaredTypeNames,
            bool hasLegacyCSharpApi)
        {
            if (!ThirdPartyToolMigrationRules.ContainsLegacyEditorWindowCaptureUtilityTimeoutMigrationForAssembly(
                    source,
                    AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) || hasLegacyCSharpApi,
                    AssemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory),
                    AssemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory),
                    legacyAssemblyAliases,
                    GetAssemblyScopedNames(AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory, assemblyDirectory),
                    assemblyDeclaredTypeNames))
            {
                return;
            }

            ToolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
        }

        private void AddRegistrarApplicationReferences()
        {
            foreach (string registrarAssemblyDirectory in RegistrarAssemblyDirectories)
            {
                ApplicationReferenceAssemblyDirectories.Add(registrarAssemblyDirectory);
            }
        }
    }
}
