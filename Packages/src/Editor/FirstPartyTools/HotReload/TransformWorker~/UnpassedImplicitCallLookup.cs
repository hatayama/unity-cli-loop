using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// What: finds the internal method of a compiled type of the target assembly that the compiler
/// calls for an await, foreach, await foreach, deconstruction, positional pattern, collection
/// initializer, query, interpolated string handler, implicit Index or Range support, ref struct
/// using, or await using, when the worker's binding found no method for it. The worker
/// reads a referenced assembly through its public surface only, so such a method looks missing to
/// it while the shim compile binds it, and a closure or state machine calling it fails at run time.
/// The method is looked up by the name the construct fixes, on the receiver type or as an extension
/// method whose this parameter accepts the receiver, so a construct nothing can bind keeps failing.
/// </summary>
internal static class UnpassedImplicitCallLookup
{
    /// <summary>
    /// What: the use of the internal method a construct around <paramref name="error"/> calls, or
    /// null when no construct there misses a method that the target assembly declares internally.
    /// </summary>
    internal static UnpassedInternalMemberUse FindUseOrNull(
        Diagnostic error,
        SemanticModel semanticModel,
        SyntaxNode bodyNode,
        MethodDeclarationSyntax methodDeclarationOrNull,
        MethodTransformDecision decision,
        IAssemblySymbol targetAssembly)
    {
        Location location = error.Location;
        if (targetAssembly == null
            || !location.IsInSource
            || location.SourceTree != bodyNode.SyntaxTree
            || !bodyNode.Span.Contains(location.SourceSpan))
        {
            return null;
        }

        SyntaxNode reported = bodyNode.FindNode(location.SourceSpan, getInnermostNodeForTie: true);
        foreach (SyntaxNode construct in reported.AncestorsAndSelf())
        {
            foreach ((ITypeSymbol receiver, string methodName, int? outParameterCount)
                in EnumerateMissingCalls(semanticModel, construct))
            {
                IMethodSymbol method = FindInternalMethodOrNull(
                    receiver,
                    methodName,
                    outParameterCount,
                    semanticModel,
                    targetAssembly);
                if (method != null)
                {
                    return UnpassedInternalMemberUse.ForImplicitCall(
                        method,
                        construct,
                        bodyNode,
                        methodDeclarationOrNull,
                        decision,
                        semanticModel);
                }
            }

            if (construct == bodyNode)
            {
                break;
            }
        }

        return null;
    }

    // Why an expected out-parameter count: a deconstruction or positional pattern calls the
    // Deconstruct with one out parameter per part, and another internal Deconstruct of the same
    // name is not the method the compiled body calls. Every other call leaves it unset.
    private static IEnumerable<(ITypeSymbol Receiver, string MethodName, int? OutParameterCount)> EnumerateMissingCalls(
        SemanticModel semanticModel,
        SyntaxNode construct)
    {
        switch (construct)
        {
            case AwaitExpressionSyntax awaitExpression:
                return AnyArity(UnpassedAwaitCallLookup.MissingAwaitCalls(semanticModel, awaitExpression));
            case CommonForEachStatementSyntax forEach:
                return MissingForEachCalls(semanticModel, forEach);
            case AssignmentExpressionSyntax assignment
                when assignment.Left is TupleExpressionSyntax or DeclarationExpressionSyntax:
                return MissingDeconstructCalls(semanticModel, assignment);
            case RecursivePatternSyntax pattern when pattern.PositionalPatternClause != null:
                return MissingPositionalPatternCalls(semanticModel, pattern);
            case InitializerExpressionSyntax initializer
                when initializer.IsKind(SyntaxKind.CollectionInitializerExpression):
                return AnyArity(MissingCollectionInitializerCalls(semanticModel, initializer));
            case QueryExpressionSyntax query:
                return AnyArity(UnpassedQueryCallLookup.MissingCalls(semanticModel, query));
            case UsingStatementSyntax usingStatement:
                return AnyArity(MissingDisposeCalls(
                    semanticModel,
                    usingStatement.SpanStart,
                    TypeOf(semanticModel, (SyntaxNode)usingStatement.Declaration?.Type ?? usingStatement.Expression),
                    usingStatement.AwaitKeyword != default));
            case LocalDeclarationStatementSyntax localDeclaration when localDeclaration.UsingKeyword != default:
                return AnyArity(MissingDisposeCalls(
                    semanticModel,
                    localDeclaration.SpanStart,
                    TypeOf(semanticModel, localDeclaration.Declaration.Type),
                    localDeclaration.AwaitKeyword != default));
            default:
                return AnyArity(MissingExpressionPatternCalls(semanticModel, construct));
        }
    }

