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

internal sealed class ShimTypeBuilder
{
    private readonly List<MethodDeclarationSyntax> _methods = new List<MethodDeclarationSyntax>();

    public ShimTypeBuilder(
        string shimTypeName,
        string namespaceName,
        List<UsingDirectiveSyntax> usings)
    {
        ShimTypeName = shimTypeName;
        NamespaceName = namespaceName ?? string.Empty;
        Usings = usings ?? new List<UsingDirectiveSyntax>();
        AccessorPlan = new AccessorPlan();
    }

    public string ShimTypeName { get; }

    public string NamespaceName { get; }

    public List<UsingDirectiveSyntax> Usings { get; }

    /// <summary>
    /// Shim-type-level accessor registry — shared across all delegation methods in this type so
    /// AllocateName stays unique and overloads cannot collide after a per-method merge.
    /// </summary>
    public AccessorPlan AccessorPlan { get; }

    public void AddMethod(MethodDeclarationSyntax shimMethod, string shimMethodName)
    {
        MethodDeclarationSyntax named = shimMethod.WithIdentifier(SyntaxFactory.Identifier(shimMethodName));
        _methods.Add(named);
    }

    public IEnumerable<MemberDeclarationSyntax> EmitMembers()
    {
        foreach (AccessorEntry accessor in AccessorPlan.Entries)
        {
            yield return accessor.EmitFieldDeclaration();
        }

        if (AccessorPlan.Entries.Count > 0)
        {
            yield return EmitBindAccessorsMethod();
        }

        foreach (MethodDeclarationSyntax method in _methods)
        {
            yield return method;
        }
    }

    private MethodDeclarationSyntax EmitBindAccessorsMethod()
    {
        List<StatementSyntax> statements = new List<StatementSyntax>();
        foreach (AccessorEntry accessor in AccessorPlan.Entries)
        {
            statements.Add(accessor.EmitBindStatement());
        }

        return SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                "__BindAccessors")
            .WithModifiers(
                SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithBody(SyntaxFactory.Block(statements));
    }
}
