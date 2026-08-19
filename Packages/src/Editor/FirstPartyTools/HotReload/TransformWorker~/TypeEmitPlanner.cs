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

internal static class TypeEmitPlanner
{
    internal static (List<TypeEmitState> TypeEmitStates, int ShimTypeCounter, int GlobalShimMethodCounter)
        QueueAllTypeEmitStates(
            CompilationUnitSyntax root,
            SemanticModel semanticModel,
            IAssemblySymbol targetTypesAssemblySymbol,
            WorkerInput input,
            BaselineSnapshotState baseline,
            List<UsingDirectiveSyntax> assemblyGlobalUsings,
            List<ShimTypeBuilder> shimTypes,
            AddedMethodCatalog addedMethodCatalog,
            AddedFieldCatalog addedFieldCatalog,
            List<WorkerSkipped> skipped,
            List<WorkerUnchangedMethod> unchangedMethods,
            List<string> declarationDriftWarnings,
            List<WorkerRemovedMember> removedMembers,
            List<WorkerRemovedMethodSignature> removedMethodSignatures,
            int shimTypeCounter,
            int globalShimMethodCounter)
    {
        List<TypeEmitState> typeEmitStates = new List<TypeEmitState>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            INamedTypeSymbol typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration);
            if (typeSymbol == null)
            {
                continue;
            }

            string typeMetadataNameFromSyntax = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);

            // Property setters/init and all indexer accessors with bodies stay Skipped.
            // Property getters are patched below (not reported here).
            (Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
                Dictionary<string, IndexerDeclarationSyntax> snapshotIndexerMap,
                Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap,
                Dictionary<string, IndexerDeclarationSyntax> plainCurrentIndexerMap) =
                baseline.GetAccessorBaselineMaps();
            UnsupportedMemberSkipCollector.AppendExplicitAccessorSkips(
                typeDeclaration,
                typeMetadataNameFromSyntax,
                semanticModel,
                skipped,
                snapshotPropertyMap,
                snapshotIndexerMap,
                plainCurrentPropertyMap,
                plainCurrentIndexerMap,
                addedMethodCatalog);
            (Dictionary<string, ConstructorDeclarationSyntax> snapshotConstructorMap,
                Dictionary<string, MemberDeclarationSyntax> snapshotOperatorMap,
                Dictionary<string, EventDeclarationSyntax> snapshotEventMap,
                Dictionary<string, ConstructorDeclarationSyntax> plainCurrentConstructorMap,
                Dictionary<string, MemberDeclarationSyntax> plainCurrentOperatorMap,
                Dictionary<string, EventDeclarationSyntax> plainCurrentEventMap) =
                baseline.GetUnsupportedMemberBaselineMaps();
            UnsupportedMemberSkipCollector.AppendUnsupportedMemberKindSkips(
                typeDeclaration,
                typeMetadataNameFromSyntax,
                semanticModel,
                skipped,
                snapshotConstructorMap,
                snapshotOperatorMap,
                snapshotEventMap,
                plainCurrentConstructorMap,
                plainCurrentOperatorMap,
                plainCurrentEventMap);

            TypeEmitState typeState = new TypeEmitState
            {
                TypeDeclaration = typeDeclaration,
                TypeSymbol = typeSymbol,
                TypeMetadataNameFromSyntax = typeMetadataNameFromSyntax
            };
            (int nextShimTypeCounter, int nextGlobalShimMethodCounter) = QueueTypeMethods(
                typeState,
                semanticModel,
                targetTypesAssemblySymbol,
                input,
                baseline.HasBaseline,
                baseline.SnapshotMethodMap,
                baseline.PlainCurrentMethodMap,
                root,
                assemblyGlobalUsings,
                shimTypes,
                addedMethodCatalog,
                addedFieldCatalog,
                skipped,
                unchangedMethods,
                declarationDriftWarnings,
                removedMembers,
                removedMethodSignatures,
                shimTypeCounter,
                globalShimMethodCounter);
            shimTypeCounter = nextShimTypeCounter;
            globalShimMethodCounter = nextGlobalShimMethodCounter;
            typeEmitStates.Add(typeState);
        }

        return (typeEmitStates, shimTypeCounter, globalShimMethodCounter);
    }

    internal static (int ShimTypeCounter, int GlobalShimMethodCounter) QueueTypeMethods(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol,
        WorkerInput input,
        bool hasBaseline,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        List<string> declarationDriftWarnings,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures,
        int shimTypeCounter,
        int globalShimMethodCounter)
    {
        INamedTypeSymbol compiledType = TransformWorkerProgram.FindCompiledType(typeState.TypeSymbol, targetTypesAssemblySymbol);
        if (compiledType == null)
        {
            OrdinaryMethodQueue.SkipAllMethodsOnUncompiledType(typeState, semanticModel, skipped, addedMethodCatalog);
            return (shimTypeCounter, globalShimMethodCounter);
        }

        TransformWorkerProgram.ClassifyAddedFields(
            typeState,
            semanticModel,
            compiledType,
            targetTypesAssemblySymbol,
            addedFieldCatalog,
            declarationDriftWarnings);

        foreach (MethodDeclarationSyntax methodDeclaration in typeState.TypeDeclaration.Members
            .OfType<MethodDeclarationSyntax>())
        {
            (shimTypeCounter, globalShimMethodCounter) = OrdinaryMethodQueue.QueueOrdinaryMethod(
                methodDeclaration,
                typeState,
                semanticModel,
                compiledType,
                input,
                hasBaseline,
                snapshotMethodMap,
                plainCurrentMethodMap,
                root,
                assemblyGlobalUsings,
                shimTypes,
                addedMethodCatalog,
                skipped,
                unchangedMethods,
                declarationDriftWarnings,
                removedMembers,
                removedMethodSignatures,
                shimTypeCounter,
                globalShimMethodCounter);
        }

        return (shimTypeCounter, globalShimMethodCounter);
    }
}
