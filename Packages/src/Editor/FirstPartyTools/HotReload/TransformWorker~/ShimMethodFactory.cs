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

    /// <summary>
    /// Makes the shim of an instance member hot reload added throw NullReferenceException on a
    /// null receiver before its body runs, the way a call to a compiled member does.
    /// </summary>
    /// <remarks>
    /// Why in the shim and not at the call site: compiled code evaluates the arguments before the
    /// null receiver throws, and a check at the call site would throw before them. Why the object
    /// cast: a UnityEngine.Object receiver would otherwise use Unity's == and also refuse a
    /// destroyed object, which a compiled call still reaches. An async or iterator shim throws
    /// when its body starts rather than at the call, which is still closer than running it.
    /// </remarks>
    public static MethodDeclarationSyntax GuardAddedMemberReceiver(
        MethodDeclarationSyntax shim,
        IMethodSymbol methodSymbol)
    {
        if (methodSymbol.IsStatic || methodSymbol.ContainingType.IsValueType)
        {
            return shim;
        }

        StatementSyntax guard = SyntaxFactory.ParseStatement(
            "if ((object)" + TransformWorkerProgramMarker.InstanceParameterName
            + " == null) throw new global::System.NullReferenceException();");
        if (shim.Body != null)
        {
            return shim.WithBody(shim.Body.WithStatements(shim.Body.Statements.Insert(0, guard)));
        }

        ArrowExpressionClauseSyntax arrow = shim.ExpressionBody;
        StatementSyntax bodyStatement = ToBodyStatement(arrow.Expression, methodSymbol);
        // Why the annotations move to the statement: the #line mapping is injected from them. An
        // accessor arrow carries its own, which does not survive the change to a block; an
        // expression-bodied method carries the expression's line on the declaration instead,
        // where it would now map the '{' and guard lines rather than the expression.
        SyntaxNode lineSource = arrow.HasAnnotations(TransformWorkerProgram.UloopLineAnnotationKind)
            ? (SyntaxNode)arrow
            : shim;
        bodyStatement = (StatementSyntax)PropertyGetterEmitter.TransferUloopLineAnnotations(lineSource, bodyStatement);
        return shim
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .WithBody(SyntaxFactory.Block(guard, bodyStatement));
    }

    // Why a throw expression is special: `=> throw ...` is legal only as an expression body, and
    // neither `return throw ...;` nor a throw expression statement compiles.
    private static StatementSyntax ToBodyStatement(ExpressionSyntax expression, IMethodSymbol methodSymbol)
    {
        if (expression is ThrowExpressionSyntax throwExpression)
        {
            return SyntaxFactory.ThrowStatement(throwExpression.Expression);
        }

        return ReturnsValue(methodSymbol)
            ? SyntaxFactory.ReturnStatement(expression)
            : SyntaxFactory.ExpressionStatement(expression);
    }

    // An async method returning a non-generic awaitable returns nothing from its body either.
    private static bool ReturnsValue(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.ReturnsVoid)
        {
            return false;
        }

        if (!methodSymbol.IsAsync)
        {
            return true;
        }

        return methodSymbol.ReturnType is INamedTypeSymbol namedReturnType && namedReturnType.IsGenericType;
    }

    // Why strip directives: #if sits on the method's leading trivia while its matching #endif
    // belongs to the next token, so copied directives are unbalanced in the shim; #line mapping
    // is injected later from annotations and needs no user directives. Disabled text from an
    // inactive #if region would otherwise lose its guarding directives and become live code
    // inside the static shim class, causing CS0708 for instance fields.
    private static SyntaxTriviaList StripDirectiveTrivia(SyntaxTriviaList trivia)
    {
        List<SyntaxTrivia> kept = new List<SyntaxTrivia>();
        foreach (SyntaxTrivia item in trivia)
        {
            if (item.IsDirective || item.IsKind(SyntaxKind.DisabledTextTrivia))
            {
                continue;
            }

            kept.Add(item);
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
