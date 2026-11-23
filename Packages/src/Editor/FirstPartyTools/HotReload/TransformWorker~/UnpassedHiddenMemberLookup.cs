using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// What: finds, for a use of a member of a compiled type the run was not given, a private or
/// protected member of the same name that the code there cannot reach and that the patch could
/// resolve the use to instead. The worker imports such a type through its public surface, so neither
/// of its bindings sees that member; the all-members compiled copy of the target assembly does, and
/// the patch, compiled against a copy where every member is public, would pick it.
/// </summary>
internal static class UnpassedHiddenMemberLookup
{
    /// <summary>
    /// What: the member of a compiled type the run was not given that the patch could resolve the use
    /// to in place of the member Unity's compiler resolves, or null when there is none or Unity's pick
    /// is not known.
    /// </summary>
    internal static ISymbol FindOrNull(
        SemanticModel semanticModel,
        SemanticModel enforcingModel,
        SyntaxNode node,
        IAssemblySymbol targetAssembly)
    {
        if (targetAssembly == null)
        {
            return null;
        }

        ISymbol pick = enforcingModel.GetSymbolInfo(node).Symbol;
        if (!IsLookedUpMember(pick))
        {
            return null;
        }

        ISymbol enclosing = semanticModel.GetEnclosingSymbol(node.SpanStart);
        INamedTypeSymbol enclosingType = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType;
        if (enclosingType == null)
        {
            return null;
        }

        UnpassedHiddenUse use = new UnpassedHiddenUse(
            semanticModel,
            enforcingModel,
            node,
            targetAssembly,
            pick,
            enclosingType,
            ReceiverTypeOrNull(semanticModel, node, enclosingType));
        foreach (INamedTypeSymbol compiledType in LookupTypes(use))
        {
            ISymbol hidden = FindHiddenMemberOrNull(compiledType, use);
            if (hidden != null)
            {
                return hidden;
            }
        }

        return null;
    }

    private static bool IsLookedUpMember(ISymbol pick)
    {
        if (pick == null || pick.ContainingType == null)
        {
            return false;
        }

        if (pick is IMethodSymbol method)
        {
            return method.MethodKind == MethodKind.Ordinary
                || method.MethodKind == MethodKind.Constructor
                || method.MethodKind == MethodKind.ReducedExtension;
        }

        return pick is IPropertySymbol || pick is IFieldSymbol || pick is IEventSymbol;
    }

    // Why this and base stand for the caller's own type: a protected member reached through either
    // is reachable, which the type of the expression alone does not say for base.
    private static ITypeSymbol ReceiverTypeOrNull(
        SemanticModel semanticModel,
        SyntaxNode node,
        INamedTypeSymbol enclosingType)
    {
        ExpressionSyntax receiver = HiddenMemberPickGuard.ReceiverExpressionOrNull(node);
        if (receiver == null)
        {
            return null;
        }

        if (receiver is ThisExpressionSyntax || receiver is BaseExpressionSyntax)
        {
            return enclosingType;
        }

        ITypeSymbol receiverType = semanticModel.GetTypeInfo(receiver).Type;
        return receiverType == null || receiverType.TypeKind == TypeKind.Error ? null : receiverType;
    }

    // Why only the created type for a construction and the declaring class for an extension method:
    // constructors are not inherited, and an extension method is looked up in its class alone.
    // Why the walk stops at the type that declares Unity's pick: a member of a base type further up
    // is hidden by the pick, or loses overload resolution to it, in the patch too.
    // Why the receiver's type first, else the caller's: member lookup starts there, and a private
    // member of a derived type hides the member of the base that Unity's compiler resolves.
    private static IEnumerable<INamedTypeSymbol> LookupTypes(UnpassedHiddenUse use)
    {
        INamedTypeSymbol declaring = use.Pick.ContainingType.OriginalDefinition;
        if (use.Pick is IMethodSymbol method
            && (method.MethodKind == MethodKind.Constructor || AccessibilityRules.UnreducedMethod(method).IsExtensionMethod))
        {
            INamedTypeSymbol copy = UnpassedCompiledCopyOrNull(declaring, use);
            if (copy != null)
            {
                yield return copy;
            }

            yield break;
        }

        INamedTypeSymbol start = use.ReceiverType as INamedTypeSymbol
            ?? (use.ReceiverType == null ? use.EnclosingType : declaring);
        for (INamedTypeSymbol type = start.OriginalDefinition; type != null; type = type.BaseType?.OriginalDefinition)
        {
            INamedTypeSymbol copy = UnpassedCompiledCopyOrNull(type, use);
            if (copy != null)
            {
                yield return copy;
            }

            if (UnpassedIndexerApplicability.HaveTheSameMetadataName(type, declaring))
            {
                yield break;
            }
        }
    }

