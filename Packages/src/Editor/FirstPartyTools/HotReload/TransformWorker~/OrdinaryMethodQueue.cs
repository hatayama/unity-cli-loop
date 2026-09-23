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

internal static class OrdinaryMethodQueue
{
    internal static void QueueOrdinaryMethod(
        MethodDeclarationSyntax methodDeclaration,
        TypeEmitState typeState,
        SemanticModel semanticModel,
        INamedTypeSymbol compiledType,
        WorkerInput input,
        bool hasBaseline,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedMethodCatalog addedMethodCatalog,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        List<string> declarationDriftWarnings,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures,
        ShimNameAllocator shimNames)
    {
        IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null)
        {
            return;
        }

        string[] parameterTypeFullNames = methodSymbol.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        string methodKey = WorkerMethodKeys.BuildMethodKey(
            CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
            methodSymbol.Name,
            parameterTypeFullNames,
            methodSymbol.Arity);
        (bool isAddedMethod, bool replacesCompiledMethod) = ClassifyOrdinaryMethodAddedState(
            methodDeclaration,
            compiledType,
            methodSymbol);
        if (TrySkipExcludedOrdinaryMethod(
            isAddedMethod,
            replacesCompiledMethod,
            methodKey,
            methodDeclaration,
            typeState,
            input,
            snapshotMethodMap,
            plainCurrentMethodMap,
            addedMethodCatalog))
        {
            return;
        }

        string syntaxMethodKey = WorkerSyntaxIndex.BuildSyntaxMethodKey(
            typeState.TypeMetadataNameFromSyntax,
            methodDeclaration);
        if (TrySkipInterfaceOrdinaryMethod(
            isAddedMethod,
            replacesCompiledMethod,
            hasBaseline,
            syntaxMethodKey,
            methodSymbol,
            typeState,
            snapshotMethodMap,
            plainCurrentMethodMap,
            addedMethodCatalog,
            skipped))
        {
            return;
        }

        if (UnchangedOrdinaryMethodRecorder.TryRecordRetainedUnchangedOrdinaryMethod(
            isAddedMethod,
            syntaxMethodKey,
            methodSymbol,
            typeState,
            parameterTypeFullNames,
            unchangedMethods))
        {
            return;
        }

        if (UnchangedOrdinaryMethodRecorder.TryRecordUnchangedOrdinaryMethod(
            isAddedMethod,
            hasBaseline,
            syntaxMethodKey,
            methodSymbol,
            typeState,
            parameterTypeFullNames,
            snapshotMethodMap,
            plainCurrentMethodMap,
            unchangedMethods))
        {
            return;
        }

        MethodTransformDecision decision = DecideOrdinaryMethodTransform(
            isAddedMethod,
            methodDeclaration,
            methodSymbol,
            typeState,
            semanticModel);
        if (decision.SkipReason != null)
        {
            skipped.Add(new WorkerSkipped
            {
                SourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath,
                Method = WorkerMethodKeys.FormatMethodLabel(methodSymbol),
                Reason = decision.SkipReason
            });
            if (isAddedMethod)
            {
                // Why strip skipped added declarations: otherwise drift warns about
                // fields/initializers for a method the skip reason already explained.
                RemovedMemberCollector.RecordHandledAddedMethodSyntaxKey(
                    addedMethodCatalog,
                    syntaxMethodKey,
                    replacesCompiledMethod,
                    snapshotMethodMap,
                    plainCurrentMethodMap);
            }

            return;
        }

