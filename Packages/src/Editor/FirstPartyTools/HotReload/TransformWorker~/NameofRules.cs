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

internal static class NameofRules
{
    // Why text-only: nameof is a language keyword with a null symbol; a user-defined method
    // literally named "nameof" would also match, but that pathological case is ignored in practice.
    public static bool IsNameofInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is IdentifierNameSyntax identifier
            && identifier.Identifier.ValueText == "nameof";
    }

    public static bool IsInsideNameofArgument(SyntaxNode node)
    {
        for (SyntaxNode current = node; current != null; current = current.Parent)
        {
            if (current is ArgumentSyntax
                && current.Parent is ArgumentListSyntax argumentList
                && argumentList.Parent is InvocationExpressionSyntax invocation
                && IsNameofInvocation(invocation))
            {
                return true;
            }
        }

        return false;
    }
}
