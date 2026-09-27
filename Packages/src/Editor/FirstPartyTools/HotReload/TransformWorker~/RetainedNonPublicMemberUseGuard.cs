using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Finds where a member added to one type uses a non-public member of a retained introduced type
// that this run keeps in its binding tree. Such a use binds only on the reloads that keep that
// declaration: every other reload binds the retained artifact through the public surface the
// default metadata import exposes, where the member is absent, so an addition that used it would
// be applied on one reload and dropped on the next unrelated one.
internal static class RetainedNonPublicMemberUseGuard
{
    /// <summary>
    /// The first member the body uses that is non-public on a retained type this run keeps, other
    /// than the type the added member belongs to, or null when the body uses none. A property
    /// counts through the accessor the use calls.
    /// </summary>
    internal static ISymbol FindUse(
        SemanticModel semanticModel,
        SyntaxNode bodyNode,
        INamedTypeSymbol hostType,
        WorkerSourceUnit sourceUnit)
    {
        if (sourceUnit.RunRetainedBodyEditTypes.Count == 0)
        {
            return null;
        }

        // Why nameof arguments are not skipped, unlike InaccessibleAccessScanner: that scanner asks
        // whether the emitted code needs an accessor, while this asks whether the name binds, and a
        // reload that binds the artifact cannot find the member inside nameof either.
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            ISymbol nonPublicMember = FindNonPublicMemberUse(semanticModel.GetSymbolInfo(node).Symbol, node);
            if (nonPublicMember != null
                && IsMemberOfAnotherRetainedType(nonPublicMember, hostType, semanticModel, sourceUnit))
            {
                return nonPublicMember;
            }
        }

        return null;
    }

    // The member the node uses when it is non-public: the accessor the use calls for a property,
    // else the member itself.
    private static ISymbol FindNonPublicMemberUse(ISymbol symbol, SyntaxNode node)
    {
        if (symbol is IPropertySymbol property)
        {
            return FindNonPublicPropertyUse(property, node);
        }

        if (symbol is not IFieldSymbol && symbol is not IMethodSymbol && symbol is not IEventSymbol)
        {
            return null;
        }

        return AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol) ? symbol : null;
    }

    // Why the type the added member belongs to is excluded: its added members exist only while its
    // source differs from its artifact, and every such reload keeps its declaration, so they bind
    // the same way on each reload that has them.
    private static bool IsMemberOfAnotherRetainedType(
        ISymbol member,
        INamedTypeSymbol hostType,
        SemanticModel semanticModel,
        WorkerSourceUnit sourceUnit)
    {
        INamedTypeSymbol containingType = member.ContainingType;
        if (containingType == null
            || SymbolEqualityComparer.Default.Equals(containingType.OriginalDefinition, hostType.OriginalDefinition))
        {
            return false;
        }

        return RetainedBodyEditHome.FindRunRetainedType(sourceUnit, semanticModel, containingType.OriginalDefinition) != null;
    }

    // Why a property is judged by the accessor the use calls: the artifact import keeps a property
    // with only its public accessors, so a read of `{ get; internal set; }` binds on every reload
    // while a write binds only where the declaration is kept. nameof calls no accessor and needs
    // the property alone, which the import keeps while any accessor is public.
    private static ISymbol FindNonPublicPropertyUse(IPropertySymbol property, SyntaxNode node)
    {
        if (NameofRules.IsInsideNameofArgument(node))
        {
            return AccessibilityRules.IsInaccessibleFromExternalAssembly(property) ? property : null;
        }

        SyntaxNode site = AccessedExpression(node);
        // A ref-returning property has only a getter, and a write goes through the reference it returns.
        bool returnsByRef = property.ReturnsByRef || property.ReturnsByRefReadonly;
        if ((returnsByRef || CallsGetter(site)) && IsNonPublicAccessor(property.GetMethod))
        {
            return property.GetMethod;
        }

        if (!returnsByRef && CallsSetter(site) && IsNonPublicAccessor(property.SetMethod))
        {
            return property.SetMethod;
        }

        return null;
    }

    // The member access or binding a property name is the name of, so the name and the access
    // around it resolve to the same site, outside any parentheses, since `(a.P) = 1` writes P.
    private static SyntaxNode AccessedExpression(SyntaxNode node)
    {
        SyntaxNode site = node;
        if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node)
        {
            site = memberAccess;
        }
        else if (node.Parent is MemberBindingExpressionSyntax memberBinding && memberBinding.Name == node)
        {
            site = memberBinding;
        }

        while (site.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            site = parenthesized;
        }

        return site;
    }

    private static bool CallsSetter(SyntaxNode site)
    {
        AssignmentExpressionSyntax assignment = AssignmentTargetedBy(site);
        if (assignment != null)
        {
            return !IsNestedInitializer(assignment);
        }

        return IsIncrementOperand(site) || IsDeconstructionTarget(site);
    }

    private static bool CallsGetter(SyntaxNode site)
    {
        AssignmentExpressionSyntax assignment = AssignmentTargetedBy(site);
        if (assignment != null)
        {
            return !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || IsNestedInitializer(assignment);
        }

        return !IsDeconstructionTarget(site);
    }

    // `P = { ... }` inside an object initializer reads P and fills what it returns, never setting it.
    private static bool IsNestedInitializer(AssignmentExpressionSyntax assignment)
    {
        return assignment.Right is InitializerExpressionSyntax;
    }

    private static AssignmentExpressionSyntax AssignmentTargetedBy(SyntaxNode site)
    {
        return site.Parent is AssignmentExpressionSyntax assignment && assignment.Left == site
            ? assignment
            : null;
    }

    private static bool IsIncrementOperand(SyntaxNode site)
    {
        return (site.Parent is PrefixUnaryExpressionSyntax prefix
                && (prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression)))
            || (site.Parent is PostfixUnaryExpressionSyntax postfix
                && (postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression)));
    }

    // An element of a tuple, at any depth, that a simple assignment deconstructs into.
    private static bool IsDeconstructionTarget(SyntaxNode site)
    {
        SyntaxNode target = site;
        while (target.Parent is ArgumentSyntax argument && argument.Parent is TupleExpressionSyntax tuple)
        {
            target = tuple;
        }

        return target != site && AssignmentTargetedBy(target) != null;
    }

    // Why a missing accessor counts as public, unlike AccessibilityRules.IsInaccessibleAccessor:
    // the body already bound, so a use never calls an accessor the property lacks.
    private static bool IsNonPublicAccessor(IMethodSymbol accessor)
    {
        return accessor != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(accessor);
    }
}