    private static IEnumerable<(ITypeSymbol, string, int?)> AnyArity(IEnumerable<(ITypeSymbol, string)> calls)
    {
        return calls.Select(call => (call.Item1, call.Item2, (int?)null));
    }

    // The expressions whose pattern members the compiler calls: an interpolated string, or an
    // addition of interpolated strings, converted to a handler (the creation of an explicit
    // conversion belongs to the cast), and an element access, plain or after `?.`, that goes
    // through the implicit Index or Range support.
    private static IEnumerable<(ITypeSymbol Receiver, string MethodName)> MissingExpressionPatternCalls(
        SemanticModel semanticModel,
        SyntaxNode construct)
    {
        switch (construct)
        {
            case InterpolatedStringExpressionSyntax interpolatedString:
                return MissingHandlerCalls(semanticModel, interpolatedString);
            case BinaryExpressionSyntax addition when addition.IsKind(SyntaxKind.AddExpression):
                return MissingHandlerCalls(semanticModel, addition);
            case CastExpressionSyntax cast:
                return MissingHandlerCalls(semanticModel, cast);
            case ElementAccessExpressionSyntax elementAccess:
                return MissingIndexOrRangeCalls(
                    semanticModel,
                    elementAccess,
                    elementAccess.Expression,
                    elementAccess.ArgumentList);
            case ElementBindingExpressionSyntax elementBinding:
                return MissingIndexOrRangeCalls(
                    semanticModel,
                    elementBinding,
                    elementBinding.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault()?.Expression,
                    elementBinding.ArgumentList);
            default:
                return Array.Empty<(ITypeSymbol, string)>();
        }
    }

    // Why the parts' syntax names the method: a part whose append call bound to nothing has no
    // method left, and a literal part calls AppendLiteral while an interpolation calls
    // AppendFormatted.
    private static IEnumerable<(ITypeSymbol, string)> MissingHandlerCalls(
        SemanticModel semanticModel,
        ExpressionSyntax converted)
    {
        IInterpolatedStringHandlerCreationOperation creation =
            ImplicitCallTargets.FindHandlerCreationOrNull(semanticModel, converted);
        if (creation == null)
        {
            return Array.Empty<(ITypeSymbol, string)>();
        }

        List<(ITypeSymbol, string)> missing = new List<(ITypeSymbol, string)>();
        if (creation.HandlerCreation is not IObjectCreationOperation)
        {
            missing.Add((creation.Type, WellKnownMemberNames.InstanceConstructorName));
        }

        foreach (IInterpolatedStringAppendOperation append in ImplicitCallTargets.AppendOperations(creation.Content))
        {
            if (append.AppendCall is not IInvocationOperation)
            {
                missing.Add((creation.Type, append.Syntax is InterpolationSyntax ? "AppendFormatted" : "AppendLiteral"));
            }
        }

        return missing;
    }

    // Why every member of the support pattern the access calls: an access the worker could not
    // bind does not say which of Length, Count, the int indexer or Slice it missed, and the first
    // internal one found is a member the compiled access calls. Length and Count are left out for
    // a range the compiler lowers without reading them.
    private static IEnumerable<(ITypeSymbol, string)> MissingIndexOrRangeCalls(
        SemanticModel semanticModel,
        ExpressionSyntax access,
        ExpressionSyntax receiverExpression,
        BracketedArgumentListSyntax arguments)
    {
        if (semanticModel.GetSymbolInfo(access).Symbol != null)
        {
            return Array.Empty<(ITypeSymbol, string)>();
        }

        // Why Nullable<T> is unwrapped: after `?.` on a nullable struct the access runs on the
        // underlying value, whose members are the ones the compiler calls.
        ITypeSymbol receiver = TypeOf(semanticModel, receiverExpression);
        if (receiver is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            receiver = nullable.TypeArguments[0];
        }

        // Why only the indexer otherwise: the implicit Index and Range support applies to a single
        // Index or Range argument, and any other access that bound to nothing calls an indexer of
        // its own shape, which may be the internal member.
        string argumentType = arguments.Arguments.Count == 1
            ? TypeOf(semanticModel, arguments.Arguments[0].Expression)?.ToDisplayString()
            : null;
        bool isRange = argumentType == "System.Range";
        bool isIndex = argumentType == "System.Index";
        if (!isRange && !isIndex)
        {
            return new[] { (receiver, WellKnownMemberNames.Indexer) };
        }

        List<(ITypeSymbol, string)> candidates = new List<(ITypeSymbol, string)>();
        if (ImplicitCallTargets.ReadsLength(semanticModel, arguments))
        {
            candidates.Add((receiver, "Length"));
            candidates.Add((receiver, "Count"));
        }

        if (isRange)
        {
            candidates.Add((receiver, "Slice"));
        }

        // Why the indexer for a range too: a type with its own Range indexer is indexed through it
        // directly, without the implicit support.
        candidates.Add((receiver, WellKnownMemberNames.Indexer));
        return candidates;
    }

