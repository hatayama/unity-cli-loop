using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// What: methods the compiler calls for a syntax node with no invocation of them in the source
/// (await, foreach, await foreach, deconstruction, collection initializer, query expression,
/// interpolated string handler, implicit Index or Range support, ref struct using, await using).
/// A closure or state machine emitted into the shim assembly makes those calls too, so an
/// inaccessible one fails at run time exactly like a written call. None has an accessor rewrite,
/// so such a body is refused.
/// </summary>
internal static class ImplicitCallTargets
{
    private const string AwaitConstruct = "await";
    private const string ForEachConstruct = "foreach";
    private const string DeconstructionConstruct = "a deconstruction";
    private const string CollectionInitializerConstruct = "a collection initializer";
    private const string QueryConstruct = "a query expression";
    private const string UsingConstruct = "using";
    private const string AwaitUsingConstruct = "await using";
    private const string PositionalPatternConstruct = "a positional pattern";
    private const string InterpolatedStringHandlerConstruct = "an interpolated string handler";
    internal const string IndexOrRangeConstruct = "an index or range";
    private const string AwaitForEachConstruct = "await foreach";

    /// <summary>
    /// What: the refusal for the first inaccessible method the compiler calls for this node, or
    /// null when the node makes no such call.
    /// </summary>
    /// <remarks>
    /// Why the enforcing binding when a target is missing: an operand whose type a referenced DLL
    /// also declares internally (Harmony's copies of Memory and Span) gets an error type under the
    /// accessibility-ignoring binding, so the compiler's call is not found there; see
    /// AccessibilityRules.BoundSymbolOrNull. A missing target is also normal (foreach over an array,
    /// a tuple deconstruction), so the enforcing pass only adds a second look.
    /// </remarks>
    internal static WorkerReason FindInaccessibleCallReasonOrNull(SemanticModel semanticModel, SyntaxNode node)
    {
        WorkerReason reason = FindReasonOrNull(semanticModel, node, out bool metMissingTarget);
        if (reason != null || !metMissingTarget)
        {
            return reason;
        }

        return FindReasonOrNull(AccessibilityRules.EnforcingModel(semanticModel), node, out _);
    }

    private static WorkerReason FindReasonOrNull(SemanticModel semanticModel, SyntaxNode node, out bool metMissingTarget)
    {
        metMissingTarget = false;
        foreach ((IMethodSymbol method, string construct) in EnumerateTargets(semanticModel, node))
        {
            if (method == null)
            {
                metMissingTarget = true;
                continue;
            }

            IMethodSymbol target = AccessibilityRules.UnreducedMethod(method);
            if (AccessibilityRules.IsInaccessibleFromExternalAssembly(target))
            {
                return WorkerReason.Of(
                    HotReloadWorkerReasonCode.AccessorImplicitCallNoShape,
                    DisplayName(target),
                    construct);
            }
        }

        return null;
    }

    private static IEnumerable<(IMethodSymbol Method, string Construct)> EnumerateTargets(
        SemanticModel semanticModel,
        SyntaxNode node)
    {
        switch (node)
        {
            case AwaitExpressionSyntax awaitExpression:
                return AwaitTargets(semanticModel.GetAwaitExpressionInfo(awaitExpression), AwaitConstruct);
            case CommonForEachStatementSyntax forEach:
                return ForEachTargets(semanticModel, forEach);
            case AssignmentExpressionSyntax assignment when IsDeconstruction(assignment):
                return DeconstructionTargets(semanticModel.GetDeconstructionInfo(assignment));
            case InitializerExpressionSyntax initializer
                when initializer.IsKind(SyntaxKind.CollectionInitializerExpression):
                return CollectionInitializerTargets(semanticModel, initializer);
            case SelectOrGroupClauseSyntax selectOrGroup:
                return Single(semanticModel.GetSymbolInfo(selectOrGroup).Symbol, QueryConstruct);
            case OrderingSyntax ordering:
                return Single(semanticModel.GetSymbolInfo(ordering).Symbol, QueryConstruct);
            case QueryClauseSyntax queryClause:
                return QueryClauseTargets(semanticModel.GetQueryClauseInfo(queryClause));
            case RecursivePatternSyntax pattern when pattern.PositionalPatternClause != null:
                return Single(
                    (semanticModel.GetOperation(pattern) as IRecursivePatternOperation)?.DeconstructSymbol,
                    PositionalPatternConstruct);
            case UsingStatementSyntax usingStatement:
                return UsingTargets(
                    semanticModel,
                    usingStatement,
                    usingStatement.Declaration?.Type,
                    usingStatement.Expression,
                    usingStatement.AwaitKeyword != default);
            case LocalDeclarationStatementSyntax localDeclaration
                when localDeclaration.UsingKeyword != default:
                return UsingTargets(
                    semanticModel,
                    localDeclaration,
                    localDeclaration.Declaration.Type,
                    null,
                    localDeclaration.AwaitKeyword != default);
            default:
                return ExpressionPatternTargets(semanticModel, node);
        }
    }

