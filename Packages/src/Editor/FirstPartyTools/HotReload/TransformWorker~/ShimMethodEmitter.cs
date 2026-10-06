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
    internal static void EmitQueuedMethodsAndPropertyGetters(
        List<TypeEmitState> typeEmitStates,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        AddedPropertyCatalog addedPropertyCatalog,
        WorkerInput input,
        List<WorkerEntry> entries,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        List<ShimTypeBuilder> shimTypes,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        ShimNameAllocator shimNames)
    {
        foreach (TypeEmitState typeState in typeEmitStates)
        {
            EmitQueuedMethods(
                typeState,
                addedMethodCatalog,
                addedFieldCatalog,
                addedPropertyCatalog,
                entries);
            AddedPropertyEmitter.EmitAddedPropertyAccessors(
                typeState,
                addedPropertyCatalog,
                addedMethodCatalog,
                addedFieldCatalog,
                entries);
            PropertyGetterEmitter.EmitPropertyGettersForType(
                typeState,
                addedMethodCatalog,
                addedFieldCatalog,
                addedPropertyCatalog,
                input,
                entries,
                skipped,
                unchangedMethods,
                shimTypes,
                assemblyGlobalUsings,
                shimNames);
        }
    }

    internal static void EmitQueuedMethods(
        TypeEmitState typeState,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        AddedPropertyCatalog addedPropertyCatalog,
        List<WorkerEntry> entries)
    {
        SemanticModel semanticModel = typeState.SourceUnit.SemanticModel;
        foreach (QueuedShimMethod queued in typeState.QueuedMethods)
        {
            AccessorPlan rewritePlan = queued.Decision.UsesDelegation || queued.Decision.ClosureScopedAccessors
                ? queued.ShimType.AccessorPlan
                : null;
            MethodDeclarationSyntax rewrittenMethod = RewriteMethodBody(
                queued.MethodDeclaration,
                queued.MethodSymbol,
                typeState.TypeSymbol,
                semanticModel,
                rewritePlan,
                queued.Decision.ClosureScopedAccessors,
                addedMethodCatalog,
                addedFieldCatalog,
                addedPropertyCatalog);
            if (queued.IsAddedMethod)
            {
                queued.ShimType.AddAddedMemberMethod(rewrittenMethod, queued.ShimMethodName, queued.MethodSymbol);
            }
            else
            {
                queued.ShimType.AddMethod(rewrittenMethod, queued.ShimMethodName);
            }

            SyntaxNode bodyNode =
                (SyntaxNode)queued.MethodDeclaration.Body ?? queued.MethodDeclaration.ExpressionBody;
            string[] calledAddedMethodKeys = AddedCallSiteGuard.CollectCalledAddedMethodKeys(
                bodyNode,
                semanticModel,
                addedMethodCatalog,
                addedPropertyCatalog,
                queued.MethodKey);

            entries.Add(new WorkerEntry
            {
                SourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath,
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
                LifecycleNote = ComputeEntryLifecycleNote(queued, typeState.TypeSymbol),
                ReplacesCompiledMethod = queued.ReplacesCompiledMethod,
                HomeAssemblyName = typeState.HomeAssemblyName
            });
        }
    }

    internal static MethodDeclarationSyntax RewriteMethodBody(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol targetType,
        SemanticModel semanticModel,
        AccessorPlan accessorPlan,
        bool accessorsOnlyInClosures,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        AddedPropertyCatalog addedPropertyCatalog)
    {
        // Why a single rewriter: rewriting the tree invalidates SemanticModel for new nodes.
        // Qualify + accessor rewrite both classify symbols on the original tree in one Visit pass.
        ShimBodyRewriter rewriter = new ShimBodyRewriter(
            semanticModel,
            targetType,
            accessorPlan,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog,
            accessorsOnlyInClosures
                ? (SyntaxNode)methodDeclaration.Body ?? methodDeclaration.ExpressionBody
                : null);
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

        return string.Format(LifecycleNotes.SelectDirectFormat(methodName), methodName);
    }

    /// <summary>
    /// The note a queued method's entry carries: the one-shot lifecycle note when the method is
    /// one, else the Test Runner note when it is an added test method, else none.
    /// </summary>
    // Why the one-shot note first: the rows that carry it today keep it unchanged. Why the
    // added-test condition is the warning's: a row gets the note exactly when the run warns.
    internal static string ComputeEntryLifecycleNote(QueuedShimMethod queued, INamedTypeSymbol typeSymbol)
    {
        string oneShotNote = ComputeLifecycleNote(queued.MethodDeclaration, queued.MethodSymbol, typeSymbol);
        if (oneShotNote != null)
        {
            return oneShotNote;
        }

        if (queued.IsAddedMethod && TestAttributeNames.HasTestAttribute(queued.MethodDeclaration))
        {
            return TestAttributeNames.AddedTestMethodLifecycleNote;
        }

        return null;
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
