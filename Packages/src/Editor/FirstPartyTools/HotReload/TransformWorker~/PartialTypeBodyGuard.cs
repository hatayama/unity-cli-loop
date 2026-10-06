using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// Skips a body on a partial type that names something no visible part declares, instead of
/// letting it fail the whole file in the shim compile: a part generated at compile time is
/// invisible to the worker, so the name may be perfectly valid. An internal member of a compiled
/// type the run was not given looks just as missing to the worker; such a use goes through when
/// the patched method runs it itself, and is skipped with a reason that says why otherwise.
/// </summary>
internal static class PartialTypeBodyGuard
{
    // Why only these: other binding errors are expected (a compiled API still expects the
    // compiled copy of a type the run declares from source) and the shim compile settles
    // them. An unresolved name is what a missing part looks like, and it cannot compile in the
    // shim either, except for an internal member of a compiled type of the target assembly,
    // which the shim compile sees.
    private static readonly HashSet<string> UnresolvedNameDiagnosticIds =
        new HashSet<string>(StringComparer.Ordinal) { "CS0103", "CS1061", "CS0117", "CS0246" };

    /// <summary>
    /// The skip reason for the body, or null when the type is not partial, or when the body names
    /// nothing unresolved apart from internal members the patched method can reach.
    /// </summary>
    internal static WorkerReason DescribeSkipOrNull(
        TypeDeclarationSyntax typeDeclaration,
        SemanticModel semanticModel,
        SyntaxNode bodyNode,
        MethodDeclarationSyntax methodDeclarationOrNull,
        MethodTransformDecision decision,
        INamedTypeSymbol typeSymbol,
        IAssemblySymbol targetAssembly)
    {
        if (bodyNode == null || !PartialTypeParts.IsPartialOrNestedInPartial(typeDeclaration))
        {
            return null;
        }

        // Why every error and not the first: an internal member the patched method can reach says
        // nothing about the next error, which may be a name nothing declares.
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

            string diagnosticText = diagnostic.Id + ": " + diagnostic.GetMessage(CultureInfo.InvariantCulture);
            UnpassedInternalMemberUse use = UnpassedInternalMemberUse.FindOrNull(
                diagnostic,
                semanticModel,
                bodyNode,
                methodDeclarationOrNull,
                decision,
                typeSymbol,
                targetAssembly);
            if (use == null)
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformPartialBodyUnbound, diagnosticText);
            }

            if (use.CanBePatchedInPlace)
            {
                continue;
            }

            return WorkerReason.Of(
                HotReloadWorkerReasonCode.MethodTransformUnpassedInternalMemberOutOfReach,
                diagnosticText,
                "'" + use.DeclaringType.Name + "'");
        }

        return null;
    }
}
