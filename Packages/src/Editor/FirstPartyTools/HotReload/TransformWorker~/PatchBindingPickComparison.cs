using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// What: compares the member a use binds to in the patch's compile with the one Unity's compiler
/// binds it to, and returns the patch's member when the code there cannot reach it. Binding the use
/// in the patch's view lets the compiler do the lookup (using static directives, extension classes
/// in scope, instance members before extensions, outer types), overload resolution and conversions.
/// </summary>
internal static class PatchBindingPickComparison
{
    /// <summary>
    /// What: the member the patch could use at <paramref name="node"/> in place of Unity's pick,
    /// which the code there cannot reach, or null when the two agree or the patch's member is
    /// reachable. Unity's pick is unknown when the worker's binding resolves nothing, as for an
    /// internal member of a compiled type, which it does not import; any member the patch picks
    /// then counts as different.
    /// </summary>
    internal static ISymbol FindHiddenPickOrNull(
        SemanticModel enforcingModel,
        SemanticModel patchModel,
        SyntaxNode node,
        IAssemblySymbol targetAssembly)
    {
        ISymbol unityPick = enforcingModel.GetSymbolInfo(node).Symbol;
        if (unityPick != null && !IsMember(unityPick))
        {
            return null;
        }

        SymbolInfo patchInfo = patchModel.GetSymbolInfo(node);
        if (unityPick == null && !IsMember(patchInfo.Symbol) && !HasMemberCandidate(patchInfo))
        {
            return null;
        }

        ISymbol enclosing = patchModel.GetEnclosingSymbol(node.SpanStart);
        INamedTypeSymbol enclosingType = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType;
        if (enclosingType == null)
        {
            return null;
        }

        PatchUse use = new PatchUse(patchModel, node, targetAssembly, unityPick, enclosingType, ReceiverTypeOrNull(patchModel, node));
        ISymbol patchPick = patchInfo.Symbol;
        if (patchPick == null)
        {
            return FirstHiddenCandidateOrNull(patchInfo.CandidateSymbols, use, null);
        }

        if (!IsOfThePatchedAssembly(patchPick, use))
        {
            return IsReachable(patchPick, use)
                ? RunDeclaredParameterFitOrNull(patchPick, use)
                : FirstHiddenCandidateOrNull(use.PatchModel.GetMemberGroup(node), use, patchPick);
        }

        if (!IsSameMember(patchPick, unityPick) && !IsReachable(patchPick, use))
        {
            return patchPick;
        }

        return RunDeclaredParameterFitOrNull(patchPick, use);
    }

