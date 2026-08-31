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

internal static class ShimMethodEmitter
{
    internal static (int ShimTypeCounter, int GlobalShimMethodCounter) EmitQueuedMethodsAndPropertyGetters(
        List<TypeEmitState> typeEmitStates,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        CompilationUnitSyntax root,
        WorkerInput input,
        BaselineSnapshotState baseline,
        List<WorkerEntry> entries,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        List<ShimTypeBuilder> shimTypes,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        int shimTypeCounter,
        int globalShimMethodCounter)
    {
        foreach (TypeEmitState typeState in typeEmitStates)
        {
            EmitQueuedMethods(
                typeState,
                semanticModel,
                addedMethodCatalog,
                addedFieldCatalog,
                entries);
            (shimTypeCounter, globalShimMethodCounter) = PropertyGetterEmitter.EmitPropertyGettersForType(
                typeState,
                semanticModel,
                addedMethodCatalog,
                addedFieldCatalog,
                root,
                input,
                baseline,
                entries,
                skipped,
                unchangedMethods,
                shimTypes,
                assemblyGlobalUsings,
                shimTypeCounter,
                globalShimMethodCounter);
        }

        return (shimTypeCounter, globalShimMethodCounter);
    }

    internal static void EmitQueuedMethods(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerEntry> entries)
    {
        foreach (QueuedShimMethod queued in typeState.QueuedMethods)
        {
            AccessorPlan rewritePlan = queued.Decision.UsesDelegation
                ? queued.ShimType.AccessorPlan
                : null;
            MethodDeclarationSyntax rewrittenMethod = RewriteMethodBody(
                queued.MethodDeclaration,
                queued.MethodSymbol,
                typeState.TypeSymbol,
                semanticModel,
                rewritePlan,
                addedMethodCatalog,
                addedFieldCatalog);
            queued.ShimType.AddMethod(rewrittenMethod, queued.ShimMethodName);

            SyntaxNode bodyNode =
                (SyntaxNode)queued.MethodDeclaration.Body ?? queued.MethodDeclaration.ExpressionBody;
            string[] calledAddedMethodKeys = AddedCallSiteGuard.CollectCalledAddedMethodKeys(
                bodyNode,
                semanticModel,
                addedMethodCatalog,
                queued.MethodKey);

            entries.Add(new WorkerEntry
            {
                TypeMetadataName = CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
                MethodName = queued.MethodSymbol.Name,
                ParameterTypeFullNames = queued.ParameterTypeFullNames,
                GenericArity = queued.MethodSymbol.Arity,
                ShimTypeName = queued.ShimType.ShimTypeName,
                ShimMethodName = queued.ShimMethodName,
                PatchKind = queued.Decision.PatchKind,
                CalledAddedMethodKeys = calledAddedMethodKeys,
                SourceStartLine = queued.SourceStartLine,
                SourceEndLine = queued.SourceEndLine,
                LifecycleNote = ComputeLifecycleNote(
                    queued.MethodDeclaration,
                    queued.MethodSymbol,
                    typeState.TypeSymbol),
                ReplacesCompiledMethod = queued.ReplacesCompiledMethod
            });
        }
    }

    internal static MethodDeclarationSyntax RewriteMethodBody(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol targetType,
        SemanticModel semanticModel,
        AccessorPlan accessorPlan,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        // Why a single rewriter: rewriting the tree invalidates SemanticModel for new nodes.
        // Qualify + accessor rewrite both classify symbols on the original tree in one Visit pass.
        ShimBodyRewriter rewriter = new ShimBodyRewriter(
            semanticModel,
            targetType,
            accessorPlan,
            addedMethodCatalog,
            addedFieldCatalog);
        MethodDeclarationSyntax rewritten = (MethodDeclarationSyntax)rewriter.Visit(methodDeclaration);
        return ShimMethodFactory.ToShimMethod(rewritten, methodSymbol);
    }

    /// <summary>
    /// Attaches original-source 1-based line annotations to every method and statement in the
    /// parsed tree. Must run before compilation so the SemanticModel binds the annotated tree.
    /// </summary>
    // What: direct one-shot Unity lifecycle note only. Indirect "only called from Awake"
    // notes were dropped — syntax-only caller walks cannot prove that claim (ctors,
    // accessors, lambdas, other types in the same file).
    internal static string ComputeLifecycleNote(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol typeSymbol)
    {
        string methodName = methodDeclaration.Identifier.Text;
        if (!IsOneShotLifecycleMethodName(methodName))
        {
            return null;
        }

        if (!IsUnityEngineMonoBehaviourDerived(typeSymbol))
        {
            return null;
        }

        // Why private void (): Unity message methods are instance void with no parameters;
        // public/static/parameterized Start() on a MonoBehaviour is not the lifecycle hook.
        if (methodSymbol.DeclaredAccessibility != Accessibility.Private
            || methodSymbol.IsStatic
            || !methodSymbol.ReturnsVoid
            || methodSymbol.Parameters.Length != 0)
        {
            return null;
        }

        return string.Format(LifecycleNotes.DirectFormat, methodName);
    }

    internal static bool IsOneShotLifecycleMethodName(string methodName)
    {
        for (int index = 0; index < LifecycleNotes.OneShotLifecycleMethodNames.Length; index++)
        {
            if (LifecycleNotes.OneShotLifecycleMethodNames[index] == methodName)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsUnityEngineMonoBehaviourDerived(INamedTypeSymbol typeSymbol)
    {
        INamedTypeSymbol current = typeSymbol;
        while (current != null)
        {
            if (current.Name == "MonoBehaviour"
                && current.ContainingNamespace != null
                && current.ContainingNamespace.ToDisplayString() == "UnityEngine")
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }
}
