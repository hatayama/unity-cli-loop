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

/// <summary>
/// What: rewrites a field-like event's raise, read, and clear into calls on the Harmony
/// backing-field accessor. C# only allows += / -= on an event outside its declaring type, so a
/// shim that kept the event member would not compile.
/// </summary>
internal static class EventAccessorShimRewrite
{
    /// <summary>
    /// What: an event read as a value — '((TDelegate)__EV_E(instance))'. The cast collapses the
    /// accessor's ref return to a single read, so 'E?.Invoke()' keeps its read-once semantics
    /// instead of reading the field again for the call.
    /// </summary>
    internal static ExpressionSyntax CreateEventRead(AccessorEntry entry, ExpressionSyntax visitedReceiver)
    {
        ExpressionSyntax accessorCall = CreateAccessorCall(entry, visitedReceiver);
        TypeSyntax delegateType = SyntaxFactory.ParseTypeName(
            entry.EventSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        return SyntaxFactory.ParenthesizedExpression(
            SyntaxFactory.CastExpression(delegateType, accessorCall));
    }

    /// <summary>
    /// What: 'E = handler' as a write through the accessor's ref return, mirroring the
    /// inaccessible-field assignment shape.
    /// </summary>
    internal static ExpressionSyntax CreateEventWriteTarget(
        AccessorEntry entry,
        ExpressionSyntax visitedReceiver)
    {
        return CreateAccessorCall(entry, visitedReceiver);
    }

    private static ExpressionSyntax CreateAccessorCall(AccessorEntry entry, ExpressionSyntax visitedReceiver)
    {
        if (entry.EventSymbol.IsStatic)
        {
            return HarmonyAccessorShimRewrite.CreateDelegateInvocation(
                entry.DelegateFieldName,
                Array.Empty<ExpressionSyntax>());
        }

        return HarmonyAccessorShimRewrite.CreateDelegateInvocation(
            entry.DelegateFieldName,
            new[] { visitedReceiver });
    }
}
