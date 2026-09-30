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
    private readonly List<string> _invocationCounterFieldNames = new List<string>();

    public ShimTypeBuilder(
        string shimTypeName,
        string namespaceName,
        List<UsingDirectiveSyntax> usings,
        string sourceProjectRelativePath)
    {
        ShimTypeName = shimTypeName;
        NamespaceName = namespaceName ?? string.Empty;
        Usings = usings ?? new List<UsingDirectiveSyntax>();
        SourceProjectRelativePath = sourceProjectRelativePath;
        AccessorPlan = new AccessorPlan();
    }

    public string ShimTypeName { get; }

    // The edited file whose methods this shim type hosts. Emit names it in the #line directives
    // so a shim compile error maps back to the file the body was written in.
    public string SourceProjectRelativePath { get; }

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

    /// <summary>
    /// Adds the shim of a member hot reload added together with the static field that counts how
    /// often its body starts, so no added-member shim can be emitted without its counter.
    /// </summary>
    public void AddAddedMemberMethod(
        MethodDeclarationSyntax shimMethod,
        string shimMethodName,
        IMethodSymbol methodSymbol)
    {
        string counterFieldName = shimMethodName + TransformWorkerProgramMarker.AddedMemberInvocationCounterSuffix;
        AddMethod(
            ShimMethodFactory.PrependAddedMemberPreamble(shimMethod, methodSymbol, counterFieldName),
            shimMethodName);
        _invocationCounterFieldNames.Add(counterFieldName);
    }

    public IEnumerable<MemberDeclarationSyntax> EmitMembers()
    {
        foreach (AccessorEntry accessor in AccessorPlan.Entries)
        {
            yield return accessor.EmitFieldDeclaration();
        }

        foreach (string counterFieldName in _invocationCounterFieldNames)
        {
            yield return EmitInvocationCounterField(counterFieldName);
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

    private static FieldDeclarationSyntax EmitInvocationCounterField(string counterFieldName)
    {
        return SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.LongKeyword)))
                    .WithVariables(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(counterFieldName))))
            .WithModifiers(
                SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)));
    }
}
