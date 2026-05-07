using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace UnityCliLoop.DeadCodeScanner
{
    /// <summary>
    /// Finds unreferenced Unity package symbols while avoiding Unity and reflection entry points.
    /// </summary>
    public sealed class DeadCodeScanner
    {
        private readonly AsmdefWorkspaceBuilder _workspaceBuilder = new();

        public async Task<IReadOnlyList<DeadCodeIssue>> ScanAsync(ScanOptions options, CancellationToken ct)
        {
            WorkspaceBuildResult workspace = _workspaceBuilder.Build(options.RootPath);
            List<DeadCodeIssue> issues = new();

            foreach (Project project in workspace.Solution.Projects)
            {
                ct.ThrowIfCancellationRequested();
                if (!workspace.Projects.TryGetValue(project.Id, out ProjectAnalysisInfo? projectInfo) || !projectInfo.IsProduction)
                {
                    continue;
                }

                Compilation? compilation = await project.GetCompilationAsync(ct);
                if (compilation == null)
                {
                    continue;
                }

                foreach (Document document in project.Documents)
                {
                    SyntaxNode? root = await document.GetSyntaxRootAsync(ct);
                    SemanticModel? semanticModel = await document.GetSemanticModelAsync(ct);
                    if (root == null || semanticModel == null)
                    {
                        continue;
                    }

                    if (options.IncludeTypes)
                    {
                        await ScanTypesAsync(workspace, options, document, root, semanticModel, issues, ct);
                    }

                    if (options.IncludeMembers)
                    {
                        await ScanMembersAsync(workspace, options, document, root, semanticModel, issues, ct);
                    }

                    if (options.IncludeLocals)
                    {
                        ScanLocalVariables(options, document, root, semanticModel, issues);
                    }
                }
            }

            return issues
                .OrderBy(issue => issue.FilePath, StringComparer.Ordinal)
                .ThenBy(issue => issue.Line)
                .ThenBy(issue => issue.FullName, StringComparer.Ordinal)
                .ToArray();
        }

        private static async Task ScanTypesAsync(
            WorkspaceBuildResult workspace,
            ScanOptions options,
            Document document,
            SyntaxNode root,
            SemanticModel semanticModel,
            List<DeadCodeIssue> issues,
            CancellationToken ct)
        {
            IEnumerable<BaseTypeDeclarationSyntax> typeDeclarations = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>();
            foreach (BaseTypeDeclarationSyntax declaration in typeDeclarations)
            {
                ct.ThrowIfCancellationRequested();
                INamedTypeSymbol? symbol = semanticModel.GetDeclaredSymbol(declaration, ct) as INamedTypeSymbol;
                if (symbol == null || !ShouldAnalyzeAccessibility(symbol.DeclaredAccessibility, options.Scope))
                {
                    continue;
                }

                await AddSymbolIssueAsync(workspace, options, document, symbol, "type", issues, ct);
            }

            IEnumerable<DelegateDeclarationSyntax> delegateDeclarations = root.DescendantNodes().OfType<DelegateDeclarationSyntax>();
            foreach (DelegateDeclarationSyntax declaration in delegateDeclarations)
            {
                ct.ThrowIfCancellationRequested();
                INamedTypeSymbol? symbol = semanticModel.GetDeclaredSymbol(declaration, ct) as INamedTypeSymbol;
                if (symbol == null || !ShouldAnalyzeAccessibility(symbol.DeclaredAccessibility, options.Scope))
                {
                    continue;
                }

                await AddSymbolIssueAsync(workspace, options, document, symbol, "delegate", issues, ct);
            }
        }

        private static async Task ScanMembersAsync(
            WorkspaceBuildResult workspace,
            ScanOptions options,
            Document document,
            SyntaxNode root,
            SemanticModel semanticModel,
            List<DeadCodeIssue> issues,
            CancellationToken ct)
        {
            foreach (MemberDeclarationSyntax declaration in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                ct.ThrowIfCancellationRequested();
                foreach (ISymbol symbol in GetDeclaredMemberSymbols(declaration, semanticModel, ct))
                {
                    if (!ShouldAnalyzeMember(symbol, options.Scope))
                    {
                        continue;
                    }

                    await AddSymbolIssueAsync(workspace, options, document, symbol, symbol.Kind.ToString(), issues, ct);
                }
            }
        }

        private static ImmutableArray<ISymbol> GetDeclaredMemberSymbols(
            MemberDeclarationSyntax declaration,
            SemanticModel semanticModel,
            CancellationToken ct)
        {
            if (declaration is FieldDeclarationSyntax fieldDeclaration)
            {
                return fieldDeclaration.Declaration.Variables
                    .Select(variable => semanticModel.GetDeclaredSymbol(variable, ct))
                    .Where(symbol => symbol != null)
                    .Cast<ISymbol>()
                    .ToImmutableArray();
            }

            ISymbol? symbol = semanticModel.GetDeclaredSymbol(declaration, ct);
            if (symbol == null)
            {
                return ImmutableArray<ISymbol>.Empty;
            }

            return ImmutableArray.Create(symbol);
        }

        private static bool ShouldAnalyzeMember(ISymbol symbol, ScanScope scope)
        {
            if (symbol is INamedTypeSymbol)
            {
                return false;
            }

            if (symbol is IMethodSymbol methodSymbol)
            {
                if (methodSymbol.MethodKind != MethodKind.Ordinary
                    && methodSymbol.MethodKind != MethodKind.Constructor)
                {
                    return false;
                }

                if (methodSymbol.IsImplicitlyDeclared || methodSymbol.IsOverride)
                {
                    return false;
                }
            }

            if (symbol.IsImplicitlyDeclared)
            {
                return false;
            }

            return ShouldAnalyzeAccessibility(symbol.DeclaredAccessibility, scope);
        }

        private static async Task AddSymbolIssueAsync(
            WorkspaceBuildResult workspace,
            ScanOptions options,
            Document document,
            ISymbol symbol,
            string symbolKind,
            List<DeadCodeIssue> issues,
            CancellationToken ct)
        {
            KeeperDecision keeperDecision = UnityKeeperClassifier.Classify(symbol);
            if (keeperDecision.IsKept)
            {
                if (options.IncludeKept)
                {
                    issues.Add(CreateIssue(
                        DeadCodeCategory.KeptByUnityOrReflection,
                        symbol,
                        symbolKind,
                        document,
                        0,
                        0,
                        keeperDecision.Reason));
                }

                return;
            }

            ReferenceCounts referenceCounts = await CountReferencesAsync(workspace, symbol, ct);
            if (symbol is INamedTypeSymbol namedTypeSymbol
                && referenceCounts.ProductionCount == 0
                && referenceCounts.NonProductionCount == 0
                && await HasReferencedMemberAsync(workspace, namedTypeSymbol, ct))
            {
                return;
            }

            if (referenceCounts.ProductionCount > 0)
            {
                return;
            }

            if (referenceCounts.NonProductionCount > 0)
            {
                if (!options.IncludeTestOnly)
                {
                    return;
                }

                issues.Add(CreateIssue(
                    DeadCodeCategory.TestOnly,
                    symbol,
                    symbolKind,
                    document,
                    referenceCounts.ProductionCount,
                    referenceCounts.NonProductionCount,
                    "Referenced only outside package production assemblies."));
                return;
            }

            DeadCodeCategory category = CreateUnusedCategory(symbol);
            issues.Add(CreateIssue(
                category,
                symbol,
                symbolKind,
                document,
                referenceCounts.ProductionCount,
                referenceCounts.NonProductionCount,
                "No references were found in production or non-production code."));
        }

        private static async Task<ReferenceCounts> CountReferencesAsync(
            WorkspaceBuildResult workspace,
            ISymbol symbol,
            CancellationToken ct)
        {
            IEnumerable<ReferencedSymbol> references = await SymbolFinder.FindReferencesAsync(symbol, workspace.Solution, ct);
            int productionCount = 0;
            int nonProductionCount = 0;

            foreach (ReferencedSymbol referencedSymbol in references)
            {
                foreach (ReferenceLocation location in referencedSymbol.Locations)
                {
                    if (location.Document == null)
                    {
                        continue;
                    }

                    if (!workspace.Projects.TryGetValue(location.Document.Project.Id, out ProjectAnalysisInfo? projectInfo))
                    {
                        continue;
                    }

                    if (projectInfo.IsProduction)
                    {
                        productionCount++;
                    }
                    else
                    {
                        nonProductionCount++;
                    }
                }
            }

            return new ReferenceCounts(productionCount, nonProductionCount);
        }

        private static async Task<bool> HasReferencedMemberAsync(
            WorkspaceBuildResult workspace,
            INamedTypeSymbol typeSymbol,
            CancellationToken ct)
        {
            foreach (ISymbol member in typeSymbol.GetMembers())
            {
                if (member.IsImplicitlyDeclared || member is INamedTypeSymbol)
                {
                    continue;
                }

                ReferenceCounts referenceCounts = await CountReferencesAsync(workspace, member, ct);
                if (referenceCounts.ProductionCount > 0 || referenceCounts.NonProductionCount > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static DeadCodeCategory CreateUnusedCategory(ISymbol symbol)
        {
            if (symbol.DeclaredAccessibility == Accessibility.Private)
            {
                return DeadCodeCategory.UnusedPrivateMember;
            }

            if (symbol.DeclaredAccessibility == Accessibility.Public
                || symbol.DeclaredAccessibility == Accessibility.Protected
                || symbol.DeclaredAccessibility == Accessibility.Internal
                || symbol.DeclaredAccessibility == Accessibility.ProtectedOrInternal
                || symbol.DeclaredAccessibility == Accessibility.ProtectedAndInternal)
            {
                return DeadCodeCategory.PublicCandidate;
            }

            return DeadCodeCategory.Unused;
        }

        private static DeadCodeIssue CreateIssue(
            DeadCodeCategory category,
            ISymbol symbol,
            string symbolKind,
            Document document,
            int productionReferenceCount,
            int nonProductionReferenceCount,
            string reason)
        {
            FileLinePositionSpan lineSpan = GetLineSpan(symbol);
            string filePath = !string.IsNullOrEmpty(lineSpan.Path) ? lineSpan.Path : document.FilePath ?? string.Empty;
            string assemblyName = symbol.ContainingAssembly?.Name ?? document.Project.Name;
            return new DeadCodeIssue(
                category,
                symbolKind,
                symbol.DeclaredAccessibility.ToString(),
                symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                assemblyName,
                filePath,
                lineSpan.StartLinePosition.Line + 1,
                productionReferenceCount,
                nonProductionReferenceCount,
                reason);
        }

        private static FileLinePositionSpan GetLineSpan(ISymbol symbol)
        {
            Location? location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource);
            if (location == null)
            {
                return default;
            }

            return location.GetLineSpan();
        }

        private static void ScanLocalVariables(
            ScanOptions options,
            Document document,
            SyntaxNode root,
            SemanticModel semanticModel,
            List<DeadCodeIssue> issues)
        {
            foreach (BlockSyntax block in root.DescendantNodes().OfType<BlockSyntax>())
            {
                DataFlowAnalysis dataFlow = semanticModel.AnalyzeDataFlow(block);
                if (!dataFlow.Succeeded)
                {
                    continue;
                }

                foreach (ISymbol localSymbol in dataFlow.VariablesDeclared)
                {
                    if (localSymbol is not ILocalSymbol typedLocalSymbol)
                    {
                        continue;
                    }

                    if (typedLocalSymbol.Name == "_" || IsUsingDeclaration(typedLocalSymbol))
                    {
                        continue;
                    }

                    bool isRead = dataFlow.ReadInside.Any(readSymbol =>
                        SymbolEqualityComparer.Default.Equals(readSymbol, typedLocalSymbol));
                    if (isRead)
                    {
                        continue;
                    }

                    issues.Add(CreateLocalIssue(options, document, typedLocalSymbol));
                }
            }
        }

        private static bool IsUsingDeclaration(ILocalSymbol localSymbol)
        {
            SyntaxReference? syntaxReference = localSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            SyntaxNode? syntaxNode = syntaxReference?.GetSyntax();
            LocalDeclarationStatementSyntax? localDeclaration = syntaxNode?.AncestorsAndSelf()
                .OfType<LocalDeclarationStatementSyntax>()
                .FirstOrDefault();
            return localDeclaration != null && localDeclaration.UsingKeyword.ValueText == "using";
        }

        private static DeadCodeIssue CreateLocalIssue(ScanOptions options, Document document, ILocalSymbol symbol)
        {
            FileLinePositionSpan lineSpan = GetLineSpan(symbol);
            return new DeadCodeIssue(
                DeadCodeCategory.UnusedLocal,
                "local",
                "Local",
                symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                document.Project.Name,
                !string.IsNullOrEmpty(lineSpan.Path) ? lineSpan.Path : document.FilePath ?? string.Empty,
                lineSpan.StartLinePosition.Line + 1,
                0,
                0,
                $"Local variable is never read under scope '{options.Scope}'.");
        }

        private static bool ShouldAnalyzeAccessibility(Accessibility accessibility, ScanScope scope)
        {
            if (accessibility == Accessibility.Private)
            {
                return true;
            }

            if (scope == ScanScope.Private)
            {
                return false;
            }

            if (accessibility == Accessibility.Internal
                || accessibility == Accessibility.ProtectedAndInternal
                || accessibility == Accessibility.ProtectedOrInternal)
            {
                return true;
            }

            if (scope == ScanScope.Internal)
            {
                return false;
            }

            return accessibility == Accessibility.Public || accessibility == Accessibility.Protected;
        }

        private readonly struct ReferenceCounts
        {
            public int ProductionCount { get; }
            public int NonProductionCount { get; }

            public ReferenceCounts(int productionCount, int nonProductionCount)
            {
                ProductionCount = productionCount;
                NonProductionCount = nonProductionCount;
            }
        }
    }
}
