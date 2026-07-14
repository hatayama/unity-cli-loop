using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UnityCliLoop.CodeComplexity
{
    /// <summary>
    /// Finds Task-like awaits in tool assemblies that omit ConfigureAwait(false).
    /// </summary>
    public sealed class ConfigureAwaitGuard
    {
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

        /// <summary>
        /// Scans FirstPartyTools for await expressions of Task/Task&lt;T&gt;/ValueTask/ValueTask&lt;T&gt;
        /// that are missing ConfigureAwait(false).
        /// Custom awaitables (e.g. SwitchToMainThreadAwaitable) are excluded by return type —
        /// intentional main-thread hops are the mechanism, not the hazard.
        /// </summary>
        public IReadOnlyList<ConfigureAwaitViolation> FindMissingConfigureAwait(string repositoryRoot)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(repositoryRoot), "repositoryRoot must not be empty");

            string[] sourceFiles = CollectToolAssemblySourceFiles(repositoryRoot);
            if (sourceFiles.Length == 0)
            {
                return Array.Empty<ConfigureAwaitViolation>();
            }

            CSharpCompilation compilation = CreateCompilation(sourceFiles);
            List<ConfigureAwaitViolation> violations = new();
            foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees)
            {
                if (!IsFirstPartyToolsPath(syntaxTree.FilePath, repositoryRoot))
                {
                    continue;
                }

                SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
                CompilationUnitSyntax root = syntaxTree.GetCompilationUnitRoot();
                foreach (AwaitExpressionSyntax awaitExpression in root.DescendantNodes().OfType<AwaitExpressionSyntax>())
                {
                    if (HasConfigureAwaitFalse(awaitExpression.Expression))
                    {
                        continue;
                    }

                    ITypeSymbol? awaitedType = semanticModel.GetTypeInfo(awaitExpression.Expression).Type;
                    if (awaitedType == null || !IsTaskLike(awaitedType))
                    {
                        // Why skip non-Task awaitables: SwitchToMainThreadAwaitable and similar
                        // intentional main-thread hops are the mechanism, not the hazard.
                        continue;
                    }

                    FileLinePositionSpan lineSpan = awaitExpression.GetLocation().GetLineSpan();
                    violations.Add(new ConfigureAwaitViolation(
                        CreateReportPath(repositoryRoot, syntaxTree.FilePath),
                        lineSpan.StartLinePosition.Line + 1,
                        lineSpan.StartLinePosition.Character + 1,
                        awaitExpression.ToString()));
                }
            }

            return violations
                .OrderBy(violation => violation.FilePath, StringComparer.Ordinal)
                .ThenBy(violation => violation.Line)
                .ThenBy(violation => violation.Column)
                .ToArray();
        }

        private static string[] CollectToolAssemblySourceFiles(string repositoryRoot)
        {
            string firstPartyToolsPath = Path.Combine(repositoryRoot, "Packages", "src", "Editor", "FirstPartyTools");
            string toolContractsPath = Path.Combine(repositoryRoot, "Packages", "src", "Editor", "ToolContracts");
            return CollectCsFiles(firstPartyToolsPath)
                .Concat(CollectCsFiles(toolContractsPath))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static IEnumerable<string> CollectCsFiles(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories);
        }

        private static bool IsFirstPartyToolsPath(string filePath, string repositoryRoot)
        {
            string relativePath = CreateReportPath(repositoryRoot, filePath);
            return relativePath.StartsWith("Packages/src/Editor/FirstPartyTools/", StringComparison.Ordinal);
        }

        private static bool HasConfigureAwaitFalse(ExpressionSyntax expression)
        {
            if (expression is not InvocationExpressionSyntax invocation)
            {
                return false;
            }

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!string.Equals(memberAccess.Name.Identifier.ValueText, "ConfigureAwait", StringComparison.Ordinal))
            {
                return false;
            }

            if (invocation.ArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            ExpressionSyntax argumentExpression = invocation.ArgumentList.Arguments[0].Expression;
            return argumentExpression is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.FalseLiteralExpression);
        }

        private static bool IsTaskLike(ITypeSymbol type)
        {
            ITypeSymbol unbound = type.OriginalDefinition;
            string? fullName = unbound.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return fullName is
                "global::System.Threading.Tasks.Task" or
                "global::System.Threading.Tasks.Task<TResult>" or
                "global::System.Threading.Tasks.ValueTask" or
                "global::System.Threading.Tasks.ValueTask<TResult>";
        }

        private static CSharpCompilation CreateCompilation(IReadOnlyList<string> sourceFiles)
        {
            List<SyntaxTree> syntaxTrees = new();
            foreach (string sourceFile in sourceFiles)
            {
                SourceText sourceText = SourceText.From(File.ReadAllText(sourceFile));
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                    sourceText,
                    CSharpParseOptions.Default
                        .WithLanguageVersion(LanguageVersion.Latest)
                        .WithPreprocessorSymbols(DefaultPreprocessorSymbols),
                    path: sourceFile));
            }

            return CSharpCompilation.Create(
                "UnityCliLoop.ConfigureAwaitGuard.Analysis",
                syntaxTrees,
                CreateMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
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

        private static string CreateReportPath(string rootPath, string filePath)
        {
            string fullRootPath = Path.GetFullPath(rootPath);
            string fullFilePath = Path.GetFullPath(filePath);
            return Path.GetRelativePath(fullRootPath, fullFilePath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
        }
    }

    /// <summary>
    /// One Task-like await that omitted ConfigureAwait(false).
    /// </summary>
    public sealed class ConfigureAwaitViolation
    {
        public string FilePath { get; }
        public int Line { get; }
        public int Column { get; }
        public string AwaitExpression { get; }

        public ConfigureAwaitViolation(string filePath, int line, int column, string awaitExpression)
        {
            FilePath = filePath;
            Line = line;
            Column = column;
            AwaitExpression = awaitExpression;
        }
    }
}
