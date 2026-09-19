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

/// <summary>
/// Whether the member around one use of a candidate was skipped, applied, or neither by this reload.
/// </summary>
internal enum SkippedWriterUseSiteKind
{
    Other,
    Skipped,
    Applied
}

/// <summary>
/// The method or accessor around one use of a candidate, with the label the response shows for it.
/// </summary>
internal readonly struct SkippedWriterUseSite
{
    public static readonly SkippedWriterUseSite Other = new SkippedWriterUseSite(null, SkippedWriterUseSiteKind.Other);

    public SkippedWriterUseSite(string label, SkippedWriterUseSiteKind kind)
    {
        Label = label;
        Kind = kind;
    }

    public string Label { get; }

    public SkippedWriterUseSiteKind Kind { get; }
}

/// <summary>
/// Classifies the member around a use, so the warning counts added property accessors like
/// methods: a queued method or an emitted added accessor is applied, a labelled skipped row is
/// skipped, and anything else is a member whose effect this reload cannot tell.
/// </summary>
internal sealed class SkippedWriterUseSiteClassifier
{
    private readonly HashSet<string> _skippedLabels;
    private readonly HashSet<SyntaxNode> _queuedMethods;
    private readonly AddedPropertyCatalog _addedPropertyCatalog;

    public SkippedWriterUseSiteClassifier(
        HashSet<string> skippedLabels,
        HashSet<SyntaxNode> queuedMethods,
        AddedPropertyCatalog addedPropertyCatalog)
    {
        _skippedLabels = skippedLabels ?? throw new ArgumentNullException(nameof(skippedLabels));
        _queuedMethods = queuedMethods ?? throw new ArgumentNullException(nameof(queuedMethods));
        _addedPropertyCatalog = addedPropertyCatalog ?? throw new ArgumentNullException(nameof(addedPropertyCatalog));
    }

    // A constructor, field initializer, compiled property accessor or other member stays Other:
    // a write there is a writer this reload did not skip, so it keeps the warning quiet.
    public SkippedWriterUseSite Classify(SemanticModel semanticModel, SyntaxNode use)
    {
        MemberDeclarationSyntax member = use.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        switch (member)
        {
            case MethodDeclarationSyntax method:
                return ClassifyMethod(semanticModel, method);
            case PropertyDeclarationSyntax property:
                return ClassifyAddedPropertyAccessor(semanticModel, property, use);
            default:
                return SkippedWriterUseSite.Other;
        }
    }

    private SkippedWriterUseSite ClassifyMethod(SemanticModel semanticModel, MethodDeclarationSyntax method)
    {
        string label = WorkerMethodKeys.FormatMethodLabel(semanticModel.GetDeclaredSymbol(method));
        if (_skippedLabels.Contains(label))
        {
            return new SkippedWriterUseSite(label, SkippedWriterUseSiteKind.Skipped);
        }

        return _queuedMethods.Contains(method)
            ? new SkippedWriterUseSite(label, SkippedWriterUseSiteKind.Applied)
            : SkippedWriterUseSite.Other;
    }

    // Why only added properties: their accessors are emitted outside the method queue, and an
    // unavailable one is skipped whole, so both accessor labels share one applied-or-skipped fate.
    private SkippedWriterUseSite ClassifyAddedPropertyAccessor(
        SemanticModel semanticModel,
        PropertyDeclarationSyntax property,
        SyntaxNode use)
    {
        IPropertySymbol propertySymbol = semanticModel.GetDeclaredSymbol(property);
        AddedPropertyBinding binding = _addedPropertyCatalog.FindBySymbolOrNull(propertySymbol);
        IMethodSymbol accessorSymbol = FindAccessorSymbol(property, propertySymbol, use);
        if (binding == null || accessorSymbol == null)
        {
            return SkippedWriterUseSite.Other;
        }

        string label = WorkerMethodKeys.FormatMethodLabel(accessorSymbol);
        if (_skippedLabels.Contains(label))
        {
            return new SkippedWriterUseSite(label, SkippedWriterUseSiteKind.Skipped);
        }

        return binding.UnavailableReason == null
            ? new SkippedWriterUseSite(label, SkippedWriterUseSiteKind.Applied)
            : SkippedWriterUseSite.Other;
    }

    // Null for a use in the property initializer, which belongs to no accessor.
    private static IMethodSymbol FindAccessorSymbol(
        PropertyDeclarationSyntax property,
        IPropertySymbol propertySymbol,
        SyntaxNode use)
    {
        if (propertySymbol == null)
        {
            return null;
        }

        if (property.ExpressionBody != null && property.ExpressionBody.Span.Contains(use.Span))
        {
            return propertySymbol.GetMethod;
        }

        AccessorDeclarationSyntax accessor = use.Ancestors().OfType<AccessorDeclarationSyntax>().FirstOrDefault();
        if (accessor == null)
        {
            return null;
        }

        return accessor.IsKind(SyntaxKind.GetAccessorDeclaration)
            ? propertySymbol.GetMethod
            : propertySymbol.SetMethod;
    }
}
