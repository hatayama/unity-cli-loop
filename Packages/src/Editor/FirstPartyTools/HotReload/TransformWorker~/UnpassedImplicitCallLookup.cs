using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// What: finds the internal method of a compiled type of the target assembly that the compiler
/// calls for an await, foreach, deconstruction, positional pattern, collection initializer, query,
/// ref struct using, or await using, when the worker's binding found no method for it. The worker
/// reads a referenced assembly through its public surface only, so such a method looks missing to
/// it while the shim compile binds it, and a closure or state machine calling it fails at run time.
/// The method is looked up by the name the construct fixes, on the receiver type or as an extension
/// method whose first parameter accepts the receiver, so a construct nothing can bind keeps failing.
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
                return MissingAwaitCalls(semanticModel, awaitExpression);
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
                return MissingQueryCalls(semanticModel, query);
            case UsingStatementSyntax usingStatement:
                return MissingDisposeCalls(
                    TypeOf(semanticModel, (SyntaxNode)usingStatement.Declaration?.Type ?? usingStatement.Expression),
                    usingStatement.AwaitKeyword != default);
            case LocalDeclarationStatementSyntax localDeclaration when localDeclaration.UsingKeyword != default:
                return MissingDisposeCalls(
                    TypeOf(semanticModel, localDeclaration.Declaration.Type),
                    localDeclaration.AwaitKeyword != default);
            default:
                return Array.Empty<(ITypeSymbol, string)>();
        }
    }

    private static IEnumerable<(ITypeSymbol, string)> MissingAwaitCalls(
        SemanticModel semanticModel,
        AwaitExpressionSyntax awaitExpression)
    {
        AwaitExpressionInfo info = semanticModel.GetAwaitExpressionInfo(awaitExpression);
        if (info.GetAwaiterMethod == null)
        {
            return Single(TypeOf(semanticModel, awaitExpression.Expression), "GetAwaiter");
        }

        List<(ITypeSymbol, string)> missing = new List<(ITypeSymbol, string)>();
        ITypeSymbol awaiter = info.GetAwaiterMethod.ReturnType;
        if (info.IsCompletedProperty == null)
        {
            missing.Add((awaiter, "IsCompleted"));
        }

        if (info.GetResultMethod == null)
        {
            missing.Add((awaiter, "GetResult"));
        }

        return missing;
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
        if (info.GetEnumeratorMethod == null)
        {
            string name = forEach.AwaitKeyword != default ? "GetAsyncEnumerator" : "GetEnumerator";
            return Single(TypeOf(semanticModel, forEach.Expression), name);
        }

        if (forEach is ForEachVariableStatementSyntax forEachVariable
            && semanticModel.GetDeconstructionInfo(forEachVariable).Method == null)
        {
            return Single(info.ElementType, "Deconstruct");
        }

        return Array.Empty<(ITypeSymbol, string)>();
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

    // Why the receiver is carried: each clause calls its query method on what the previous call
    // returned. After a clause that binds to nothing, the last type known stands in, which is right
    // for a query whose methods all return the same sequence type.
    private static IEnumerable<(ITypeSymbol, string)> MissingQueryCalls(
        SemanticModel semanticModel,
        QueryExpressionSyntax query)
    {
        ITypeSymbol receiver = TypeOf(semanticModel, query.FromClause.Expression);
        List<(ITypeSymbol, string)> missing = new List<(ITypeSymbol, string)>();
        if (query.FromClause.Type != null)
        {
            receiver = CallOrRecordMissing(
                semanticModel.GetQueryClauseInfo(query.FromClause).CastInfo.Symbol,
                receiver,
                "Cast",
                missing);
        }

        foreach (QueryClauseSyntax clause in query.Body.Clauses)
        {
            receiver = AddMissingClauseCalls(semanticModel, clause, receiver, missing);
        }

        CallOrRecordMissing(
            semanticModel.GetSymbolInfo(query.Body.SelectOrGroup).Symbol,
            receiver,
            query.Body.SelectOrGroup is GroupClauseSyntax ? "GroupBy" : "Select",
            missing);
        return missing;
    }

    // The type the query goes on with: the bound method's return type, or the receiver unchanged
    // after recording the method as missing.
    private static ITypeSymbol CallOrRecordMissing(
        ISymbol bound,
        ITypeSymbol receiver,
        string methodName,
        List<(ITypeSymbol, string)> missing)
    {
        if (bound is IMethodSymbol method)
        {
            return method.ReturnType;
        }

        missing.Add((receiver, methodName));
        return receiver;
    }

    private static ITypeSymbol AddMissingClauseCalls(
        SemanticModel semanticModel,
        QueryClauseSyntax clause,
        ITypeSymbol receiver,
        List<(ITypeSymbol, string)> missing)
    {
        if (clause is OrderByClauseSyntax orderBy)
        {
            return AddMissingOrderingCalls(semanticModel, orderBy, receiver, missing);
        }

        QueryClauseInfo info = semanticModel.GetQueryClauseInfo(clause);
        if (clause is JoinClauseSyntax join && join.Type != null && info.CastInfo.Symbol == null)
        {
            missing.Add((TypeOf(semanticModel, join.InExpression), "Cast"));
        }

        if (clause is FromClauseSyntax from && from.Type != null && info.CastInfo.Symbol == null)
        {
            missing.Add((TypeOf(semanticModel, from.Expression), "Cast"));
        }

        return CallOrRecordMissing(info.OperationInfo.Symbol, receiver, OperationName(clause), missing);
    }

    private static ITypeSymbol AddMissingOrderingCalls(
        SemanticModel semanticModel,
        OrderByClauseSyntax orderBy,
        ITypeSymbol receiver,
        List<(ITypeSymbol, string)> missing)
    {
        for (int index = 0; index < orderBy.Orderings.Count; index++)
        {
            OrderingSyntax ordering = orderBy.Orderings[index];
            bool descending = ordering.AscendingOrDescendingKeyword.IsKind(SyntaxKind.DescendingKeyword);
            string name = index == 0
                ? (descending ? "OrderByDescending" : "OrderBy")
                : (descending ? "ThenByDescending" : "ThenBy");
            receiver = CallOrRecordMissing(semanticModel.GetSymbolInfo(ordering).Symbol, receiver, name, missing);
        }

        return receiver;
    }

    private static string OperationName(QueryClauseSyntax clause)
    {
        switch (clause)
        {
            case WhereClauseSyntax:
                return "Where";
            case FromClauseSyntax:
                return "SelectMany";
            case JoinClauseSyntax join:
                return join.Into != null ? "GroupJoin" : "Join";
            default:
                return "Select";
        }
    }

    private static IEnumerable<(ITypeSymbol, string)> MissingDisposeCalls(ITypeSymbol resourceType, bool isAwaitUsing)
    {
        if (resourceType == null)
        {
            return Array.Empty<(ITypeSymbol, string)>();
        }

        if (isAwaitUsing)
        {
            return Implements(resourceType, "System.IAsyncDisposable")
                ? Array.Empty<(ITypeSymbol, string)>()
                : Single(resourceType, "DisposeAsync");
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
                    && method.Parameters[0].Type is INamedTypeSymbol thisType
                    && acceptedThisTypes.Contains(ConstDriftCollector.ToReflectionMetadataName(thisType.OriginalDefinition))
                    && AccessibilityRules.IsInaccessibleFromExternalAssembly(method))
                {
                    return method;
                }
            }
        }

        return null;
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
