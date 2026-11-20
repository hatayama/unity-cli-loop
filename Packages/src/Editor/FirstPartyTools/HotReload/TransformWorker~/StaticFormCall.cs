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
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// What: finds where a body names a class only as the class of a static-form call to one of its
/// methods (Cls.M(x, a)). The accessor rewrite and the added-method rewrite replace such a call
/// whole, class name included, so the name reaches the shim only when neither rewrites the call.
/// </summary>
internal static class StaticFormCall
{
    /// <summary>
    /// What: the static-form call whose class expression holds <paramref name="typeName"/>, or null
    /// when the name is not the class of a call.
    /// </summary>
    internal static InvocationExpressionSyntax FindCallNamingTheClassOrNull(
        SemanticModel semanticModel,
        SyntaxNode typeName)
    {
        MemberAccessExpressionSyntax access = FindAccessWhoseReceiverHolds(typeName);
        if (access?.Parent is not InvocationExpressionSyntax invocation || invocation.Expression != access)
        {
            return null;
        }

        if (AccessibilityRules.BoundSymbolOrNull(semanticModel, invocation) is not IMethodSymbol method)
        {
            return null;
        }

        // Why the whole receiver must bind to the method's class: only then is every name in it part
        // of the class name the rewrite drops. A receiver-form or instance call has a value there,
        // which the rewrite keeps as the first argument.
        ISymbol receiver = AccessibilityRules.BoundSymbolOrNull(semanticModel, access.Expression);
        if (!SymbolEqualityComparer.Default.Equals(receiver, method.ContainingType))
        {
            return null;
        }

        return invocation;
    }

    // What: the member access whose receiver expression holds the name, climbing only through the
    // parts of a qualified name (Ns.Cls, global::Ns.Cls), or null when the name is not in one.
    private static MemberAccessExpressionSyntax FindAccessWhoseReceiverHolds(SyntaxNode typeName)
    {
        SyntaxNode child = typeName;
        for (SyntaxNode parent = typeName.Parent;
             parent is MemberAccessExpressionSyntax or QualifiedNameSyntax or AliasQualifiedNameSyntax;
             parent = parent.Parent)
        {
            if (parent is MemberAccessExpressionSyntax access && access.Expression == child)
            {
                return access;
            }

            child = parent;
        }

        return null;
    }
}