    private static IEnumerable<(ITypeSymbol, string, int?)> MissingDeconstructCalls(
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment)
    {
        List<(ITypeSymbol, string, int?)> missing = new List<(ITypeSymbol, string, int?)>();
        AddMissingDeconstructCalls(
            assignment.Left,
            TypeOf(semanticModel, assignment.Right),
            semanticModel.GetDeconstructionInfo(assignment),
            missing);
        return missing;
    }

    // Why element types are traced: each nested target on the left deconstructs one part of the
    // value, a tuple element or an out parameter of the Deconstruct that bound, and that part's
    // Deconstruct is the call that may be missing.
    private static void AddMissingDeconstructCalls(
        SyntaxNode left,
        ITypeSymbol value,
        DeconstructionInfo info,
        List<(ITypeSymbol, string, int?)> missing)
    {
        IReadOnlyList<ITypeSymbol> parts = PartTypesOrNull(value, info);
        IReadOnlyList<SyntaxNode> targets = DeconstructionTargetsOrNull(left);
        if (parts == null)
        {
            missing.Add((value, "Deconstruct", targets?.Count));
            return;
        }

        if (targets == null || targets.Count != parts.Count)
        {
            return;
        }

        for (int index = 0; index < targets.Count; index++)
        {
            if (DeconstructionTargetsOrNull(targets[index]) == null)
            {
                continue;
            }

            DeconstructionInfo nested = info.Nested.Length == targets.Count ? info.Nested[index] : default;
            AddMissingDeconstructCalls(targets[index], parts[index], nested, missing);
        }
    }

    // The types a value splits into, or null when no Deconstruct bound for it.
    private static IReadOnlyList<ITypeSymbol> PartTypesOrNull(ITypeSymbol value, DeconstructionInfo info)
    {
        if (value is INamedTypeSymbol { IsTupleType: true } tuple)
        {
            return tuple.TupleElements.Select(element => element.Type).ToList();
        }

        if (info.Method == null)
        {
            return null;
        }

        return info.Method.Parameters
            .Where(parameter => parameter.RefKind == RefKind.Out)
            .Select(parameter => parameter.Type)
            .ToList();
    }

