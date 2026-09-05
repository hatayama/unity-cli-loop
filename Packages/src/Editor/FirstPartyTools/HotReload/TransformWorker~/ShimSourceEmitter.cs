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

internal static class ShimSourceEmitter
{
    public static string Emit(List<ShimTypeBuilder> shimTypes)
    {
        if (shimTypes.Count == 0)
        {
            return string.Empty;
        }

        // Each source's projectRelativePath shape is validated at the input boundary (ParseErrors).

        // Emit each shim type in the original type's namespace (and with that type's usings) so
        // unqualified sibling-type references in transplanted bodies still resolve. Manifest
        // shimTypeName stays the short name; orchestrator resolves by Type.Name. A type from the
        // global namespace gets a synthesized namespace for the same reason (see below).
        CompilationUnitSyntax unit = SyntaxFactory.CompilationUnit();
        Dictionary<string, string> projectRelativePathsByShimTypeName = new Dictionary<string, string>();
        foreach (ShimTypeBuilder shimType in shimTypes)
        {
            projectRelativePathsByShimTypeName[shimType.ShimTypeName] = shimType.SourceProjectRelativePath;
            ClassDeclarationSyntax classDeclaration = SyntaxFactory.ClassDeclaration(shimType.ShimTypeName)
                .WithModifiers(
                    SyntaxFactory.TokenList(
                        SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                        SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithMembers(SyntaxFactory.List(shimType.EmitMembers()));

            // Why every shim type gets a namespace declaration: a using belongs to the file it was
            // written in, and the shim types of a group come from several files. Compilation-unit
            // usings are one flat list shared by all of them, so two files whose usings conflict
            // (the same alias bound differently, or two namespaces exporting the same type name)
            // would break the whole shim assembly. A namespace declaration scopes each file's
            // usings to its own shim type. Unqualified references to global-namespace types still
            // resolve, because name lookup from inside a namespace walks outward to global.
            string namespaceName = ShimNamespaceNames.ResolveShimNamespaceName(shimType.NamespaceName);
            NamespaceDeclarationSyntax namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(
                    SyntaxFactory.ParseName(namespaceName))
                .WithUsings(SyntaxFactory.List(shimType.Usings))
                .WithMembers(
                    SyntaxFactory.SingletonList<MemberDeclarationSyntax>(classDeclaration));
            unit = unit.AddMembers(namespaceDeclaration);
        }

        // Why after NormalizeWhitespace: formatting would otherwise shift #line relative to
        // statements; annotations survive formatting so we inject directives on the final tree.
        unit = unit.NormalizeWhitespace();
        unit = InjectLineDirectives(unit, projectRelativePathsByShimTypeName);

        StringBuilder builder = new StringBuilder();
        builder.AppendLine(
            "// Generated shims mirror user method signatures verbatim; repo style rules apply to hand-written code only.");
        builder.Append(unit.ToFullString());
        return builder.ToString();
    }

    private static CompilationUnitSyntax InjectLineDirectives(
        CompilationUnitSyntax unit,
        Dictionary<string, string> projectRelativePathsByShimTypeName)
    {
        List<SyntaxNode> annotatedNodes = unit.GetAnnotatedNodes(TransformWorkerProgram.UloopLineAnnotationKind)
            .ToList();
        // Why resolve the document before replacing: ReplaceNodes hands back rewritten nodes that
        // are detached from the unit, so the enclosing shim class is only reachable up front.
        Dictionary<SyntaxNode, string> projectRelativePathsByNode = new Dictionary<SyntaxNode, string>();
        foreach (SyntaxNode annotatedNode in annotatedNodes)
        {
            projectRelativePathsByNode[annotatedNode] = ResolveProjectRelativePath(
                annotatedNode,
                projectRelativePathsByShimTypeName);
        }

        if (annotatedNodes.Count > 0)
        {
            unit = unit.ReplaceNodes(
                annotatedNodes,
                (original, rewritten) =>
                {
                    SyntaxAnnotation annotation = original
                        .GetAnnotations(TransformWorkerProgram.UloopLineAnnotationKind)
                        .First();
                    // Why leading trivia starts/ends with newline: #line must occupy its own line.
                    // Why after comments/regions: those trivia consume mapped lines if they sit
                    // under the directive, so a later statement inherits the earlier line number.
                    string directiveText =
                        "\n#line " + annotation.Data + " \"" + projectRelativePathsByNode[original] + "\"\n";
                    return LineDirectiveTriviaInjector.Attach(rewritten, directiveText);
                });
        }

        // Reset mapping after each method so scaffold (__BindAccessors, fields, class braces)
        // does not inherit the previous method's document/line.
        List<MethodDeclarationSyntax> methods = unit.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .ToList();
        if (methods.Count == 0)
        {
            return unit;
        }

        // Why ParseLeadingTrivia into trailing: ParseTrailingTrivia does not reliably produce
        // LineDirectiveTrivia for "#line default", while ParseLeadingTrivia does — and directive
        // trivia is legal in a trailing trivia list for ToFullString emission.
        return unit.ReplaceNodes(
            methods,
            (original, rewritten) =>
            {
                SyntaxTriviaList defaultDirective = SyntaxFactory.ParseLeadingTrivia("\n#line default\n");
                return rewritten.WithTrailingTrivia(
                    rewritten.GetTrailingTrivia().AddRange(defaultDirective));
            });
    }

    // The edited file a shim body came from, taken from the shim class that hosts it.
    private static string ResolveProjectRelativePath(
        SyntaxNode annotatedNode,
        Dictionary<string, string> projectRelativePathsByShimTypeName)
    {
        ClassDeclarationSyntax shimClass = annotatedNode.Ancestors()
            .OfType<ClassDeclarationSyntax>()
            .LastOrDefault();
        Debug.Assert(shimClass != null, "An annotated shim node must live inside a shim class.");
        string shimTypeName = shimClass.Identifier.ValueText;
        Debug.Assert(
            projectRelativePathsByShimTypeName.ContainsKey(shimTypeName),
            "Every emitted shim class must come from a known shim type.");
        return projectRelativePathsByShimTypeName[shimTypeName];
    }
}
