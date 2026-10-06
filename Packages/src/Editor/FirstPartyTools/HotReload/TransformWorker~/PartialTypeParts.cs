using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// What the run knows about the parts of its partial types that live in files it was not
/// given: the trees that complete the binding, and which types cannot be completed.
/// </summary>
internal sealed class PartialTypeParts
{
    // No trees and nothing untrusted: a run that declares no partial type.
    internal static readonly PartialTypeParts None = new PartialTypeParts(
        Array.Empty<SyntaxTree>(),
        new Dictionary<string, string>(StringComparer.Ordinal),
        everyPartialTypeUnverified: false);

    // Nothing was checked, so no partial type may be bound against its other parts. Also what
    // a source unit holds until a pipeline loads the parts, so a pipeline that never does
    // keeps skipping partial types instead of binding them incomplete.
    internal static readonly PartialTypeParts Unverified = new PartialTypeParts(
        Array.Empty<SyntaxTree>(),
        new Dictionary<string, string>(StringComparer.Ordinal),
        everyPartialTypeUnverified: true);

    private readonly HashSet<SyntaxTree> _bindingOnlyTreeSet;
    private readonly Dictionary<string, string> _changedPartPathByTypeMetadataName;
    private readonly bool _everyPartialTypeUnverified;

    internal PartialTypeParts(
        IReadOnlyList<SyntaxTree> bindingOnlyTrees,
        Dictionary<string, string> changedPartPathByTypeMetadataName,
        bool everyPartialTypeUnverified)
    {
        BindingOnlyTrees = bindingOnlyTrees ?? throw new ArgumentNullException(nameof(bindingOnlyTrees));
        _changedPartPathByTypeMetadataName = changedPartPathByTypeMetadataName
            ?? throw new ArgumentNullException(nameof(changedPartPathByTypeMetadataName));
        _everyPartialTypeUnverified = everyPartialTypeUnverified;
        _bindingOnlyTreeSet = new HashSet<SyntaxTree>(bindingOnlyTrees);
    }

    // The reduced trees of the other parts, which enter the binding compilation but are not files
    // of the run: nothing is transformed or reported from them.
    internal IReadOnlyList<SyntaxTree> BindingOnlyTrees { get; }

    internal bool IsBindingOnlyTree(SyntaxTree tree)
    {
        return _bindingOnlyTreeSet.Contains(tree);
    }

    // The reason typeDeclaration (or a partial type enclosing it) cannot be bound against the
    // compiled type, or null when every other part is trusted.
    internal WorkerReason FindUntrustedOtherPartReasonOrNull(TypeDeclarationSyntax typeDeclaration)
    {
        for (TypeDeclarationSyntax declaration = typeDeclaration;
             declaration != null;
             declaration = declaration.Parent as TypeDeclarationSyntax)
        {
            if (!IsPartial(declaration))
            {
                continue;
            }

            if (_everyPartialTypeUnverified)
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformPartialOtherPartsUnverified);
            }

            string metadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(declaration);
            if (_changedPartPathByTypeMetadataName.TryGetValue(metadataName, out string changedPartPath))
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformPartialOtherPartChanged, changedPartPath);
            }
        }

        return null;
    }

    internal static bool IsPartial(TypeDeclarationSyntax declaration)
    {
        return declaration.Modifiers.Any(SyntaxKind.PartialKeyword);
    }

    // True when declaration or a type enclosing it is partial, which is when another file can
    // declare members the body binds to.
    internal static bool IsPartialOrNestedInPartial(TypeDeclarationSyntax declaration)
    {
        for (TypeDeclarationSyntax current = declaration; current != null; current = current.Parent as TypeDeclarationSyntax)
        {
            if (IsPartial(current))
            {
                return true;
            }
        }

        return false;
    }
}
