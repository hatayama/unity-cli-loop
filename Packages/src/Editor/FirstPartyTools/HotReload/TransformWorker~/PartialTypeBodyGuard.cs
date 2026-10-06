using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// Skips a body on a partial type that names something no visible part declares, instead of
/// letting it fail the whole file in the shim compile: a part generated at compile time is
/// invisible to the worker, so the name may be perfectly valid.
/// </summary>
internal static class PartialTypeBodyGuard
{
    // Why only these: other binding errors are expected (a compiled API still expects the
    // compiled copy of a type the run declares from source) and the shim compile settles
    // them. An unresolved name is what a missing part looks like, and it cannot compile
    // in the shim either.
    private static readonly HashSet<string> UnresolvedNameDiagnosticIds =
        new HashSet<string>(StringComparer.Ordinal) { "CS0103", "CS1061", "CS0117", "CS0246" };

    /// <summary>The skip reason for the body, or null when it names nothing unresolved or the type is not partial.</summary>
    internal static WorkerReason DescribeSkipOrNull(
        TypeDeclarationSyntax typeDeclaration,
        SemanticModel semanticModel,
        SyntaxNode bodyNode)
    {
        if (bodyNode == null || !PartialTypeParts.IsPartialOrNestedInPartial(typeDeclaration))
        {
            return null;
        }

        foreach (Diagnostic diagnostic in semanticModel.GetDiagnostics(bodyNode.Span))
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
            {
                continue;
            }

            if (!UnresolvedNameDiagnosticIds.Contains(diagnostic.Id))
            {
                continue;
            }

            return WorkerReason.Of(
                HotReloadWorkerReasonCode.MethodTransformPartialBodyUnbound,
                diagnostic.Id + ": " + diagnostic.GetMessage(CultureInfo.InvariantCulture));
        }

        return null;
    }
}
