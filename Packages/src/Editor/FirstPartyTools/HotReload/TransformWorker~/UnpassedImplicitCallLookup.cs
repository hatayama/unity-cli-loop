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
            foreach ((ITypeSymbol receiver, string methodName) in EnumerateMissingCalls(semanticModel, construct))
            {
                IMethodSymbol method = FindInternalMethodOrNull(receiver, methodName, semanticModel, targetAssembly);
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

    private static IEnumerable<(ITypeSymbol Receiver, string MethodName)> EnumerateMissingCalls(
        SemanticModel semanticModel,
        SyntaxNode construct)
    {
        switch (construct)
        {
            case AwaitExpressionSyntax awaitExpression:
                return UnpassedAwaitCallLookup.MissingAwaitCalls(semanticModel, awaitExpression);
            case CommonForEachStatementSyntax forEach:
                return MissingForEachCalls(semanticModel, forEach);
            case AssignmentExpressionSyntax assignment
                when assignment.Left is TupleExpressionSyntax or DeclarationExpressionSyntax:
                return MissingDeconstructCalls(semanticModel, assignment);
            case RecursivePatternSyntax pattern when pattern.PositionalPatternClause != null:
                return MissingPositionalPatternCalls(semanticModel, pattern);
            case InitializerExpressionSyntax initializer
                when initializer.IsKind(SyntaxKind.CollectionInitializerExpression):
                return MissingCollectionInitializerCalls(semanticModel, initializer);
            case QueryExpressionSyntax query:
                return UnpassedQueryCallLookup.MissingCalls(semanticModel, query);
            case UsingStatementSyntax usingStatement:
                return MissingDisposeCalls(
                    semanticModel,
                    usingStatement.SpanStart,
                    TypeOf(semanticModel, (SyntaxNode)usingStatement.Declaration?.Type ?? usingStatement.Expression),
                    usingStatement.AwaitKeyword != default);
            case LocalDeclarationStatementSyntax localDeclaration when localDeclaration.UsingKeyword != default:
                return MissingDisposeCalls(
                    semanticModel,
                    localDeclaration.SpanStart,
                    TypeOf(semanticModel, localDeclaration.Declaration.Type),
                    localDeclaration.AwaitKeyword != default);
            default:
                return MissingExpressionPatternCalls(semanticModel, construct);
        }
    }

    // The expressions whose pattern members the compiler calls: an interpolated string, or an
    // addition of interpolated strings, converted to a handler, and an element access, plain or
    // after `?.`, that goes through the implicit Index or Range support.
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

        bool isRange = false;
        bool isIndex = false;
        foreach (ArgumentSyntax argument in arguments.Arguments)
        {
            string argumentType = TypeOf(semanticModel, argument.Expression)?.ToDisplayString();
            isRange |= argumentType == "System.Range";
            isIndex |= argumentType == "System.Index";
        }

        if (!isRange && !isIndex)
        {
            return Array.Empty<(ITypeSymbol, string)>();
        }

        ITypeSymbol receiver = TypeOf(semanticModel, receiverExpression);
        List<(ITypeSymbol, string)> candidates = new List<(ITypeSymbol, string)>();
        if (ImplicitCallTargets.ReadsLength(semanticModel, arguments))
        {
            candidates.Add((receiver, "Length"));
            candidates.Add((receiver, "Count"));
        }

        candidates.Add((receiver, isRange ? "Slice" : WellKnownMemberNames.Indexer));
        return candidates;
    }

    private static IEnumerable<(ITypeSymbol, string)> MissingDeconstructCalls(
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment)
    {
        List<(ITypeSymbol, string)> missing = new List<(ITypeSymbol, string)>();
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
        List<(ITypeSymbol, string)> missing)
    {
        IReadOnlyList<ITypeSymbol> parts = PartTypesOrNull(value, info);
        if (parts == null)
        {
            missing.Add((value, "Deconstruct"));
            return;
        }

        IReadOnlyList<SyntaxNode> targets = DeconstructionTargetsOrNull(left);
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

    private static IEnumerable<(ITypeSymbol, string)> MissingForEachCalls(
        SemanticModel semanticModel,
        CommonForEachStatementSyntax forEach)
    {
        ForEachStatementInfo info = semanticModel.GetForEachStatementInfo(forEach);
        bool isAwaitForEach = forEach.AwaitKeyword != default;
        if (!isAwaitForEach && info.GetEnumeratorMethod == null)
        {
            return Single(TypeOf(semanticModel, forEach.Expression), "GetEnumerator");
        }

        // Why an await foreach is traced even when its enumerator bound: the worker's binding keeps
        // GetAsyncEnumerator, MoveNextAsync and DisposeAsync when only the awaiter of a result they
        // return is missing, so the bound methods do not show which call failed.
        List<(ITypeSymbol, string)> missing = new List<(ITypeSymbol, string)>();
        if (isAwaitForEach)
        {
            missing.AddRange(UnpassedAwaitCallLookup.MissingAwaitForEachCalls(
                semanticModel,
                forEach.SpanStart,
                TypeOf(semanticModel, forEach.Expression)));
        }

        if (forEach is ForEachVariableStatementSyntax forEachVariable
            && info.ElementType != null
            && semanticModel.GetDeconstructionInfo(forEachVariable).Method == null)
        {
            missing.Add((info.ElementType, "Deconstruct"));
        }

        return missing;
    }

    // Why the input type from the context: a pattern that binds to no Deconstruct has no reliable
    // operation, while the matched expression and an explicit type still have their types.
    private static IEnumerable<(ITypeSymbol, string)> MissingPositionalPatternCalls(
        SemanticModel semanticModel,
        RecursivePatternSyntax pattern)
    {
        if ((semanticModel.GetOperation(pattern) as IRecursivePatternOperation)?.DeconstructSymbol != null)
        {
            return Array.Empty<(ITypeSymbol, string)>();
        }

        SyntaxNode typed = pattern.Type ?? FindMatchedExpressionOrNull(pattern);
        return Single(TypeOf(semanticModel, typed), "Deconstruct");
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
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly)
    {
        if (receiver is not INamedTypeSymbol namedReceiver)
        {
            return null;
        }

        ISymbol member = UnpassedInternalMemberUse.FindInternalMemberOfUnpassedTypeOrNull(
            namedReceiver,
            methodName,
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

        return FindInternalExtensionOrNull(namedReceiver, methodName, targetAssembly);
    }

    // Why names and not symbols: the target assembly comes from a separate compilation, so its
    // types are other symbols than the receiver the body binds to.
    private static IMethodSymbol FindInternalExtensionOrNull(
        INamedTypeSymbol receiver,
        string methodName,
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
    private static bool AcceptsReceiver(ITypeSymbol thisType, INamedTypeSymbol receiver, HashSet<string> acceptedThisTypes)
    {
        switch (thisType)
        {
            case INamedTypeSymbol named:
                return acceptedThisTypes.Contains(ConstDriftCollector.ToReflectionMetadataName(named.OriginalDefinition));
            case ITypeParameterSymbol typeParameter:
                return (!typeParameter.HasValueTypeConstraint || receiver.IsValueType)
                    && (!typeParameter.HasReferenceTypeConstraint || receiver.IsReferenceType)
                    && typeParameter.ConstraintTypes.All(constraint => constraint is INamedTypeSymbol namedConstraint
                        && acceptedThisTypes.Contains(
                            ConstDriftCollector.ToReflectionMetadataName(namedConstraint.OriginalDefinition)));
            default:
                return false;
        }
    }

    // The receiver, its base types and its interfaces, any of which an extension's 'this' may name.
    private static HashSet<string> AcceptedThisTypeNames(INamedTypeSymbol receiver)
    {
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        for (INamedTypeSymbol type = receiver; type != null; type = type.BaseType)
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
