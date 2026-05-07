using System;
using System.Collections.Generic;

namespace UnityCliLoop.DeadCodeScanner
{
    /// <summary>
    /// Defines how far the scanner is allowed to report symbols beyond private implementation details.
    /// </summary>
    public enum ScanScope
    {
        Private,
        Internal,
        Public
    }

    /// <summary>
    /// Defines the report shape emitted by the scanner command.
    /// </summary>
    public enum ReportFormat
    {
        Table,
        Json
    }

    /// <summary>
    /// Separates direct deletion candidates from symbols that need human review.
    /// </summary>
    public enum DeadCodeCategory
    {
        Unused,
        TestOnly,
        PublicCandidate,
        UnusedPrivateMember,
        UnusedLocal,
        KeptByUnityOrReflection
    }

    /// <summary>
    /// Carries all user-selected scanner filters so analysis and reporting stay deterministic.
    /// </summary>
    public sealed class ScanOptions
    {
        public string RootPath { get; }
        public ScanScope Scope { get; }
        public bool IncludeTypes { get; }
        public bool IncludeMembers { get; }
        public bool IncludeLocals { get; }
        public bool IncludeTestOnly { get; }
        public bool IncludeKept { get; }
        public ReportFormat Format { get; }
        public bool FailOnHighConfidence { get; }

        public ScanOptions(
            string rootPath,
            ScanScope scope,
            bool includeTypes,
            bool includeMembers,
            bool includeLocals,
            bool includeTestOnly,
            bool includeKept,
            ReportFormat format,
            bool failOnHighConfidence)
        {
            RootPath = rootPath;
            Scope = scope;
            IncludeTypes = includeTypes;
            IncludeMembers = includeMembers;
            IncludeLocals = includeLocals;
            IncludeTestOnly = includeTestOnly;
            IncludeKept = includeKept;
            Format = format;
            FailOnHighConfidence = failOnHighConfidence;
        }

        public static ScanOptions Default(string rootPath)
        {
            return new ScanOptions(
                rootPath,
                ScanScope.Private,
                includeTypes: true,
                includeMembers: true,
                includeLocals: true,
                includeTestOnly: true,
                includeKept: false,
                ReportFormat.Table,
                failOnHighConfidence: false);
        }
    }

    /// <summary>
    /// Represents one scanner finding with enough source context for a reviewer to decide deletion.
    /// </summary>
    public sealed class DeadCodeIssue
    {
        public DeadCodeCategory Category { get; }
        public string SymbolKind { get; }
        public string Accessibility { get; }
        public string FullName { get; }
        public string AssemblyName { get; }
        public string FilePath { get; }
        public int Line { get; }
        public int ProductionReferenceCount { get; }
        public int NonProductionReferenceCount { get; }
        public string Reason { get; }

        public DeadCodeIssue(
            DeadCodeCategory category,
            string symbolKind,
            string accessibility,
            string fullName,
            string assemblyName,
            string filePath,
            int line,
            int productionReferenceCount,
            int nonProductionReferenceCount,
            string reason)
        {
            Category = category;
            SymbolKind = symbolKind;
            Accessibility = accessibility;
            FullName = fullName;
            AssemblyName = assemblyName;
            FilePath = filePath;
            Line = line;
            ProductionReferenceCount = productionReferenceCount;
            NonProductionReferenceCount = nonProductionReferenceCount;
            Reason = reason;
        }

        public bool IsHighConfidenceDeletionCandidate()
        {
            return Category == DeadCodeCategory.Unused
                || Category == DeadCodeCategory.UnusedPrivateMember
                || Category == DeadCodeCategory.UnusedLocal;
        }
    }

    /// <summary>
    /// Stores an assembly definition and the C# files owned by its nearest asmdef boundary.
    /// </summary>
    public sealed class AsmdefProjectInfo
    {
        public string Name { get; }
        public string DirectoryPath { get; }
        public string[] References { get; }
        public string[] PreprocessorSymbols { get; }
        public string[] SourceFiles { get; }

        public AsmdefProjectInfo(
            string name,
            string directoryPath,
            string[] references,
            string[] preprocessorSymbols,
            string[] sourceFiles)
        {
            Name = name;
            DirectoryPath = directoryPath;
            References = references;
            PreprocessorSymbols = preprocessorSymbols;
            SourceFiles = sourceFiles;
        }
    }

    /// <summary>
    /// Keeps Roslyn project identity together with whether the project belongs to package production code.
    /// </summary>
    public sealed class ProjectAnalysisInfo
    {
        public Microsoft.CodeAnalysis.ProjectId ProjectId { get; }
        public bool IsProduction { get; }

        public ProjectAnalysisInfo(Microsoft.CodeAnalysis.ProjectId projectId, bool isProduction)
        {
            ProjectId = projectId;
            IsProduction = isProduction;
        }
    }

    /// <summary>
    /// Bundles the constructed Roslyn solution and the project metadata needed for result classification.
    /// </summary>
    public sealed class WorkspaceBuildResult
    {
        public Microsoft.CodeAnalysis.Solution Solution { get; }
        public IReadOnlyDictionary<Microsoft.CodeAnalysis.ProjectId, ProjectAnalysisInfo> Projects { get; }

        public WorkspaceBuildResult(
            Microsoft.CodeAnalysis.Solution solution,
            IReadOnlyDictionary<Microsoft.CodeAnalysis.ProjectId, ProjectAnalysisInfo> projects)
        {
            Solution = solution;
            Projects = projects;
        }
    }
}
