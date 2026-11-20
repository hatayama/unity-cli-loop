using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Finds a use, inside a method body, of an internal accessor of a public or protected property or
/// indexer declared by a compiled type of the target assembly that the run does not declare. The
/// member binds in the worker, but the accessor the use calls is not there: a write then reads as
/// one to a read-only member, and a read as one of a write-only member.
/// </summary>
/// <remarks>
/// Why the worker sees no accessor rather than an inaccessible one: its binding compilation imports a
/// referenced assembly through the public surface only, which leaves an internal accessor out of an
/// otherwise public member. The shim compile references a copy with every accessor public, so a body
/// with such a use compiles there and is reported patched, and then throws MethodAccessException
/// wherever it runs as ordinary code of the shim assembly.
/// </remarks>
internal static class UnpassedInternalAccessorLookup
{
    // Why only these and why not the missing-name IDs: a write through a missing setter or init
    // accessor reports CS0200 and a read through a missing getter reports CS0154. Each is also a
    // genuine error when no internal accessor explains it, and the shim compile reports that one as
    // before, so these IDs reach this lookup only and never the missing-name handling.
    private static readonly HashSet<string> MissingAccessorDiagnosticIds =
        new HashSet<string>(StringComparer.Ordinal) { "CS0200", "CS0154" };

    /// <summary>Whether <paramref name="error"/> is one a missing accessor of a bound member reports.</summary>
    internal static bool IsMissingAccessorDiagnostic(Diagnostic error)
    {
        return MissingAccessorDiagnosticIds.Contains(error.Id);
    }

    /// <summary>
    /// The use <paramref name="error"/> reports, or null when the error does not come from a read or
    /// write that calls an internal accessor of a public or protected property or indexer of a
    /// compiled type of the target assembly the run does not declare.
    /// </summary>
    internal static UnpassedInternalMemberUse FindOrNull(
        Diagnostic error,
        SemanticModel semanticModel,
        SyntaxNode bodyNode,
        MethodDeclarationSyntax methodDeclarationOrNull,
        MethodTransformDecision decision,
        IAssemblySymbol targetAssembly)
    {
        Debug.Assert(error != null, "error must not be null.");
        if (targetAssembly == null || !IsMissingAccessorDiagnostic(error))
        {
            return null;
        }

        ExpressionSyntax reported = FindReportedAccessOrNull(error, bodyNode, out bool isBareName);
        if (reported == null)
        {
            return null;
        }

        foreach (ExpressionSyntax access in CandidateAccesses(reported))
        {
            IMethodSymbol accessor = FindInternalAccessorOfAccessOrNull(access, semanticModel, targetAssembly);
            if (accessor != null)
            {
                return UnpassedInternalMemberUse.ForAccessorCall(
                    accessor,
                    access,
                    isBareName,
                    bodyNode,
                    methodDeclarationOrNull,
                    decision,
                    semanticModel);
            }
        }

        return null;
    }

    private static IMethodSymbol FindInternalAccessorOfAccessOrNull(
        ExpressionSyntax access,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly)
    {
        if (BoundPropertyOrNull(semanticModel, access) is not IPropertySymbol bound)
        {
            return null;
        }

        IPropertySymbol compiled = FindCompiledPropertyOrNull(bound, semanticModel, targetAssembly);
        return compiled == null ? null : FindInternalAccessorCalledOrNull(compiled, access);
    }

    // Why the whole chain of an extended property pattern: the compiler reports '{ A.B: 1 }' on all
    // of 'A.B', whichever of A and B lacks its getter, and the pattern reads each of them.
    private static IEnumerable<ExpressionSyntax> CandidateAccesses(ExpressionSyntax reported)
    {
        yield return reported;
        if (!IsHeadOfAnExtendedPropertyPattern(reported))
        {
            yield break;
        }

        for (ExpressionSyntax part = reported; part is MemberAccessExpressionSyntax access; part = access.Expression)
        {
            yield return access.Expression;
        }
    }

    private static bool IsHeadOfAnExtendedPropertyPattern(ExpressionSyntax expression)
    {
        SyntaxNode current = expression;
        while (current.Parent is MemberAccessExpressionSyntax access && access.Expression == current)
        {
            current = access;
        }

        return current.Parent is ExpressionColonSyntax;
    }