    // The all-members copy of the type itself, or null when the run declares it or it is not a type
    // of the target assembly.
    private static INamedTypeSymbol UnpassedCompiledCopyOrNull(INamedTypeSymbol type, UnpassedHiddenUse use)
    {
        foreach (INamedTypeSymbol compiled in UnpassedInternalMemberUse.EnumerateUnpassedCompiledTypes(
                     type,
                     use.SemanticModel,
                     use.TargetAssembly))
        {
            return UnpassedIndexerApplicability.HaveTheSameMetadataName(compiled, type) ? compiled : null;
        }

        return null;
    }

    // Why by documentation comment id: the overloads share the name, and the copy's symbols never
    // equal the worker's, so the full signature is what tells Unity's pick apart from the others.
    private static ISymbol FindHiddenMemberOrNull(INamedTypeSymbol compiledType, UnpassedHiddenUse use)
    {
        string pickId = Definition(use.Pick).GetDocumentationCommentId();
        foreach (ISymbol candidate in compiledType.GetMembers(use.Pick.Name))
        {
            if (candidate.GetDocumentationCommentId() == pickId
                || UnpassedHiddenMemberReach.IsReachable(candidate, use.EnclosingType, use.ReceiverType)
                || !MayApply(candidate, use))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    // Why the reduced parameter list for a call written on a receiver: its argument list leaves out
    // the receiver, while the copy gives the extension method unreduced, with the this parameter.
    // Why a method that is not an extension cannot apply there: only extension methods are looked up
    // for a call written on a receiver of another type.
    // Why a member without parameters may apply: a field, property or event of the name hides the
    // member Unity's compiler resolves.
    private static bool MayApply(ISymbol candidate, UnpassedHiddenUse use)
    {
        IReadOnlyList<IParameterSymbol> parameters = ParametersOrNull(candidate);
        if (parameters == null)
        {
            return true;
        }

        int skipped = 0;
        if (use.Pick is IMethodSymbol pickMethod && pickMethod.MethodKind == MethodKind.ReducedExtension)
        {
            if (candidate is not IMethodSymbol candidateMethod || !candidateMethod.IsExtensionMethod)
            {
                return false;
            }

            skipped = 1;
        }

        List<CandidateParameter> mapped = new List<CandidateParameter>(parameters.Count);
        for (int index = skipped; index < parameters.Count; index++)
        {
            IParameterSymbol parameter = parameters[index];
            mapped.Add(new CandidateParameter(
                CompiledCopyTypeMapper.MapOrNull(parameter.Type, use.SemanticModel.Compilation, use.TargetAssembly),
                parameter.IsOptional,
                parameter.IsParams));
        }

        return HiddenCandidateApplicability.MayApplyToParameters(use.EnforcingModel, use.Node, mapped);
    }

    private static IReadOnlyList<IParameterSymbol> ParametersOrNull(ISymbol candidate)
    {
        if (candidate is IMethodSymbol method)
        {
            return method.Parameters;
        }

        if (candidate is IPropertySymbol property && property.IsIndexer)
        {
            return property.Parameters;
        }

        return null;
    }

    private static ISymbol Definition(ISymbol symbol)
    {
        return symbol is IMethodSymbol method
            ? AccessibilityRules.UnreducedMethod(method).OriginalDefinition
            : symbol.OriginalDefinition;
    }

    private readonly struct UnpassedHiddenUse
    {
        internal UnpassedHiddenUse(
            SemanticModel semanticModel,
            SemanticModel enforcingModel,
            SyntaxNode node,
            IAssemblySymbol targetAssembly,
            ISymbol pick,
            INamedTypeSymbol enclosingType,
            ITypeSymbol receiverType)
        {
            SemanticModel = semanticModel;
            EnforcingModel = enforcingModel;
            Node = node;
            TargetAssembly = targetAssembly;
            Pick = pick;
            EnclosingType = enclosingType;
            ReceiverType = receiverType;
        }

        internal SemanticModel SemanticModel { get; }

        internal SemanticModel EnforcingModel { get; }

        internal SyntaxNode Node { get; }

        internal IAssemblySymbol TargetAssembly { get; }

        internal ISymbol Pick { get; }

        internal INamedTypeSymbol EnclosingType { get; }

        internal ITypeSymbol ReceiverType { get; }
    }
}
