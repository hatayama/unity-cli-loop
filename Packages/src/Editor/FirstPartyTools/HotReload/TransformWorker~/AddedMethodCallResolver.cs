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

        IMethodSymbol symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol != null)
        {
            if (symbol.MethodKind != MethodKind.Ordinary)
            {
                return null;
            }

            AddedMethodBinding bound = catalog.FindOrNull(WorkerMethodKeys.BuildMethodKeyFromSymbol(symbol));
            isStaticCall = symbol.IsStatic;
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
        isStaticCall = binding != null && binding.IsStatic;
        return binding;
    }
}