    // The expressions whose pattern members the compiler calls: an interpolated string, or an
    // addition of interpolated strings, converted to a handler (the creation of an explicit
    // conversion belongs to the cast), and an element access, plain or after `?.`, that goes
    // through the implicit Index or Range support.
    private static IEnumerable<(IMethodSymbol Method, string Construct)> ExpressionPatternTargets(
        SemanticModel semanticModel,
        SyntaxNode node)
    {
        switch (node)
        {
            case InterpolatedStringExpressionSyntax interpolatedString:
                return HandlerTargets(FindHandlerCreationOrNull(semanticModel, interpolatedString));
            case BinaryExpressionSyntax addition when addition.IsKind(SyntaxKind.AddExpression):
                return HandlerTargets(FindHandlerCreationOrNull(semanticModel, addition));
            case CastExpressionSyntax cast:
                return HandlerTargets(FindHandlerCreationOrNull(semanticModel, cast));
            case ElementAccessExpressionSyntax elementAccess:
                return IndexOrRangeTargets(semanticModel, elementAccess, elementAccess.ArgumentList);
            case ElementBindingExpressionSyntax elementBinding:
                return IndexOrRangeTargets(semanticModel, elementBinding, elementBinding.ArgumentList);
            default:
                return Array.Empty<(IMethodSymbol, string)>();
        }
    }

    private static IEnumerable<(IMethodSymbol, string)> AwaitTargets(AwaitExpressionInfo info, string construct)
    {
        yield return (info.GetAwaiterMethod, construct);
        yield return (info.IsCompletedProperty?.GetMethod, construct);
        yield return (info.GetResultMethod, construct);
    }

    // Why a speculative await: the compiler awaits the value DisposeAsync or MoveNextAsync returns
    // with no await in the source, and no semantic API reports that awaiter. Binding
    // `await default(R)` at the statement applies the compiler's own awaiter rules, extension
    // GetAwaiter included.
    private static IEnumerable<(IMethodSymbol, string)> AwaiterTargetsOf(
        SemanticModel semanticModel,
        int position,
        ITypeSymbol awaited,
        string construct)
    {
        if (awaited == null || awaited.SpecialType == SpecialType.System_Void)
        {
            return Array.Empty<(IMethodSymbol, string)>();
        }

        StatementSyntax statement = SyntaxFactory.ParseStatement(
            "await default(" + awaited.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ");");
        if (statement is not ExpressionStatementSyntax { Expression: AwaitExpressionSyntax awaitExpression }
            || !semanticModel.TryGetSpeculativeSemanticModel(position, statement, out SemanticModel speculative))
        {
            return Array.Empty<(IMethodSymbol, string)>();
        }

        return AwaitTargets(speculative.GetAwaitExpressionInfo(awaitExpression), construct);
    }

    private static IEnumerable<(IMethodSymbol, string)> ForEachTargets(
        SemanticModel semanticModel,
        CommonForEachStatementSyntax forEach)
    {
        ForEachStatementInfo info = semanticModel.GetForEachStatementInfo(forEach);
        yield return (info.GetEnumeratorMethod, ForEachConstruct);
        yield return (info.MoveNextMethod, ForEachConstruct);
        yield return (info.CurrentProperty?.GetMethod, ForEachConstruct);
        yield return (info.DisposeMethod, ForEachConstruct);
        if (info.IsAsynchronous)
        {
            foreach ((IMethodSymbol, string) target in AwaiterTargetsOf(
                         semanticModel, forEach.SpanStart, info.MoveNextMethod?.ReturnType, AwaitForEachConstruct))
            {
                yield return target;
            }

            foreach ((IMethodSymbol, string) target in AwaiterTargetsOf(
                         semanticModel, forEach.SpanStart, info.DisposeMethod?.ReturnType, AwaitForEachConstruct))
            {
                yield return target;
            }
        }

        if (forEach is ForEachVariableStatementSyntax forEachVariable)
        {
            foreach ((IMethodSymbol, string) target in DeconstructionTargets(
                         semanticModel.GetDeconstructionInfo(forEachVariable)))
            {
                yield return target;
            }
        }
    }

    // Why the left side decides: `(a, b) = x` and `var (a, b) = x` deconstruct, and a tuple
    // assignment whose right side is a tuple literal reports no Deconstruct method, which is fine.
    private static bool IsDeconstruction(AssignmentExpressionSyntax assignment)
    {
        return assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && assignment.Left is TupleExpressionSyntax or DeclarationExpressionSyntax;
    }

