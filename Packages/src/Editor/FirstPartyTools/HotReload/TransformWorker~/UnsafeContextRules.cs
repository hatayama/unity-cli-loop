using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Decides whether an edited member needs an unsafe context. Patches are compiled without unsafe
/// code, so such a member cannot be patched and is refused up front instead of failing the shim
/// compile for the whole file.
/// </summary>
internal static class UnsafeContextRules
{
    /// <summary>
    /// True when the member's signature or body uses pointers, function pointers, a fixed
    /// statement, an unsafe block or local function, or sizeof of a type that is not built in.
    /// The member's own and its containing type's unsafe modifiers are not reasons by themselves:
    /// the patch drops them, so a body that uses no pointers still compiles.
    /// </summary>
    internal static bool NeedsUnsafeContext(IMethodSymbol methodSymbol, SyntaxNode bodyNode, SemanticModel semanticModel)
    {
        if (HasPointerLikeSignature(methodSymbol))
        {
            return true;
        }

        if (bodyNode == null)
        {
            return false;
        }

        return bodyNode.DescendantNodesAndSelf().Any(node => NodeNeedsUnsafeContext(node, semanticModel));
    }

    /// <summary>
    /// True for a pointer or function pointer type, looking through arrays (an int*[] is as unsafe
    /// as int*).
    /// </summary>
    internal static bool IsPointerLike(ITypeSymbol type)
    {
        ITypeSymbol elementType = type;
        while (elementType is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
        }

        return elementType != null
            && (elementType.TypeKind == TypeKind.Pointer || elementType.TypeKind == TypeKind.FunctionPointer);
    }

    private static bool HasPointerLikeSignature(IMethodSymbol methodSymbol)
    {
        return IsPointerLike(methodSymbol.ReturnType)
            || methodSymbol.Parameters.Any(parameter => IsPointerLike(parameter.Type));
    }

    // Why match SyntaxKind rather than tokens: '&' and '*' are also bitwise-and and multiply, which
    // are safe; only the address-of and indirection node kinds need an unsafe context.
    private static bool NodeNeedsUnsafeContext(SyntaxNode node, SemanticModel semanticModel)
    {
        if (node is TypeSyntax type && IsTypeOfOperandOrTypeArgument(type))
        {
            return false;
        }

        switch (node.Kind())
        {
            case SyntaxKind.UnsafeStatement:
            case SyntaxKind.FixedStatement:
            case SyntaxKind.PointerType:
            case SyntaxKind.FunctionPointerType:
            case SyntaxKind.AddressOfExpression:
            case SyntaxKind.PointerIndirectionExpression:
            case SyntaxKind.PointerMemberAccessExpression:
                return true;
            case SyntaxKind.LocalFunctionStatement:
                return ((LocalFunctionStatementSyntax)node).Modifiers.Any(SyntaxKind.UnsafeKeyword);
            case SyntaxKind.SizeOfExpression:
                // Why the constant test: sizeof of a built-in type is a constant and safe, while
                // sizeof of any other type needs an unsafe context.
                return !semanticModel.GetConstantValue(node).HasValue;
        }

        if (CallsPointerLikeParameter(node, semanticModel))
        {
            return true;
        }

        return node is ExpressionSyntax expression && HasPointerLikeType(expression, semanticModel);
    }

    // Why the callee's parameters: an omitted optional pointer argument leaves no expression in the
    // body, yet the call still needs an unsafe context (CS0214 at the call site). A collection
    // initializer element calls Add implicitly, with no invocation node of its own.
    private static bool CallsPointerLikeParameter(SyntaxNode node, SemanticModel semanticModel)
    {
        ISymbol callee = null;
        if (node is InvocationExpressionSyntax || node is BaseObjectCreationExpressionSyntax)
        {
            callee = semanticModel.GetSymbolInfo(node).Symbol;
        }
        else if (node is ExpressionSyntax element && node.Parent.IsKind(SyntaxKind.CollectionInitializerExpression))
        {
            callee = semanticModel.GetCollectionInitializerSymbolInfo(element).Symbol;
        }

        return callee is IMethodSymbol method
            && method.Parameters.Any(parameter => IsPointerLike(parameter.Type));
    }

    // Why exempt: a pointer type that only names a type, as a typeof operand or a generic type
    // argument (typeof(int*), typeof(delegate*<int*, void>), List<int*[]>), compiles without an
    // unsafe context. The climb passes through function pointer parameters, which are not types.
    private static bool IsTypeOfOperandOrTypeArgument(TypeSyntax type)
    {
        SyntaxNode current = type;
        while (current.Parent is TypeSyntax
            || current.Parent is FunctionPointerParameterSyntax
            || current.Parent is FunctionPointerParameterListSyntax)
        {
            current = current.Parent;
        }

        return current.Parent is TypeArgumentListSyntax || current.Parent is TypeOfExpressionSyntax;
    }

    // Why the semantic test on top of the syntax kinds: a body can handle a pointer with no pointer
    // syntax at all, as in 'var p = GetPointer();' or 'Use(GetPointer())'.
    private static bool HasPointerLikeType(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        // Why the element type of a stackalloc is not judged on its own: it binds as a pointer even
        // when the stackalloc is converted to Span<T>; the stackalloc itself is judged below.
        if (expression.Parent is StackAllocArrayCreationExpressionSyntax)
        {
            return false;
        }

        TypeInfo typeInfo = semanticModel.GetTypeInfo(expression);
        // Why only the converted type for stackalloc: its natural type is a pointer even when it is
        // converted to Span<T>, which is safe.
        if (expression is StackAllocArrayCreationExpressionSyntax
            || expression is ImplicitStackAllocArrayCreationExpressionSyntax)
        {
            return IsPointerLike(typeInfo.ConvertedType);
        }

        return IsPointerLike(typeInfo.Type) || IsPointerLike(typeInfo.ConvertedType);
    }
}
