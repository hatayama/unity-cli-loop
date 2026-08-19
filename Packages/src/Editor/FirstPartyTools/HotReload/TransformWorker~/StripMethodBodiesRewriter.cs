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

private sealed class StripMethodBodiesRewriter : CSharpSyntaxRewriter
{
    // Using directives never change patched behavior: WorkerUsingCollector.CollectUsingsForType copies the edited
    // file's usings into every shim, so comparing them here only produces false drift warnings
    // for using-only edits. extern alias declarations stay compared (not copied into shims).
    public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node)
    {
        return null;
    }

    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        MethodDeclarationSyntax visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
        if (visited.Body == null && visited.ExpressionBody == null)
        {
            return visited;
        }

        return visited
            .WithExpressionBody(null)
            .WithSemicolonToken(default(SyntaxToken))
            .WithBody(SyntaxFactory.Block());
    }

    // Why strip getters only: patched getter edits must not look like outside-body drift.
    // Setter/init/indexer bodies stay so those still-unapplied edits keep the warning.
    public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        PropertyDeclarationSyntax visited = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node);
        if (visited.ExpressionBody != null)
        {
            return visited
                .WithExpressionBody(null)
                .WithSemicolonToken(default(SyntaxToken))
                .WithAccessorList(
                    SyntaxFactory.AccessorList(
                        SyntaxFactory.SingletonList(
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                .WithBody(SyntaxFactory.Block()))));
        }

        if (visited.AccessorList == null)
        {
            return visited;
        }

        List<AccessorDeclarationSyntax> accessors = new List<AccessorDeclarationSyntax>();
        foreach (AccessorDeclarationSyntax accessor in visited.AccessorList.Accessors)
        {
            if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration)
                && (accessor.Body != null || accessor.ExpressionBody != null))
            {
                accessors.Add(
                    accessor
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default(SyntaxToken))
                        .WithBody(SyntaxFactory.Block()));
            }
            else
            {
                accessors.Add(accessor);
            }
        }

        return visited.WithAccessorList(
            SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));
    }

    // Why strip ctor/operator/event-accessor bodies: those members are reported as
    // per-member Skipped, so a body-only edit must not also look like outside-body drift.
    // Signature, attributes, and constructor initializers stay so those edits still warn.
    public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        ConstructorDeclarationSyntax visited =
            (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node);
        if (visited.Body == null && visited.ExpressionBody == null)
        {
            return visited;
        }

        return visited
            .WithExpressionBody(null)
            .WithSemicolonToken(default(SyntaxToken))
            .WithBody(SyntaxFactory.Block());
    }

    public override SyntaxNode VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        OperatorDeclarationSyntax visited = (OperatorDeclarationSyntax)base.VisitOperatorDeclaration(node);
        if (visited.Body == null && visited.ExpressionBody == null)
        {
            return visited;
        }

        return visited
            .WithExpressionBody(null)
            .WithSemicolonToken(default(SyntaxToken))
            .WithBody(SyntaxFactory.Block());
    }

    public override SyntaxNode VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        ConversionOperatorDeclarationSyntax visited =
            (ConversionOperatorDeclarationSyntax)base.VisitConversionOperatorDeclaration(node);
        if (visited.Body == null && visited.ExpressionBody == null)
        {
            return visited;
        }

        return visited
            .WithExpressionBody(null)
            .WithSemicolonToken(default(SyntaxToken))
            .WithBody(SyntaxFactory.Block());
    }

    public override SyntaxNode VisitEventDeclaration(EventDeclarationSyntax node)
    {
        EventDeclarationSyntax visited = (EventDeclarationSyntax)base.VisitEventDeclaration(node);
        if (visited.AccessorList == null)
        {
            return visited;
        }

        List<AccessorDeclarationSyntax> accessors = new List<AccessorDeclarationSyntax>();
        foreach (AccessorDeclarationSyntax accessor in visited.AccessorList.Accessors)
        {
            if (accessor.Body == null && accessor.ExpressionBody == null)
            {
                accessors.Add(accessor);
                continue;
            }

            accessors.Add(
                accessor
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default(SyntaxToken))
                    .WithBody(SyntaxFactory.Block()));
        }

        return visited.WithAccessorList(
            SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));
    }

    // Why strip const initializers: const drift has its own warning with both values;
    // leaving EqualsValueClause here would also trip the generic outside-body warning.
    public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        FieldDeclarationSyntax visited = (FieldDeclarationSyntax)base.VisitFieldDeclaration(node);
        bool isConst = false;
        foreach (SyntaxToken modifier in visited.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.ConstKeyword))
            {
                isConst = true;
                break;
            }
        }

        if (!isConst)
        {
            return visited;
        }

        List<VariableDeclaratorSyntax> declarators = new List<VariableDeclaratorSyntax>();
        foreach (VariableDeclaratorSyntax declarator in visited.Declaration.Variables)
        {
            declarators.Add(declarator.WithInitializer(null));
        }

        return visited.WithDeclaration(
            visited.Declaration.WithVariables(SyntaxFactory.SeparatedList(declarators)));
    }

    // Why strip enum member values: enum constants use the same dedicated const-drift path.
    public override SyntaxNode VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node)
    {
        EnumMemberDeclarationSyntax visited =
            (EnumMemberDeclarationSyntax)base.VisitEnumMemberDeclaration(node);
        if (visited.EqualsValue == null)
        {
            return visited;
        }

        return visited.WithEqualsValue(null);
    }
}
