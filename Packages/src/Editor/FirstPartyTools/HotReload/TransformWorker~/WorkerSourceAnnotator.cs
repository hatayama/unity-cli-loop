using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class WorkerSourceAnnotator
{
    internal static (SyntaxTree SyntaxTree, CompilationUnitSyntax PlainRoot) ParseAndAnnotateSource(
        string sourceText,
        CSharpParseOptions parseOptions,
        string sourcePath,
        List<string> parseErrors)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(sourceText, Encoding.UTF8),
            parseOptions,
            path: sourcePath);

        ImmutableArray<Diagnostic> parseDiagnostics = syntaxTree.GetDiagnostics().ToImmutableArray();
        foreach (Diagnostic diagnostic in parseDiagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                parseErrors.Add(diagnostic.ToString());
            }
        }

        // Why capture plainRoot before annotate: StatementSyntax annotations make
        // SyntaxFactory.AreEquivalent(topLevel:false) return false for some method shapes
        // (long single return / unchecked multi-statement / switch) even when the source text
        // is identical. Baseline comparison must use unannotated nodes on both sides.
        // Why annotate before CSharpCompilation.Create: annotating after GetSemanticModel
        // detaches nodes from the bound tree and ShimBodyRewriter's GetSymbolInfo throws
        // "Syntax node is not within syntax tree". Binding the SemanticModel to the annotated
        // tree keeps rewriter lookups valid while uloop-line annotations ride through to Emit.
        CompilationUnitSyntax plainRoot = syntaxTree.GetCompilationUnitRoot();
        CompilationUnitSyntax annotatedRoot = AnnotateOriginalSourceLines(plainRoot);
        syntaxTree = syntaxTree.WithRootAndOptions(annotatedRoot, syntaxTree.Options);
        return (syntaxTree, plainRoot);
    }

    internal static CompilationUnitSyntax AnnotateOriginalSourceLines(CompilationUnitSyntax root)
    {
        List<SyntaxNode> nodesToAnnotate = new List<SyntaxNode>();
        nodesToAnnotate.AddRange(root.DescendantNodes().OfType<MethodDeclarationSyntax>());
        nodesToAnnotate.AddRange(root.DescendantNodes().OfType<StatementSyntax>());
        // Why property/accessor arrows: expression-bodied getters are rewritten into synthetic
        // MethodDeclarations that would otherwise carry no #line annotations into the shim.
        foreach (PropertyDeclarationSyntax propertyDeclaration in root.DescendantNodes()
            .OfType<PropertyDeclarationSyntax>())
        {
            if (propertyDeclaration.ExpressionBody != null)
            {
                nodesToAnnotate.Add(propertyDeclaration.ExpressionBody);
            }
        }

        foreach (AccessorDeclarationSyntax accessor in root.DescendantNodes()
            .OfType<AccessorDeclarationSyntax>())
        {
            if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration) && accessor.ExpressionBody != null)
            {
                nodesToAnnotate.Add(accessor.ExpressionBody);
            }
        }

        if (nodesToAnnotate.Count == 0)
        {
            return root;
        }

        // Why rewritten (not original): ReplaceNodes applies nested replacements first; basing the
        // parent annotation on original would drop statement annotations already applied inside.
        return root.ReplaceNodes(
            nodesToAnnotate,
            (original, rewritten) =>
            {
                int line = ResolveUloopLineAnnotationLine(original);
                return rewritten.WithAdditionalAnnotations(
                    new SyntaxAnnotation(
                        TransformWorkerProgram.UloopLineAnnotationKind,
                        line.ToString(CultureInfo.InvariantCulture)));
            });
    }

    internal static int ResolveUloopLineAnnotationLine(SyntaxNode node)
    {
        if (node is MethodDeclarationSyntax methodDeclaration && methodDeclaration.ExpressionBody != null)
        {
            // Why arrow expression (not declaration start): NormalizeWhitespace collapses the
            // method to one line, so mapping to the arrow expression's original start is the only
            // location that still matches the user's intent for expression-bodied methods.
            return methodDeclaration.ExpressionBody.Expression.GetLocation()
                .GetLineSpan().StartLinePosition.Line + 1;
        }

        if (node is ArrowExpressionClauseSyntax arrowExpressionClause)
        {
            return arrowExpressionClause.Expression.GetLocation()
                .GetLineSpan().StartLinePosition.Line + 1;
        }

        return node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }
}
