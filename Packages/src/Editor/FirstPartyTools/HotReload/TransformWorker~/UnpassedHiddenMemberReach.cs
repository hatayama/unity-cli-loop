using Microsoft.CodeAnalysis;

/// <summary>
/// What: whether code of the run could reach a member of the all-members compiled copy of a type the
/// run was not given, as Unity's compiler decides it. The copy lives in another compilation than the
/// code, so the compilation's own accessibility query cannot be asked; the types are compared by
/// metadata name instead.
/// </summary>
internal static class UnpassedHiddenMemberReach
{
    /// <summary>
    /// What: true for a member every type of the target assembly may reach (the run's code compiles
    /// into that assembly in reality), false for a private one, and for a protected or private
    /// protected one true only from a type derived from the declaring type, through a receiver of the
    /// caller's type or a type derived from it, or for a static member.
    /// </summary>
    internal static bool IsReachable(ISymbol member, INamedTypeSymbol enclosingType, ITypeSymbol receiverType)
    {
        switch (member.DeclaredAccessibility)
        {
            case Accessibility.Private:
                return false;
            case Accessibility.Protected:
            case Accessibility.ProtectedAndInternal:
                return IsReachableAsProtected(member, enclosingType, receiverType);
            default:
                return true;
        }
    }

    // Why a constructor is never reached this way: a derived type may call a protected constructor of
    // its base only from a constructor initializer, and a body reaches it through 'new' alone.
    private static bool IsReachableAsProtected(ISymbol member, INamedTypeSymbol enclosingType, ITypeSymbol receiverType)
    {
        if (member is IMethodSymbol { MethodKind: MethodKind.Constructor }
            || !DerivesFromOrIs(enclosingType, member.ContainingType))
        {
            return false;
        }

        return member.IsStatic || receiverType == null || DerivesFromOrIs(receiverType, enclosingType);
    }

    private static bool DerivesFromOrIs(ITypeSymbol type, INamedTypeSymbol baseType)
    {
        for (INamedTypeSymbol current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (UnpassedIndexerApplicability.HaveTheSameMetadataName(current, baseType))
            {
                return true;
            }
        }

        return false;
    }
}