    private static IReadOnlyList<SyntaxNode> DeconstructionTargetsOrNull(SyntaxNode target)
    {
        switch (target)
        {
            case TupleExpressionSyntax tuple:
                return tuple.Arguments.Select(argument => (SyntaxNode)argument.Expression).ToList();
            case DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax designation }:
                return designation.Variables.ToList<SyntaxNode>();
            case ParenthesizedVariableDesignationSyntax designation:
                return designation.Variables.ToList<SyntaxNode>();
            default:
                return null;
        }
    }

    private static IEnumerable<(ITypeSymbol, string, int?)> MissingForEachCalls(
        SemanticModel semanticModel,
        CommonForEachStatementSyntax forEach)
    {
        ForEachStatementInfo info = semanticModel.GetForEachStatementInfo(forEach);
        bool isAwaitForEach = forEach.AwaitKeyword != default;
        if (!isAwaitForEach && info.GetEnumeratorMethod == null)
        {
            return AnyArity(Single(TypeOf(semanticModel, forEach.Expression), "GetEnumerator"));
        }

        // Why an await foreach is traced even when its enumerator bound: the worker's binding keeps
        // GetAsyncEnumerator, MoveNextAsync and DisposeAsync when only the awaiter of a result they
        // return is missing, so the bound methods do not show which call failed.
        List<(ITypeSymbol, string, int?)> missing = new List<(ITypeSymbol, string, int?)>();
        if (isAwaitForEach)
        {
            missing.AddRange(AnyArity(UnpassedAwaitCallLookup.MissingAwaitForEachCalls(
                semanticModel,
                forEach.SpanStart,
                TypeOf(semanticModel, forEach.Expression))));
        }

        if (forEach is ForEachVariableStatementSyntax forEachVariable
            && info.ElementType != null
            && semanticModel.GetDeconstructionInfo(forEachVariable).Method == null)
        {
            missing.Add((
                info.ElementType,
                "Deconstruct",
                DeconstructionTargetsOrNull(forEachVariable.Variable)?.Count));
        }

        return missing;
    }

    // Why the input type from the context: a pattern that binds to no Deconstruct has no reliable
    // operation, while the matched expression and an explicit type still have their types.
    private static IEnumerable<(ITypeSymbol, string, int?)> MissingPositionalPatternCalls(
        SemanticModel semanticModel,
        RecursivePatternSyntax pattern)
    {
        if ((semanticModel.GetOperation(pattern) as IRecursivePatternOperation)?.DeconstructSymbol != null)
        {
            return Array.Empty<(ITypeSymbol, string, int?)>();
        }

        SyntaxNode typed = pattern.Type ?? FindMatchedExpressionOrNull(pattern);
        return new[]
        {
            (TypeOf(semanticModel, typed), "Deconstruct", (int?)pattern.PositionalPatternClause.Subpatterns.Count),
        };
    }

    private static SyntaxNode FindMatchedExpressionOrNull(RecursivePatternSyntax pattern)
    {
        switch (pattern.Parent)
        {
            case IsPatternExpressionSyntax isPattern:
                return isPattern.Expression;
            case SwitchExpressionArmSyntax arm when arm.Parent is SwitchExpressionSyntax switchExpression:
                return switchExpression.GoverningExpression;
            case CasePatternSwitchLabelSyntax label when label.Parent?.Parent is SwitchStatementSyntax switchStatement:
                return switchStatement.Expression;
            default:
                return null;
        }
    }

    private static IEnumerable<(ITypeSymbol, string)> MissingCollectionInitializerCalls(
        SemanticModel semanticModel,
        InitializerExpressionSyntax initializer)
    {
        foreach (ExpressionSyntax element in initializer.Expressions)
        {
            if (semanticModel.GetCollectionInitializerSymbolInfo(element).Symbol == null)
            {
                return Single(TypeOf(semanticModel, initializer.Parent), "Add");
            }
        }

        return Array.Empty<(ITypeSymbol, string)>();
    }

    private static IEnumerable<(ITypeSymbol, string)> MissingDisposeCalls(
        SemanticModel semanticModel,
        int position,
        ITypeSymbol resourceType,
        bool isAwaitUsing)
    {
        if (resourceType == null)
        {
            return Array.Empty<(ITypeSymbol, string)>();
        }

        if (isAwaitUsing)
        {
            return Implements(resourceType, "System.IAsyncDisposable")
                ? Array.Empty<(ITypeSymbol, string)>()
                : UnpassedAwaitCallLookup.MissingAwaitUsingCalls(semanticModel, position, resourceType);
        }

        return resourceType.IsRefLikeType && !Implements(resourceType, "System.IDisposable")
            ? Single(resourceType, "Dispose")
            : Array.Empty<(ITypeSymbol, string)>();
    }

    private static IMethodSymbol FindInternalMethodOrNull(
        ITypeSymbol receiver,
        string methodName,
        int? outParameterCount,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly)
    {
        if (receiver == null)
        {
            return null;
        }

        // Why an array goes straight to the extensions: it declares no members of its own, but a
        // `this T` extension can still take it.
        if (receiver is not INamedTypeSymbol namedReceiver)
        {
            return FindInternalExtensionOrNull(receiver, methodName, outParameterCount, targetAssembly);
        }

        ISymbol member = UnpassedInternalMemberUse.FindInternalMemberOfUnpassedTypeOrNull(
            namedReceiver,
            methodName,
            outParameterCount,
            semanticModel,
            targetAssembly);
        if (member is IMethodSymbol instanceMethod)
        {
            return instanceMethod;
        }

        // An awaiter's IsCompleted is a property; the compiler calls its getter.
        if (member is IPropertySymbol { GetMethod: not null } property)
        {
            return property.GetMethod;
        }

        return FindInternalExtensionOrNull(namedReceiver, methodName, outParameterCount, targetAssembly);
    }

    // Why names and not symbols: the target assembly comes from a separate compilation, so its
    // types are other symbols than the receiver the body binds to.
    private static IMethodSymbol FindInternalExtensionOrNull(
        ITypeSymbol receiver,
        string methodName,
        int? outParameterCount,
        IAssemblySymbol targetAssembly)
    {
        HashSet<string> acceptedThisTypes = AcceptedThisTypeNames(receiver);
        foreach (INamedTypeSymbol type in EnumerateTypes(targetAssembly.GlobalNamespace))
        {
            if (!type.MightContainExtensionMethods)
            {
                continue;
            }

            foreach (IMethodSymbol method in type.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (method.IsExtensionMethod
                    && UnpassedInternalMemberUse.HasOutParameterCount(method, outParameterCount)
                    && AcceptsReceiver(method.Parameters[0].Type, receiver, acceptedThisTypes)
                    && AccessibilityRules.IsInaccessibleFromExternalAssembly(method))
                {
                    return method;
                }
            }
        }

        return null;
    }

    // Why a type parameter is compared by its constraints: `this T` accepts every receiver its
    // constraints allow, and an unconstrained one accepts any receiver.
    private static bool AcceptsReceiver(ITypeSymbol thisType, ITypeSymbol receiver, HashSet<string> acceptedThisTypes)
    {
        switch (thisType)
        {
            case INamedTypeSymbol named:
                return acceptedThisTypes.Contains(ConstDriftCollector.ToReflectionMetadataName(named.OriginalDefinition));
            // Why the element is compared as the receiver: `this T[]` takes an array of the same
            // rank whose element its element type accepts.
            case IArrayTypeSymbol arrayThisType:
                return receiver is IArrayTypeSymbol arrayReceiver
                    && arrayReceiver.Rank == arrayThisType.Rank
                    && AcceptsReceiver(
                        arrayThisType.ElementType,
                        arrayReceiver.ElementType,
                        AcceptedThisTypeNames(arrayReceiver.ElementType));
            case ITypeParameterSymbol typeParameter:
                return SatisfiesKindConstraints(typeParameter, receiver)
                    && typeParameter.ConstraintTypes.All(constraint => constraint is INamedTypeSymbol namedConstraint
                        && acceptedThisTypes.Contains(
                            ConstDriftCollector.ToReflectionMetadataName(namedConstraint.OriginalDefinition)));
            default:
                return false;
        }
    }

    // The constraints on what kind of type the receiver is: class, struct, unmanaged, and new().
    // Why only a public parameterless constructor counts for new(): the worker sees the receiver's
    // assembly through its public surface, as the body that calls the extension does.
    private static bool SatisfiesKindConstraints(ITypeParameterSymbol typeParameter, ITypeSymbol receiver)
    {
        // Why Nullable<T> fails the struct constraint: C# requires a non-nullable value type there.
        if (typeParameter.HasValueTypeConstraint
            && (!receiver.IsValueType || receiver.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T))
        {
            return false;
        }

        if (typeParameter.HasReferenceTypeConstraint && !receiver.IsReferenceType)
        {
            return false;
        }

        if (typeParameter.HasUnmanagedTypeConstraint && !receiver.IsUnmanagedType)
        {
            return false;
        }

        // Why a type-parameter receiver passes new() by its own constraint: it has no constructors
        // to inspect, and the compiler accepts it when it is itself constrained to new().
        return !typeParameter.HasConstructorConstraint
            || receiver.IsValueType
            || receiver is ITypeParameterSymbol { HasConstructorConstraint: true }
            || (receiver is INamedTypeSymbol { IsAbstract: false } named && named.InstanceConstructors.Any(constructor =>
                constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public));
    }

    // The receiver, its base types and its interfaces, any of which an extension's 'this' may name.
    private static HashSet<string> AcceptedThisTypeNames(ITypeSymbol receiver)
    {
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        for (INamedTypeSymbol type = receiver as INamedTypeSymbol ?? receiver.BaseType; type != null; type = type.BaseType)
        {
            names.Add(ConstDriftCollector.ToReflectionMetadataName(type.OriginalDefinition));
        }

        foreach (INamedTypeSymbol implemented in receiver.AllInterfaces)
        {
            names.Add(ConstDriftCollector.ToReflectionMetadataName(implemented.OriginalDefinition));
        }

        return names;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (INamedTypeSymbol type in namespaceSymbol.GetTypeMembers())
        {
            yield return type;
        }

        foreach (INamespaceSymbol nested in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (INamedTypeSymbol type in EnumerateTypes(nested))
            {
                yield return type;
            }
        }
    }

    private static bool Implements(ITypeSymbol type, string interfaceFullName)
    {
        return type.AllInterfaces.Any(candidate => candidate.ToDisplayString() == interfaceFullName);
    }

    private static ITypeSymbol TypeOf(SemanticModel semanticModel, SyntaxNode node)
    {
        return node == null ? null : semanticModel.GetTypeInfo(node).Type;
    }

    private static IEnumerable<(ITypeSymbol, string)> Single(ITypeSymbol receiver, string methodName)
    {
        return new[] { (receiver, methodName) };
    }
}