    private static bool HasMemberCandidate(SymbolInfo info)
    {
        foreach (ISymbol candidate in info.CandidateSymbols)
        {
            if (IsMember(candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMember(ISymbol symbol)
    {
        return symbol != null
            && symbol.ContainingType != null
            && (symbol is IMethodSymbol || symbol is IPropertySymbol || symbol is IFieldSymbol || symbol is IEventSymbol);
    }

    // Why only a candidate other than Unity's pick: with every member imported, a member of a type
    // the run declares also comes from the compiled copy, and the two copies of one extension
    // method leave the binding ambiguous without any other member taking part.
    // Why only one that may apply: a candidate the arguments cannot fit is never what the patch
    // calls, whatever made the binding fail.
    // Why the member group when this binding picks a member of another assembly the code cannot
    // reach: the patch's compile keeps that member hidden and resolves among the rest, so its pick
    // is unknown here, and any member of the patched assemblies that may apply could be it.
    private static ISymbol FirstHiddenCandidateOrNull(ImmutableArray<ISymbol> candidates, PatchUse use, ISymbol patchPickOrNull)
    {
        foreach (ISymbol candidate in candidates)
        {
            if (IsMember(candidate)
                && IsOfThePatchedAssembly(candidate, use)
                && !IsSameMember(candidate, use.UnityPick)
                && !IsReachable(candidate, use)
                && MayApply(candidate, use, patchPickOrNull))
            {
                return candidate;
            }
        }

        return null;
    }

    // Why the worker's binding misses this one: a type the run declares is compiled from source
    // here, and a parameter of a compiled member names its compiled copy, so no conversion is found
    // where the patch, which knows one type, finds one. The other members of the use's member group
    // are therefore checked with the parameter types mapped to the run's declarations.
    private static ISymbol RunDeclaredParameterFitOrNull(ISymbol patchPick, PatchUse use)
    {
        foreach (ISymbol member in use.PatchModel.GetMemberGroup(use.Node))
        {
            if (!IsOfThePatchedAssembly(member, use)
                || IsSameMember(member, patchPick)
                || IsSameMember(member, use.UnityPick)
                || IsReachable(member, use)
                || !CompiledCopyTypeMapper.HasParameterTypeTheRunDeclares(member, use.PatchModel.Compilation, use.TargetAssembly))
            {
                continue;
            }

            if (MayApply(member, use, patchPick))
            {
                return member;
            }
        }

        return null;
    }

    // Why the this parameter is dropped for an unreduced extension method in a call written on a
    // receiver: the argument list leaves the receiver out.
    // Why a member without parameters may apply: a field, property or event of the name hides the
    // member Unity's compiler resolves.
    private static bool MayApply(ISymbol member, PatchUse use, ISymbol patchPick)
    {
        ImmutableArray<IParameterSymbol> parameters = member switch
        {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol { IsIndexer: true } indexer => indexer.Parameters,
            _ => default
        };
        if (parameters.IsDefault)
        {
            return true;
        }

        int skipped = patchPick is IMethodSymbol { MethodKind: MethodKind.ReducedExtension }
            && member is IMethodSymbol { MethodKind: MethodKind.Ordinary, IsExtensionMethod: true }
            ? 1
            : 0;
        List<CandidateParameter> mapped = new List<CandidateParameter>(parameters.Length);
        for (int index = skipped; index < parameters.Length; index++)
        {
            IParameterSymbol parameter = parameters[index];
            mapped.Add(new CandidateParameter(
                CompiledCopyTypeMapper.MapOrNull(parameter.Type, use.PatchModel.Compilation, use.TargetAssembly),
                parameter.IsOptional,
                parameter.IsParams));
        }

        return HiddenCandidateApplicability.MayApplyToParameters(use.PatchModel, use.Node, mapped);
    }

    // Why only the run's source and the target assembly: the patch's compile makes every member
    // public in the target assembly alone, while this compilation imports every member of every
    // reference, so a private member of another assembly here is one the patch never sees.
    private static bool IsOfThePatchedAssembly(ISymbol member, PatchUse use)
    {
        IAssemblySymbol assembly = member.ContainingAssembly;
        return assembly != null
            && (SymbolEqualityComparer.Default.Equals(assembly, use.PatchModel.Compilation.Assembly)
                || IsTargetAssembly(assembly, use));
    }

    private static bool IsTargetAssembly(IAssemblySymbol assembly, PatchUse use)
    {
        return use.TargetAssembly != null && assembly.Identity.Equals(use.TargetAssembly.Identity);
    }

    // Why a member of the target assembly that every type of it may reach counts as reachable
    // although the compiler here says otherwise: the run's code compiles into that assembly in
    // reality, while this compilation is another assembly.
    private static bool IsReachable(ISymbol member, PatchUse use)
    {
        Compilation compilation = use.PatchModel.Compilation;
        if (member.ContainingAssembly != null
            && IsTargetAssembly(member.ContainingAssembly, use)
            && HiddenMemberPickGuard.IsReachableFromTheWholeAssembly(member))
        {
            return true;
        }

        ISymbol unreduced = member is IMethodSymbol method ? AccessibilityRules.UnreducedMethod(method) : member;
        return compilation.IsSymbolAccessibleWithin(unreduced, use.EnclosingType, use.ReceiverType);
    }

    // Why no receiver type for this and base: a protected member is reachable through either, and
    // base has the base type, through which the accessibility query would refuse it.
    private static ITypeSymbol ReceiverTypeOrNull(SemanticModel patchModel, SyntaxNode node)
    {
        ExpressionSyntax receiver = HiddenMemberPickGuard.ReceiverExpressionOrNull(node);
        if (receiver is ThisExpressionSyntax || receiver is BaseExpressionSyntax)
        {
            return null;
        }

        return HiddenMemberPickGuard.FindReceiverTypeOrNull(patchModel, node);
    }

    // Why by documentation comment id of the unreduced original definition: the two bindings come
    // from different compilations, whose symbols never equal, and may report one extension method
    // reduced and unreduced, or one generic member constructed apart.
    // Why an unknown pick is never the same: a member the patch picks cannot be shown to be the one
    // Unity's compiler calls.
    private static bool IsSameMember(ISymbol first, ISymbol second)
    {
        if (first == null || second == null)
        {
            return false;
        }

        return Definition(first).GetDocumentationCommentId() == Definition(second).GetDocumentationCommentId();
    }

    private static ISymbol Definition(ISymbol symbol)
    {
        return symbol is IMethodSymbol method
            ? AccessibilityRules.UnreducedMethod(method).OriginalDefinition
            : symbol.OriginalDefinition;
    }

    private readonly struct PatchUse
    {
        internal PatchUse(
            SemanticModel patchModel,
            SyntaxNode node,
            IAssemblySymbol targetAssembly,
            ISymbol unityPick,
            INamedTypeSymbol enclosingType,
            ITypeSymbol receiverType)
        {
            PatchModel = patchModel;
            Node = node;
            TargetAssembly = targetAssembly;
            UnityPick = unityPick;
            EnclosingType = enclosingType;
            ReceiverType = receiverType;
        }

        internal SemanticModel PatchModel { get; }

        internal SyntaxNode Node { get; }

        internal IAssemblySymbol TargetAssembly { get; }

        internal ISymbol UnityPick { get; }

        internal INamedTypeSymbol EnclosingType { get; }

        internal ITypeSymbol ReceiverType { get; }
    }
}
