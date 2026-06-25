using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeQuality.Analyzers.Maintainability.CodeMetrics;

namespace UnityCliLoop.CodeComplexity
{
    /// <summary>
    /// Runs Microsoft's CA1502 analyzer against Unity package source without Unity-generated project files.
    /// </summary>
    public sealed class CodeComplexityAnalyzerRunner
    {
        private const string ComplexityRuleId = "CA1502";

        private static readonly string[] DefaultPreprocessorSymbols =
        {
            "UNITY_EDITOR",
            "UNITY_EDITOR_OSX",
            "UNITY_2022_3_OR_NEWER",
            "UNITY_6000_0_OR_NEWER",
            "UNITY_6000_3_OR_NEWER",
            "UNITY_6000_4_OR_NEWER",
            "ULOOP_HAS_INPUT_SYSTEM",
            "ULOOP_DEBUG",
            "ULOOP_HAS_TEST_FRAMEWORK"
        };

        public async Task<IReadOnlyList<CodeComplexityIssue>> AnalyzeAsync(CodeComplexityOptions options, CancellationToken ct)
        {
            Debug.Assert(options.MaxComplexity > 0, "Command-line parsing must reject non-positive thresholds.");

            SourceFileSet fileSet = SourceFileCollector.Collect(options.RootPath);
            string[] sourceFiles = CreateSourceFileList(fileSet, options.IncludeNonProduction);
            if (sourceFiles.Length == 0)
            {
                return Array.Empty<CodeComplexityIssue>();
            }

            Compilation compilation = CreateCompilation(sourceFiles);
            ImmutableArray<DiagnosticAnalyzer> analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new CodeMetricsAnalyzer());
            AnalyzerOptions analyzerOptions = new(ImmutableArray.Create<AdditionalText>(CreateCodeMetricsConfig(options.MaxComplexity)));
            CompilationWithAnalyzersOptions analyzerRunOptions = new(
                analyzerOptions,
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false);
            CompilationWithAnalyzers compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, analyzerRunOptions);
            ImmutableArray<Diagnostic> diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

            return diagnostics
                .Where(diagnostic => diagnostic.Id == ComplexityRuleId)
                .Select(diagnostic => CreateIssue(diagnostic, options.RootPath))
                .OrderBy(issue => issue.FilePath, StringComparer.Ordinal)
                .ThenBy(issue => issue.Line)
                .ThenBy(issue => issue.Column)
                .ThenBy(issue => issue.Message, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] CreateSourceFileList(SourceFileSet fileSet, bool includeNonProduction)
        {
            if (!includeNonProduction)
            {
                return fileSet.ProductionFiles.ToArray();
            }

            return fileSet.ProductionFiles
                .Concat(fileSet.NonProductionFiles)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static Compilation CreateCompilation(IReadOnlyList<string> sourceFiles)
        {
            List<SyntaxTree> syntaxTrees = new();
            foreach (string sourceFile in sourceFiles)
            {
                SourceText sourceText = SourceText.From(File.ReadAllText(sourceFile));
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                    sourceText,
                    CreateParseOptions(),
                    path: sourceFile));
            }

            Dictionary<string, ReportDiagnostic> diagnosticOptions = new(StringComparer.Ordinal)
            {
                [ComplexityRuleId] = ReportDiagnostic.Warn
            };

            return CSharpCompilation.Create(
                "UnityCliLoop.CodeComplexity.Analysis",
                syntaxTrees,
                CreateMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithSpecificDiagnosticOptions(diagnosticOptions));
        }

        private static CSharpParseOptions CreateParseOptions()
        {
            return CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Latest)
                .WithPreprocessorSymbols(DefaultPreprocessorSymbols);
        }

        private static ImmutableArray<MetadataReference> CreateMetadataReferences()
        {
            string? trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrEmpty(trustedPlatformAssemblies))
            {
                return ImmutableArray<MetadataReference>.Empty;
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Where(File.Exists)
                .Distinct(StringComparer.Ordinal)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
                .ToImmutableArray();
        }

        private static CodeMetricsAdditionalText CreateCodeMetricsConfig(int maxComplexity)
        {
            string defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "CodeMetricsConfig.txt");
            if (maxComplexity == 15 && File.Exists(defaultConfigPath))
            {
                return CodeMetricsAdditionalText.FromFile(defaultConfigPath);
            }

            return CodeMetricsAdditionalText.FromThreshold(maxComplexity);
        }

        private static CodeComplexityIssue CreateIssue(Diagnostic diagnostic, string rootPath)
        {
            FileLinePositionSpan lineSpan = diagnostic.Location.GetLineSpan();
            LinePosition startLinePosition = lineSpan.StartLinePosition;

            return new CodeComplexityIssue(
                diagnostic.Id,
                diagnostic.Severity.ToString(),
                CreateReportPath(rootPath, lineSpan.Path),
                startLinePosition.Line + 1,
                startLinePosition.Character + 1,
                diagnostic.GetMessage());
        }

        private static string CreateReportPath(string rootPath, string diagnosticPath)
        {
            if (string.IsNullOrEmpty(diagnosticPath))
            {
                return diagnosticPath;
            }

            string fullRootPath = Path.GetFullPath(rootPath);
            string fullDiagnosticPath = Path.GetFullPath(diagnosticPath);
            string relativePath = Path.GetRelativePath(fullRootPath, fullDiagnosticPath);
            return relativePath
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
        }
    }
}
