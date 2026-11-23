using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// What: the run's binding compilation as the patch's compile sees it: the same trees and
/// references, with every member of the referenced assemblies imported and accessibility ignored.
/// The patch is compiled against a copy of the assembly where every member is public, so a use
/// binds here to the member the patch would call, private and protected members of compiled types
/// included, which the worker's own binding imports only through their public surface.
/// </summary>
internal sealed class PatchBindingCompilation
{
    private readonly CSharpCompilation _compilation;
    private readonly Dictionary<SyntaxTree, SemanticModel> _models = new Dictionary<SyntaxTree, SemanticModel>();

    /// <summary>
    /// What: wraps <paramref name="bindingCompilation"/> with every metadata member imported. The
    /// compilation is created lazily by Roslyn, so a run that never asks pays only for this object.
    /// </summary>
    internal PatchBindingCompilation(CSharpCompilation bindingCompilation)
    {
        if (bindingCompilation == null)
        {
            throw new ArgumentNullException(nameof(bindingCompilation));
        }

        _compilation = bindingCompilation.WithOptions(
            bindingCompilation.Options.WithMetadataImportOptions(MetadataImportOptions.All));
    }

    /// <summary>
    /// What: the accessibility-ignoring model of <paramref name="tree"/>, which must be one of the
    /// binding compilation's trees.
    /// </summary>
    internal SemanticModel ModelFor(SyntaxTree tree)
    {
        if (_models.TryGetValue(tree, out SemanticModel model))
        {
            return model;
        }

        if (!_compilation.ContainsSyntaxTree(tree))
        {
            throw new ArgumentException("The tree is not one of the binding compilation's trees.", nameof(tree));
        }

        model = _compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        _models.Add(tree, model);
        return model;
    }
}