    /// <summary>
    /// "System.Index" or "System.Range" when the construct is an element access with a single
    /// argument of that type, the only access for which the compiler calls Length, Count or an
    /// indexer it does not name; otherwise null.
    /// </summary>
    internal static string IndexOrRangeArgumentTypeOrNull(SemanticModel semanticModel, SyntaxNode construct)
    {
        BracketedArgumentListSyntax arguments = construct switch
        {
            ElementAccessExpressionSyntax elementAccess => elementAccess.ArgumentList,
            ElementBindingExpressionSyntax elementBinding => elementBinding.ArgumentList,
            _ => null
        };
        if (arguments == null || arguments.Arguments.Count != 1)
        {
            return null;
        }

        string argumentType = semanticModel.GetTypeInfo(arguments.Arguments[0].Expression).Type?.ToDisplayString();
        return argumentType == IndexTypeName || argumentType == RangeTypeName ? argumentType : null;
    }

    private const string IndexTypeName = "System.Index";
    private const string RangeTypeName = "System.Range";

    // Why these indexers only: an Index argument is read through 'this[int]' with Length or Count,
    // or through 'this[Index]'; a Range argument only through 'this[Range]', since a range slices
    // with Slice instead. Any other indexer is not what the access calls, so an error against it
    // stays a genuine error.
    private static bool IsIndexerTheAccessCalls(IPropertySymbol property, string indexOrRangeArgumentType)
    {
        if (!property.IsIndexer)
        {
            return true;
        }

        if (indexOrRangeArgumentType == null || property.Parameters.Length != 1)
        {
            return false;
        }

        ITypeSymbol parameterType = property.Parameters[0].Type;
        return parameterType.SpecialType == SpecialType.System_Int32 && indexOrRangeArgumentType == IndexTypeName
            || parameterType.ToDisplayString() == indexOrRangeArgumentType;
    }

    // The members whose getter a construct calls without naming it: an awaiter's IsCompleted, and
    // the Length, Count and indexer of an Index or Range access. Why not any name: a property named
    // like a method the construct calls, such as GetAwaiter, is not what the compiler calls, and the
    // error it raises stays the shim compile's to report. Why the indexer only for an Index or Range
    // argument: any other element access names the indexer itself, which the accessor route judges
    // by its parameter types, so a wrong argument or an undefined name inside it stays a genuine
    // error rather than a use of some other indexer's internal getter.
    private static readonly HashSet<string> ImplicitlyReadPropertyNames =
        new HashSet<string>(StringComparer.Ordinal) { "IsCompleted", "Length", "Count", WellKnownMemberNames.Indexer };