    private static IEnumerable<(IMethodSymbol, string)> DeconstructionTargets(DeconstructionInfo info)
    {
        yield return (info.Method, DeconstructionConstruct);
        foreach (DeconstructionInfo nested in info.Nested)
        {
            foreach ((IMethodSymbol, string) target in DeconstructionTargets(nested))
            {
                yield return target;
            }
        }
    }

    private static IEnumerable<(IMethodSymbol, string)> CollectionInitializerTargets(
        SemanticModel semanticModel,
        InitializerExpressionSyntax initializer)
    {
        foreach (ExpressionSyntax element in initializer.Expressions)
        {
            yield return (
                semanticModel.GetCollectionInitializerSymbolInfo(element).Symbol as IMethodSymbol,
                CollectionInitializerConstruct);
        }
    }

    // The handler conversion the expression goes through, whether it is an argument, a local's
    // initializer, or any other converted expression; null for an ordinary string or addition.
    // Why the creation must belong to this very node: the compiler attaches the conversion of
    // `$"a" + $"b"` to the addition, and each string inside it would otherwise report it again.
    internal static IInterpolatedStringHandlerCreationOperation FindHandlerCreationOrNull(
        SemanticModel semanticModel,
        ExpressionSyntax expression)
    {
        IOperation operation = semanticModel.GetOperation(expression);
        if (operation == null)
        {
            return null;
        }

        while (operation.Parent != null && operation.Parent.Syntax == expression)
        {
            operation = operation.Parent;
        }

        return operation.DescendantsAndSelf()
            .OfType<IInterpolatedStringHandlerCreationOperation>()
            .FirstOrDefault(creation => creation.Syntax == expression);
    }

    private static IEnumerable<(IMethodSymbol, string)> HandlerTargets(
        IInterpolatedStringHandlerCreationOperation creation)
    {
        if (creation == null)
        {
            yield break;
        }

        yield return (
            (creation.HandlerCreation as IObjectCreationOperation)?.Constructor,
            InterpolatedStringHandlerConstruct);
        foreach (IInterpolatedStringAppendOperation append in AppendOperations(creation.Content))
        {
            yield return (
                (append.AppendCall as IInvocationOperation)?.TargetMethod,
                InterpolatedStringHandlerConstruct);
        }
    }

    // Why only the content's own parts: a formatted value may hold another interpolated string,
    // whose handler calls belong to that string's node.
    internal static IEnumerable<IInterpolatedStringAppendOperation> AppendOperations(IOperation content)
    {
        switch (content)
        {
            case IInterpolatedStringOperation interpolated:
                return interpolated.Parts.OfType<IInterpolatedStringAppendOperation>();
            case IInterpolatedStringAdditionOperation addition:
                return AppendOperations(addition.Left).Concat(AppendOperations(addition.Right));
            default:
                return Array.Empty<IInterpolatedStringAppendOperation>();
        }
    }

    // `x[^1]` and `x[a..b]` on a type without an Index or Range indexer: the compiler reads
    // Length or Count, then calls the int indexer or Slice.
    private static IEnumerable<(IMethodSymbol, string)> IndexOrRangeTargets(
        SemanticModel semanticModel,
        ExpressionSyntax elementAccess,
        BracketedArgumentListSyntax arguments)
    {
        if (semanticModel.GetOperation(elementAccess) is not IImplicitIndexerReferenceOperation indexer)
        {
            yield break;
        }

        if (ReadsLength(semanticModel, arguments))
        {
            yield return ((indexer.LengthSymbol as IPropertySymbol)?.GetMethod, IndexOrRangeConstruct);
        }

        if (indexer.IndexerSymbol is IMethodSymbol slice)
        {
            yield return (slice, IndexOrRangeConstruct);
            yield break;
        }

        if (indexer.IndexerSymbol is not IPropertySymbol property)
        {
            yield break;
        }

        AssignmentExpressionSyntax assignment = AssignmentTargetRules.AssignmentTargetedBy(elementAccess);
        // Why the accessor a write calls: a ref-returning indexer has no setter, and a write goes
        // through the reference its getter returns.
        if (assignment != null)
        {
            yield return (AssignmentTargetRules.AccessorCalledByWrite(property), IndexOrRangeConstruct);
        }

        if (assignment == null || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            yield return (property.GetMethod, IndexOrRangeConstruct);
        }
    }

