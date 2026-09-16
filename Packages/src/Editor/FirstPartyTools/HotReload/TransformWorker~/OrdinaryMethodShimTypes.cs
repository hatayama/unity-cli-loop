using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// The type-level steps the ordinary method queue takes before it can queue a single method:
// allocating the shim type that will host the bodies, refusing every method of a type the
// compiled assembly does not have yet, and warning about an added Unity message.
internal static class OrdinaryMethodShimTypes
{
    internal static ShimTypeBuilder EnsureShimType(
        TypeEmitState typeState,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        ShimNameAllocator shimNames)
    {
        if (typeState.CurrentShimType != null)
        {
            return typeState.CurrentShimType;
        }

        string shimTypeName = shimNames.NextShimTypeName(typeState.TypeSymbol.Name);
        string namespaceName = typeState.TypeSymbol.ContainingNamespace == null
            || typeState.TypeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeState.TypeSymbol.ContainingNamespace.ToDisplayString();
        typeState.CurrentShimType = new ShimTypeBuilder(
            shimTypeName,
            namespaceName,
            WorkerUsingCollector.CollectUsingsForType(root, typeState.TypeDeclaration, assemblyGlobalUsings),
            typeState.SourceUnit.Input.ProjectRelativePath);
        shimTypes.Add(typeState.CurrentShimType);
        return typeState.CurrentShimType;
    }

    internal static void SkipAllMethodsOnUncompiledType(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped,
        AddedMethodCatalog addedMethodCatalog)
    {
        typeState.TypeIsAbsentFromCompiledAssembly = true;
        addedMethodCatalog.AddAddedTypeSyntaxKey(typeState.TypeMetadataNameFromSyntax);
        foreach (MethodDeclarationSyntax methodDeclaration in typeState.TypeDeclaration.Members
            .OfType<MethodDeclarationSyntax>())
        {
            IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);
            if (methodSymbol == null)
            {
                continue;
            }

            skipped.Add(new WorkerSkipped
            {
                SourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath,
                Method = WorkerMethodKeys.FormatMethodLabel(methodSymbol),
                Reason = WorkerReason.Of(HotReloadWorkerReasonCode.AddedMethodTypeNotIntroduced)
            });
        }
    }

    internal static void AppendUnityMessageWarningIfNeeded(
        INamedTypeSymbol typeSymbol,
        IMethodSymbol methodSymbol,
        List<string> declarationDriftWarnings)
    {
        if (!ShimMethodEmitter.IsUnityEngineMonoBehaviourDerived(typeSymbol)
            || !UnityMessageNames.Contains(methodSymbol.Name))
        {
            return;
        }

        declarationDriftWarnings.Add(
            string.Format(
                CultureInfo.InvariantCulture,
                UnityMessageNames.AddedMessageWarningFormat,
                methodSymbol.Name,
                typeSymbol.ToDisplayString()));
    }
}
