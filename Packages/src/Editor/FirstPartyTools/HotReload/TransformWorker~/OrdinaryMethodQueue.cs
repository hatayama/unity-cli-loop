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

internal static class OrdinaryMethodQueue
{
    internal static (int ShimTypeCounter, int GlobalShimMethodCounter) QueueOrdinaryMethod(
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
        int shimTypeCounter,
        int globalShimMethodCounter)
    {
        IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null)
        {
            return (shimTypeCounter, globalShimMethodCounter);
        }

        string[] parameterTypeFullNames = methodSymbol.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        string methodKey = TransformWorkerProgram.BuildMethodKey(
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
            return (shimTypeCounter, globalShimMethodCounter);
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
            return (shimTypeCounter, globalShimMethodCounter);
        }

        if (TryRecordUnchangedOrdinaryMethod(
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
            return (shimTypeCounter, globalShimMethodCounter);
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
                Method = TransformWorkerProgram.FormatMethodLabel(methodSymbol),
                Reason = decision.SkipReason
            });
            if (isAddedMethod)
            {
                // Why strip skipped added declarations: otherwise drift warns about
                // fields/initializers for a method the skip reason already explained.
                TransformWorkerProgram.RecordHandledAddedMethodSyntaxKey(
                    addedMethodCatalog,
                    syntaxMethodKey,
                    replacesCompiledMethod,
                    snapshotMethodMap,
                    plainCurrentMethodMap);
            }

            return (shimTypeCounter, globalShimMethodCounter);
        }

        return QueueDecidedOrdinaryMethod(
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
            shimTypeCounter,
            globalShimMethodCounter);
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

        CompiledMethodMatch compiledMatch = TransformWorkerProgram.MatchCompiledOrdinaryMethod(compiledType, methodSymbol);
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
                TransformWorkerProgram.RecordHandledAddedMethodSyntaxKey(
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
            Method = TransformWorkerProgram.FormatMethodLabel(methodSymbol),
            Reason = AddedMethodSkipReasons.InterfaceMember
        });
        if (isAddedMethod)
        {
            TransformWorkerProgram.RecordHandledAddedMethodSyntaxKey(
                addedMethodCatalog,
                syntaxMethodKey,
                replacesCompiledMethod,
                snapshotMethodMap,
                plainCurrentMethodMap);
        }

        return true;
    }

    internal static bool TryRecordUnchangedOrdinaryMethod(
        bool isAddedMethod,
        bool hasBaseline,
        string syntaxMethodKey,
        IMethodSymbol methodSymbol,
        TypeEmitState typeState,
        string[] parameterTypeFullNames,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        List<WorkerUnchangedMethod> unchangedMethods)
    {
        if (isAddedMethod || !hasBaseline)
        {
            return false;
        }

        // Why plainDecl: compare unannotated nodes; annotated methodDeclaration breaks
        // AreEquivalent for long-return / unchecked / switch shapes (see plainRoot).
        if (snapshotMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax snapshotDecl)
            && plainCurrentMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax plainDecl)
            && SyntaxFactory.AreEquivalent(snapshotDecl, plainDecl, topLevel: false))
        {
            unchangedMethods.Add(new WorkerUnchangedMethod
            {
                TypeMetadataName = CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
                MethodName = methodSymbol.Name,
                ParameterTypeFullNames = parameterTypeFullNames,
                GenericArity = methodSymbol.Arity
            });
            return true;
        }

        return false;
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
        string addedSkip = isAddedMethod
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
                semanticModel);
        if (isAddedMethod && decision.SkipReason == null)
        {
            decision = MethodTransformDecider.DecideAddedMethodAccessors(
                methodSymbol,
                typeState.TypeSymbol,
                methodBodyNode,
                semanticModel,
                decision);
        }

        return decision;
    }

    internal static (int ShimTypeCounter, int GlobalShimMethodCounter) QueueDecidedOrdinaryMethod(
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
        int shimTypeCounter,
        int globalShimMethodCounter)
    {
        ShimTypeBuilder shimType;
        (shimType, shimTypeCounter) = EnsureShimType(
            typeState,
            root,
            assemblyGlobalUsings,
            shimTypes,
            shimTypeCounter);
        string shimMethodName = methodSymbol.Name + "__shim" + globalShimMethodCounter;
        globalShimMethodCounter++;

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
            TransformWorkerProgram.AddRemovedMethodName(removedMembers, methodSymbol.Name);
            TransformWorkerProgram.AddRemovedMethodSignature(
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
                    IsStatic = methodSymbol.IsStatic
                });
            TransformWorkerProgram.RecordHandledAddedMethodSyntaxKey(
                addedMethodCatalog,
                syntaxMethodKey,
                replacesCompiledMethod,
                snapshotMethodMap,
                plainCurrentMethodMap);
            AppendUnityMessageWarningIfNeeded(
                typeState.TypeSymbol,
                methodSymbol,
                declarationDriftWarnings);
        }

        return (shimTypeCounter, globalShimMethodCounter);
    }

    internal static (ShimTypeBuilder ShimType, int ShimTypeCounter) EnsureShimType(
        TypeEmitState typeState,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        int shimTypeCounter)
    {
        if (typeState.CurrentShimType != null)
        {
            return (typeState.CurrentShimType, shimTypeCounter);
        }

        string shimTypeName = typeState.TypeSymbol.Name + "_UloopHotReloadShims_" + shimTypeCounter;
        shimTypeCounter++;
        string namespaceName = typeState.TypeSymbol.ContainingNamespace == null
            || typeState.TypeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeState.TypeSymbol.ContainingNamespace.ToDisplayString();
        typeState.CurrentShimType = new ShimTypeBuilder(
            shimTypeName,
            namespaceName,
            TransformWorkerProgram.CollectUsingsForType(root, typeState.TypeDeclaration, assemblyGlobalUsings));
        shimTypes.Add(typeState.CurrentShimType);
        return (typeState.CurrentShimType, shimTypeCounter);
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
                Method = TransformWorkerProgram.FormatMethodLabel(methodSymbol),
                Reason = AddedMethodSkipReasons.NewTypeOutOfScope
            });
        }
    }

    internal static void AppendUnityMessageWarningIfNeeded(
        INamedTypeSymbol typeSymbol,
        IMethodSymbol methodSymbol,
        List<string> declarationDriftWarnings)
    {
        if (!TransformWorkerProgram.IsUnityEngineMonoBehaviourDerived(typeSymbol)
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
