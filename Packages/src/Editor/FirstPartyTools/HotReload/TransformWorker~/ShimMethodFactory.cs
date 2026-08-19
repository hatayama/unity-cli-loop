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

internal static class ShimMethodFactory
{
    public static MethodDeclarationSyntax ToShimMethod(
        MethodDeclarationSyntax rewrittenOriginal,
        IMethodSymbol methodSymbol)
    {
        TypeSyntax returnType = rewrittenOriginal.ReturnType.WithoutTrivia();
        SyntaxTokenList modifiers = SyntaxFactory.TokenList(
            SyntaxFactory.Token(SyntaxKind.PublicKeyword),
            SyntaxFactory.Token(SyntaxKind.StaticKeyword));

        // Async is preserved so the shim assembly still emits a state machine when the original
        // was async (transplant covers the stub; MoveNext stays in the shim assembly).
        if (rewrittenOriginal.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.AsyncKeyword)))
        {
            modifiers = modifiers.Add(SyntaxFactory.Token(SyntaxKind.AsyncKeyword));
        }

        SeparatedSyntaxList<ParameterSyntax> parameters = BuildShimParameters(rewrittenOriginal, methodSymbol);
        MethodDeclarationSyntax shim = rewrittenOriginal
            .WithAttributeLists(default)
            .WithModifiers(modifiers)
            .WithReturnType(returnType)
            .WithParameterList(SyntaxFactory.ParameterList(parameters))
            .WithExplicitInterfaceSpecifier(null)
            .WithConstraintClauses(default)
            .WithLeadingTrivia(StripDirectiveTrivia(rewrittenOriginal.GetLeadingTrivia()))
            .WithTrailingTrivia(StripDirectiveTrivia(rewrittenOriginal.GetTrailingTrivia()));

        // Expression-bodied methods must keep their terminating semicolon; block bodies must not.
        return rewrittenOriginal.ExpressionBody != null
            ? shim.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            : shim.WithSemicolonToken(default);
    }

    // Why strip directives: #if sits on the method's leading trivia while its matching #endif
    // belongs to the next token, so copied directives are unbalanced in the shim; #line mapping
    // is injected later from annotations and needs no user directives.
    private static SyntaxTriviaList StripDirectiveTrivia(SyntaxTriviaList trivia)
    {
        List<SyntaxTrivia> kept = new List<SyntaxTrivia>();
        foreach (SyntaxTrivia item in trivia)
        {
            if (!item.IsDirective)
            {
                kept.Add(item);
            }
        }

        return SyntaxFactory.TriviaList(kept);
    }

    private static SeparatedSyntaxList<ParameterSyntax> BuildShimParameters(
        MethodDeclarationSyntax rewrittenOriginal,
        IMethodSymbol methodSymbol)
    {
        List<ParameterSyntax> parameters = new List<ParameterSyntax>();
        if (!methodSymbol.IsStatic)
        {
            TypeSyntax instanceType = SyntaxFactory.ParseTypeName(
                methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            parameters.Add(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier(TransformWorkerProgramMarker.InstanceParameterName))
                    .WithType(instanceType));
        }

        foreach (ParameterSyntax originalParameter in rewrittenOriginal.ParameterList.Parameters)
        {
            parameters.Add(originalParameter.WithoutTrivia());
        }

        return SyntaxFactory.SeparatedList(parameters);
    }
}
