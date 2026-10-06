using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// A use, inside a method body, of an internal member declared by a compiled type of the target
/// assembly that the run does not declare: the worker's binding reports the member as missing,
/// while the shim compile binds it and the patched method can call it.
/// </summary>
/// <remarks>
/// Why the worker reports such a member as missing rather than inaccessible: its binding
/// compilation imports a referenced assembly through the public surface only, so an internal
/// member of a compiled type is not there at all. The shim compile references a copy of the
/// target assembly with every member made public.
/// </remarks>
internal sealed class UnpassedInternalMemberUse
{
    private static readonly HashSet<string> MemberNotFoundDiagnosticIds =
        new HashSet<string>(StringComparer.Ordinal) { "CS0103", "CS1061", "CS0117" };

    private UnpassedInternalMemberUse(INamedTypeSymbol declaringType, bool canBePatchedInPlace)
    {
        DeclaringType = declaringType;
        CanBePatchedInPlace = canBePatchedInPlace;
    }

    /// <summary>The compiled type that declares the member, read with every member visible.</summary>
    internal INamedTypeSymbol DeclaringType { get; }

    /// <summary>True when the patched method itself runs the use, so a guard may let the body through.</summary>
    internal bool CanBePatchedInPlace { get; }

    /// <summary>
    /// The use <paramref name="error"/> reports, or null when the error is not a missing member that
    /// turns out to be an internal member of a compiled type of the target assembly the run does
    /// not declare.
    /// </summary>
    internal static UnpassedInternalMemberUse FindOrNull(
        Diagnostic error,
        SemanticModel semanticModel,
        SyntaxNode bodyNode,
        MethodDeclarationSyntax methodDeclarationOrNull,
        MethodTransformDecision decision,
        INamedTypeSymbol enclosingType,
        IAssemblySymbol targetAssembly)
    {
        Debug.Assert(error != null, "error must not be null.");
        Debug.Assert(semanticModel != null, "semanticModel must not be null.");
        Debug.Assert(bodyNode != null, "bodyNode must not be null.");
        Debug.Assert(decision != null, "decision must not be null.");
        Debug.Assert(enclosingType != null, "enclosingType must not be null.");

        if (targetAssembly == null || !MemberNotFoundDiagnosticIds.Contains(error.Id))
        {
            return null;
        }

        SimpleNameSyntax name = FindReportedNameOrNull(error, bodyNode);
        if (name == null)
        {
            return null;
        }

        bool hasReceiver = HasReceiver(name);
        INamedTypeSymbol start = hasReceiver ? FindReceiverTypeOrNull(name, semanticModel) : enclosingType;
        if (start == null)
        {
            return null;
        }

        ISymbol member = FindInternalMemberOfUnpassedTypeOrNull(
            start,
            name.Identifier.ValueText,
            semanticModel,
            targetAssembly);
        if (member == null)
        {
            return null;
        }

        // Why a bare name is out of reach: the shim is a static method, and it qualifies a bare
        // member name only when the worker binds the name. This member never binds there, so the
        // name would reach the shim compile unqualified and fail the whole file.
        bool canBePatchedInPlace = hasReceiver
            && IsPatchableKind(member, name)
            && !RunsOutsideThePatchedMethod(name, bodyNode, methodDeclarationOrNull, decision);
        return new UnpassedInternalMemberUse(member.ContainingType, canBePatchedInPlace);
    }

    private static SimpleNameSyntax FindReportedNameOrNull(Diagnostic error, SyntaxNode bodyNode)
    {
        Location location = error.Location;
        if (!location.IsInSource
            || location.SourceTree != bodyNode.SyntaxTree
            || !bodyNode.Span.Contains(location.SourceSpan))
        {
            return null;
        }

        // Why a member binding is unwrapped: for 'value?.Name' the compiler reports the whole
        // '.Name' binding, while for 'value.Name' and a bare name it reports the name itself.
        SyntaxNode reported = bodyNode.FindNode(location.SourceSpan, getInnermostNodeForTie: true);
        if (reported is MemberBindingExpressionSyntax binding)
        {
            return binding.Name;
        }

        return reported as SimpleNameSyntax;
    }

    // 'value.Name', 'Type.Name' and 'this.Name', or 'value?.Name'.
    private static bool HasReceiver(SimpleNameSyntax name)
    {
        if (name.Parent is MemberAccessExpressionSyntax access)
        {
            return access.Name == name;
        }

        return name.Parent is MemberBindingExpressionSyntax binding && binding.Name == name;
    }

