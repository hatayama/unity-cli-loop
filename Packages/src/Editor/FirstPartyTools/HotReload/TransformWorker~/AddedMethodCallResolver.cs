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
using Microsoft.CodeAnalysis.Text;

// Resolves an invocation to an added-method catalog binding for rewrite and call-site collection.
internal static class AddedMethodCallResolver
{
    // Why a fallback: added methods exist only on edited sources. A receiver reached through an
    // unedited compiled type binds to the metadata type, so GetSymbolInfo stays unbound.
    internal static AddedMethodBinding ResolveBindingOrNull(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        AddedMethodCatalog catalog,
        out bool isStaticCall)
    {
        isStaticCall = false;
        Debug.Assert(invocation != null, "invocation");
        Debug.Assert(semanticModel != null, "semanticModel");
        Debug.Assert(catalog != null, "catalog");

        // Why resolved: a call passing a BCL type a referenced assembly also declares internally
        // (Memory<int>, ReadOnlySpan<int>) binds to nothing with accessibility ignored, and the
        // call to the added method would reach the shim unrewritten.
        IMethodSymbol symbol = AccessibilityRules.ResolvedSymbolOrNull(semanticModel, invocation) as IMethodSymbol;
        if (symbol != null)
        {
            IMethodSymbol staticForm = StaticFormOf(symbol);
            if (staticForm.MethodKind != MethodKind.Ordinary)
            {
                return null;
            }

            AddedMethodBinding bound = catalog.FindOrNull(WorkerMethodKeys.BuildMethodKeyFromSymbol(staticForm));
            // Why not static for a receiver-form call: the receiver is the extension method's first
            // argument, which the static form spells out and the receiver form does not.
            isStaticCall = staticForm.IsStatic && symbol.MethodKind != MethodKind.ReducedExtension;
            return bound;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        ITypeSymbol receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        if (receiverType == null)
        {
            receiverType = semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol as INamedTypeSymbol;
        }

        if (receiverType is not INamedTypeSymbol named || named.TypeKind == TypeKind.Error)
        {
            return null;
        }

        AddedMethodBinding binding = catalog.FindUniqueByReceiverOrNull(
            CecilTypeNames.ToMetadataName(named),
            memberAccess.Name.Identifier.ValueText,
            invocation.ArgumentList.Arguments.Count);
        if (binding == null)
        {
            return null;
        }

        // Why match staticness: a value receiver plus a static added method is CS0176;
        // rewriting it would drop receiver evaluation. A type-name receiver plus an
        // instance method would pass the type name as the instance argument.
        bool receiverIsTypeName =
            semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol is ITypeSymbol;
        if (binding.IsStatic != receiverIsTypeName)
        {
            return null;
        }

        isStaticCall = binding.IsStatic;
        return binding;
    }

    /// <summary>
    /// The method a call written on a receiver ('value.Extension(a)') makes, in the form the same call
    /// written in the static form ('Extensions.Extension(value, a)') binds to, type arguments included;
    /// any other method as it is.
    /// </summary>
    internal static IMethodSymbol StaticFormOf(IMethodSymbol method)
    {
        if (method.MethodKind != MethodKind.ReducedExtension)
        {
            return method;
        }

        return method.GetConstructedReducedFrom() ?? method.ReducedFrom;
    }
}
