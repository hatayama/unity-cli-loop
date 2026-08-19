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

private sealed class StripHandledMemberDeclarationsRewriter : CSharpSyntaxRewriter
{
    private readonly HashSet<string> _syntaxKeysToStrip;
    private readonly HashSet<string> _typeSyntaxKeysToStrip;
    private readonly HashSet<string> _propertySyntaxKeysToStrip;

    public StripHandledMemberDeclarationsRewriter(
        IReadOnlyCollection<string> syntaxKeysToStrip,
        IReadOnlyCollection<string> typeSyntaxKeysToStrip,
        IReadOnlyCollection<string> propertySyntaxKeysToStrip)
    {
        _syntaxKeysToStrip = new HashSet<string>(syntaxKeysToStrip, StringComparer.Ordinal);
        _typeSyntaxKeysToStrip = new HashSet<string>(
            typeSyntaxKeysToStrip ?? Array.Empty<string>(),
            StringComparer.Ordinal);
        _propertySyntaxKeysToStrip = new HashSet<string>(
            propertySyntaxKeysToStrip ?? Array.Empty<string>(),
            StringComparer.Ordinal);
    }

    public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        if (ShouldStripType(node))
        {
            return null;
        }

        return base.VisitClassDeclaration(node);
    }

    public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node)
    {
        if (ShouldStripType(node))
        {
            return null;
        }

        return base.VisitStructDeclaration(node);
    }

    public override SyntaxNode VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        if (ShouldStripType(node))
        {
            return null;
        }

        return base.VisitRecordDeclaration(node);
    }

    public override SyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        if (ShouldStripType(node))
        {
            return null;
        }

        return base.VisitInterfaceDeclaration(node);
    }

    private bool ShouldStripType(TypeDeclarationSyntax node)
    {
        return _typeSyntaxKeysToStrip.Contains(WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(node));
    }

    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        TypeDeclarationSyntax typeDeclaration = node.Parent as TypeDeclarationSyntax;
        if (typeDeclaration == null)
        {
            return base.VisitMethodDeclaration(node);
        }

        string syntaxKey = WorkerSyntaxIndex.BuildSyntaxMethodKey(
            WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration),
            node);
        if (_syntaxKeysToStrip.Contains(syntaxKey))
        {
            return null;
        }

        return base.VisitMethodDeclaration(node);
    }

    public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        TypeDeclarationSyntax typeDeclaration = node.Parent as TypeDeclarationSyntax;
        if (typeDeclaration == null)
        {
            return base.VisitPropertyDeclaration(node);
        }

        string syntaxKey = WorkerSyntaxIndex.BuildSyntaxPropertyKey(
            WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration),
            node);
        if (_propertySyntaxKeysToStrip.Contains(syntaxKey))
        {
            return null;
        }

        return base.VisitPropertyDeclaration(node);
    }

    public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        TypeDeclarationSyntax typeDeclaration = node.Parent as TypeDeclarationSyntax;
        if (typeDeclaration == null)
        {
            return base.VisitFieldDeclaration(node);
        }

        string typeMetadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);
        List<VariableDeclaratorSyntax> remaining = new List<VariableDeclaratorSyntax>();
        foreach (VariableDeclaratorSyntax variable in node.Declaration.Variables)
        {
            string syntaxKey = WorkerSyntaxIndex.BuildSyntaxFieldKey(typeMetadataName, variable.Identifier.Text);
            if (!_syntaxKeysToStrip.Contains(syntaxKey))
            {
                remaining.Add(variable);
            }
        }

        if (remaining.Count == 0)
        {
            return null;
        }

        if (remaining.Count == node.Declaration.Variables.Count)
        {
            return base.VisitFieldDeclaration(node);
        }

        return node.WithDeclaration(
            node.Declaration.WithVariables(SyntaxFactory.SeparatedList(remaining)));
    }
}