    // Why a range of int endpoints reads nothing: the compiler lowers `x[a..b]` to
    // `x.Slice(a, b - a)` and `x[..b]` to `x.Slice(0, b)` when no endpoint counts from the end,
    // while `^n`, an open end, and an Index or Range value all need Length to find their offsets.
    internal static bool ReadsLength(SemanticModel semanticModel, BracketedArgumentListSyntax arguments)
    {
        ExpressionSyntax argument = arguments.Arguments.Count == 1 ? arguments.Arguments[0].Expression : null;
        while (argument is ParenthesizedExpressionSyntax parenthesized)
        {
            argument = parenthesized.Expression;
        }

        if (argument is not RangeExpressionSyntax range || range.RightOperand == null)
        {
            return true;
        }

        return (range.LeftOperand != null && !IsInt(semanticModel, range.LeftOperand))
            || !IsInt(semanticModel, range.RightOperand);
    }

    private static bool IsInt(SemanticModel semanticModel, ExpressionSyntax operand)
    {
        return semanticModel.GetTypeInfo(operand).Type?.SpecialType == SpecialType.System_Int32;
    }

    private static IEnumerable<(IMethodSymbol, string)> QueryClauseTargets(QueryClauseInfo info)
    {
        yield return (info.CastInfo.Symbol as IMethodSymbol, QueryConstruct);
        yield return (info.OperationInfo.Symbol as IMethodSymbol, QueryConstruct);
    }

    // Why only these resource types: the compiler that builds the patch (the Unity installation's)
    // calls IDisposable.Dispose or IAsyncDisposable.DisposeAsync through the interface, which is
    // public, whenever the resource implements it, even when a pattern method also exists. A ref
    // struct without IDisposable may offer a pattern Dispose, and an await using on any type without
    // IAsyncDisposable a pattern DisposeAsync, whose result the compiler then awaits. No semantic API
    // reports which method was bound, so the call is bound speculatively, which applies the
    // compiler's own overload rules.
    private static IEnumerable<(IMethodSymbol, string)> UsingTargets(
        SemanticModel semanticModel,
        SyntaxNode statement,
        TypeSyntax declaredType,
        ExpressionSyntax resource,
        bool isAwaitUsing)
    {
        SyntaxNode typedNode = (SyntaxNode)declaredType ?? resource;
        ITypeSymbol resourceType = typedNode == null ? null : semanticModel.GetTypeInfo(typedNode).Type;
        if (resourceType == null)
        {
            return Array.Empty<(IMethodSymbol, string)>();
        }

        if (isAwaitUsing)
        {
            if (Implements(resourceType, "System.IAsyncDisposable"))
            {
                return Array.Empty<(IMethodSymbol, string)>();
            }

            IMethodSymbol disposeAsync = BindPatternMethodOrNull(semanticModel, typedNode.SpanStart, resourceType, "DisposeAsync");
            return Single(disposeAsync, AwaitUsingConstruct)
                .Concat(AwaiterTargetsOf(semanticModel, statement.SpanStart, disposeAsync?.ReturnType, AwaitUsingConstruct));
        }

        if (!resourceType.IsRefLikeType || Implements(resourceType, "System.IDisposable"))
        {
            return Array.Empty<(IMethodSymbol, string)>();
        }

        return Single(BindPatternMethodOrNull(semanticModel, typedNode.SpanStart, resourceType, "Dispose"), UsingConstruct);
    }

    private static bool Implements(ITypeSymbol type, string interfaceFullName)
    {
        return type.AllInterfaces.Any(candidate => candidate.ToDisplayString() == interfaceFullName);
    }

    // The instance method a call without arguments binds to at the using, as the compiler picks
    // the pattern method: inherited members, defaults, params, and overload preference included.
    // Why extension methods are dropped: the dispose pattern does not consider them.
    private static IMethodSymbol BindPatternMethodOrNull(
        SemanticModel semanticModel,
        int position,
        ITypeSymbol resourceType,
        string methodName)
    {
        ExpressionSyntax call = SyntaxFactory.ParseExpression(
            "default(" + resourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")." + methodName + "()");
        IMethodSymbol method = semanticModel
            .GetSpeculativeSymbolInfo(position, call, SpeculativeBindingOption.BindAsExpression)
            .Symbol as IMethodSymbol;
        return method == null || method.IsStatic || method.ReducedFrom != null ? null : method;
    }

    private static IEnumerable<(IMethodSymbol, string)> Single(ISymbol symbol, string construct)
    {
        return new[] { (symbol as IMethodSymbol, construct) };
    }

    // A property accessor is named by its property and a constructor by the creation, which is
    // what the source would show.
    internal static string DisplayName(IMethodSymbol method)
    {
        if (method.MethodKind == MethodKind.Constructor)
        {
            return "new " + method.ContainingType.Name + "(...)";
        }

        return method.AssociatedSymbol is IPropertySymbol property ? property.Name : method.Name;
    }
}