    private static INamedTypeSymbol FindReceiverTypeOrNull(SimpleNameSyntax name, SemanticModel semanticModel)
    {
        ExpressionSyntax receiver = name.Parent is MemberAccessExpressionSyntax access
            ? access.Expression
            : name.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>()?.Expression;
        if (receiver == null)
        {
            return null;
        }

        // Why not a type parameter, pointer, dynamic or Nullable<T> receiver: the member is not
        // looked up on a compiled type of the target assembly through any of them.
        INamedTypeSymbol type = semanticModel.GetTypeInfo(receiver).Type as INamedTypeSymbol;
        if (type == null || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return null;
        }

        return type;
    }

    private static ISymbol FindInternalMemberOfUnpassedTypeOrNull(
        INamedTypeSymbol start,
        string memberName,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly)
    {
        INamedTypeSymbol compiled = FindInTargetAssemblyOrNull(start.OriginalDefinition, semanticModel, targetAssembly);
        for (INamedTypeSymbol type = compiled; type != null; type = type.BaseType?.OriginalDefinition)
        {
            // Why only types of the target assembly: the shim compile makes every member public in
            // the target assembly and in some project assemblies, and the worker cannot tell which
            // others. The full import shows the internal members of another assembly's base type too.
            if (!IsOfAssembly(type, targetAssembly))
            {
                continue;
            }

            // Why skip a type the run declares: its members come from the source parts the run
            // reads, so a member missing there is a part the worker cannot see, which is what the
            // missing-name reason explains. Its compiled copy would still list that member.
            if (IsDeclaredByTheRun(type, semanticModel))
            {
                continue;
            }

            // Why internal only: another type's private member is a use the real compiler rejects
            // too, and protected or protected internal members already bind in the worker.
            foreach (ISymbol member in type.GetMembers(memberName))
            {
                if (member.DeclaredAccessibility == Accessibility.Internal)
                {
                    return member;
                }
            }
        }

        return null;
    }

    // Why names and not symbols: the target assembly comes from a separate compilation that
    // imports every member, so its types are other symbols than the ones this body binds to.
    private static INamedTypeSymbol FindInTargetAssemblyOrNull(
        INamedTypeSymbol type,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly)
    {
        IAssemblySymbol owner = type.ContainingAssembly;
        if (owner == null)
        {
            return null;
        }

        bool declaredByTheRun = owner.Identity.Equals(semanticModel.Compilation.Assembly.Identity);
        if (!declaredByTheRun && !owner.Identity.Equals(targetAssembly.Identity))
        {
            return null;
        }

        return targetAssembly.GetTypeByMetadataName(ConstDriftCollector.ToReflectionMetadataName(type));
    }

    private static bool IsOfAssembly(INamedTypeSymbol type, IAssemblySymbol assembly)
    {
        return type.ContainingAssembly != null && type.ContainingAssembly.Identity.Equals(assembly.Identity);
    }

    // Why the compilation's own assembly: it holds only the types the run declares from source,
    // while Compilation.GetTypeByMetadataName would also find every referenced type.
    private static bool IsDeclaredByTheRun(INamedTypeSymbol type, SemanticModel semanticModel)
    {
        return semanticModel.Compilation.Assembly.GetTypeByMetadataName(ConstDriftCollector.ToReflectionMetadataName(type)) != null;
    }

    // Why only fields, properties and invoked methods: those are the uses a run has shown to bind
    // in the shim and to work once patched. A method passed as a delegate and an event have not
    // been run that way, so they stay skipped.
    private static bool IsPatchableKind(ISymbol member, SimpleNameSyntax name)
    {
        if (member is IFieldSymbol || member is IPropertySymbol)
        {
            return true;
        }

        return member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary && IsInvoked(name);
    }

    private static bool IsInvoked(SimpleNameSyntax name)
    {
        SyntaxNode callee = HasReceiver(name) ? name.Parent : name;
        return callee.Parent is InvocationExpressionSyntax invocation && invocation.Expression == callee;
    }

    // Why these places are out of reach: a closure body, an async or iterator state machine, and a
    // delegating shim run as ordinary code of the shim assembly, which the runtime checks for
    // access, so a call to an internal member there throws MethodAccessException after the run
    // reported success. Only the statements copied into the patched method skip that check.
    private static bool RunsOutsideThePatchedMethod(
        SyntaxNode name,
        SyntaxNode bodyNode,
        MethodDeclarationSyntax methodDeclarationOrNull,
        MethodTransformDecision decision)
    {
        if (decision.UsesDelegation)
        {
            return true;
        }

        if (MethodTransformDecider.IsAsyncOrIterator(methodDeclarationOrNull, bodyNode))
        {
            return true;
        }

        foreach (SyntaxNode closureBody in MethodTransformDecider.FindClosureBodies(bodyNode))
        {
            if (closureBody.Span.Contains(name.Span))
            {
                return true;
            }
        }

        return false;
    }
}
