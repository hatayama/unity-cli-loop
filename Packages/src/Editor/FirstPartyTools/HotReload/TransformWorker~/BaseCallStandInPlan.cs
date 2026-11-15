using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// The base-call stand-ins one shim type declares: one static method per distinct base method its
/// shims call, named so the Editor can find that base method and redirect the call to it.
/// </summary>
internal sealed class BaseCallStandInPlan
{
    private const string UnredirectedMessage = "A hot reload base-call stand-in ran without being redirected.";

    private readonly List<(string Key, MethodDeclarationSyntax Declaration, string Name)> _standIns =
        new List<(string Key, MethodDeclarationSyntax Declaration, string Name)>();

    /// <summary>The stand-in name for this base call, registering its declaration on first use.</summary>
    internal string GetOrAdd(int depth, IMethodSymbol target, TypeSyntax hostTypeSyntax)
    {
        string key = depth + "|" + target.Name + "|"
            + string.Join(",", target.Parameters.Select(CecilTypeNames.ToParameterTypeFullName));
        foreach ((string Key, MethodDeclarationSyntax Declaration, string Name) standIn in _standIns)
        {
            if (standIn.Key == key)
            {
                return standIn.Name;
            }
        }

        string name = HotReloadBaseCallStandInNames.Format(depth, target.Name, _standIns.Count);
        _standIns.Add((key, BuildDeclaration(name, target, hostTypeSyntax), name));
        return name;
    }

    /// <summary>The registered stand-in declarations, in registration order.</summary>
    internal IEnumerable<MethodDeclarationSyntax> EmitMethods()
    {
        return _standIns.Select(standIn => standIn.Declaration);
    }

    // Why a throwing block body: the Editor redirects every call before the IL runs, so reaching
    // the body means the redirect was skipped, and a block that only throws is legal for any
    // non-ref return type.
    private static MethodDeclarationSyntax BuildDeclaration(string name, IMethodSymbol target, TypeSyntax hostTypeSyntax)
    {
        List<ParameterSyntax> parameters = new List<ParameterSyntax>
        {
            SyntaxFactory.Parameter(SyntaxFactory.Identifier(TransformWorkerProgramMarker.InstanceParameterName))
                .WithType(hostTypeSyntax)
        };
        // Why positional names: the stand-in never reads its parameters, and a target parameter
        // named with a C# keyword (@event) would otherwise emit an invalid identifier.
        for (int index = 0; index < target.Parameters.Length; index++)
        {
            IParameterSymbol parameter = target.Parameters[index];
            parameters.Add(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("p" + index.ToString(CultureInfo.InvariantCulture)))
                    .WithModifiers(BuildRefModifiers(parameter.RefKind))
                    .WithType(ToTypeSyntax(parameter.Type)));
        }

        TypeSyntax returnType = target.ReturnsVoid
            ? SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword))
            : ToTypeSyntax(target.ReturnType);
        ThrowStatementSyntax throwStatement = SyntaxFactory.ThrowStatement(
            SyntaxFactory.ParseExpression(
                "new global::System.InvalidOperationException(\"" + UnredirectedMessage + "\")"));
        return SyntaxFactory.MethodDeclaration(returnType, name)
            .WithModifiers(
                SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters)))
            .WithBody(SyntaxFactory.Block(throwStatement));
    }

    private static SyntaxTokenList BuildRefModifiers(RefKind refKind)
    {
        switch (refKind)
        {
            case RefKind.Ref:
                return SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.RefKeyword));
            case RefKind.Out:
                return SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.OutKeyword));
            case RefKind.In:
                return SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.InKeyword));
            default:
                return default;
        }
    }

    private static TypeSyntax ToTypeSyntax(ITypeSymbol type)
    {
        return SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }
}
