using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// Skips an existing method of a file the run pulled in to re-bind its active patches when that
/// method's body no longer binds, and names the file the reader can pass so it binds again.
/// </summary>
/// <remarks>
/// Why only such files: the binding guard runs for added methods alone, so an existing body
/// that used a member only an earlier reload's file declared reaches the shim unbound and fails
/// the whole file with a compile error. The reader never passed this file, so a Failed row for
/// it reports an error in code they did not touch. A file the reader passed keeps its Failed
/// rows, because the error there is in the edit they just made.
/// </remarks>
internal static class ReappliedSiblingBodyGuard
{
    /// <summary>The skip reason for the body, or null when it binds.</summary>
    internal static WorkerReason DescribeSkipOrNull(
        SemanticModel semanticModel,
        SyntaxNode methodBodyNode,
        IAssemblySymbol targetAssembly)
    {
        Diagnostic bindingError = AddedMemberBindingGuard.FindFirstBindingError(semanticModel, methodBodyNode);
        if (bindingError == null)
        {
            return null;
        }

        string diagnosticText = bindingError.Id + ": " + bindingError.GetMessage(CultureInfo.InvariantCulture);
        INamedTypeSymbol receiver = FindCompiledReceiverOfUnboundMember(semanticModel, methodBodyNode, targetAssembly);
        if (receiver == null)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformSiblingBodyUnbound, diagnosticText);
        }

        return WorkerReason.NamingCompiledTypes(
            HotReloadWorkerReasonCode.MethodTransformSiblingBodyBindsCompiledType,
            new[] { CecilTypeNames.ToMetadataName(receiver.OriginalDefinition) },
            diagnosticText,
            "'" + receiver.Name + "'");
    }

    // Why not CompiledSignatureSplitCollector: it names a compiled API whose signature still takes
    // the compiled copy of a type this run declares, while here the member itself is missing from
    // the compiled type, so there is no split for it to find.
    // Why only a receiver of the target assembly that no run file declares: a type the run declares
    // is already in source, so no file is left to pass, and a type of another assembly cannot be
    // supplied through --files of this assembly's reload.
    private static INamedTypeSymbol FindCompiledReceiverOfUnboundMember(
        SemanticModel semanticModel,
        SyntaxNode methodBodyNode,
        IAssemblySymbol targetAssembly)
    {
        foreach (MemberAccessExpressionSyntax access in methodBodyNode.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (semanticModel.GetSymbolInfo(access.Name).Symbol != null)
            {
                continue;
            }

            if (!(semanticModel.GetTypeInfo(access.Expression).Type is INamedTypeSymbol receiver))
            {
                continue;
            }

            if (receiver.DeclaringSyntaxReferences.Length > 0)
            {
                continue;
            }

            // Why identities and not symbols: the target assembly symbol comes from the home
            // compilation, while the receiver is read through this body's compilation, so the same
            // assembly arrives as two different symbols.
            if (targetAssembly != null && receiver.ContainingAssembly != null
                && receiver.ContainingAssembly.Identity.Equals(targetAssembly.Identity))
            {
                return receiver;
            }
        }

        return null;
    }
}