        QueueDecidedOrdinaryMethod(
            methodDeclaration,
            methodSymbol,
            decision,
            isAddedMethod,
            replacesCompiledMethod,
            methodKey,
            syntaxMethodKey,
            parameterTypeFullNames,
            typeState,
            root,
            assemblyGlobalUsings,
            shimTypes,
            addedMethodCatalog,
            snapshotMethodMap,
            plainCurrentMethodMap,
            declarationDriftWarnings,
            removedMembers,
            removedMethodSignatures,
            shimNames);
    }

    internal static (bool IsAddedMethod, bool ReplacesCompiledMethod) ClassifyOrdinaryMethodAddedState(
        MethodDeclarationSyntax methodDeclaration,
        INamedTypeSymbol compiledType,
        IMethodSymbol methodSymbol)
    {
        // Why skip explicit-interface methods: compiled GetMembers(simpleName) does not
        // see them (metadata name is Interface.Method), so they would be misclassified as
        // Added and skip the unchanged/baseline path.
        if (methodDeclaration.ExplicitInterfaceSpecifier != null)
        {
            return (false, false);
        }

        CompiledMethodMatch compiledMatch = CompiledMemberMatcher.MatchCompiledOrdinaryMethod(compiledType, methodSymbol);
        return (
            compiledMatch != CompiledMethodMatch.Matched,
            compiledMatch == CompiledMethodMatch.ReturnTypeChanged);
    }

    internal static bool TrySkipExcludedOrdinaryMethod(
        bool isAddedMethod,
        bool replacesCompiledMethod,
        string methodKey,
        MethodDeclarationSyntax methodDeclaration,
        TypeEmitState typeState,
        WorkerInput input,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        AddedMethodCatalog addedMethodCatalog)
    {
        if (isAddedMethod)
        {
            addedMethodCatalog.MarkClassifiedAdded(methodKey);
            if (input.ExcludedAddedMethodKeys.Contains(methodKey))
            {
                RemovedMemberCollector.RecordHandledAddedMethodSyntaxKey(
                    addedMethodCatalog,
                    WorkerSyntaxIndex.BuildSyntaxMethodKey(typeState.TypeMetadataNameFromSyntax, methodDeclaration),
                    replacesCompiledMethod,
                    snapshotMethodMap,
                    plainCurrentMethodMap);
                return true;
            }

            return false;
        }

        return input.ExcludedMethodKeys.Contains(methodKey);
    }

    internal static bool TrySkipInterfaceOrdinaryMethod(
        bool isAddedMethod,
        bool replacesCompiledMethod,
        bool hasBaseline,
        string syntaxMethodKey,
        IMethodSymbol methodSymbol,
        TypeEmitState typeState,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        AddedMethodCatalog addedMethodCatalog,
        List<WorkerSkipped> skipped)
    {
        if (typeState.TypeSymbol.TypeKind != TypeKind.Interface)
        {
            return false;
        }

        if (!isAddedMethod && hasBaseline
            && snapshotMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax snapshotDecl)
            && plainCurrentMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax plainDecl)
            && SyntaxFactory.AreEquivalent(snapshotDecl, plainDecl, topLevel: false))
        {
            // Why not unchangedMethods: RevertUnchangedPatches Resolve/ReadAssembly is
            // wasted for members Harmony will never patch. Stay inert.
            return true;
        }

        skipped.Add(new WorkerSkipped
        {
            SourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath,
            Method = WorkerMethodKeys.FormatMethodLabel(methodSymbol),
            Reason = WorkerReason.Of(HotReloadWorkerReasonCode.AddedMethodInterfaceMember)
        });
        if (isAddedMethod)
        {
            RemovedMemberCollector.RecordHandledAddedMethodSyntaxKey(
                addedMethodCatalog,
                syntaxMethodKey,
                replacesCompiledMethod,
                snapshotMethodMap,
                plainCurrentMethodMap);
        }

        return true;
    }

    internal static MethodTransformDecision DecideOrdinaryMethodTransform(
        bool isAddedMethod,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        TypeEmitState typeState,
        SemanticModel semanticModel)
    {
        SyntaxNode methodBodyNode =
            (SyntaxNode)methodDeclaration.Body ?? methodDeclaration.ExpressionBody;
        WorkerReason addedSkip = isAddedMethod
            ? MethodTransformDecider.EvaluateAddedMethodSkipReason(methodSymbol, methodDeclaration)
            : null;
        MethodTransformDecision decision = addedSkip != null
            ? MethodTransformDecision.Skip(addedSkip)
            : MethodTransformDecider.DecideMethodTransform(
                typeState.TypeDeclaration,
                typeState.TypeSymbol,
                methodDeclaration,
                methodSymbol,
                methodBodyNode,
                semanticModel,
                typeState.CompiledType,
                typeState.AddedMemberAccess,
                typeState.AddedEvents);
        if (isAddedMethod && decision.SkipReason == null)
        {
            decision = MethodTransformDecider.DecideAddedMethodAccessors(
                methodSymbol,
                typeState.TypeSymbol,
                methodBodyNode,
                semanticModel,
                decision,
                typeState.AddedMemberAccess,
                typeState.SourceUnit.ArtifactMap,
                typeState.TargetAssembly);
        }

        return decision;
    }

    internal static void QueueDecidedOrdinaryMethod(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        MethodTransformDecision decision,
        bool isAddedMethod,
        bool replacesCompiledMethod,
        string methodKey,
        string syntaxMethodKey,
        string[] parameterTypeFullNames,
        TypeEmitState typeState,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedMethodCatalog addedMethodCatalog,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        List<string> declarationDriftWarnings,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures,
        ShimNameAllocator shimNames)
    {
        ShimTypeBuilder shimType = OrdinaryMethodShimTypes.EnsureShimType(
            typeState,
            root,
            assemblyGlobalUsings,
            shimTypes,
            shimNames);
        string shimMethodName = shimNames.NextShimMethodName(methodSymbol.Name);

        FileLinePositionSpan originalSpan = methodDeclaration.GetLocation().GetLineSpan();
        QueuedShimMethod queued = new QueuedShimMethod
        {
            MethodDeclaration = methodDeclaration,
            MethodSymbol = methodSymbol,
            Decision = decision,
            ShimMethodName = shimMethodName,
            ShimType = shimType,
            SourceStartLine = originalSpan.StartLinePosition.Line + 1,
            SourceEndLine = originalSpan.EndLinePosition.Line + 1,
            ParameterTypeFullNames = parameterTypeFullNames,
            MethodKey = methodKey,
            IsAddedMethod = isAddedMethod,
            ReplacesCompiledMethod = replacesCompiledMethod
        };
        typeState.QueuedMethods.Add(queued);

        if (replacesCompiledMethod)
        {
            RemovedMemberCollector.AddRemovedMethodName(removedMembers, methodSymbol.Name);
            RemovedMemberCollector.AddRemovedMethodSignature(
                removedMethodSignatures,
                typeState.TypeSymbol,
                methodSymbol.Name,
                parameterTypeFullNames,
                methodSymbol.Arity);
        }

        if (isAddedMethod)
        {
            addedMethodCatalog.Register(
                new AddedMethodBinding
                {
                    MethodKey = methodKey,
                    ShimTypeName = shimType.ShimTypeName,
                    ShimMethodName = shimMethodName,
                    NamespaceName = shimType.NamespaceName,
                    IsStatic = methodSymbol.IsStatic,
                    ParameterCount = methodSymbol.Parameters.Length
                });
            RemovedMemberCollector.RecordHandledAddedMethodSyntaxKey(
                addedMethodCatalog,
                syntaxMethodKey,
                replacesCompiledMethod,
                snapshotMethodMap,
                plainCurrentMethodMap);
            TestAttributeNames.AppendAddedTestMethodWarningIfNeeded(
                methodDeclaration,
                typeState.TypeSymbol,
                methodSymbol,
                declarationDriftWarnings);
        }
    }
}