    /// <summary>
    /// The internal getter of a public or protected property or indexer named
    /// <paramref name="memberName"/> of <paramref name="start"/> or a base type, as the target
    /// assembly declares it, or null, also for a name the compiler never reads implicitly, and for an
    /// indexer that an access with an argument of <paramref name="indexOrRangeArgumentTypeOrNull"/>
    /// does not call. An await reads an awaiter's IsCompleted through it, and an Index or Range
    /// access Length, Count or the indexer.
    /// </summary>
    internal static IMethodSymbol FindInternalGetterOrNull(
        INamedTypeSymbol start,
        string memberName,
        string indexOrRangeArgumentTypeOrNull,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly)
    {
        if (!ImplicitlyReadPropertyNames.Contains(memberName))
        {
            return null;
        }

        foreach (INamedTypeSymbol type in UnpassedInternalMemberUse.EnumerateUnpassedCompiledTypes(start, semanticModel, targetAssembly))
        {
            foreach (IPropertySymbol property in type.GetMembers(memberName).OfType<IPropertySymbol>())
            {
                if (IsIndexerTheAccessCalls(property, indexOrRangeArgumentTypeOrNull)
                    && IsInternalAccessorOfAVisibleMember(property.GetMethod))
                {
                    return property.GetMethod;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// "The getter of 'Name'", "The setter of 'Name'" or "The init accessor of 'Name'" when
    /// <paramref name="method"/> is an internal accessor of a public or protected property or
    /// indexer, or null for any other method. Name is the member's name, 'this[]' for an indexer.
    /// </summary>
    internal static string DescribeInternalAccessorOrNull(IMethodSymbol method)
    {
        if (!IsInternalAccessorOfAVisibleMember(method))
        {
            return null;
        }

        string accessor = method.MethodKind == MethodKind.PropertyGet
            ? "The getter of '"
            : method.IsInitOnly ? "The init accessor of '" : "The setter of '";
        return accessor + method.AssociatedSymbol.Name + "'";
    }

    // Why internal and private protected only: the worker's import shows a protected accessor, and
    // a private one is an accessor the real compiler rejects outside its type too. Why the member
    // must be visible: an internal member is missing as a whole, which the missing-name handling
    // reports with the member's own name.
    private static bool IsInternalAccessorOfAVisibleMember(IMethodSymbol accessor)
    {
        if (accessor?.AssociatedSymbol is not IPropertySymbol property)
        {
            return false;
        }

        return IsHiddenFromThePublicSurface(accessor.DeclaredAccessibility)
            && !IsHiddenFromThePublicSurface(property.DeclaredAccessibility)
            && property.DeclaredAccessibility != Accessibility.Private;
    }

    private static bool IsHiddenFromThePublicSurface(Accessibility accessibility)
    {
        return accessibility == Accessibility.Internal || accessibility == Accessibility.ProtectedAndInternal;
    }

    // The read or write the error is reported on: a member access with its receiver ('value.Name',
    // 'Type.Name'), an element access ('value[i]'), either form after '?.', an index initializer
    // ('[i] = v'), a member named in an object or with initializer or in a property pattern, or a
    // bare name, which <paramref name="isBareName"/> reports: the shim qualifies only a name the
    // worker binds as written, so a bare name stays out of reach even in the method's own statements.
    private static ExpressionSyntax FindReportedAccessOrNull(Diagnostic error, SyntaxNode bodyNode, out bool isBareName)
    {
        isBareName = false;
        Location location = error.Location;
        if (!location.IsInSource
            || location.SourceTree != bodyNode.SyntaxTree
            || !bodyNode.Span.Contains(location.SourceSpan))
        {
            return null;
        }

        SyntaxNode reported = AccessNamedBy(bodyNode.FindNode(location.SourceSpan, getInnermostNodeForTie: true));
        switch (reported)
        {
            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess;
            case ElementAccessExpressionSyntax elementAccess:
                return elementAccess;
            case MemberBindingExpressionSyntax memberBinding:
                return memberBinding;
            case ElementBindingExpressionSyntax elementBinding:
                return elementBinding;
            case ImplicitElementAccessSyntax indexInitializer:
                return indexInitializer;
            case IdentifierNameSyntax identifier:
                isBareName = !IsNamedWithoutAReceiverByItsConstruct(identifier);
                return identifier;
            default:
                return null;
        }
    }

    // Why these unwrap: a property pattern member is reported on its 'Name:', an index initializer on
    // its '[i]', and a member access or a member binding on its name, each inside the access it names.
    private static SyntaxNode AccessNamedBy(SyntaxNode reported)
    {
        if (reported is NameColonSyntax nameColon)
        {
            return nameColon.Name;
        }

        if (reported is BracketedArgumentListSyntax { Parent: ImplicitElementAccessSyntax indexInitializer })
        {
            return indexInitializer;
        }

        bool isNameOfAnAccess = reported is SimpleNameSyntax name
            && (reported.Parent is MemberAccessExpressionSyntax { Name: var accessName } && accessName == name
                || reported.Parent is MemberBindingExpressionSyntax);
        return isNameOfAnAccess ? reported.Parent : reported;
    }

    // A member an object or with initializer sets, or a property pattern reads, is looked up on the
    // value being created or matched, not by a bare name in scope.
    private static bool IsNamedWithoutAReceiverByItsConstruct(IdentifierNameSyntax name)
    {
        if (name.Parent is AssignmentExpressionSyntax assignment
            && assignment.Left == name
            && assignment.Parent is InitializerExpressionSyntax initializer)
        {
            return initializer.IsKind(SyntaxKind.ObjectInitializerExpression)
                || initializer.IsKind(SyntaxKind.WithInitializerExpression);
        }

        if (HarmonyAccessorShimRewrite.IsSubpatternMemberName(name))
        {
            return true;
        }

        // Why the head of an extended property pattern: in '{ A.B: 1 }' the pattern reads A on the
        // matched value, the same as '{ A: { B: 1 } }' does.
        return IsHeadOfAnExtendedPropertyPattern(name);
    }

    // Why a single candidate counts: a member access the binder rejects for its missing accessor
    // may come back as a candidate rather than the symbol, and it is still the member the use names.
    private static ISymbol BoundPropertyOrNull(SemanticModel semanticModel, ExpressionSyntax access)
    {
        SymbolInfo info = semanticModel.GetSymbolInfo(access);
        return info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null);
    }

    // Why names and parameter types and not symbols: the target assembly comes from a separate
    // compilation that imports every member, so its properties are other symbols than the one this
    // body binds to. An indexer overload is told apart by its parameter types. Why the bound
    // member's original definition: the compiled types are generic definitions, so an access through
    // 'Holder<int>' must be compared as 'Holder<T>.this[T]', not 'this[int]'. Why only the declaring
    // type: the bound property's containing type declares it, so a base type's property of the same
    // name is one it hides, and when the run declares that type the error is genuine.
    private static IPropertySymbol FindCompiledPropertyOrNull(
        IPropertySymbol bound,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly)
    {
        INamedTypeSymbol declaring = UnpassedInternalMemberUse
            .EnumerateUnpassedCompiledTypes(bound.ContainingType, semanticModel, targetAssembly)
            .FirstOrDefault();
        if (declaring == null
            || declaring.ToDisplayString() != bound.ContainingType.OriginalDefinition.ToDisplayString())
        {
            return null;
        }

        return declaring.GetMembers(bound.Name)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(property => HasTheSameParameterTypes(property, bound.OriginalDefinition));
    }

    private static bool HasTheSameParameterTypes(IPropertySymbol compiled, IPropertySymbol bound)
    {
        return compiled.Parameters.Length == bound.Parameters.Length
            && compiled.Parameters.Zip(bound.Parameters, (left, right) => left.Type.ToDisplayString() == right.Type.ToDisplayString())
                .All(same => same);
    }

    // Why the getter is checked first: a compound assignment or an increment calls both accessors,
    // and the reason then names the getter. Why decide by the use: matching any internal accessor
    // would refuse a read of a member whose setter alone is internal, which the shim runs.
    private static IMethodSymbol FindInternalAccessorCalledOrNull(IPropertySymbol compiled, ExpressionSyntax access)
    {
        (bool reads, bool writes) = AccessorsCalledBy(access);
        if (reads && IsInternalAccessorOfAVisibleMember(compiled.GetMethod))
        {
            return compiled.GetMethod;
        }

        IMethodSymbol store = AssignmentTargetRules.AccessorCalledByWrite(compiled);
        return writes && IsInternalAccessorOfAVisibleMember(store) ? store : null;
    }

    /// <summary>
    /// Whether <paramref name="access"/> calls the getter, the setter, or both, of the property or
    /// indexer it names. A simple assignment, an initializer and a deconstruction only store; a
    /// nested initializer ('P = { ... }') only reads, filling what P returns; a compound assignment,
    /// an increment and a decrement read and store; every other use, a property pattern included,
    /// reads.
    /// </summary>
    internal static (bool Reads, bool Writes) AccessorsCalledBy(ExpressionSyntax access)
    {
        AssignmentExpressionSyntax assignment = AssignmentTargetRules.AssignmentTargetedBy(access);
        if (assignment != null && AssignmentTargetRules.IsNestedInitializer(assignment))
        {
            return (true, false);
        }

        if (assignment != null)
        {
            return (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression), true);
        }

        if (IsIncrementedOrDecremented(access))
        {
            return (true, true);
        }

        return AssignmentTargetRules.IsDeconstructionTarget(access) ? (false, true) : (true, false);
    }

    private static bool IsIncrementedOrDecremented(ExpressionSyntax access)
    {
        SyntaxNode operand = access;
        while (operand.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            operand = parenthesized;
        }

        return operand.Parent is PrefixUnaryExpressionSyntax prefix
                && (prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression))
            || operand.Parent is PostfixUnaryExpressionSyntax postfix
                && (postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression));
    }
}
