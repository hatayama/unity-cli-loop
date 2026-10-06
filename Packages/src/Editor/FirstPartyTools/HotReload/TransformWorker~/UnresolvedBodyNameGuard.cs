using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// Skips an edited body that names something the worker cannot resolve, where emitting it would
/// break. On a partial type, a name no visible part declares is skipped instead of failing the
/// whole file in the shim compile: a part generated at compile time is invisible to the worker,
/// so the name may be perfectly valid. An internal member of a compiled type the run was not given
/// looks just as missing to the worker, on a partial or a plain type; a use of it the patched
/// method cannot reach is skipped with a reason that says why.
/// </summary>
internal static class UnresolvedBodyNameGuard
{
    // Why only these: other binding errors are expected (a compiled API still expects the
    // compiled copy of a type the run declares from source) and the shim compile settles
    // them. An unresolved name is what a missing part looks like, and it cannot compile in the
    // shim either, except for an internal member of a compiled type of the target assembly,
    // which the shim compile sees.
    private static readonly HashSet<string> UnresolvedNameDiagnosticIds =
        new HashSet<string>(StringComparer.Ordinal) { "CS0103", "CS1061", "CS0117", "CS0246" };

    /// <summary>
    /// The skip reason for the body, or null when every name in it the worker cannot resolve is an
    /// internal member the patched method can reach, or, on a plain type, is not such a member at
    /// all, which the shim compile settles as before.
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
        if (bodyNode == null)
        {
            return null;
        }

        bool isPartial = PartialTypeParts.IsPartialOrNestedInPartial(typeDeclaration);

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
                if (isPartial)
                {
                    return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformPartialBodyUnbound, diagnosticText);
                }

                // Why a plain type goes on: some of these names bind in the shim compile, such as an
                // internal member of another assembly that grants access through InternalsVisibleTo,
                // and such a body is patched and runs today. The shim compile reports the rest.
                continue;
            }

            if (IsWithinReach(use, isPartial))
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

    // Why the two rules differ: a partial type skips a body whose names do not resolve, and lets
    // through only the uses a run has shown to work in place. A plain type emits such a body, so
    // only the uses known to break are closed there: one that runs outside the patched method,
    // where the runtime checks access and the call throws, and a bare name, which reaches the shim
    // compile unqualified and fails the whole file. A method passed as a delegate, an event, and a
    // member named in an object initializer or a property pattern stay patched on a plain type,
    // as they were.
    private static bool IsWithinReach(UnpassedInternalMemberUse use, bool isPartial)
    {
        if (isPartial)
        {
            return use.CanBePatchedInPlace;
        }

        return !use.MayRunOutsideThePatchedMethod && !use.IsSimpleNameLookup;
    }
}
