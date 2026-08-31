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

internal static class WorkerMethodKeys
{
    // Keep in sync with HotReloadWireMethodKeys.BuildMethodKey (Unity package side)
    // and HotReloadCallSiteScanner.CreateHit.
    // Why arity suffix: Caller(int) and Caller<T>(int) must not share a wire key.
    // Arity 0 keeps the bare name so existing non-generic keys stay stable.
    internal static string BuildMethodKey(
        string typeMetadataName,
        string methodName,
        string[] parameterTypeFullNames,
        int genericArity)
    {
        string nameWithArity = methodName;
        if (genericArity > 0)
        {
            nameWithArity = methodName + "`" + genericArity.ToString(CultureInfo.InvariantCulture);
        }

        return typeMetadataName + "::" + nameWithArity + "("
            + string.Join(",", parameterTypeFullNames ?? Array.Empty<string>()) + ")";
    }

    internal static string BuildMethodKeyFromSymbol(IMethodSymbol methodSymbol)
    {
        string[] parameterTypeFullNames = methodSymbol.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        return BuildMethodKey(
            CecilTypeNames.ToMetadataName(methodSymbol.ContainingType),
            methodSymbol.Name,
            parameterTypeFullNames,
            methodSymbol.Arity);
    }

    // Keep in sync with HotReloadPatcher.FormatMethodKeyParts.
    // Why FormatMethodKeyParts shape: Methods[].Method must use one label for every Kind.
    // Roslyn FullyQualifiedFormat (global::, type arguments) was the Skipped-only outlier.
    internal static string FormatMethodLabel(IMethodSymbol methodSymbol)
    {
        string typeMetadataName =
            CecilTypeNames.ToMetadataName(methodSymbol.ContainingType).Replace('/', '+');
        string[] parameterTypeFullNames = methodSymbol.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        StringBuilder builder = new StringBuilder();
        builder.Append(typeMetadataName);
        builder.Append('.');
        builder.Append(methodSymbol.Name);
        if (methodSymbol.Arity > 0)
        {
            builder.Append('`');
            builder.Append(methodSymbol.Arity.ToString(CultureInfo.InvariantCulture));
        }

        builder.Append('(');
        for (int index = 0; index < parameterTypeFullNames.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(parameterTypeFullNames[index].Replace('/', '+'));
        }

        builder.Append(')');
        return builder.ToString();
    }
}
